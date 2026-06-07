using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NotBSRenderer;

namespace BlueSky.Rendering.PostProcessing;

/// <summary>
/// Radial Blur - creates speed lines / zoom effect from a center point
/// </summary>
public class RadialBlur : IDisposable
{
    private readonly IRHIDevice _device;
    private IRHIPipeline? _blurPipeline;
    private IRHIBuffer? _settingsBuffer;
    
    [StructLayout(LayoutKind.Sequential)]
    private struct RadialBlurSettings
    {
        public Vector2 Center;
        public float Intensity;
        public int SampleCount;
        public float Falloff;
        public Vector3 _padding;
    }
    
    public RadialBlur(IRHIDevice device)
    {
        _device = device;
    }
    
    public void Initialize()
    {
        CreatePipeline();
        CreateBuffers();
        
        Console.WriteLine("[RadialBlur] Initialized");
    }
    
    public IRHITexture Apply(IRHICommandBuffer cmd, IRHITexture input, float intensity, Vector2 center, int samples = 10, float falloff = 1.0f)
    {
        if (_blurPipeline == null || intensity <= 0.001f)
            return input;
        
        var settings = new RadialBlurSettings
        {
            Center = center,
            Intensity = intensity,
            SampleCount = samples,
            Falloff = falloff
        };
        
        _device.UpdateBuffer(_settingsBuffer!, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref settings, 1)));
        
        var outputDesc = new TextureDesc
        {
            Width = input.Width,
            Height = input.Height,
            Depth = 1,
            Format = input.Format,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "RadialBlur_Output"
        };
        
        var output = _device.CreateTexture(outputDesc);
        
        cmd.BeginRenderPass(output, ClearValue.FromColor(0, 0, 0, 1));
        cmd.SetPipeline(_blurPipeline);
        cmd.SetTexture(input, 0);
        cmd.SetUniformBuffer(_settingsBuffer!, 1);
        cmd.Draw(3, 1, 0, 0); // Fullscreen triangle
        cmd.EndRenderPass();
        
        return output;
    }
    
    private void CreatePipeline()
    {
        // TODO: Load radial blur shader
    }
    
    private void CreateBuffers()
    {
        _settingsBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)Marshal.SizeOf<RadialBlurSettings>(),
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "RadialBlur_Settings"
        });
    }
    
    public void Dispose()
    {
        _settingsBuffer?.Dispose();
        _blurPipeline?.Dispose();
    }
}
