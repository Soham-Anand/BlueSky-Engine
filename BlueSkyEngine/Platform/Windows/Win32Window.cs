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
    
    internal event Action<uint, IntPtr, IntPtr>? OnMessage;
    
    /// <summary>
    /// Fired when files are dragged and dropped onto the window.
    /// </summary>
    public event Action<string[]>? FilesDropped;
    
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
        
        // Use WS_EX_ACCEPTFILES to enable drag-and-drop
        _hwnd = CreateWindowExW(
            WS_EX_ACCEPTFILES, _className, options.Title,
            WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            100, 100, (int)options.Width, (int)options.Height,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        
        if (_hwnd == IntPtr.Zero)
            throw new Exception($"Failed to create window: {Marshal.GetLastWin32Error()}");
        
        // Enable drag-and-drop file acceptance
        DragAcceptFiles(_hwnd, true);
        
        ShowWindow(_hwnd, SW_SHOW);
        UpdateWindow(_hwnd);
        
        _isVisible = true;
        Console.WriteLine("[Win32Window] Window created and shown (drag-drop enabled)");
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
    
    /// <summary>
    /// Show a native Win32 file open dialog. Returns selected file paths, or null if cancelled.
    /// Uses GetOpenFileNameW from comdlg32.dll — no WinForms dependency.
    /// </summary>
    public string[]? ShowOpenFileDialog()
    {
        const int maxBuffer = 8192; // Support multiple file selection
        IntPtr fileBuffer = Marshal.AllocHGlobal(maxBuffer * 2); // Unicode = 2 bytes per char
        
        try
        {
            // Zero the buffer
            unsafe
            {
                new Span<byte>((void*)fileBuffer, maxBuffer * 2).Clear();
            }
            
            var ofn = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                hwndOwner = _hwnd,
                lpstrFilter = "3D Models (*.obj;*.fbx;*.gltf;*.glb)\0*.obj;*.fbx;*.gltf;*.glb\0" +
                              "Images (*.png;*.jpg;*.jpeg;*.bmp;*.tga)\0*.png;*.jpg;*.jpeg;*.bmp;*.tga\0" +
                              "BlueSky Assets (*.blueskyasset)\0*.blueskyasset\0" +
                              "All Files (*.*)\0*.*\0\0",
                lpstrFile = fileBuffer,
                nMaxFile = maxBuffer,
                lpstrTitle = "Import Assets — BlueSky Engine",
                Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_ALLOWMULTISELECT | OFN_EXPLORER | OFN_NOCHANGEDIR,
            };
            
            if (GetOpenFileNameW(ref ofn))
            {
                // Parse the result buffer.
                // Single file: full path as one null-terminated string
                // Multiple files: directory\0file1\0file2\0\0
                var results = new List<string>();
                
                unsafe
                {
                    char* ptr = (char*)fileBuffer;
                    var strings = new List<string>();
                    
                    while (true)
                    {
                        string s = new string(ptr);
                        if (s.Length == 0) break;
                        strings.Add(s);
                        ptr += s.Length + 1;
                    }
                    
                    if (strings.Count == 1)
                    {
                        // Single file selected
                        results.Add(strings[0]);
                    }
                    else if (strings.Count > 1)
                    {
                        // Multiple files: first string is directory, rest are filenames
                        string dir = strings[0];
                        for (int i = 1; i < strings.Count; i++)
                        {
                            results.Add(System.IO.Path.Combine(dir, strings[i]));
                        }
                    }
                }
                
                return results.Count > 0 ? results.ToArray() : null;
            }
            
            return null; // User cancelled
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Win32Window] File dialog error: {ex.Message}");
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
        }
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
        OnMessage?.Invoke(msg, wParam, lParam);
        
        switch (msg)
        {
            case WM_CLOSE:
            case WM_DESTROY:
                IsClosing = true;
                Closing?.Invoke();
                PostQuitMessage(0);
                return IntPtr.Zero;
                
            case WM_SIZE:
                int newW = (short)(lParam.ToInt64() & 0xFFFF);
                int newH = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                if (newW > 0 && newH > 0)
                {
                    Size = new Vector2(newW, newH);
                    Resize?.Invoke(Size);
                    FramebufferResize?.Invoke(Size);
                }
                return IntPtr.Zero;
                
            case WM_DROPFILES:
                HandleDropFiles(wParam);
                return IntPtr.Zero;
        }
        
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }
    
    /// <summary>
    /// Extract file paths from a WM_DROPFILES message and fire the FilesDropped event.
    /// </summary>
    private void HandleDropFiles(IntPtr hDrop)
    {
        try
        {
            // Query file count (pass 0xFFFFFFFF to get count)
            uint fileCount = DragQueryFileW(hDrop, 0xFFFFFFFF, null, 0);
            
            if (fileCount == 0) return;
            
            var files = new string[fileCount];
            var buffer = new char[1024];
            
            for (uint i = 0; i < fileCount; i++)
            {
                uint charsCopied = DragQueryFileW(hDrop, i, buffer, (uint)buffer.Length);
                if (charsCopied > 0)
                {
                    files[i] = new string(buffer, 0, (int)charsCopied);
                }
                else
                {
                    files[i] = string.Empty;
                }
            }
            
            DragFinish(hDrop);
            
            // Filter out empty strings
            var validFiles = files.Where(f => !string.IsNullOrEmpty(f)).ToArray();
            
            if (validFiles.Length > 0)
            {
                Console.WriteLine($"[Win32Window] Files dropped: {validFiles.Length}");
                foreach (var f in validFiles)
                    Console.WriteLine($"  → {f}");
                    
                FilesDropped?.Invoke(validFiles);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Win32Window] Error handling dropped files: {ex.Message}");
        }
    }
}

