using System;
using System.Numerics;
using System.Collections.Generic;
using NotBSRenderer;

namespace BlueSky.Editor.UI;

/// <summary>
/// Animated button with smooth hover, press, and focus states
/// </summary>
public class AnimatedButton
{
    private static readonly Dictionary<uint, UIElementState> _buttonStates = new();
    private static float _globalTime = 0f;
    
    public static void UpdateGlobalTime(float deltaTime)
    {
        _globalTime += deltaTime;
        
        // Update all button states
        foreach (var state in _buttonStates.Values)
        {
            state.Update(deltaTime);
        }
    }
    
    public static void ClearStates()
    {
        _buttonStates.Clear();
    }
    
    /// <summary>
    /// Render an animated button with smooth transitions
    /// </summary>
    public static bool Render(
        NotBSUI ui,
        float x, float y, float w, float h,
        string text,
        uint id,
        Vector4? normalColor = null,
        Vector4? hoverColor = null,
        Vector4? pressColor = null,
        Vector4? textColor = null,
        bool enabled = true,
        string icon = "")
    {
        // Get or create state for this button
        if (!_buttonStates.TryGetValue(id, out var state))
        {
            state = new UIElementState();
            _buttonStates[id] = state;
        }
        
        // Default colors
        normalColor ??= ModernTheme.ToolbarBtnNormal;
        hoverColor ??= ModernTheme.ToolbarBtnHover;
        pressColor ??= ModernTheme.Accent;
        textColor ??= ModernTheme.TextPrimary;
        
        // Check interaction
        bool isHovered = ui.IsHovering(x, y, w, h) && enabled;
        bool isPressed = isHovered && ui.IsMouseDown;
        bool wasClicked = false;
        
        bool wasDownOnButton = state.IsPressed;

        // Update state
        state.IsHovered = isHovered;
        state.IsPressed = isPressed;
        
        // Calculate animated color
        Vector4 bgColor = normalColor.Value;
        if (state.HoverAmount.Current > 0.01f)
        {
            bgColor = Vector4.Lerp(bgColor, hoverColor.Value, state.HoverAmount.Current);
        }
        if (state.PressAmount.Current > 0.01f)
        {
            bgColor = Vector4.Lerp(bgColor, pressColor.Value, state.PressAmount.Current);
        }
        
        // Draw a flat, sharp button. The transport controls should feel like
        // editor chrome, not animated launcher UI.
        ui.Panel(x, y, w, h, enabled ? bgColor : ModernTheme.Bg2);
        var borderColor = enabled && state.HoverAmount.Current > 0.05f
            ? ModernTheme.WithAlpha(pressColor.Value, 0.75f)
            : ModernTheme.Border1;
        ui.Panel(x, y, w, 1, borderColor);
        ui.Panel(x, y + h - 1, w, 1, borderColor);
        ui.Panel(x, y, 1, h, borderColor);
        ui.Panel(x + w - 1, y, 1, h, borderColor);
        
        // Text with icon
        float textX = x + w * 0.5f;
        float textY = y + h * 0.5f - 8;
        
        if (!string.IsNullOrEmpty(icon))
        {
            // Icon + text layout
            float iconWidth = icon.Length * 7.2f;
            float textWidth = text.Length * 7.2f;
            float totalWidth = iconWidth + textWidth + 6;
            float startX = x + (w - totalWidth) * 0.5f;
            
            ui.SetCursor(startX, textY);
            ui.Text(icon, enabled ? ModernTheme.WithAlpha(pressColor.Value, 0.9f) : ModernTheme.TextDisabled);
            
            ui.SetCursor(startX + iconWidth + 6, textY);
            ui.Text(text, enabled ? textColor.Value : ModernTheme.TextDisabled);
        }
        else
        {
            // Center text
            float textWidth = text.Length * 7.2f;
            ui.SetCursor(textX - textWidth * 0.5f, textY);
            ui.Text(text, enabled ? textColor.Value : ModernTheme.TextDisabled);
        }
        
        // Detect click (mouse was down and now released while hovering)
        if (isHovered && !ui.IsMouseDown && wasDownOnButton && enabled)
        {
            wasClicked = true;
        }
        
        return wasClicked;
    }
    
    /// <summary>
    /// Render a primary action button (more prominent)
    /// </summary>
    public static bool RenderPrimary(
        NotBSUI ui,
        float x, float y, float w, float h,
        string text,
        uint id,
        bool enabled = true,
        string icon = "")
    {
        return Render(
            ui, x, y, w, h, text, id,
            normalColor: ModernTheme.Accent,
            hoverColor: ModernTheme.AccentHover,
            pressColor: ModernTheme.AccentPressed,
            textColor: ModernTheme.TextPrimary,
            enabled: enabled,
            icon: icon
        );
    }
    
    /// <summary>
    /// Render a danger/destructive button (red)
    /// </summary>
    public static bool RenderDanger(
        NotBSUI ui,
        float x, float y, float w, float h,
        string text,
        uint id,
        bool enabled = true,
        string icon = "")
    {
        return Render(
            ui, x, y, w, h, text, id,
            normalColor: ModernTheme.WithAlpha(ModernTheme.Red, 0.2f),
            hoverColor: ModernTheme.WithAlpha(ModernTheme.Red, 0.4f),
            pressColor: ModernTheme.Red,
            textColor: ModernTheme.Red,
            enabled: enabled,
            icon: icon
        );
    }
    
    /// <summary>
    /// Render a success button (green)
    /// </summary>
    public static bool RenderSuccess(
        NotBSUI ui,
        float x, float y, float w, float h,
        string text,
        uint id,
        bool enabled = true,
        string icon = "")
    {
        return Render(
            ui, x, y, w, h, text, id,
            normalColor: ModernTheme.WithAlpha(ModernTheme.Green, 0.2f),
            hoverColor: ModernTheme.WithAlpha(ModernTheme.Green, 0.4f),
            pressColor: ModernTheme.Green,
            textColor: ModernTheme.Green,
            enabled: enabled,
            icon: icon
        );
    }
}
