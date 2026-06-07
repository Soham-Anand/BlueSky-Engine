using System;
using System.Runtime.InteropServices;
using BlueSky.Platform;

namespace NotBSRenderer;

public static class RHIDiscovery
{
    public static RHIBackend DiscoverBestBackend(string[]? cliArgs, bool forceCompatibility = false)
    {
        bool wantsVulkan = HasFlag(cliArgs, "--vulkan");
        bool wantsOpenGL = HasFlag(cliArgs, "--opengl");

        if (OperatingSystem.IsWindows())
        {
            if (wantsVulkan)
            {
                if (IsVulkanSupported()) return RHIBackend.Vulkan;
                Console.WriteLine("[RHI] --vulkan requested, but Vulkan was not found. Falling back to DirectX 11.");
            }

            if (IsDirectX11Supported()) return RHIBackend.DirectX11;

            if (IsVulkanSupported()) return RHIBackend.Vulkan;
            if (wantsOpenGL && IsOpenGLSupported()) return RHIBackend.OpenGL;
            return RHIBackend.DirectX11;
        }

        if (OperatingSystem.IsLinux())
        {
            if (!wantsOpenGL && IsVulkanSupported()) return RHIBackend.Vulkan;
            return RHIBackend.OpenGL;
        }

        return DiscoverBestBackend(forceCompatibility);
    }

    public static RHIBackend DiscoverBestBackend(bool forceCompatibility = false)
    {
        if (OperatingSystem.IsMacOS())
        {
            if (IsMetalSupported()) return RHIBackend.Metal;
            throw new PlatformNotSupportedException("Metal is required on macOS.");
        }

        if (OperatingSystem.IsWindows())
        {
            // Windows defaults to DirectX 11. Vulkan is opt-in through --vulkan.
            if (IsDirectX11Supported()) return RHIBackend.DirectX11;
            if (IsVulkanSupported()) return RHIBackend.Vulkan;
            return RHIBackend.OpenGL;
        }

        if (OperatingSystem.IsLinux())
        {
            if (IsVulkanSupported()) return RHIBackend.Vulkan;
            return RHIBackend.OpenGL;
        }

        return RHIBackend.OpenGL;
    }

    private static bool HasFlag(string[]? args, string flag)
    {
        if (args == null) return false;
        foreach (var arg in args)
        {
            if (arg.Equals(flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool IsMetalSupported()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try
        {
            var device = MTLCreateSystemDefaultDevice();
            if (device != IntPtr.Zero)
            {
                // We don't need to hold onto it, just check if it exists
                // Note: On macOS, we don't have a simple Release for Metal pointers here 
                // but since this is a one-time check at startup, a tiny leak is acceptable 
                // if we can't easily release it without full ObjC interop.
                return true;
            }
        }
        catch { }
        return false;
    }

    public static bool IsDirectX12Supported()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            IntPtr lib = NativeLibrary.Load("d3d12.dll");
            if (lib == IntPtr.Zero) return false;
            IntPtr proc = NativeLibrary.GetExport(lib, "D3D12CreateDevice");
            NativeLibrary.Free(lib);
            return proc != IntPtr.Zero;
        }
        catch { }
        return false;
    }

    public static bool IsDirectX11Supported()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            IntPtr lib = NativeLibrary.Load("d3d11.dll");
            if (lib == IntPtr.Zero) return false;
            IntPtr proc = NativeLibrary.GetExport(lib, "D3D11CreateDevice");
            NativeLibrary.Free(lib);
            return proc != IntPtr.Zero;
        }
        catch { }
        return false;
    }

    public static bool IsOpenGLSupported()
    {
        // Simple heuristic: if we are on any modern OS, OpenGL is usually supported via a fallback
        // To be strictly correct, we'd need to create a dummy WGL/GLX/CGL context.
        // For now, we'll assume true if nothing else works.
        return true;
    }

    public static bool IsVulkanSupported()
    {
        // Headless Vulkan check: try to load the library
        string libName = OperatingSystem.IsWindows() ? "vulkan-1.dll" : 
                         OperatingSystem.IsMacOS() ? "libvulkan.dylib" : "libvulkan.so.1";
        
        IntPtr lib = NativeLibrary.Load(libName, typeof(RHIDiscovery).Assembly, null);
        if (lib == IntPtr.Zero) return false;
        
        try
        {
            // If we can load the library, it's a good sign, but let's try to get vkCreateInstance
            IntPtr proc = NativeLibrary.GetExport(lib, "vkCreateInstance");
            return proc != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
        finally
        {
            NativeLibrary.Free(lib);
        }
    }

    [DllImport("/System/Library/Frameworks/Metal.framework/Metal")]
    private static extern IntPtr MTLCreateSystemDefaultDevice();
}
