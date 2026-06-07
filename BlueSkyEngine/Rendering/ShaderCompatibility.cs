// BlueSkyEngine - Shader Model Cross-Compatibility System
//
// CRITICAL: Shader Model vs Hardware Compatibility
// ==================================================
// Different hardware supports different shader models:
//
// Shader Model 4.0 (DX11 FL 10.0/10.1):
// - Intel HD Graphics (Sandy Bridge i5-2410M, 2011)
// - GeForce 8/9 series (2006-2008)
// - Radeon HD 2000/3000 series (2007-2008)
// - Features: Geometry Shaders, Stream Output, Texture Arrays
// - NO Compute Shaders, NO UAVs
//
// Shader Model 5.0 (DX11 FL 11.0+):
// - Intel HD Graphics 4000+ (Ivy Bridge, 2012+)
// - GeForce GTX 400+ (2010+)
// - Radeon HD 5000+ (2009+)
// - Features: Compute Shaders, UAVs, Tessellation
//
// Strategy:
// 1. Detect hardware shader model at runtime
// 2. Load appropriate shader variant (SM 4.0 or SM 5.0)
// 3. Fallback to CPU path if compute shaders unavailable

using System;
using System.Collections.Generic;
using System.IO;
using NotBSRenderer;

namespace BlueSky.Rendering;

/// <summary>
/// Shader model compatibility detector and loader
/// Ensures correct shader variant is loaded for target hardware
/// </summary>
public static class ShaderCompatibility
{
    /// <summary>
    /// Detect shader model support from RHI capabilities
    /// </summary>
    public static ShaderModel DetectShaderModel(IRHIDevice device)
    {
        var caps = device.Capabilities;
        
        // Shader Model 5.0: Compute Shaders + UAVs
        if (caps.HasFlag(RHICapabilities.ComputeShaders))
        {
            return ShaderModel.SM_5_0;
        }
        
        // Shader Model 4.1: Geometry Shaders + Tessellation
        if (caps.HasFlag(RHICapabilities.GeometryShaders) && 
            caps.HasFlag(RHICapabilities.TessellationShaders))
        {
            return ShaderModel.SM_4_1;
        }
        
        // Shader Model 4.0: Geometry Shaders only
        if (caps.HasFlag(RHICapabilities.GeometryShaders))
        {
            return ShaderModel.SM_4_0;
        }
        
        // Fallback: Shader Model 3.0 (should never happen with DX11)
        return ShaderModel.SM_3_0;
    }
    
    /// <summary>
    /// Get shader file path for target shader model
    /// </summary>
    public static string GetShaderPath(string baseName, ShaderModel model, ShaderStage stage)
    {
        string modelSuffix = model switch
        {
            ShaderModel.SM_3_0 => "sm30",
            ShaderModel.SM_4_0 => "sm40",
            ShaderModel.SM_4_1 => "sm41",
            ShaderModel.SM_5_0 => "sm50",
            ShaderModel.SM_6_0 => "sm60",
            _ => "sm40"
        };
        
        string stageSuffix = stage switch
        {
            ShaderStage.Vertex => "vs",
            ShaderStage.Fragment => "ps",
            ShaderStage.Compute => "cs",
            _ => "vs"
        };
        
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", 
                           $"{baseName}_{stageSuffix}_{modelSuffix}.cso");
    }
    
    /// <summary>
    /// Load shader bytecode with automatic fallback
    /// </summary>
    public static byte[]? LoadShaderBytecode(string baseName, ShaderModel targetModel, ShaderStage stage)
    {
        // Try target model first
        string path = GetShaderPath(baseName, targetModel, stage);
        if (File.Exists(path))
        {
            Console.WriteLine($"[ShaderCompat] Loading {baseName} ({targetModel})");
            return File.ReadAllBytes(path);
        }
        
        // Fallback to lower shader models
        var fallbackModels = GetFallbackModels(targetModel);
        foreach (var fallback in fallbackModels)
        {
            path = GetShaderPath(baseName, fallback, stage);
            if (File.Exists(path))
            {
                Console.WriteLine($"[ShaderCompat] Loading {baseName} ({fallback}) as fallback for {targetModel}");
                return File.ReadAllBytes(path);
            }
        }
        
        Console.WriteLine($"[ShaderCompat] WARNING: No shader found for {baseName} (target: {targetModel})");
        return null;
    }
    
    /// <summary>
    /// Get fallback shader models in priority order
    /// </summary>
    private static ShaderModel[] GetFallbackModels(ShaderModel target)
    {
        return target switch
        {
            ShaderModel.SM_6_0 => new[] { ShaderModel.SM_5_0, ShaderModel.SM_4_1, ShaderModel.SM_4_0 },
            ShaderModel.SM_5_0 => new[] { ShaderModel.SM_4_1, ShaderModel.SM_4_0 },
            ShaderModel.SM_4_1 => new[] { ShaderModel.SM_4_0 },
            ShaderModel.SM_4_0 => new[] { ShaderModel.SM_3_0 },
            _ => Array.Empty<ShaderModel>()
        };
    }
    
    /// <summary>
    /// Check if compute shaders are available
    /// </summary>
    public static bool SupportsComputeShaders(ShaderModel model)
    {
        return model >= ShaderModel.SM_5_0;
    }
    
    /// <summary>
    /// Check if tessellation is available
    /// </summary>
    public static bool SupportsTessellation(ShaderModel model)
    {
        return model >= ShaderModel.SM_4_1;
    }
    
    /// <summary>
    /// Check if geometry shaders are available
    /// </summary>
    public static bool SupportsGeometryShaders(ShaderModel model)
    {
        return model >= ShaderModel.SM_4_0;
    }
    
    /// <summary>
    /// Get recommended rendering path for shader model
    /// </summary>
    public static string GetRecommendedPath(ShaderModel model)
    {
        return model switch
        {
            ShaderModel.SM_6_0 => "GPU-Driven with Mesh Shaders",
            ShaderModel.SM_5_0 => "GPU-Driven with Compute Shaders",
            ShaderModel.SM_4_1 => "CPU Culling with Tessellation",
            ShaderModel.SM_4_0 => "CPU Culling with Geometry Shaders",
            _ => "Legacy Forward Rendering"
        };
    }
    
    /// <summary>
    /// Print compatibility report
    /// </summary>
    public static void PrintCompatibilityReport(IRHIDevice device)
    {
        var model = DetectShaderModel(device);
        
        Console.WriteLine("================================================================================");
        Console.WriteLine("Shader Model Compatibility Report");
        Console.WriteLine("================================================================================");
        Console.WriteLine($"Backend: {device.Backend}");
        Console.WriteLine($"Detected Shader Model: {model}");
        Console.WriteLine($"Recommended Path: {GetRecommendedPath(model)}");
        Console.WriteLine();
        Console.WriteLine("Feature Support:");
        Console.WriteLine($"  Compute Shaders:    {(SupportsComputeShaders(model) ? "✓ Yes" : "✗ No")}");
        Console.WriteLine($"  Tessellation:       {(SupportsTessellation(model) ? "✓ Yes" : "✗ No")}");
        Console.WriteLine($"  Geometry Shaders:   {(SupportsGeometryShaders(model) ? "✓ Yes" : "✗ No")}");
        Console.WriteLine($"  Indirect Drawing:   {(device.Capabilities.HasFlag(RHICapabilities.IndirectDrawing) ? "✓ Yes" : "✗ No")}");
        Console.WriteLine($"  Bindless Resources: {(device.Capabilities.HasFlag(RHICapabilities.BindlessResources) ? "✓ Yes" : "✗ No")}");
        Console.WriteLine();
        
        // Hardware examples
        Console.WriteLine("Example Hardware:");
        Console.WriteLine(model switch
        {
            ShaderModel.SM_5_0 => "  - Intel HD Graphics 4000+ (Ivy Bridge 2012+)\n  - GeForce GTX 400+ (Fermi 2010+)\n  - Radeon HD 5000+ (Evergreen 2009+)",
            ShaderModel.SM_4_1 => "  - Intel HD Graphics 3000 (Sandy Bridge 2011)\n  - GeForce GTX 200 series (Tesla 2008)\n  - Radeon HD 4000 series (TeraScale 2008)",
            ShaderModel.SM_4_0 => "  - Intel HD Graphics (Westmere 2010)\n  - GeForce 8/9 series (2006-2008)\n  - Radeon HD 2000/3000 series (2007-2008)",
            _ => "  - Legacy hardware"
        });
        Console.WriteLine("================================================================================");
    }
}

/// <summary>
/// Shader model enumeration
/// </summary>
public enum ShaderModel
{
    SM_3_0,  // DX9 (Legacy, not supported)
    SM_4_0,  // DX11 FL 10.0 - Geometry Shaders
    SM_4_1,  // DX11 FL 10.1 - Tessellation
    SM_5_0,  // DX11 FL 11.0 - Compute Shaders
    SM_6_0   // DX12 - Mesh Shaders, Raytracing
}

/// <summary>
/// Shader loader with automatic compatibility handling
/// </summary>
public class CompatibleShaderLoader
{
    private readonly IRHIDevice _device;
    private readonly ShaderModel _shaderModel;
    private readonly Dictionary<string, byte[]> _shaderCache = new();
    
    public ShaderModel ShaderModel => _shaderModel;
    
    public CompatibleShaderLoader(IRHIDevice device)
    {
        _device = device;
        _shaderModel = ShaderCompatibility.DetectShaderModel(device);
        
        Console.WriteLine($"[ShaderLoader] Initialized for {_shaderModel}");
    }
    
    /// <summary>
    /// Load shader with automatic fallback
    /// </summary>
    public byte[]? LoadShader(string name, ShaderStage stage)
    {
        string cacheKey = $"{name}_{stage}_{_shaderModel}";
        
        if (_shaderCache.TryGetValue(cacheKey, out var cached))
            return cached;
        
        byte[]? bytecode = null;
        
        if (_device.Backend == RHIBackend.Metal)
        {
            // On Metal, load the compiled metallib file natively
            string fileName = name + ".metallib";
            
            string[] searchPaths = new[]
            {
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", fileName),
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Editor", "Shaders", fileName),
                System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Editor", "Shaders", fileName),
                System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "BlueSkyEngine", "Editor", "Shaders", fileName),
                System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "BlueSkyEngine", "Rendering", "Shaders", fileName),
            };
            
            string? found = Array.Find(searchPaths, System.IO.File.Exists);
            if (found != null)
            {
                bytecode = System.IO.File.ReadAllBytes(found);
                Console.WriteLine($"[ShaderCompat] Metal backend: loaded {fileName} from {found} ({bytecode.Length} bytes)");
            }
        }
        else if (_device.Backend == RHIBackend.Vulkan)
        {
            string stageSuffix = stage switch
            {
                ShaderStage.Vertex => "vert",
                ShaderStage.Fragment => "frag",
                ShaderStage.Compute => "comp",
                _ => "vert"
            };
            string fileName = $"{name}.{stageSuffix}.spv";

            string[] searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Editor", "Shaders", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "Editor", "Shaders", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "BlueSkyEngine", "Editor", "Shaders", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "BlueSkyEngine", "Rendering", "Shaders", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "BlueSkyEngine", "Rendering", "EasePlus", "Shaders", fileName),
            };

            string? found = Array.Find(searchPaths, File.Exists);
            if (found != null)
            {
                bytecode = File.ReadAllBytes(found);
                Console.WriteLine($"[ShaderCompat] Vulkan backend: loaded {fileName} from {found} ({bytecode.Length} bytes)");
            }
        }
        else
        {
            bytecode = ShaderCompatibility.LoadShaderBytecode(name, _shaderModel, stage);
        }
        
        if (bytecode != null)
            _shaderCache[cacheKey] = bytecode;
        
        return bytecode;
    }
    
    /// <summary>
    /// Create graphics pipeline with compatible shaders
    /// </summary>
    public IRHIPipeline? CreateGraphicsPipeline(string shaderName, GraphicsPipelineDesc desc)
    {
        // Load vertex shader
        var vsBytecode = LoadShader(shaderName, ShaderStage.Vertex);
        if (vsBytecode == null)
        {
            Console.WriteLine($"[ShaderLoader] Failed to load vertex shader: {shaderName}");
            return null;
        }
        
        // Load fragment shader
        var psBytecode = LoadShader(shaderName, ShaderStage.Fragment);
        if (psBytecode == null)
        {
            Console.WriteLine($"[ShaderLoader] Failed to load fragment shader: {shaderName}");
            return null;
        }
        
        // Update shader descriptors
        desc.VertexShader.Bytecode = vsBytecode;
        desc.FragmentShader.Bytecode = psBytecode;
        
        return _device.CreateGraphicsPipeline(desc);
    }
    
    /// <summary>
    /// Create compute pipeline (only if SM 5.0+)
    /// </summary>
    public IRHIPipeline? CreateComputePipeline(string shaderName, ComputePipelineDesc desc)
    {
        if (!ShaderCompatibility.SupportsComputeShaders(_shaderModel))
        {
            Console.WriteLine($"[ShaderLoader] Compute shaders not supported on {_shaderModel}");
            return null;
        }
        
        var csBytecode = LoadShader(shaderName, ShaderStage.Compute);
        if (csBytecode == null)
        {
            Console.WriteLine($"[ShaderLoader] Failed to load compute shader: {shaderName}");
            return null;
        }
        
        desc.ComputeShader.Bytecode = csBytecode;
        
        return _device.CreateComputePipeline(desc);
    }
}
