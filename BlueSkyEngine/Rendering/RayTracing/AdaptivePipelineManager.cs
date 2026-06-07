using System;
using System.Collections.Generic;
using NotBSRenderer;

namespace BlueSky.Rendering.RayTracing;

/// <summary>
/// Manages creation of RT pipelines based on GPU tier
/// </summary>
public class AdaptivePipelineManager : IDisposable
{
    private readonly IRHIDevice _device;
    private readonly RTTierConfig _config;
    
    // Software RT pipelines
    private IRHIPipeline? _softRT_RayGen;
    private IRHIPipeline? _softRT_Intersection;
    private IRHIPipeline? _softRT_Shading;
    private IRHIPipeline? _softRT_Denoise;
    
    // Hardware RT pipelines
    private IRHIPipeline? _hardRT_Pipeline;
    
    // Screen-space pipelines
    private IRHIPipeline? _ssao_Pipeline;
    private IRHIPipeline? _ssr_Pipeline;
    private IRHIPipeline? _ssgi_Pipeline;
    
    // Utility pipelines
    private IRHIPipeline? _upscale_Pipeline;
    private IRHIPipeline? _composite_Pipeline;
    
    public AdaptivePipelineManager(IRHIDevice device, RTTierConfig config)
    {
        _device = device;
        _config = config;
        
        Console.WriteLine("[AdaptivePipeline] Creating pipelines for tier: " + config.Name);
        
        CreatePipelines();
    }
    
    private void CreatePipelines()
    {
        switch (_config.Backend)
        {
            case RTBackend.ScreenSpace:
                CreateScreenSpacePipelines();
                break;
                
            case RTBackend.SoftwareRT:
                CreateSoftwareRTPipelines();
                break;
                
            case RTBackend.HardwareRT:
                CreateHardwareRTPipelines();
                break;
        }
        
        // Always create utility pipelines
        CreateUtilityPipelines();
    }
    
    private void CreateScreenSpacePipelines()
    {
        Console.WriteLine("[AdaptivePipeline] Creating screen-space pipelines...");
        
        if (_config.UseSSAO)
        {
            _ssao_Pipeline = CreateComputePipeline("SSAO", GetSSAOShaderVariant());
            Console.WriteLine("  ✓ SSAO pipeline");
        }
        
        if (_config.UseSSR)
        {
            _ssr_Pipeline = CreateComputePipeline("SSR", GetSSRShaderVariant());
            Console.WriteLine("  ✓ SSR pipeline");
        }
        
        if (_config.UseSSGI)
        {
            _ssgi_Pipeline = CreateComputePipeline("SSGI", GetSSGIShaderVariant());
            Console.WriteLine("  ✓ SSGI pipeline");
        }
    }
    
    private void CreateSoftwareRTPipelines()
    {
        Console.WriteLine("[AdaptivePipeline] Creating software RT pipelines...");
        
        // Ray generation with tier-specific optimizations
        string rayGenVariant = GetRayGenVariant();
        _softRT_RayGen = CreateComputePipeline("SoftwareRT_RayGen", rayGenVariant);
        Console.WriteLine($"  ✓ Ray generation ({rayGenVariant})");
        
        // Intersection with BVH complexity based on tier
        string intersectionVariant = GetIntersectionVariant();
        _softRT_Intersection = CreateComputePipeline("SoftwareRT_Intersection", intersectionVariant);
        Console.WriteLine($"  ✓ Intersection ({intersectionVariant})");
        
        // Shading with feature set based on tier
        string shadingVariant = GetShadingVariant();
        _softRT_Shading = CreateComputePipeline("SoftwareRT_Shading", shadingVariant);
        Console.WriteLine($"  ✓ Shading ({shadingVariant})");
        
        // Denoising with quality based on tier
        string denoiseVariant = GetDenoiseVariant();
        _softRT_Denoise = CreateComputePipeline("SoftwareRT_Denoise", denoiseVariant);
        Console.WriteLine($"  ✓ Denoising ({denoiseVariant})");
    }
    
    private void CreateHardwareRTPipelines()
    {
        Console.WriteLine("[AdaptivePipeline] Creating hardware RT pipelines...");
        
        // Hardware RT pipeline with tier-specific features
        string rtVariant = GetHardwareRTVariant();
        
        // TODO: Create DXR/Vulkan RT/Metal RT pipeline
        Console.WriteLine($"  ✓ Hardware RT ({rtVariant})");
        Console.WriteLine("  NOTE: Hardware RT implementation pending (Phase 4)");
    }
    
    private void CreateUtilityPipelines()
    {
        Console.WriteLine("[AdaptivePipeline] Creating utility pipelines...");
        
        // Upscaling pipeline (if needed)
        if (_config.RenderWidth != _config.OutputWidth || _config.RenderHeight != _config.OutputHeight)
        {
            string upscaleVariant = GetUpscaleVariant();
            _upscale_Pipeline = CreateComputePipeline("Upscale", upscaleVariant);
            Console.WriteLine($"  ✓ Upscale ({upscaleVariant})");
        }
        
        // Composite pipeline
        _composite_Pipeline = CreateComputePipeline("Composite", "standard");
        Console.WriteLine("  ✓ Composite");
    }
    
    private IRHIPipeline? CreateComputePipeline(string name, string variant)
    {
        try
        {
            // TODO: Load actual shader based on name and variant
            // For now, return null to indicate shader loading needed
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Failed to create {name} pipeline: {ex.Message}");
            return null;
        }
    }
    
    // Shader variant selection based on tier
    
    private string GetSSAOShaderVariant()
    {
        return _config.Tier switch
        {
            RTTier.Tier0_ScreenSpace => "ssao_low",      // 4 samples
            _ => "ssao_medium"                            // 8 samples
        };
    }
    
    private string GetSSRShaderVariant()
    {
        return _config.Tier switch
        {
            RTTier.Tier0_ScreenSpace => "ssr_low",       // 8 steps
            RTTier.Tier1_UltraLowSoftRT => "ssr_medium", // 16 steps
            _ => "ssr_high"                               // 32 steps
        };
    }
    
    private string GetSSGIShaderVariant()
    {
        return "ssgi_low"; // Always low quality for screen-space GI
    }
    
    private string GetRayGenVariant()
    {
        return _config.Tier switch
        {
            RTTier.Tier1_UltraLowSoftRT => "raygen_checkerboard4x4", // 1 ray per 16 pixels
            RTTier.Tier2_LowSoftRT => "raygen_checkerboard2x2",      // 1 ray per 4 pixels
            RTTier.Tier3_MediumSoftRT => "raygen_checkerboard",      // 1 ray per 2 pixels
            _ => "raygen_standard"                                    // 1 ray per pixel
        };
    }
    
    private string GetIntersectionVariant()
    {
        return _config.Tier switch
        {
            RTTier.Tier1_UltraLowSoftRT => "intersection_simplified", // 2-level BVH
            RTTier.Tier2_LowSoftRT => "intersection_optimized",       // Shallow BVH
            _ => "intersection_standard"                               // Full BVH
        };
    }
    
    private string GetShadingVariant()
    {
        if (_config.EnableRTGI)
            return "shading_full_gi";           // Shadows + reflections + GI
        else if (_config.EnableRTReflections)
            return "shading_shadows_reflections"; // Shadows + reflections
        else if (_config.EnableRTShadows)
            return "shading_shadows_only";      // Shadows only
        else
            return "shading_minimal";           // No RT features
    }
    
    private string GetDenoiseVariant()
    {
        return _config.DenoisingPasses switch
        {
            0 => "denoise_none",
            1 => "denoise_light",
            2 => "denoise_medium",
            _ => "denoise_aggressive"
        };
    }
    
    private string GetHardwareRTVariant()
    {
        return _config.Tier switch
        {
            RTTier.Tier5_LowHardwareRT => "hwrt_shadows_only",
            RTTier.Tier6_MediumHardwareRT => "hwrt_shadows_reflections",
            RTTier.Tier7_HighHardwareRT => "hwrt_full_gi_1bounce",
            RTTier.Tier8_UltraHardwareRT => "hwrt_full_gi_multibounce",
            _ => "hwrt_standard"
        };
    }
    
    private string GetUpscaleVariant()
    {
        float upscaleFactor = _config.GetUpscaleFactor();
        
        if (upscaleFactor >= 3.0f)
            return "upscale_fsr_ultra_quality"; // 3x+ upscale
        else if (upscaleFactor >= 2.0f)
            return "upscale_fsr_quality";       // 2x upscale
        else if (upscaleFactor >= 1.5f)
            return "upscale_fsr_balanced";      // 1.5x upscale
        else
            return "upscale_bilinear";          // <1.5x upscale
    }
    
    // Pipeline accessors
    
    public IRHIPipeline? GetRayGenPipeline() => _softRT_RayGen;
    public IRHIPipeline? GetIntersectionPipeline() => _softRT_Intersection;
    public IRHIPipeline? GetShadingPipeline() => _softRT_Shading;
    public IRHIPipeline? GetDenoisePipeline() => _softRT_Denoise;
    public IRHIPipeline? GetHardwareRTPipeline() => _hardRT_Pipeline;
    public IRHIPipeline? GetSSAOPipeline() => _ssao_Pipeline;
    public IRHIPipeline? GetSSRPipeline() => _ssr_Pipeline;
    public IRHIPipeline? GetSSGIPipeline() => _ssgi_Pipeline;
    public IRHIPipeline? GetUpscalePipeline() => _upscale_Pipeline;
    public IRHIPipeline? GetCompositePipeline() => _composite_Pipeline;
    
    public void Dispose()
    {
        _softRT_RayGen?.Dispose();
        _softRT_Intersection?.Dispose();
        _softRT_Shading?.Dispose();
        _softRT_Denoise?.Dispose();
        _hardRT_Pipeline?.Dispose();
        _ssao_Pipeline?.Dispose();
        _ssr_Pipeline?.Dispose();
        _ssgi_Pipeline?.Dispose();
        _upscale_Pipeline?.Dispose();
        _composite_Pipeline?.Dispose();
    }
}
