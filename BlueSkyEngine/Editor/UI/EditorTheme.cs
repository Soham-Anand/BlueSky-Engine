using System.Numerics;

namespace BlueSky.Editor.UI;

/// <summary>
/// Centralized design system for the BlueSky Engine editor.
/// Minimalist, spacious dark slate palette with soft tonal hierarchy and generous breathing room.
/// </summary>
public static class EditorTheme
{
    // ═══════════════════════════════════════════════════════════════════
    //  BASE PALETTE - Soft, calm carbon-slate hierarchy
    // ═══════════════════════════════════════════════════════════════════

    public static readonly Vector4 Bg0             = V(0.045f, 0.049f, 0.058f);  // Deep wells & recessed inputs
    public static readonly Vector4 Bg1             = V(0.068f, 0.074f, 0.086f);  // Primary panel canvas
    public static readonly Vector4 Bg2             = V(0.094f, 0.102f, 0.118f);  // Cards & secondary surfaces
    public static readonly Vector4 Bg3             = V(0.125f, 0.136f, 0.158f);  // Interactive controls
    public static readonly Vector4 Bg4             = V(0.162f, 0.176f, 0.204f);  // Hover state
    public static readonly Vector4 BgElevated      = V(0.105f, 0.114f, 0.134f);  // Modals & popovers
    public static readonly Vector4 BgGlass         = new(0.055f, 0.060f, 0.072f, 0.82f); // Minimal floating HUD

    // Subtle hairlines (low contrast so panels don't look boxed-in)
    public static readonly Vector4 Border0         = V(0.032f, 0.036f, 0.044f);
    public static readonly Vector4 Border1         = V(0.125f, 0.138f, 0.162f);
    public static readonly Vector4 Border2         = V(0.205f, 0.228f, 0.268f);
    public static readonly Vector4 Highlight       = new(1.0f, 1.0f, 1.0f, 0.04f);
    public static readonly Vector4 HighlightSubtle = new(1.0f, 1.0f, 1.0f, 0.02f);
    public const float CharHeight = 14f;

    // ═══════════════════════════════════════════════════════════════════
    //  TEXT - Calm, readable hierarchy
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 TextPrimary   = V(0.94f, 0.96f, 0.98f);
    public static readonly Vector4 TextSecondary = V(0.72f, 0.76f, 0.82f);
    public static readonly Vector4 TextMuted     = V(0.48f, 0.52f, 0.59f);
    public static readonly Vector4 TextDisabled  = V(0.34f, 0.37f, 0.43f);

    // ═══════════════════════════════════════════════════════════════════
    //  ACCENTS
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 Accent       = V(0.24f, 0.58f, 0.96f);
    public static readonly Vector4 AccentHover  = V(0.36f, 0.68f, 1.00f);
    public static readonly Vector4 AccentDim    = V(0.10f, 0.26f, 0.46f);
    public static readonly Vector4 AccentGlow   = new(0.24f, 0.58f, 0.96f, 0.14f);
    public static readonly Vector4 AccentCyan   = V(0.22f, 0.76f, 0.86f);
    public static readonly Vector4 AccentWarm   = V(0.94f, 0.56f, 0.28f);

    public static readonly Vector4 Green        = V(0.24f, 0.76f, 0.48f);
    public static readonly Vector4 GreenDim     = V(0.12f, 0.42f, 0.26f);
    public static readonly Vector4 Yellow       = V(0.94f, 0.74f, 0.26f);
    public static readonly Vector4 Orange       = V(0.94f, 0.56f, 0.26f);
    public static readonly Vector4 Red          = V(0.88f, 0.32f, 0.36f);
    public static readonly Vector4 RedHover     = V(0.96f, 0.42f, 0.46f);
    public static readonly Vector4 Purple       = V(0.66f, 0.48f, 0.92f);
    public static readonly Vector4 Teal         = V(0.22f, 0.74f, 0.68f);

    // 3D Axis semantic colors (softened for minimal UI)
    public static readonly Vector4 AxisX        = V(0.88f, 0.34f, 0.38f);
    public static readonly Vector4 AxisY        = V(0.28f, 0.76f, 0.46f);
    public static readonly Vector4 AxisZ        = V(0.30f, 0.60f, 0.96f);

    // Folder icon colors
    public static readonly Vector4 FolderFront  = V(0.90f, 0.68f, 0.32f);
    public static readonly Vector4 FolderBack   = V(0.68f, 0.48f, 0.20f);

    // Widgets
    public static readonly Vector4 SliderTrack         = V(0.042f, 0.046f, 0.055f);
    public static readonly Vector4 SliderFill          = V(0.24f, 0.58f, 0.96f);
    public static readonly Vector4 SliderFillHot       = V(0.36f, 0.68f, 1.00f);
    public static readonly Vector4 InputBg             = V(0.048f, 0.053f, 0.064f);
    public static readonly Vector4 InputFocusBg        = V(0.075f, 0.110f, 0.170f);
    public static readonly Vector4 InputBorder         = V(0.130f, 0.145f, 0.172f);
    public static readonly Vector4 InputText           = V(0.93f, 0.95f, 0.98f);
    public static readonly Vector4 ScrollbarTrack      = new(0.045f, 0.049f, 0.058f, 0.0f);
    public static readonly Vector4 ScrollbarThumb      = new(0.30f, 0.34f, 0.40f, 0.55f);
    public static readonly Vector4 ScrollbarThumbHover = new(0.44f, 0.49f, 0.58f, 0.80f);
    public static readonly Vector4 ScrollbarThumbHot   = new(0.24f, 0.58f, 0.96f, 0.95f);

    // ═══════════════════════════════════════════════════════════════════
    //  DOCKING / TABS
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 TabBarBg       = V(0.050f, 0.055f, 0.066f);
    public static readonly Vector4 TabActive      = V(0.068f, 0.074f, 0.086f);
    public static readonly Vector4 TabInactive    = V(0.050f, 0.055f, 0.066f);
    public static readonly Vector4 TabHover       = V(0.088f, 0.096f, 0.114f);
    public static readonly Vector4 TabIndicator   = Accent;
    public static readonly Vector4 TabText        = TextPrimary;
    public static readonly Vector4 TabTextDim     = TextMuted;

    public static readonly Vector4 SplitterNormal = V(0.040f, 0.044f, 0.052f);
    public static readonly Vector4 SplitterHot    = Accent;

    // ═══════════════════════════════════════════════════════════════════
    //  SHAPE DEFAULTS - Soft minimal radii
    // ═══════════════════════════════════════════════════════════════════
    public const float CardRadius     = 8f;
    public const float ButtonRadius   = 6f;
    public const float PillRadius     = 999f;
    public const float InputRadius    = 6f;
    public const float SmallRadius    = 5f;

    // ═══════════════════════════════════════════════════════════════════
    //  SELECTION
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 SelectionBg     = new(0.24f, 0.58f, 0.96f, 0.18f);
    public static readonly Vector4 SelectionBorder = Accent;
    public static readonly Vector4 HoverBg         = new(1.0f, 1.0f, 1.0f, 0.045f);

    // ═══════════════════════════════════════════════════════════════════
    //  TOOLBAR
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 ToolbarBg        = V(0.056f, 0.062f, 0.074f);
    public static readonly Vector4 ToolbarBtnNormal = V(0.088f, 0.096f, 0.114f);
    public static readonly Vector4 ToolbarBtnHover  = V(0.130f, 0.142f, 0.168f);
    public static readonly Vector4 ToolbarBtnActive = Accent;

    public static readonly Vector4 PlayGreen    = V(0.22f, 0.74f, 0.44f);
    public static readonly Vector4 PauseYellow  = V(0.92f, 0.70f, 0.24f);
    public static readonly Vector4 StopRed      = V(0.86f, 0.30f, 0.32f);

    // ═══════════════════════════════════════════════════════════════════
    //  PROJECT BROWSER (Launcher)
    // ═══════════════════════════════════════════════════════════════════
    public static readonly Vector4 LauncherBg        = V(0.052f, 0.057f, 0.068f);
    public static readonly Vector4 LauncherSidebar   = V(0.042f, 0.046f, 0.055f);
    public static readonly Vector4 LauncherCardBg    = V(0.080f, 0.088f, 0.104f);
    public static readonly Vector4 LauncherCardHover = V(0.110f, 0.120f, 0.142f);
    public static readonly Vector4 LauncherBrand     = new(0.42f, 0.74f, 1.00f, 1.0f);

    // ═══════════════════════════════════════════════════════════════════
    //  SPACING - Generous breathing room (minimalist layout)
    // ═══════════════════════════════════════════════════════════════════
    public const float Pad        = 10f;
    public const float PadLg      = 16f;
    public const float PadXl      = 24f;
    public const float HeaderH    = 38f;   // Single unified top bar height
    public const float ToolbarH   = 0f;    // Merged into unified top bar to eliminate double-bar clutter
    public const float TabH       = 28f;
    public const float SplitterW  = 3f;
    public const float RowH       = 28f;   // Breathable list rows
    public const float SectionH   = 28f;
    public const float StatusH    = 24f;
    public const float MinPanelW  = 80f;
    public const float PropLabelW = 92f;

    private static Vector4 V(float r, float g, float b) => new(r, g, b, 1.0f);

    public static Vector4 Lighten(Vector4 c, float amount) =>
        new(c.X + (1f - c.X) * amount, c.Y + (1f - c.Y) * amount, c.Z + (1f - c.Z) * amount, c.W);

    public static Vector4 Darken(Vector4 c, float amount) =>
        new(c.X * (1f - amount), c.Y * (1f - amount), c.Z * (1f - amount), c.W);

    public static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);
}
