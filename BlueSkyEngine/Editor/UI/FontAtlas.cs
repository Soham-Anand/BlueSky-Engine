using System;
using System.IO;
using System.Numerics;
using NotBSRenderer;
using StbTrueTypeSharp;

namespace BlueSky.Editor.UI;

/// <summary>
/// Bakes a TTF font into an R8 GPU texture atlas so NotBSUIRenderer can
/// emit properly-shaped glyph quads instead of coloured placeholder blocks.
///
/// Covers ASCII printable range (0x20–0x7E, i.e. space through tilde).
/// </summary>
public sealed class FontAtlas : IDisposable
{
    // ── atlas constants ────────────────────────────────────────────────────
    public const int   AtlasWidth      = 512;
    public const int   AtlasHeight     = 512;
    public const int   FirstChar       = 32;   // ' '  (space)
    public const int   NumChars        = 95;   // ' ' through '~'
    public const float FontSizePixels  = 16f;

    // ── state ─────────────────────────────────────────────────────────────
    private readonly StbTrueType.stbtt_bakedchar[] _charData;
    private          IRHITexture?                  _atlasTexture;
    private          bool                          _disposed;

    /// <summary>The GPU texture that holds the baked glyph atlas (R8Unorm).</summary>
    public IRHITexture AtlasTexture =>
        _atlasTexture ?? throw new InvalidOperationException("FontAtlas has been disposed.");

    /// <summary>Height of one line of text, in pixels.</summary>
    public float LineHeight => FontSizePixels;

    // ── construction ───────────────────────────────────────────────────────
    public FontAtlas(IRHIDevice device, string ttfPath)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!File.Exists(ttfPath))
            throw new FileNotFoundException($"Font file not found: {ttfPath}");

        byte[] fontBytes = File.ReadAllBytes(ttfPath);
        byte[] pixels    = new byte[AtlasWidth * AtlasHeight]; // R8, 1 byte/pixel
        _charData        = new StbTrueType.stbtt_bakedchar[NumChars];

        var bakeOk = StbTrueType.stbtt_BakeFontBitmap(
            fontBytes, 0,
            FontSizePixels,
            pixels, AtlasWidth, AtlasHeight,
            FirstChar, NumChars,
            _charData);

        if (!bakeOk)
            throw new Exception(
                $"Font atlas bake failed — atlas too small or font data invalid. " +
                $"Increase AtlasWidth/AtlasHeight or reduce NumChars/FontSizePixels.");

        // ── upload to GPU as an R8Unorm sampled texture ────────────────────
        _atlasTexture = device.CreateTexture(new TextureDesc
        {
            Width       = AtlasWidth,
            Height      = AtlasHeight,
            Depth       = 1,
            MipLevels   = 1,
            ArrayLayers = 1,
            Format      = TextureFormat.R8Unorm,
            Usage       = TextureUsage.Sampled,
            DebugName   = "FontAtlas",
        });

        device.UploadTexture(_atlasTexture, pixels);

        Console.WriteLine(
            $"[FontAtlas] Baked {NumChars} glyphs at {FontSizePixels}px " +
            $"into {AtlasWidth}x{AtlasHeight} R8 texture.");
    }

    // ── public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fills <paramref name="p0"/>/<paramref name="p1"/> with screen-space corners
    /// and <paramref name="uv0"/>/<paramref name="uv1"/> with normalised atlas UVs for
    /// one character.  <paramref name="xPos"/> is advanced by the glyph's advance width.
    /// <paramref name="yBaseline"/> is the Y of the text baseline in screen-space.
    /// Returns <c>false</c> if the character is outside the baked range.
    /// </summary>
    public unsafe bool TryGetGlyphQuad(
        char    c,
        ref float xPos,
        float   yBaseline,
        out Vector2 p0,  out Vector2 p1,
        out Vector2 uv0, out Vector2 uv1)
    {
        p0 = p1 = uv0 = uv1 = default;

        int idx = c - FirstChar;
        if (idx < 0 || idx >= NumChars)
            return false;

        // stbtt_GetBakedQuad advances xPos and computes the pixel quad.
        // opengl_fillfirst = true → aligns to integer pixels (sharper glyphs).
        // yBaseline is the ink baseline; q.y0/q.y1 will be above/below it.
        float localX = xPos;
        float localY = yBaseline;
        StbTrueType.stbtt_aligned_quad q = default;

        fixed (StbTrueType.stbtt_bakedchar* pCharData = _charData)
        {
            StbTrueType.stbtt_GetBakedQuad(
                pCharData,
                AtlasWidth, AtlasHeight,
                idx,
                &localX, &localY,
                &q,
                1);
        }

        xPos = localX;

        p0  = new Vector2(q.x0, q.y0);
        p1  = new Vector2(q.x1, q.y1);
        uv0 = new Vector2(q.s0, q.t0);
        uv1 = new Vector2(q.s1, q.t1);
        return true;
    }

    /// <summary>
    /// Measures the pixel width of <paramref name="text"/> without rendering it.
    /// Useful for centering or right-aligning strings.
    /// </summary>
    public unsafe float MeasureWidth(ReadOnlySpan<char> text)
    {
        float x = 0f, y = 0f;
        
        fixed (StbTrueType.stbtt_bakedchar* pCharData = _charData)
        {
            foreach (char c in text)
            {
                int idx = c - FirstChar;
                if (idx < 0 || idx >= NumChars) continue;

                StbTrueType.stbtt_aligned_quad q = default;
                StbTrueType.stbtt_GetBakedQuad(
                    pCharData,
                    AtlasWidth, AtlasHeight,
                    idx,
                    &x, &y,
                    &q,
                    1);
            }
        }
        return x;
    }

    // ── IDisposable ────────────────────────────────────────────────────────
    public void Dispose()
    {
        if (_disposed) return;
        _atlasTexture?.Dispose();
        _atlasTexture = null;
        _disposed     = true;
    }
}
