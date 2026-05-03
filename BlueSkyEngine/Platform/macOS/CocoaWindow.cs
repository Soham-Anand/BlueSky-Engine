using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Numerics;
using static BlueSky.Platform.macOS.CocoaInterop;

namespace BlueSky.Platform.macOS;

/// <summary>
/// macOS window implementation using Cocoa (NSWindow/NSView).
/// </summary>
public class CocoaWindow : IWindow
{
    private IntPtr _nsWindow;
    private IntPtr _nsView;
    private bool _disposed;
    private bool _isClosing;
    private readonly Stopwatch _stopwatch;
    private CocoaInput? _registeredInput;
    
    private string _title;
    private Vector2 _size;
    private Vector2 _position;
    private bool _isVisible;
    private bool _isFocused;
    private bool _cursorCaptured;
    
    public CocoaWindow(WindowOptions options)
    {
        _stopwatch = Stopwatch.StartNew();
        _title = options.Title;
        _size = new Vector2(options.Width, options.Height);

        // Set activation policy so the app can become active
        var nsApp = GetSharedApplication();
        var setActivationPolicySel = GetSelector("setActivationPolicy:");
        SetActivationPolicy(nsApp, setActivationPolicySel, 0); // NSApplicationActivationPolicyRegular = 0

        // Create NSWindow
        _nsWindow = CreateNSWindow(options);

        // Create NSView for rendering
        _nsView = CreateContentView();

        // DON'T create CAMetalLayer - let bgfx create it!
        // bgfx will create and configure the layer itself when we pass the NSView

        if (options.StartVisible)
            Show();
    }
    
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetActivationPolicy(IntPtr receiver, IntPtr selector, long policy);
    
    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            if (_nsWindow != IntPtr.Zero)
            {
                var nsString = CreateNSString(value);
                var setTitleSel = GetSelector("setTitle:");
                objc_msgSend_void_ptr(_nsWindow, setTitleSel, nsString);
                Release(nsString);
            }
        }
    }
    
    public Vector2 Size
    {
        get => _size;
        set
        {
            _size = value;
            if (_nsWindow != IntPtr.Zero)
            {
                var frameSel = GetSelector("frame");
                var frame = objc_msgSend_CGRect(_nsWindow, frameSel);
                frame.Size = new CGSize(value.X, value.Y);
                
                var setFrameSel = GetSelector("setFrame:display:");
                objc_msgSend_void_rect(_nsWindow, setFrameSel, frame);
            }
        }
    }
    
    public Vector2 Position
    {
        get => _position;
        set
        {
            _position = value;
            if (_nsWindow != IntPtr.Zero)
            {
                var frameSel = GetSelector("frame");
                var frame = objc_msgSend_CGRect(_nsWindow, frameSel);
                frame.Origin = new CGPoint(value.X, value.Y);
                
                var setFrameSel = GetSelector("setFrameOrigin:");
                objc_msgSend_void_rect(_nsWindow, setFrameSel, frame);
            }
        }
    }
    
    public Vector2 FramebufferSize
    {
        get
        {
            if (_nsWindow != IntPtr.Zero)
            {
                var backingScaleFactorSel = GetSelector("backingScaleFactor");
                var scale = objc_msgSend_double(_nsWindow, backingScaleFactorSel);
                return new Vector2(_size.X * (float)scale, _size.Y * (float)scale);
            }
            return _size;
        }
    }
    
    /// <summary>
    /// Registers a CocoaInput so that the event loop can forward mouse/keyboard events.
    /// </summary>
    internal void RegisterInput(CocoaInput input) => _registeredInput = input;
    
    public bool IsVisible => _isVisible;
    
    public bool IsFocused => _isFocused;
    
    public bool IsClosing => _isClosing;
    
    public double Time => _stopwatch.Elapsed.TotalSeconds;
    
    public event Action<Vector2>? Resize;
    public event Action<Vector2>? FramebufferResize;
    public event Action? FocusGained;
    public event Action? FocusLost;
    public event Action? Closing;
    public event Action<double>? Update;
    public event Action<double>? Render;
    public event Action<string[]>? FilesDropped;

    public string[]? ShowOpenFileDialog()
    {
        try
        {
            // Create NSOpenPanel
            var openPanelClass = GetClass("NSOpenPanel");
            var openPanel = objc_msgSend(openPanelClass, GetSelector("openPanel"));

            if (openPanel == IntPtr.Zero)
            {
                Console.WriteLine("[CocoaWindow] Failed to create NSOpenPanel");
                return null;
            }

            // Configure panel
            var setCanChooseFilesSel = GetSelector("setCanChooseFiles:");
            objc_msgSend_void_bool(openPanel, setCanChooseFilesSel, true);

            var setCanChooseDirectoriesSel = GetSelector("setCanChooseDirectories:");
            objc_msgSend_void_bool(openPanel, setCanChooseDirectoriesSel, false);

            var setAllowsMultipleSelectionSel = GetSelector("setAllowsMultipleSelection:");
            objc_msgSend_void_bool(openPanel, setAllowsMultipleSelectionSel, true);

            // Run modal
            var runModalSel = GetSelector("runModal");
            var result = objc_msgSend_int(openPanel, runModalSel);

            if (result == 1) // NSModalResponseOK
            {
                var urlsSel = GetSelector("URLs");
                var urls = objc_msgSend(openPanel, urlsSel);

                if (urls != IntPtr.Zero)
                {
                    var countSel = GetSelector("count");
                    var count = objc_msgSend_int(urls, countSel);

                    var filePaths = new List<string>();
                    for (int i = 0; i < count; i++)
                    {
                        var objectAtIndexSel = GetSelector("objectAtIndex:");
                        var urlObj = objc_msgSend_ptr_int(urls, objectAtIndexSel, i);
                        var pathSel = GetSelector("path");
                        var pathPtr = objc_msgSend_ptr(urlObj, pathSel);
                        var path = NSStringToCSharpString(pathPtr);
                        filePaths.Add(path);
                    }

                    return filePaths.ToArray();
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CocoaWindow] Error showing file dialog: {ex.Message}");
            return null;
        }
    }

    private static string NSStringToCSharpString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero) return "";

        var utf8Sel = GetSelector("UTF8String");
        var utf8Ptr = objc_msgSend(nsString, utf8Sel);
        return Marshal.PtrToStringUTF8(utf8Ptr) ?? "";
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_bool(IntPtr receiver, IntPtr selector, bool value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern int objc_msgSend_int(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern int objc_msgSend_int(IntPtr receiver, IntPtr selector, int index);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ptr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ptr_int(IntPtr receiver, IntPtr selector, int arg);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ptr_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);
    
    public void Show()
    {
        if (_nsWindow != IntPtr.Zero)
        {
            // Activate the application
            var nsApp = GetSharedApplication();
            var activateIgnoringOtherAppsSel = GetSelector("activateIgnoringOtherApps:");
            SetBool(nsApp, activateIgnoringOtherAppsSel, true);

            // Make window key and bring to front
            var makeKeyAndOrderFrontSel = GetSelector("makeKeyAndOrderFront:");
            objc_msgSend_void_ptr(_nsWindow, makeKeyAndOrderFrontSel, IntPtr.Zero);

            // Enable drag and drop on existing content view
            try
            {
                var contentViewSel = GetSelector("contentView");
                var contentView = objc_msgSend(_nsWindow, contentViewSel);
                
                // Register for file URLs
                var fileType = CreateNSString("public.file-url");
                var registerForDraggedTypesSel = GetSelector("registerForDraggedTypes:");
                var arrayWithObjectSel = GetSelector("arrayWithObject:");
                var nsArray = objc_msgSend_ptr_ptr(GetClass("NSArray"), arrayWithObjectSel, fileType);
                objc_msgSend_void_ptr(contentView, registerForDraggedTypesSel, nsArray);
                Release(fileType);
                Release(nsArray);
                
                Console.WriteLine("[CocoaWindow] Drag and drop enabled on content view");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CocoaWindow] Failed to enable drag and drop: {ex.Message}");
            }

            // Center window on screen
            var centerSel = GetSelector("center");
            objc_msgSend_void(_nsWindow, centerSel);

            _isVisible = true;

            Console.WriteLine("[CocoaWindow] Window shown and activated");
        }
    }
    
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetBool(IntPtr receiver, IntPtr selector, bool value);

    public void Hide()
    {
        if (_nsWindow != IntPtr.Zero)
        {
            var orderOutSel = GetSelector("orderOut:");
            objc_msgSend_void_ptr(_nsWindow, orderOutSel, IntPtr.Zero);
            _isVisible = false;
        }
    }
    
    public void Close()
    {
        _isClosing = true;
        Closing?.Invoke();
    }
    
    public void ProcessEvents()
    {
        // Polling the content view bounds to detect resize/dpi changes natively
        var contentViewSelLocal = GetSelector("contentView");
        var contentViewLocal = objc_msgSend(_nsWindow, contentViewSelLocal);
        var boundsSelLocal = GetSelector("bounds");
        var viewBounds = objc_msgSend_CGRect(contentViewLocal, boundsSelLocal);
        
        var newSize = new Vector2((float)viewBounds.Size.Width, (float)viewBounds.Size.Height);
        if (Math.Abs(_size.X - newSize.X) > 0.1f || Math.Abs(_size.Y - newSize.Y) > 0.1f)
        {
            if (newSize.X > 0 && newSize.Y > 0)
            {
                _size = newSize;
                
                // Update specific Layer pixel mapping
                var layerSel = GetSelector("layer");
                var layer = objc_msgSend(contentViewLocal, layerSel);
                if (layer != IntPtr.Zero)
                {
                    unsafe
                    {
                        var setDrawableSizeSel = GetSelector("setDrawableSize:");
                        var backingScaleFactorSel = GetSelector("backingScaleFactor");
                        var scale = objc_msgSend_double(_nsWindow, backingScaleFactorSel);
                        
                        // We must set drawable size in physical pixels!
                        System.Runtime.InteropServices.StructLayoutAttribute? attr = null; // Unused, just to trick parser
                        var cgSize = new { width = viewBounds.Size.Width * scale, height = viewBounds.Size.Height * scale };
                        // We actually have a CGSize struct from MetalSwapchain, but here we can just use the memory representation.
                        // Or we can rely on MetalSwapchain to resize the drawable.
                        
                        var fbSize = new Vector2((float)(viewBounds.Size.Width * scale), (float)(viewBounds.Size.Height * scale));
                        Resize?.Invoke(_size);
                        FramebufferResize?.Invoke(fbSize);
                    }
                }
            }
        }

        // Process NSEvents
        var nsApp = GetSharedApplication();
        var untilDateSel = GetSelector("distantPast");
        var distantPast = objc_msgSend(GetClass("NSDate"), untilDateSel);
        
        var nextEventSel = GetSelector("nextEventMatchingMask:untilDate:inMode:dequeue:");
        var defaultRunLoopMode = GetNSDefaultRunLoopMode();
        
        // Cache selectors
        var typeSel           = GetSelector("type");
        var locationSel       = GetSelector("locationInWindow");
        var sendEventSel      = GetSelector("sendEvent:");
        var updateWindowsSel  = GetSelector("updateWindows");
        var isVisibleSel      = GetSelector("isVisible");
        var modifierFlagsSel  = GetSelector("modifierFlags");
        var charactersSel     = GetSelector("characters");
        var utf8Sel           = GetSelector("UTF8String");
        var keyCodeSel        = GetSelector("keyCode");
        var buttonNumberSel   = GetSelector("buttonNumber");
        
        // Check if window is closed (e.g. from Red X button)
        if (!objc_msgSend_bool(_nsWindow, isVisibleSel))
        {
            Close();
            return;
        }
        
        // Process all pending events
        while (true)
        {
            var evt = NextEventMatchingMask(nsApp, nextEventSel, ulong.MaxValue, distantPast, defaultRunLoopMode, true);
            if (evt == IntPtr.Zero) break;
            
            var eventType = objc_msgSend_ulong(evt, typeSel);
            
            // ── Keyboard: Cmd+Q to quit ────────────────────────────────────
            // NSEventTypeKeyDown = 10, NSEventTypeKeyUp = 11
            bool consumedKeyEvent = false;
            if (eventType == 10 || eventType == 11)
            {
                var modifiers = objc_msgSend_ulong(evt, modifierFlagsSel);
                var characters = objc_msgSend(evt, charactersSel);
                ushort keyCode = CocoaInterop.objc_msgSend_ushort(evt, keyCodeSel);

                if (characters != IntPtr.Zero)
                {
                    var utf8Ptr = objc_msgSend(characters, utf8Sel);
                    var keyString = Marshal.PtrToStringUTF8(utf8Ptr);

                    if ((modifiers & 1048576) != 0 && keyString == "q")
                    {
                        Close();
                        break;
                    }
                }

                // Map macOS modifier flags to our ModifierKeys
                var modKeys = Input.ModifierKeys.None;
                if ((modifiers & 131072) != 0) modKeys |= Input.ModifierKeys.Shift;     // NSShiftKeyMask
                if ((modifiers & 262144) != 0) modKeys |= Input.ModifierKeys.Control;   // NSControlKeyMask
                if ((modifiers & 524288) != 0) modKeys |= Input.ModifierKeys.Alt;       // NSAlternateKeyMask
                if ((modifiers & 1048576) != 0) modKeys |= Input.ModifierKeys.Super;    // NSCommandKeyMask

                // Map macOS keycode to our KeyCode and send game key events
                if (_registeredInput != null)
                {
                    var key = MapMacKeyCode(keyCode);
                    if (key != Input.KeyCode.Unknown)
                    {
                        if (eventType == 10) // KeyDown
                            _registeredInput.OnKeyDown(key, modKeys);
                        else // KeyUp
                            _registeredInput.OnKeyUp(key, modKeys);
                        consumedKeyEvent = true; // Mark as consumed to prevent alert sound
                    }

                    // keycode 51 is Backspace on Mac (auto-release for text input)
                    if (keyCode == 51 && eventType == 10)
                    {
                        _registeredInput.OnKeyDown(Input.KeyCode.Backspace, modKeys);
                        _registeredInput.OnKeyUp(Input.KeyCode.Backspace, modKeys);
                        consumedKeyEvent = true;
                    }

                    // Text input for text fields
                    if (eventType == 10 && characters != IntPtr.Zero)
                    {
                        var utf8Ptr = objc_msgSend(characters, utf8Sel);
                        var keyString = Marshal.PtrToStringUTF8(utf8Ptr);
                        if (!string.IsNullOrEmpty(keyString))
                        {
                            foreach (char c in keyString)
                            {
                                if (!char.IsControl(c) || c == ' ')
                                {
                                    _registeredInput.OnCharInput(c);
                                }
                            }
                            consumedKeyEvent = true;
                        }
                    }
                }
            }
            
            // ── Mouse events → CocoaInput ──────────────────────────────────
            if (_registeredInput != null)
            {
                // NSEvent locationInWindow is in window coords (origin = bottom-left).
                var loc = objc_msgSend_CGPoint(evt, locationSel);
                
                // Extract raw hardware deltas from NSEvent (not affected by cursor position)
                var deltaXSel = GetSelector("deltaX");
                var deltaYSel = GetSelector("deltaY");
                float dx = (float)objc_msgSend_double(evt, deltaXSel);
                float dy = (float)objc_msgSend_double(evt, deltaYSel);
                var delta = new Vector2(dx, dy);
                
                var contentHeight = (double)_size.Y;
                var mousePos = new Vector2((float)loc.X, (float)(contentHeight - loc.Y));
                
                switch (eventType)
                {
                    case 1:  // NSEventTypeLeftMouseDown
                        if (!_cursorCaptured) _registeredInput.OnMouseMove(mousePos, delta);
                        _registeredInput.OnMouseDown(Input.MouseButton.Left);
                        break;
                    case 2:  // NSEventTypeLeftMouseUp
                        if (!_cursorCaptured) _registeredInput.OnMouseMove(mousePos, delta);
                        _registeredInput.OnMouseUp(Input.MouseButton.Left);
                        break;
                    case 3:  // NSEventTypeRightMouseDown
                        if (!_cursorCaptured) _registeredInput.OnMouseMove(mousePos, delta);
                        _registeredInput.OnMouseDown(Input.MouseButton.Right);
                        break;
                    case 4:  // NSEventTypeRightMouseUp
                        if (!_cursorCaptured) _registeredInput.OnMouseMove(mousePos, delta);
                        _registeredInput.OnMouseUp(Input.MouseButton.Right);
                        break;
                    case 5:  // NSEventTypeMouseMoved
                    case 6:  // NSEventTypeLeftMouseDragged
                    case 7:  // NSEventTypeRightMouseDragged
                    case 27: // NSEventTypeOtherMouseDragged
                        if (_cursorCaptured)
                            _registeredInput.OnMouseDelta(delta); // only deltas, no position update
                        else
                            _registeredInput.OnMouseMove(mousePos, delta);
                        break;
                }
            }

            // Only send event to default handler if we didn't consume it (prevents alert sound)
            if (!consumedKeyEvent)
            {
                objc_msgSend_void_ptr(nsApp, sendEventSel, evt);
            }
            objc_msgSend_void(nsApp, updateWindowsSel);
        }
        
        // Trigger update and render every frame
        Update?.Invoke(1.0 / 60.0);
        Render?.Invoke(1.0 / 60.0);
    }
    
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr NextEventMatchingMask(IntPtr receiver, IntPtr selector, ulong mask, IntPtr date, IntPtr mode, bool dequeue);
    
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern CGPoint objc_msgSend_CGPoint(IntPtr receiver, IntPtr selector);
    
    public IntPtr GetNativeHandle() => _nsWindow;  // Return NSWindow for bgfx (not NSView!)

    // Map macOS keycodes to our KeyCode enum
    private Input.KeyCode MapMacKeyCode(ushort keyCode)
    {
        // macOS keycodes: https://developer.apple.com/library/archive/documentation/mac/pdf/MacintoshToolboxEssentials.pdf (page 164)
        switch (keyCode)
        {
            case 0: return Input.KeyCode.A;
            case 1: return Input.KeyCode.S;
            case 2: return Input.KeyCode.D;
            case 3: return Input.KeyCode.F;
            case 6: return Input.KeyCode.Z;       // Z key for undo
            case 12: return Input.KeyCode.Q;
            case 13: return Input.KeyCode.W;
            case 14: return Input.KeyCode.E;
            case 15: return Input.KeyCode.R;
            case 35: return Input.KeyCode.P;      // P key for command palette
            case 34: return Input.KeyCode.I;      // I key for import
            case 36: return Input.KeyCode.Enter;  // Enter/Return
            case 49: return Input.KeyCode.Space;
            case 53: return Input.KeyCode.Escape; // Escape
            case 56: return Input.KeyCode.LeftShift;
            case 59: return Input.KeyCode.LeftControl;
            case 123: return Input.KeyCode.Left;  // Left arrow
            case 124: return Input.KeyCode.Right; // Right arrow
            case 125: return Input.KeyCode.Down;  // Down arrow
            case 126: return Input.KeyCode.Up;    // Up arrow
            default: return Input.KeyCode.Unknown;
        }
    }

    public void SetCursorVisible(bool visible)
    {
        // Use NSCursor hide/unhide for macOS
        var nscursorClass = GetClass("NSCursor");
        if (visible)
        {
            var unhideSel = GetSelector("unhide");
            objc_msgSend_void(nscursorClass, unhideSel);
        }
        else
        {
            var hideSel = GetSelector("hide");
            objc_msgSend_void(nscursorClass, hideSel);
        }
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern int CGAssociateMouseAndMouseCursorPosition(int connected);

    public void SetCursorCaptured(bool captured)
    {
        _cursorCaptured = captured;
        CGAssociateMouseAndMouseCursorPosition(captured ? 0 : 1);
    }

    private IntPtr CreateNSWindow(WindowOptions options)
    {
        uint styleMask = NSWindowStyleMaskTitled | NSWindowStyleMaskClosable | NSWindowStyleMaskMiniaturizable;
        if (options.Resizable)
            styleMask |= NSWindowStyleMaskResizable;
        
        var rect = new CGRect(0, 0, options.Width, options.Height);
        
        var windowClass = GetClass("NSWindow");
        var allocSel = GetSelector("alloc");
        var window = objc_msgSend(windowClass, allocSel);
        
        var initSel = GetSelector("initWithContentRect:styleMask:backing:defer:");
        window = objc_msgSend_ptr_rect_uint_uint_bool(
            window, initSel, rect, styleMask, NSBackingStoreBuffered, false);
        
        if (window == IntPtr.Zero)
            throw new Exception("Failed to create NSWindow");
        
        // Set title
        var nsString = CreateNSString(options.Title);
        var setTitleSel = GetSelector("setTitle:");
        objc_msgSend_void_ptr(window, setTitleSel, nsString);
        Release(nsString);
        
        // Make window opaque (not transparent!)
        var setOpaquesel = GetSelector("setOpaque:");
        objc_msgSend_void_bool(window, setOpaquesel, true);
        
        // Note: Don't set background color - let the CAMetalLayer show through
        
        // Center window if requested
        if (options.X == -1 || options.Y == -1)
        {
            var centerSel = GetSelector("center");
            objc_msgSend_void(window, centerSel);
        }
        
        // NSWindow is retained by default; we need to release when closing later
        
        // ** CRITICAL FIX **
        // By default, NSWindow ignores mouse movement unless a button is held down! 
        // We MUST explicitly enable MouseMoved events to allow UI hover states and normal hit-testing!
        var acceptsMouseSel = GetSelector("setAcceptsMouseMovedEvents:");
        objc_msgSend_void_bool(window, acceptsMouseSel, true);
        
        Console.WriteLine("[CocoaWindow] NSWindow created with title bar and opaque background");
        
        return window;
    }
    
    private IntPtr CreateContentView()
    {
        // Create NSView
        var nsViewClass = GetClass("NSView");
        var allocSel = GetSelector("alloc");
        var initSel = GetSelector("init");
        
        var view = objc_msgSend(nsViewClass, allocSel);
        view = objc_msgSend(view, initSel);
        
        if (view == IntPtr.Zero)
            throw new Exception("Failed to create NSView");
        
        // Set view frame to match window's pure content bounds (ignores title bar height)
        var contentViewSel = GetSelector("contentView");
        var windowContentView = objc_msgSend(_nsWindow, contentViewSel);
        var boundsSel = GetSelector("bounds");
        var viewFrame = objc_msgSend_CGRect(windowContentView, boundsSel);
        
        // Update our _size to reflect actual content size (without titlebar)
        _size = new Vector2((float)viewFrame.Size.Width, (float)viewFrame.Size.Height);
        
        var setFrameSel = GetSelector("setFrame:");
        objc_msgSend_void_rect(view, setFrameSel, viewFrame);
        
        Console.WriteLine($"[CocoaWindow] View created with frame: {viewFrame.Size.Width}x{viewFrame.Size.Height}");
        
        // Create CAMetalLayer
        var metalLayerClass = GetClass("CAMetalLayer");
        var layer = objc_msgSend(objc_msgSend(metalLayerClass, allocSel), initSel);
        
        if (layer == IntPtr.Zero)
            throw new Exception("Failed to create CAMetalLayer");
        
        Console.WriteLine($"[CocoaWindow] Created CAMetalLayer: {layer}");
        
        // Configure the layer
        var frameSel = GetSelector("frame");
        viewFrame = objc_msgSend_CGRect(view, frameSel);
        
        // Set pixel format
        var setPixelFormatSel = GetSelector("setPixelFormat:");
        objc_msgSend_void_ulong(layer, setPixelFormatSel, MTLPixelFormatBGRA8Unorm);
        
        // Set the layer's frame to match the view
        var setLayerFrameSel = GetSelector("setFrame:");
        objc_msgSend_void_rect(layer, setLayerFrameSel, viewFrame);
        
        // Set drawableSize to match the frame
        unsafe
        {
            var setDrawableSizeSel = GetSelector("setDrawableSize:");
            CGSize size = new CGSize(viewFrame.Size.Width, viewFrame.Size.Height);
            objc_msgSend_void_CGSize(layer, setDrawableSizeSel, size);
        }
        
        // Set contentsScale for Retina
        var backingScaleFactorSel = GetSelector("backingScaleFactor");
        var scale = objc_msgSend_double(_nsWindow, backingScaleFactorSel);
        var setContentsScaleSel = GetSelector("setContentsScale:");
        objc_msgSend_void_double(layer, setContentsScaleSel, scale);
        
        // Make layer opaque
        var setOpaqueSel = GetSelector("setOpaque:");
        objc_msgSend_void_bool(layer, setOpaqueSel, true);
        
        Console.WriteLine($"[CocoaWindow] Configured CAMetalLayer: frame={viewFrame.Size.Width}x{viewFrame.Size.Height}, scale={scale}");
        
        // CRITICAL: Enable layer-backed rendering FIRST
        var setWantsLayerSel = GetSelector("setWantsLayer:");
        objc_msgSend_void_bool(view, setWantsLayerSel, true);
        
        // THEN replace the automatically created layer with our CAMetalLayer
        var setLayerSel = GetSelector("setLayer:");
        objc_msgSend_void_ptr(view, setLayerSel, layer);
        
        // CRITICAL: Tell the view to use layer-backed drawing
        var setLayerContentsRedrawPolicySel = GetSelector("setLayerContentsRedrawPolicy:");
        objc_msgSend_void_int(view, setLayerContentsRedrawPolicySel, 2); // NSViewLayerContentsRedrawDuringViewResize
        
        Console.WriteLine($"[CocoaWindow] Set layer and enabled wantsLayer");
        
        // Retain the layer since we're holding a reference
        Retain(layer);
        
        // Set the view as the window's contentView
        var setContentViewSel = GetSelector("setContentView:");
        objc_msgSend_void_ptr(_nsWindow, setContentViewSel, view);
        
        // Force display update
        var displaySel = GetSelector("display");
        objc_msgSend_void(_nsWindow, displaySel);
        
        Console.WriteLine($"[CocoaWindow] View set as window's contentView and display called");
        
        return Retain(view);
    }
    
    private IntPtr GetSharedApplication()
    {
        var nsAppClass = GetClass("NSApplication");
        var sharedAppSel = GetSelector("sharedApplication");
        return objc_msgSend_ptr(nsAppClass, sharedAppSel);
    }

    private IntPtr GetNSDefaultRunLoopMode()
    {
        return CreateNSString("kCFRunLoopDefaultMode");
    }
    
    private IntPtr CreateNSString(string str)
    {
        var nsStringClass = GetClass("NSString");
        var stringWithUTF8Sel = GetSelector("stringWithUTF8String:");
        
        var utf8Ptr = Marshal.StringToHGlobalAnsi(str);
        try
        {
            return objc_msgSend_ret_ptr(nsStringClass, stringWithUTF8Sel, utf8Ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(utf8Ptr);
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        if (_nsView != IntPtr.Zero)
        {
            Release(_nsView);
            _nsView = IntPtr.Zero;
        }
        
        if (_nsWindow != IntPtr.Zero)
        {
            var closeSel = GetSelector("close");
            objc_msgSend_void(_nsWindow, closeSel);
            Release(_nsWindow);
            _nsWindow = IntPtr.Zero;
        }
        
        _disposed = true;
    }
}
