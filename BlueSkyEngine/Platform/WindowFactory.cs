using System;
using System.Runtime.InteropServices;

namespace BlueSky.Platform;

/// <summary>
/// Factory for creating platform-specific windows.
/// </summary>
public static class WindowFactory
{
    /// <summary>
    /// Creates a window for the current platform.
    /// </summary>
    public static IWindow Create(WindowOptions options)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new macOS.CocoaWindow(options);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new Windows.Win32Window(options);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                return new Linux.X11Window(options);

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                return new Linux.WaylandWindow(options);

            throw new PlatformNotSupportedException("Linux windowing requires DISPLAY or WAYLAND_DISPLAY.");
        }
        else
        {
            throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription}");
        }
    }
    
    /// <summary>
    /// Creates an input context for the given window.
    /// </summary>
    public static IInputContext CreateInput(this IWindow window)
    {
        if (window is macOS.CocoaWindow cocoaWindow)
        {
            var input = new macOS.CocoaInput();
            cocoaWindow.RegisterInput(input);
            return input;
        }
        else if (window is Windows.Win32Window winWindow)
        {
            return new Windows.Win32Input(winWindow);
        }
        else if (window is Linux.X11Window x11Window)
        {
            return new Linux.LinuxInput(x11Window.GetDisplayHandle(), isWayland: false);
        }
        else if (window is Linux.WaylandWindow waylandWindow)
        {
            return new Linux.LinuxInput(waylandWindow.GetWaylandDisplay(), isWayland: true);
        }
        else
        {
            throw new PlatformNotSupportedException($"Input not supported for window type: {window.GetType().Name}");
        }
    }
}
