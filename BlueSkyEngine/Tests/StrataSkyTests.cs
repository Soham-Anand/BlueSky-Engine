using System;
using System.Numerics;
using BlueSky.Rendering.Strata;

namespace BlueSky.Tests;

public static class StrataSkyTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Strata Sky + LUT Tests...");
        bool passed = true;

        passed &= TestUniformSkyL0Dominant();
        passed &= TestOverheadSunLightsUp();
        passed &= TestCaptureDeterministic();
        passed &= TestEvaluateMatchesSample();
        passed &= TestLutShapeAndCorners();
        passed &= TestLutNearIdentity();
        passed &= TestLutStripSample();
        passed &= TestPresetDivergesAndClamps();

        return passed;
    }

    private static bool TestUniformSkyL0Dominant()
    {
        try
        {
            // White-only DC term must reconstruct to 1.0 in every direction:
            // E(n) = sqrt(4π)·Y00 = sqrt(4π)·0.282095 = 1.
            var coeffs = new Vector3[StrataSkyCapture.CoeffCount];
            coeffs[0] = new Vector3(MathF.Sqrt(4f * MathF.PI));
            bool valid = true;
            foreach (var d in new[] {
                new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f),
                new Vector3(0f, 0f, 1f), new Vector3(0.3f, -0.8f, 0.5f) })
            {
                var e = StrataSkyCapture.Evaluate(coeffs, Vector3.Normalize(d));
                valid &= Math.Abs(e.X - 1f) < 1e-4f && Math.Abs(e.Y - 1f) < 1e-4f && Math.Abs(e.Z - 1f) < 1e-4f;
            }
            Console.WriteLine($"  ✓ Uniform white L0 reconstructs 1.0: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ L0 Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestOverheadSunLightsUp()
    {
        try
        {
            var sun = new Vector3(0f, 1f, 0f);
            var coeffs = StrataSkyCapture.CaptureCoefficients(sun);
            var up = StrataSkyCapture.Evaluate(coeffs, new Vector3(0f, 1f, 0f));
            var down = StrataSkyCapture.Evaluate(coeffs, new Vector3(0f, -1f, 0f));
            bool valid = up.Length() > down.Length() && up.Y > 0.2f;
            Console.WriteLine($"  ✓ Overhead sun: up={up.Length():F2} > down={down.Length():F2}: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Sun Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestCaptureDeterministic()
    {
        try
        {
            var sun = new Vector3(0.5f, 0.8f, 0.4f);
            var a = StrataSkyCapture.CaptureCoefficients(sun);
            var b = StrataSkyCapture.CaptureCoefficients(sun);
            bool valid = true;
            for (int i = 0; i < a.Length && valid; i++)
                valid = a[i] == b[i];
            Console.WriteLine($"  ✓ Capture deterministic: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Determinism Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestEvaluateMatchesSample()
    {
        try
        {
            // SH reconstruction of smooth sky must track direct samples.
            var sun = new Vector3(0.3f, 0.7f, 0.4f);
            var coeffs = StrataSkyCapture.CaptureCoefficients(sun);
            var dirs = new[] {
                new Vector3(0f, 1f, 0f), new Vector3(0.7f, 0.7f, 0f),
                new Vector3(0f, 0.3f, 0.9f), new Vector3(-0.5f, 0.8f, 0.2f),
            };
            bool valid = true;
            foreach (var d in dirs)
            {
                var direct = StrataSkyCapture.SampleSky(Vector3.Normalize(d), sun);
                var approx = StrataSkyCapture.Evaluate(coeffs, Vector3.Normalize(d));
                float err = (direct - approx).Length();
                valid &= err < 0.35f; // low-order SH smooths the sun disc: loose bound
            }
            Console.WriteLine($"  ✓ SH tracks sky samples: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ SH track Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestLutShapeAndCorners()
    {
        try
        {
            var strip = StrataLut.Generate();
            bool valid = strip.Length == 256 * 16 * 4;
            // Black corner stays ~black, white corner stays ~white.
            int black = 0;
            int wi = ((15 * 256) + (15 * 16 + 15)) * 4;
            valid &= strip[black] < 24 && strip[black + 1] < 24 && strip[black + 2] < 24;
            valid &= strip[wi] > 232 && strip[wi + 1] > 232 && strip[wi + 2] > 232;
            Console.WriteLine($"  ✓ LUT shape + corners: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ LUT Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestLutNearIdentity()
    {
        try
        {
            var strip = StrataLut.Generate(contrast: 0f, saturation: 1f);
            bool valid = true;
            foreach (float v in new[] { 0.1f, 0.25f, 0.5f, 0.75f, 0.9f })
            {
                var c = StrataLut.SampleStrip(strip, v, v, v);
                valid &= Math.Abs(c[0] - v) < 0.08f && Math.Abs(c[1] - v) < 0.08f && Math.Abs(c[2] - v) < 0.08f;
            }
            Console.WriteLine($"  ✓ LUT near-identity at zero grade: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ LUT identity Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestLutStripSample()
    {
        try
        {
            var strip = StrataLut.Generate();
            // Mid grey through the graded LUT must stay mid-ish (no explosions).
            var c = StrataLut.SampleStrip(strip, 0.5f, 0.5f, 0.5f);
            bool valid = c[0] > 0.3f && c[0] < 0.7f && c[1] > 0.3f && c[1] < 0.7f && c[2] > 0.3f && c[2] < 0.7f;
            Console.WriteLine($"  ✓ LUT graded mid stays sane: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ LUT sample Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestPresetDivergesAndClamps()
    {
        try
        {
            var sun = Vector3.Normalize(new Vector3(0.5f, 0.6f, 0.3f));
            var day = StrataSkyCapture.CaptureCoefficients(sun, 0f);
            var studio = StrataSkyCapture.CaptureCoefficients(sun, 1f);
            // Studio must be darker overall (dimmed sun + dark zenith).
            float daySum = 0f, studioSum = 0f;
            foreach (var c in day) daySum += c.X + c.Y + c.Z;
            foreach (var c in studio) studioSum += c.X + c.Y + c.Z;
            // Out-of-range clamps to the ends: -1 == 0, 2 == 1.
            var lo = StrataSkyCapture.CaptureCoefficients(sun, -1f);
            var hi = StrataSkyCapture.CaptureCoefficients(sun, 2f);
            bool clamped = true;
            for (int i = 0; i < StrataSkyCapture.CoeffCount; i++)
                clamped &= (lo[i] - day[i]).LengthSquared() < 1e-10f
                    && (hi[i] - studio[i]).LengthSquared() < 1e-10f;
            bool valid = studioSum < daySum * 0.5f && clamped;
            Console.WriteLine($"  ✓ Sky preset diverges (day {daySum:F2} vs studio {studioSum:F2}) + clamps: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Preset Exception: {ex.Message}");
            return false;
        }
    }
}
