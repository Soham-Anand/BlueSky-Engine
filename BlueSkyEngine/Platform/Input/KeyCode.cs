namespace BlueSky.Platform.Input;

/// <summary>
/// Unified key code enumeration for cross-platform keyboard input.
/// </summary>
public enum KeyCode
{
    Unknown = 0,
    
    // Letters
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    
    // Numbers (top row)
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    
    // Function keys
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
    
    // Modifiers
    LeftShift,
    RightShift,
    LeftControl,
    RightControl,
    LeftAlt,
    RightAlt,
    LeftSuper,    // Windows key / Command key
    RightSuper,
    
    // Navigation
    Up,
    Down,
    Left,
    Right,
    Home,
    End,
    PageUp,
    PageDown,
    
    // Editing
    Backspace,
    Delete,
    Insert,
    Tab,
    Enter,
    Escape,
    Space,
    
    // Punctuation
    Apostrophe,      // '
    Comma,           // ,
    Minus,           // -
    Period,          // .
    Slash,           // /
    Semicolon,       // ;
    Equal,           // =
    LeftBracket,     // [
    Backslash,       // \
    RightBracket,    // ]
    GraveAccent,     // `
    
    // Keypad
    Keypad0,
    Keypad1,
    Keypad2,
    Keypad3,
    Keypad4,
    Keypad5,
    Keypad6,
    Keypad7,
    Keypad8,
    Keypad9,
    KeypadDecimal,
    KeypadDivide,
    KeypadMultiply,
    KeypadSubtract,
    KeypadAdd,
    KeypadEnter,
    KeypadEqual,
    
    // Special
    CapsLock,
    ScrollLock,
    NumLock,
    PrintScreen,
    Pause,
    Menu,
}
