using System.Numerics;

namespace BlueSky.Platform.Linux;

/// <summary>
/// Minimal native X11 window. This keeps Linux support GLFW-free and gives
/// Vulkan/OpenGL backends a real native window handle to target.
/// </summary>
public sealed class X11Window : IWindow
{
    private readonly System.Diagnostics.Stopwatch _timer = System.Diagnostics.Stopwatch.StartNew();
    private nint _display;
    private ulong _window;
    private bool _visible;
    private bool _focused = true;
    private double _lastTime;

    public string Title { get; set; }
    public Vector2 Size { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 FramebufferSize => Size;
    public bool IsVisible => _visible;
    public bool IsFocused => _focused;
    public bool IsClosing { get; private set; }
    public double Time => _timer.Elapsed.TotalSeconds;

    public event Action<Vector2>? Resize;
    public event Action<Vector2>? FramebufferResize;
    public event Action? FocusGained;
    public event Action? FocusLost;
    public event Action? Closing;
    public event Action<double>? Update;
    public event Action<double>? Render;

    public X11Window(WindowOptions options)
    {
        Title = options.Title;
        Size = new Vector2(options.Width, options.Height);
        Position = Vector2.Zero;

        _display = X11Interop.XOpenDisplay(null);
        if (_display == 0)
            throw new PlatformNotSupportedException("X11 display is unavailable. Set DISPLAY or run under XWayland.");

        int screen = X11Interop.XDefaultScreen(_display);
        ulong root = X11Interop.XRootWindow(_display, screen);
        _window = X11Interop.XCreateSimpleWindow(
            _display,
            root,
            0,
            0,
            (uint)Math.Max(1, options.Width),
            (uint)Math.Max(1, options.Height),
            0,
            X11Interop.XBlackPixel(_display, screen),
            X11Interop.XWhitePixel(_display, screen));

        X11Interop.XStoreName(_display, _window, Title);
        X11Interop.XSelectInput(_display, _window, (nint)(
            X11Interop.ExposureMask |
            X11Interop.StructureNotifyMask |
            X11Interop.KeyPressMask |
            X11Interop.KeyReleaseMask |
            X11Interop.ButtonPressMask |
            X11Interop.ButtonReleaseMask |
            X11Interop.PointerMotionMask));

        Console.WriteLine("[Linux/X11] Native window created");
    }

    public void Show()
    {
        X11Interop.XMapWindow(_display, _window);
        X11Interop.XFlush(_display);
        _visible = true;
    }

    public void Hide()
    {
        X11Interop.XUnmapWindow(_display, _window);
        X11Interop.XFlush(_display);
        _visible = false;
    }

    public void Close()
    {
        IsClosing = true;
        Closing?.Invoke();
    }

    public void ProcessEvents()
    {
        while (_display != 0 && X11Interop.XPending(_display) > 0)
        {
            var ev = new X11Interop.XEvent();
            X11Interop.XNextEvent(_display, ref ev);
        }

        var currentTime = Time;
        var dt = currentTime - _lastTime;
        _lastTime = currentTime;
        Update?.Invoke(dt);
        Render?.Invoke(dt);
    }

    public nint GetNativeHandle() => (nint)_window;

    public nint GetDisplayHandle() => _display;

    public void SetCursorVisible(bool visible)
    {
        if (_display == 0 || _window == 0)
            return;
        
        if (visible)
        {
            // Undefine cursor (use default)
            X11Interop.XUndefineCursor(_display, _window);
        }
        else
        {
            // Create blank cursor
            var blankCursor = X11Interop.XCreateFontCursor(_display, 0); // XC_arrow = 0, but we want invisible
            if (blankCursor != 0)
            {
                X11Interop.XDefineCursor(_display, _window, blankCursor);
                X11Interop.XFreeCursor(_display, blankCursor);
            }
        }
        
        X11Interop.XFlush(_display);
    }

    public void SetCursorCaptured(bool captured)
    {
        if (_display == 0 || _window == 0)
            return;
        
        if (captured)
        {
            // Grab pointer to window
            X11Interop.XGrabPointer(
                _display,
                _window,
                true,
                (nint)(X11Interop.ButtonPressMask | X11Interop.ButtonReleaseMask | X11Interop.PointerMotionMask),
                0, // GrabModeSync
                0, // PointerModeSync
                _window,
                0,
                0);
        }
        else
        {
            // Ungrab pointer
            X11Interop.XUngrabPointer(_display, 0);
        }
        
        X11Interop.XFlush(_display);
    }

    public void Dispose()
    {
        if (_display != 0)
        {
            if (_window != 0)
            {
                X11Interop.XDestroyWindow(_display, _window);
                _window = 0;
            }

            X11Interop.XCloseDisplay(_display);
            _display = 0;
        }
    }
}
