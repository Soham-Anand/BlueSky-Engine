using System;

namespace BlueSky.Rendering.Strata;

/// <summary>
/// Display-referred 16³ color LUT packed as a 256×16 2D strip
/// (tile = blue slice, x = red-major, y = green). Sampled AFTER the sRGB
/// encode, before dither. Ships near-identity with a gentle S-curve so the
/// pipeline is proven before any artistic grade lands.
/// </summary>
public static class StrataLut
{
    public const int Size = 16;
    public const int StripWidth = Size * Size; // 256
    public const int StripHeight = Size;       // 16

    /// <summary>Generates the strip as RGBA8 bytes. contrast: 0 = identity.</summary>
    public static byte[] Generate(float contrast = 0.08f, float saturation = 1.04f)
    {
        var out_ = new byte[StripWidth * StripHeight * 4];
        for (int b = 0; b < Size; b++)
        {
            for (int g = 0; g < Size; g++)
            {
                for (int r = 0; r < Size; r++)
                {
                    float fr = r / (float)(Size - 1);
                    float fg = g / (float)(Size - 1);
                    float fb = b / (float)(Size - 1);

                    // Gentle S-curve around mid grey + mild saturation lift.
                    float cr = SCurve(fr, contrast);
                    float cg = SCurve(fg, contrast);
                    float cb = SCurve(fb, contrast);
                    float luma = 0.299f * cr + 0.587f * cg + 0.114f * cb;
                    cr = luma + (cr - luma) * saturation;
                    cg = luma + (cg - luma) * saturation;
                    cb = luma + (cb - luma) * saturation;

                    int x = b * Size + r, y = g;
                    int i = (y * StripWidth + x) * 4;
                    out_[i] = ToByte(cr);
                    out_[i + 1] = ToByte(cg);
                    out_[i + 2] = ToByte(cb);
                    out_[i + 3] = 255;
                }
            }
        }
        return out_;
    }

    private static float SCurve(float x, float amount)
    {
        // Smoothstep-based contrast around 0.5; amount 0 = identity.
        float s = x * x * (3f - 2f * x);
        return Math.Clamp(x + (s - x) * amount * 4f, 0f, 1f);
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(v * 255f, 0f, 255f);

    /// <summary>CPU mirror of the in-shader strip lookup (for tests).</summary>
    public static float[] SampleStrip(byte[] strip, float r, float g, float b)
    {
        r = Math.Clamp(r, 0f, 1f); g = Math.Clamp(g, 0f, 1f); b = Math.Clamp(b, 0f, 1f);
        float bx = b * (Size - 1);
        int b0 = Math.Min((int)bx, Size - 2);
        float f = bx - b0;
        float[] Fetch(int bb, float fr, float fg)
        {
            int x = Math.Clamp((int)(fr * (Size - 1) + 0.5f), 0, Size - 1);
            int y = Math.Clamp((int)(fg * (Size - 1) + 0.5f), 0, Size - 1);
            int i = ((y * StripWidth) + (bb * Size + x)) * 4;
            return new[] { strip[i] / 255f, strip[i + 1] / 255f, strip[i + 2] / 255f };
        }
        var c0 = Fetch(b0, r, g);
        var c1 = Fetch(b0 + 1, r, g);
        return new[] {
            c0[0] + (c1[0] - c0[0]) * f,
            c0[1] + (c1[1] - c0[1]) * f,
            c0[2] + (c1[2] - c0[2]) * f,
        };
    }
}
