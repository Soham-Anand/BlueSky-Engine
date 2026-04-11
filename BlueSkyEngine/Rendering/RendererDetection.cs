using System;
using System.Runtime.InteropServices;

namespace BlueSky.Rendering;

public enum RendererBackend
{
    Metal,
    Vulkan,
    DirectX11,
    DirectX12,
    OpenGL
}

public static class RendererDetection
{
    public static RendererBackend DetectBestRenderer()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return DetectMacOSRenderer();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return DetectLinuxRenderer();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return DetectWindowsRenderer();
        }
        
        // Fallback
        return RendererBackend.OpenGL;
    }
    
    private static RendererBackend DetectMacOSRenderer()
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        
        // Apple Silicon always supports Metal
        if (arch == Architecture.Arm64)
        {
            Console.WriteLine("[Renderer Detection] macOS Apple Silicon detected -> Metal");
            return RendererBackend.Metal;
        }
        
        // Intel Mac - check if Metal is supported
        if (IsMetalSupported())
        {
            Console.WriteLine("[Renderer Detection] macOS Intel with Metal support -> Metal");
            return RendererBackend.Metal;
        }
        
        Console.WriteLine("[Renderer Detection] macOS Intel without Metal support -> OpenGL");
        return RendererBackend.OpenGL;
    }
    
    private static RendererBackend DetectLinuxRenderer()
    {
        // Check for Vulkan support
        if (IsVulkanSupported())
        {
            Console.WriteLine("[Renderer Detection] Linux with Vulkan support -> Vulkan");
            return RendererBackend.Vulkan;
        }
        
        Console.WriteLine("[Renderer Detection] Linux without Vulkan support -> OpenGL");
        return RendererBackend.OpenGL;
    }
    
    private static RendererBackend DetectWindowsRenderer()
    {
        // Windows: prefer DirectX 11, fallback to OpenGL
        Console.WriteLine("[Renderer Detection] Windows detected -> DirectX 11");
        return RendererBackend.DirectX11;
    }
    
    private static bool IsMetalSupported()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return false;
        
        try
        {
            // Check if Metal framework is available
            // Metal requires macOS 10.11+ and compatible GPU
            var result = ExecuteCommand("system_profiler", "SPDisplaysDataType");
            return result.Contains("Metal") && !result.Contains("Metal: Not Supported");
        }
        catch
        {
            return false;
        }
    }
    
    private static bool IsVulkanSupported()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;
        
        try
        {
            // Check if vulkaninfo command exists and runs successfully
            var result = ExecuteCommand("vulkaninfo", "--summary");
            return !string.IsNullOrEmpty(result) && !result.Contains("ERROR");
        }
        catch
        {
            return false;
        }
    }
    
    private static string ExecuteCommand(string command, string arguments)
    {
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000); // 5 second timeout
            
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }
}
