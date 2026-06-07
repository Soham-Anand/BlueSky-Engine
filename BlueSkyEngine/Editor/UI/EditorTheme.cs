using System.Numerics;

namespace BlueSky.Editor.UI;

/// <summary>
/// Centralized design system for the BlueSky Engine editor.
/// Compact production-editor chrome with layered graphite surfaces,
/// cool command accents, and warm semantic contrast.
/// </summary>
public static class EditorTheme
{
    // ═══════════════════════════════════════════════════════════════════
    //  BASE PALETTE - neutral graphite, no soft-tint wash
    // ═══════════════════════════════════════════════════════════════════

    // Backgrounds - layered depth (darkest to lightest)
    public static readonly Vector4 Bg0        = V(0.025f, 0.028f, 0.034f);  // Deep chrome, splitters
    public static readonly Vector4 Bg1        = V(0.039f, 0.044f, 0.052f);  // Panel backgrounds
    public static readonly Vector4 Bg2        = V(0.055f, 0.062f, 0.074f);  // Section headers
    public static readonly Vector4 Bg3        = V(0.076f, 0.086f, 0.102f);  // Inputs, active surfaces
    public static readonly Vector4 Bg4        = V(0.105f, 0.120f, 0.142f);  // Hover states
    public static readonly Vector4 BgElevated = V(0.066f, 0.076f, 0.092f);
    public static readonly Vector4 BgGlass    = new(0.030f, 0.038f, 0.050f, 0.88f);

    // Borders
    public static readonly Vector4 Border0    = V(0.008f, 0.010f, 0.014f);  // Hard dividers
    public static readonly Vector4 Border1    = V(0.125f, 0.145f, 0.168f);  // Subtle borders
    public static readonly Vector4 Border2    = V(0.225f, 0.255f, 0.300f);  // Visible borders
    public static readonly Vector4 Highlight  = new(1.0f, 1.0f, 1.0f, 0.055f);

    // ═══════════════════════════════════════════════════════════════════
    //  TEXT
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 TextPrimary   = V(0.92f, 0.94f, 0.96f);
    public static readonly Vector4 TextSecondary = V(0.70f, 0.74f, 0.78f);
    public static readonly Vector4 TextMuted     = V(0.48f, 0.52f, 0.57f);
    public static readonly Vector4 TextDisabled  = V(0.34f, 0.37f, 0.42f);

    // ═══════════════════════════════════════════════════════════════════
    //  ACCENTS - restrained, functional, readable
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 Accent       = V(0.18f, 0.62f, 0.98f);
    public static readonly Vector4 AccentHover  = V(0.40f, 0.75f, 1.00f);
    public static readonly Vector4 AccentDim    = V(0.095f, 0.270f, 0.455f);
    public static readonly Vector4 AccentGlow   = new(0.18f, 0.62f, 0.98f, 0.16f);
    public static readonly Vector4 AccentCyan   = V(0.19f, 0.78f, 0.82f);
    public static readonly Vector4 AccentWarm   = V(0.98f, 0.52f, 0.30f);

    public static readonly Vector4 Green        = V(0.25f, 0.78f, 0.46f);
    public static readonly Vector4 GreenDim     = V(0.16f, 0.48f, 0.30f);
    public static readonly Vector4 Yellow       = V(0.92f, 0.72f, 0.25f);
    public static readonly Vector4 Orange       = V(0.95f, 0.56f, 0.25f);
    public static readonly Vector4 Red          = V(0.90f, 0.28f, 0.30f);
    public static readonly Vector4 RedHover     = V(1.00f, 0.40f, 0.42f);
    public static readonly Vector4 Purple       = V(0.66f, 0.47f, 0.92f);
    public static readonly Vector4 Teal         = V(0.20f, 0.70f, 0.66f);

    // Folder icon colors
    public static readonly Vector4 FolderFront  = V(0.82f, 0.63f, 0.30f);
    public static readonly Vector4 FolderBack   = V(0.62f, 0.42f, 0.18f);

    // ═══════════════════════════════════════════════════════════════════
    //  DOCKING / TABS
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 TabBarBg       = V(0.030f, 0.035f, 0.044f);
    public static readonly Vector4 TabActive      = V(0.070f, 0.082f, 0.100f);
    public static readonly Vector4 TabInactive    = V(0.046f, 0.053f, 0.064f);
    public static readonly Vector4 TabHover       = V(0.088f, 0.103f, 0.124f);
    public static readonly Vector4 TabIndicator   = Accent;
    public static readonly Vector4 TabText        = TextPrimary;
    public static readonly Vector4 TabTextDim     = TextMuted;

    public static readonly Vector4 SplitterNormal = V(0.020f, 0.023f, 0.028f);
    public static readonly Vector4 SplitterHot    = Accent;

    // ═══════════════════════════════════════════════════════════════════
    //  SHAPE DEFAULTS
    // ═══════════════════════════════════════════════════════════════════
    public const float CardRadius     = 8f;    // Content browser cards
    public const float ButtonRadius   = 6f;    // Toolbar buttons
    public const float PillRadius     = 999f;  // FPS badge, Play button
    public const float InputRadius    = 6f;    // Text fields, sliders
    public const float SmallRadius    = 4f;    // Tiny elements

    // ═══════════════════════════════════════════════════════════════════
    //  SELECTION — layered highlight
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 SelectionBg     = new(0.13f, 0.38f, 0.68f, 0.42f);
    public static readonly Vector4 SelectionBorder = Accent;
    public static readonly Vector4 HoverBg         = new(0.11f, 0.135f, 0.165f, 0.94f);

    // ═══════════════════════════════════════════════════════════════════
    //  TOOLBAR
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 ToolbarBg   = V(0.038f, 0.046f, 0.058f);
    public static readonly Vector4 ToolbarBtnNormal = V(0.060f, 0.072f, 0.090f);
    public static readonly Vector4 ToolbarBtnHover  = V(0.095f, 0.118f, 0.148f);
    public static readonly Vector4 ToolbarBtnActive = Accent;

    // Play/Pause/Stop transport controls
    public static readonly Vector4 PlayGreen    = V(0.17f, 0.66f, 0.36f);
    public static readonly Vector4 PauseYellow  = V(0.86f, 0.68f, 0.20f);
    public static readonly Vector4 StopRed      = V(0.78f, 0.23f, 0.23f);

    // ═══════════════════════════════════════════════════════════════════
    //  PROJECT BROWSER (Launcher)
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 LauncherBg        = V(0.030f, 0.036f, 0.048f);
    public static readonly Vector4 LauncherSidebar   = V(0.022f, 0.027f, 0.037f);
    public static readonly Vector4 LauncherCardBg    = V(0.060f, 0.072f, 0.090f);
    public static readonly Vector4 LauncherCardHover = V(0.090f, 0.112f, 0.140f);
    public static readonly Vector4 LauncherBrand     = new(0.38f, 0.75f, 1.0f, 1.0f);

    // ═══════════════════════════════════════════════════════════════════
    //  SPACING (logical pixels)
    // ═══════════════════════════════════════════════════════════════════
    // Note: keep these values conservative; the UI system is pixel-based,
    // and tighter defaults help the editor feel less "chunky".
    public const float Pad        = 6f;
    public const float PadLg      = 10f;
    public const float PadXl      = 16f;
    public const float HeaderH    = 28f;
    public const float ToolbarH   = 34f;
    public const float TabH       = 24f;
    public const float SplitterW  = 2f;
    public const float RowH       = 22f;
    public const float SectionH   = 24f;
    public const float StatusH    = 18f;
    public const float MinPanelW  = 80f;   // Minimum dock panel width
    public const float PropLabelW = 80f;   // Property label column width for alignment

    // ═══════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Create an opaque RGBA color from RGB.</summary>
    private static Vector4 V(float r, float g, float b) => new(r, g, b, 1.0f);

    /// <summary>Lighten a color by a percentage (0..1).</summary>
    public static Vector4 Lighten(Vector4 c, float amount) =>
        new(c.X + (1f - c.X) * amount, c.Y + (1f - c.Y) * amount, c.Z + (1f - c.Z) * amount, c.W);

    /// <summary>Darken a color by a percentage (0..1).</summary>
    public static Vector4 Darken(Vector4 c, float amount) =>
        new(c.X * (1f - amount), c.Y * (1f - amount), c.Z * (1f - amount), c.W);

    /// <summary>Create a semi-transparent version of a color.</summary>
    public static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);
}
