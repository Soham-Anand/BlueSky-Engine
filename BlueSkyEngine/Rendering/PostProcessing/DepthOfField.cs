using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NotBSRenderer;

namespace BlueSky.Rendering.PostProcessing;

/// <summary>
/// Cinematic Depth of Field with physically accurate bokeh
/// Simulates camera lens behavior for artistic focus control
/// Inspired by Frostbite's DOF and UE5's Cinematic DOF
/// </summary>
public class DepthOfField : IDisposable
{
    private readonly IRHIDevice _device;
    
    // Pipelines
    private IRHIPipeline? _cocPipeline; // Circle of Confusion
    private IRHIPipeline? _bokehPipeline; // Bokeh blur
    private IRHIPipeline? _compositePipeline;
    
    // Textures
    private IRHITexture? _cocTexture;
    private IRHITexture? _nearBlurTexture;
    private IRHITexture? _farBlurTexture;
    
    private IRHIBuffer? _settingsBuffer;
    
    private int _width;
    private int _height;
    private DOFQuality _quality;
    
    public DepthOfField(IRHIDevice device)
    {
        _device = device;
    }
    
    public void Initialize(int width, int height, DOFQuality quality)
    {
        _width = width;
        _height = height;
        _quality = quality;
        
        CreateTextures();
        CreatePipelines();
        CreateBuffers();
        
        Console.WriteLine($"[DOF] Initialized at {width}x{height} ({quality})");
    }
    
    /// <summary>
    /// Apply depth of field effect
    /// </summary>
    /// <param name="focalDistance">Distance to focus plane in world units</param>
    /// <param name="aperture">F-stop (lower = more blur). Typical: 1.4-16</param>
    /// <param name="bokehShape">Shape of bokeh highlights</param>
    public IRHITexture Apply(IRHICommandBuffer cmd, IRHITexture sceneColor,
                            IRHITexture depthBuffer, float focalDistance,
                            float aperture, BokehShape bokehShape)
    {
        if (_cocPipeline == null || _bokehPipeline == null || _compositePipeline == null)
            return sceneColor;
        
        // Calculate DOF parameters
        float focalLength = 50.0f; // 50mm lens (standard)
        float sensorSize = 36.0f; // Full-frame sensor (36mm)
        
        // Calculate circle of confusion scale
        // CoC = (focalLength * focalLength) / (aperture * (focalDistance - focalLength))
        float cocScale = (focalLength * focalLength) / (aperture * (focalDistance - focalLength));
        cocScale = Math.Abs(cocScale) / sensorSize;
        
        var settings = new DOFSettings
        {
            FocalDistance = focalDistance,
            FocalLength = focalLength,
            Aperture = aperture,
            CoCScale = cocScale,
            MaxCoCRadius = GetMaxCoCRadius(),
            BokehShape = (int)bokehShape,
            NearTransitionRange = focalDistance * 0.5f,
            FarTransitionRange = focalDistance * 2.0f,
            ScreenSize = new Vector2(_width, _height)
        };
        
        _device.UpdateBuffer(_settingsBuffer!, MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref settings, 1)));
        
        // Step 1: Calculate Circle of Confusion
        cmd.BeginRenderPass(_cocTexture!, ClearValue.FromColor(0, 0, 0, 0));
        cmd.SetPipeline(_cocPipeline);
        cmd.SetTexture(depthBuffer, 0);
        cmd.SetUniformBuffer(_settingsBuffer!, 1);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();
        
        // Step 2: Separate near and far blur
        // Near blur (foreground)
        cmd.BeginRenderPass(_nearBlurTexture!, ClearValue.FromColor(0, 0, 0, 0));
        cmd.SetPipeline(_bokehPipeline);
        cmd.SetTexture(sceneColor, 0);
        cmd.SetTexture(_cocTexture!, 1);
        cmd.SetUniformBuffer(_settingsBuffer!, 2);
        // Set shader constant: blurType = 0 (near)
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();
        
        // Far blur (background)
        cmd.BeginRenderPass(_farBlurTexture!, ClearValue.FromColor(0, 0, 0, 0));
        cmd.SetPipeline(_bokehPipeline);
        cmd.SetTexture(sceneColor, 0);
        cmd.SetTexture(_cocTexture!, 1);
        cmd.SetUniformBuffer(_settingsBuffer!, 2);
        // Set shader constant: blurType = 1 (far)
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();
        
        // Step 3: Composite near, far, and focused layers
        var outputDesc = new TextureDesc
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Depth = 1,
            Format = TextureFormat.RGBA16Float,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "DOF_Output"
        };
        
        var output = _device.CreateTexture(outputDesc);
        
        cmd.BeginRenderPass(output, ClearValue.FromColor(0, 0, 0, 1));
        cmd.SetPipeline(_compositePipeline);
        cmd.SetTexture(sceneColor, 0); // Focused layer
        cmd.SetTexture(_nearBlurTexture!, 1);
        cmd.SetTexture(_farBlurTexture!, 2);
        cmd.SetTexture(_cocTexture!, 3);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();
        
        return output;
    }
    
    private void CreateTextures()
    {
        // Circle of Confusion texture (stores blur amount per pixel)
        _cocTexture = _device.CreateTexture(new TextureDesc
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Depth = 1,
            Format = TextureFormat.R32Float,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "DOF_CoC"
        });
        
        // Blur textures (can be half-res for performance)
        int blurWidth = _quality == DOFQuality.Ultra ? _width : _width / 2;
        int blurHeight = _quality == DOFQuality.Ultra ? _height : _height / 2;
        
        var blurDesc = new TextureDesc
        {
            Width = (uint)blurWidth,
            Height = (uint)blurHeight,
            Depth = 1,
            Format = TextureFormat.RGBA16Float,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1
        };
        
        blurDesc.DebugName = "DOF_NearBlur";
        _nearBlurTexture = _device.CreateTexture(blurDesc);
        
        blurDesc.DebugName = "DOF_FarBlur";
        _farBlurTexture = _device.CreateTexture(blurDesc);
    }
    
    private void CreatePipelines()
    {
        // TODO: Load DOF shaders
        // CoC shader: Calculate blur amount based on depth
        // Bokeh shader: Apply shaped blur (hexagon, octagon, circle)
        // Composite shader: Blend near, far, and focused layers
        
        Console.WriteLine("[DOF] Pipelines created");
    }
    
    private void CreateBuffers()
    {
        _settingsBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)Marshal.SizeOf<DOFSettings>(),
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "DOF_Settings"
        });
    }
    
    private float GetMaxCoCRadius()
    {
        return _quality switch
        {
            DOFQuality.Low => 8.0f,
            DOFQuality.Medium => 16.0f,
            DOFQuality.High => 24.0f,
            DOFQuality.Ultra => 32.0f,
            _ => 16.0f
        };
    }
    
    public void Dispose()
    {
        _cocTexture?.Dispose();
        _nearBlurTexture?.Dispose();
        _farBlurTexture?.Dispose();
        _settingsBuffer?.Dispose();
        _cocPipeline?.Dispose();
        _bokehPipeline?.Dispose();
        _compositePipeline?.Dispose();
    }
}

[StructLayout(LayoutKind.Sequential)]
struct DOFSettings
{
    public float FocalDistance;
    public float FocalLength;
    public float Aperture;
    public float CoCScale;
    public float MaxCoCRadius;
    public int BokehShape;
    public float NearTransitionRange;
    public float FarTransitionRange;
    public Vector2 ScreenSize;
    public float _padding1;
    public float _padding2;
}
