using System;
using System.Numerics;
using System.Collections.Generic;
using BlueSky.Platform.Input;

namespace BlueSky.Platform.Windows;

public class Win32Input : IInputContext
{
    private readonly Win32Window _window;
    private readonly HashSet<KeyCode> _keysDown = new();
    private readonly HashSet<KeyCode> _keysPressed = new();
    private readonly HashSet<KeyCode> _keysReleased = new();

    private readonly HashSet<MouseButton> _buttonsDown = new();
    private readonly HashSet<MouseButton> _buttonsPressed = new();
    private readonly HashSet<MouseButton> _buttonsReleased = new();

    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;
    private Vector2 _scrollDelta;

    private bool _lShift, _rShift, _lCtrl, _rCtrl, _lAlt, _rAlt, _lSuper, _rSuper;

    // Infinite mouse state
    private bool _cursorCaptured;
    private Vector2 _captureCenter;
    private bool _skipNextDelta;
    private bool _pendingWarp;

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

    public Win32Input(Win32Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        window.OnMessage += HandleMessage;
        window.PostProcessEvents += FlushWarp;
    }

    /// <summary>
    /// Enable/disable infinite mouse. When captured, mouse deltas are computed
    /// relative to center and the cursor is warped back — keeps it hidden inside viewport.
    /// </summary>
    public void SetCapture(bool captured, float centerX, float centerY)
    {
        _cursorCaptured = captured;
        _captureCenter = new Vector2(centerX, centerY);
        _skipNextDelta = true;
        _pendingWarp = false;
    }

    /// <summary>
    /// Called after the Win32 message pump finishes. Performs the deferred
    /// cursor warp so SetCursorPos doesn't re-enter WM_MOUSEMOVE.
    /// </summary>
    public void FlushWarp()
    {
        if (_pendingWarp && _cursorCaptured)
        {
            _pendingWarp = false;
            Win32Interop.SetCursorPos((int)_captureCenter.X, (int)_captureCenter.Y);
        }
    }

    private void HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32Interop.WM_MOUSEMOVE:
            {
                int x = (short)(lParam.ToInt64() & 0xFFFF);
                int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                var newPos = new Vector2(x, y);

                if (_cursorCaptured)
                {
                    // Infinite mouse: delta from center
                    if (_skipNextDelta)
                    {
                        _skipNextDelta = false;
                        _mousePosition = newPos;
                        _mouseDelta = Vector2.Zero;
                    }
                    else
                    {
                        _mouseDelta = newPos - _captureCenter;
                        _mousePosition = newPos;
                    }
                    // Defer warp to after message pump finishes — calling
                    // SetCursorPos here generates a new WM_MOUSEMOVE, creating
                    // an infinite feedback loop that starves the game loop.
                    _pendingWarp = true;
                }
                else
                {
                    _mouseDelta = newPos - _mousePosition;
                    _mousePosition = newPos;
                }
                MouseMove?.Invoke(_mousePosition);
                break;
            }

            case Win32Interop.WM_LBUTTONDOWN:
                _buttonsDown.Add(MouseButton.Left);
                _buttonsPressed.Add(MouseButton.Left);
                MouseDown?.Invoke(MouseButton.Left);
                break;

            case Win32Interop.WM_LBUTTONUP:
                _buttonsDown.Remove(MouseButton.Left);
                _buttonsReleased.Add(MouseButton.Left);
                MouseUp?.Invoke(MouseButton.Left);
                break;

            case Win32Interop.WM_RBUTTONDOWN:
                _buttonsDown.Add(MouseButton.Right);
                _buttonsPressed.Add(MouseButton.Right);
                MouseDown?.Invoke(MouseButton.Right);
                break;

            case Win32Interop.WM_RBUTTONUP:
                _buttonsDown.Remove(MouseButton.Right);
                _buttonsReleased.Add(MouseButton.Right);
                MouseUp?.Invoke(MouseButton.Right);
                break;

            case Win32Interop.WM_MOUSEWHEEL:
            {
                int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                _scrollDelta = new Vector2(0, delta / 120f);
                MouseScroll?.Invoke(_scrollDelta);
                break;
            }

            case Win32Interop.WM_KEYDOWN:
            case Win32Interop.WM_SYSKEYDOWN:
            {
                int vk = wParam.ToInt32();

                bool isRepeat = (lParam.ToInt64() & (1L << 30)) != 0;
                if (isRepeat)
                    break;

                if (MapKey(vk, out var key))
                {
                    _keysDown.Add(key);
                    _keysPressed.Add(key);
                    UpdateModifiers(vk, true);
                    KeyDown?.Invoke(key, GetModifiers());
                }
                break;
            }

            case Win32Interop.WM_KEYUP:
            case Win32Interop.WM_SYSKEYUP:
            {
                int vk = wParam.ToInt32();

                if (MapKey(vk, out var key))
                {
                    _keysDown.Remove(key);
                    _keysReleased.Add(key);
                    UpdateModifiers(vk, false);
                    KeyUp?.Invoke(key, GetModifiers());
                }
                break;
            }

            case Win32Interop.WM_CHAR:
            {
                char c = (char)wParam.ToInt32();
                if (!char.IsControl(c))
                    CharInput?.Invoke(c);
                break;
            }
        }
    }

    private void UpdateModifiers(int vk, bool pressed)
    {
        switch (vk)
        {
            case 0xA0: _lShift = pressed; break;
            case 0xA1: _rShift = pressed; break;
            case 0xA2: _lCtrl = pressed; break;
            case 0xA3: _rCtrl = pressed; break;
            case 0xA4: _lAlt = pressed; break;
            case 0xA5: _rAlt = pressed; break;
            case 0x5B: _lSuper = pressed; break;
            case 0x5C: _rSuper = pressed; break;
        }
    }

    public void BeginFrame()
    {
        _keysPressed.Clear();
        _keysReleased.Clear();
        _buttonsPressed.Clear();
        _buttonsReleased.Clear();
        _mouseDelta = Vector2.Zero;
        _scrollDelta = Vector2.Zero;
    }

    public bool IsKeyDown(KeyCode key) => _keysDown.Contains(key);
    public bool IsKeyPressed(KeyCode key) => _keysPressed.Contains(key);
    public bool IsKeyReleased(KeyCode key) => _keysReleased.Contains(key);

    public ModifierKeys GetModifiers()
    {
        var m = ModifierKeys.None;
        if (_lShift || _rShift) m |= ModifierKeys.Shift;
        if (_lCtrl || _rCtrl) m |= ModifierKeys.Control;
        if (_lAlt || _rAlt) m |= ModifierKeys.Alt;
        if (_lSuper || _rSuper) m |= ModifierKeys.Super;
        return m;
    }

    public bool IsMouseButtonDown(MouseButton button) => _buttonsDown.Contains(button);
    public bool IsMouseButtonPressed(MouseButton button) => _buttonsPressed.Contains(button);
    public bool IsMouseButtonReleased(MouseButton button) => _buttonsReleased.Contains(button);

    public void Dispose()
    {
        _window.OnMessage -= HandleMessage;
        _window.PostProcessEvents -= FlushWarp;
        _cursorCaptured = false;
        _pendingWarp = false;
    }

    // ── VK → KeyCode mapping ──────────────────────────────────────────────────
    private static bool MapKey(int vk, out KeyCode key)
    {
        key = VkMap.TryGetValue(vk, out var mapped) ? mapped : KeyCode.Unknown;
        return key != KeyCode.Unknown;
    }

    private static readonly Dictionary<int, KeyCode> VkMap = new()
    {
        // Letters
        [0x41] = KeyCode.A,        [0x42] = KeyCode.B,        [0x43] = KeyCode.C,
        [0x44] = KeyCode.D,        [0x45] = KeyCode.E,        [0x46] = KeyCode.F,
        [0x47] = KeyCode.G,        [0x48] = KeyCode.H,        [0x49] = KeyCode.I,
        [0x4A] = KeyCode.J,        [0x4B] = KeyCode.K,        [0x4C] = KeyCode.L,
        [0x4D] = KeyCode.M,        [0x4E] = KeyCode.N,        [0x4F] = KeyCode.O,
        [0x50] = KeyCode.P,        [0x51] = KeyCode.Q,        [0x52] = KeyCode.R,
        [0x53] = KeyCode.S,        [0x54] = KeyCode.T,        [0x55] = KeyCode.U,
        [0x56] = KeyCode.V,        [0x57] = KeyCode.W,        [0x58] = KeyCode.X,
        [0x59] = KeyCode.Y,        [0x5A] = KeyCode.Z,

        // Top row numbers
        [0x30] = KeyCode.D0,       [0x31] = KeyCode.D1,       [0x32] = KeyCode.D2,
        [0x33] = KeyCode.D3,       [0x34] = KeyCode.D4,       [0x35] = KeyCode.D5,
        [0x36] = KeyCode.D6,       [0x37] = KeyCode.D7,       [0x38] = KeyCode.D8,
        [0x39] = KeyCode.D9,

        // Function keys
        [0x70] = KeyCode.F1,       [0x71] = KeyCode.F2,       [0x72] = KeyCode.F3,
        [0x73] = KeyCode.F4,       [0x74] = KeyCode.F5,       [0x75] = KeyCode.F6,
        [0x76] = KeyCode.F7,       [0x77] = KeyCode.F8,       [0x78] = KeyCode.F9,
        [0x79] = KeyCode.F10,      [0x7A] = KeyCode.F11,      [0x7B] = KeyCode.F12,
        [0x7C] = KeyCode.F13,      [0x7D] = KeyCode.F14,      [0x7E] = KeyCode.F15,
        [0x7F] = KeyCode.F16,      [0x80] = KeyCode.F17,      [0x81] = KeyCode.F18,
        [0x82] = KeyCode.F19,      [0x83] = KeyCode.F20,      [0x84] = KeyCode.F21,
        [0x85] = KeyCode.F22,      [0x86] = KeyCode.F23,      [0x87] = KeyCode.F24,

        // Modifiers
        [0xA0] = KeyCode.LeftShift,     [0xA1] = KeyCode.RightShift,
        [0xA2] = KeyCode.LeftControl,   [0xA3] = KeyCode.RightControl,
        [0xA4] = KeyCode.LeftAlt,       [0xA5] = KeyCode.RightAlt,
        [0x5B] = KeyCode.LeftSuper,     [0x5C] = KeyCode.RightSuper,

        // Navigation
        [0x26] = KeyCode.Up,       [0x28] = KeyCode.Down,
        [0x25] = KeyCode.Left,     [0x27] = KeyCode.Right,
        [0x24] = KeyCode.Home,     [0x23] = KeyCode.End,
        [0x21] = KeyCode.PageUp,   [0x22] = KeyCode.PageDown,

        // Editing
        [0x08] = KeyCode.Backspace,  [0x2E] = KeyCode.Delete,
        [0x2D] = KeyCode.Insert,     [0x09] = KeyCode.Tab,
        [0x0D] = KeyCode.Enter,      [0x1B] = KeyCode.Escape,
        [0x20] = KeyCode.Space,

        // Punctuation (VK_OEM_*)
        [0xDE] = KeyCode.Apostrophe,    [0xBC] = KeyCode.Comma,
        [0xBD] = KeyCode.Minus,         [0xBE] = KeyCode.Period,
        [0xBF] = KeyCode.Slash,         [0xBA] = KeyCode.Semicolon,
        [0xBB] = KeyCode.Equal,         [0xDB] = KeyCode.LeftBracket,
        [0xDC] = KeyCode.Backslash,     [0xDD] = KeyCode.RightBracket,
        [0xC0] = KeyCode.GraveAccent,

        // Numpad
        [0x60] = KeyCode.Keypad0,  [0x61] = KeyCode.Keypad1,  [0x62] = KeyCode.Keypad2,
        [0x63] = KeyCode.Keypad3,  [0x64] = KeyCode.Keypad4,  [0x65] = KeyCode.Keypad5,
        [0x66] = KeyCode.Keypad6,  [0x67] = KeyCode.Keypad7,  [0x68] = KeyCode.Keypad8,
        [0x69] = KeyCode.Keypad9,
        [0x6E] = KeyCode.KeypadDecimal,  [0x6F] = KeyCode.KeypadDivide,
        [0x6A] = KeyCode.KeypadMultiply, [0x6D] = KeyCode.KeypadSubtract,
        [0x6B] = KeyCode.KeypadAdd,      [0x6C] = KeyCode.KeypadEqual,

        // Special
        [0x14] = KeyCode.CapsLock,    [0x91] = KeyCode.ScrollLock,
        [0x90] = KeyCode.NumLock,     [0x2C] = KeyCode.PrintScreen,
        [0x13] = KeyCode.Pause,       [0x5D] = KeyCode.Menu,
    };
}
