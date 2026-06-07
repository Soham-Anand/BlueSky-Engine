using System;
using System.Numerics;
using NotBSRenderer;

namespace BlueSky.Editor.UI;

/// <summary>
/// Small shared drawing helpers for editor chrome. These keep panels visually
/// consistent without pulling layout ownership away from each tool.
/// </summary>
public static class EditorChrome
{
    public static void Surface(NotBSUI ui, float x, float y, float w, float h, Vector4? bg = null, bool elevated = false)
    {
        if (elevated)
            ui.Shadow(x + 1, y + 1, w, h, 2, 4, 0.22f);

        ui.RoundedPanel(x, y, w, h, bg ?? EditorTheme.Bg1, EditorTheme.CardRadius);
        Stroke(ui, x, y, w, h, EditorTheme.Border1);
        ui.Panel(x + 1, y + 1, MathF.Max(0, w - 2), 1, EditorTheme.Highlight);
    }

    public static void Header(NotBSUI ui, float x, float y, float w, float h, string title, string? meta = null, Vector4? accent = null)
    {
        var a = accent ?? EditorTheme.Accent;
        ui.RoundedGradientPanel(x, y, w, h, EditorTheme.Bg3, EditorTheme.Bg2, EditorTheme.SmallRadius);
        ui.Panel(x, y, 3, h, a);
        ui.Panel(x, y + h - 1, w, 1, EditorTheme.Border0);
        ui.SetCursor(x + 12, y + h * 0.5f - 7);
        ui.Text(title, EditorTheme.TextPrimary);

        if (!string.IsNullOrEmpty(meta))
        {
            float metaW = ui.MeasureText(meta);
            ui.SetCursor(x + w - metaW - 12, y + h * 0.5f - 7);
            ui.Text(meta, EditorTheme.TextMuted);
        }
    }

    public static void Pill(NotBSUI ui, float x, float y, float w, float h, string text, Vector4 accent, bool filled = false)
    {
        var bg = filled ? EditorTheme.WithAlpha(accent, 0.22f) : EditorTheme.Bg2;
        ui.RoundedPanel(x, y, w, h, bg, EditorTheme.PillRadius);
        Stroke(ui, x, y, w, h, EditorTheme.WithAlpha(accent, filled ? 0.55f : 0.32f));
        ui.Circle(x + 11, y + h / 2, 3, accent, true);
        ui.TextCentered(x + 20, y, MathF.Max(0, w - 26), h, text, filled ? EditorTheme.TextPrimary : EditorTheme.TextSecondary);
    }

    public static void SectionTitle(NotBSUI ui, float x, float y, float w, string title, Vector4 accent)
    {
        ui.Panel(x, y + 10, w, 1, EditorTheme.Border1);
        ui.Panel(x, y + 10, 28, 1, accent);
        ui.SetCursor(x, y - 1);
        ui.Text(title, EditorTheme.TextMuted);
    }

    public static void Stroke(NotBSUI ui, float x, float y, float w, float h, Vector4 color)
    {
        ui.Panel(x, y, w, 1, color);
        ui.Panel(x, y + h - 1, w, 1, color);
        ui.Panel(x, y, 1, h, color);
        ui.Panel(x + w - 1, y, 1, h, color);
    }
}
