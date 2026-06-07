// BlueSkyEngine - Shader Compilation Service
// Compiles shaders for all platforms (HLSL, MSL, GLSL)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BlueSky.Core.Diagnostics;
using BlueSky.Rendering.Materials;
using NotBSRenderer;

namespace BlueSky.Rendering.Shaders;

/// <summary>
/// Shader compiler - compiles HLSL to platform-specific bytecode.
/// Supports DirectX (DXC), Metal (metal), Vulkan (glslangValidator).
/// </summary>
public sealed class ShaderCompiler
{
    private static readonly Lazy<ShaderCompiler> _instance = new(() => new ShaderCompiler());
    public static ShaderCompiler Instance => _instance.Value;
    
    private readonly string _tempDir;
    private readonly ShaderCache _cache;
    
    private ShaderCompiler()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BlueSkyShaders");
        Directory.CreateDirectory(_tempDir);
        _cache = ShaderCache.Instance;
    }
    
    /// <summary>
    /// Compile shader for platform.
    /// </summary>
    public async Task<CompiledShader?> CompileAsync(string shaderSource, NotBSRenderer.ShaderStage stage, RHIBackend backend, Dictionary<string, string>? defines = null)
    {
        // Check cache first
        string cacheKey = GetCacheKey(shaderSource, stage, backend, defines);
        if (_cache.TryGet(cacheKey, backend.ToString(), out var cachedBytecode) && cachedBytecode != null)
        {
            return new CompiledShader
            {
                Bytecode = cachedBytecode,
                Stage = stage,
                Backend = backend
            };
        }
        
        // Inject defines
        if (defines != null && defines.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var (key, value) in defines)
            {
                sb.AppendLine($"#define {key} {value}");
            }
            sb.AppendLine();
            sb.Append(shaderSource);
            shaderSource = sb.ToString();
        }
        
        // Compile based on backend
        byte[]? bytecode = backend switch
        {
            RHIBackend.DirectX11 or RHIBackend.DirectX12 => await CompileDXCAsync(shaderSource, stage),
            RHIBackend.Metal => await CompileMetalAsync(shaderSource, stage),
            RHIBackend.Vulkan => await CompileVulkanAsync(shaderSource, stage),
            _ => null
        };
        
        if (bytecode == null)
        {
            ErrorHandler.LogError($"Failed to compile shader for {backend}", null, "ShaderCompiler");
            return null;
        }
        
        // Cache compiled bytecode
        _cache.Store(cacheKey, backend.ToString(), bytecode);
        
        return new CompiledShader
        {
            Bytecode = bytecode,
            Stage = stage,
            Backend = backend
        };
    }
    
    private async Task<byte[]?> CompileDXCAsync(string source, NotBSRenderer.ShaderStage stage)
    {
        // Write source to temp file
        string sourcePath = Path.Combine(_tempDir, $"shader_{Guid.NewGuid()}.hlsl");
        string outputPath = Path.Combine(_tempDir, $"shader_{Guid.NewGuid()}.dxil");
        
        try
        {
            await File.WriteAllTextAsync(sourcePath, source);
            
            // Compile with DXC
            string profile = stage switch
            {
                NotBSRenderer.ShaderStage.Vertex => "vs_6_0",
                NotBSRenderer.ShaderStage.Fragment => "ps_6_0",
                NotBSRenderer.ShaderStage.Compute => "cs_6_0",
                _ => "vs_6_0"
            };
            
            var psi = new ProcessStartInfo
            {
                FileName = "dxc",
                Arguments = $"-T {profile} -E main -Fo {outputPath} {sourcePath}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null)
            {
                ErrorHandler.LogError("Failed to start DXC compiler", null, "ShaderCompiler");
                return null;
            }
            
            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                ErrorHandler.LogError($"DXC compilation failed: {error}", null, "ShaderCompiler");
                return null;
            }
            
            // Read compiled bytecode
            if (File.Exists(outputPath))
            {
                return await File.ReadAllBytesAsync(outputPath);
            }
            
            return null;
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError("DXC compilation exception", ex, "ShaderCompiler");
            return null;
        }
        finally
        {
            // Cleanup temp files
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
    
    private async Task<byte[]?> CompileMetalAsync(string source, NotBSRenderer.ShaderStage stage)
    {
        // Metal shaders need to be written in MSL, not HLSL
        // For now, return null (would need HLSL → MSL transpiler)
        ErrorHandler.LogWarning("Metal shader compilation requires MSL source (HLSL transpiler not implemented)", "ShaderCompiler");
        return null;
    }
    
    private async Task<byte[]?> CompileVulkanAsync(string source, NotBSRenderer.ShaderStage stage)
    {
        // Vulkan uses SPIR-V
        // Can compile HLSL → SPIR-V using DXC with -spirv flag
        string sourcePath = Path.Combine(_tempDir, $"shader_{Guid.NewGuid()}.hlsl");
        string outputPath = Path.Combine(_tempDir, $"shader_{Guid.NewGuid()}.spv");
        
        try
        {
            await File.WriteAllTextAsync(sourcePath, source);
            
            string profile = stage switch
            {
                NotBSRenderer.ShaderStage.Vertex => "vs_6_0",
                NotBSRenderer.ShaderStage.Fragment => "ps_6_0",
                NotBSRenderer.ShaderStage.Compute => "cs_6_0",
                _ => "vs_6_0"
            };
            
            var psi = new ProcessStartInfo
            {
                FileName = "dxc",
                Arguments = $"-spirv -T {profile} -E main -Fo {outputPath} {sourcePath}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) return null;
            
            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                ErrorHandler.LogError($"SPIR-V compilation failed: {error}", null, "ShaderCompiler");
                return null;
            }
            
            if (File.Exists(outputPath))
            {
                return await File.ReadAllBytesAsync(outputPath);
            }
            
            return null;
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError("SPIR-V compilation exception", ex, "ShaderCompiler");
            return null;
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
    
    private string GetCacheKey(string source, NotBSRenderer.ShaderStage stage, RHIBackend backend, Dictionary<string, string>? defines)
    {
        var sb = new StringBuilder();
        sb.Append(source);
        sb.Append(stage);
        sb.Append(backend);
        
        if (defines != null)
        {
            foreach (var (key, value) in defines.OrderBy(kvp => kvp.Key))
            {
                sb.Append(key);
                sb.Append(value);
            }
        }
        
        return sb.ToString();
    }
}

/// <summary>
/// Compiled shader.
/// </summary>
public class CompiledShader
{
    public byte[] Bytecode = Array.Empty<byte>();
    public NotBSRenderer.ShaderStage Stage;
    public RHIBackend Backend;
}
