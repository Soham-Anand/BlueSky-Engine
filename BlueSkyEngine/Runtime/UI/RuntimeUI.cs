using System;
using System.Collections.Generic;
using System.Numerics;
using NotBSRenderer;

namespace BlueSky.Runtime.UI;

public enum RuntimeUIAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    Center,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

internal enum RuntimeUIElementKind
{
    Label,
    Panel,
    ProgressBar
}

public static class RuntimeUI
{
    private readonly struct Element
    {
        public RuntimeUIElementKind Kind { get; init; }
        public string Text { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public float Width { get; init; }
        public float Height { get; init; }
        public float Value { get; init; }
        public Vector4 Color { get; init; }
        public Vector4 ColorB { get; init; }
        public RuntimeUIAnchor Anchor { get; init; }
    }

    private static readonly object Sync = new();
    private static readonly List<Element> FrameElements = new();

    public static event Action<RuntimeUIContext>? Draw;

    public static Vector4 TextPrimary { get; } = new(0.94f, 0.96f, 1.0f, 1.0f);
    public static Vector4 PanelColor { get; } = new(0.045f, 0.052f, 0.068f, 0.86f);
    public static Vector4 Accent { get; } = new(0.24f, 0.58f, 1.0f, 1.0f);

    public static void BeginFrame()
    {
        lock (Sync)
            FrameElements.Clear();
    }

    public static void Label(string text, float x, float y,
                             RuntimeUIAnchor anchor = RuntimeUIAnchor.TopLeft,
                             Vector4? color = null)
    {
        lock (Sync)
        {
            FrameElements.Add(new Element
            {
                Kind = RuntimeUIElementKind.Label,
                Text = text ?? string.Empty,
                X = x,
                Y = y,
                Color = color ?? TextPrimary,
                Anchor = anchor
            });
        }
    }

    public static void Panel(float x, float y, float width, float height,
                             RuntimeUIAnchor anchor = RuntimeUIAnchor.TopLeft,
                             Vector4? color = null)
    {
        lock (Sync)
        {
            FrameElements.Add(new Element
            {
                Kind = RuntimeUIElementKind.Panel,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Color = color ?? PanelColor,
                Anchor = anchor
            });
        }
    }

    public static void ProgressBar(float x, float y, float width, float height, float value,
                                   RuntimeUIAnchor anchor = RuntimeUIAnchor.TopLeft,
                                   Vector4? fill = null,
                                   Vector4? background = null)
    {
        lock (Sync)
        {
            FrameElements.Add(new Element
            {
                Kind = RuntimeUIElementKind.ProgressBar,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Value = Math.Clamp(value, 0f, 1f),
                Color = fill ?? Accent,
                ColorB = background ?? new Vector4(0.02f, 0.025f, 0.034f, 0.80f),
                Anchor = anchor
            });
        }
    }

    public static void Render(NotBSUI ui, float width, float height, float deltaTime)
    {
        Element[] elements;
        lock (Sync)
            elements = FrameElements.ToArray();

        var context = new RuntimeUIContext(ui, width, height, deltaTime);
        foreach (var element in elements)
            context.DrawElement(element.Kind, element.Text, element.X, element.Y, element.Width, element.Height,
                                element.Value, element.Color, element.ColorB, element.Anchor);

        Draw?.Invoke(context);

        lock (Sync)
            FrameElements.Clear();
    }
}

public readonly struct RuntimeUIContext
{
    private readonly NotBSUI _ui;

    public RuntimeUIContext(NotBSUI ui, float width, float height, float deltaTime)
    {
        _ui = ui;
        Width = width;
        Height = height;
        DeltaTime = deltaTime;
    }

    public float Width { get; }
    public float Height { get; }
    public float DeltaTime { get; }

    public void Label(string text, float x, float y,
                      RuntimeUIAnchor anchor = RuntimeUIAnchor.TopLeft,
                      Vector4? color = null)
    {
        var size = new Vector2(_ui.MeasureText(text), _ui.TextLineHeight);
        var pos = Resolve(x, y, size.X, size.Y, anchor);
        _ui.TextAt(pos.X, pos.Y, text, color ?? RuntimeUI.TextPrimary);
    }

    public void Panel(float x, float y, float width, float height,
                      RuntimeUIAnchor anchor = RuntimeUIAnchor.TopLeft,
                      Vector4? color = null)
    {
        var pos = Resolve(x, y, width, height, anchor);
        _ui.RoundedPanel(pos.X + 3, pos.Y + 4, width, height, new Vector4(0, 0, 0, 0.24f), 8f);
        _ui.RoundedPanel(pos.X, pos.Y, width, height, color ?? RuntimeUI.PanelColor, 8f);
    }

    public bool Button(float x, float y, float width, float height, string text,
                       RuntimeUIAnchor anchor = RuntimeUIAnchor.TopLeft,
                       uint id = 0)
    {
        var pos = Resolve(x, y, width, height, anchor);
        return _ui.ButtonEx(pos.X, pos.Y, width, height, text,
            new Vector4(0.10f, 0.13f, 0.17f, 0.92f),
            new Vector4(0.15f, 0.20f, 0.28f, 0.98f),
            new Vector4(0.06f, 0.10f, 0.18f, 1.00f),
            new Vector4(0, 0, 0, 0),
            RuntimeUI.TextPrimary,
            id);
    }

    public void ProgressBar(float x, float y, float width, float height, float value,
                            RuntimeUIAnchor anchor = RuntimeUIAnchor.TopLeft,
                            Vector4? fill = null,
                            Vector4? background = null)
    {
        var pos = Resolve(x, y, width, height, anchor);
        float clamped = Math.Clamp(value, 0f, 1f);
        _ui.RoundedPanel(pos.X, pos.Y, width, height, background ?? new Vector4(0.02f, 0.025f, 0.034f, 0.80f), height * 0.5f);
        if (clamped > 0.001f)
            _ui.RoundedPanel(pos.X, pos.Y, MathF.Max(height, width * clamped), height, fill ?? RuntimeUI.Accent, height * 0.5f);
    }

    internal void DrawElement(RuntimeUIElementKind kind, string text, float x, float y, float width, float height,
                              float value, Vector4 color, Vector4 colorB, RuntimeUIAnchor anchor)
    {
        switch (kind)
        {
            case RuntimeUIElementKind.Label:
                Label(text, x, y, anchor, color);
                break;
            case RuntimeUIElementKind.Panel:
                Panel(x, y, width, height, anchor, color);
                break;
            case RuntimeUIElementKind.ProgressBar:
                ProgressBar(x, y, width, height, value, anchor, color, colorB);
                break;
        }
    }

    private Vector2 Resolve(float x, float y, float width, float height, RuntimeUIAnchor anchor)
    {
        return anchor switch
        {
            RuntimeUIAnchor.TopCenter => new Vector2(Width * 0.5f + x - width * 0.5f, y),
            RuntimeUIAnchor.TopRight => new Vector2(Width + x - width, y),
            RuntimeUIAnchor.CenterLeft => new Vector2(x, Height * 0.5f + y - height * 0.5f),
            RuntimeUIAnchor.Center => new Vector2(Width * 0.5f + x - width * 0.5f, Height * 0.5f + y - height * 0.5f),
            RuntimeUIAnchor.CenterRight => new Vector2(Width + x - width, Height * 0.5f + y - height * 0.5f),
            RuntimeUIAnchor.BottomLeft => new Vector2(x, Height + y - height),
            RuntimeUIAnchor.BottomCenter => new Vector2(Width * 0.5f + x - width * 0.5f, Height + y - height),
            RuntimeUIAnchor.BottomRight => new Vector2(Width + x - width, Height + y - height),
            _ => new Vector2(x, y)
        };
    }
}
