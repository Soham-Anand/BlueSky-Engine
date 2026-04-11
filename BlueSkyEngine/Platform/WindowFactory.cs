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
            throw new PlatformNotSupportedException("Linux windowing not yet implemented");
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
        else
        {
            throw new PlatformNotSupportedException($"Input not supported for window type: {window.GetType().Name}");
        }
    }
}
