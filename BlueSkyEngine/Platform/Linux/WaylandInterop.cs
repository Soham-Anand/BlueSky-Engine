using System.Runtime.InteropServices;

namespace BlueSky.Platform.Linux;

/// <summary>
/// Wayland protocol P/Invoke declarations
/// </summary>
internal static class WaylandInterop
{
    private const string WaylandClientLib = "libwayland-client.so.0";
    
    // Wayland listener callback delegate
    public delegate void WlListenerDelegate(IntPtr data, IntPtr wl, uint opcode, IntPtr message);
    
    // Wayland interface structure
    [StructLayout(LayoutKind.Sequential)]
    public struct WlInterface
    {
        public IntPtr name;
        public int version;
        public int method_count;
        public IntPtr methods;
        public int event_count;
        public IntPtr events;
    }
    
    // Wayland proxy/listener structure
    [StructLayout(LayoutKind.Sequential)]
    public struct WlListener
    {
        public IntPtr notify;
    }
    
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
    
    [DllImport(WaylandClientLib)]
    public static extern IntPtr wl_display_get_registry(IntPtr display);
    
    // Registry functions
    [DllImport(WaylandClientLib)]
    public static extern void wl_registry_destroy(IntPtr registry);
    
    [DllImport(WaylandClientLib)]
    public static extern IntPtr wl_registry_bind(
        IntPtr registry,
        uint name,
        IntPtr iface,
        uint version);
    
    // Compositor functions
    [DllImport(WaylandClientLib)]
    public static extern IntPtr wl_compositor_create_surface(IntPtr compositor);
    
    [DllImport(WaylandClientLib)]
    public static extern void wl_compositor_destroy(IntPtr compositor);
    
    // Surface functions
    [DllImport(WaylandClientLib)]
    public static extern void wl_surface_destroy(IntPtr surface);
    
    [DllImport(WaylandClientLib)]
    public static extern void wl_surface_commit(IntPtr surface);
    
    [DllImport(WaylandClientLib)]
    public static extern void wl_surface_attach(IntPtr surface, IntPtr buffer, int x, int y);
    
    [DllImport(WaylandClientLib)]
    public static extern void wl_surface_damage(IntPtr surface, int x, int y, int width, int height);
    
    [DllImport(WaylandClientLib)]
    public static extern void wl_surface_set_buffer_scale(IntPtr surface, int scale);
    
    // Callback functions
    [DllImport(WaylandClientLib)]
    public static extern IntPtr wl_callback_destroy(IntPtr callback);
    
    // Seat functions (input handling)
    [DllImport(WaylandClientLib)]
    public static extern void wl_seat_destroy(IntPtr seat);
    
    [DllImport(WaylandClientLib)]
    public static extern IntPtr wl_seat_get_pointer(IntPtr seat);
    
    [DllImport(WaylandClientLib)]
    public static extern IntPtr wl_seat_get_keyboard(IntPtr seat);
    
    [DllImport(WaylandClientLib)]
    public static extern IntPtr wl_seat_get_touch(IntPtr seat);
    
    // Pointer functions
    [DllImport(WaylandClientLib)]
    public static extern void wl_pointer_destroy(IntPtr pointer);
    
    [DllImport(WaylandClientLib)]
    public static extern void wl_pointer_set_cursor(
        IntPtr pointer,
        uint serial,
        IntPtr surface,
        int hotspot_x,
        int hotspot_y);
    
    [DllImport(WaylandClientLib)]
    public static extern void wl_pointer_release(IntPtr pointer);
    
    // Keyboard functions
    [DllImport(WaylandClientLib)]
    public static extern void wl_keyboard_destroy(IntPtr keyboard);
    
    [DllImport(WaylandClientLib)]
    public static extern void wl_keyboard_release(IntPtr keyboard);
    
    // XDG Shell interface (manually declared - normally generated from XML)
    private const string XdgShellLib = "libwayland-client.so.0";
    
    // XDG WM Base functions
    [DllImport(XdgShellLib)]
    public static extern void xdg_wm_base_destroy(IntPtr wm_base);
    
    [DllImport(XdgShellLib)]
    public static extern IntPtr xdg_wm_base_create_positioner(IntPtr wm_base);
    
    [DllImport(XdgShellLib)]
    public static extern IntPtr xdg_wm_base_get_xdg_surface(IntPtr wm_base, IntPtr surface);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_wm_base_pong(IntPtr wm_base, uint serial);
    
    // XDG Surface functions
    [DllImport(XdgShellLib)]
    public static extern void xdg_surface_destroy(IntPtr xdg_surface);
    
    [DllImport(XdgShellLib)]
    public static extern IntPtr xdg_surface_get_toplevel(IntPtr xdg_surface);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_surface_ack_configure(IntPtr xdg_surface, uint serial);
    
    // XDG Toplevel functions
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_destroy(IntPtr toplevel);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_set_title(IntPtr toplevel, string title);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_set_app_id(IntPtr toplevel, string app_id);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_set_fullscreen(IntPtr toplevel, IntPtr output);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_unset_fullscreen(IntPtr toplevel);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_set_maximized(IntPtr toplevel);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_unset_maximized(IntPtr toplevel);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_set_minimized(IntPtr toplevel);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_set_parent(IntPtr toplevel, IntPtr parent);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_set_positioner(IntPtr toplevel, IntPtr positioner);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_set_size(IntPtr toplevel, int width, int height);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_set_min_size(IntPtr toplevel, int width, int height);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_set_max_size(IntPtr toplevel, int width, int height);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_move(IntPtr toplevel, IntPtr seat, uint serial);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_resize(IntPtr toplevel, IntPtr seat, uint serial, uint edges);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_toplevel_show_window_menu(IntPtr toplevel, IntPtr seat, uint serial, int x, int y);
    
    // XDG Positioner functions
    [DllImport(XdgShellLib)]
    public static extern void xdg_positioner_destroy(IntPtr positioner);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_positioner_set_size(IntPtr positioner, int width, int height);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_positioner_set_anchor_rect(IntPtr positioner, int x, int y, int width, int height);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_positioner_set_anchor(IntPtr positioner, uint anchor);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_positioner_set_gravity(IntPtr positioner, uint gravity);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_positioner_set_constraint_adjustment(IntPtr positioner, uint constraint_adjustment);
    
    [DllImport(XdgShellLib)]
    public static extern void xdg_positioner_set_offset(IntPtr positioner, int x, int y);
    
    // Pointer constraints (for cursor capture)
    private const string PointerConstraintsLib = "libwayland-client.so.0";
    
    [DllImport(PointerConstraintsLib)]
    public static extern IntPtr zwp_pointer_constraints_v1_lock_pointer(
        IntPtr constraints,
        IntPtr surface,
        IntPtr pointer,
        IntPtr region,
        uint lifetime);
    
    [DllImport(PointerConstraintsLib)]
    public static extern IntPtr zwp_pointer_constraints_v1_confine_pointer(
        IntPtr constraints,
        IntPtr surface,
        IntPtr pointer,
        IntPtr region,
        uint lifetime);
    
    [DllImport(PointerConstraintsLib)]
    public static extern void zwp_pointer_constraints_v1_destroy(IntPtr constraints);
    
    // Relative pointer
    [DllImport(PointerConstraintsLib)]
    public static extern IntPtr zwp_relative_pointer_manager_v1_get_relative_pointer(
        IntPtr manager,
        IntPtr pointer);
    
    [DllImport(PointerConstraintsLib)]
    public static extern void zwp_relative_pointer_manager_v1_destroy(IntPtr manager);
    
    // Helper to check if Wayland is available
    public static bool IsWaylandAvailable()
    {
        var display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        return !string.IsNullOrEmpty(display);
    }
    
    // XDG Shell listener callbacks (these need to be implemented in managed code)
    public static class XdgWmBaseEvents
    {
        public const uint Ping = 0;
    }
    
    public static class XdgSurfaceEvents
    {
        public const uint Configure = 0;
    }
    
    public static class XdgToplevelEvents
    {
        public const uint Configure = 0;
        public const uint Close = 1;
    }
    
    // Wl Seat events
    public static class WlSeatEvents
    {
        public const uint Capabilities = 0;
        public const uint Name = 1;
    }
    
    // Wl Pointer events
    public static class WlPointerEvents
    {
        public const uint Enter = 0;
        public const uint Leave = 1;
        public const uint Motion = 2;
        public const uint Button = 3;
        public const uint Axis = 4;
        public const uint Frame = 5;
        public const uint AxisSource = 6;
        public const uint AxisStop = 7;
        public const uint AxisDiscrete = 8;
    }
    
    // Wl Keyboard events
    public static class WlKeyboardEvents
    {
        public const uint Keymap = 0;
        public const uint Enter = 1;
        public const uint Leave = 2;
        public const uint Key = 3;
        public const uint Modifiers = 4;
        public const uint RepeatInfo = 5;
    }
}