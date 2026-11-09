using System;
using System.Numerics;
using BlueSky.BSR.RHI;

namespace BlueSky.Editor.UI;

/// <summary>
/// Minimalist, low-noise drawing helpers for editor chrome: borderless tonal surfaces,
/// clean section dividers, and airy X/Y/Z vector pills.
/// </summary>
public static class EditorChrome
{
    public static void Surface(EditorUI ui, float x, float y, float w, float h, Vector4? bg = null, bool elevated = false)
    {
        if (elevated)
            ui.Shadow(x + 2, y + 3, w, h, 3, 5, 0.25f);

        ui.RoundedPanel(x, y, w, h, bg ?? EditorTheme.Bg1, EditorTheme.CardRadius);
    }

    public static void Header(EditorUI ui, float x, float y, float w, float h, string title, string? meta = null, Vector4? accent = null)
    {
        ui.Panel(x, y, w, h, EditorTheme.Bg1);
        ui.SetCursor(x + 14, y + (h - EditorTheme.CharHeight) * 0.5f);
        ui.Text(title, EditorTheme.TextPrimary);

        if (!string.IsNullOrEmpty(meta))
        {
            float metaW = ui.MeasureText(meta);
            ui.SetCursor(x + w - metaW - 14f, y + (h - EditorTheme.CharHeight) * 0.5f);
            ui.Text(meta, EditorTheme.TextMuted);
        }

        ui.Panel(x + 12, y + h - 1, MathF.Max(0, w - 24), 1, EditorTheme.Border1);
    }

    public static void Pill(EditorUI ui, float x, float y, float w, float h, string text, Vector4 accent, bool filled = false)
    {
        var bg = filled ? EditorTheme.WithAlpha(accent, 0.20f) : EditorTheme.Bg2;
        ui.RoundedPanel(x, y, w, h, bg, EditorTheme.PillRadius);
        ui.Circle(x + 11, y + h * 0.5f, 3f, accent, true);
        ui.TextCentered(x + 16, y, MathF.Max(0, w - 20), h, text, filled ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);
    }

    public static void Badge(EditorUI ui, float x, float y, string text, Vector4 accent)
    {
        float w = ui.MeasureText(text) + 12f;
        float h = 18f;
        ui.RoundedPanel(x, y, w, h, EditorTheme.WithAlpha(accent, 0.14f), 4f);
        ui.TextCentered(x, y, w, h, text, accent);
    }

    public static void SectionTitle(EditorUI ui, float x, float y, float w, string title, Vector4 accent)
    {
        float barH = 26f;
        ui.RoundedPanel(x, y + 6, 3, barH - 12, accent, 1.5f);
        ui.SetCursor(x + 10, y + 5);
        ui.Text(title, EditorTheme.TextPrimary);
        ui.Panel(x, y + barH - 1, w, 1, EditorTheme.Border1);
    }

    /// <summary>
    /// Minimal 3-axis (X / Y / Z) vector row with soft rounded pills and colored axis labels.
    /// </summary>
    public static void Vector3Row(EditorUI ui, float x, float y, float totalW, float xVal, float yVal, float zVal)
    {
        float gap = 8f;
        float boxW = MathF.Max(38f, (totalW - gap * 2f) / 3f);
        float boxH = 24f;

        DrawAxisBox(ui, x, y, boxW, boxH, "X", xVal, EditorTheme.AxisX);
        DrawAxisBox(ui, x + boxW + gap, y, boxW, boxH, "Y", yVal, EditorTheme.AxisY);
        DrawAxisBox(ui, x + (boxW + gap) * 2f, y, boxW, boxH, "Z", zVal, EditorTheme.AxisZ);
    }

    private static void DrawAxisBox(EditorUI ui, float x, float y, float w, float h, string axis, float val, Vector4 axisColor)
    {
        ui.RoundedPanel(x, y, w, h, EditorTheme.Bg0, EditorTheme.InputRadius);
        ui.TextCentered(x + 2, y, 16f, h, axis, axisColor);

        string formatted = MathF.Abs(val) >= 100f ? $"{val:F0}" : $"{val:F1}";
        ui.TextInRect(x + 20f, y, MathF.Max(0f, w - 24f), h, formatted, EditorTheme.TextSecondary, EditorUI.TextAlign.Left);
    }

    public static void Stroke(EditorUI ui, float x, float y, float w, float h, Vector4 color)
    {
        if (w <= 0 || h <= 0) return;
        ui.Panel(x, y, w, 1, color);
        ui.Panel(x, y + h - 1, w, 1, color);
        ui.Panel(x, y, 1, h, color);
        ui.Panel(x + w - 1, y, 1, h, color);
    }
}
