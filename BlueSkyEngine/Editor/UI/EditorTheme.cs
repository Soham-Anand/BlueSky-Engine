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
    public static readonly Vector4 Bg0        = V(0.051f, 0.055f, 0.071f);  // Deepest: splitters, shadows
    public static readonly Vector4 Bg1        = V(0.072f, 0.076f, 0.092f);  // Panel backgrounds
    public static readonly Vector4 Bg2        = V(0.092f, 0.096f, 0.112f);  // Surfaces (cards, sections)
    public static readonly Vector4 Bg3        = V(0.115f, 0.120f, 0.138f);  // Elevated surfaces
    public static readonly Vector4 Bg4        = V(0.142f, 0.148f, 0.170f);  // Hover states, inputs

    // Borders
    public static readonly Vector4 Border0    = V(0.040f, 0.045f, 0.058f);  // Hard dividers
    public static readonly Vector4 Border1    = V(0.120f, 0.130f, 0.155f);  // Subtle borders
    public static readonly Vector4 Border2    = V(0.180f, 0.195f, 0.225f);  // Visible borders

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
    public static readonly Vector4 Accent       = V(0.29f, 0.62f, 1.00f);   // Primary blue (brighter)
    public static readonly Vector4 AccentHover  = V(0.38f, 0.70f, 1.00f);   // Lighter blue
    public static readonly Vector4 AccentDim    = V(0.18f, 0.42f, 0.78f);   // Pressed / darker blue
    public static readonly Vector4 AccentGlow   = new(0.30f, 0.62f, 1.0f, 0.12f); // Selection glow (subtler)
    public static readonly Vector4 AccentCyan   = V(0.30f, 0.85f, 0.95f);   // Secondary accent
    public static readonly Vector4 AccentWarm   = V(1.00f, 0.55f, 0.30f);   // Warm accent for contrast

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

    public static readonly Vector4 SplitterNormal = V(0.035f, 0.040f, 0.052f);
    public static readonly Vector4 SplitterHot    = Accent;

    // ═══════════════════════════════════════════════════════════════════
    //  ROUNDED RECT DEFAULTS
    // ═══════════════════════════════════════════════════════════════════
    public const float CardRadius     = 8f;    // Content browser cards
    public const float ButtonRadius   = 6f;    // Toolbar buttons
    public const float PillRadius     = 12f;   // FPS badge, Play button
    public const float InputRadius    = 4f;    // Text fields, sliders
    public const float SmallRadius    = 3f;    // Tiny elements

    // ═══════════════════════════════════════════════════════════════════
    //  SELECTION — layered highlight
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 SelectionBg     = new(0.22f, 0.42f, 0.72f, 0.30f);
    public static readonly Vector4 SelectionBorder = Accent;
    public static readonly Vector4 HoverBg         = new(0.20f, 0.22f, 0.26f, 0.80f);

    // ═══════════════════════════════════════════════════════════════════
    //  TOOLBAR
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 ToolbarBg   = V(0.062f, 0.066f, 0.082f);
    public static readonly Vector4 ToolbarBtnNormal = V(0.100f, 0.108f, 0.128f);
    public static readonly Vector4 ToolbarBtnHover  = V(0.140f, 0.150f, 0.175f);
    public static readonly Vector4 ToolbarBtnActive = Accent;

    // Play/Pause/Stop transport controls
    public static readonly Vector4 PlayGreen    = V(0.18f, 0.78f, 0.42f);
    public static readonly Vector4 PauseYellow  = V(0.95f, 0.78f, 0.22f);
    public static readonly Vector4 StopRed      = V(0.88f, 0.30f, 0.30f);

    // ═══════════════════════════════════════════════════════════════════
    //  PROJECT BROWSER (Launcher)
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 LauncherBg        = V(0.045f, 0.048f, 0.062f);
    public static readonly Vector4 LauncherSidebar   = V(0.038f, 0.042f, 0.055f);
    public static readonly Vector4 LauncherCardBg    = V(0.078f, 0.082f, 0.098f);
    public static readonly Vector4 LauncherCardHover = V(0.105f, 0.112f, 0.132f);
    public static readonly Vector4 LauncherBrand     = new(0.40f, 0.72f, 1.0f, 1.0f);

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
