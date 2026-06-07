using System;
using System.Numerics;
using System.Runtime.InteropServices;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using NotBSRenderer;

namespace BlueSky.Rendering;

/// <summary>
/// Clean terrain renderer — single mesh per terrain entity, proper instance buffer at slot 30.
/// </summary>
public sealed class TerrainRenderer : IDisposable
{
    private readonly IRHIDevice _device;

    // Per-terrain GPU resources
    private IRHIBuffer? _vertexBuffer;
    private IRHIBuffer? _indexBuffer;
    private int         _indexCount;

    // Instance buffer at slot 30 (matches vs_mesh: constant EntityUniforms* entities [[buffer(30)]])
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
    private struct MaterialData
    {
        public Vector4 AlbedoAndMetallic; // xyz=albedo, w=metallic
        public float   Roughness;
        public float   Ao;
        public float   Emission;
        public float   Subsurface;
        public int     UseAlbedoTex;
        public int     UseNormalTex;
        public int     UseRMATex;
        public int     BlendMode;
        public int     UseOpacityTex;
        private int    _pad0;
        private int    _pad1;
        private int    _pad2;
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
        Matrix4x4         viewProj,
        Vector3           cameraPos)
    {
        if (_disposed || _instanceBuffer == null)
            return;

        // ── Pipeline + shared textures ────────────────────────────────────────
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

        // ── Terrain material — neutral editor checker like Unreal's default floor
        var material = new MaterialData
        {
            AlbedoAndMetallic = new Vector4(0.7f, 0.7f, 0.7f, 0.0f),
            Roughness         = 0.9f,
            Ao                = 1.0f,
            Emission          = 0.0f,
            Subsurface        = 0.0f,
            UseAlbedoTex      = 0,
            UseNormalTex      = 0,
            UseRMATex         = 0,
            BlendMode         = 0,
            UseOpacityTex     = 0
        };
        var matSpan = MemoryMarshal.CreateSpan(ref material, 1);
        cmd.SetFragmentUniforms(11, MemoryMarshal.AsBytes(matSpan));

        // ── Bind instance buffer at slot 30 (shader reads entities[instance_id]) ─
        cmd.SetUniformBuffer(_instanceBuffer, 30);

        // ── Iterate terrain entities ──────────────────────────────────────────
        var query = world.CreateQuery()
            .All<TerrainComponent>()
            .All<TransformComponent>()
            .Build();

        foreach (var ecsChunk in world.GetQueryChunks(query))
        {
            var entities    = ecsChunk.GetEntities();
            int terrainIdx  = ecsChunk.GetComponentIndex(typeof(TerrainComponent));
            int transformIdx= ecsChunk.GetComponentIndex(typeof(TransformComponent));

            for (int i = 0; i < ecsChunk.Count; i++)
            {
                var entity    = entities[i];
                var transform = ecsChunk.GetComponent<TransformComponent>(i, transformIdx);

                var meshData = terrainSystem.GetMesh((uint)entity.Id);
                if (meshData == null)
                    continue;

                // Upload mesh if needed
                UploadMesh(meshData.Value);

                if (_vertexBuffer == null || _indexBuffer == null || _indexCount == 0)
                    continue;

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
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private void UploadMesh(TerrainMeshData meshData)
    {
        if (meshData.Vertices == null || meshData.Indices == null || meshData.Vertices.Length == 0)
            return;

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
