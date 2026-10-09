using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using BlueSky.Platform.Input;

namespace BlueSky.Platform.Linux;

/// <summary>
/// Linux input context for an X11 window.
/// Handles keyboard (XKB) and pointer event translation.
/// </summary>
public sealed class LinuxInput : IInputContext
{
    private X11Window? _x11Window;
    
    // Keyboard state
    private nint _xkbContext;
    private nint _xkbKeymap;
    private nint _xkbState;
    private bool[] _keyStates = new bool[256];
    private bool[] _keysPressed = new bool[256];
    private bool[] _keysReleased = new bool[256];
    
    // Pointer state
    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;
    private Vector2 _scrollDelta;
    private bool _hasMousePosition;
    private bool[] _mouseButtonStates = new bool[5];
    private bool[] _mouseButtonsPressed = new bool[5];
    private bool[] _mouseButtonsReleased = new bool[5];
    
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

    public LinuxInput(X11Window window)
    {
        _x11Window = window ?? throw new ArgumentNullException(nameof(window));
        _x11Window.X11Event += ProcessX11Event;
        try
        {
            InitializeXkb();
        }
        catch (Exception ex)
        {
            Dispose();
            throw new PlatformNotSupportedException(
                "Linux input requires libxkbcommon and a usable system keymap.", ex);
        }
        Console.WriteLine("[LinuxInput] Initialized for X11");
    }
    
    private void InitializeXkb()
    {
        _xkbContext = XkbCommonInterop.xkb_context_new(0);
        if (_xkbContext == nint.Zero)
            throw new InvalidOperationException("xkb_context_new returned null.");

        _xkbKeymap = XkbCommonInterop.xkb_keymap_new_from_names(_xkbContext, nint.Zero, 0);
        if (_xkbKeymap == nint.Zero)
            throw new InvalidOperationException("xkb_keymap_new_from_names returned null.");

        _xkbState = XkbCommonInterop.xkb_state_new(_xkbKeymap);
        if (_xkbState == nint.Zero)
            throw new InvalidOperationException("xkb_state_new returned null.");

        Console.WriteLine("[LinuxInput] XKB initialized successfully");
    }

    public void BeginFrame()
    {
        _mouseDelta = Vector2.Zero;
        _scrollDelta = Vector2.Zero;
        Array.Clear(_keysPressed);
        Array.Clear(_keysReleased);
        Array.Clear(_mouseButtonsPressed);
        Array.Clear(_mouseButtonsReleased);
        
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
                int keyIndex = (int)evt.KeyCode;
                if (keyIndex <= 0 || keyIndex >= _keyStates.Length)
                    continue;

                if (evt.IsDown && !_keyStates[keyIndex])
                {
                    _keyStates[keyIndex] = true;
                    _keysPressed[keyIndex] = true;
                    KeyDown?.Invoke(evt.KeyCode, evt.Modifiers);
                }
                else if (!evt.IsDown && _keyStates[keyIndex])
                {
                    _keyStates[keyIndex] = false;
                    _keysReleased[keyIndex] = true;
                    KeyUp?.Invoke(evt.KeyCode, evt.Modifiers);
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
                        if ((uint)evt.Button >= _mouseButtonStates.Length) break;
                        if (_mouseButtonStates[(int)evt.Button]) break;
                        _mouseButtonStates[(int)evt.Button] = true;
                        _mouseButtonsPressed[(int)evt.Button] = true;
                        MouseDown?.Invoke(evt.Button);
                        break;
                    case MouseEventTypes.ButtonUp:
                        if ((uint)evt.Button >= _mouseButtonStates.Length) break;
                        if (!_mouseButtonStates[(int)evt.Button]) break;
                        _mouseButtonStates[(int)evt.Button] = false;
                        _mouseButtonsReleased[(int)evt.Button] = true;
                        MouseUp?.Invoke(evt.Button);
                        break;
                    case MouseEventTypes.Move:
                        if (_hasMousePosition)
                            _mouseDelta += evt.Position - _mousePosition;
                        _mousePosition = evt.Position;
                        _hasMousePosition = true;
                        MouseMove?.Invoke(_mousePosition);
                        break;
                    case MouseEventTypes.Scroll:
                        _scrollDelta += evt.ScrollDelta;
                        MouseScroll?.Invoke(_scrollDelta);
                        break;
                }
            }
        }
    }

    public bool IsKeyDown(KeyCode key) => (uint)key < _keyStates.Length && _keyStates[(int)key];
    public bool IsKeyPressed(KeyCode key) => (uint)key < _keysPressed.Length && _keysPressed[(int)key];
    public bool IsKeyReleased(KeyCode key) => (uint)key < _keysReleased.Length && _keysReleased[(int)key];
    
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
    
    public bool IsMouseButtonDown(MouseButton button) => (uint)button < _mouseButtonStates.Length && _mouseButtonStates[(int)button];
    public bool IsMouseButtonPressed(MouseButton button) => (uint)button < _mouseButtonsPressed.Length && _mouseButtonsPressed[(int)button];
    public bool IsMouseButtonReleased(MouseButton button) => (uint)button < _mouseButtonsReleased.Length && _mouseButtonsReleased[(int)button];

    // X11 event processing
    private void ProcessX11Event(X11Interop.XEvent evt)
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
        
        uint xkbKeycode = evt.Key.Keycode;
        if (xkbKeycode == 0) return;
        
        XkbCommonInterop.xkb_state_update_key(_xkbState, xkbKeycode, 1); // XKB_KEY_DOWN
        var keysym = XkbCommonInterop.xkb_state_key_get_one_sym(_xkbState, xkbKeycode);
        
        // Convert keysym to KeyCode
        var keyCode = KeysymToKeyCode(keysym);
        
        lock (_keyQueue)
        {
            _keyQueue.Enqueue(new KeyEvent(keyCode, true, GetModifiers()));
        }
        
        // Try to get character input
        var buffer = new byte[32];
        var len = XkbCommonInterop.xkb_keysym_to_utf8(keysym, buffer, (uint)buffer.Length);
        if (len > 1)
        {
            var text = Encoding.UTF8.GetString(buffer, 0, Math.Min(len - 1, buffer.Length));
            foreach (var c in text)
                CharInput?.Invoke(c);
        }
    }
    
    private void HandleX11KeyRelease(X11Interop.XEvent evt)
    {
        if (_xkbState == nint.Zero)
            return;
        
        uint xkbKeycode = evt.Key.Keycode;
        if (xkbKeycode == 0) return;
        var keysym = XkbCommonInterop.xkb_state_key_get_one_sym(_xkbState, xkbKeycode);
        XkbCommonInterop.xkb_state_update_key(_xkbState, xkbKeycode, 2); // XKB_KEY_UP
        var keyCode = KeysymToKeyCode(keysym);
        
        lock (_keyQueue)
        {
            _keyQueue.Enqueue(new KeyEvent(keyCode, false, GetModifiers()));
        }
    }
    
    private void HandleX11ButtonPress(X11Interop.XEvent evt)
    {
        uint button = evt.Button.Button;
        
        MouseButton mouseButton = button switch
        {
            1 => MouseButton.Left,
            2 => MouseButton.Middle,
            3 => MouseButton.Right,
            4 => MouseButton.X1, // Scroll up
            5 => MouseButton.X2, // Scroll down
            _ => (MouseButton)(-1)
        };
        
        // X11 buttons 4–7 are wheel directions; 8 and 9 are side buttons.
        if (button >= 4 && button <= 7)
        {
            var scroll = button switch { 4 => new Vector2(0, 1), 5 => new Vector2(0, -1), 6 => new Vector2(-1, 0), _ => new Vector2(1, 0) };
            lock (_mouseQueue)
            {
                _mouseQueue.Enqueue(new MouseEvent(
                    MouseEventTypes.Scroll,
                    Vector2.Zero,
                    scroll,
                    MouseButton.Left));
            }
        }
        else
        {
            mouseButton = button switch { 8 => MouseButton.X1, 9 => MouseButton.X2, _ => mouseButton };
            if ((int)mouseButton >= 0)
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
    }
    
    private void HandleX11ButtonRelease(X11Interop.XEvent evt)
    {
        uint button = evt.Button.Button;
        // X11 reports wheel activity as button press only.
        if (button >= 4 && button <= 7)
            return;
        
        MouseButton mouseButton = button switch
        {
            1 => MouseButton.Left,
            2 => MouseButton.Middle,
            3 => MouseButton.Right,
            8 => MouseButton.X1,
            9 => MouseButton.X2,
            _ => (MouseButton)(-1)
        };
        
        if ((int)mouseButton >= 0)
        {
            lock (_mouseQueue)
            {
                _mouseQueue.Enqueue(new MouseEvent(
                    MouseEventTypes.ButtonUp,
                    Vector2.Zero,
                    Vector2.Zero,
                    mouseButton));
            }
        }
    }
    
    private void HandleX11MotionNotify(X11Interop.XEvent evt)
    {
        lock (_mouseQueue)
        {
            _mouseQueue.Enqueue(new MouseEvent(
                MouseEventTypes.Move,
                new Vector2(evt.Motion.X, evt.Motion.Y),
                Vector2.Zero,
                MouseButton.Left));
        }
    }
    
    private static KeyCode KeysymToKeyCode(uint keysym)
    {
        if (keysym is >= 'a' and <= 'z') return (KeyCode)((int)KeyCode.A + keysym - 'a');
        if (keysym is >= 'A' and <= 'Z') return (KeyCode)((int)KeyCode.A + keysym - 'A');
        if (keysym is >= '0' and <= '9') return (KeyCode)((int)KeyCode.D0 + keysym - '0');
        if (keysym is >= 0xFFBE and <= 0xFFD5) return (KeyCode)((int)KeyCode.F1 + keysym - 0xFFBE);

        return keysym switch
        {
            0x20 => KeyCode.Space,
            0x27 => KeyCode.Apostrophe,
            0x2C => KeyCode.Comma,
            0x2D => KeyCode.Minus,
            0x2E => KeyCode.Period,
            0x2F => KeyCode.Slash,
            0x3B => KeyCode.Semicolon,
            0x3D => KeyCode.Equal,
            0x5B => KeyCode.LeftBracket,
            0x5C => KeyCode.Backslash,
            0x5D => KeyCode.RightBracket,
            0x60 => KeyCode.GraveAccent,
            0xFF08 => KeyCode.Backspace,
            0xFF09 => KeyCode.Tab,
            0xFF0D => KeyCode.Enter,
            0xFF1B => KeyCode.Escape,
            0xFF50 => KeyCode.Home,
            0xFF51 => KeyCode.Left,
            0xFF52 => KeyCode.Up,
            0xFF53 => KeyCode.Right,
            0xFF54 => KeyCode.Down,
            0xFF55 => KeyCode.PageUp,
            0xFF56 => KeyCode.PageDown,
            0xFF57 => KeyCode.End,
            0xFF63 => KeyCode.Insert,
            0xFFFF => KeyCode.Delete,
            0xFFE1 => KeyCode.LeftShift,
            0xFFE2 => KeyCode.RightShift,
            0xFFE3 => KeyCode.LeftControl,
            0xFFE4 => KeyCode.RightControl,
            0xFFE9 => KeyCode.LeftAlt,
            0xFFEA => KeyCode.RightAlt,
            0xFFEB => KeyCode.LeftSuper,
            0xFFEC => KeyCode.RightSuper,
            0xFF7F => KeyCode.NumLock,
            0xFF14 => KeyCode.ScrollLock,
            0xFFE5 => KeyCode.CapsLock,
            0xFF61 => KeyCode.PrintScreen,
            0xFF13 => KeyCode.Pause,
            0xFF67 => KeyCode.Menu,
            0xFFB0 => KeyCode.Keypad0,
            0xFFB1 => KeyCode.Keypad1,
            0xFFB2 => KeyCode.Keypad2,
            0xFFB3 => KeyCode.Keypad3,
            0xFFB4 => KeyCode.Keypad4,
            0xFFB5 => KeyCode.Keypad5,
            0xFFB6 => KeyCode.Keypad6,
            0xFFB7 => KeyCode.Keypad7,
            0xFFB8 => KeyCode.Keypad8,
            0xFFB9 => KeyCode.Keypad9,
            0xFFAE => KeyCode.KeypadDecimal,
            0xFFAF => KeyCode.KeypadDivide,
            0xFFAA => KeyCode.KeypadMultiply,
            0xFFAD => KeyCode.KeypadSubtract,
            0xFFAB => KeyCode.KeypadAdd,
            0xFF8D => KeyCode.KeypadEnter,
            0xFFBD => KeyCode.KeypadEqual,
            _ => KeyCode.Unknown
        };
    }

    public void Dispose()
    {
        if (_x11Window != null)
        {
            _x11Window.X11Event -= ProcessX11Event;
            _x11Window = null;
        }

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
            [Out] byte[] buffer,
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
        public ModifierKeys Modifiers;
        
        public KeyEvent(KeyCode keyCode, bool isDown, ModifierKeys modifiers)
        {
            KeyCode = keyCode;
            IsDown = isDown;
            Modifiers = modifiers;
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
