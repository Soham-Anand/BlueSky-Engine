using System;
using System.Numerics;

namespace BlueSky.Platform;

/// <summary>
/// Cross-platform window interface providing window management and event handling.
/// </summary>
public interface IWindow : IDisposable
{
    /// <summary>
    /// Gets or sets the window title.
    /// </summary>
    string Title { get; set; }
    
    /// <summary>
    /// Gets or sets the window size in screen coordinates.
    /// </summary>
    Vector2 Size { get; set; }
    
    /// <summary>
    /// Gets or sets the window position in screen coordinates.
    /// </summary>
    Vector2 Position { get; set; }
    
    /// <summary>
    /// Gets the framebuffer size in pixels (may differ from Size on high-DPI displays).
    /// </summary>
    Vector2 FramebufferSize { get; }
    
    /// <summary>
    /// Gets whether the window is currently visible.
    /// </summary>
    bool IsVisible { get; }
    
    /// <summary>
    /// Gets whether the window currently has input focus.
    /// </summary>
    bool IsFocused { get; }
    
    /// <summary>
    /// Gets whether the window is in the process of closing.
    /// </summary>
    bool IsClosing { get; }
    
    /// <summary>
    /// Gets the current time in seconds since window creation.
    /// </summary>
    double Time { get; }
    
    /// <summary>
    /// Shows the window.
    /// </summary>
    void Show();
    
    /// <summary>
    /// Hides the window.
    /// </summary>
    void Hide();
    
    /// <summary>
    /// Closes the window and triggers the Closing event.
    /// </summary>
    void Close();
    
    /// <summary>
    /// Processes pending window and input events. Should be called once per frame.
    /// </summary>
    void ProcessEvents();
    
    /// <summary>
    /// Gets the native window handle for the current platform.
    /// Returns NSView* on macOS, HWND on Windows, Window on Linux.
    /// </summary>
    IntPtr GetNativeHandle();

    /// <summary>
    /// Sets the cursor visibility. When false, the cursor is hidden (useful for fly camera).
    /// </summary>
    void SetCursorVisible(bool visible);

    /// <summary>
    /// Captures or releases the mouse. When captured, mouse movement doesn't move the
    /// system cursor (infinite mouse — ideal for camera rotation). Uses
    /// CGAssociateMouseAndMouseCursorPosition on macOS.
    /// </summary>
    void SetCursorCaptured(bool captured);
    
    /// <summary>
    /// Fired when the window is resized.
    /// </summary>
    event Action<Vector2>? Resize;
    
    /// <summary>
    /// Fired when the framebuffer is resized (may differ from window size on high-DPI displays).
    /// </summary>
    event Action<Vector2>? FramebufferResize;
    
    /// <summary>
    /// Fired when the window gains input focus.
    /// </summary>
    event Action? FocusGained;
    
    /// <summary>
    /// Fired when the window loses input focus.
    /// </summary>
    event Action? FocusLost;
    
    /// <summary>
    /// Fired when the window is about to close. Can be used to prevent closing.
    /// </summary>
    event Action? Closing;
    
    /// <summary>
    /// Fired once per frame for game logic updates.
    /// </summary>
    event Action<double>? Update;
    
    /// <summary>
    /// Fired once per frame for rendering.
    /// </summary>
    event Action<double>? Render;
}
