using System;
using System.Numerics;
using System.Runtime.InteropServices;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Rendering.RHI;

namespace BlueSky.Rendering;

/// <summary>
    /// Clean terrain renderer — single mesh per terrain entity, proper instance buffer at slot 12.
/// </summary>
public sealed class TerrainRenderer : IDisposable
{
    private readonly IRHIDevice _device;

    // Per-terrain GPU resources (WARNING: This renderer only supports ONE terrain entity!)
    private IRHIBuffer? _vertexBuffer;
    private IRHIBuffer? _indexBuffer;
    private int         _indexCount;
    private int         _cachedVertexCount;  // Track if mesh changed
    private uint        _cachedEntityId;     // Track which terrain we cached
    private int         _cachedVersion;      // Track cached version

    // Instance buffer at slot 12 (matches vs_mesh: cbuffer EntityUniforms : register(b12))
    private IRHIBuffer? _instanceBuffer;

    private bool _disposed;

    // ── Structs must match Metal shader exactly ───────────────────────────────

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
        public Vector4 BaseColor;           // 16  rgb=base, a=draw alpha
        public float   Roughness;           // 4
        public float   Metallic;            // 4
        public float   AO;                  // 4
        public float   EmissiveStrength;    // 4
        public float   SpecularStrength;    // 4
        public float   Shininess;           // 4
        public float   Alpha;               // 4
        public uint    Flags;               // 4
        public Vector2 UVScale;             // 8
        public Vector2 UVOffset;            // 8
        public Vector4 EmissiveColor;       // 16
        public Vector4 Custom0;             // 16
        public Vector4 Custom1;             // 16
    }

    // ─────────────────────────────────────────────────────────────────────────

    public TerrainRenderer(IRHIDevice device)
    {
        _device = device;

        // Allocate a single-slot instance buffer (one terrain entity at a time)
        _instanceBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = (ulong)Marshal.SizeOf<EntityUniforms>(),
            Usage      = BufferUsage.Uniform | BufferUsage.TransferDst,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "Terrain.InstanceBuffer"
        });
    }

    // ─────────────────────────────────────────────────────────────────────────

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
        {
            return;
        }
        if (_instanceBuffer == null)
        {
            return;
        }

        // ── Pipeline + shared textures ────────────────────────────────────────
        // NOTE: fs_mesh unconditionally samples t6/t7 (detail, gated by feat=0 so
        // cheap), t8 (grade LUT — ALWAYS sampled) and t9 (bent, gated). Leaving
        // them unbound returns black on Metal/DX11/Vulkan, which is why terrain
        // rendered black while regular meshes (which bind defaults) did not.
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

        // ── Terrain surface — plain white (no material system) ────────────────
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
            Flags            = (1u << 12) | ((uint)(debugView & 7) << 14), // Terrain (+debug view)
            UVScale          = new Vector2(1, 1),
            UVOffset         = new Vector2(0, 0),
            EmissiveColor    = new Vector4(0, 0, 0, 1),
            Custom0          = new Vector4(1, 0, 0, 0),
            Custom1          = new Vector4(0, 0, 0, 0),
        };
        var surfaceSpan = MemoryMarshal.CreateSpan(ref surface, 1);
        cmd.SetFragmentUniforms(11, MemoryMarshal.AsBytes(surfaceSpan));
        cmd.SetFragmentUniforms(2, MemoryMarshal.AsBytes(surfaceSpan));

        // ── Bind instance buffer at slot 12 (shader reads EntityModel/EntityColor) ─
        cmd.SetUniformBuffer(_instanceBuffer, 12);

        // ── Iterate terrain entities ──────────────────────────────────────────
        var query = world.CreateQuery()
            .All<TerrainComponent>()
            .All<TransformComponent>()
            .Build();

        int chunkCount = 0;
        int entityCount = 0;
        int drawCount = 0;

        foreach (var ecsChunk in world.GetQueryChunks(query))
        {
            chunkCount++;
            var entities    = ecsChunk.GetEntities();
            int terrainIdx  = ecsChunk.GetComponentIndex(typeof(TerrainComponent));
            int transformIdx= ecsChunk.GetComponentIndex(typeof(TransformComponent));

            for (int i = 0; i < ecsChunk.Count; i++)
            {
                var entity    = entities[i];
                var transform = ecsChunk.GetComponent<TransformComponent>(i, transformIdx);
                uint entityId = (uint)entity.Id;
                entityCount++;

                var meshData = terrainSystem.GetMesh(entityId);
                if (meshData == null)
                {
                    continue;
                }

                // Upload mesh if needed (only if entity or mesh changed)
                UploadMesh(meshData.Value, entityId);

                if (_vertexBuffer == null || _indexBuffer == null || _indexCount == 0)
                {
                    continue;
                }

                // Upload this entity's world matrix into the instance buffer
                var worldMatrix = ToMatrix4x4(transform.WorldMatrix);
                var instance = new EntityUniforms
                {
                    Model = worldMatrix,
                    Color = new Vector4(1, 1, 1, 1)
                };
                var instSpan = MemoryMarshal.CreateSpan(ref instance, 1);
                _device.UpdateBuffer(_instanceBuffer, MemoryMarshal.AsBytes(instSpan));

                // Draw
                cmd.SetVertexBuffer(_vertexBuffer, 0);
                cmd.SetIndexBuffer(_indexBuffer, IndexType.UInt32);
                cmd.DrawIndexed((uint)_indexCount, 1, 0, 0, 0); // firstInstance=0 → entities[0]
                drawCount++;
            }
        }

    }

    // ─────────────────────────────────────────────────────────────────────────

    private void UploadMesh(TerrainMeshData meshData, uint entityId)
    {
        if (meshData.Vertices == null || meshData.Indices == null || meshData.Vertices.Length == 0)
            return;

        // Only reupload if mesh version, entity ID, or vertex count changed
        if (_vertexBuffer != null && 
            _cachedEntityId == entityId && 
            _cachedVertexCount == meshData.Vertices.Length &&
            _cachedVersion == meshData.Version)
        {
            return;
        }

        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();

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

        _vertexBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = (ulong)(vertices.Length * Marshal.SizeOf<TerrainVertex>()),
            Usage      = BufferUsage.Vertex | BufferUsage.TransferDst,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "Terrain.VB"
        });
        _device.UpdateBuffer(_vertexBuffer, MemoryMarshal.AsBytes(vertices.AsSpan()));

        _indexBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = (ulong)(meshData.Indices.Length * sizeof(uint)),
            Usage      = BufferUsage.Index | BufferUsage.TransferDst,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "Terrain.IB"
        });
        _device.UpdateBuffer(_indexBuffer, MemoryMarshal.AsBytes(meshData.Indices.AsSpan()));

        _indexCount = meshData.Indices.Length;
        _cachedVertexCount = meshData.Vertices.Length;
        _cachedEntityId = entityId;
        _cachedVersion = meshData.Version;
        
        Console.WriteLine($"[TerrainRenderer] Uploaded terrain mesh for Entity_{entityId}: {vertices.Length} vertices, {_indexCount} indices (version {meshData.Version})");
    }

    private static Matrix4x4 ToMatrix4x4(BlueSky.Core.Math.Matrix4x4 m) =>
        new(m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _instanceBuffer?.Dispose();
    }
}
