using System.Runtime.InteropServices;

namespace BlueSky.Platform.Linux;

/// <summary>
/// Wayland protocol P/Invoke declarations
/// </summary>
internal static class WaylandInterop
{
    private const string WaylandClientLib = "libwayland-client.so.0";
    
    // Display functions
    [DllImport(WaylandClientLib)]
    public static extern IntPtr wl_display_connect(string? name);
    
    [DllImport(WaylandClientLib)]
    public static extern void wl_display_disconnect(IntPtr display);
    
    [DllImport(WaylandClientLib)]
    public static extern int wl_display_dispatch(IntPtr display);
    
    [DllImport(WaylandClientLib)]
    public static extern int wl_display_dispatch_pending(IntPtr display);
    
    [DllImport(WaylandClientLib)]
    public static extern int wl_display_flush(IntPtr display);
    
    [DllImport(WaylandClientLib)]
    public static extern int wl_display_roundtrip(IntPtr display);
    
    // Compositor functions
    [DllImport(WaylandClientLib)]
    public static extern IntPtr wl_compositor_create_surface(IntPtr compositor);
    
    // Surface functions
    [DllImport(WaylandClientLib)]
    public static extern void wl_surface_destroy(IntPtr surface);
    
    [DllImport(WaylandClientLib)]
    public static extern void wl_surface_commit(IntPtr surface);
    
    // TODO: Add xdg-shell protocol functions
    // These are typically generated from XML protocol files
    // For now, we'll need to manually declare them or use a code generator
    
    // Helper to check if Wayland is available
    public static bool IsWaylandAvailable()
    {
        var display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        return !string.IsNullOrEmpty(display);
    }
}
