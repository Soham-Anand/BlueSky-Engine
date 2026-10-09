using System.Numerics;

namespace BlueSky.Platform.Linux;

/// <summary>
/// Minimal native X11 window. This keeps Linux support GLFW-free and gives
/// Vulkan surfaces and Linux input a real native window to target.
/// </summary>
public sealed class X11Window : IWindow
{
    private readonly System.Diagnostics.Stopwatch _timer = System.Diagnostics.Stopwatch.StartNew();
    private nint _display;
    private ulong _window;
    private ulong _wmProtocolsAtom;
    private ulong _wmDeleteWindowAtom;
    private ulong _rootWindow;
    private ulong _netWmStateAtom;
    private ulong _netWmStateFullscreenAtom;
    private readonly bool _fullscreenRequested;
    private bool _fullscreenApplied;
    private string _title = string.Empty;
    private Vector2 _size;
    private Vector2 _position;
    private bool _visible;
    private bool _focused;
    private double _lastTime;

    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? string.Empty;
            if (_display != 0 && _window != 0)
                X11Interop.XStoreName(_display, _window, _title);
        }
    }
    public Vector2 Size
    {
        get => _size;
        set
        {
            _size = new Vector2(MathF.Max(1, value.X), MathF.Max(1, value.Y));
            if (_display != 0 && _window != 0)
                X11Interop.XResizeWindow(_display, _window, (uint)_size.X, (uint)_size.Y);
        }
    }
    public Vector2 Position
    {
        get => _position;
        set
        {
            _position = value;
            if (_display != 0 && _window != 0)
                X11Interop.XMoveWindow(_display, _window, (int)value.X, (int)value.Y);
        }
    }
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
    internal event Action<X11Interop.XEvent>? X11Event;

    public X11Window(WindowOptions options)
    {
        _title = options.Title ?? string.Empty;
        _size = new Vector2(Math.Max(1, options.Width), Math.Max(1, options.Height));
        _fullscreenRequested = options.Fullscreen;

        _display = X11Interop.XOpenDisplay(null);
        if (_display == 0)
            throw new PlatformNotSupportedException("X11 display is unavailable. Set DISPLAY or run under XWayland.");

        int screen = X11Interop.XDefaultScreen(_display);
        ulong root = X11Interop.XRootWindow(_display, screen);
        _rootWindow = root;
        _netWmStateAtom = X11Interop.XInternAtom(_display, "_NET_WM_STATE", false);
        _netWmStateFullscreenAtom = X11Interop.XInternAtom(_display, "_NET_WM_STATE_FULLSCREEN", false);
        int x = options.Fullscreen ? 0 : options.X == -1 ? Math.Max(0, (X11Interop.XDisplayWidth(_display, screen) - (int)_size.X) / 2) : options.X;
        int y = options.Fullscreen ? 0 : options.Y == -1 ? Math.Max(0, (X11Interop.XDisplayHeight(_display, screen) - (int)_size.Y) / 2) : options.Y;
        _position = new Vector2(x, y);
        _window = X11Interop.XCreateSimpleWindow(
            _display,
            root,
            x,
            y,
            (uint)_size.X,
            (uint)_size.Y,
            0,
            X11Interop.XBlackPixel(_display, screen),
            X11Interop.XWhitePixel(_display, screen));

        X11Interop.XStoreName(_display, _window, _title);
        if (!options.Resizable && !options.Fullscreen)
        {
            var sizeHints = new X11Interop.XSizeHints
            {
                Flags = (nint)((1L << 4) | (1L << 5)), // PMinSize | PMaxSize
                MinWidth = (int)_size.X,
                MinHeight = (int)_size.Y,
                MaxWidth = (int)_size.X,
                MaxHeight = (int)_size.Y
            };
            X11Interop.XSetWMNormalHints(_display, _window, ref sizeHints);
        }
        _wmProtocolsAtom = X11Interop.XInternAtom(_display, "WM_PROTOCOLS", false);
        _wmDeleteWindowAtom = X11Interop.XInternAtom(_display, "WM_DELETE_WINDOW", false);
        if (_wmProtocolsAtom != 0 && _wmDeleteWindowAtom != 0)
            X11Interop.XSetWMProtocols(_display, _window, new[] { _wmDeleteWindowAtom }, 1);
        X11Interop.XSelectInput(_display, _window, (nint)(
            X11Interop.StructureNotifyMask |
            X11Interop.KeyPressMask |
            X11Interop.KeyReleaseMask |
            X11Interop.ButtonPressMask |
            X11Interop.ButtonReleaseMask |
            X11Interop.PointerMotionMask |
            X11Interop.FocusChangeMask));

        Console.WriteLine("[Linux/X11] Native window created");
        if (options.StartVisible)
            Show();
    }

    public void Show()
    {
        X11Interop.XMapWindow(_display, _window);
        if (_fullscreenRequested && !_fullscreenApplied)
        {
            RequestFullscreen();
            _fullscreenApplied = true;
        }
        X11Interop.XFlush(_display);
        _visible = true;
    }

    private void RequestFullscreen()
    {
        if (_netWmStateAtom == 0 || _netWmStateFullscreenAtom == 0)
            return;

        var message = new X11Interop.XEvent
        {
            ClientMessage = new X11Interop.XClientMessageEvent
            {
                Type = X11Interop.ClientMessage,
                SendEvent = true,
                Display = _display,
                Window = _window,
                MessageType = _netWmStateAtom,
                Format = 32,
                Data0 = (nint)1, // _NET_WM_STATE_ADD
                Data1 = (nint)_netWmStateFullscreenAtom,
                Data3 = (nint)1 // application request
            }
        };
        nint mask = (nint)(X11Interop.SubstructureNotifyMask | X11Interop.SubstructureRedirectMask);
        X11Interop.XSendEvent(_display, _rootWindow, false, mask, ref message);
    }

    public void Hide()
    {
        X11Interop.XUnmapWindow(_display, _window);
        X11Interop.XFlush(_display);
        _visible = false;
    }

    public void Close()
    {
        if (IsClosing) return;
        IsClosing = true;
        Closing?.Invoke();
    }

    public void ProcessEvents()
    {
        while (_display != 0 && X11Interop.XPending(_display) > 0)
        {
            var ev = new X11Interop.XEvent();
            X11Interop.XNextEvent(_display, ref ev);
            ProcessWindowEvent(ev);
            X11Event?.Invoke(ev);
        }

        var currentTime = Time;
        var dt = currentTime - _lastTime;
        _lastTime = currentTime;
        Update?.Invoke(dt);
        Render?.Invoke(dt);
    }

    private void ProcessWindowEvent(X11Interop.XEvent ev)
    {
        switch (ev.Type)
        {
            case X11Interop.ClientMessage:
                if (ev.ClientMessage.MessageType == _wmProtocolsAtom &&
                    (ulong)ev.ClientMessage.Data0 == _wmDeleteWindowAtom)
                    Close();
                break;
            case X11Interop.DestroyNotify:
                Close();
                break;
            case X11Interop.ConfigureNotify:
                Vector2 newPosition = new(ev.Configure.X, ev.Configure.Y);
                Vector2 newSize = new(Math.Max(1, ev.Configure.Width), Math.Max(1, ev.Configure.Height));
                _position = newPosition;
                if (_size != newSize)
                {
                    _size = newSize;
                    Resize?.Invoke(newSize);
                    FramebufferResize?.Invoke(newSize);
                }
                break;
            case X11Interop.FocusIn:
                if (!_focused)
                {
                    _focused = true;
                    FocusGained?.Invoke();
                }
                break;
            case X11Interop.FocusOut:
                if (_focused)
                {
                    _focused = false;
                    FocusLost?.Invoke();
                }
                break;
        }
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
            // A zeroed 1x1 bitmap used as both source and mask yields a transparent cursor.
            var bitmap = X11Interop.XCreateBitmapFromData(_display, _window, new byte[] { 0 }, 1, 1);
            if (bitmap != 0)
            {
                var foreground = new X11Interop.XColor();
                var background = new X11Interop.XColor();
                var blankCursor = X11Interop.XCreatePixmapCursor(_display, bitmap, bitmap,
                    ref foreground, ref background, 0, 0);
                if (blankCursor != 0)
                {
                    X11Interop.XDefineCursor(_display, _window, blankCursor);
                    X11Interop.XFreeCursor(_display, blankCursor);
                }
                X11Interop.XFreePixmap(_display, bitmap);
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
                1, // GrabModeAsync: keep delivering pointer events to the application.
                1, // GrabModeAsync
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
