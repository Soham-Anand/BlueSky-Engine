using System.Numerics;

namespace BlueSky.Editor.UI;

/// <summary>
/// Modern palette used by animated/notification widgets.
/// Kept visually aligned with the sharper editor chrome.
/// </summary>
public static class ModernTheme
{
    // ═══════════════════════════════════════════════════════════════════
    //  BASE COLORS - Refined dark theme with better contrast
    // ═══════════════════════════════════════════════════════════════════
    
    // Backgrounds - Layered depth
    public static readonly Vector4 Bg0 = EditorTheme.Bg0;
    public static readonly Vector4 Bg1 = EditorTheme.Bg1;
    public static readonly Vector4 Bg2 = EditorTheme.Bg2;
    public static readonly Vector4 Bg3 = EditorTheme.Bg3;
    public static readonly Vector4 Bg4 = EditorTheme.Bg4;
    
    // Text - Clear hierarchy
    public static readonly Vector4 TextPrimary = EditorTheme.TextPrimary;
    public static readonly Vector4 TextSecondary = EditorTheme.TextSecondary;
    public static readonly Vector4 TextMuted = EditorTheme.TextMuted;
    public static readonly Vector4 TextDisabled = EditorTheme.TextDisabled;
    
    // Borders - Subtle separation
    public static readonly Vector4 Border0 = EditorTheme.Border0;
    public static readonly Vector4 Border1 = EditorTheme.Border1;
    public static readonly Vector4 Border2 = EditorTheme.Border2;
    
    // ═══════════════════════════════════════════════════════════════════
    //  ACCENT COLORS - Vibrant and modern
    // ═══════════════════════════════════════════════════════════════════
    
    // Primary accent - Electric blue
    public static readonly Vector4 Accent = EditorTheme.Accent;
    public static readonly Vector4 AccentHover = EditorTheme.AccentHover;
    public static readonly Vector4 AccentPressed = EditorTheme.AccentDim;
    public static readonly Vector4 AccentDim = EditorTheme.AccentDim;
    public static readonly Vector4 AccentGlow = EditorTheme.AccentGlow;
    
    // Secondary accents
    public static readonly Vector4 Purple = EditorTheme.Purple;
    public static readonly Vector4 PurpleGlow = EditorTheme.WithAlpha(EditorTheme.Purple, 0.12f);
    
    public static readonly Vector4 Green = EditorTheme.Green;
    public static readonly Vector4 GreenGlow = EditorTheme.WithAlpha(EditorTheme.Green, 0.12f);
    
    public static readonly Vector4 Blue = EditorTheme.AccentHover;
    
    public static readonly Vector4 Orange = EditorTheme.Orange;
    public static readonly Vector4 OrangeGlow = EditorTheme.WithAlpha(EditorTheme.Orange, 0.12f);
    
    public static readonly Vector4 Red = EditorTheme.Red;
    public static readonly Vector4 RedGlow = EditorTheme.WithAlpha(EditorTheme.Red, 0.12f);
    
    public static readonly Vector4 Yellow = EditorTheme.Yellow;
    public static readonly Vector4 Cyan = EditorTheme.AccentCyan;
    
    // ═══════════════════════════════════════════════════════════════════
    //  INTERACTIVE STATES - Smooth transitions
    // ═══════════════════════════════════════════════════════════════════
    
    public static readonly Vector4 HoverBg = EditorTheme.HoverBg;
    public static readonly Vector4 PressedBg = EditorTheme.Bg2;
    public static readonly Vector4 SelectionBg = EditorTheme.SelectionBg;
    public static readonly Vector4 SelectionBorder = EditorTheme.SelectionBorder;
    
    // ═══════════════════════════════════════════════════════════════════
    //  SEMANTIC COLORS - Status and feedback
    // ═══════════════════════════════════════════════════════════════════
    
    public static readonly Vector4 Success = Green;
    public static readonly Vector4 Warning = Yellow;
    public static readonly Vector4 Error = Red;
    public static readonly Vector4 Info = Blue;
    
    // ═══════════════════════════════════════════════════════════════════
    //  COMPONENT-SPECIFIC COLORS
    // ═══════════════════════════════════════════════════════════════════
    
    // Toolbar
    public static readonly Vector4 ToolbarBg = EditorTheme.ToolbarBg;
    public static readonly Vector4 ToolbarBtnNormal = EditorTheme.ToolbarBtnNormal;
    public static readonly Vector4 ToolbarBtnHover = EditorTheme.ToolbarBtnHover;
    public static readonly Vector4 ToolbarBtnActive = new(0.16f, 0.36f, 0.62f, 0.40f);
    
    // Panels
    public static readonly Vector4 PanelHeaderBg = Bg3;
    public static readonly Vector4 PanelContentBg = Bg1;
    public static readonly Vector4 PanelBorder = Border1;
    
    // Cards
    public static readonly Vector4 CardBg = new(0.058f, 0.064f, 0.076f, 1f);
    public static readonly Vector4 CardHover = new(0.088f, 0.098f, 0.116f, 1f);
    public static readonly Vector4 CardPressed = new(0.047f, 0.051f, 0.058f, 1f);
    
    // Inputs
    public static readonly Vector4 InputBg = new(0.032f, 0.035f, 0.040f, 1f);
    public static readonly Vector4 InputBorder = Border1;
    public static readonly Vector4 InputFocusBorder = Accent;
    public static readonly Vector4 InputPlaceholder = TextDisabled;
    
    // Scrollbars
    public static readonly Vector4 ScrollbarTrack = new(0.12f, 0.125f, 0.135f, 1f);
    public static readonly Vector4 ScrollbarThumb = new(0.25f, 0.27f, 0.30f, 1f);
    public static readonly Vector4 ScrollbarThumbHover = new(0.35f, 0.37f, 0.40f, 1f);
    
    // Modals
    public static readonly Vector4 ModalOverlay = new(0f, 0f, 0f, 0.7f);
    public static readonly Vector4 ModalBg = Bg2;
    public static readonly Vector4 ModalBorder = Border2;
    
    // ═══════════════════════════════════════════════════════════════════
    //  UTILITY FUNCTIONS
    // ═══════════════════════════════════════════════════════════════════
    
    public static Vector4 WithAlpha(Vector4 color, float alpha)
    {
        return new Vector4(color.X, color.Y, color.Z, alpha);
    }
    
    public static Vector4 Lighten(Vector4 color, float amount)
    {
        return new Vector4(
            MathF.Min(1f, color.X + amount),
            MathF.Min(1f, color.Y + amount),
            MathF.Min(1f, color.Z + amount),
            color.W
        );
    }
    
    public static Vector4 Darken(Vector4 color, float amount)
    {
        return new Vector4(
            MathF.Max(0f, color.X - amount),
            MathF.Max(0f, color.Y - amount),
            MathF.Max(0f, color.Z - amount),
            color.W
        );
    }
    
    public static Vector4 Lerp(Vector4 a, Vector4 b, float t)
    {
        return Vector4.Lerp(a, b, t);
    }
    
    public static Vector4 Mix(Vector4 a, Vector4 b, float amount)
    {
        return Vector4.Lerp(a, b, amount);
    }
    
    // Glow effect - adds subtle luminosity
    public static Vector4 AddGlow(Vector4 color, float intensity = 0.15f)
    {
        return new Vector4(
            MathF.Min(1f, color.X + intensity),
            MathF.Min(1f, color.Y + intensity),
            MathF.Min(1f, color.Z + intensity),
            color.W
        );
    }
    
    // Convert Vector4 to uint color (RGBA)
    public static uint ToUInt(Vector4 color)
    {
        byte r = (byte)(color.X * 255f);
        byte g = (byte)(color.Y * 255f);
        byte b = (byte)(color.Z * 255f);
        byte a = (byte)(color.W * 255f);
        return (uint)((a << 24) | (b << 16) | (g << 8) | r);
    }
}
