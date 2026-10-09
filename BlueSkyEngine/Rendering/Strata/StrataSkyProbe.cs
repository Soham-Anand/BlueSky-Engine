using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Rendering.Strata;

/// <summary>
/// Phase-B light probes: nearest-probe selection + SH projection.
/// Projection reuses StrataSkyCapture's basis (same estimator family as sky
/// capture), so probe and sky coefficients are directly comparable — the
/// renderer stays agnostic to coefficient origin, per the Strata plan.
/// Pure functions: fully unit-tested, no GPU, no disk.
/// </summary>
public static class StrataSkyProbe
{
    /// <summary>Stability floor for the Monte Carlo projection.</summary>
    public const int MinProbeSamples = 16;

    /// <summary>
    /// Projects fixed direction samples to 9 SH coefficients (Y-up basis).
    /// Same 4π/N uniform estimator as CaptureCoefficients.
    /// </summary>
    public static Vector3[] ProjectSamples(float[] dirs, float[] colors, int count)
    {
        if (dirs == null || colors == null || dirs.Length < count * 3 || colors.Length < count * 3 || count <= 0)
            throw new InvalidOperationException("Probe projection failed: matched dir/color samples required.");
        var coeffs = new Vector3[StrataSkyCapture.CoeffCount];
        float weight = 4f * MathF.PI / count;
        for (int i = 0; i < count; i++)
        {
            var dir = new Vector3(dirs[i * 3], dirs[i * 3 + 1], dirs[i * 3 + 2]);
            var radiance = new Vector3(colors[i * 3], colors[i * 3 + 1], colors[i * 3 + 2]);
            for (int l = 0; l < StrataSkyCapture.CoeffCount; l++)
                coeffs[l] += radiance * (ProjectBasis(l, dir) * weight);
        }
        return coeffs;
    }

    /// <summary>
    /// Nearest probe to a camera position within its radius. Returns the
    /// probe index, or -1 when no probe covers the point (sky fallback).
    /// </summary>
    public static int SelectProbe(IList<StrataPack.PackProbe> probes, Vector3 cameraPos)
    {
        if (probes == null || probes.Count == 0)
            return -1;
        int best = -1;
        float bestD2 = float.MaxValue;
        for (int i = 0; i < probes.Count; i++)
        {
            var p = probes[i];
            if (p?.Position == null || p.Position.Length != 3 || p.Radius <= 0f)
                continue;
            float dx = cameraPos.X - p.Position[0];
            float dy = cameraPos.Y - p.Position[1];
            float dz = cameraPos.Z - p.Position[2];
            float d2 = dx * dx + dy * dy + dz * dz;
            if (d2 <= p.Radius * p.Radius && d2 < bestD2)
            {
                bestD2 = d2;
                best = i;
            }
        }
        return best;
    }

    // Y-up SH basis (must match StrataSkyCapture.Basis exactly).
    private static float ProjectBasis(int l, Vector3 n)
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
}
