using System;
using System.Runtime.InteropServices;

namespace BlueSky.Rendering.RHI;

public static class RHIDiscovery
{
    public static RHIBackend DiscoverBestBackend(string[]? cliArgs)
    {
        if (HasFlag(cliArgs, "--opengl"))
            throw new PlatformNotSupportedException("OpenGL is not an available RHI backend. Available backends: DirectX 11, Vulkan, and Metal on macOS.");

        if (HasFlag(cliArgs, "--vulkan"))
        {
            if (IsVulkanSupported()) return RHIBackend.Vulkan;
            throw new PlatformNotSupportedException("--vulkan was requested, but the Vulkan loader is not available.");
        }

        return DiscoverBestBackend();
    }

    public static RHIBackend DiscoverBestBackend()
    {
        if (OperatingSystem.IsMacOS())
        {
            if (IsMetalSupported()) return RHIBackend.Metal;
            throw new PlatformNotSupportedException("A Metal device is required on macOS.");
        }

        if (OperatingSystem.IsWindows())
        {
            if (IsDirectX11Supported()) return RHIBackend.DirectX11;
            if (IsVulkanSupported()) return RHIBackend.Vulkan;
            throw new PlatformNotSupportedException("Neither DirectX 11 nor Vulkan is available.");
        }

        if (OperatingSystem.IsLinux())
        {
            if (IsVulkanSupported()) return RHIBackend.Vulkan;
            throw new PlatformNotSupportedException("Vulkan is unavailable on this Linux system.");
        }

        throw new PlatformNotSupportedException($"No RHI backend is available for {RuntimeInformation.OSDescription}.");
    }

    private static bool HasFlag(string[]? args, string flag)
    {
        if (args == null) return false;
        foreach (var arg in args)
            if (arg.Equals(flag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool IsMetalSupported()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try
        {
            var device = MTLCreateSystemDefaultDevice();
            if (device == IntPtr.Zero) return false;
            ObjCRelease(device);
            return true;
        }
        catch { return false; }
    }

    public static bool IsDirectX11Supported() =>
        OperatingSystem.IsWindows() && HasNativeExport("d3d11.dll", "D3D11CreateDevice");

    public static bool IsVulkanSupported()
    {
        string library = OperatingSystem.IsWindows() ? "vulkan-1.dll" :
                         OperatingSystem.IsMacOS() ? "libvulkan.dylib" : "libvulkan.so.1";
        return HasNativeExport(library, "vkCreateInstance");
    }

    private static bool HasNativeExport(string libraryName, string exportName)
    {
        IntPtr library = IntPtr.Zero;
        try
        {
            if (!NativeLibrary.TryLoad(libraryName, out library)) return false;
            return NativeLibrary.TryGetExport(library, exportName, out _);
        }
        catch { return false; }
        finally
        {
            if (library != IntPtr.Zero) NativeLibrary.Free(library);
        }
    }

    [DllImport("/System/Library/Frameworks/Metal.framework/Metal")]
    private static extern IntPtr MTLCreateSystemDefaultDevice();

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_release")]
    private static extern void ObjCRelease(IntPtr value);
}
