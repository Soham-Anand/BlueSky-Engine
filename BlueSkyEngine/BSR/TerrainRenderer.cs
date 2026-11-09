using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;
using BlueSky.BSR.RHI;
using BSFrustum = BlueSky.Ramanujan.Frustum;
using BSAABB = BlueSky.Ramanujan.AABB;
using BSMatrix4x4 = BlueSky.Ramanujan.Matrix4x4;
using BSVector3 = BlueSky.Ramanujan.Vector3;

namespace BlueSky.BSR;

/// <summary>
/// Multi-terrain, chunked & frustum-culled multi-LOD terrain renderer.
/// Packs all chunk LOD index slices into a single unified GPU IndexBuffer per terrain
/// and coalesces contiguous visible slices into single DrawIndexed calls.
/// </summary>
public sealed class TerrainRenderer : IDisposable
{
    private readonly IRHIDevice _device;

    private sealed class TerrainGpuEntry : IDisposable
    {
        public IRHIBuffer? VertexBuffer;
        public IRHIBuffer? IndexBuffer;
        public int IndexCount;
        public int CachedVertexCount;
        public int CachedVersion;
        public uint[][]? ChunkLodFirstIndex;
        public int[][]? ChunkLodIndexCounts;
        public readonly IRHIBuffer?[] InstanceBufferRing = new IRHIBuffer?[3];

        public void DisposeMeshBuffers()
        {
            VertexBuffer?.Dispose();
            VertexBuffer = null;
            IndexBuffer?.Dispose();
            IndexBuffer = null;
            ChunkLodFirstIndex = null;
            ChunkLodIndexCounts = null;
        }

        public void Dispose()
        {
            DisposeMeshBuffers();
            for (int r = 0; r < InstanceBufferRing.Length; r++)
            {
                InstanceBufferRing[r]?.Dispose();
                InstanceBufferRing[r] = null;
            }
        }
    }

    private readonly Dictionary<uint, TerrainGpuEntry> _terrainCache = new();
    private readonly HashSet<uint> _seenThisFrame = new();
    private readonly List<uint> _staleIds = new();

    // Telemetry for SkyCoreDiagnostics / UIPerformanceMonitor
    public int LastVisibleChunks { get; private set; }
    public int LastCulledChunks { get; private set; }
    public int LastRenderedTriangles { get; private set; }
    public int CachedTerrainCount => _terrainCache.Count;

    private readonly IRHISampler? _linearSampler;
    private readonly IRHISampler? _shadowSampler;

    private int MetalFrame()
    {
        var inner = (_device as BlueSky.BSR.RHI.IRHIWrapped<BlueSky.BSR.RHI.IRHIDevice>)?.Inner;
        if (inner is BlueSky.BSR.RHI.Metal.MetalDevice metal)
            return metal.FrameIndex;
        return 0;
    }

    private void DrainMetalFlight()
    {
        var inner = (_device as BlueSky.BSR.RHI.IRHIWrapped<BlueSky.BSR.RHI.IRHIDevice>)?.Inner;
        (inner as BlueSky.BSR.RHI.Metal.MetalDevice)?.DrainPending();
    }

    private bool _disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct TerrainVertex
    {
        public Vector3 Position; // 12 bytes
        public Vector3 Normal;   // 12 bytes
        public Vector2 UV;       // 8 bytes  → stride = 32
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EntityUniforms
    {
        public Matrix4x4 Model;  // 64 bytes
        public Vector4   Color;  // 16 bytes
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AstraSurface
    {
        public Vector4 BaseColor;
        public float   Roughness;
        public float   Metallic;
        public float   AO;
        public float   EmissiveStrength;
        public float   SpecularStrength;
        public float   Shininess;
        public float   Alpha;
        public uint    Flags;
        public Vector2 UVScale;
        public Vector2 UVOffset;
        public Vector4 EmissiveColor;
        public Vector4 Custom0;
        public Vector4 Custom1;
    }

    public TerrainRenderer(IRHIDevice device)
    {
        _device = device;

        _linearSampler = _device.CreateSampler(SamplerDesc.LinearClamp);
        _shadowSampler = _device.CreateSampler(new SamplerDesc
        {
            Filter = SamplerFilter.Linear,
            AddressU = SamplerAddressMode.Clamp,
            AddressV = SamplerAddressMode.Clamp,
            AddressW = SamplerAddressMode.Clamp,
            MaxAnisotropy = 1,
            MinLod = 0,
            MaxLod = 1000,
            CompareLess = true,
        });
    }

    public void Render(
        IRHICommandBuffer cmd,
        World             world,
        TerrainSystem     terrainSystem,
        IRHIPipeline      meshPipeline,
        IRHIBuffer        viewUniformBuffer,
        IRHIBuffer        lightBuffer,
        IRHIBuffer        lightCountBuffer,
        IRHIBuffer        lightSettingsBuffer,
        IRHITexture       shadowMap,
        IRHITexture       whiteTexture,
        IRHITexture       normalTexture,
        IRHITexture       rmaTexture,
        IRHITexture       opacityTexture,
        IRHITexture?      detailAlbedoTexture,
        IRHITexture?      detailNormalTexture,
        IRHITexture?      gradeLutTexture,
        IRHITexture?      bentTexture,
        int               debugView,
        Matrix4x4         viewProj,
        Vector3           cameraPos)
    {
        if (_disposed)
            return;

        LastVisibleChunks = 0;
        LastCulledChunks = 0;
        LastRenderedTriangles = 0;
        _seenThisFrame.Clear();

        cmd.SetPipeline(meshPipeline);
        cmd.SetUniformBuffer(viewUniformBuffer, 10);
        cmd.SetUniformBuffer(lightBuffer, 13);
        cmd.SetUniformBuffer(lightCountBuffer, 14);
        cmd.SetUniformBuffer(lightSettingsBuffer, 15);
        cmd.SetTexture(shadowMap,    1);
        cmd.SetTexture(whiteTexture, 2);
        cmd.SetTexture(normalTexture,3);
        cmd.SetTexture(rmaTexture,   4);
        cmd.SetTexture(opacityTexture,5);
        cmd.SetTexture(detailAlbedoTexture ?? whiteTexture, 6);
        cmd.SetTexture(detailNormalTexture ?? normalTexture, 7);
        cmd.SetTexture(gradeLutTexture ?? whiteTexture, 8);
        cmd.SetTexture(bentTexture ?? whiteTexture, 9);

        if (_shadowSampler != null)
            cmd.SetSampler(_shadowSampler, 1);
        if (_linearSampler != null)
        {
            for (uint s = 2; s <= 9; s++)
                cmd.SetSampler(_linearSampler, s);
        }

        var surface = new AstraSurface
        {
            BaseColor        = new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
            Roughness        = 0.9f,
            Metallic         = 0.0f,
            AO               = 1.0f,
            EmissiveStrength = 0.0f,
            SpecularStrength = 0.5f,
            Shininess        = 16.0f,
            Alpha            = 1.0f,
            Flags            = (1u << 12) | ((uint)(debugView & 7) << 14),
            UVScale          = new Vector2(1, 1),
            UVOffset         = new Vector2(0, 0),
            EmissiveColor    = new Vector4(0, 0, 0, 1),
            Custom0          = new Vector4(1, 0, 0, 0),
            Custom1          = new Vector4(0, 0, 0, 0),
        };
        var surfaceSpan = MemoryMarshal.CreateSpan(ref surface, 1);
        cmd.SetFragmentUniforms(11, MemoryMarshal.AsBytes(surfaceSpan));
        cmd.SetFragmentUniforms(2, MemoryMarshal.AsBytes(surfaceSpan));

        var frustum = BSFrustum.FromViewProjection(FromNumerics(viewProj));
        int frameRing = MetalFrame();

        var query = world.CreateQuery()
            .All<TerrainComponent>()
            .All<TransformComponent>()
            .Build();

        foreach (var ecsChunk in world.EnumerateQueryChunks(query))
        {
            var entities     = ecsChunk.GetEntities();
            int transformIdx = ecsChunk.GetComponentIndex(typeof(TransformComponent));

            for (int i = 0; i < ecsChunk.Count; i++)
            {
                var entity    = entities[i];
                var transform = ecsChunk.GetComponent<TransformComponent>(i, transformIdx);
                uint entityId = (uint)entity.Id;

                var meshData = terrainSystem.GetMesh(entityId);
                if (meshData == null)
                    continue;

                _seenThisFrame.Add(entityId);
                var entry = GetOrUploadTerrainEntry(entityId, meshData.Value);
                var instBuf = entry.InstanceBufferRing[frameRing];
                if (entry.VertexBuffer == null || entry.IndexBuffer == null || instBuf == null)
                    continue;

                var bsWorldMatrix = transform.WorldMatrix;
                var worldMatrix = ToMatrix4x4(bsWorldMatrix);
                var instance = new EntityUniforms
                {
                    Model = worldMatrix,
                    Color = new Vector4(1, 1, 1, 1)
                };
                var instSpan = MemoryMarshal.CreateSpan(ref instance, 1);
                _device.UpdateBuffer(instBuf, MemoryMarshal.AsBytes(instSpan));

                cmd.SetUniformBuffer(instBuf, 12);
                cmd.SetVertexBuffer(entry.VertexBuffer, 0);
                cmd.SetIndexBuffer(entry.IndexBuffer, IndexType.UInt32);

                BSVector3 localCamPos = new(cameraPos.X, cameraPos.Y, cameraPos.Z);
                if (Matrix4x4.Invert(worldMatrix, out var invWorld))
                {
                    var transformed = Vector3.Transform(cameraPos, invWorld);
                    localCamPos = new BSVector3(transformed.X, transformed.Y, transformed.Z);
                }

                if (meshData.Value.Chunks is { Length: > 0 } chunks &&
                    entry.ChunkLodFirstIndex != null &&
                    entry.ChunkLodIndexCounts != null &&
                    entry.ChunkLodFirstIndex.Length == chunks.Length)
                {
                    uint pendingFirstIndex = 0;
                    uint pendingIndexCount = 0;

                    for (int c = 0; c < chunks.Length; c++)
                    {
                        ref readonly var chunk = ref chunks[c];
                        var localBox = new BSAABB(chunk.BoundsMin, chunk.BoundsMax).Expand(0.5f);
                        var worldBox = localBox.Transform(bsWorldMatrix);

                        if (!frustum.IntersectsAABB(worldBox))
                        {
                            LastCulledChunks++;
                            continue;
                        }

                        int lodCount = entry.ChunkLodIndexCounts[c].Length;
                        int selectedLod = SelectChunkLodIndex(in chunk, localCamPos, lodCount);
                        uint firstIndex = entry.ChunkLodFirstIndex[c][selectedLod];
                        int idxCount = entry.ChunkLodIndexCounts[c][selectedLod];

                        if (idxCount > 0)
                        {
                            LastVisibleChunks++;
                            LastRenderedTriangles += idxCount / 3;

                            // Coalesce contiguous index ranges into a single DrawIndexed call
                            if (pendingIndexCount > 0 && pendingFirstIndex + pendingIndexCount == firstIndex)
                            {
                                pendingIndexCount += (uint)idxCount;
                            }
                            else
                            {
                                if (pendingIndexCount > 0)
                                    cmd.DrawIndexed(pendingIndexCount, 1, pendingFirstIndex, 0, 0);

                                pendingFirstIndex = firstIndex;
                                pendingIndexCount = (uint)idxCount;
                            }
                        }
                    }

                    if (pendingIndexCount > 0)
                        cmd.DrawIndexed(pendingIndexCount, 1, pendingFirstIndex, 0, 0);
                }
                else if (entry.IndexCount > 0)
                {
                    cmd.DrawIndexed((uint)entry.IndexCount, 1, 0, 0, 0);
                    LastVisibleChunks++;
                    LastRenderedTriangles += entry.IndexCount / 3;
                }
            }
        }

        // Evict GPU buffers for any terrain entities that were destroyed or streamed out
        if (_terrainCache.Count > _seenThisFrame.Count)
        {
            _staleIds.Clear();
            foreach (var id in _terrainCache.Keys)
            {
                if (!_seenThisFrame.Contains(id))
                    _staleIds.Add(id);
            }
            if (_staleIds.Count > 0)
            {
                DrainMetalFlight();
                foreach (var id in _staleIds)
                {
                    if (_terrainCache.Remove(id, out var stale))
                        stale.Dispose();
                }
            }
        }
    }

    private static int SelectChunkLodIndex(in TerrainChunkMeshData chunk, BSVector3 cameraLocalPos, int lodCount)
    {
        if (lodCount <= 1)
            return 0;

        var center = (chunk.BoundsMin + chunk.BoundsMax) * 0.5f;
        float dist = (cameraLocalPos - center).Length;
        int lod = 0;
        float threshold = 64.0f;
        while (lod + 1 < lodCount && dist > threshold)
        {
            lod++;
            threshold *= 2.0f;
        }
        return lod;
    }

    private TerrainGpuEntry GetOrUploadTerrainEntry(uint entityId, TerrainMeshData meshData)
    {
        if (!_terrainCache.TryGetValue(entityId, out var entry))
        {
            entry = new TerrainGpuEntry();
            for (int r = 0; r < 3; r++)
            {
                entry.InstanceBufferRing[r] = _device.CreateBuffer(new BufferDesc
                {
                    Size       = (ulong)Marshal.SizeOf<EntityUniforms>(),
                    Usage      = BufferUsage.Uniform | BufferUsage.TransferDst,
                    MemoryType = MemoryType.CpuToGpu,
                    DebugName  = $"Terrain.{entityId}.InstanceBuffer{r}"
                });
            }
            _terrainCache[entityId] = entry;
        }

        if (meshData.Vertices == null || meshData.Indices == null || meshData.Vertices.Length == 0)
            return entry;

        if (entry.VertexBuffer != null &&
            entry.CachedVertexCount == meshData.Vertices.Length &&
            entry.CachedVersion == meshData.Version)
        {
            return entry;
        }

        DrainMetalFlight();
        entry.DisposeMeshBuffers();

        var vertices = new TerrainVertex[meshData.Vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new TerrainVertex
            {
                Position = new Vector3(meshData.Vertices[i].X, meshData.Vertices[i].Y, meshData.Vertices[i].Z),
                Normal   = new Vector3(meshData.Normals[i].X,  meshData.Normals[i].Y,  meshData.Normals[i].Z),
                UV       = new Vector2(meshData.UVs[i].X,      meshData.UVs[i].Y)
            };
        }

        entry.VertexBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = (ulong)(vertices.Length * Marshal.SizeOf<TerrainVertex>()),
            Usage      = BufferUsage.Vertex | BufferUsage.TransferDst,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = $"Terrain.{entityId}.VB"
        });
        _device.UpdateBuffer(entry.VertexBuffer, MemoryMarshal.AsBytes(vertices.AsSpan()));

        if (meshData.Chunks is { Length: > 0 } chunks)
        {
            int maxLodCount = 0;
            int totalPackedIndices = 0;
            entry.ChunkLodFirstIndex = new uint[chunks.Length][];
            entry.ChunkLodIndexCounts = new int[chunks.Length][];

            for (int c = 0; c < chunks.Length; c++)
            {
                int lodCount = chunks[c].LodIndices?.Length ?? 0;
                if (lodCount > maxLodCount) maxLodCount = lodCount;
                entry.ChunkLodFirstIndex[c] = new uint[lodCount];
                entry.ChunkLodIndexCounts[c] = new int[lodCount];

                for (int l = 0; l < lodCount; l++)
                {
                    int len = chunks[c].LodIndices![l]?.Length ?? 0;
                    entry.ChunkLodIndexCounts[c][l] = len;
                    totalPackedIndices += len;
                }
            }

            if (totalPackedIndices > 0)
            {
                // Pack by LOD major, then chunk minor so all LOD-0 chunks are contiguous in the IndexBuffer!
                var packedIndices = new uint[totalPackedIndices];
                int writeCursor = 0;
                for (int l = 0; l < maxLodCount; l++)
                {
                    for (int c = 0; c < chunks.Length; c++)
                    {
                        var lods = chunks[c].LodIndices;
                        if (lods == null || l >= lods.Length) continue;
                        var lodIdx = lods[l];
                        if (lodIdx == null || lodIdx.Length == 0) continue;

                        entry.ChunkLodFirstIndex[c][l] = (uint)writeCursor;
                        Array.Copy(lodIdx, 0, packedIndices, writeCursor, lodIdx.Length);
                        writeCursor += lodIdx.Length;
                    }
                }

                entry.IndexBuffer = _device.CreateBuffer(new BufferDesc
                {
                    Size       = (ulong)(packedIndices.Length * sizeof(uint)),
                    Usage      = BufferUsage.Index | BufferUsage.TransferDst,
                    MemoryType = MemoryType.CpuToGpu,
                    DebugName  = $"Terrain.{entityId}.UnifiedChunkLOD.IB"
                });
                _device.UpdateBuffer(entry.IndexBuffer, MemoryMarshal.AsBytes(packedIndices.AsSpan()));
            }
        }

        if (entry.IndexBuffer == null)
        {
            entry.IndexBuffer = _device.CreateBuffer(new BufferDesc
            {
                Size       = (ulong)(meshData.Indices.Length * sizeof(uint)),
                Usage      = BufferUsage.Index | BufferUsage.TransferDst,
                MemoryType = MemoryType.CpuToGpu,
                DebugName  = $"Terrain.{entityId}.IB"
            });
            _device.UpdateBuffer(entry.IndexBuffer, MemoryMarshal.AsBytes(meshData.Indices.AsSpan()));
        }

        entry.IndexCount = meshData.Indices.Length;
        entry.CachedVertexCount = meshData.Vertices.Length;
        entry.CachedVersion = meshData.Version;
        return entry;
    }

    private static Matrix4x4 ToMatrix4x4(BSMatrix4x4 m) =>
        new(m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44);

    private static BSMatrix4x4 FromNumerics(Matrix4x4 m) =>
        new(m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _terrainCache.Values)
            entry.Dispose();
        _terrainCache.Clear();
        _linearSampler?.Dispose();
        _shadowSampler?.Dispose();
    }
}
