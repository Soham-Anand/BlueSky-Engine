using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NotBSRenderer;
using BlueSky.Platform;

namespace BlueSky.Rendering.EasePlus;

/// <summary>
/// Ease+ Memory Manager — Centralized render target and buffer pooling.
///
/// On Intel HD 3000 (shared memory), every byte of VRAM is precious.
/// This manager pre-allocates all render targets at startup and recycles
/// them across frames, eliminating runtime allocation jitter.
///
/// Memory Budget (720p):
///   G-Buffer Normal:    1280×720 × 4B  = 3.5 MB (RGBA8)
///   Depth:              1280×720 × 4B  = 3.5 MB (D32F)
///   Light Buffer:        640×360 × 8B  = 1.8 MB (RGBA16F, half-res)
///   SDF Volume:           64³   × 4B  = 1.0 MB (R32F)
///   SH Probes:           8×4×8  × 48B = 12 KB  (9 coefficients × RGB)
///   Post-FX Target:     1280×720 × 4B  = 3.5 MB (RGBA8)
///   ─────────────────────────────────────────────
///   Total:                              ≈ 13.3 MB
/// </summary>
public class EasePlusMemoryManager : IDisposable
{
    private readonly IRHIDevice _device;
    
    // ── G-Buffer ─────────────────────────────────────────────────────────
    /// <summary>RT0: View-space normal (RG) + roughness (B) + metallic (A)</summary>
    public IRHITexture? GBufferNormal { get; private set; }
    /// <summary>Hardware depth buffer, also sampled in light pass</summary>
    public IRHITexture? GBufferDepth { get; private set; }
    
    // ── Light Buffer (half resolution) ───────────────────────────────────
    /// <summary>Half-res RGBA16F: RGB = accumulated light, A = specular intensity</summary>
    public IRHITexture? LightBuffer { get; private set; }
    /// <summary>Half-res depth for bilateral upsample</summary>
    public IRHITexture? LightDepth { get; private set; }
    
    // ── SDF Volume ───────────────────────────────────────────────────────
    /// <summary>3D distance field texture for SDF raymarching reflections</summary>
    public IRHITexture? SDFVolume { get; private set; }
    /// <summary>SDF reflection result (quarter-res, temporally accumulated)</summary>
    public IRHITexture? SDFReflectionBuffer { get; private set; }
    public IRHITexture? SDFReflectionHistory { get; private set; }
    
    // ── Post-FX ──────────────────────────────────────────────────────────
    public IRHITexture? PostFXTarget { get; private set; }
    public IRHITexture? SceneColorTarget { get; private set; }
    
    // ── Uniform Buffers ──────────────────────────────────────────────────
    /// <summary>Per-frame view/projection/camera data</summary>
    public IRHIBuffer? ViewUniformBuffer { get; private set; }
    /// <summary>Per-object model matrix + material params</summary>
    public IRHIBuffer? ObjectUniformBuffer { get; private set; }
    /// <summary>Tile light index list (uploaded by CPU culler each frame)</summary>
    public IRHIBuffer? TileLightBuffer { get; private set; }
    /// <summary>Light data array (position, color, range, etc.)</summary>
    public IRHIBuffer? LightDataBuffer { get; private set; }
    /// <summary>SH probe coefficients</summary>
    public IRHIBuffer? SHProbeBuffer { get; private set; }
    /// <summary>Material data for forward pass</summary>
    public IRHIBuffer? MaterialBuffer { get; private set; }
    /// <summary>Uniforms for PostFX pass</summary>
    public IRHIBuffer? PostFXUniformBuffer { get; private set; }
    
    // ── Dimensions ───────────────────────────────────────────────────────
    public uint FullWidth { get; private set; }
    public uint FullHeight { get; private set; }
    public uint HalfWidth { get; private set; }
    public uint HalfHeight { get; private set; }
    public uint QuarterWidth { get; private set; }
    public uint QuarterHeight { get; private set; }
    public uint SDFResolution { get; private set; } = 64;
    public uint LightingResolutionDivisor { get; private set; } = 2;
    
    private bool _disposed;
    
    public EasePlusMemoryManager(IRHIDevice device)
    {
        _device = device;
    }

    public void ConfigureLightingResolution(uint divisor)
    {
        LightingResolutionDivisor = Math.Clamp(divisor, 2u, 4u);
    }
    
    /// <summary>
    /// Allocate all render targets for the given resolution.
    /// Call on startup and on window resize.
    /// </summary>
    public void Allocate(uint width, uint height)
    {
        // Release previous allocations
        ReleaseRenderTargets();
        
        FullWidth = Math.Max(width, 1);
        FullHeight = Math.Max(height, 1);
        HalfWidth = Math.Max(FullWidth / LightingResolutionDivisor, 1);
        HalfHeight = Math.Max(FullHeight / LightingResolutionDivisor, 1);
        QuarterWidth = Math.Max(FullWidth / 4, 1);
        QuarterHeight = Math.Max(FullHeight / 4, 1);
        
        Console.WriteLine($"[Ease+Memory] Allocating render targets: {FullWidth}×{FullHeight}");
        Console.WriteLine($"  Light-res: {HalfWidth}×{HalfHeight} (1/{LightingResolutionDivisor})");
        Console.WriteLine($"  Quarter-res: {QuarterWidth}×{QuarterHeight}");
        Console.WriteLine($"  SDF Volume: {SDFResolution}³");
        
        // ── G-Buffer ─────────────────────────────────────────────────────
        GBufferNormal = _device.CreateTexture(new TextureDesc
        {
            Width = FullWidth, Height = FullHeight, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            DebugName = "Ease+.GBuffer.Normal"
        });
        
        GBufferDepth = _device.CreateTexture(new TextureDesc
        {
            Width = FullWidth, Height = FullHeight, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.Depth32Float,
            Usage = TextureUsage.DepthStencil | TextureUsage.Sampled,
            DebugName = "Ease+.GBuffer.Depth"
        });
        
        // ── Light Buffer (half-res for bandwidth savings) ────────────────
        LightBuffer = _device.CreateTexture(new TextureDesc
        {
            Width = HalfWidth, Height = HalfHeight, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm, // Switched from RGBA16F to RGBA8Unorm for bandwidth
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            DebugName = "Ease+.LightBuffer"
        });
        
        LightDepth = _device.CreateTexture(new TextureDesc
        {
            Width = HalfWidth, Height = HalfHeight, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.Depth32Float,
            Usage = TextureUsage.DepthStencil | TextureUsage.Sampled,
            DebugName = "Ease+.LightDepth"
        });
        
        // ── SDF Reflection (quarter-res + temporal) ──────────────────────
        SDFReflectionBuffer = _device.CreateTexture(new TextureDesc
        {
            Width = QuarterWidth, Height = QuarterHeight, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            DebugName = "Ease+.SDF.Reflection"
        });
        
        SDFReflectionHistory = _device.CreateTexture(new TextureDesc
        {
            Width = QuarterWidth, Height = QuarterHeight, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            DebugName = "Ease+.SDF.ReflectionHistory"
        });
        
        // ── Post-FX Target ───────────────────────────────────────────────
        PostFXTarget = _device.CreateTexture(new TextureDesc
        {
            Width = FullWidth, Height = FullHeight, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            DebugName = "Ease+.PostFX"
        });

        SceneColorTarget = _device.CreateTexture(new TextureDesc
        {
            Width = FullWidth, Height = FullHeight, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            DebugName = "Ease+.SceneColor"
        });
        
        // ── Uniform Buffers ──────────────────────────────────────────────
        AllocateUniformBuffers();
        
        uint totalBytes = EstimateMemoryUsage();
        Console.WriteLine($"[Ease+Memory] ✓ Allocated ~{totalBytes / (1024 * 1024.0):F1} MB VRAM");
    }
    
    private void AllocateUniformBuffers()
    {
        ViewUniformBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = 512, // EasePlusViewUniforms: 4×Matrix4x4 + camera/sun data ≈ 320B + padding
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "Ease+.ViewUB"
        });
        
        ObjectUniformBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = 128, // Model matrix + color
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "Ease+.ObjectUB"
        });
        
        // Tile light buffer: max 80×45 tiles × 8 ints (32 bytes) = 112 KB
        // (for 1280×720 with 16×16 tiles)
        uint maxTilesX = (FullWidth + 15) / 16;
        uint maxTilesY = (FullHeight + 15) / 16;
        uint tileCount = maxTilesX * maxTilesY;
        TileLightBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = Math.Max(tileCount * 32, 4096), // 8 ints per tile (LightCount + 7 indices)
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "Ease+.TileLightUB"
        });
        
        // Light data: max 128 lights × 64 bytes each
        LightDataBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = 128 * 64,
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "Ease+.LightDataUB"
        });
        
        // SH Probe data: 256 probes × 144 bytes (9 SH coefficients × RGB as Vector4 = 9 * 16 = 144)
        SHProbeBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = 256 * 144,
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "Ease+.SHProbeUB"
        });
        
        MaterialBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = 64, // albedo + metallic + roughness + AO + emission
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "Ease+.MaterialUB"
        });

        PostFXUniformBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = 32, // EasePlusPostFXUniforms
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "Ease+.PostFXUB"
        });
    }
    
    private uint EstimateMemoryUsage()
    {
        uint total = 0;
        total += FullWidth * FullHeight * 4;          // GBuffer Normal (RGBA8)
        total += FullWidth * FullHeight * 4;          // GBuffer Depth (D32F)
        total += HalfWidth * HalfHeight * 4;          // Light Buffer (RGBA8)
        total += HalfWidth * HalfHeight * 4;          // Light Depth (D32F)
        total += FullWidth * FullHeight * 4;          // PostFX Target
        total += 128 * 64 + 256 * 48 + 4096;         // Uniform buffers
        return total;
    }
    
    private void ReleaseRenderTargets()
    {
        GBufferNormal?.Dispose();
        GBufferDepth?.Dispose();
        LightBuffer?.Dispose();
        LightDepth?.Dispose();
        SDFVolume?.Dispose();
        SDFReflectionBuffer?.Dispose();
        SDFReflectionHistory?.Dispose();
        PostFXTarget?.Dispose();
        ViewUniformBuffer?.Dispose();
        ObjectUniformBuffer?.Dispose();
        TileLightBuffer?.Dispose();
        LightDataBuffer?.Dispose();
        SHProbeBuffer?.Dispose();
        MaterialBuffer?.Dispose();
        SceneColorTarget?.Dispose();
        PostFXUniformBuffer?.Dispose();
    }
    
    /// <summary>
    /// Swap the SDF reflection buffers (current ↔ history) for temporal accumulation.
    /// </summary>
    public void SwapReflectionBuffers()
    {
        (SDFReflectionBuffer, SDFReflectionHistory) = (SDFReflectionHistory, SDFReflectionBuffer);
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        ReleaseRenderTargets();
        _disposed = true;
        Console.WriteLine("[Ease+Memory] Disposed");
    }
}
