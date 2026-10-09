using System.Numerics;
using System.Runtime.InteropServices;

namespace BlueSky.Editor.UI;

// ──────────────────────────────────────────────────────────────────────────────
// UIVertex — consumed by EditorUIRenderer.
//
// Layout (stride = 40 bytes, Pack = 1 so the struct is exactly as declared):
//   offset  0 :  float2  Position   (screen-space pixels)
//   offset  8 :  float4  Color      (RGBA, linear)
//   offset 24 :  float2  UV         (normalised atlas coords; (0,0) for solid)
//   offset 32 :  float   Mode       (0 = solid colour, 1 = glyph alpha-texture)
//   offset 36 :  float   _pad       (keeps stride at 40, aligns to float)
// ──────────────────────────────────────────────────────────────────────────────
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct UIVertex
{
    public Vector2 Position;   // 8 bytes
    public Vector4 Color;      // 16 bytes
    public Vector2 UV;         // 8 bytes
    public float   Mode;       // 4 bytes
    private float  _pad;       // 4 bytes  ← padding, do not remove

    internal const int Stride = 40;

    /// <summary>Solid-colour geometry vertex (rect, line, circle).</summary>
    public UIVertex(Vector2 position, Vector4 color)
    {
        Position = position;
        Color    = color;
        UV       = Vector2.Zero;
        Mode     = 0f;
        _pad     = 0f;
    }

    /// <summary>Glyph vertex — samples the font atlas at <paramref name="uv"/>.</summary>
    public UIVertex(Vector2 position, Vector4 color, Vector2 uv)
    {
        Position = position;
        Color    = color;
        UV       = uv;
        Mode     = 1f;
        _pad     = 0f;
    }
}
