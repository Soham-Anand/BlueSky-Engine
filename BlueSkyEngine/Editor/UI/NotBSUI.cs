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
        public Vector4 ColorEnd;       // End color for gradients
        public string? Text;
        public float Radius;
        public float CornerRadius;     // Corner radius for rounded rects
        public bool GradientVertical;  // true = top-to-bottom, false = left-to-right
        public Vector4? ClipRect;      // Optional clip rect (x, y, width, height) for scissoring
    }

    public enum DrawCommandType
    {
        Rect,
        RectFilled,
        Circle,
        CircleFilled,
        Text,
        Line,
        GradientRectFilled,
        RoundedRectFilled,
        RoundedGradientRectFilled
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
    private float _scrollDelta;
    private Dictionary<string, float> _scrollOffsets = new();
    private Dictionary<string, float> _scrollTargets = new();  // Smooth scroll targets
    private Stack<Vector4> _clipRectStack = new();               // Clip rect stack
    private Vector4? _activeClipRect = null;                    // Current active clip rect
    
    // Scrollbar drag state
    private string? _draggingScrollbar = null;
    private float _scrollbarDragStartY;
    private float _scrollbarDragStartOffset;

    public bool IsMouseDown => _mouseDown;
    public Vector2 MousePosition => _mousePos;
    public double Time { get; set; }
    
    // Allows disabling input interactions for the UI (useful for modals)
    public bool InputEnabled { get; set; } = true;

    public NotBSUI(uint windowWidth, uint windowHeight)
    {
        _windowSize = new Vector2(windowWidth, windowHeight);
    }

    public void Resize(uint width, uint height)
    {
        _windowSize = new Vector2(width, height);
    }

    public void BeginFrame(Vector2 mousePos, bool mouseDown, string typedText = "", bool backspacePressed = false, float scrollDelta = 0f)
    {
        _drawCommands.Clear();
        _idCounter = 0;
        _hotItem = 0;
        
        _typedText = typedText ?? "";
        _backspacePressed = backspacePressed;
        _scrollDelta = scrollDelta;

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
    
    public Vector2 GetCursor() => _cursorPos;

    public float BeginScrollArea(string id, float x, float y, float width, float height, float contentHeight)
    {
        if (!_scrollOffsets.TryGetValue(id, out float scroll)) scroll = 0;
        if (!_scrollTargets.TryGetValue(id, out float scrollTarget)) scrollTarget = scroll;

        float maxScroll = Math.Max(0, contentHeight - height);

        // Handle scrollbar dragging
        if (_draggingScrollbar == id)
        {
            if (_mouseDown)
            {
                float trackHeight = height;
                float thumbHeight = Math.Max(20f, height * (height / Math.Max(1f, contentHeight)));
                float scrollRange = trackHeight - thumbHeight;
                if (scrollRange > 0)
                {
                    float deltaY = _mousePos.Y - _scrollbarDragStartY;
                    float newScroll = _scrollbarDragStartOffset + (deltaY / scrollRange) * maxScroll;
                    scrollTarget = Math.Clamp(newScroll, 0, maxScroll);
                }
            }
            else
            {
                _draggingScrollbar = null;
            }
        }

        // Mouse wheel scrolling
        if (IsHovering(x, y, width, height) && _draggingScrollbar == null)
        {
            if (Math.Abs(_scrollDelta) > 0.001f)
            {
                scrollTarget -= _scrollDelta * 60f;
                scrollTarget = Math.Clamp(scrollTarget, 0, maxScroll);
            }
        }
        _scrollTargets[id] = scrollTarget;

        // Smooth scroll interpolation (lerp towards target)
        float lerpSpeed = 12f; // Higher = snappier
        float dt = 1f / 60f;  // Approximate frame time
        scroll = scroll + (scrollTarget - scroll) * Math.Min(1f, lerpSpeed * dt);
        // Snap if very close
        if (Math.Abs(scroll - scrollTarget) < 0.5f) scroll = scrollTarget;
        scroll = Math.Clamp(scroll, 0, maxScroll);
        _scrollOffsets[id] = scroll;

        // Push clip rect for this scroll area
        var clipRect = new Vector4(x, y, width, height);
        _clipRectStack.Push(clipRect);
        _activeClipRect = clipRect;

        // Draw scrollbar
        if (maxScroll > 0)
        {
            float trackWidth = 8f;
            float trackX = x + width - trackWidth;
            
            // Scrollbar track background
            _drawCommands.Add(new DrawCommand
            {
                Type = DrawCommandType.RoundedRectFilled,
                Position = new Vector2(trackX, y),
                Size = new Vector2(trackWidth, height),
                Color = new Vector4(0.15f, 0.15f, 0.18f, 0.5f),
                CornerRadius = 4f
            });
            
            // Scrollbar thumb
            float thumbHeight = Math.Max(20f, height * (height / contentHeight));
            float scrollPct = maxScroll > 0 ? scroll / maxScroll : 0;
            float thumbY = y + scrollPct * (height - thumbHeight);
            
            bool isThumbHovered = IsHovering(trackX, thumbY, trackWidth, thumbHeight);
            bool isThumbActive = _draggingScrollbar == id;
            var thumbColor = isThumbActive ? new Vector4(0.6f, 0.6f, 0.7f, 0.95f) :
                             isThumbHovered ? new Vector4(0.5f, 0.5f, 0.6f, 0.9f) :
                             new Vector4(0.4f, 0.4f, 0.45f, 0.8f);
            
            _drawCommands.Add(new DrawCommand
            {
                Type = DrawCommandType.RoundedRectFilled,
                Position = new Vector2(trackX, thumbY),
                Size = new Vector2(trackWidth, thumbHeight),
                Color = thumbColor,
                CornerRadius = 4f
            });
            
            // Scrollbar drag interaction
            if (isThumbHovered && _mousePressed && _draggingScrollbar == null)
            {
                _draggingScrollbar = id;
                _scrollbarDragStartY = _mousePos.Y;
                _scrollbarDragStartOffset = scroll;
            }
            // Click on track to jump
            else if (IsHovering(trackX, y, trackWidth, height) && _mousePressed && !isThumbHovered && _draggingScrollbar == null)
            {
                float clickPct = (_mousePos.Y - y) / height;
                _scrollTargets[id] = Math.Clamp(clickPct * maxScroll, 0, maxScroll);
            }
        }

        // We shift the cursor up by scroll
        _cursorPos.Y -= scroll;
        return scroll;
    }

    public void EndScrollArea(string id)
    {
        // Pop clip rect
        if (_clipRectStack.Count > 0)
        {
            _clipRectStack.Pop();
            _activeClipRect = _clipRectStack.Count > 0 ? _clipRectStack.Peek() : null;
        }
        
        if (_scrollOffsets.TryGetValue(id, out float scroll))
        {
            _cursorPos.Y += scroll;
        }
    }

    public void Text(string text, Vector4 color)
    {
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.Text,
            Position = _cursorPos,
            Color = color,
            Text = text,
            ClipRect = _activeClipRect
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
            Type = DrawCommandType.RoundedRectFilled,
            Position = pos,
            Size = size,
            Color = new Vector4(0.051f, 0.055f, 0.071f, 1.0f), // EditorTheme.Bg0
            CornerRadius = 4f
        });

        // Draw filled portion
        float fillWidth = ((value - min) / (max - min)) * width;
        if (fillWidth > 2)
        {
            _drawCommands.Add(new DrawCommand
            {
                Type = DrawCommandType.RoundedRectFilled,
                Position = pos,
                Size = new Vector2(fillWidth, height),
                Color = isActive ? new Vector4(0.38f, 0.70f, 1.00f, 1.0f) : new Vector4(0.29f, 0.62f, 1.00f, 1.0f), // Accent
                CornerRadius = 4f
            });
        }
        
        // Value text
        string valText = value.ToString("0.00");
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.Text,
            Position = new Vector2(pos.X + width / 2 - valText.Length * 3.5f, pos.Y + height / 2 - 7),
            Text = valText,
            Color = new Vector4(1f, 1f, 1f, 0.8f) // TextPrimary
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
            Color = color,
            ClipRect = _activeClipRect
        });

        if (borderColor.HasValue)
        {
            _drawCommands.Add(new DrawCommand
            {
                Type = DrawCommandType.Rect,
                Position = new Vector2(x, y),
                Size = new Vector2(width, height),
                Color = borderColor.Value,
                ClipRect = _activeClipRect
            });
        }
    }

    /// <summary>Draw a filled rectangle with a linear gradient (top-to-bottom or left-to-right).</summary>
    public void GradientPanel(float x, float y, float width, float height,
                              Vector4 colorStart, Vector4 colorEnd,
                              bool vertical = true)
    {
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.GradientRectFilled,
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            Color = colorStart,
            ColorEnd = colorEnd,
            GradientVertical = vertical
        });
    }

    /// <summary>Draw a filled rectangle with rounded corners.</summary>
    public void RoundedPanel(float x, float y, float width, float height,
                             Vector4 color, float cornerRadius = 6f)
    {
        if (cornerRadius < 0.5f)
        {
            // Fall back to regular panel for tiny radii
            Panel(x, y, width, height, color);
            return;
        }
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RoundedRectFilled,
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            Color = color,
            CornerRadius = cornerRadius
        });
    }

    /// <summary>Draw a rounded rectangle with a linear gradient fill.</summary>
    public void RoundedGradientPanel(float x, float y, float width, float height,
                                     Vector4 colorStart, Vector4 colorEnd,
                                     float cornerRadius = 6f, bool vertical = true)
    {
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RoundedGradientRectFilled,
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            Color = colorStart,
            ColorEnd = colorEnd,
            CornerRadius = cornerRadius,
            GradientVertical = vertical
        });
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
        
        // Shadow removed to fix rendering bleeding artifact        
        // Draw button background (gradient for depth)
        Vector4 bgTop = new Vector4(
            Math.Min(1f, bgColor.X + 0.05f),
            Math.Min(1f, bgColor.Y + 0.05f),
            Math.Min(1f, bgColor.Z + 0.05f),
            bgColor.W);
        
        Vector4 bgBottom = new Vector4(
            Math.Max(0f, bgColor.X - 0.05f),
            Math.Max(0f, bgColor.Y - 0.05f),
            Math.Max(0f, bgColor.Z - 0.05f),
            bgColor.W);

        if (isActive) {
            // Invert gradient when pressed
            var temp = bgTop;
            bgTop = bgBottom;
            bgBottom = temp;
        }

        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RoundedGradientRectFilled,
            Position = pos,
            Size = size,
            Color = bgTop,
            ColorEnd = bgBottom,
            CornerRadius = 6f,
            GradientVertical = true,
            ClipRect = _activeClipRect
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
            Text = text,
            ClipRect = _activeClipRect
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
        
        // Shadow removed to fix rendering bleeding artifact
        
        // Card body (rounded)
        _drawCommands.Add(new DrawCommand
        {
            Type = DrawCommandType.RoundedRectFilled,
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            Color = color,
            CornerRadius = 6f,
            ClipRect = _activeClipRect
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
        if (!InputEnabled) return false;
        
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
