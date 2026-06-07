using System;
using System.Text;
using BlueSky.Rendering.ForwardPlus;

namespace NotBSRenderer;

/// <summary>
/// Detects and reports RHI capabilities
/// Compares BlueSky Engine features with Frostbite 3 and Unreal Engine 5
/// </summary>
public static class RHICapabilityDetector
{
    /// <summary>
    /// Detect capabilities for a given RHI backend
    /// </summary>
    public static RHICapabilities DetectCapabilities(RHIBackend backend, D3D11FeatureLevel featureLevel = D3D11FeatureLevel.Level_11_0)
    {
        return backend switch
        {
            RHIBackend.DirectX11 => DetectDX11Capabilities(featureLevel),
            RHIBackend.DirectX12 => DetectDX12Capabilities(),
            RHIBackend.Vulkan => DetectVulkanCapabilities(),
            RHIBackend.Metal => DetectMetalCapabilities(),
            _ => RHICapabilities.None
        };
    }
    
    private static RHICapabilities DetectDX11Capabilities(D3D11FeatureLevel featureLevel)
    {
        // Base capabilities for all DX11 feature levels (10.0+)
        var caps = RHICapabilities.IndirectDrawing | 
                   RHICapabilities.GeometryShaders;
        
        // Feature Level 10.1+
        if (featureLevel >= D3D11FeatureLevel.Level_10_1)
        {
            caps |= RHICapabilities.TessellationShaders;
        }
        
        // Feature Level 11.0+ (Full DX11)
        if (featureLevel >= D3D11FeatureLevel.Level_11_0)
        {
            caps |= RHICapabilities.ComputeShaders |
                    RHICapabilities.MultiDrawIndirect;
        }
        
        // Feature Level 11.1+ (Enhanced DX11)
        if (featureLevel >= D3D11FeatureLevel.Level_11_1)
        {
            // UAVs at all stages, logical blend ops
            // Still no bindless (requires DX12)
        }
        
        return caps;
    }
    
    private static RHICapabilities DetectDX12Capabilities()
    {
        // DX12: Full modern feature set
        return RHICapabilities.ComputeShaders |
               RHICapabilities.BindlessResources |
               RHICapabilities.IndirectDrawing |
               RHICapabilities.MultiDrawIndirect |
               RHICapabilities.AsyncCompute |
               RHICapabilities.MeshShaders |
               RHICapabilities.VariableRateShading |
               RHICapabilities.RayTracing;
    }
    
    private static RHICapabilities DetectVulkanCapabilities()
    {
        // Vulkan: Full modern feature set (similar to DX12)
        return RHICapabilities.ComputeShaders |
               RHICapabilities.BindlessResources |
               RHICapabilities.IndirectDrawing |
               RHICapabilities.MultiDrawIndirect |
               RHICapabilities.AsyncCompute |
               RHICapabilities.MeshShaders |
               RHICapabilities.RayTracing;
    }
    
    private static RHICapabilities DetectMetalCapabilities()
    {
        // Metal: Modern feature set (Apple Silicon)
        return RHICapabilities.ComputeShaders |
               RHICapabilities.BindlessResources |
               RHICapabilities.IndirectDrawing |
               RHICapabilities.MultiDrawIndirect |
               RHICapabilities.AsyncCompute |
               RHICapabilities.MeshShaders;
    }
    
    /// <summary>
    /// Get recommended rendering path based on capabilities
    /// </summary>
    public static RenderingPath GetRecommendedPath(RHICapabilities capabilities)
    {
        if (capabilities.HasFlag(RHICapabilities.ComputeShaders) &&
            capabilities.HasFlag(RHICapabilities.BindlessResources))
        {
            return RenderingPath.ForwardPlusBindless;
        }
        else if (capabilities.HasFlag(RHICapabilities.ComputeShaders))
        {
            return RenderingPath.ForwardPlusCompute;
        }
        else
        {
            return RenderingPath.ForwardPlusCPU;
        }
    }
    
    /// <summary>
    /// Generate a detailed capability report comparing with Frostbite 3 and UE5
    /// </summary>
    public static string GenerateCapabilityReport(IRHIDevice device)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=================================================================");
        sb.AppendLine("BlueSky Engine - RHI Capability Report");
        sb.AppendLine("=================================================================");
        sb.AppendLine();
        
        sb.AppendLine($"Backend: {device.Backend}");
        sb.AppendLine($"Binding Mode: {device.BindingMode}");
        sb.AppendLine();
        
        sb.AppendLine("Feature Comparison:");
        sb.AppendLine("-------------------------------------------------------------------");
        sb.AppendLine("Feature                    | BlueSky | Frostbite 3 | Unreal 5");
        sb.AppendLine("-------------------------------------------------------------------");
        
        var caps = device.Capabilities;
        
        // Forward+ / Clustered Rendering
        bool hasForwardPlus = caps.HasFlag(RHICapabilities.ComputeShaders);
        sb.AppendLine($"Forward+ Rendering         | {YesNo(hasForwardPlus),-7} | Yes         | Yes");
        
        // Bindless Resources
        bool hasBindless = caps.HasFlag(RHICapabilities.BindlessResources);
        sb.AppendLine($"Bindless Resources         | {YesNo(hasBindless),-7} | Yes         | Yes");
        
        // Compute Shaders
        bool hasCompute = caps.HasFlag(RHICapabilities.ComputeShaders);
        sb.AppendLine($"Compute Shaders            | {YesNo(hasCompute),-7} | Yes         | Yes");
        
        // Async Compute
        bool hasAsyncCompute = caps.HasFlag(RHICapabilities.AsyncCompute);
        sb.AppendLine($"Async Compute              | {YesNo(hasAsyncCompute),-7} | Yes         | Yes");
        
        // Indirect Drawing
        bool hasIndirect = caps.HasFlag(RHICapabilities.IndirectDrawing);
        sb.AppendLine($"Indirect Drawing           | {YesNo(hasIndirect),-7} | Yes         | Yes");
        
        // Multi-Draw Indirect
        bool hasMultiDraw = caps.HasFlag(RHICapabilities.MultiDrawIndirect);
        sb.AppendLine($"Multi-Draw Indirect        | {YesNo(hasMultiDraw),-7} | Yes         | Yes");
        
        // Mesh Shaders
        bool hasMeshShaders = caps.HasFlag(RHICapabilities.MeshShaders);
        sb.AppendLine($"Mesh Shaders               | {YesNo(hasMeshShaders),-7} | No          | Yes");
        
        // Ray Tracing
        bool hasRayTracing = caps.HasFlag(RHICapabilities.RayTracing);
        sb.AppendLine($"Ray Tracing                | {YesNo(hasRayTracing),-7} | No          | Yes");
        
        // Variable Rate Shading
        bool hasVRS = caps.HasFlag(RHICapabilities.VariableRateShading);
        sb.AppendLine($"Variable Rate Shading      | {YesNo(hasVRS),-7} | No          | Yes");
        
        sb.AppendLine("-------------------------------------------------------------------");
        sb.AppendLine();
        
        // Rendering Path
        var path = GetRecommendedPath(caps);
        sb.AppendLine($"Recommended Rendering Path: {path}");
        sb.AppendLine();
        
        // Architecture Comparison
        sb.AppendLine("Architecture Comparison:");
        sb.AppendLine("-------------------------------------------------------------------");
        sb.AppendLine();
        
        sb.AppendLine("BlueSky Engine:");
        sb.AppendLine("  - Forward+ (Clustered Forward) rendering");
        sb.AppendLine("  - Automatic fallback: Bindless → Compute → CPU");
        sb.AppendLine("  - PBR with GGX BRDF");
        sb.AppendLine("  - Exponential depth slicing for clusters");
        sb.AppendLine("  - DX11 Feature Levels 10.0+ to DX12/Vulkan/Metal");
        sb.AppendLine();
        
        sb.AppendLine("Frostbite 3 (Battlefield 4, 2013):");
        sb.AppendLine("  - Deferred rendering with tiled lighting");
        sb.AppendLine("  - Compute-based light culling");
        sb.AppendLine("  - PBR with normalized Blinn-Phong");
        sb.AppendLine("  - DX11 minimum requirement");
        sb.AppendLine();
        
        sb.AppendLine("Unreal Engine 5 (2022):");
        sb.AppendLine("  - Nanite virtualized geometry");
        sb.AppendLine("  - Lumen global illumination");
        sb.AppendLine("  - Forward+ and Deferred paths");
        sb.AppendLine("  - Hardware ray tracing support");
        sb.AppendLine("  - DX12/Vulkan for full features");
        sb.AppendLine();
        
        // Performance Characteristics
        sb.AppendLine("Performance Characteristics:");
        sb.AppendLine("-------------------------------------------------------------------");
        sb.AppendLine();
        
        if (hasBindless && hasCompute)
        {
            sb.AppendLine("Modern Mode (DX12/Vulkan/Metal):");
            sb.AppendLine("  - GPU-based light culling");
            sb.AppendLine("  - Full cluster count (16x9x24)");
            sb.AppendLine("  - Max 128 lights per cluster");
            sb.AppendLine("  - Full GGX BRDF");
            sb.AppendLine("  - Bindless resource access");
            sb.AppendLine("  - Target: 120+ FPS on discrete GPUs");
        }
        else if (hasCompute)
        {
            sb.AppendLine("Compute Mode (DX11 Feature Level 11.0+):");
            sb.AppendLine("  - GPU-based light culling");
            sb.AppendLine("  - Standard cluster count (16x9x24)");
            sb.AppendLine("  - Max 64 lights per cluster");
            sb.AppendLine("  - Full GGX BRDF");
            sb.AppendLine("  - Slot-based resource binding");
            sb.AppendLine("  - Target: 60 FPS on mid-range GPUs");
        }
        else
        {
            sb.AppendLine("Legacy Mode (DX11 Feature Level 10.0/10.1):");
            sb.AppendLine("  - CPU-based light culling");
            sb.AppendLine("  - Reduced cluster count (8x5x16)");
            sb.AppendLine("  - Max 32 lights per cluster");
            sb.AppendLine("  - Simplified BRDF");
            sb.AppendLine("  - Target: 60 FPS on integrated graphics");
        }
        
        sb.AppendLine();
        sb.AppendLine("=================================================================");
        
        return sb.ToString();
    }
    
    private static string YesNo(bool value) => value ? "Yes" : "No";
}

/// <summary>
/// Rendering path selection
/// </summary>
public enum RenderingPath
{
    /// <summary>
    /// Forward+ with bindless resources (DX12/Vulkan/Metal)
    /// Matches UE5's forward renderer
    /// </summary>
    ForwardPlusBindless,
    
    /// <summary>
    /// Forward+ with compute shaders (DX11 Feature Level 11.0+)
    /// Similar to Frostbite 3's approach
    /// </summary>
    ForwardPlusCompute,
    
    /// <summary>
    /// Forward+ with CPU culling (DX11 Feature Level 10.x)
    /// Fallback for older hardware
    /// </summary>
    ForwardPlusCPU,
    
    /// <summary>
    /// Traditional forward rendering (ultra-legacy, not recommended)
    /// </summary>
    Forward
}

/// <summary>
/// Quality preset based on capabilities
/// </summary>
public static class QualityPresets
{
    public static ClusterConfig GetClusterConfig(RHICapabilities capabilities)
    {
        if (capabilities.HasFlag(RHICapabilities.BindlessResources))
        {
            // High-end: Full cluster resolution
            return new ClusterConfig
            {
                ClusterCountX = 16,
                ClusterCountY = 9,
                ClusterCountZ = 24,
                MaxLightsPerCluster = 128,
                MaxLights = 2048
            };
        }
        else if (capabilities.HasFlag(RHICapabilities.ComputeShaders))
        {
            // Mid-range: Standard cluster resolution
            return new ClusterConfig
            {
                ClusterCountX = 16,
                ClusterCountY = 9,
                ClusterCountZ = 24,
                MaxLightsPerCluster = 64,
                MaxLights = 1024
            };
        }
        else
        {
            // Low-end: Reduced cluster resolution
            return new ClusterConfig
            {
                ClusterCountX = 8,
                ClusterCountY = 5,
                ClusterCountZ = 16,
                MaxLightsPerCluster = 32,
                MaxLights = 256
            };
        }
    }
}
