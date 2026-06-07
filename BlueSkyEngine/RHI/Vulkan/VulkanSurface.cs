using System;
using BlueSky.Platform;
using BlueSky.Platform.Linux;
using BlueSky.Platform.Windows;

namespace NotBSRenderer.Vulkan;

/// <summary>
/// Creates a VkSurfaceKHR from a platform-specific window handle.
/// Supports X11, Wayland, Win32, and Metal (MoltenVK) surfaces.
/// </summary>
internal static class VulkanSurface
{
    public static IntPtr Create(IntPtr instance, IWindow window)
    {
        if (window is X11Window x11)
            return CreateX11Surface(instance, x11);

        if (window is WaylandWindow wayland)
            return CreateWaylandSurface(instance, wayland);

        if (window is Win32Window win32)
            return CreateWin32Surface(instance, win32);

        // macOS: Metal surface via MoltenVK
        if (OperatingSystem.IsMacOS())
            return CreateMetalSurface(instance, window);

        throw new PlatformNotSupportedException(
            $"Cannot create Vulkan surface for window type: {window.GetType().Name}");
    }

    private static IntPtr CreateX11Surface(IntPtr instance, X11Window window)
    {
        if (VulkanInterop.vkCreateXlibSurfaceKHR == null)
            throw new InvalidOperationException("VK_KHR_xlib_surface extension not available");

        var createInfo = new VulkanInterop.VkXlibSurfaceCreateInfoKHR
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_XLIB_SURFACE_CREATE_INFO_KHR,
            dpy = window.GetDisplayHandle(),
            window = (ulong)window.GetNativeHandle()
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateXlibSurfaceKHR(instance, ref createInfo, IntPtr.Zero, out var surface),
            "vkCreateXlibSurfaceKHR");

        Console.WriteLine("[Vulkan] X11 surface created");
        return surface;
    }

    private static IntPtr CreateWaylandSurface(IntPtr instance, WaylandWindow window)
    {
        if (VulkanInterop.vkCreateWaylandSurfaceKHR == null)
            throw new InvalidOperationException("VK_KHR_wayland_surface extension not available");

        var createInfo = new VulkanInterop.VkWaylandSurfaceCreateInfoKHR
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_WAYLAND_SURFACE_CREATE_INFO_KHR,
            display = window.GetWaylandDisplay(),
            surface = window.GetNativeHandle()
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateWaylandSurfaceKHR(instance, ref createInfo, IntPtr.Zero, out var surface),
            "vkCreateWaylandSurfaceKHR");

        Console.WriteLine("[Vulkan] Wayland surface created");
        return surface;
    }

    private static IntPtr CreateWin32Surface(IntPtr instance, Win32Window window)
    {
        if (VulkanInterop.vkCreateWin32SurfaceKHR == null)
            throw new InvalidOperationException("VK_KHR_win32_surface extension not available");

        var createInfo = new VulkanInterop.VkWin32SurfaceCreateInfoKHR
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR,
            hinstance = System.Diagnostics.Process.GetCurrentProcess().Handle,
            hwnd = window.GetNativeHandle()
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateWin32SurfaceKHR(instance, ref createInfo, IntPtr.Zero, out var surface),
            "vkCreateWin32SurfaceKHR");

        Console.WriteLine("[Vulkan] Win32 surface created");
        return surface;
    }

    private static IntPtr CreateMetalSurface(IntPtr instance, IWindow window)
    {
        if (VulkanInterop.vkCreateMetalSurfaceEXT == null)
            throw new InvalidOperationException("VK_EXT_metal_surface extension not available");

        // On macOS the native handle is an NSView*. MoltenVK expects a CAMetalLayer.
        // The CocoaWindow should ensure the view's layer is a CAMetalLayer.
        var createInfo = new VulkanInterop.VkMetalSurfaceCreateInfoEXT
        {
            sType = VulkanInterop.VK_STRUCTURE_TYPE_METAL_SURFACE_CREATE_INFO_EXT,
            pLayer = window.GetNativeHandle()
        };

        VulkanInterop.VkCheck(
            VulkanInterop.vkCreateMetalSurfaceEXT(instance, ref createInfo, IntPtr.Zero, out var surface),
            "vkCreateMetalSurfaceEXT");

        Console.WriteLine("[Vulkan] Metal surface created (MoltenVK)");
        return surface;
    }

    /// <summary>
    /// Get required instance extensions for the current platform's surface type.
    /// </summary>
    public static string[] GetRequiredExtensions()
    {
        var extensions = new System.Collections.Generic.List<string> { "VK_KHR_surface" };

        if (OperatingSystem.IsLinux())
        {
            // Request both — we'll use whichever matches the window type
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                extensions.Add("VK_KHR_wayland_surface");

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                extensions.Add("VK_KHR_xlib_surface");

            // If neither is detected yet, add both as a safety net
            if (extensions.Count == 1)
            {
                extensions.Add("VK_KHR_xlib_surface");
                extensions.Add("VK_KHR_wayland_surface");
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            extensions.Add("VK_KHR_win32_surface");
        }
        else if (OperatingSystem.IsMacOS())
        {
            extensions.Add("VK_EXT_metal_surface");
            extensions.Add("VK_KHR_portability_enumeration");
        }

        return extensions.ToArray();
    }
}
