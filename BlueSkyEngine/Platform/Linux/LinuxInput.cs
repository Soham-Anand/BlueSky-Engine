using System.Numerics;
using System.Runtime.InteropServices;
using BlueSky.Platform.Input;

namespace BlueSky.Platform.Linux;

/// <summary>
/// Linux input context for X11 and Wayland windows.
/// Handles keyboard (XKB) and pointer event translation.
/// </summary>
public sealed class LinuxInput : IInputContext
{
    private nint _display;
    private bool _isWayland;
    
    // Keyboard state
    private nint _xkbContext;
    private nint _xkbKeymap;
    private nint _xkbState;
    private bool[] _keyStates = new bool[256];
    
    // Pointer state
    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;
    private Vector2 _scrollDelta;
    private bool[] _mouseButtonStates = new bool[5];
    
    // Event queues
    private readonly Queue<KeyEvent> _keyQueue = new();
    private readonly Queue<MouseEvent> _mouseQueue = new();
    
    public Vector2 MousePosition => _mousePosition;
    public Vector2 MouseDelta => _mouseDelta;
    public Vector2 ScrollDelta => _scrollDelta;

    public event Action<KeyCode, ModifierKeys>? KeyDown;
    public event Action<KeyCode, ModifierKeys>? KeyUp;
    public event Action<char>? CharInput;
    public event Action<MouseButton>? MouseDown;
    public event Action<MouseButton>? MouseUp;
    public event Action<Vector2>? MouseMove;
    public event Action<Vector2>? MouseScroll;

    public LinuxInput(nint display, bool isWayland = false)
    {
        _display = display;
        _isWayland = isWayland;
        
        if (!isWayland && display != nint.Zero)
        {
            InitializeXkb();
        }
        
        Console.WriteLine($"[LinuxInput] Initialized for {(isWayland ? "Wayland" : "X11")}");
    }
    
    private void InitializeXkb()
    {
        // XKB common library
        const string XkbCommon = "libxkbcommon.so.0";
        
        try
        {
            // Create XKB context
            _xkbContext = XkbCommonInterop.xkb_context_new(0);
            if (_xkbContext == nint.Zero)
            {
                Console.WriteLine("[LinuxInput] Failed to create XKB context");
                return;
            }
            
            // Create default keymap
            _xkbKeymap = XkbCommonInterop.xkb_keymap_new_from_names(
                _xkbContext, 
                nint.Zero, 
                0);
                
            if (_xkbKeymap == nint.Zero)
            {
                Console.WriteLine("[LinuxInput] Failed to create XKB keymap");
                XkbCommonInterop.xkb_context_unref(_xkbContext);
                _xkbContext = nint.Zero;
                return;
            }
            
            // Create XKB state
            _xkbState = XkbCommonInterop.xkb_state_new(_xkbKeymap);
            if (_xkbState == nint.Zero)
            {
                Console.WriteLine("[LinuxInput] Failed to create XKB state");
                XkbCommonInterop.xkb_keymap_unref(_xkbKeymap);
                XkbCommonInterop.xkb_context_unref(_xkbContext);
                _xkbContext = nint.Zero;
                _xkbKeymap = nint.Zero;
                return;
            }
            
            Console.WriteLine("[LinuxInput] XKB initialized successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LinuxInput] XKB initialization failed: {ex.Message}");
        }
    }

    public void BeginFrame()
    {
        _mouseDelta = Vector2.Zero;
        _scrollDelta = Vector2.Zero;
        
        // Process queued events
        ProcessKeyQueue();
        ProcessMouseQueue();
    }
    
    private void ProcessKeyQueue()
    {
        lock (_keyQueue)
        {
            while (_keyQueue.Count > 0)
            {
                var evt = _keyQueue.Dequeue();
                if (evt.IsDown)
                {
                    _keyStates[(int)evt.KeyCode] = true;
                    KeyDown?.Invoke(evt.KeyCode, GetModifiers());
                }
                else
                {
                    _keyStates[(int)evt.KeyCode] = false;
                    KeyUp?.Invoke(evt.KeyCode, GetModifiers());
                }
            }
        }
    }
    
    private void ProcessMouseQueue()
    {
        lock (_mouseQueue)
        {
            while (_mouseQueue.Count > 0)
            {
                var evt = _mouseQueue.Dequeue();
                switch (evt.Type)
                {
                    case MouseEventTypes.ButtonDown:
                        _mouseButtonStates[(int)evt.Button] = true;
                        MouseDown?.Invoke(evt.Button);
                        break;
                    case MouseEventTypes.ButtonUp:
                        _mouseButtonStates[(int)evt.Button] = false;
                        MouseUp?.Invoke(evt.Button);
                        break;
                    case MouseEventTypes.Move:
                        _mouseDelta = evt.Position - _mousePosition;
                        _mousePosition = evt.Position;
                        MouseMove?.Invoke(_mousePosition);
                        break;
                    case MouseEventTypes.Scroll:
                        _scrollDelta = evt.ScrollDelta;
                        MouseScroll?.Invoke(_scrollDelta);
                        break;
                }
            }
        }
    }

    public bool IsKeyDown(KeyCode key) => (int)key < 256 && _keyStates[(int)key];
    
    public bool IsKeyPressed(KeyCode key)
    {
        // Check if key was just pressed this frame
        return false; // Would need previous frame state
    }
    
    public bool IsKeyReleased(KeyCode key)
    {
        // Check if key was just released this frame
        return false; // Would need previous frame state
    }
    
    public ModifierKeys GetModifiers()
    {
        if (_xkbState == nint.Zero)
            return ModifierKeys.None;
        
        var modifiers = ModifierKeys.None;
        
        // Check XKB state for modifiers
        if (XkbCommonInterop.xkb_state_mod_name_is_active(
            _xkbState, 
            XkbCommonInterop.XKB_MOD_NAME_SHIFT) > 0)
            modifiers |= ModifierKeys.Shift;
            
        if (XkbCommonInterop.xkb_state_mod_name_is_active(
            _xkbState, 
            XkbCommonInterop.XKB_MOD_NAME_CTRL) > 0)
            modifiers |= ModifierKeys.Control;
            
        if (XkbCommonInterop.xkb_state_mod_name_is_active(
            _xkbState, 
            XkbCommonInterop.XKB_MOD_NAME_ALT) > 0)
            modifiers |= ModifierKeys.Alt;
            
        if (XkbCommonInterop.xkb_state_mod_name_is_active(
            _xkbState, 
            XkbCommonInterop.XKB_MOD_NAME_LOGO) > 0)
            modifiers |= ModifierKeys.Super;
        
        return modifiers;
    }
    
    public bool IsMouseButtonDown(MouseButton button) 
        => (int)button < 5 && _mouseButtonStates[(int)button];
    
    public bool IsMouseButtonPressed(MouseButton button) => false;
    public bool IsMouseButtonReleased(MouseButton button) => false;

    // X11 event processing
    internal void ProcessX11Event(ref X11Interop.XEvent evt)
    {
        switch (evt.Type)
        {
            case X11Interop.KeyPress:
                HandleX11KeyPress(evt);
                break;
            case X11Interop.KeyRelease:
                HandleX11KeyRelease(evt);
                break;
            case X11Interop.ButtonPress:
                HandleX11ButtonPress(evt);
                break;
            case X11Interop.ButtonRelease:
                HandleX11ButtonRelease(evt);
                break;
            case X11Interop.MotionNotify:
                HandleX11MotionNotify(evt);
                break;
        }
    }
    
    private void HandleX11KeyPress(X11Interop.XEvent evt)
    {
        if (_xkbState == nint.Zero)
            return;
        
        // Extract keycode from event
        var keycode = evt.Type & 0xFF; // Simplified - actual parsing needed
        
        // Convert X11 keycode to XKB keycode
        var xkbKeycode = (uint)(keycode - 8); // X11 keycode offset
        
        // Get XKB keysym
        var keysym = XkbCommonInterop.xkb_state_key_get_one_sym(_xkbState, xkbKeycode);
        
        // Update XKB state
        XkbCommonInterop.xkb_state_update_key(_xkbState, xkbKeycode, 1); // XKB_KEY_DOWN
        
        // Convert keysym to KeyCode
        var keyCode = KeysymToKeyCode(keysym);
        
        lock (_keyQueue)
        {
            _keyQueue.Enqueue(new KeyEvent(keyCode, true));
        }
        
        // Try to get character input
        var buffer = new char[16];
        var len = XkbCommonInterop.xkb_keysym_to_utf8(keysym, buffer, (uint)buffer.Length);
        if (len > 0)
        {
            foreach (var c in new string(buffer).Take(len))
            {
                CharInput?.Invoke(c);
            }
        }
    }
    
    private void HandleX11KeyRelease(X11Interop.XEvent evt)
    {
        if (_xkbState == nint.Zero)
            return;
        
        var keycode = evt.Type & 0xFF;
        var xkbKeycode = (uint)(keycode - 8);
        
        XkbCommonInterop.xkb_state_update_key(_xkbState, xkbKeycode, 2); // XKB_KEY_UP
        
        var keysym = XkbCommonInterop.xkb_state_key_get_one_sym(_xkbState, xkbKeycode);
        var keyCode = KeysymToKeyCode(keysym);
        
        lock (_keyQueue)
        {
            _keyQueue.Enqueue(new KeyEvent(keyCode, false));
        }
    }
    
    private void HandleX11ButtonPress(X11Interop.XEvent evt)
    {
        // Extract button number from event
        var button = evt.Type & 0xFF; // Simplified
        
        MouseButton mouseButton = button switch
        {
            1 => MouseButton.Left,
            2 => MouseButton.Middle,
            3 => MouseButton.Right,
            4 => MouseButton.X1, // Scroll up
            5 => MouseButton.X2, // Scroll down
            _ => MouseButton.Left
        };
        
        // Handle scroll wheel (buttons 4 and 5 are scroll in X11)
        if (button == 4 || button == 5)
        {
            var scroll = button == 4 ? 1 : -1;
            lock (_mouseQueue)
            {
                _mouseQueue.Enqueue(new MouseEvent(
                    MouseEventTypes.Scroll,
                    Vector2.Zero,
                    new Vector2(0, scroll),
                    MouseButton.Left));
            }
        }
        else
        {
            lock (_mouseQueue)
            {
                _mouseQueue.Enqueue(new MouseEvent(
                    MouseEventTypes.ButtonDown,
                    Vector2.Zero,
                    Vector2.Zero,
                    mouseButton));
            }
        }
    }
    
    private void HandleX11ButtonRelease(X11Interop.XEvent evt)
    {
        var button = evt.Type & 0xFF;
        
        MouseButton mouseButton = button switch
        {
            1 => MouseButton.Left,
            2 => MouseButton.Middle,
            3 => MouseButton.Right,
            _ => MouseButton.Left
        };
        
        lock (_mouseQueue)
        {
            _mouseQueue.Enqueue(new MouseEvent(
                MouseEventTypes.ButtonUp,
                Vector2.Zero,
                Vector2.Zero,
                mouseButton));
        }
    }
    
    private void HandleX11MotionNotify(X11Interop.XEvent evt)
    {
        // Extract motion event data
        // This is simplified - actual XEvent parsing needed
        lock (_mouseQueue)
        {
            _mouseQueue.Enqueue(new MouseEvent(
                MouseEventTypes.Move,
                _mousePosition,
                Vector2.Zero,
                MouseButton.Left));
        }
    }
    
    // Wayland event processing
    public void ProcessWaylandPointerMotion(float x, float y)
    {
        var newPos = new Vector2(x, y);
        lock (_mouseQueue)
        {
            _mouseQueue.Enqueue(new MouseEvent(
                MouseEventTypes.Move,
                newPos,
                Vector2.Zero,
                MouseButton.Left));
        }
    }
    
    public void ProcessWaylandPointerButton(uint button, bool pressed)
    {
        var mouseButton = button switch
        {
            0x110 => MouseButton.Left,   // BTN_LEFT
            0x111 => MouseButton.Right,  // BTN_RIGHT
            0x112 => MouseButton.Middle, // BTN_MIDDLE
            _ => MouseButton.Left
        };
        
        lock (_mouseQueue)
        {
            _mouseQueue.Enqueue(new MouseEvent(
                pressed ? MouseEventTypes.ButtonDown : MouseEventTypes.ButtonUp,
                _mousePosition,
                Vector2.Zero,
                mouseButton));
        }
    }
    
    public void ProcessWaylandPointerAxis(float dx, float dy)
    {
        lock (_mouseQueue)
        {
            _mouseQueue.Enqueue(new MouseEvent(
                MouseEventTypes.Scroll,
                Vector2.Zero,
                new Vector2(dx, dy),
                MouseButton.Left));
        }
    }
    
    public void ProcessWaylandKey(uint keycode, bool pressed)
    {
        // Wayland sends evdev keycodes directly
        var xkbKeycode = keycode + 8; // Convert to XKB keycode
        
        if (_xkbState != nint.Zero)
        {
            var keysym = XkbCommonInterop.xkb_state_key_get_one_sym(_xkbState, xkbKeycode);
            XkbCommonInterop.xkb_state_update_key(
                _xkbState, 
                xkbKeycode, 
                pressed ? 1u : 2u); // XKB_KEY_DOWN or XKB_KEY_UP
            
            var keyCode = KeysymToKeyCode(keysym);
            
            lock (_keyQueue)
            {
                _keyQueue.Enqueue(new KeyEvent(keyCode, pressed));
            }
        }
    }

    private static KeyCode KeysymToKeyCode(uint keysym)
    {
        // Convert XKB keysym to engine KeyCode
        // This is a simplified mapping - full implementation would map all keys
        
        if (keysym >= 32 && keysym <= 126)
            return (KeyCode)keysym; // ASCII range
            
        return keysym switch
        {
            65307 => KeyCode.Escape,
            65300 => KeyCode.Home,
            65367 => KeyCode.End,
            65301 => KeyCode.Up,
            65362 => KeyCode.Up,
            65302 => KeyCode.Down,
            65364 => KeyCode.Down,
            65303 => KeyCode.Right,
            65363 => KeyCode.Right,
            65304 => KeyCode.Left,
            65361 => KeyCode.Left,
            65288 => KeyCode.Backspace,
            65293 => KeyCode.Enter,
            65505 => KeyCode.LeftShift,
            65506 => KeyCode.RightShift,
            65507 => KeyCode.LeftControl,
            65508 => KeyCode.RightControl,
            65513 => KeyCode.LeftAlt,
            65514 => KeyCode.RightAlt,
            65515 => KeyCode.LeftSuper,
            65516 => KeyCode.RightSuper,
            65535 => KeyCode.Delete,
            _ => KeyCode.Unknown
        };
    }

    public void Dispose()
    {
        if (_xkbState != nint.Zero)
        {
            XkbCommonInterop.xkb_state_unref(_xkbState);
            _xkbState = nint.Zero;
        }
        
        if (_xkbKeymap != nint.Zero)
        {
            XkbCommonInterop.xkb_keymap_unref(_xkbKeymap);
            _xkbKeymap = nint.Zero;
        }
        
        if (_xkbContext != nint.Zero)
        {
            XkbCommonInterop.xkb_context_unref(_xkbContext);
            _xkbContext = nint.Zero;
        }
        
        Console.WriteLine("[LinuxInput] Disposed");
    }
    
    // XKB Common interop
    internal static class XkbCommonInterop
    {
        private const string XkbCommon = "libxkbcommon.so.0";
        
        public const string XKB_MOD_NAME_SHIFT = "Shift";
        public const string XKB_MOD_NAME_CTRL = "Ctrl";
        public const string XKB_MOD_NAME_ALT = "Alt";
        public const string XKB_MOD_NAME_LOGO = "Mod4";
        
        public const uint XKB_KEY_DOWN = 1;
        public const uint XKB_KEY_UP = 2;
        
        [DllImport(XkbCommon)]
        public static extern nint xkb_context_new(int flags);
        
        [DllImport(XkbCommon)]
        public static extern void xkb_context_unref(nint context);
        
        [DllImport(XkbCommon)]
        public static extern nint xkb_keymap_new_from_names(
            nint context, 
            nint names, 
            int flags);
        
        [DllImport(XkbCommon)]
        public static extern void xkb_keymap_unref(nint keymap);
        
        [DllImport(XkbCommon)]
        public static extern nint xkb_state_new(nint keymap);
        
        [DllImport(XkbCommon)]
        public static extern void xkb_state_unref(nint state);
        
        [DllImport(XkbCommon)]
        public static extern int xkb_state_update_key(
            nint state, 
            uint key, 
            uint direction);
        
        [DllImport(XkbCommon)]
        public static extern uint xkb_state_key_get_one_sym(
            nint state, 
            uint key);
        
        [DllImport(XkbCommon)]
        public static extern int xkb_state_mod_name_is_active(
            nint state, 
            [MarshalAs(UnmanagedType.LPStr)] string name);
        
        [DllImport(XkbCommon)]
        public static extern int xkb_keysym_to_utf8(
            uint keysym, 
            Span<char> buffer, 
            uint buffer_size);
    }
    
    // Event structures
    private enum MouseEventTypes
    {
        Move,
        ButtonDown,
        ButtonUp,
        Scroll
    }
    
    private struct KeyEvent
    {
        public KeyCode KeyCode;
        public bool IsDown;
        
        public KeyEvent(KeyCode keyCode, bool isDown)
        {
            KeyCode = keyCode;
            IsDown = isDown;
        }
    }
    
    private struct MouseEvent
    {
        public MouseEventTypes Type;
        public Vector2 Position;
        public Vector2 ScrollDelta;
        public MouseButton Button;
        
        public MouseEvent(
            MouseEventTypes type,
            Vector2 position,
            Vector2 scrollDelta,
            MouseButton button)
        {
            Type = type;
            Position = position;
            ScrollDelta = scrollDelta;
            Button = button;
        }
    }
}