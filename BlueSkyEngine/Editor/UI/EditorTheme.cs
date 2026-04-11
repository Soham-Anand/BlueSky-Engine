using System.Numerics;

namespace BlueSky.Editor.UI;

/// <summary>
/// Centralized design system for the BlueSky Engine editor.
/// Every color, spacing, and sizing constant lives here so the entire
/// editor feels cohesive. Inspired by JetBrains / VS Code dark themes.
/// </summary>
public static class EditorTheme
{
    // ═══════════════════════════════════════════════════════════════════
    //  BASE PALETTE — warm dark tones, never pure black
    // ═══════════════════════════════════════════════════════════════════

    // Backgrounds — layered depth (darkest → lightest)
    public static readonly Vector4 Bg0        = V(0.075f, 0.078f, 0.085f);  // Deepest: splitters, shadows
    public static readonly Vector4 Bg1        = V(0.098f, 0.102f, 0.110f);  // Panel backgrounds
    public static readonly Vector4 Bg2        = V(0.118f, 0.122f, 0.130f);  // Surfaces (cards, sections)
    public static readonly Vector4 Bg3        = V(0.145f, 0.149f, 0.158f);  // Elevated surfaces
    public static readonly Vector4 Bg4        = V(0.175f, 0.180f, 0.192f);  // Hover states, inputs

    // Borders
    public static readonly Vector4 Border0    = V(0.060f, 0.065f, 0.070f);  // Hard dividers
    public static readonly Vector4 Border1    = V(0.160f, 0.170f, 0.185f);  // Subtle borders
    public static readonly Vector4 Border2    = V(0.220f, 0.235f, 0.260f);  // Visible borders

    // ═══════════════════════════════════════════════════════════════════
    //  TEXT
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 TextPrimary   = V(0.93f, 0.94f, 0.96f);
    public static readonly Vector4 TextSecondary = V(0.68f, 0.70f, 0.75f);
    public static readonly Vector4 TextMuted     = V(0.45f, 0.47f, 0.52f);
    public static readonly Vector4 TextDisabled  = V(0.32f, 0.34f, 0.38f);

    // ═══════════════════════════════════════════════════════════════════
    //  ACCENTS — vibrant but not garish
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 Accent       = V(0.22f, 0.53f, 0.96f);   // Primary blue
    public static readonly Vector4 AccentHover  = V(0.32f, 0.62f, 1.00f);   // Lighter blue
    public static readonly Vector4 AccentDim    = V(0.15f, 0.38f, 0.72f);   // Pressed / darker blue
    public static readonly Vector4 AccentGlow   = new(0.30f, 0.60f, 1.0f, 0.18f); // Selection glow

    public static readonly Vector4 Green        = V(0.30f, 0.82f, 0.50f);
    public static readonly Vector4 GreenDim     = V(0.22f, 0.60f, 0.38f);
    public static readonly Vector4 Yellow       = V(1.00f, 0.82f, 0.28f);
    public static readonly Vector4 Orange       = V(0.96f, 0.62f, 0.22f);
    public static readonly Vector4 Red          = V(0.92f, 0.35f, 0.35f);
    public static readonly Vector4 RedHover     = V(1.00f, 0.45f, 0.45f);
    public static readonly Vector4 Purple       = V(0.72f, 0.45f, 0.96f);
    public static readonly Vector4 Teal         = V(0.25f, 0.80f, 0.78f);

    // Folder icon colors
    public static readonly Vector4 FolderFront  = V(1.00f, 0.72f, 0.32f);
    public static readonly Vector4 FolderBack   = V(0.90f, 0.55f, 0.18f);

    // ═══════════════════════════════════════════════════════════════════
    //  DOCKING / TABS
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 TabBarBg       = V(0.082f, 0.086f, 0.094f);
    public static readonly Vector4 TabActive      = V(0.118f, 0.122f, 0.130f); // Same as Bg2 — seamless
    public static readonly Vector4 TabInactive    = V(0.092f, 0.096f, 0.104f);
    public static readonly Vector4 TabHover       = V(0.135f, 0.140f, 0.150f);
    public static readonly Vector4 TabIndicator   = Accent;                     // Blue bar on active tab
    public static readonly Vector4 TabText        = TextPrimary;
    public static readonly Vector4 TabTextDim     = TextMuted;

    public static readonly Vector4 SplitterNormal = V(0.055f, 0.060f, 0.068f);
    public static readonly Vector4 SplitterHot    = Accent;

    // ═══════════════════════════════════════════════════════════════════
    //  SELECTION — layered highlight
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 SelectionBg     = new(0.22f, 0.42f, 0.72f, 0.30f);
    public static readonly Vector4 SelectionBorder = Accent;
    public static readonly Vector4 HoverBg         = new(0.20f, 0.22f, 0.26f, 0.80f);

    // ═══════════════════════════════════════════════════════════════════
    //  TOOLBAR
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 ToolbarBg   = V(0.090f, 0.094f, 0.102f);
    public static readonly Vector4 ToolbarBtnNormal = V(0.130f, 0.135f, 0.148f);
    public static readonly Vector4 ToolbarBtnHover  = V(0.170f, 0.178f, 0.195f);
    public static readonly Vector4 ToolbarBtnActive = Accent;

    // Play/Pause/Stop transport controls
    public static readonly Vector4 PlayGreen    = V(0.18f, 0.78f, 0.42f);
    public static readonly Vector4 PauseYellow  = V(0.95f, 0.78f, 0.22f);
    public static readonly Vector4 StopRed      = V(0.88f, 0.30f, 0.30f);

    // ═══════════════════════════════════════════════════════════════════
    //  PROJECT BROWSER (Launcher)
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 LauncherBg        = V(0.070f, 0.074f, 0.082f);
    public static readonly Vector4 LauncherSidebar   = V(0.060f, 0.064f, 0.072f);
    public static readonly Vector4 LauncherCardBg    = V(0.105f, 0.110f, 0.120f);
    public static readonly Vector4 LauncherCardHover = V(0.135f, 0.142f, 0.155f);
    public static readonly Vector4 LauncherBrand     = new(0.40f, 0.68f, 1.0f, 1.0f);

    // ═══════════════════════════════════════════════════════════════════
    //  SPACING (logical pixels)
    // ═══════════════════════════════════════════════════════════════════
    public const float Pad        = 10f;
    public const float PadLg      = 18f;
    public const float PadXl      = 28f;
    public const float HeaderH    = 28f;   // Menu bar height — compact
    public const float ToolbarH   = 32f;   // Toolbar height — compact
    public const float TabH       = 26f;   // Dock tab height — compact
    public const float SplitterW  = 4f;    // Splitter grab width (was 3)
    public const float RowH       = 28f;   // Standard list row height (was 26)
    public const float SectionH   = 32f;   // Section header height (was 30)
    public const float StatusH    = 18f;   // Status bar height — minimal
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
