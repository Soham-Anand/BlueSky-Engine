using BlueSky.Editor.UI;
using NotBSRenderer;

namespace BlueSky.Editor.Services;

/// <summary>
/// Viewport + GPU resources and editor viewport book-keeping.
/// </summary>
public sealed class ViewportService
{
    public BlueSky.Rendering.Viewport? Viewport { get; set; }
    public BlueSky.Editor.ViewportRenderer? EditorViewportRenderer { get; set; }

    public IRHITexture? DepthTexture { get; set; }
    public uint DepthW { get; set; }
    public uint DepthH { get; set; }

    public DockRect LastViewportRect { get; set; }
    public bool ViewportNeedsRender { get; set; }
}

