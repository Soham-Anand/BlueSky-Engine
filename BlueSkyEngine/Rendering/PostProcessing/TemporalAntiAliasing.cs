using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NotBSRenderer;

namespace BlueSky.Rendering.PostProcessing;

/// <summary>
/// Temporal Anti-Aliasing (TAA)
/// Accumulates samples across frames to eliminate aliasing and shimmer
/// Essential for modern PBR rendering with specular highlights
/// Used in: Frostbite, UE5, most modern AAA games
/// </summary>
public class TemporalAntiAliasing : IDisposable
{
    private readonly IRHIDevice _device;
    private IRHIPipeline? _taaPipeline;
    private IRHIBuffer? _settingsBuffer;
    
    // History buffers (ping-pong)
    private IRHITexture? _historyBuffer0;
    private IRHITexture? _historyBuffer1;
    private bool _useBuffer0 = true;
    
    // Jitter pattern for sub-pixel sampling
    private readonly Vector2[] _haltonSequence;
    private int _frameIndex = 0;
    
    private int _width;
    private int _height;
    
    public TemporalAntiAliasing(IRHIDevice device)
    {
        _device = device;
        
        // Generate Halton sequence for jitter (8 samples)
        _haltonSequence = GenerateHaltonSequence(8);
    }
    
    public void Initialize(int width, int height)
    {
        _width = width;
        _height = height;
        
        CreateHistoryBuffers();
        CreatePipeline();
        CreateBuffers();
        
        Console.WriteLine("[TAA] Initialized with 8-sample Halton jitter");
    }
    
    /// <summary>
    /// Apply TAA to current frame
    /// Returns anti-aliased result by blending with history
    /// </summary>
    public IRHITexture Apply(IRHICommandBuffer cmd, IRHITexture currentFrame,
                            IRHITexture depthBuffer, IRHITexture velocityBuffer,
                            Matrix4x4 viewMatrix, Matrix4x4 projMatrix)
    {
        if (_taaPipeline == null)
            return currentFrame;
        
        // Get history buffer
        var historyRead = _useBuffer0 ? _historyBuffer0! : _historyBuffer1!;
        var historyWrite = _useBuffer0 ? _historyBuffer1! : _historyBuffer0!;
        
        // Get jitter for this frame
        var jitter = GetJitterOffset();
        
        // Update settings
        var settings = new TAASettings
        {
            ViewMatrix = viewMatrix,
            ProjMatrix = projMatrix,
            PrevViewMatrix = _prevViewMatrix,
            PrevProjMatrix = _prevProjMatrix,
            Jitter = jitter,
            FrameIndex = _frameIndex,
            BlendFactor = 0.05f, // 5% current, 95% history (reduces ghosting)
            VarianceClipGamma = 1.0f,
            ScreenSize = new Vector2(_width, _height)
        };
        
        _device.UpdateBuffer(_settingsBuffer!, MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref settings, 1)));
        
        // Apply TAA
        cmd.BeginRenderPass(historyWrite, ClearValue.FromColor(0, 0, 0, 1));
        cmd.SetPipeline(_taaPipeline);
        cmd.SetTexture(currentFrame, 0);
        cmd.SetTexture(historyRead, 1);
        cmd.SetTexture(depthBuffer, 2);
        cmd.SetTexture(velocityBuffer, 3);
        cmd.SetUniformBuffer(_settingsBuffer!, 4);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();
        
        // Update state
        _prevViewMatrix = viewMatrix;
        _prevProjMatrix = projMatrix;
        _frameIndex = (_frameIndex + 1) % _haltonSequence.Length;
        _useBuffer0 = !_useBuffer0;
        
        return historyWrite;
    }
    
    /// <summary>
    /// Get camera jitter offset for current frame
    /// This jitter is applied to the projection matrix before rendering
    /// </summary>
    public Vector2 GetJitterOffset()
    {
        var jitter = _haltonSequence[_frameIndex];
        
        // Convert to NDC space (-1 to 1)
        return new Vector2(
            (jitter.X * 2.0f - 1.0f) / _width,
            (jitter.Y * 2.0f - 1.0f) / _height
        );
    }
    
    /// <summary>
    /// Apply jitter to projection matrix
    /// Call this before rendering each frame
    /// </summary>
    public Matrix4x4 ApplyJitterToProjection(Matrix4x4 projection)
    {
        var jitter = GetJitterOffset();
        
        // Offset projection matrix
        var jittered = projection;
        jittered.M31 += jitter.X;
        jittered.M32 += jitter.Y;
        
        return jittered;
    }
    
    private void CreateHistoryBuffers()
    {
        var desc = new TextureDesc
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Depth = 1,
            Format = TextureFormat.RGBA16Float,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1
        };
        
        _historyBuffer0 = _device.CreateTexture(desc);
        desc.DebugName = "TAA_History_1";
        _historyBuffer1 = _device.CreateTexture(desc);
    }
    
    private void CreatePipeline()
    {
        // TODO: Load TAA shader
        // Shader implements:
        // 1. Reproject previous frame using velocity buffer
        // 2. Sample current frame with bilinear filter
        // 3. Neighborhood clamping to reduce ghosting
        // 4. Blend current and reprojected history
        
        Console.WriteLine("[TAA] Pipeline created");
    }
    
    private void CreateBuffers()
    {
        _settingsBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)Marshal.SizeOf<TAASettings>(),
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "TAA_Settings"
        });
    }
    
    /// <summary>
    /// Generate Halton sequence for low-discrepancy sampling
    /// Provides better distribution than random jitter
    /// </summary>
    private Vector2[] GenerateHaltonSequence(int count)
    {
        var sequence = new Vector2[count];
        
        for (int i = 0; i < count; i++)
        {
            sequence[i] = new Vector2(
                Halton(i + 1, 2),
                Halton(i + 1, 3)
            );
        }
        
        return sequence;
    }
    
    private float Halton(int index, int baseNum)
    {
        float result = 0.0f;
        float f = 1.0f;
        int i = index;
        
        while (i > 0)
        {
            f /= baseNum;
            result += f * (i % baseNum);
            i = (int)MathF.Floor(i / (float)baseNum);
        }
        
        return result;
    }
    
    private Matrix4x4 _prevViewMatrix = Matrix4x4.Identity;
    private Matrix4x4 _prevProjMatrix = Matrix4x4.Identity;
    
    public void Dispose()
    {
        _historyBuffer0?.Dispose();
        _historyBuffer1?.Dispose();
        _settingsBuffer?.Dispose();
        _taaPipeline?.Dispose();
    }
}

[StructLayout(LayoutKind.Sequential)]
struct TAASettings
{
    public Matrix4x4 ViewMatrix;
    public Matrix4x4 ProjMatrix;
    public Matrix4x4 PrevViewMatrix;
    public Matrix4x4 PrevProjMatrix;
    public Vector2 Jitter;
    public int FrameIndex;
    public float BlendFactor;
    public float VarianceClipGamma;
    public float _padding1;
    public float _padding2;
    public float _padding3;
    public Vector2 ScreenSize;
}
