namespace BlueSky.Networking;

/// <summary>
/// Shared launch options for the editor's standalone process launcher.
/// Keeps multiplayer arguments in one place so the runtime and editor
/// can evolve together.
/// </summary>
public sealed class StandaloneLaunchOptions
{
    public string ScenePath { get; set; } = "";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool Fullscreen { get; set; }
    public bool VSync { get; set; } = true;
    public MultiplayerConfig Multiplayer { get; set; } = new();
}

