using System;

namespace BlueSky.BSR.Vectra
{
    /// <summary>
    /// Bakes a soft, cloud-shaped density field at startup. Low-frequency
    /// domain warping makes broad billows; mid-frequency value noise and
    /// cellular pockets break the circular silhouette without introducing
    /// pixel-scale detail. The perimeter is forced to zero for safe rotated
    /// linear sampling. Baked once; the shader uses one texture fetch.
    /// </summary>
    public static class VectraSprite
    {
        public const int Size = 128;

        public static byte[] Bake()
        {
            var out_ = new byte[Size * Size * 4];

            // Seven 3D cumulus lobe centers (x, y, radius, weight) arranged as a
            // natural cauliflower puff cluster strictly inside r < 0.84.
            Span<float> lx = stackalloc float[] {  0.00f, -0.11f,  0.12f, -0.06f,  0.08f, -0.13f,  0.03f };
            Span<float> ly = stackalloc float[] {  0.01f,  0.08f,  0.06f, -0.11f, -0.09f, -0.02f,  0.13f };
            Span<float> lr = stackalloc float[] {  0.29f,  0.23f,  0.22f,  0.22f,  0.21f,  0.19f,  0.19f };
            Span<float> lw = stackalloc float[] {  1.00f,  0.88f,  0.85f,  0.82f,  0.80f,  0.74f,  0.76f };

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size - 0.5f;
                    float v = (y + 0.5f) / Size - 0.5f;
                    float r0 = MathF.Sqrt(u * u + v * v) * 2f;

                    // Domain warp attenuated near the perimeter so lobes stay inside the circle.
                    float warpAtten = 1f - Smooth01((r0 - 0.38f) / 0.44f);
                    float wx = (Fbm(x * 0.036f + 11.3f, y * 0.036f + 4.7f) - 0.5f) * 0.16f * warpAtten;
                    float wy = (Fbm(x * 0.036f + 29.1f, y * 0.036f + 19.8f) - 0.5f) * 0.16f * warpAtten;
                    float wu = u + wx;
                    float wv = v + wy;

                    // Evaluate 3D spherical lobe cluster heightfield + crevice valleys between lobes.
                    float maxLobe = 0f;
                    float secondLobe = 0f;
                    for (int k = 0; k < 7; k++)
                    {
                        float dx = (wu - lx[k]) / lr[k];
                        float dy = (wv - ly[k]) / lr[k];
                        float d2 = dx * dx + dy * dy;
                        if (d2 < 1f)
                        {
                            float sphereH = MathF.Sqrt(1f - d2) * lw[k];
                            if (sphereH > maxLobe)
                            {
                                secondLobe = maxLobe;
                                maxLobe = sphereH;
                            }
                            else if (sphereH > secondLobe)
                            {
                                secondLobe = sphereH;
                            }
                        }
                    }

                    // Crevice factor: where two lobes meet (maxLobe ≈ secondLobe),
                    // carve a subtle dark cumulus crease to make individual billows pop.
                    float crease = maxLobe > 0.05f
                        ? Smooth01(Math.Clamp((maxLobe - secondLobe * 0.82f) / 0.28f, 0f, 1f))
                        : 0f;

                    // Pre-baked multi-octave Perlin-Worley billow detail (G) and
                    // orthogonal turbulent wisp field (B) — replaces 36 per-pixel
                    // shader hash evaluations with a single texture fetch!
                    float sx = (wu + 0.5f) * Size;
                    float sy = (wv + 0.5f) * Size;
                    float fbmBroad = Fbm(sx * 0.042f + 3.1f, sy * 0.042f + 7.7f);
                    float worley1  = CellularNoise(sx * 0.065f + 8.4f, sy * 0.065f + 13.8f);
                    float worley2  = CellularNoise(sx * 0.130f + 21.7f, sy * 0.130f + 5.9f);
                    float billow   = 1f - MathF.Abs(fbmBroad * 2f - 1f);

                    float detailG = Math.Clamp(fbmBroad * 0.42f + worley1 * 0.38f + worley2 * 0.20f, 0f, 1f);
                    float wispB   = Math.Clamp(
                        Fbm(sx * 0.055f + 43.2f, sy * 0.055f + 17.6f) * 0.55f + billow * 0.45f,
                        0f, 1f);

                    // Sculpted volumetric density (A) with crisp cauliflower lobes.
                    float rawSilhouette = maxLobe * (0.68f + 0.32f * crease);
                    float sculpted = rawSilhouette * (0.58f + 0.28f * detailG + 0.14f * billow);
                    float body = Smooth01((sculpted - 0.06f) / 0.62f);

                    // Strict C2 radial window: reaches exact zero by r0 = 0.86 on all 4 sides.
                    float radialEdge = 1f - Smooth01((r0 - 0.52f) / 0.34f);
                    body *= radialEdge * radialEdge;

                    // R channel: baked 3D cavity/crevice self-occlusion (0.42 in deep folds
                    // up to 1.0 on lobe crowns) for rich single-sample billow relief.
                    float cavityR = Math.Clamp(0.42f + 0.34f * crease + 0.24f * detailG, 0f, 1f);

                    int i = (y * Size + x) * 4;
                    out_[i]     = ToByte(cavityR);
                    out_[i + 1] = ToByte(detailG);
                    out_[i + 2] = ToByte(wispB);
                    out_[i + 3] = ToByte(body);
                }
            }
            // One light blur pass removes texel stair-stepping while keeping
            // the cauliflower creases crisp, followed by strict border zero-pinning.
            SoftBlur(out_, Size, Size);
            FeatherEdges(out_, Size, Size);
            return out_;
        }

        private static float Fbm(float x, float y)
        {
            float v = 0.54f * ValueNoise(x, y);
            float nx = x * 1.62f - y * 1.18f + 17.3f;
            float ny = x * 1.18f + y * 1.62f + 9.1f;
            v += 0.30f * ValueNoise(nx, ny);
            float nx2 = nx * 1.62f - ny * 1.18f + 31.7f;
            float ny2 = nx * 1.18f + ny * 1.62f + 23.4f;
            v += 0.16f * ValueNoise(nx2, ny2);
            return v;
        }

        private static float Smooth01(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        private static byte ToByte(float v) => (byte)Math.Clamp(v * 255f, 0f, 255f);

        private static float ValueNoise(float x, float y)
        {
            int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3f - 2f * xf);
            float v = yf * yf * (3f - 2f * yf);
            return Lerp(
                Lerp(Hash(xi, yi), Hash(xi + 1, yi), u),
                Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), u), v);
        }

        private static float Hash(int x, int y)
        {
            uint n = (uint)(x * 374761393 + y * 668265263);
            n = (n ^ (n >> 13)) * 1274126177u;
            n ^= n >> 16;
            return (n & 0xFFFFFF) / 16777215f;
        }

        private static float CellularNoise(float x, float y)
        {
            int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
            float nearest = 2f;
            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int cx = xi + ox, cy = yi + oy;
                    float fx = cx + Hash(cx, cy);
                    float fy = cy + Hash(cx + 37, cy - 19);
                    float dx = fx - x, dy = fy - y;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    if (distance < nearest) nearest = distance;
                }
            return 1f - Smooth01(nearest / 1.15f);
        }

        /// <summary>
        /// Builds packed directional occlusion maps from the cleaned sprite
        /// density. Map A stores +X, -X, +Y; map B stores -Y, +Z, -Z.
        /// The Z pair approximates depth thickness from local density because
        /// a 2D sprite has no actual interior volume.
        /// </summary>
        public static (byte[] Positive, byte[] Negative, int W, int H) BakeDirectionalLightMaps(
            byte[] rgba, int w, int h, int maxDimension)
        {
            if (rgba == null || w <= 0 || h <= 0 || rgba.Length < w * h * 4)
                throw new ArgumentException("Invalid sprite density image.", nameof(rgba));
            if (maxDimension <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDimension));
            int longest = Math.Max(w, h);
            int outW = Math.Max(1, w * Math.Min(longest, maxDimension) / longest);
            int outH = Math.Max(1, h * Math.Min(longest, maxDimension) / longest);
            var positive = new byte[outW * outH * 4];
            var negative = new byte[outW * outH * 4];
            float rayLength = Math.Min(w, h) * 0.24f;
            const int raySteps = 12;

            for (int y = 0; y < outH; y++)
            {
                int sy = Math.Clamp((int)((y + 0.5f) * h / outH), 0, h - 1);
                for (int x = 0; x < outW; x++)
                {
                    int sx = Math.Clamp((int)((x + 0.5f) * w / outW), 0, w - 1);
                    int o = (y * outW + x) * 4;
                    positive[o] = OcclusionAlong(rgba, w, h, sx, sy, 1, 0, rayLength, raySteps);
                    positive[o + 1] = OcclusionAlong(rgba, w, h, sx, sy, -1, 0, rayLength, raySteps);
                    positive[o + 2] = OcclusionAlong(rgba, w, h, sx, sy, 0, 1, rayLength, raySteps);
                    positive[o + 3] = 255;
                    negative[o] = OcclusionAlong(rgba, w, h, sx, sy, 0, -1, rayLength, raySteps);
                    byte throughDepth = ToByte(1f - MathF.Exp(-rgba[(sy * w + sx) * 4 + 3] / 255f * 1.6f));
                    negative[o + 1] = throughDepth;
                    negative[o + 2] = throughDepth;
                    negative[o + 3] = 255;
                }
            }
            return (positive, negative, outW, outH);
        }

        private static byte OcclusionAlong(byte[] rgba, int w, int h, int x, int y,
            int dx, int dy, float rayLength, int raySteps)
        {
            float opticalDepth = 0f;
            for (int step = 1; step <= raySteps; step++)
            {
                float distance = rayLength * step / raySteps;
                int sx = Math.Clamp((int)MathF.Round(x + dx * distance), 0, w - 1);
                int sy = Math.Clamp((int)MathF.Round(y + dy * distance), 0, h - 1);
                float density = rgba[(sy * w + sx) * 4 + 3] / 255f;
                opticalDepth += density * (1.15f / raySteps);
            }
            return ToByte(1f - MathF.Exp(-opticalDepth));
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>
        /// Loads a user smoke PNG: validates, rescues alpha-less (additive-style)
        /// images via luminance, box-downscales to MaxSize. Returns null LOUDLY
        /// (caller logs + falls back to Bake()) on any failure. Never throws.
        /// </summary>
        public const int MaxSize = 256;

        public static (byte[] Rgba, int W, int H)? TryLoadCustomPng(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                    return null;
                StbImageSharp.StbImage.stbi_set_flip_vertically_on_load(0);
                using var stream = System.IO.File.OpenRead(path);
                var image = StbImageSharp.ImageResult.FromStream(
                    stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
                if (image == null || image.Width <= 0 || image.Height <= 0 || image.Data == null)
                    return null;
                int w = image.Width, h = image.Height;
                byte[] rgba = image.Data;
                if (rgba.Length < w * h * 4)
                    return null;

                // Alpha rescue when the channel carries no information (constant
                // across the image): white-background PNGs (grey smoke on white)
                // get alpha = 1 - luminance, black-background (additive-style)
                // get alpha = luminance. RGB is whitened; tint comes from vertices.
                float minLum = 1f, maxLum = 0f, sumLum = 0f;
                bool alphaFlat = true;
                byte firstA = rgba[3];
                for (int i = 0; i < w * h; i++)
                {
                    if (rgba[i * 4 + 3] != firstA) { alphaFlat = false; break; }
                    float lum = (rgba[i * 4] * 0.299f + rgba[i * 4 + 1] * 0.587f + rgba[i * 4 + 2] * 0.114f) / 255f;
                    if (lum < minLum) minLum = lum;
                    if (lum > maxLum) maxLum = lum;
                    sumLum += lum;
                }
                if (alphaFlat)
                {
                    float meanLum = sumLum / (w * h);
                    if (meanLum > 0.5f)
                    {
                        float span = Math.Max(1e-3f, 1f - minLum);
                        for (int i = 0; i < w * h; i++)
                        {
                            float lum = (rgba[i * 4] * 0.299f + rgba[i * 4 + 1] * 0.587f + rgba[i * 4 + 2] * 0.114f) / 255f;
                            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 255;
                            rgba[i * 4 + 3] = (byte)Math.Clamp((1f - lum) / span * 255f, 0f, 255f);
                        }
                    }
                    else
                    {
                        float span = Math.Max(1e-3f, maxLum);
                        for (int i = 0; i < w * h; i++)
                        {
                            float lum = (rgba[i * 4] * 0.299f + rgba[i * 4 + 1] * 0.587f + rgba[i * 4 + 2] * 0.114f) / 255f;
                            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 255;
                            rgba[i * 4 + 3] = (byte)Math.Clamp(lum / span * 255f, 0f, 255f);
                        }
                    }

                    // Flattened previews often have a checkerboard matte baked
                    // into RGB. A straight luminance inversion turns its gray
                    // squares into low, but visible, alpha (a rectangular veil
                    // around the smoke). Estimate that matte floor from the
                    // outer border and remove it before filtering/downscaling.
                    SuppressRecoveredMatte(rgba, w, h);
                }

                if (w <= MaxSize && h <= MaxSize)
                {
                    SoftBlur(rgba, w, h);
                    FeatherEdges(rgba, w, h);
                    return (rgba, w, h);
                }
                int sw = Math.Max(1, w * MaxSize / Math.Max(w, h));
                int sh = Math.Max(1, h * MaxSize / Math.Max(w, h));
                byte[] small = BoxDownscale(rgba, w, h, sw, sh);
                SoftBlur(small, sw, sh);
                FeatherEdges(small, sw, sh);
                return (small, sw, sh);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Removes the residual alpha level present in the outer border of an
        /// opaque image whose alpha was reconstructed from luminance. The 90th
        /// percentile tolerates a few smoke pixels touching the edge while
        /// catching common checkerboard and compression mattes. Genuine alpha
        /// PNGs do not pass through this path.
        /// </summary>
        private static void SuppressRecoveredMatte(byte[] rgba, int w, int h)
        {
            if (w < 8 || h < 8)
                return;

            Span<int> histogram = stackalloc int[256];
            int margin = Math.Max(1, Math.Min(w, h) / 32);
            int borderPixels = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (y >= margin && y < h - margin &&
                        x >= margin && x < w - margin)
                        continue;
                    histogram[rgba[(y * w + x) * 4 + 3]]++;
                    borderPixels++;
                }
            }

            int target = (int)Math.Ceiling(borderPixels * 0.90);
            int matteFloor = 0;
            int cumulative = 0;
            for (; matteFloor < histogram.Length - 1; matteFloor++)
            {
                cumulative += histogram[matteFloor];
                if (cumulative >= target)
                    break;
            }
            if (matteFloor == 0 || matteFloor >= 254)
                return;

            int range = 255 - matteFloor;
            for (int i = 3; i < rgba.Length; i += 4)
            {
                int alpha = rgba[i];
                rgba[i] = alpha <= matteFloor
                    ? (byte)0
                    : (byte)((alpha - matteFloor) * 255 / range);
            }
        }

        /// <summary>
        /// Pins both the outer rectangular rim and circular corners (r >= 0.92)
        /// to zero alpha so rotated UV sampling can never catch square corner
        /// density or border texels (the hard-rectangle artifact).
        /// </summary>
        private static void FeatherEdges(byte[] rgba, int w, int h)
        {
            float rim = Math.Max(3f, Math.Min(w, h) * 0.08f);
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h - 0.5f;
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4 + 3;
                    if (x == 0 || x == w - 1 || y == 0 || y == h - 1)
                    {
                        rgba[i] = 0;
                        continue;
                    }
                    float u = (x + 0.5f) / w - 0.5f;
                    float r = MathF.Sqrt(u * u + v * v) * 2f;
                    float radialMask = 1f - Smooth01((r - 0.68f) / 0.24f);
                    float edge = Math.Min(Math.Min(x, w - 1 - x), Math.Min(y, h - 1 - y));
                    float boxMask = edge >= rim ? 1f : Smooth01(edge / rim);
                    rgba[i] = (byte)Math.Clamp(MathF.Round(rgba[i] * radialMask * boxMask), 0f, 255f);
                }
            }
        }

        /// <summary>
        /// One 3x3 blur pass over RGBA. Melts compression blocks and straight
        /// seams hiding inside downloaded PNGs so rotated sampling can't expose
        /// them as hard quads. One-time import cost, zero runtime cost.
        /// </summary>
        private static void SoftBlur(byte[] rgba, int w, int h)
        {
            var copy = (byte[])rgba.Clone();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    long r = 0, g = 0, b = 0, a = 0;
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int sx = Math.Clamp(x + ox, 0, w - 1);
                            int sy = Math.Clamp(y + oy, 0, h - 1);
                            int i = (sy * w + sx) * 4;
                            r += copy[i]; g += copy[i + 1]; b += copy[i + 2]; a += copy[i + 3];
                        }
                    }
                    int o = (y * w + x) * 4;
                    rgba[o] = (byte)(r / 9);
                    rgba[o + 1] = (byte)(g / 9);
                    rgba[o + 2] = (byte)(b / 9);
                    rgba[o + 3] = (byte)(a / 9);
                }
            }
        }

        private static byte[] BoxDownscale(byte[] src, int w, int h, int sw, int sh)
        {
            var dst = new byte[sw * sh * 4];
            float xScale = (float)w / sw, yScale = (float)h / sh;
            for (int y = 0; y < sh; y++)
            {
                for (int x = 0; x < sw; x++)
                {
                    int x0 = (int)(x * xScale), x1 = Math.Min(w, (int)((x + 1) * xScale));
                    int y0 = (int)(y * yScale), y1 = Math.Min(h, (int)((y + 1) * yScale));
                    long r = 0, g = 0, b = 0, a = 0;
                    int n = 0;
                    for (int sy = y0; sy < y1; sy++)
                        for (int sx = x0; sx < x1; sx++)
                        {
                            int i = (sy * w + sx) * 4;
                            r += src[i]; g += src[i + 1]; b += src[i + 2]; a += src[i + 3];
                            n++;
                        }
                    int o = (y * sw + x) * 4;
                    dst[o] = (byte)(r / Math.Max(1, n));
                    dst[o + 1] = (byte)(g / Math.Max(1, n));
                    dst[o + 2] = (byte)(b / Math.Max(1, n));
                    dst[o + 3] = (byte)(a / Math.Max(1, n));
                }
            }
            return dst;
        }
    }
}
