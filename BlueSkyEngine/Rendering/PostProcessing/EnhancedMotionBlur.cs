using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NotBSRenderer;

namespace BlueSky.Rendering.PostProcessing;

/// <summary>
/// Enhanced Motion Blur - Tile-based per-object motion blur
/// Uses velocity buffer and calculates dominant velocity per tile.
/// </summary>
public class EnhancedMotionBlur : IDisposable
{
    private readonly IRHIDevice _device;
    
    private IRHIPipeline? _tileMaxPipeline;
    private IRHIPipeline? _neighborMaxPipeline;
    private IRHIPipeline? _blurPipeline;
    
    private IRHIBuffer? _settingsBuffer;
    
    private IRHITexture? _tileMaxTexture;
    private IRHITexture? _neighborMaxTexture;
    
    private int _width;
    private int _height;
    
    [StructLayout(LayoutKind.Sequential)]
    private struct MotionBlurSettings
    {
        public float VelocityScale;
        public int MaxSamples;
        public Vector2 _padding;
    }
    
    public EnhancedMotionBlur(IRHIDevice device)
    {
        _device = device;
    }
    
    public void Initialize(int width, int height)
    {
        _width = width;
        _height = height;
        
        CreatePipelines();
        CreateBuffers();
        CreateIntermediateTextures();
        
        Console.WriteLine("[EnhancedMotionBlur] Initialized");
    }
    
    public IRHITexture Apply(IRHICommandBuffer cmd, IRHITexture sceneColor, IRHITexture velocityBuffer, IRHITexture depthBuffer, float intensity)
    {
        if (_blurPipeline == null || _tileMaxPipeline == null || _neighborMaxPipeline == null || intensity <= 0.001f)
            return sceneColor;
            
        var settings = new MotionBlurSettings
        {
            VelocityScale = intensity,
            MaxSamples = 16 // Configurable quality
        };
        
        _device.UpdateBuffer(_settingsBuffer!, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref settings, 1)));
        
        // 1. TileMax Pass: Downsample velocity buffer (e.g., 20x20 tiles) and find max velocity magnitude
        cmd.BeginRenderPass(_tileMaxTexture!, ClearValue.FromColor(0, 0, 0, 0));
        cmd.SetPipeline(_tileMaxPipeline);
        cmd.SetTexture(velocityBuffer, 0);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();
        
        // 2. NeighborMax Pass: Gather max velocity from 3x3 neighborhood of tiles
        cmd.BeginRenderPass(_neighborMaxTexture!, ClearValue.FromColor(0, 0, 0, 0));
        cmd.SetPipeline(_neighborMaxPipeline);
        cmd.SetTexture(_tileMaxTexture!, 0);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();
        
        // 3. Final Blur Pass: Directional blur based on neighbor max velocity and pixel velocity
        var outputDesc = new TextureDesc
        {
            Width = sceneColor.Width,
            Height = sceneColor.Height,
            Depth = 1,
            Format = sceneColor.Format,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "EnhancedMotionBlur_Output"
        };
        
        var output = _device.CreateTexture(outputDesc);
        
        cmd.BeginRenderPass(output, ClearValue.FromColor(0, 0, 0, 1));
        cmd.SetPipeline(_blurPipeline);
        cmd.SetTexture(sceneColor, 0);
        cmd.SetTexture(velocityBuffer, 1);
        cmd.SetTexture(_neighborMaxTexture!, 2);
        cmd.SetTexture(depthBuffer, 3);
        cmd.SetUniformBuffer(_settingsBuffer!, 4);
        cmd.Draw(3, 1, 0, 0);
        cmd.EndRenderPass();
        
        return output;
    }
    
    private void CreatePipelines()
    {
        // TODO: Load shaders for TileMax, NeighborMax, and Final Blur passes
    }
    
    private void CreateBuffers()
    {
        _settingsBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)Marshal.SizeOf<MotionBlurSettings>(),
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "EnhancedMotionBlur_Settings"
        });
    }
    
    private void CreateIntermediateTextures()
    {
        int tileSize = 20;
        int tileWidth = (_width + tileSize - 1) / tileSize;
        int tileHeight = (_height + tileSize - 1) / tileSize;
        
        var desc = new TextureDesc
        {
            Width = (uint)Math.Max(1, tileWidth),
            Height = (uint)Math.Max(1, tileHeight),
            Depth = 1,
            Format = TextureFormat.RG32Float, // Stores Velocity X,Y
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "MotionBlur_TileMax"
        };
        
        _tileMaxTexture = _device.CreateTexture(desc);
        
        desc.DebugName = "MotionBlur_NeighborMax";
        _neighborMaxTexture = _device.CreateTexture(desc);
    }
    
    public void Dispose()
    {
        _settingsBuffer?.Dispose();
        _tileMaxTexture?.Dispose();
        _neighborMaxTexture?.Dispose();
        
        _tileMaxPipeline?.Dispose();
        _neighborMaxPipeline?.Dispose();
        _blurPipeline?.Dispose();
    }
}
