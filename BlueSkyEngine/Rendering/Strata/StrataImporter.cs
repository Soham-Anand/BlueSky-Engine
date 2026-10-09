using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Rendering.Strata;

/// <summary>
/// Tier-1 Strata importer: pure CPU functions that turn authoring-time
/// source data into a codec-ready <see cref="StrataMaterial"/>.
///
/// The importer's single most important job is colorspace discipline:
/// every color input is converted sRGB → linear EXACTLY ONCE, here.
/// Nothing downstream ever converts again.
/// </summary>
public static class StrataImporter
{
    // ── 1. sRGB → linear (exact piecewise transfer) ──────────────────────

    public static float LinearizeSrgb(float c)
    {
        c = Math.Clamp(c, 0f, 1f);
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    public static Vector3 LinearizeSrgb(Vector3 srgb)
        => new(LinearizeSrgb(srgb.X), LinearizeSrgb(srgb.Y), LinearizeSrgb(srgb.Z));

    /// <summary>Exact inverse: linear → sRGB display value (for pickers).</summary>
    public static float SrgbEncode(float c)
    {
        c = Math.Clamp(c, 0f, 1f);
        return c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
    }

    public static Vector3 SrgbEncode(Vector3 linear)
        => new(SrgbEncode(linear.X), SrgbEncode(linear.Y), SrgbEncode(linear.Z));

    /// <summary>Linearizes one RGBA8 buffer in place (RGB channels only).</summary>
    public static void LinearizeSrgbInPlace(byte[] rgba)
    {
        // 256-entry LUT keeps this O(n) with zero transcendental cost per pixel.
        Span<float> lut = stackalloc float[256];
        for (int i = 0; i < 256; i++)
            lut[i] = LinearizeSrgb(i / 255f);
        for (int i = 0; i + 3 < rgba.Length + 1; i += 4)
        {
            rgba[i] = (byte)Math.Clamp(lut[rgba[i]] * 255f, 0f, 255f);
            rgba[i + 1] = (byte)Math.Clamp(lut[rgba[i + 1]] * 255f, 0f, 255f);
            rgba[i + 2] = (byte)Math.Clamp(lut[rgba[i + 2]] * 255f, 0f, 255f);
        }
    }

    // ── 2. RMA packing (R=roughness, G=metallic, B=AO, A=unused) ──────────

    /// <summary>
    /// Packs per-pixel roughness/metallic/AO into one RGBA8 RMA texture.
    /// All inputs are linear data in [0,1]; arrays must match in length.
    /// </summary>
    public static byte[] PackRMA(float[] roughness, float[] metallic, float[] ao)
    {
        if (roughness.Length != metallic.Length || roughness.Length != ao.Length)
            throw new InvalidOperationException(
                "Strata import failed: roughness/metallic/AO arrays differ in length.");
        var out_ = new byte[roughness.Length * 4];
        for (int i = 0; i < roughness.Length; i++)
        {
            out_[i * 4] = ToByte(roughness[i]);
            out_[i * 4 + 1] = ToByte(metallic[i]);
            out_[i * 4 + 2] = ToByte(ao[i]);
            out_[i * 4 + 3] = 255;
        }
        return out_;
    }

    /// <summary>Constant-fill RMA for materials with scalar params (no maps).</summary>
    public static byte[] PackRMAUniform(int width, int height, float roughness, float metallic, float ao)
    {
        int n = width * height;
        var r = new float[n]; var m = new float[n]; var a = new float[n];
        Array.Fill(r, roughness); Array.Fill(m, metallic); Array.Fill(a, ao);
        return PackRMA(r, m, a);
    }

    // ── 3. Detail-map pairing ─────────────────────────────────────────────

    /// <summary>
    /// Validates an albedo/normal detail pair: same dimensions, power-of-two
    /// optional but flagged. Returns the tile rate the shader should use.
    /// Throws (loud) on dimension mismatch — a stretched detail map is a bug.
    /// </summary>
    public static float PairDetailMaps(int albedoW, int albedoH, int normalW, int normalH, float requestedTile)
    {
        if (albedoW != normalW || albedoH != normalH)
            throw new InvalidOperationException(
                $"Strata import failed: detail pair dimensions differ " +
                $"({albedoW}x{albedoH} vs {normalW}x{normalH}). Resize first.");
        if (requestedTile < 1f)
            throw new InvalidOperationException(
                "Strata import failed: detail tile rate must be >= 1.");
        return requestedTile;
    }

    // ── 4. Toksvig mip chain for normal maps ──────────────────────────────

    /// <summary>
    /// Builds a box-filtered mip chain for an RGBA8 normal map (XYZ in RGB,
    /// tangent-space, [0,255] encoding). Each level renormalizes; the average
    /// pre-normalize length is the Toksvig variance signal the shader uses
    /// to widen roughness with distance (anti-shimmer).
    /// Returns levels[0] = source copy, levels[k] = half res, + per-level
    /// mean normal length (1.0 = perfectly smooth at that scale).
    /// </summary>
    public static (List<byte[]> Levels, List<float> MeanLengths) BuildToksvigMips(
        byte[] topLevel, int width, int height)
    {
        if (topLevel.Length != width * height * 4)
            throw new InvalidOperationException(
                "Strata import failed: normal-map byte count does not match dimensions.");

        var levels = new List<byte[]> { (byte[])topLevel.Clone() };
        var lengths = new List<float> { MeanNormalLength(topLevel) };

        int w = width, h = height;
        byte[] current = levels[0];
        while (w > 1 || h > 1)
        {
            int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
            var next = new byte[nw * nh * 4];
            double lenAcc = 0;
            for (int y = 0; y < nh; y++)
            {
                for (int x = 0; x < nw; x++)
                {
                    float nx = 0, ny = 0, nz = 0;
                    for (int dy = 0; dy < 2; dy++)
                    {
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int sx = Math.Min(x * 2 + dx, w - 1);
                            int sy = Math.Min(y * 2 + dy, h - 1);
                            int si = (sy * w + sx) * 4;
                            nx += current[si] / 255f * 2f - 1f;
                            ny += current[si + 1] / 255f * 2f - 1f;
                            nz += current[si + 2] / 255f * 2f - 1f;
                        }
                    }
                    nx /= 4f; ny /= 4f; nz /= 4f;
                    float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                    lenAcc += len;
                    float inv = len > 1e-6f ? 1f / len : 0f;
                    int di = (y * nw + x) * 4;
                    next[di] = ToByte(nx * inv * 0.5f + 0.5f);
                    next[di + 1] = ToByte(ny * inv * 0.5f + 0.5f);
                    next[di + 2] = ToByte(nz * inv * 0.5f + 0.5f);
                    next[di + 3] = 255;
                }
            }
            levels.Add(next);
            lengths.Add((float)(lenAcc / (nw * nh)));
            current = next; w = nw; h = nh;
        }
        return (levels, lengths);
    }

    private static float MeanNormalLength(byte[] rgba)
    {
        double acc = 0;
        int n = rgba.Length / 4;
        for (int i = 0; i < n; i++)
        {
            float nx = rgba[i * 4] / 255f * 2f - 1f;
            float ny = rgba[i * 4 + 1] / 255f * 2f - 1f;
            float nz = rgba[i * 4 + 2] / 255f * 2f - 1f;
            acc += Math.Sqrt(nx * nx + ny * ny + nz * nz);
        }
        return n == 0 ? 0f : (float)(acc / n);
    }

    // ── 5. Material assembly ──────────────────────────────────────────────

    /// <summary>
    /// Assembles a codec-ready material from linearized params + texture blobs.
    /// Colors passed here MUST already be linear (use LinearizeSrgb first).
    /// Sets feature bits for every Tier-1 lobe with data present.
    /// clearcoat: 0 = off; >0 enables the Clearcoat bit (Tier-2 lacquer lobe).
    /// </summary>
    public static StrataMaterial Assemble(
        string name,
        Vector3 albedoLinear,
        float metallic,
        float roughness,
        float ao,
        Vector3 emissiveLinear,
        float emissiveIntensity,
        byte[]? albedoSrgb = null, int albedoW = 0, int albedoH = 0,
        byte[]? normalMap = null, int normalW = 0, int normalH = 0,
        byte[]? rmaMap = null, int rmaW = 0, int rmaH = 0,
        byte[]? detailAlbedo = null, byte[]? detailNormal = null,
        int detailW = 0, int detailH = 0, float detailTile = 8f,
        bool screenDoor = false,
        float clearcoat = 0f,
        float alpha = 1f,
        float iridescence = 0f,
        float sheen = 0f,
        float anisotropy = 0f,
        float skin = 0f,
        float hair = 0f,
        byte[]? bentMap = null, int bentW = 0, int bentH = 0,
        Vector3? clearcoatTintLinear = null)
    {
        var mat = new StrataMaterial
        {
            Name = name,
            AlbedoLinear = albedoLinear,
            Metallic = metallic,
            Roughness = roughness,
            AO = ao,
            ClearcoatTintLinear = Vector3.Clamp(
                clearcoatTintLinear ?? Vector3.One, Vector3.Zero, Vector3.One),
            EmissiveLinear = emissiveLinear,
            EmissiveIntensity = emissiveIntensity,
            Alpha = Math.Clamp(alpha, 0f, 1f),
            Features = StrataFeature.BakedAO | StrataFeature.WrappedDiffuse,
        };
        var payload = new List<byte>();

        void AddTexture(string slot, StrataColorspace cs, byte[] bytes, int w, int h)
        {
            mat.Textures.Add(new StrataTextureRef
            {
                Slot = slot,
                Colorspace = cs,
                Width = w,
                Height = h,
                Format = "RGBA8",
                PayloadOffset = (ulong)payload.Count,
                PayloadSize = (ulong)bytes.Length,
            });
            payload.AddRange(bytes);
        }

        if (albedoSrgb != null)
        {
            // Albedo textures stay sRGB-encoded in the payload; the Srgb flag
            // tells the GPU to HW-decode on sample (existing RHI RGBA8Srgb path).
            // Only scalar colors are linearized (see LinearizeSrgb).
            AddTexture("albedo", StrataColorspace.Srgb, albedoSrgb, albedoW, albedoH);
            mat.Features |= StrataFeature.AlbedoMap;
        }
        if (normalMap != null)
        {
            AddTexture("normal", StrataColorspace.Linear, normalMap, normalW, normalH);
            mat.Features |= StrataFeature.Toksvig;
        }
        if (rmaMap != null)
        {
            AddTexture("rma", StrataColorspace.Linear, rmaMap, rmaW, rmaH);
        }
        if (detailAlbedo != null && detailNormal != null)
        {
            PairDetailMaps(detailW, detailH, detailW, detailH, detailTile);
            AddTexture("detailAlbedo", StrataColorspace.Srgb, detailAlbedo, detailW, detailH);
            AddTexture("detailNormal", StrataColorspace.Linear, detailNormal, detailW, detailH);
            mat.Features |= StrataFeature.DetailNormal | StrataFeature.BumpOffset;
        }
        if (emissiveIntensity > 0f)
        {
            mat.Features |= StrataFeature.BakedAO; // emissive rides the base path
        }
        if (screenDoor)
        {
            mat.Features |= StrataFeature.ScreenDoor;
        }
        if (clearcoat > 0f)
        {
            // Tier-2 lacquer lobe, fixed full strength in-shader (v1).
            mat.Features |= StrataFeature.Clearcoat;
        }
        if (mat.Alpha < 0.999f)
        {
            // Transparent pass: alpha blend, no depth write, far-to-near.
            mat.Features |= StrataFeature.Transparent;
        }
        if (iridescence > 0f)
        {
            // Tier-2 thin-film lobe, fixed ramp in-shader (v1, clearcoat precedent).
            mat.Features |= StrataFeature.Iridescence;
        }
        if (sheen > 0f)
        {
            // Tier-2 cloth lobe, fixed fabric response in-shader (v1).
            mat.Features |= StrataFeature.Sheen;
        }
        if (anisotropy > 0f)
        {
            // Tier-2 brushed-metal lobe, fixed stretch in-shader (v1).
            mat.Features |= StrataFeature.Anisotropy;
        }
        if (skin > 0f)
        {
            // Tier-2 flesh lobe: fixed skin response driving the SSS terms (v1).
            mat.Features |= StrataFeature.Skin;
        }
        if (hair > 0f)
        {
            // Tier-2 hair lobe: fixed Kajiya-Kay response in-shader (v1).
            mat.Features |= StrataFeature.Hair;
        }
        if (bentMap != null)
        {
            // Bent-normal lobe: world-space RGB (Ease baker, "world"
            // encoding), linear. Loud on size mismatch — a half-present
            // bent map must never silently fall back.
            if (bentW <= 0 || bentH <= 0 || bentMap.Length != bentW * bentH * 4)
                throw new InvalidOperationException(
                    $"Strata import failed: bent map is {bentMap.Length} bytes, need {bentW}×{bentH}×4 RGBA8.");
            AddTexture("bent", StrataColorspace.Linear, bentMap, bentW, bentH);
            mat.Features |= StrataFeature.BentNormal;
        }

        mat.Payload = payload.ToArray();
        return mat;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(v * 255f, 0f, 255f);
}
