using System;
using System.Numerics;

namespace BlueSky.Rendering.Strata;

/// <summary>
/// Sky-captured image-based lighting for Strata.
///
/// Mirrors the viewport sky shader (gradient + sun disc/glow + haze) on the
/// CPU, samples it over a Fibonacci sphere, and projects to 9 spherical
/// harmonic coefficients (Y-up basis, matching shader N). Recomputed only
/// when the sun moves — free at runtime (one 9×float4 uniform upload).
/// </summary>
public static class StrataSkyCapture
{
    public const int CoeffCount = 9;
    public const int SampleCount = 128;

    // Y-up SH basis weights (permuted from Sloan: up axis is Y here).
    private static float Basis(int l, Vector3 n)
    {
        float x = n.X, y = n.Y, z = n.Z;
        return l switch
        {
            0 => 0.282095f,
            1 => 0.488603f * y,
            2 => 0.488603f * z,
            3 => 0.488603f * x,
            4 => 1.092548f * x * y,
            5 => 1.092548f * y * z,
            6 => 0.315392f * (3f * y * y - 1f),
            7 => 1.092548f * x * z,
            _ => 0.546274f * (x * x - z * z),
        };
    }

    /// <summary>C# port of computeSky (viewport_3d.metal). Linear radiance.</summary>
    /// <param name="preset">0 = day, 1 = studio. Palettes lerp between them.</param>
    public static Vector3 SampleSky(Vector3 rayDir, Vector3 sunDir, float preset = 0f)
    {
        preset = Math.Clamp(preset, 0f, 1f);
        float height = Math.Clamp(rayDir.Y, 0f, 1f);
        var zenith = Lerp(new Vector3(0.15f, 0.45f, 0.85f), new Vector3(0.015f, 0.02f, 0.03f), preset);
        var horizon = Lerp(new Vector3(0.55f, 0.82f, 0.98f), new Vector3(0.07f, 0.08f, 0.10f), preset);
        var sky = horizon + (zenith - horizon) * MathF.Pow(height, 0.6f);

        float cosTheta = Math.Clamp(Vector3.Dot(rayDir, Vector3.Normalize(sunDir)), -1f, 1f);
        float theta = MathF.Acos(Math.Clamp(cosTheta, -1f, 1f));
        float sunDisc = 1f - Smoothstep(0.0045f, 0.005f, theta);
        float sunGlow = MathF.Exp(-theta * 10f) * 0.4f;
        float sunHaze = MathF.Exp(-theta * 2.5f) * 0.15f;
        var sunColor = Lerp(new Vector3(1f, 1f, 0.95f), new Vector3(0.85f, 0.9f, 1f), preset);
        var glowColor = Lerp(new Vector3(1f, 0.95f, 0.85f), new Vector3(0.45f, 0.5f, 0.6f), preset);
        sky += sunColor * sunDisc * Lerp(1.5f, 1.1f, preset)
             + glowColor * (sunGlow + sunHaze);

        float haze = MathF.Exp(-height * 3.5f) * 0.2f;
        var hazeTarget = Lerp(new Vector3(0.8f, 0.9f, 1f), new Vector3(0.04f, 0.05f, 0.07f), preset);
        sky = sky + (hazeTarget - sky) * haze;
        return new Vector3(
            Math.Clamp(sky.X, 0f, 1f),
            Math.Clamp(sky.Y, 0f, 1f),
            Math.Clamp(sky.Z, 0f, 1f));
    }

    private static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Smoothstep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Projects sky radiance to 9 SH coefficients. Below-horizon samples use
    /// a fixed ground tint (the shader sky is upper-hemisphere only).
    /// </summary>
    public static Vector3[] CaptureCoefficients(Vector3 sunDir, float preset = 0f, Vector3? groundTint = null)
    {
        Vector3 ground = groundTint ?? Lerp(new Vector3(0.3f, 0.25f, 0.2f),
            new Vector3(0.06f, 0.055f, 0.05f), Math.Clamp(preset, 0f, 1f));
        var coeffs = new Vector3[CoeffCount];
        float weight = 4f * MathF.PI / SampleCount;
        var golden = MathF.PI * (3f - MathF.Sqrt(5f));

        for (int i = 0; i < SampleCount; i++)
        {
            float yy = 1f - (i + 0.5f) * 2f / SampleCount;
            float r = MathF.Sqrt(Math.Max(0f, 1f - yy * yy));
            float phi = golden * i;
            var dir = new Vector3(MathF.Cos(phi) * r, yy, MathF.Sin(phi) * r);
            Vector3 radiance = dir.Y >= 0 ? SampleSky(dir, sunDir, preset) : ground;
            for (int l = 0; l < CoeffCount; l++)
            {
                float b = Basis(l, dir);
                coeffs[l] += radiance * (b * weight);
            }
        }
        return coeffs;
    }

    /// <summary>Evaluates captured SH for a Y-up normal (mirrors the shader).</summary>
    public static Vector3 Evaluate(Vector3[] coeffs, Vector3 n)
    {
        Vector3 r = Vector3.Zero;
        for (int l = 0; l < CoeffCount; l++)
            r += coeffs[l] * Basis(l, n);
        return r;
    }
}
