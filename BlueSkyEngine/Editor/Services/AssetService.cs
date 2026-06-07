using System.Numerics;

namespace BlueSky.Editor.Services;

/// <summary>
/// Content browser + drag/drop + context menu state.
/// Pure state holder; the drawing logic stays in EditorPanels/SceneCommands for now.
/// </summary>
public sealed class AssetService
{
    public int SelectedSourceIndex { get; set; } = 0;
    public int SelectedAssetIndex { get; set; } = -1;

    public string CurrentBrowserDir { get; set; } = "";

    public string? DraggedAssetPath { get; set; }
    public Vector2 DragStartMouse { get; set; }
    public bool IsDraggingAsset { get; set; }
    public uint DoubleClickTarget { get; set; }
    public double LastClickTime { get; set; }

    public bool ShowContextMenu { get; set; }
    public float ContextMenuX { get; set; }
    public float ContextMenuY { get; set; }
    public string ContextMenuPath { get; set; } = "";
}

