using System.Numerics;
using System.Runtime.InteropServices;

namespace BlueSky.Platform.Linux;

/// <summary>
/// Wayland window implementation for Linux
/// Modern Linux display protocol (preferred over X11)
/// </summary>
public class WaylandWindow : IWindow
{
    private IntPtr _display;
    private IntPtr _registry;
    private IntPtr _compositor;
    private IntPtr _wmBase;
    private IntPtr _seat;
    private IntPtr _surface;
    private IntPtr _xdgSurface;
    private IntPtr _xdgToplevel;
    private IntPtr _pointer;
    private IntPtr _keyboard;
    private bool _isVisible;
    private bool _isFocused;
    
    // Event serials for pointer/keyboard
    private uint _lastEnterSerial;
    
    // Input state
    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;
    private Vector2 _scrollDelta;
    
    public string Title { get; set; }
    public Vector2 Size { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 FramebufferSize => Size;
    public bool IsVisible => _isVisible;
    public bool IsFocused => _isFocused;
    public bool IsClosing { get; private set; }
    public double Time => _timer.Elapsed.TotalSeconds;
    
    private readonly System.Diagnostics.Stopwatch _timer;
    private double _lastTime;
    
    public event Action<Vector2>? Resize;
    public event Action<Vector2>? FramebufferResize;
    public event Action? FocusGained;
    public event Action? FocusLost;
    public event Action? Closing;
    public event Action<double>? Update;
    public event Action<double>? Render;
    
    public WaylandWindow(WindowOptions options)
    {
        Title = options.Title;
        Size = new Vector2(options.Width, options.Height);
        _timer = System.Diagnostics.Stopwatch.StartNew();
        
        Console.WriteLine("[Wayland] Initializing Wayland window...");
        
        try
        {
            // 1. Connect to Wayland display
            _display = WaylandInterop.wl_display_connect(null);
            if (_display == IntPtr.Zero)
            {
                throw new PlatformNotSupportedException(
                    "Failed to connect to Wayland display. " +
                    "Check WAYLAND_DISPLAY environment variable or run under XWayland.");
            }
            Console.WriteLine("[Wayland] Display connected");
            
            // 2. Get registry
            _registry = WaylandInterop.wl_display_get_registry(_display);
            Console.WriteLine("[Wayland] Registry obtained");
            
            // 3. Roundtrip to get globals
            WaylandInterop.wl_display_roundtrip(_display);
            
            // 4. Bind to compositor (global 1)
            _compositor = BindToCompositor(1);
            
            // 5. Bind to xdg_wm_base (global 2)  
            _wmBase = BindToXdgWmBase(2);
            
            // 6. Bind to seat (global 3)
            _seat = BindToSeat(3);
            
            // 7. Create surface
            _surface = WaylandInterop.wl_compositor_create_surface(_compositor);
            Console.WriteLine("[Wayland] Surface created");
            
            // 8. Create xdg_surface and xdg_toplevel
            _xdgSurface = WaylandInterop.xdg_wm_base_get_xdg_surface(_wmBase, _surface);
            _xdgToplevel = WaylandInterop.xdg_surface_get_toplevel(_xdgSurface);
            
            WaylandInterop.xdg_toplevel_set_title(_xdgToplevel, Title);
            WaylandInterop.xdg_toplevel_set_app_id(_xdgToplevel, "bluesky-engine");
            Console.WriteLine("[Wayland] XDG shell setup complete");
            
            // 9. Get pointer and keyboard from seat
            if (_seat != IntPtr.Zero)
            {
                _pointer = WaylandInterop.wl_seat_get_pointer(_seat);
                _keyboard = WaylandInterop.wl_seat_get_keyboard(_seat);
                Console.WriteLine("[Wayland] Input devices obtained");
            }
            
            // 10. Set initial size and commit
            WaylandInterop.xdg_toplevel_set_size(_xdgToplevel, options.Width, options.Height);
            WaylandInterop.wl_surface_commit(_surface);
            WaylandInterop.wl_display_flush(_display);
            
            Console.WriteLine($"[Wayland] Window created: {options.Width}x{options.Height}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Wayland] Error: {ex.Message}");
            Dispose();
            throw;
        }
    }
    
    private IntPtr BindToCompositor(uint global)
    {
        var interfaceName = Marshal.StringToHGlobalAnsi("wl_compositor");
        var compositor = WaylandInterop.wl_registry_bind(_registry, global, interfaceName, 4);
        Marshal.FreeHGlobal(interfaceName);
        return compositor;
    }
    
    private IntPtr BindToXdgWmBase(uint global)
    {
        var interfaceName = Marshal.StringToHGlobalAnsi("xdg_wm_base");
        var wmBase = WaylandInterop.wl_registry_bind(_registry, global, interfaceName, 2);
        Marshal.FreeHGlobal(interfaceName);
        
        // Respond to ping events
        WaylandInterop.wl_display_dispatch_pending(_display);
        
        return wmBase;
    }
    
    private IntPtr BindToSeat(uint global)
    {
        var interfaceName = Marshal.StringToHGlobalAnsi("wl_seat");
        var seat = WaylandInterop.wl_registry_bind(_registry, global, interfaceName, 7);
        Marshal.FreeHGlobal(interfaceName);
        return seat;
    }
    
    public void Show()
    {
        WaylandInterop.wl_surface_commit(_surface);
        WaylandInterop.wl_display_flush(_display);
        _isVisible = true;
        Console.WriteLine("[Wayland] Window shown");
    }
    
    public void Hide()
    {
        _isVisible = false;
    }
    
    public void Close()
    {
        IsClosing = true;
        Closing?.Invoke();
    }
    
    public void ProcessEvents()
    {
        // Dispatch pending Wayland events
        WaylandInterop.wl_display_dispatch_pending(_display);
        WaylandInterop.wl_display_flush(_display);
        
        // Pump WM base ping
        if (_wmBase != IntPtr.Zero)
        {
            WaylandInterop.xdg_wm_base_pong(_wmBase, 0);
        }
        
        if (!IsClosing)
        {
            var currentTime = Time;
            var dt = currentTime - _lastTime;
            _lastTime = currentTime;
            Update?.Invoke(dt);
            Render?.Invoke(dt);
        }
    }
    
    public IntPtr GetNativeHandle()
    {
        // Return wl_surface for Vulkan VK_KHR_wayland_surface
        return _surface;
    }
    
    public IntPtr GetWaylandDisplay()
    {
        return _display;
    }

    public void SetCursorVisible(bool visible)
    {
        if (_pointer != IntPtr.Zero)
        {
            // Set to null surface for invisible, or default cursor
            WaylandInterop.wl_pointer_set_cursor(_pointer, _lastEnterSerial, IntPtr.Zero, 0, 0);
        }
    }

    public void SetCursorCaptured(bool captured)
    {
        // Pointer constraints would require binding to zwp_pointer_constraints_v1
        // For now, just log the request
        Console.WriteLine($"[Wayland] Cursor capture {(captured ? "requested" : "released")}");
    }
    
    public void Dispose()
    {
        Console.WriteLine("[Wayland] Cleaning up...");
        
        if (_pointer != IntPtr.Zero)
        {
            WaylandInterop.wl_pointer_release(_pointer);
            _pointer = IntPtr.Zero;
        }
        
        if (_keyboard != IntPtr.Zero)
        {
            WaylandInterop.wl_keyboard_release(_keyboard);
            _keyboard = IntPtr.Zero;
        }
        
        if (_seat != IntPtr.Zero)
        {
            WaylandInterop.wl_seat_destroy(_seat);
            _seat = IntPtr.Zero;
        }
        
        if (_xdgToplevel != IntPtr.Zero)
        {
            WaylandInterop.xdg_toplevel_destroy(_xdgToplevel);
            _xdgToplevel = IntPtr.Zero;
        }
        
        if (_xdgSurface != IntPtr.Zero)
        {
            WaylandInterop.xdg_surface_destroy(_xdgSurface);
            _xdgSurface = IntPtr.Zero;
        }
        
        if (_surface != IntPtr.Zero)
        {
            WaylandInterop.wl_surface_destroy(_surface);
            _surface = IntPtr.Zero;
        }
        
        if (_wmBase != IntPtr.Zero)
        {
            WaylandInterop.xdg_wm_base_destroy(_wmBase);
            _wmBase = IntPtr.Zero;
        }
        
        if (_compositor != IntPtr.Zero)
        {
            WaylandInterop.wl_compositor_destroy(_compositor);
            _compositor = IntPtr.Zero;
        }
        
        if (_registry != IntPtr.Zero)
        {
            WaylandInterop.wl_registry_destroy(_registry);
            _registry = IntPtr.Zero;
        }
        
        if (_display != IntPtr.Zero)
        {
            WaylandInterop.wl_display_disconnect(_display);
            _display = IntPtr.Zero;
        }
        
        Console.WriteLine("[Wayland] Window disposed");
    }
}