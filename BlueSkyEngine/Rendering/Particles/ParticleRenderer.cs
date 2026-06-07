using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NotBSRenderer;
using BlueSky.Core.ECS;

namespace BlueSky.Rendering.Particles;

public class ParticleRenderer : IDisposable
{
    private readonly IRHIDevice _device;
    private IRHIPipeline? _particlePipeline;
    private IRHIBuffer? _vertexBuffer; // Quad vertices
    private IRHITexture? _particleAtlas;
    private IRHIBuffer? _uniformBuffer;
    
    [StructLayout(LayoutKind.Sequential)]
    private struct ParticleUniforms
    {
        public Matrix4x4 ViewProjection;
        public Vector3 CameraUp;
        public float AtlasCols;
        public Vector3 CameraRight;
        public float AtlasRows;
    }

    public ParticleRenderer(IRHIDevice device)
    {
        _device = device;
    }

    public void Initialize()
    {
        CreateQuadBuffer();
        CreateUniformBuffer();
        CreatePipeline();
        CreateDefaultTexture();
    }

    public void Render(IRHICommandBuffer cmd, IRHIBuffer particleBuffer, uint particleCount, Matrix4x4 viewProjection, Vector3 cameraPosition, Vector3 cameraUp, Vector3 cameraRight)
    {
        if (_particlePipeline == null || particleCount == 0) return;

        var uniforms = new ParticleUniforms
        {
            ViewProjection = viewProjection,
            CameraUp = cameraUp,
            CameraRight = cameraRight,
            AtlasCols = 1.0f, // Update if using real atlas
            AtlasRows = 1.0f
        };
        
        _device.UpdateBuffer(_uniformBuffer!, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref uniforms, 1)));

        cmd.SetPipeline(_particlePipeline);
        
        // Vertex buffer (slot 0: quad vertices, slot 1: particle instance data)
        cmd.SetVertexBuffer(_vertexBuffer!, 0);
        cmd.SetVertexBuffer(particleBuffer, 1);
        
        cmd.SetUniformBuffer(_uniformBuffer!, 0);
        if (_particleAtlas != null)
        {
            cmd.SetTexture(_particleAtlas, 0, 1); // Set texture in set 1 binding 0
        }

        cmd.Draw(6, particleCount);
    }

    private void CreateQuadBuffer()
    {
        // Simple quad for instancing (x, y)
        float[] vertices = {
            -0.5f, -0.5f,
             0.5f, -0.5f,
             0.5f,  0.5f,
             0.5f,  0.5f,
            -0.5f,  0.5f,
            -0.5f, -0.5f
        };

        _vertexBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)(vertices.Length * sizeof(float)),
            Usage = BufferUsage.Vertex | BufferUsage.TransferDst,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "ParticleQuadBuffer"
        });

        _device.UploadBuffer(_vertexBuffer, MemoryMarshal.AsBytes(vertices.AsSpan()));
    }

    private void CreateUniformBuffer()
    {
        _uniformBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)Marshal.SizeOf<ParticleUniforms>(),
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "ParticleUniformBuffer"
        });
    }

    private void CreatePipeline()
    {
        // In a real implementation we would compile the HLSL/Metal shaders and create the pipeline here.
        // We will mock this for now to follow the architecture pattern.
    }

    private void CreateDefaultTexture()
    {
        // Create a simple soft circle texture (16x16)
        int size = 16;
        byte[] data = new byte[size * size * 4];
        float center = size / 2.0f;
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - center;
                float dy = y + 0.5f - center;
                float distSq = (dx * dx + dy * dy) / (center * center);
                
                float alpha = Math.Clamp(1.0f - distSq, 0.0f, 1.0f);
                byte a = (byte)(alpha * 255);
                
                int index = (y * size + x) * 4;
                data[index] = 255;
                data[index + 1] = 255;
                data[index + 2] = 255;
                data[index + 3] = a;
            }
        }

        _particleAtlas = _device.CreateTexture(new TextureDesc
        {
            Width = (uint)size,
            Height = (uint)size,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDst,
            DebugName = "ParticleDefaultAtlas"
        });

        _device.UploadTexture(_particleAtlas, data);
    }

    public void Dispose()
    {
        _vertexBuffer?.Dispose();
        _uniformBuffer?.Dispose();
        _particleAtlas?.Dispose();
        _particlePipeline?.Dispose();
    }
}
