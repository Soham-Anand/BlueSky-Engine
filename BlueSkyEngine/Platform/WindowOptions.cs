namespace BlueSky.Platform;

/// <summary>
/// Options for creating a window.
/// </summary>
public struct WindowOptions
{
    /// <summary>
    /// The window title.
    /// </summary>
    public string Title;
    
    /// <summary>
    /// The window width in screen coordinates.
    /// </summary>
    public int Width;
    
    /// <summary>
    /// The window height in screen coordinates.
    /// </summary>
    public int Height;
    
    /// <summary>
    /// The window X position. Use -1 for centered.
    /// </summary>
    public int X;
    
    /// <summary>
    /// The window Y position. Use -1 for centered.
    /// </summary>
    public int Y;
    
    /// <summary>
    /// Whether to enable VSync.
    /// </summary>
    public bool VSync;
    
    /// <summary>
    /// Whether the window is resizable.
    /// </summary>
    public bool Resizable;
    
    /// <summary>
    /// Whether to start in fullscreen mode.
    /// </summary>
    public bool Fullscreen;
    
    /// <summary>
    /// Whether the window should be visible immediately after creation.
    /// </summary>
    public bool StartVisible;
    
    /// <summary>
    /// Default window options.
    /// </summary>
    public static WindowOptions Default => new()
    {
        Title = "BlueSky Engine",
        Width = 1280,
        Height = 720,
        X = -1,
        Y = -1,
        VSync = true,
        Resizable = true,
        Fullscreen = false,
        StartVisible = true
    };
}
