using System.Numerics;

namespace BlueSky.Editor.UI;

/// <summary>
/// Modern, refined color palette with better contrast and visual hierarchy
/// </summary>
public static class ModernTheme
{
    // ═══════════════════════════════════════════════════════════════════
    //  BASE COLORS - Refined dark theme with better contrast
    // ═══════════════════════════════════════════════════════════════════
    
    // Backgrounds - Layered depth
    public static readonly Vector4 Bg0 = new(0.09f, 0.095f, 0.105f, 1f);  // Deepest
    public static readonly Vector4 Bg1 = new(0.11f, 0.115f, 0.125f, 1f);  // Deep
    public static readonly Vector4 Bg2 = new(0.13f, 0.135f, 0.145f, 1f);  // Mid
    public static readonly Vector4 Bg3 = new(0.15f, 0.155f, 0.165f, 1f);  // Surface
    public static readonly Vector4 Bg4 = new(0.17f, 0.175f, 0.185f, 1f);  // Elevated
    
    // Text - Clear hierarchy
    public static readonly Vector4 TextPrimary = new(0.95f, 0.96f, 0.97f, 1f);
    public static readonly Vector4 TextSecondary = new(0.75f, 0.77f, 0.80f, 1f);
    public static readonly Vector4 TextMuted = new(0.55f, 0.57f, 0.60f, 1f);
    public static readonly Vector4 TextDisabled = new(0.40f, 0.42f, 0.45f, 1f);
    
    // Borders - Subtle separation
    public static readonly Vector4 Border0 = new(0.20f, 0.22f, 0.25f, 1f);  // Subtle
    public static readonly Vector4 Border1 = new(0.25f, 0.27f, 0.30f, 1f);  // Normal
    public static readonly Vector4 Border2 = new(0.30f, 0.32f, 0.35f, 1f);  // Strong
    
    // ═══════════════════════════════════════════════════════════════════
    //  ACCENT COLORS - Vibrant and modern
    // ═══════════════════════════════════════════════════════════════════
    
    // Primary accent - Electric blue
    public static readonly Vector4 Accent = new(0.30f, 0.60f, 1.00f, 1f);
    public static readonly Vector4 AccentHover = new(0.40f, 0.70f, 1.00f, 1f);
    public static readonly Vector4 AccentPressed = new(0.25f, 0.55f, 0.95f, 1f);
    public static readonly Vector4 AccentDim = new(0.20f, 0.45f, 0.80f, 1f);
    public static readonly Vector4 AccentGlow = new(0.30f, 0.60f, 1.00f, 0.15f);
    
    // Secondary accents
    public static readonly Vector4 Purple = new(0.70f, 0.40f, 1.00f, 1f);
    public static readonly Vector4 PurpleGlow = new(0.70f, 0.40f, 1.00f, 0.15f);
    
    public static readonly Vector4 Green = new(0.35f, 0.90f, 0.55f, 1f);
    public static readonly Vector4 GreenGlow = new(0.35f, 0.90f, 0.55f, 0.15f);
    
    public static readonly Vector4 Orange = new(1.00f, 0.65f, 0.30f, 1f);
    public static readonly Vector4 OrangeGlow = new(1.00f, 0.65f, 0.30f, 0.15f);
    
    public static readonly Vector4 Red = new(0.95f, 0.40f, 0.40f, 1f);
    public static readonly Vector4 RedGlow = new(0.95f, 0.40f, 0.40f, 0.15f);
    
    public static readonly Vector4 Yellow = new(1.00f, 0.85f, 0.40f, 1f);
    public static readonly Vector4 Cyan = new(0.40f, 0.90f, 0.95f, 1f);
    
    // ═══════════════════════════════════════════════════════════════════
    //  INTERACTIVE STATES - Smooth transitions
    // ═══════════════════════════════════════════════════════════════════
    
    public static readonly Vector4 HoverBg = new(0.18f, 0.20f, 0.23f, 1f);
    public static readonly Vector4 PressedBg = new(0.14f, 0.16f, 0.19f, 1f);
    public static readonly Vector4 SelectionBg = new(0.25f, 0.45f, 0.75f, 0.25f);
    public static readonly Vector4 SelectionBorder = new(0.30f, 0.60f, 1.00f, 0.8f);
    
    // ═══════════════════════════════════════════════════════════════════
    //  SEMANTIC COLORS - Status and feedback
    // ═══════════════════════════════════════════════════════════════════
    
    public static readonly Vector4 Success = new(0.35f, 0.90f, 0.55f, 1f);
    public static readonly Vector4 Warning = new(1.00f, 0.75f, 0.30f, 1f);
    public static readonly Vector4 Error = new(0.95f, 0.40f, 0.40f, 1f);
    public static readonly Vector4 Info = new(0.40f, 0.75f, 1.00f, 1f);
    
    // ═══════════════════════════════════════════════════════════════════
    //  COMPONENT-SPECIFIC COLORS
    // ═══════════════════════════════════════════════════════════════════
    
    // Toolbar
    public static readonly Vector4 ToolbarBg = Bg2;
    public static readonly Vector4 ToolbarBtnNormal = new(0.16f, 0.17f, 0.19f, 1f);
    public static readonly Vector4 ToolbarBtnHover = new(0.20f, 0.22f, 0.25f, 1f);
    public static readonly Vector4 ToolbarBtnActive = new(0.25f, 0.45f, 0.75f, 0.4f);
    
    // Panels
    public static readonly Vector4 PanelHeaderBg = Bg3;
    public static readonly Vector4 PanelContentBg = Bg1;
    public static readonly Vector4 PanelBorder = Border1;
    
    // Cards
    public static readonly Vector4 CardBg = new(0.14f, 0.145f, 0.155f, 1f);
    public static readonly Vector4 CardHover = new(0.17f, 0.175f, 0.185f, 1f);
    public static readonly Vector4 CardPressed = new(0.12f, 0.125f, 0.135f, 1f);
    
    // Inputs
    public static readonly Vector4 InputBg = new(0.10f, 0.105f, 0.115f, 1f);
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
}
