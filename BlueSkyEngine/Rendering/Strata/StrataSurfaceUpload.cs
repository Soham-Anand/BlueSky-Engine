using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Rendering.Strata;

/// <summary>
/// Bridges StrataMaterial → renderer surface constants + GPU textures.
///
/// Mapping (must match the AstraSurface 112-byte layout + shader reads):
///   BaseColor   = AlbedoLinear (alpha 1)
///   Alpha       = draw opacity → AstraSurface.Alpha → fs_mesh output alpha
///   Roughness / Metallic / AO = scalar params (texture overrides in shader)
///   Custom0     = (1, wrap, detailTile, toksvigK)
///   Custom1.x   = feature mask bits (uint-as-float, exact to 2^24)
///   Custom1.yzw = 0
/// Everything else mirrors the legacy defaults (orange-clay path unchanged).
/// </summary>
public static class StrataSurfaceUpload
{
    public const float DefaultWrap = 0.5f;
    public const float DefaultDetailTile = 8f;
    public const float DefaultToksvigK = 0.5f;

    public struct SurfaceParams
    {
        public Vector4 BaseColor;
        public float Roughness;
        public float Metallic;
        public float AO;
        public float Wrap;
        public float DetailTile;
        public float ToksvigK;
        public uint FeatureMask;
        public float Alpha;
        public Vector3 ClearcoatTintLinear;
    }

    public static SurfaceParams BuildParams(StrataMaterial mat)
    {
        var p = new SurfaceParams
        {
            BaseColor = new Vector4(mat.AlbedoLinear, 1f),
            Roughness = mat.Roughness,
            Metallic = mat.Metallic,
            AO = mat.AO,
            Wrap = 0f,
            DetailTile = DefaultDetailTile,
            ToksvigK = DefaultToksvigK,
            FeatureMask = (uint)mat.Features,
            Alpha = Math.Clamp(mat.Alpha, 0f, 1f),
            ClearcoatTintLinear = mat.ClearcoatTintLinear,
        };
        if (mat.Features.HasFlag(StrataFeature.WrappedDiffuse))
            p.Wrap = DefaultWrap;
        return p;
    }

    /// <summary>
    /// Demo vehicle material: deep red dielectric + full Tier-1 set.
    /// Built through the REAL importer (Assemble) so the proof exercises
    /// the same code path future .stratamat files will use.
    /// Source textures are generated procedurally (no disk dependency).
    /// </summary>
    public static StrataMaterial BuildDemoVehicleMaterial()
    {
        const int W = 128, H = 128;
        var rma = new byte[W * H * 4];
        var flatNormal = new byte[W * H * 4];
        var detailA = new byte[W * H * 4];
        var detailN = new byte[W * H * 4];
        var rng = new Random(20260923);
        for (int i = 0; i < W * H; i++)
        {
            flatNormal[i * 4] = 128;
            flatNormal[i * 4 + 1] = 128;
            flatNormal[i * 4 + 2] = 255;
            flatNormal[i * 4 + 3] = 255;
        }
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 4;
                // RMA: mid roughness, dielectric, AO dips in a panel-gap grid.
                bool gap = (x % 32) < 2 || (y % 32) < 2;
                rma[i] = 89;                       // roughness ~0.35
                rma[i + 1] = 0;                    // metallic 0
                rma[i + 2] = gap ? (byte)153 : (byte)255; // AO 0.6 / 1.0
                rma[i + 3] = 230;                  // Toksvig variance ~0.9
                // Detail albedo: fine noise around mid grey, height in alpha.
                byte n = (byte)(128 + rng.Next(-24, 25));
                detailA[i] = n; detailA[i + 1] = n; detailA[i + 2] = n;
                detailA[i + 3] = (byte)(128 + rng.Next(-40, 41));
                // Detail normal: mostly flat with noise tilt.
                detailN[i] = (byte)(128 + rng.Next(-18, 19));
                detailN[i + 1] = (byte)(128 + rng.Next(-18, 19));
                detailN[i + 2] = 255;
                detailN[i + 3] = 255;
            }
        }

        return StrataImporter.Assemble(
            "DemoVehiclePaint",
            StrataImporter.LinearizeSrgb(new Vector3(0.55f, 0.03f, 0.02f)),
            0f, 0.35f, 1f, Vector3.Zero, 0f,
            normalMap: flatNormal, normalW: W, normalH: H,
            rmaMap: rma, rmaW: W, rmaH: H,
            detailAlbedo: detailA, detailNormal: detailN,
            detailW: W, detailH: H, detailTile: 8f,
            clearcoat: 1f,
            iridescence: 1f,
            sheen: 1f,
            anisotropy: 1f);
    }
}
