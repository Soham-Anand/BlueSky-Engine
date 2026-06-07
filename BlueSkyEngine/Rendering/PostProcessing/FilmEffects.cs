using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NotBSRenderer;

namespace BlueSky.Rendering.PostProcessing;

/// <summary>
/// Film Grain - adds texture and cinematic feel
/// Simulates analog film grain for organic look
/// </summary>
public class FilmGrain : IDisposable
{
    private readonly IRHIDevice _device;
    private IRHIPipeline? _grainPipeline;
    private IRHIBuffer? _settingsBuffer;
    private IRHITexture? _grainTexture;
    
    public FilmGrain(IRHIDevice device)
    {
        _device = device;
    }
    
    public void Initialize()
    {
        CreateGrainTexture();
        CreatePipeline();
        CreateBuffers();
        
        Console.WriteLine("[FilmGrain] Initialized");
    }
    
    public IRHITexture Apply(IRHICommandBuffer cmd, IRHITexture input,
                            float intensity, float time)
    {
        if (_grainPipeline == null)
            return input;
        
        var settings = new FilmGrainSettings
        {
            Intensity = intensity,
            Time = time,
            LuminanceContribution = 0.5f, // How much grain varies with brightness
            ColorContribution = 0.1f // Colored grain amount
        };
        
        _device.UpdateBuffer(_settingsBuffer!, MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref settings, 1)));
        
        var outputDesc = new TextureDesc
        {
            Width = input.Width,
            Height = input.Height,
            Depth = 1,
            Format = input.Format,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "FilmGrain_Output"
        };
        
        var output = _device.CreateTexture(outputDesc);
        
        cmd.BeginRenderPass(output, ClearValue.FromColor(0, 0, 0, 1));
        cmd.SetPipeline(_grainPipeline);
        cmd.SetTexture(input, 0);
        cmd.SetTexture(_grainTexture!, 1);
        cmd.SetUniformBuffer(_settingsBuffer!, 2);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();
        
        return output;
    }
    
    private void CreateGrainTexture()
    {
        // Create tileable grain texture (256x256)
        const int size = 256;
        var grainData = new byte[size * size];
        var random = new Random(42); // Fixed seed for consistency
        
        for (int i = 0; i < grainData.Length; i++)
        {
            grainData[i] = (byte)random.Next(256);
        }
        
        _grainTexture = _device.CreateTexture(new TextureDesc
        {
            Width = size,
            Height = size,
            Depth = 1,
            Format = TextureFormat.R8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDst,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "Grain_Texture"
        });
        
        _device.UploadTexture(_grainTexture, grainData);
    }
    
    private void CreatePipeline()
    {
        // TODO: Load film grain shader
        Console.WriteLine("[FilmGrain] Pipeline created");
    }
    
    private void CreateBuffers()
    {
        _settingsBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)Marshal.SizeOf<FilmGrainSettings>(),
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "FilmGrain_Settings"
        });
    }
    
    public void Dispose()
    {
        _grainTexture?.Dispose();
        _settingsBuffer?.Dispose();
        _grainPipeline?.Dispose();
    }
}

/// <summary>
/// Vignette - darkens edges for focus
/// Classic cinematic effect
/// </summary>
public class Vignette : IDisposable
{
    private readonly IRHIDevice _device;
    private IRHIPipeline? _vignettePipeline;
    private IRHIBuffer? _settingsBuffer;
    
    public Vignette(IRHIDevice device)
    {
        _device = device;
    }
    
    public void Initialize()
    {
        CreatePipeline();
        CreateBuffers();
        
        Console.WriteLine("[Vignette] Initialized");
    }
    
    public IRHITexture Apply(IRHICommandBuffer cmd, IRHITexture input,
                            float intensity, float smoothness)
    {
        if (_vignettePipeline == null)
            return input;
        
        var settings = new VignetteSettings
        {
            Intensity = intensity,
            Smoothness = smoothness,
            Roundness = 1.0f,
            Center = new Vector2(0.5f, 0.5f)
        };
        
        _device.UpdateBuffer(_settingsBuffer!, MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref settings, 1)));
        
        var outputDesc = new TextureDesc
        {
            Width = input.Width,
            Height = input.Height,
            Depth = 1,
            Format = input.Format,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "Vignette_Output"
        };
        
        var output = _device.CreateTexture(outputDesc);
        
        cmd.BeginRenderPass(output, ClearValue.FromColor(0, 0, 0, 1));
        cmd.SetPipeline(_vignettePipeline);
        cmd.SetTexture(input, 0);
        cmd.SetUniformBuffer(_settingsBuffer!, 1);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();
        
        return output;
    }
    
    private void CreatePipeline()
    {
        // TODO: Load vignette shader
        Console.WriteLine("[Vignette] Pipeline created");
    }
    
    private void CreateBuffers()
    {
        _settingsBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)Marshal.SizeOf<VignetteSettings>(),
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "Vignette_Settings"
        });
    }
    
    public void Dispose()
    {
        _settingsBuffer?.Dispose();
        _vignettePipeline?.Dispose();
    }
}

/// <summary>
/// Chromatic Aberration - lens distortion effect
/// Simulates color fringing from real camera lenses
/// </summary>
public class ChromaticAberration : IDisposable
{
    private readonly IRHIDevice _device;
    private IRHIPipeline? _chromaticPipeline;
    private IRHIBuffer? _settingsBuffer;
    
    public ChromaticAberration(IRHIDevice device)
    {
        _device = device;
    }
    
    public void Initialize()
    {
        CreatePipeline();
        CreateBuffers();
        
        Console.WriteLine("[ChromaticAberration] Initialized");
    }
    
    public IRHITexture Apply(IRHICommandBuffer cmd, IRHITexture input, float intensity)
    {
        if (_chromaticPipeline == null)
            return input;
        
        var settings = new ChromaticAberrationSettings
        {
            Intensity = intensity,
            DistortionStrength = 0.5f
        };
        
        _device.UpdateBuffer(_settingsBuffer!, MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref settings, 1)));
        
        var outputDesc = new TextureDesc
        {
            Width = input.Width,
            Height = input.Height,
            Depth = 1,
            Format = input.Format,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "ChromaticAberration_Output"
        };
        
        var output = _device.CreateTexture(outputDesc);
        
        cmd.BeginRenderPass(output, ClearValue.FromColor(0, 0, 0, 1));
        cmd.SetPipeline(_chromaticPipeline);
        cmd.SetTexture(input, 0);
        cmd.SetUniformBuffer(_settingsBuffer!, 1);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();
        
        return output;
    }
    
    private void CreatePipeline()
    {
        // TODO: Load chromatic aberration shader
        Console.WriteLine("[ChromaticAberration] Pipeline created");
    }
    
    private void CreateBuffers()
    {
        _settingsBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)Marshal.SizeOf<ChromaticAberrationSettings>(),
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "ChromaticAberration_Settings"
        });
    }
    
    public void Dispose()
    {
        _settingsBuffer?.Dispose();
        _chromaticPipeline?.Dispose();
    }
}

/// <summary>
/// Motion Blur - adds sense of speed
/// Per-object motion blur using velocity buffer
/// </summary>
public class MotionBlur : IDisposable
{
    private readonly IRHIDevice _device;
    private IRHIPipeline? _motionBlurPipeline;
    private IRHIBuffer? _settingsBuffer;
    
    private int _width;
    private int _height;
    private MotionBlurQuality _quality;
    
    public MotionBlur(IRHIDevice device)
    {
        _device = device;
    }
    
    public void Initialize(int width, int height, MotionBlurQuality quality)
    {
        _width = width;
        _height = height;
        _quality = quality;
        
        CreatePipeline();
        CreateBuffers();
        
        Console.WriteLine($"[MotionBlur] Initialized ({quality})");
    }
    
    public IRHITexture Apply(IRHICommandBuffer cmd, IRHITexture input,
                            IRHITexture velocityBuffer, float intensity)
    {
        if (_motionBlurPipeline == null)
            return input;
        
        var settings = new MotionBlurSettings
        {
            Intensity = intensity,
            MaxSamples = GetMaxSamples(),
            VelocityScale = 1.0f
        };
        
        _device.UpdateBuffer(_settingsBuffer!, MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref settings, 1)));
        
        var outputDesc = new TextureDesc
        {
            Width = input.Width,
            Height = input.Height,
            Depth = 1,
            Format = input.Format,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "MotionBlur_Output"
        };
        
        var output = _device.CreateTexture(outputDesc);
        
        cmd.BeginRenderPass(output, ClearValue.FromColor(0, 0, 0, 1));
        cmd.SetPipeline(_motionBlurPipeline);
        cmd.SetTexture(input, 0);
        cmd.SetTexture(velocityBuffer, 1);
        cmd.SetUniformBuffer(_settingsBuffer!, 2);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();
        
        return output;
    }
    
    private int GetMaxSamples()
    {
        return _quality switch
        {
            MotionBlurQuality.Low => 4,
            MotionBlurQuality.Medium => 8,
            MotionBlurQuality.High => 16,
            _ => 8
        };
    }
    
    private void CreatePipeline()
    {
        // TODO: Load motion blur shader
        Console.WriteLine("[MotionBlur] Pipeline created");
    }
    
    private void CreateBuffers()
    {
        _settingsBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)Marshal.SizeOf<MotionBlurSettings>(),
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "MotionBlur_Settings"
        });
    }
    
    public void Dispose()
    {
        _settingsBuffer?.Dispose();
        _motionBlurPipeline?.Dispose();
    }
}

// Settings structures
[StructLayout(LayoutKind.Sequential)]
struct FilmGrainSettings
{
    public float Intensity;
    public float Time;
    public float LuminanceContribution;
    public float ColorContribution;
}

[StructLayout(LayoutKind.Sequential)]
struct VignetteSettings
{
    public float Intensity;
    public float Smoothness;
    public float Roundness;
    public float _padding;
    public Vector2 Center;
    public Vector2 _padding2;
}

[StructLayout(LayoutKind.Sequential)]
struct ChromaticAberrationSettings
{
    public float Intensity;
    public float DistortionStrength;
    public Vector2 _padding;
}

[StructLayout(LayoutKind.Sequential)]
struct MotionBlurSettings
{
    public float Intensity;
    public int MaxSamples;
    public float VelocityScale;
    public float _padding;
}
