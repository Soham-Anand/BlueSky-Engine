using System;
using System.Numerics;
using BlueSky.Core.Platform.Detection;

namespace BlueSky.Rendering.RayTracing;

public enum RTTier
{
    Tier0_ScreenSpace,      // Fallback: no RT capability at all
    Tier0P_Polaris,         // ★ PROJECT POLARIS: AVX CPU RT on Intel HD 3000 (2011)
    Tier1_UltraLowSoftRT,   // Intel HD 4000 (2012)
    Tier2_LowSoftRT,        // GTX 1050, RX 560 (2016)
    Tier3_MediumSoftRT,     // GTX 1060, RX 580 (2016)
    Tier4_HighSoftRT,       // GTX 1070, RX 5700 XT (2016-2019)
    Tier5_LowHardwareRT,    // RTX 2060, RX 6600 XT (2019-2021)
    Tier6_MediumHardwareRT, // RTX 3060, RX 6700 XT (2021)
    Tier7_HighHardwareRT,   // RTX 3080, RX 6900 XT (2020)
    Tier8_UltraHardwareRT   // RTX 4090, RX 7900 XTX (2022+)
}

/// <summary>
/// Intelligent RT tier selector based on GPU capabilities
/// </summary>
public static class RTTierSelector
{
    public static RTTier SelectTier(GpuCapabilities gpu, bool hasHardwareRT, float computeScore, bool supportsAvx)
    {
        // Hardware RT path
        if (hasHardwareRT)
        {
            return gpu.Tier switch
            {
                GpuTier.High => RTTier.Tier8_UltraHardwareRT,
                GpuTier.Mid => RTTier.Tier6_MediumHardwareRT,
                GpuTier.Low => RTTier.Tier5_LowHardwareRT,
                _ => RTTier.Tier5_LowHardwareRT
            };
        }
        
        // Software RT path - EXTREME optimization for low-end
        if (computeScore >= 400) // GTX 1070+
            return RTTier.Tier4_HighSoftRT;
        else if (computeScore >= 250) // GTX 1060
            return RTTier.Tier3_MediumSoftRT;
        else if (computeScore >= 150) // GTX 1050
            return RTTier.Tier2_LowSoftRT;
        else if (computeScore >= 40) // Intel HD 4000 - EXTREME mode
            return RTTier.Tier1_UltraLowSoftRT;
        else if (supportsAvx) // Intel HD 3000 + AVX = POLARIS
            return RTTier.Tier0P_Polaris;
        else // Pre-Sandy Bridge or no AVX - screen-space only
            return RTTier.Tier0_ScreenSpace;
    }
    
    public static RTTierConfig GetConfig(RTTier tier, int targetWidth = 1920, int targetHeight = 1080)
    {
        return tier switch
        {
            RTTier.Tier0_ScreenSpace => new RTTierConfig
            {
                Tier = tier,
                Name = "Screen-Space Ultra-Optimized",
                Backend = RTBackend.ScreenSpace,
                RenderWidth = targetWidth / 2,
                RenderHeight = targetHeight / 2,
                OutputWidth = targetWidth,
                OutputHeight = targetHeight,
                RaysPerPixel = 0.0f,
                MaxBounces = 0,
                TemporalSamples = 1,
                UseSSAO = true,
                UseSSR = false,
                UseSSGI = false,
                EnableRTShadows = false,
                EnableRTReflections = false,
                EnableRTGI = false,
                EnableRTAO = false,
                EstimatedFPS = 120,
                VisualQuality = 35,
                UseCheckerboard = true,
                CheckerboardSize = 2,
                UseBVHSimplification = false,
                BVHMaxDepth = 0,
                DenoisingPasses = 0
            },
            
            // ★ PROJECT POLARIS: AVX CPU Ray Tracing for Intel HD 3000
            // 320×180 internal → GPU upscale → 1280×720 output
            // 60 FPS target on i5-2410M with checkerboard + temporal accumulation
            RTTier.Tier0P_Polaris => new RTTierConfig
            {
                Tier = tier,
                Name = "★ Project Polaris (AVX CPU RT)",
                Backend = RTBackend.Polaris,
                RenderWidth = 320,
                RenderHeight = 180,
                OutputWidth = Math.Min(targetWidth, 1280),
                OutputHeight = Math.Min(targetHeight, 720),
                RaysPerPixel = 0.5f, // checkerboard = half pixels
                MaxBounces = 1,      // 1-bounce (shadow ray)
                TemporalSamples = 16,
                UseSSAO = false,
                UseSSR = false,
                UseSSGI = false,
                EnableRTShadows = true,  // ✓ real ray-traced shadows!
                EnableRTReflections = false,
                EnableRTGI = false,
                EnableRTAO = false,
                EstimatedFPS = 60,
                VisualQuality = 55,
                UseCheckerboard = true,
                CheckerboardSize = 2,
                UseBVHSimplification = false,
                BVHMaxDepth = 32,
                DenoisingPasses = 0 // temporal accumulator handles this
            },
            
            RTTier.Tier1_UltraLowSoftRT => new RTTierConfig
            {
                Tier = tier,
                Name = "Ultra-Low Software RT EXTREME",
                Backend = RTBackend.SoftwareRT,
                RenderWidth = 213,
                RenderHeight = 120,
                OutputWidth = targetWidth,
                OutputHeight = targetHeight,
                RaysPerPixel = 0.015625f,
                MaxBounces = 1,
                TemporalSamples = 64,
                UseSSAO = false,
                UseSSR = false,
                UseSSGI = false,
                EnableRTShadows = true,
                EnableRTReflections = false,
                EnableRTGI = false,
                EnableRTAO = false,
                EstimatedFPS = 60,
                VisualQuality = 45,
                UseCheckerboard = true,
                CheckerboardSize = 8,
                UseBVHSimplification = true,
                BVHMaxDepth = 6,
                DenoisingPasses = 4
            },
            
            RTTier.Tier2_LowSoftRT => new RTTierConfig
            {
                Tier = tier,
                Name = "Low Software RT",
                Backend = RTBackend.SoftwareRT,
                
                // 360p → 1080p
                RenderWidth = 640,
                RenderHeight = 360,
                OutputWidth = targetWidth,
                OutputHeight = targetHeight,
                
                // 1 ray per 4 pixels
                RaysPerPixel = 0.25f,
                MaxBounces = 1,
                TemporalSamples = 16,
                
                // Screen-space fallbacks
                UseSSAO = true,
                UseSSR = true,
                UseSSGI = false,
                
                // RT shadows only
                EnableRTShadows = true,
                EnableRTReflections = false,
                EnableRTGI = false,
                EnableRTAO = false,
                
                // Performance
                EstimatedFPS = 40,
                VisualQuality = 60,
                
                // Optimizations
                UseCheckerboard = true,
                CheckerboardSize = 2, // 2x2 checkerboard
                UseBVHSimplification = false,
                BVHMaxDepth = 16,
                DenoisingPasses = 2
            },
            
            RTTier.Tier3_MediumSoftRT => new RTTierConfig
            {
                Tier = tier,
                Name = "Medium Software RT",
                Backend = RTBackend.SoftwareRT,
                
                // 540p → 1080p
                RenderWidth = 960,
                RenderHeight = 540,
                OutputWidth = targetWidth,
                OutputHeight = targetHeight,
                
                // 1 ray per 2 pixels (checkerboard)
                RaysPerPixel = 0.5f,
                MaxBounces = 1,
                TemporalSamples = 8,
                
                // Reduced screen-space
                UseSSAO = true,
                UseSSR = false,
                UseSSGI = false,
                
                // RT shadows only
                EnableRTShadows = true,
                EnableRTReflections = false,
                EnableRTGI = false,
                EnableRTAO = false,
                
                // Performance
                EstimatedFPS = 45,
                VisualQuality = 70,
                
                // Optimizations
                UseCheckerboard = true,
                CheckerboardSize = 2,
                UseBVHSimplification = false,
                BVHMaxDepth = 20,
                DenoisingPasses = 2
            },
            
            RTTier.Tier4_HighSoftRT => new RTTierConfig
            {
                Tier = tier,
                Name = "High Software RT",
                Backend = RTBackend.SoftwareRT,
                
                // 720p → 1080p
                RenderWidth = 1280,
                RenderHeight = 720,
                OutputWidth = targetWidth,
                OutputHeight = targetHeight,
                
                // 1 ray per pixel
                RaysPerPixel = 1.0f,
                MaxBounces = 1,
                TemporalSamples = 4,
                
                // Minimal screen-space
                UseSSAO = false,
                UseSSR = false,
                UseSSGI = false,
                
                // RT shadows + reflections
                EnableRTShadows = true,
                EnableRTReflections = true,
                EnableRTGI = false,
                EnableRTAO = false,
                
                // Performance
                EstimatedFPS = 50,
                VisualQuality = 80,
                
                // Optimizations
                UseCheckerboard = false,
                CheckerboardSize = 1,
                UseBVHSimplification = false,
                BVHMaxDepth = 24,
                DenoisingPasses = 1
            },
            
            RTTier.Tier5_LowHardwareRT => new RTTierConfig
            {
                Tier = tier,
                Name = "Low Hardware RT",
                Backend = RTBackend.HardwareRT,
                
                // 720p → 1080p
                RenderWidth = 1280,
                RenderHeight = 720,
                OutputWidth = targetWidth,
                OutputHeight = targetHeight,
                
                // Hardware RT
                RaysPerPixel = 0.5f,
                MaxBounces = 1,
                TemporalSamples = 8,
                
                // No screen-space needed
                UseSSAO = false,
                UseSSR = false,
                UseSSGI = false,
                
                // RT shadows only
                EnableRTShadows = true,
                EnableRTReflections = false,
                EnableRTGI = false,
                EnableRTAO = false,
                
                // Performance
                EstimatedFPS = 60,
                VisualQuality = 75,
                
                // Hardware RT optimizations
                UseCheckerboard = true,
                CheckerboardSize = 2,
                UseBVHSimplification = false,
                BVHMaxDepth = 32,
                DenoisingPasses = 2
            },
            
            RTTier.Tier6_MediumHardwareRT => new RTTierConfig
            {
                Tier = tier,
                Name = "Medium Hardware RT",
                Backend = RTBackend.HardwareRT,
                
                // 1080p native
                RenderWidth = targetWidth,
                RenderHeight = targetHeight,
                OutputWidth = targetWidth,
                OutputHeight = targetHeight,
                
                // Hardware RT
                RaysPerPixel = 1.0f,
                MaxBounces = 1,
                TemporalSamples = 4,
                
                // No screen-space
                UseSSAO = false,
                UseSSR = false,
                UseSSGI = false,
                
                // RT shadows + reflections
                EnableRTShadows = true,
                EnableRTReflections = true,
                EnableRTGI = false,
                EnableRTAO = false,
                
                // Performance
                EstimatedFPS = 60,
                VisualQuality = 85,
                
                // Optimizations
                UseCheckerboard = false,
                CheckerboardSize = 1,
                UseBVHSimplification = false,
                BVHMaxDepth = 32,
                DenoisingPasses = 1
            },
            
            RTTier.Tier7_HighHardwareRT => new RTTierConfig
            {
                Tier = tier,
                Name = "High Hardware RT",
                Backend = RTBackend.HardwareRT,
                
                // 1080p native
                RenderWidth = targetWidth,
                RenderHeight = targetHeight,
                OutputWidth = targetWidth,
                OutputHeight = targetHeight,
                
                // Hardware RT
                RaysPerPixel = 2.0f,
                MaxBounces = 2, // 1-bounce GI
                TemporalSamples = 2,
                
                // No screen-space
                UseSSAO = false,
                UseSSR = false,
                UseSSGI = false,
                
                // Full RT
                EnableRTShadows = true,
                EnableRTReflections = true,
                EnableRTGI = true,
                EnableRTAO = false,
                
                // Performance
                EstimatedFPS = 75,
                VisualQuality = 95,
                
                // Optimizations
                UseCheckerboard = false,
                CheckerboardSize = 1,
                UseBVHSimplification = false,
                BVHMaxDepth = 32,
                DenoisingPasses = 1
            },
            
            RTTier.Tier8_UltraHardwareRT => new RTTierConfig
            {
                Tier = tier,
                Name = "Ultra Hardware RT",
                Backend = RTBackend.HardwareRT,
                
                // 1440p or 4K native
                RenderWidth = Math.Min(targetWidth, 2560),
                RenderHeight = Math.Min(targetHeight, 1440),
                OutputWidth = targetWidth,
                OutputHeight = targetHeight,
                
                // Hardware RT
                RaysPerPixel = 4.0f,
                MaxBounces = 3, // Multi-bounce GI
                TemporalSamples = 1,
                
                // No screen-space
                UseSSAO = false,
                UseSSR = false,
                UseSSGI = false,
                
                // Full RT + extras
                EnableRTShadows = true,
                EnableRTReflections = true,
                EnableRTGI = true,
                EnableRTAO = true,
                
                // Performance
                EstimatedFPS = 90,
                VisualQuality = 100,
                
                // Minimal optimizations
                UseCheckerboard = false,
                CheckerboardSize = 1,
                UseBVHSimplification = false,
                BVHMaxDepth = 32,
                DenoisingPasses = 0
            },
            
            _ => throw new ArgumentException($"Unknown tier: {tier}")
        };
    }
}

/// <summary>
/// Complete configuration for an RT tier
/// </summary>
public struct RTTierConfig
{
    // Tier info
    public RTTier Tier;
    public string Name;
    public RTBackend Backend;
    
    // Resolution
    public int RenderWidth;
    public int RenderHeight;
    public int OutputWidth;
    public int OutputHeight;
    
    // Ray tracing
    public float RaysPerPixel;
    public int MaxBounces;
    public int TemporalSamples;
    
    // Screen-space fallbacks
    public bool UseSSAO;
    public bool UseSSR;
    public bool UseSSGI;
    
    // RT features
    public bool EnableRTShadows;
    public bool EnableRTReflections;
    public bool EnableRTGI;
    public bool EnableRTAO;
    
    // Performance
    public int EstimatedFPS;
    public int VisualQuality; // 0-100
    
    // Optimizations
    public bool UseCheckerboard;
    public int CheckerboardSize; // 1, 2, or 4
    public bool UseBVHSimplification;
    public int BVHMaxDepth;
    public int DenoisingPasses;
    
    public float GetUpscaleFactor()
    {
        if (RenderWidth == 0 || OutputWidth == 0) return 1.0f;
        return (float)OutputWidth / RenderWidth;
    }
    
    public int GetTotalPixels() => RenderWidth * RenderHeight;
    public int GetEffectiveRayCount() => (int)(GetTotalPixels() * RaysPerPixel);
}
