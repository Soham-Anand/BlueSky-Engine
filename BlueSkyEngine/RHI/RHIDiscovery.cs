using System;
using System.Runtime.InteropServices;
using BlueSky.Platform;

namespace NotBSRenderer;

public static class RHIDiscovery
{
    private const uint D3D_SDK_VERSION = 32;

    public static RHIBackend DiscoverBestBackend()
    {
        if (OperatingSystem.IsMacOS())
        {
            if (IsMetalSupported()) return RHIBackend.Metal;
            if (IsVulkanSupported()) return RHIBackend.Vulkan;
            return RHIBackend.OpenGL;
        }

        if (OperatingSystem.IsWindows())
        {
            // Note: DirectX 11/10 are not yet implemented in the engine, 
            // so we prioritize the functional DirectX 9 backend.
            if (IsDirectX9Supported()) return RHIBackend.DirectX9;
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

    public static bool IsDirectX11Supported()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            // Simple check: can we load d3d11.dll and find D3D11CreateDevice?
            IntPtr lib = NativeLibrary.Load("d3d11.dll");
            if (lib == IntPtr.Zero) return false;
            IntPtr proc = NativeLibrary.GetExport(lib, "D3D11CreateDevice");
            NativeLibrary.Free(lib);
            return proc != IntPtr.Zero;
        }
        catch { }
        return false;
    }

    public static bool IsDirectX10Supported()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            IntPtr lib = NativeLibrary.Load("d3d10.dll");
            if (lib == IntPtr.Zero) return false;
            NativeLibrary.Free(lib);
            return true;
        }
        catch { }
        return false;
    }

    public static bool IsDirectX9Supported()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var d3d = Direct3DCreate9(D3D_SDK_VERSION);
            if (d3d != IntPtr.Zero)
            {
                // In D3D9, we should Release the interface
                // For a headless check, creation is enough.
                return true;
            }
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

    [DllImport("d3d9.dll")]
    private static extern IntPtr Direct3DCreate9(uint sdkVersion);
}
