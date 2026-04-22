using System;
using System.Numerics;
using System.Collections.Generic;

namespace NotBSRenderer;

/// <summary>
/// NotBSUI - A custom immediate-mode UI system built from scratch.
/// </summary>
public class NotBSUI
{
    public struct DrawCommand
    {
        public DrawCommandType Type;
        public Vector2 Position;
        public Vector2 Size;
        public Vector4 Color;
        public string? Text;
        public float Radius;
    }

    public enum DrawCommandType
    {
        Rect,
        RectFilled,
        Circle,
        CircleFilled,
        Text,
        Line
    }

    private readonly List<DrawCommand> _drawCommands = new();
    private Vector2 _cursorPos;
    private Vector2 _windowSize;
    private uint _idCounter;

    // UI State
    private uint _hotItem;
    private uint _activeItem;
    private uint _focusedItem;
    private Vector2 _mousePos;
    private bool _mouseDown;
    private bool _mousePressed;
    private bool _mouseReleased;
    private string _typedText = "";
    private bool _backspacePressed;

    public bool IsMouseDown => _mouseDown;
    public Vector2 MousePosition => _mousePos;
    public double Time { get; set; }

    public NotBSUI(uint windowWidth, uint windowHeight)
    {
        _windowSize = new Vector2(windowWidth, windowHeight);
    }

    public void Resize(uint width, uint height)
    {
        _windowSize = new Vector2(width, height);
    }

    public void BeginFrame(Vector2 mousePos, bool mouseDown, string typedText = "", bool backspacePressed = false)
    {
        _drawCommands.Clear();
        _idCounter = 0;
        _hotItem = 0;
        
        _typedText = typedText ?? "";
        _backspacePressed = backspacePressed;

        _mousePressed = mouseDown && !_mouseDown;
        _mouseReleased = !mouseDown && _mouseDown;
        _mousePos = mousePos;
        _mouseDown = mouseDown;

        if (!_mouseDown && !_mouseReleased)
        {
            _activeItem = 0;
        }
        else if (_mouseDown && _activeItem == 0)
        {
            _activeItem = uint.MaxValue;
        }
    }

    public void EndFrame()
    {
        // Render all draw commands via the renderer.
    }

    public void SetCursor(float x, float y)
    {
        _cursorPos = new Vector2(x, y);
    }

    public void Text(string text, Vector4 color)
    {
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.Text,
            Position = _cursorPos,
            Color = color,
            Text = text
        });

        _cursorPos.Y += 20; // Line height
    }

    public bool Button(string text, float width, float height)
    {
        return Button(text, width, height, new Vector4(0.2f, 0.2f, 0.22f, 1.0f), new Vector4(0.3f, 0.4f, 0.5f, 1.0f));
    }

    public bool Button(string text, float width, float height, Vector4 normalColor, Vector4 hoverColor)
    {
        uint id = ++_idCounter;
        Vector2 pos = _cursorPos;
        Vector2 size = new Vector2(width, height);

        bool isHot = IsMouseOver(pos, size);
        if (isHot)
        {
            _hotItem = id;
            if (_mousePressed)
                _activeItem = id;
        }

        bool isActive = _activeItem == id;
        Vector4 color = normalColor;
        
        if (isActive)
            color = new Vector4(normalColor.X - 0.1f, normalColor.Y - 0.1f, normalColor.Z - 0.1f, 1.0f);
        else if (isHot)
            color = hoverColor;

        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RectFilled,
            Position = pos,
            Size = size,
            Color = color
        });

        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.Rect,
            Position = pos,
            Size = size,
            Color = new Vector4(0.1f, 0.1f, 0.1f, 1.0f)
        });

        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.Text,
            Position = pos + new Vector2(10, height / 2 - 8),
            Color = new Vector4(0.9f, 0.9f, 0.95f, 1.0f),
            Text = text
        });

        _cursorPos.Y += height + 5;

        return isActive && isHot && _mouseReleased;
    }

    public bool Slider(ref float value, float min = 0f, float max = 1f, float width = 200, float height = 20)
    {
        uint id = ++_idCounter;
        Vector2 pos = _cursorPos;
        Vector2 size = new Vector2(width, height);

        bool isHot = IsMouseOver(pos, size);
        bool isActive = _activeItem == id;

        if (isHot && _mousePressed)
        {
            _activeItem = id;
            isActive = true;
        }
        else if (!_mouseDown)
        {
            if (_activeItem == id)
                _activeItem = 0;
            isActive = false;
        }

        // Update value if dragging
        if (isActive)
        {
            float relativeX = _mousePos.X - pos.X;
            relativeX = Math.Clamp(relativeX, 0, width);
            value = min + (relativeX / width) * (max - min);
        }

        // Draw slider track
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RectFilled,
            Position = pos,
            Size = size,
            Color = new Vector4(0.15f, 0.15f, 0.16f, 1.0f)
        });

        // Draw filled portion
        float fillWidth = ((value - min) / (max - min)) * width;
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RectFilled,
            Position = pos,
            Size = new Vector2(fillWidth, height),
            Color = new Vector4(0.3f, 0.5f, 0.7f, 1.0f)
        });

        // Draw handle
        float handleX = pos.X + fillWidth - 4;
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RectFilled,
            Position = new Vector2(handleX, pos.Y),
            Size = new Vector2(8, height),
            Color = isActive ? new Vector4(0.6f, 0.8f, 1.0f, 1.0f) : new Vector4(0.4f, 0.6f, 0.8f, 1.0f)
        });

        // Draw border
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.Rect,
            Position = pos,
            Size = size,
            Color = new Vector4(0.3f, 0.3f, 0.35f, 1.0f)
        });

        _cursorPos.Y += height + 10;

        return isActive;
    }

    public bool TextField(ref string text, float width = 300, float height = 30)
    {
        uint id = ++_idCounter;
        Vector2 pos = _cursorPos;
        Vector2 size = new Vector2(width, height);

        bool isHot = IsMouseOver(pos, size);
        bool isFocused = _focusedItem == id;

        if (isHot && _mousePressed)
        {
            _focusedItem = id;
            isFocused = true;
        }
        else if (!isHot && _mousePressed && _focusedItem == id)
        {
            _focusedItem = 0; // Lost focus
            isFocused = false;
        }

        if (isFocused)
        {
            if (_backspacePressed && text.Length > 0)
            {
                text = text.Substring(0, text.Length - 1);
            }
            if (!string.IsNullOrEmpty(_typedText))
            {
                text += _typedText;
            }
        }

        Vector4 color = isFocused ? new Vector4(0.25f, 0.4f, 0.6f, 1.0f) : new Vector4(0.15f, 0.15f, 0.16f, 1.0f);

        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RectFilled,
            Position = pos,
            Size = size,
            Color = color
        });

        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.Rect,
            Position = pos,
            Size = size,
            Color = new Vector4(0.3f, 0.3f, 0.35f, 1.0f)
        });

        // Calculate text clipping or simple render (we have no clipping, so just draw text)
        string displayText = text;
        if (isFocused && (DateTime.UtcNow.Millisecond % 1000) < 500)
        {
            displayText += "|"; // Blinking cursor
        }

        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.Text,
            Position = pos + new Vector2(10, height / 2 - 8),
            Color = new Vector4(0.9f, 0.9f, 0.95f, 1.0f),
            Text = displayText
        });

        _cursorPos.Y += height + 10;

        return isFocused;
    }

    public void Panel(float x, float y, float width, float height, Vector4 color, Vector4? borderColor = null)
    {
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RectFilled,
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            Color = color
        });

        if (borderColor.HasValue)
        {
            _drawCommands.Add(new DrawCommand
            {
                Type = DrawCommandType.Rect,
                Position = new Vector2(x, y),
                Size = new Vector2(width, height),
                Color = borderColor.Value
            });
        }
    }

    // Shadow effect: draws a dark offset rectangle behind
    public void Shadow(float x, float y, float width, float height, float offsetX = 2, float offsetY = 2, float alpha = 0.5f)
    {
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RectFilled,
            Position = new Vector2(x + offsetX, y + offsetY),
            Size = new Vector2(width, height),
            Color = new Vector4(0, 0, 0, alpha)
        });
    }

    // Interactive button with shadow, hover, and click states
    public bool ButtonEx(float x, float y, float width, float height, string text, 
                         Vector4 normalColor, Vector4 hoverColor, Vector4 pressedColor,
                         Vector4 shadowColor, Vector4 textColor,
                         uint id = 0)
    {
        if (id == 0) id = ++_idCounter;
        
        Vector2 pos = new Vector2(x, y);
        Vector2 size = new Vector2(width, height);
        
        bool isHot = IsMouseOver(pos, size);
        if (isHot) _hotItem = id;
        
        bool isActive = _activeItem == id;
        bool wasPressed = isActive && isHot && _mouseReleased;
        
        if (isHot && _mousePressed)
            _activeItem = id;
        
        // Determine color based on state
        Vector4 bgColor = normalColor;
        float shadowAlpha = shadowColor.W;
        
        if (isActive && isHot)
        {
            bgColor = pressedColor;
            shadowAlpha *= 0.3f; // Dim shadow when pressed (button looks "pushed in")
            pos.Y += 1; // Visual offset when pressed
        }
        else if (isHot)
        {
            bgColor = hoverColor;
        }
        
        // Draw shadow first (behind)
        if (shadowAlpha > 0.01f)
        {
            _drawCommands.Add(new DrawCommand
            {
                Type = DrawCommandType.RectFilled,
                Position = new Vector2(x + 2, y + 2),
                Size = size,
                Color = new Vector4(shadowColor.X, shadowColor.Y, shadowColor.Z, shadowAlpha)
            });
        }
        
        // Draw button background
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RectFilled,
            Position = pos,
            Size = size,
            Color = bgColor
        });
        
        // Draw subtle top highlight line
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RectFilled,
            Position = new Vector2(pos.X, pos.Y),
            Size = new Vector2(size.X, 1),
            Color = new Vector4(1, 1, 1, isActive ? 0.05f : 0.15f)
        });
        
        // Draw subtle bottom shadow line (inset effect)
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RectFilled,
            Position = new Vector2(pos.X, pos.Y + size.Y - 1),
            Size = new Vector2(size.X, 1),
            Color = new Vector4(0, 0, 0, isActive ? 0.3f : 0.15f)
        });
        
        // Draw text centered
        float textWidth = text.Length * 9;
        float textX = pos.X + (size.X - textWidth) / 2;
        float textY = pos.Y + (size.Y - 12) / 2;
        
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.Text,
            Position = new Vector2(textX, textY),
            Color = textColor,
            Text = text
        });
        
        return wasPressed;
    }

    // Check if mouse is hovering over a region
    public bool IsHovering(float x, float y, float width, float height)
    {
        return IsMouseOver(new Vector2(x, y), new Vector2(width, height));
    }

    // Draw a clickable card with all states
    public bool ClickableCard(float x, float y, float width, float height, uint id,
                              Vector4 normalColor, Vector4 hoverColor, Vector4 pressedColor)
    {
        bool isHot = IsMouseOver(new Vector2(x, y), new Vector2(width, height));
        if (isHot) _hotItem = id;
        
        bool isActive = _activeItem == id;
        bool clicked = isActive && isHot && _mouseReleased;
        
        if (isHot && _mousePressed)
            _activeItem = id;
        
        Vector4 color = normalColor;
        if (isActive && isHot)
            color = pressedColor;
        else if (isHot)
            color = hoverColor;
        
        // Shadow
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RectFilled,
            Position = new Vector2(x + 1, y + 2),
            Size = new Vector2(width, height),
            Color = new Vector4(0, 0, 0, 0.3f)
        });
        
        // Card
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RectFilled,
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            Color = color
        });
        
        // Top highlight
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RectFilled,
            Position = new Vector2(x, y),
            Size = new Vector2(width, 1),
            Color = new Vector4(1, 1, 1, isActive ? 0.05f : 0.1f)
        });
        
        return clicked;
    }

    public void Circle(float x, float y, float radius, Vector4 color, bool filled = false)
    {
        _drawCommands.Add(new DrawCommand
        {
            Type = filled ? DrawCommandType.CircleFilled : DrawCommandType.Circle,
            Position = new Vector2(x, y),
            Radius = radius,
            Color = color
        });
    }

    public void Line(float x1, float y1, float x2, float y2, Vector4 color)
    {
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.Line,
            Position = new Vector2(x1, y1),
            Size = new Vector2(x2, y2),
            Color = color
        });
    }

    private bool IsMouseOver(Vector2 pos, Vector2 size)
    {
        return _mousePos.X >= pos.X && _mousePos.X <= pos.X + size.X &&
               _mousePos.Y >= pos.Y && _mousePos.Y <= pos.Y + size.Y;
    }

    public IReadOnlyList<DrawCommand> GetDrawCommands() => _drawCommands;

    // Simple software rasterizer for UI (renders to a color buffer)
    public void RasterizeToBuffer(Span<byte> buffer, int width, int height, int stride)
    {
        buffer.Fill(0);

        foreach (var cmd in _drawCommands)
        {
            switch (cmd.Type)
            {
                case DrawCommandType.RectFilled:
                    DrawFilledRect(buffer, width, height, stride, cmd);
                    break;
                case DrawCommandType.Rect:
                    DrawRect(buffer, width, height, stride, cmd);
                    break;
                case DrawCommandType.CircleFilled:
                    DrawFilledCircle(buffer, width, height, stride, cmd);
                    break;
                case DrawCommandType.Text:
                    DrawText(buffer, width, height, stride, cmd);
                    break;
            }
        }
    }

    private void DrawFilledRect(Span<byte> buffer, int width, int height, int stride, DrawCommand cmd)
    {
        int x1 = Math.Clamp((int)cmd.Position.X, 0, width);
        int y1 = Math.Clamp((int)cmd.Position.Y, 0, height);
        int x2 = Math.Clamp((int)(cmd.Position.X + cmd.Size.X), 0, width);
        int y2 = Math.Clamp((int)(cmd.Position.Y + cmd.Size.Y), 0, height);

        byte r = (byte)(cmd.Color.X * 255);
        byte g = (byte)(cmd.Color.Y * 255);
        byte b = (byte)(cmd.Color.Z * 255);
        byte a = (byte)(cmd.Color.W * 255);

        for (int y = y1; y < y2; y++)
        {
            for (int x = x1; x < x2; x++)
            {
                int offset = y * stride + x * 4;
                buffer[offset + 0] = b;
                buffer[offset + 1] = g;
                buffer[offset + 2] = r;
                buffer[offset + 3] = a;
            }
        }
    }

    private void DrawRect(Span<byte> buffer, int width, int height, int stride, DrawCommand cmd)
    {
        int x1 = Math.Clamp((int)cmd.Position.X, 0, width);
        int y1 = Math.Clamp((int)cmd.Position.Y, 0, height);
        int x2 = Math.Clamp((int)(cmd.Position.X + cmd.Size.X), 0, width);
        int y2 = Math.Clamp((int)(cmd.Position.Y + cmd.Size.Y), 0, height);

        byte r = (byte)(cmd.Color.X * 255);
        byte g = (byte)(cmd.Color.Y * 255);
        byte b = (byte)(cmd.Color.Z * 255);
        byte a = (byte)(cmd.Color.W * 255);

        for (int x = x1; x < x2; x++)
        {
            SetPixel(buffer, width, height, stride, x, y1, r, g, b, a);
            SetPixel(buffer, width, height, stride, x, y2 - 1, r, g, b, a);
        }

        for (int y = y1; y < y2; y++)
        {
            SetPixel(buffer, width, height, stride, x1, y, r, g, b, a);
            SetPixel(buffer, width, height, stride, x2 - 1, y, r, g, b, a);
        }
    }

    private void DrawFilledCircle(Span<byte> buffer, int width, int height, int stride, DrawCommand cmd)
    {
        int cx = (int)cmd.Position.X;
        int cy = (int)cmd.Position.Y;
        int radius = (int)cmd.Radius;

        byte r = (byte)(cmd.Color.X * 255);
        byte g = (byte)(cmd.Color.Y * 255);
        byte b = (byte)(cmd.Color.Z * 255);
        byte a = (byte)(cmd.Color.W * 255);

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + y * y <= radius * radius)
                {
                    SetPixel(buffer, width, height, stride, cx + x, cy + y, r, g, b, a);
                }
            }
        }
    }

    private void DrawText(Span<byte> buffer, int width, int height, int stride, DrawCommand cmd)
    {
        if (string.IsNullOrEmpty(cmd.Text)) return;

        int x = (int)cmd.Position.X;
        int y = (int)cmd.Position.Y;

        byte r = (byte)(cmd.Color.X * 255);
        byte g = (byte)(cmd.Color.Y * 255);
        byte b = (byte)(cmd.Color.Z * 255);
        byte a = (byte)(cmd.Color.W * 255);

        foreach (char _ in cmd.Text)
        {
            for (int dy = 0; dy < 12; dy++)
            {
                for (int dx = 0; dx < 8; dx++)
                {
                    SetPixel(buffer, width, height, stride, x + dx, y + dy, r, g, b, a);
                }
            }
            x += 9;
        }
    }

    private void SetPixel(Span<byte> buffer, int width, int height, int stride, int x, int y, byte r, byte g, byte b, byte a)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return;
        int offset = y * stride + x * 4;
        buffer[offset + 0] = b;
        buffer[offset + 1] = g;
        buffer[offset + 2] = r;
        buffer[offset + 3] = a;
    }
}
