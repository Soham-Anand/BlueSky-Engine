using System;
using System.Numerics;
using System.Collections.Generic;
using BlueSky.BSR.RHI;

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
        EditorUI ui,
        float x, float y, float w, float h,
        string text,
        uint id,
        Vector4? normalColor = null,
        Vector4? hoverColor = null,
        Vector4? pressColor = null,
        Vector4? textColor = null,
        bool enabled = true,
        string icon = "",
        EditorUI.UIIcon iconKind = EditorUI.UIIcon.None)
    {
        // Get or create state for this button
        if (!_buttonStates.TryGetValue(id, out var state))
        {
            state = new UIElementState();
            _buttonStates[id] = state;
        }
        
        // Default colors
        normalColor ??= EditorTheme.ToolbarBtnNormal;
        hoverColor ??= EditorTheme.ToolbarBtnHover;
        pressColor ??= EditorTheme.Accent;
        textColor ??= EditorTheme.TextPrimary;
        
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
        
        float r = MathF.Min(EditorTheme.ButtonRadius, h * 0.35f);
        ui.RoundedPanel(x, y, w, h, enabled ? bgColor : EditorTheme.Bg2, r);

        var borderColor = enabled && state.HoverAmount.Current > 0.05f
            ? EditorTheme.WithAlpha(pressColor.Value, 0.85f)
            : EditorTheme.Border1;
        EditorChrome.Stroke(ui, x, y, w, h, borderColor);
        if (enabled)
            ui.Panel(x + 2, y + 1, MathF.Max(0, w - 4), 1, EditorTheme.Highlight);
        
        // Text with icon
        float textX = x + w * 0.5f;
        float textY = y + h * 0.5f - 7;

        Vector4 glyphColor = enabled ? EditorTheme.WithAlpha(pressColor.Value, 0.95f) : EditorTheme.TextDisabled;
        if (iconKind != EditorUI.UIIcon.None)
        {
            // Vector icon + text layout
            float iconSize = Math.Min(w, h) * 0.52f;
            float textWidth = ui.MeasureText(text);
            float totalWidth = iconSize + (text.Length > 0 ? textWidth + 7 : 0);
            float startX = x + (w - totalWidth) * 0.5f;
            float iconY = y + (h - iconSize) * 0.5f;

            ui.Icon(iconKind, startX, iconY, iconSize, glyphColor);

            if (text.Length > 0)
            {
                ui.SetCursor(startX + iconSize + 7, textY);
                ui.Text(text, enabled ? textColor.Value : EditorTheme.TextDisabled);
            }
        }
        else if (!string.IsNullOrEmpty(icon))
        {
            // Icon + text layout
            float iconWidth = ui.MeasureText(icon);
            float textWidth = ui.MeasureText(text);
            float totalWidth = iconWidth + textWidth + 6;
            float startX = x + (w - totalWidth) * 0.5f;
            
            ui.SetCursor(startX, textY);
            ui.Text(icon, enabled ? EditorTheme.WithAlpha(pressColor.Value, 0.95f) : EditorTheme.TextDisabled);
            
            ui.SetCursor(startX + iconWidth + 6, textY);
            ui.Text(text, enabled ? textColor.Value : EditorTheme.TextDisabled);
        }
        else
        {
            ui.TextCentered(x, y, w, h, text, enabled ? textColor.Value : EditorTheme.TextDisabled);
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
        EditorUI ui,
        float x, float y, float w, float h,
        string text,
        uint id,
        bool enabled = true,
        string icon = "",
        EditorUI.UIIcon iconKind = EditorUI.UIIcon.None)
    {
        return Render(
            ui, x, y, w, h, text, id,
            normalColor: EditorTheme.Accent,
            hoverColor: EditorTheme.AccentHover,
            pressColor: EditorTheme.AccentDim,
            textColor: EditorTheme.TextPrimary,
            enabled: enabled,
            icon: icon,
            iconKind: iconKind
        );
    }
    
    /// <summary>
    /// Render a danger/destructive button (red)
    /// </summary>
    public static bool RenderDanger(
        EditorUI ui,
        float x, float y, float w, float h,
        string text,
        uint id,
        bool enabled = true,
        string icon = "",
        EditorUI.UIIcon iconKind = EditorUI.UIIcon.None)
    {
        return Render(
            ui, x, y, w, h, text, id,
            normalColor: EditorTheme.WithAlpha(EditorTheme.Red, 0.22f),
            hoverColor: EditorTheme.WithAlpha(EditorTheme.Red, 0.42f),
            pressColor: EditorTheme.Red,
            textColor: EditorTheme.Red,
            enabled: enabled,
            icon: icon,
            iconKind: iconKind
        );
    }
    
    /// <summary>
    /// Render a success button (green)
    /// </summary>
    public static bool RenderSuccess(
        EditorUI ui,
        float x, float y, float w, float h,
        string text,
        uint id,
        bool enabled = true,
        string icon = "",
        EditorUI.UIIcon iconKind = EditorUI.UIIcon.None)
    {
        return Render(
            ui, x, y, w, h, text, id,
            normalColor: EditorTheme.WithAlpha(EditorTheme.Green, 0.22f),
            hoverColor: EditorTheme.WithAlpha(EditorTheme.Green, 0.42f),
            pressColor: EditorTheme.Green,
            textColor: EditorTheme.Green,
            enabled: enabled,
            icon: icon,
            iconKind: iconKind
        );
    }
}
