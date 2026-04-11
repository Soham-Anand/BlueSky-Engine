using System.Numerics;
using System.Runtime.InteropServices;
using static BlueSky.Platform.Windows.Win32Interop;

namespace BlueSky.Platform.Windows;

public class Win32Window : IWindow
{
    private IntPtr _hwnd;
    private readonly string _className;
    private readonly WndProc _wndProcDelegate;
    private readonly System.Diagnostics.Stopwatch _timer;
    private double _lastTime;
    private bool _isVisible;
    private bool _isFocused;
    
    public string Title { get; set; }
    public Vector2 Size { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 FramebufferSize => Size;
    public bool IsVisible => _isVisible;
    public bool IsFocused => _isFocused;
    public bool IsClosing { get; private set; }
    public double Time => _timer.Elapsed.TotalSeconds;
    
    public event Action<Vector2>? Resize;
    public event Action<Vector2>? FramebufferResize;
    public event Action? FocusGained;
    public event Action? FocusLost;
    public event Action? Closing;
    public event Action<double>? Update;
    public event Action<double>? Render;
    
    public Win32Window(WindowOptions options)
    {
        Title = options.Title;
        Size = new Vector2(options.Width, options.Height);
        _className = $"BlueSkyWindow_{Guid.NewGuid():N}";
        _timer = System.Diagnostics.Stopwatch.StartNew();
        
        _wndProcDelegate = WindowProc;
        
        var hInstance = GetModuleHandleW(null);
        
        var wndClass = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = _className
        };
        
        if (RegisterClassW(ref wndClass) == 0)
            throw new Exception($"Failed to register window class: {Marshal.GetLastWin32Error()}");
        
        _hwnd = CreateWindowExW(
            0, _className, options.Title,
            WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            100, 100, (int)options.Width, (int)options.Height,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        
        if (_hwnd == IntPtr.Zero)
            throw new Exception($"Failed to create window: {Marshal.GetLastWin32Error()}");
        
        ShowWindow(_hwnd, SW_SHOW);
        UpdateWindow(_hwnd);
        
        _isVisible = true;
        Console.WriteLine("[Win32Window] Window created and shown");
    }
    
    public void Show()
    {
        ShowWindow(_hwnd, SW_SHOW);
        _isVisible = true;
    }
    
    public void Hide()
    {
        ShowWindow(_hwnd, SW_HIDE);
        _isVisible = false;
    }
    
    public void Close()
    {
        IsClosing = true;
        Closing?.Invoke();
    }
    
    public void ProcessEvents()
    {
        while (PeekMessageW(out MSG msg, IntPtr.Zero, 0, 0, 1))
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
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
    
    public IntPtr GetNativeHandle() => _hwnd;

    public void SetCursorVisible(bool visible)
    {
        // ShowCursor(FALSE) hides, ShowCursor(TRUE) shows
        ShowCursor(visible ? 1 : 0);
    }

    public void SetCursorCaptured(bool captured)
    {
        // TODO: Implement with ClipCursor/SetCapture for Windows
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
    
    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_CLOSE:
            case WM_DESTROY:
                IsClosing = true;
                Closing?.Invoke();
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }
}
