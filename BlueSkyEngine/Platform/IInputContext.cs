using System;
using System.Numerics;
using BlueSky.Platform.Input;

namespace BlueSky.Platform;

/// <summary>
/// Cross-platform input context providing keyboard, mouse, and touch input.
/// </summary>
public interface IInputContext : IDisposable
{
    /// <summary>
    /// Resets per-frame input state. Should be called at the beginning of each frame.
    /// </summary>
    void BeginFrame();
    
    // Keyboard State
    
    /// <summary>
    /// Checks if a key is currently held down.
    /// </summary>
    bool IsKeyDown(KeyCode key);
    
    /// <summary>
    /// Checks if a key was pressed this frame (transition from up to down).
    /// </summary>
    bool IsKeyPressed(KeyCode key);
    
    /// <summary>
    /// Checks if a key was released this frame (transition from down to up).
    /// </summary>
    bool IsKeyReleased(KeyCode key);
    
    /// <summary>
    /// Gets the current modifier key state.
    /// </summary>
    ModifierKeys GetModifiers();
    
    // Mouse State
    
    /// <summary>
    /// Gets the current mouse position relative to the window.
    /// </summary>
    Vector2 MousePosition { get; }
    
    /// <summary>
    /// Gets the mouse movement delta since last frame.
    /// </summary>
    Vector2 MouseDelta { get; }
    
    /// <summary>
    /// Gets the scroll wheel delta since last frame.
    /// </summary>
    Vector2 ScrollDelta { get; }
    
    /// <summary>
    /// Checks if a mouse button is currently held down.
    /// </summary>
    bool IsMouseButtonDown(MouseButton button);
    
    /// <summary>
    /// Checks if a mouse button was pressed this frame.
    /// </summary>
    bool IsMouseButtonPressed(MouseButton button);
    
    /// <summary>
    /// Checks if a mouse button was released this frame.
    /// </summary>
    bool IsMouseButtonReleased(MouseButton button);
    
    // Keyboard Events
    
    /// <summary>
    /// Fired when a key is pressed down.
    /// </summary>
    event Action<KeyCode, ModifierKeys>? KeyDown;
    
    /// <summary>
    /// Fired when a key is released.
    /// </summary>
    event Action<KeyCode, ModifierKeys>? KeyUp;
    
    /// <summary>
    /// Fired when a character is input (for text entry).
    /// </summary>
    event Action<char>? CharInput;
    
    // Mouse Events
    
    /// <summary>
    /// Fired when a mouse button is pressed down.
    /// </summary>
    event Action<MouseButton>? MouseDown;
    
    /// <summary>
    /// Fired when a mouse button is released.
    /// </summary>
    event Action<MouseButton>? MouseUp;
    
    /// <summary>
    /// Fired when the mouse moves.
    /// </summary>
    event Action<Vector2>? MouseMove;
    
    /// <summary>
    /// Fired when the mouse wheel scrolls.
    /// </summary>
    event Action<Vector2>? MouseScroll;
}
