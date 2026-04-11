# Design Document: Native Windowing System

## 1. Overview

This document describes the technical architecture for replacing Silk.NET with native windowing APIs in the BlueSky Engine. The design provides platform-specific window implementations (macOS Cocoa, Windows Win32, Linux X11/Wayland) with a unified cross-platform abstraction layer that integrates seamlessly with bgfx rendering.

### 1.1 Design Goals

- Remove all Silk.NET dependencies from the codebase
- Provide native window creation that works with bgfx on all platforms
- Maintain a clean cross-platform abstraction for engine code
- Support native input handling (keyboard, mouse, touch)
- Integrate with existing RendererDetection and NotBSUI systems
- Minimize performance overhead in event processing

### 1.2 Architecture Principles

- **Platform Abstraction**: Engine code interacts only with `IWindow` interface
- **Native Implementation**: Each platform uses its native windowing APIs
- **Lazy Initialization**: Platform-specific code loaded only when needed
- **Resource Safety**: RAII-style resource management with proper cleanup
- **Event-Driven**: Asynchronous event model for window and input events

## 2. System Architecture

### 2.1 Component Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    BlueSky.Editor                           │
│                    (Program.cs)                             │
└────────────────────────┬────────────────────────────────────┘
                         │
                         │ Uses IWindow
                         ▼
┌─────────────────────────────────────────────────────────────┐
│              BlueSky.Platform (New Project)                 │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐  │
│  │           IWindow (Interface)                        │  │
│  │  - Size, Position, Title                            │  │
│  │  - Show(), Hide(), Close()                          │  │
│  │  - GetNativeHandle()                                │  │
│  │  - Events: Resize, Focus, Close, etc.              │  │
│  └──────────────────────────────────────────────────────┘  │
│                         │                                   │
│         ┌───────────────┼───────────────┐                  │
│         │               │               │                   │
│         ▼               ▼               ▼                   │
│  ┌──────────┐   ┌──────────┐   ┌──────────┐              │
│  │  macOS   │   │ Windows  │   │  Linux   │              │
│  │  Window  │   │  Window  │   │  Window  │              │
│  └──────────┘   └──────────┘   └──────────┘              │
│       │               │               │                     │
│       ▼               ▼               ▼                     │
│  ┌──────────┐   ┌──────────┐   ┌──────────┐              │
│  │  Cocoa   │   │  Win32   │   │   X11    │              │
│  │  APIs    │   │  APIs    │   │  APIs    │              │
│  └──────────┘   └──────────┘   └──────────┘              │
└─────────────────────────────────────────────────────────────┘
                         │
                         │ Provides native handle
                         ▼
┌─────────────────────────────────────────────────────────────┐
│              BlueSky.Rendering                              │
│              (BgfxRenderer)                                 │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 Project Structure

```
BlueSky.Platform/
├── IWindow.cs                    # Cross-platform window interface
├── IInputContext.cs              # Cross-platform input interface
├── WindowOptions.cs              # Window creation options
├── WindowFactory.cs              # Platform-specific factory
├── Events/
│   ├── WindowEvent.cs
│   ├── KeyboardEvent.cs
│   ├── MouseEvent.cs
│   └── TouchEvent.cs
├── Input/
│   ├── KeyCode.cs                # Unified key code enumeration
│   ├── MouseButton.cs
│   └── ModifierKeys.cs
├── macOS/
│   ├── CocoaWindow.cs            # NSWindow wrapper
│   ├── CocoaInput.cs             # macOS input handling
│   ├── CocoaInterop.cs           # Objective-C P/Invoke
│   └── CocoaEventLoop.cs         # NSApplication event loop
├── Windows/
│   ├── Win32Window.cs            # HWND wrapper
│   ├── Win32Input.cs             # Windows input handling
│   ├── Win32Interop.cs           # Win32 P/Invoke
│   └── Win32EventLoop.cs         # GetMessage/DispatchMessage
└── Linux/
    ├── X11Window.cs              # X11 Window wrapper
    ├── X11Input.cs               # X11 input handling
    ├── X11Interop.cs             # X11 P/Invoke
    └── X11EventLoop.cs           # XPending/XNextEvent
```

## 3. Core Interfaces

### 3.1 IWindow Interface

```csharp
public interface IWindow : IDisposable
{
    // Properties
    string Title { get; set; }
    Vector2i Size { get; set; }
    Vector2i Position { get; set; }
    Vector2i FramebufferSize { get; }
    bool IsVisible { get; }
    bool IsFocused { get; }
    bool IsClosing { get; }
    
    // Methods
    void Show();
    void Hide();
    void Close();
    void ProcessEvents();
    IntPtr GetNativeHandle();
    
    // Events
    event Action<Vector2i> Resize;
    event Action<Vector2i> FramebufferResize;
    event Action FocusGained;
    event Action FocusLost;
    event Action Closing;
    event Action<double> Update;
    event Action<double> Render;
}
```

### 3.2 IInputContext Interface

```csharp
public interface IInputContext : IDisposable
{
    // Keyboard
    bool IsKeyDown(KeyCode key);
    bool IsKeyPressed(KeyCode key);
    bool IsKeyReleased(KeyCode key);
    ModifierKeys GetModifiers();
    
    // Mouse
    Vector2 MousePosition { get; }
    bool IsMouseButtonDown(MouseButton button);
    bool IsMouseButtonPressed(MouseButton button);
    bool IsMouseButtonReleased(MouseButton button);
    Vector2 MouseDelta { get; }
    Vector2 ScrollDelta { get; }
    
    // Events
    event Action<KeyCode, ModifierKeys> KeyDown;
    event Action<KeyCode, ModifierKeys> KeyUp;
    event Action<char> CharInput;
    event Action<MouseButton> MouseDown;
    event Action<MouseButton> MouseUp;
    event Action<Vector2> MouseMove;
    event Action<Vector2> MouseScroll;
}
```

### 3.3 WindowOptions

```csharp
public struct WindowOptions
{
    public string Title;
    public int Width;
    public int Height;
    public int X;
    public int Y;
    public bool VSync;
    public bool Resizable;
    public bool Fullscreen;
    public bool StartVisible;
    
    public static WindowOptions Default => new()
    {
        Title = "BlueSky Engine",
        Width = 1280,
        Height = 720,
        X = -1,  // Centered
        Y = -1,  // Centered
        VSync = true,
        Resizable = true,
        Fullscreen = false,
        StartVisible = true
    };
}
```

## 4. Platform-Specific Implementations

### 4.1 macOS (Cocoa) Implementation

#### 4.1.1 CocoaWindow Class

```csharp
public class CocoaWindow : IWindow
{
    private IntPtr _nsWindow;
    private IntPtr _nsView;
    private IntPtr _metalLayer;
    private CocoaEventLoop _eventLoop;
    private CocoaInput _input;
    private bool _disposed;
    
    public CocoaWindow(WindowOptions options)
    {
        // Create NSWindow
        _nsWindow = CreateNSWindow(options);
        
        // Create NSView for rendering
        _nsView = CreateContentView();
        
        // Create CAMetalLayer with proper pixel format
        _metalLayer = CreateMetalLayer();
        
        // Initialize event loop
        _eventLoop = new CocoaEventLoop(this);
        
        // Initialize input handling
        _input = new CocoaInput(_nsWindow, _nsView);
        
        if (options.StartVisible)
            Show();
    }
    
    public IntPtr GetNativeHandle() => _nsView;  // bgfx needs NSView
    
    private IntPtr CreateNSWindow(WindowOptions options)
    {
        // NSWindow creation using Objective-C runtime
        // Style: Titled, Closable, Miniaturizable, Resizable
        // ...
    }
    
    private IntPtr CreateMetalLayer()
    {
        // Create CAMetalLayer with BGRA8Unorm pixel format (80)
        // Attach to NSView
        // ...
    }
}
```

#### 4.1.2 Objective-C Interop

```csharp
internal static class CocoaInterop
{
    private const string ObjCLib = "/usr/lib/libobjc.A.dylib";
    
    [DllImport(ObjCLib)]
    public static extern IntPtr objc_getClass(string name);
    
    [DllImport(ObjCLib)]
    public static extern IntPtr sel_registerName(string name);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
    
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    public static extern void objc_msgSend_void_bool(IntPtr receiver, IntPtr selector, bool value);
    
    // Additional msgSend variants for different signatures
    // ...
    
    // Helper methods
    public static IntPtr GetClass(string name) => objc_getClass(name);
    public static IntPtr GetSelector(string name) => sel_registerName(name);
}
```

#### 4.1.3 Event Loop

```csharp
public class CocoaEventLoop
{
    private readonly CocoaWindow _window;
    private IntPtr _nsApp;
    private bool _running;
    
    public void Run()
    {
        _nsApp = CocoaInterop.GetSharedApplication();
        _running = true;
        
        while (_running)
        {
            ProcessEvents();
            _window.OnUpdate?.Invoke(GetDeltaTime());
            _window.OnRender?.Invoke(GetDeltaTime());
        }
    }
    
    public void ProcessEvents()
    {
        // Poll NSEvents using nextEventMatchingMask
        // Dispatch to appropriate handlers
        // ...
    }
}
```

### 4.2 Windows (Win32) Implementation

#### 4.2.1 Win32Window Class

```csharp
public class Win32Window : IWindow
{
    private IntPtr _hwnd;
    private Win32EventLoop _eventLoop;
    private Win32Input _input;
    private bool _disposed;
    
    public Win32Window(WindowOptions options)
    {
        // Register window class
        RegisterWindowClass();
        
        // Create window using CreateWindowEx
        _hwnd = CreateWindowHandle(options);
        
        // Initialize event loop
        _eventLoop = new Win32EventLoop(this);
        
        // Initialize input
        _input = new Win32Input(_hwnd);
        
        if (options.StartVisible)
            Show();
    }
    
    public IntPtr GetNativeHandle() => _hwnd;  // bgfx needs HWND
    
    private void RegisterWindowClass()
    {
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            style = CS_HREDRAW | CS_VREDRAW | CS_OWNDC,
            lpfnWndProc = WndProc,
            hInstance = GetModuleHandle(null),
            lpszClassName = "BlueSkyWindowClass"
        };
        
        RegisterClassEx(ref wc);
    }
    
    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Handle WM_SIZE, WM_CLOSE, WM_KEYDOWN, WM_MOUSEMOVE, etc.
        // Dispatch to event handlers
        // ...
    }
}
```

#### 4.2.2 Win32 Interop

```csharp
internal static class Win32Interop
{
    private const string User32 = "user32.dll";
    private const string Kernel32 = "kernel32.dll";
    
    [DllImport(User32)]
    public static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
    
    [DllImport(User32)]
    public static extern bool DestroyWindow(IntPtr hwnd);
    
    [DllImport(User32)]
    public static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);
    
    [DllImport(User32)]
    public static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    
    [DllImport(User32)]
    public static extern bool TranslateMessage(ref MSG lpMsg);
    
    [DllImport(User32)]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);
    
    // Additional Win32 API declarations
    // ...
}
```

### 4.3 Linux (X11) Implementation

#### 4.3.1 X11Window Class

```csharp
public class X11Window : IWindow
{
    private IntPtr _display;
    private IntPtr _window;
    private X11EventLoop _eventLoop;
    private X11Input _input;
    private bool _disposed;
    
    public X11Window(WindowOptions options)
    {
        // Open X11 display
        _display = X11Interop.XOpenDisplay(null);
        if (_display == IntPtr.Zero)
            throw new Exception("Failed to open X11 display");
        
        // Create X11 window
        _window = CreateX11Window(options);
        
        // Initialize event loop
        _eventLoop = new X11EventLoop(this);
        
        // Initialize input
        _input = new X11Input(_display, _window);
        
        if (options.StartVisible)
            Show();
    }
    
    public IntPtr GetNativeHandle() => _window;  // bgfx needs X11 Window
    
    private IntPtr CreateX11Window(WindowOptions options)
    {
        int screen = X11Interop.XDefaultScreen(_display);
        IntPtr rootWindow = X11Interop.XRootWindow(_display, screen);
        
        IntPtr window = X11Interop.XCreateSimpleWindow(
            _display, rootWindow,
            options.X, options.Y, options.Width, options.Height,
            1, 0, 0);
        
        // Select input events
        X11Interop.XSelectInput(_display, window, 
            EventMask.KeyPressMask | EventMask.KeyReleaseMask |
            EventMask.ButtonPressMask | EventMask.ButtonReleaseMask |
            EventMask.PointerMotionMask | EventMask.StructureNotifyMask);
        
        return window;
    }
}
```

## 5. Input System Design

### 5.1 Unified KeyCode Enumeration

```csharp
public enum KeyCode
{
    // Letters
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    
    // Numbers
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    
    // Function keys
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    
    // Modifiers
    LeftShift, RightShift, LeftControl, RightControl,
    LeftAlt, RightAlt, LeftSuper, RightSuper,
    
    // Navigation
    Up, Down, Left, Right,
    Home, End, PageUp, PageDown,
    
    // Editing
    Backspace, Delete, Insert, Tab, Enter, Escape, Space,
    
    // ... additional keys
}
```

### 5.2 Platform Key Mapping

Each platform implementation provides a mapping from native key codes to the unified `KeyCode` enumeration:

- **macOS**: NSEvent keyCode → KeyCode
- **Windows**: Virtual-Key Codes → KeyCode
- **Linux**: X11 KeySym → KeyCode

### 5.3 Input State Tracking

```csharp
internal class InputState
{
    private readonly HashSet<KeyCode> _keysDown = new();
    private readonly HashSet<KeyCode> _keysPressed = new();
    private readonly HashSet<KeyCode> _keysReleased = new();
    
    private readonly HashSet<MouseButton> _buttonsDown = new();
    private readonly HashSet<MouseButton> _buttonsPressed = new();
    private readonly HashSet<MouseButton> _buttonsReleased = new();
    
    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;
    private Vector2 _scrollDelta;
    
    public void BeginFrame()
    {
        _keysPressed.Clear();
        _keysReleased.Clear();
        _buttonsPressed.Clear();
        _buttonsReleased.Clear();
        _mouseDelta = Vector2.Zero;
        _scrollDelta = Vector2.Zero;
    }
    
    public void OnKeyDown(KeyCode key)
    {
        if (!_keysDown.Contains(key))
        {
            _keysDown.Add(key);
            _keysPressed.Add(key);
        }
    }
    
    // ... additional state management methods
}
```

## 6. Integration Points

### 6.1 bgfx Integration

The native window handle is passed to bgfx during initialization:

```csharp
// In BgfxRenderer.cs
public BgfxRenderer(IWindow window)
{
    _window = window;
    Initialize();
}

private void Initialize()
{
    var pd = new PlatformData();
    pd.type = NativeWindowHandleType.Default;
    pd.nwh = (void*)_window.GetNativeHandle();
    
    // bgfx initialization with platform data
    // ...
}
```

### 6.2 NotBSUI Integration

NotBSUI's `InputManager` is updated to use the new `IInputContext`:

```csharp
// In NotBSUI/Input/InputManager.cs
public class InputManager
{
    private readonly IInputContext _input;
    
    public InputManager(IInputContext input)
    {
        _input = input;
        
        // Subscribe to input events
        _input.MouseMove += OnMouseMove;
        _input.MouseDown += OnMouseDown;
        _input.MouseUp += OnMouseUp;
        // ...
    }
    
    public void ProcessInput(UIElement root)
    {
        var mousePos = _input.MousePosition;
        // Hit testing and event dispatch
        // ...
    }
}
```

### 6.3 Editor Integration

The editor's `Program.cs` is updated to use the new windowing system:

```csharp
// In BlueSky.Editor/Program.cs
public static void Main(string[] args)
{
    // Detect renderer
    var backend = RendererDetection.DetectBestRenderer();
    
    // Create window using factory
    var options = WindowOptions.Default;
    options.Title = "BlueSky Engine Editor v2.0";
    options.Width = 1920;
    options.Height = 1080;
    
    _window = WindowFactory.Create(options);
    _input = _window.CreateInput();
    
    // Setup event handlers
    _window.Resize += OnResize;
    _window.Render += OnRender;
    _window.Update += OnUpdate;
    _window.Closing += OnClose;
    
    // Initialize renderer with native window
    _renderer = CreateRenderer(backend, _window);
    
    // Initialize UI
    _uiRenderer = new UIRenderer(255);
    _inputManager = new InputManager(_input);
    
    // Run event loop
    _window.Show();
    while (!_window.IsClosing)
    {
        _window.ProcessEvents();
    }
}
```

## 7. Resource Management

### 7.1 RAII Pattern

All platform-specific resources follow RAII (Resource Acquisition Is Initialization):

```csharp
public class CocoaWindow : IWindow
{
    public void Dispose()
    {
        if (_disposed) return;
        
        // Release CAMetalLayer
        if (_metalLayer != IntPtr.Zero)
        {
            CocoaInterop.Release(_metalLayer);
            _metalLayer = IntPtr.Zero;
        }
        
        // Release NSView
        if (_nsView != IntPtr.Zero)
        {
            CocoaInterop.Release(_nsView);
            _nsView = IntPtr.Zero;
        }
        
        // Close NSWindow
        if (_nsWindow != IntPtr.Zero)
        {
            CocoaInterop.CloseWindow(_nsWindow);
            CocoaInterop.Release(_nsWindow);
            _nsWindow = IntPtr.Zero;
        }
        
        _disposed = true;
    }
}
```

### 7.2 Error Handling

Platform-specific errors are wrapped in descriptive exceptions:

```csharp
private IntPtr CreateNSWindow(WindowOptions options)
{
    IntPtr window = /* ... */;
    
    if (window == IntPtr.Zero)
    {
        throw new PlatformException(
            "Failed to create NSWindow",
            PlatformError.WindowCreationFailed,
            GetLastCocoaError());
    }
    
    return window;
}
```

## 8. Performance Considerations

### 8.1 Event Batching

Input events are batched per frame to reduce overhead:

```csharp
public void ProcessEvents()
{
    _inputState.BeginFrame();
    
    // Process all pending events
    while (HasPendingEvent())
    {
        var evt = GetNextEvent();
        DispatchEvent(evt);
    }
    
    // Dispatch batched events to subscribers
    FlushEvents();
}
```

### 8.2 Memory Allocation

Minimize allocations in hot paths:

- Reuse event objects
- Use struct-based events where possible
- Pool frequently allocated objects

### 8.3 Thread Safety

Window operations are restricted to the main thread:

```csharp
public void SetTitle(string title)
{
    if (!IsMainThread())
    {
        EnqueueMainThreadAction(() => SetTitle(title));
        return;
    }
    
    // Perform actual title change
    // ...
}
```

## 9. Testing Strategy

### 9.1 Unit Tests

- Window lifecycle (create, show, hide, close)
- Property getters/setters
- Event subscription/unsubscription
- Input state tracking

### 9.2 Integration Tests

- bgfx initialization with native handles
- Multi-window scenarios
- Window resize and framebuffer updates
- Input event flow to NotBSUI

### 9.3 Platform-Specific Tests

Each platform implementation includes tests for:
- Native API interop correctness
- Memory leak detection
- Event loop stability
- Input mapping accuracy

## 10. Migration Path

### 10.1 Phase 1: Create BlueSky.Platform Project

- Define interfaces (`IWindow`, `IInputContext`)
- Implement platform-specific windows
- Implement input handling

### 10.2 Phase 2: Update BlueSky.Rendering

- Modify `BgfxRenderer` to accept `IWindow` instead of Silk.NET `IWindow`
- Remove Silk.NET package references
- Test bgfx initialization on all platforms

### 10.3 Phase 3: Update NotBSUI

- Modify `InputManager` to use `IInputContext`
- Remove Silk.NET.Input dependencies
- Test UI input handling

### 10.4 Phase 4: Update BlueSky.Editor

- Replace Silk.NET window creation with `WindowFactory`
- Update event loop to use native event processing
- Remove all Silk.NET references

### 10.5 Phase 5: Testing and Validation

- Test on macOS (Apple Silicon and Intel)
- Test on Windows (DirectX 11)
- Test on Linux (Vulkan and OpenGL)
- Performance profiling and optimization

## 11. Future Enhancements

### 11.1 Wayland Support

Add Wayland backend for Linux alongside X11:

```csharp
public class WaylandWindow : IWindow
{
    // Wayland-specific implementation
}
```

### 11.2 Touch Input

Extend input system to support multi-touch:

```csharp
public interface IInputContext
{
    // Existing members...
    
    // Touch support
    IReadOnlyList<TouchPoint> ActiveTouches { get; }
    event Action<TouchPoint> TouchDown;
    event Action<TouchPoint> TouchMove;
    event Action<TouchPoint> TouchUp;
}
```

### 11.3 High-DPI Support

Add proper high-DPI scaling support:

```csharp
public interface IWindow
{
    // Existing members...
    
    float DpiScale { get; }
    event Action<float> DpiChanged;
}
```

### 11.4 Multiple Window Support

Enhance window management for multi-window scenarios:

```csharp
public class WindowManager
{
    public IWindow CreateWindow(WindowOptions options);
    public IReadOnlyList<IWindow> GetAllWindows();
    public void ProcessAllEvents();
}
```

## 12. Appendix

### 12.1 Platform API References

- **macOS**: [Cocoa Application Layer](https://developer.apple.com/documentation/appkit)
- **Windows**: [Win32 API](https://docs.microsoft.com/en-us/windows/win32/api/)
- **Linux**: [Xlib Programming Manual](https://www.x.org/releases/current/doc/libX11/libX11/libX11.html)

### 12.2 bgfx Platform Integration

- [bgfx Platform Data](https://bkaradzic.github.io/bgfx/internals.html#platform-data)
- [bgfx Examples](https://github.com/bkaradzic/bgfx/tree/master/examples)

### 12.3 Related Design Patterns

- **Factory Pattern**: `WindowFactory` for platform-specific instantiation
- **Strategy Pattern**: Platform-specific event loop implementations
- **Observer Pattern**: Event-driven window and input notifications
- **Adapter Pattern**: Wrapping native APIs with C# interfaces
