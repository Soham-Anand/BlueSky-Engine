using System;
using System.Collections.Generic;
using System.Numerics;
using BlueSky.Rendering.Strata;

namespace BlueSky.Tests;

public static class StrataProbeTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Strata Probe Tests...");
        bool passed = true;

        passed &= TestProjectUniformWhite();
        passed &= TestProjectBasisExactness();
        passed &= TestProjectOverheadSun();
        passed &= TestSelectNearestAndRadius();
        passed &= TestSelectFallback();
        passed &= TestProbeValidation();
        passed &= TestProbeRoundTrip();
        passed &= TestProbeImportChain();

        return passed;
    }

    // Same Fibonacci sphere as the Ease baker (estimator consistency).
    private static void FibDirs(int n, float[] dirs)
    {
        float golden = MathF.PI * (3f - MathF.Sqrt(5f));
        for (int i = 0; i < n; i++)
        {
            float yy = 1f - (i + 0.5f) * 2f / n;
            float r = MathF.Sqrt(Math.Max(0f, 1f - yy * yy));
            float phi = golden * i;
            dirs[i * 3] = MathF.Cos(phi) * r;
            dirs[i * 3 + 1] = yy;
            dirs[i * 3 + 2] = MathF.Sin(phi) * r;
        }
    }

    private static bool TestProjectUniformWhite()
    {
        try
        {
            const int n = 64;
            var dirs = new float[n * 3];
            var cols = new float[n * 3];
            FibDirs(n, dirs);
            for (int i = 0; i < cols.Length; i++) cols[i] = 1f;
            var c = StrataSkyProbe.ProjectSamples(dirs, cols, n);
            // Uniform-1 field: c0 = sqrt(4π) ≈ 3.5449 (mirrors the L0 test),
            // all higher bands ≈ 0.
            bool valid = Math.Abs(c[0].X - 3.5449f) < 5e-2f
                && Math.Abs(c[0].Y - 3.5449f) < 5e-2f
                && Math.Abs(c[0].Z - 3.5449f) < 5e-2f;
            for (int l = 1; valid && l < StrataSkyCapture.CoeffCount; l++)
                valid &= c[l].Length() < 5e-2f;
            Console.WriteLine($"  ✓ Project uniform white (c0≈3.54, rest≈0): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Project-white Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestProjectBasisExactness()
    {
        try
        {
            // Orthonormality: sampling basis function Y3 must recover ~1 in
            // slot 3 and ~0 elsewhere (quadrature exactness of the estimator).
            const int n = 64;
            var dirs = new float[n * 3];
            var cols = new float[n * 3];
            FibDirs(n, dirs);
            for (int i = 0; i < n; i++)
            {
                float x = dirs[i * 3];
                float y3 = 0.488603f * x;
                cols[i * 3] = y3;
                cols[i * 3 + 1] = 0f;
                cols[i * 3 + 2] = 0f;
            }
            var c = StrataSkyProbe.ProjectSamples(dirs, cols, n);
            bool valid = Math.Abs(c[3].X - 1f) < 5e-2f;
            for (int l = 0; valid && l < StrataSkyCapture.CoeffCount; l++)
            {
                if (l == 3) continue;
                valid &= Math.Abs(c[l].X) < 5e-2f;
            }
            Console.WriteLine($"  ✓ Project basis exactness (Y3→slot 3): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Basis Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestProjectOverheadSun()
    {
        try
        {
            // Clamped overhead sun: y-band (slot 1) must dominate positive.
            const int n = 64;
            var dirs = new float[n * 3];
            var cols = new float[n * 3];
            FibDirs(n, dirs);
            for (int i = 0; i < n; i++)
            {
                float v = Math.Max(0f, dirs[i * 3 + 1]);
                cols[i * 3] = v;
                cols[i * 3 + 1] = v;
                cols[i * 3 + 2] = v;
            }
            var c = StrataSkyProbe.ProjectSamples(dirs, cols, n);
            bool valid = c[1].X > 0.5f && c[1].X > Math.Abs(c[2].X) && c[1].X > Math.Abs(c[3].X);
            Console.WriteLine($"  ✓ Project overhead sun (y-band {c[1].X:F3} dominant): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Sun Exception: {ex.Message}");
            return false;
        }
    }

    private static List<StrataPack.PackProbe> TwoProbes() => new()
    {
        new StrataPack.PackProbe { Name = "A", Position = new[] { 0f, 0f, 0f }, Radius = 5f },
        new StrataPack.PackProbe { Name = "B", Position = new[] { 10f, 0f, 0f }, Radius = 5f },
    };

    private static bool TestSelectNearestAndRadius()
    {
        try
        {
            var probes = TwoProbes();
            bool valid = StrataSkyProbe.SelectProbe(probes, new Vector3(1f, 0f, 0f)) == 0
                && StrataSkyProbe.SelectProbe(probes, new Vector3(9f, 0f, 0f)) == 1
                && StrataSkyProbe.SelectProbe(probes, new Vector3(5f, 0f, 0f)) == 0 // tie → first wins
                && StrataSkyProbe.SelectProbe(probes, new Vector3(0f, 0f, 6f)) == -1; // outside radius
            Console.WriteLine($"  ✓ Select nearest + radius: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Select Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestSelectFallback()
    {
        try
        {
            bool valid = StrataSkyProbe.SelectProbe(new List<StrataPack.PackProbe>(), Vector3.Zero) == -1
                && StrataSkyProbe.SelectProbe(null!, Vector3.Zero) == -1
                && StrataSkyProbe.SelectProbe(TwoProbes(), new Vector3(100f, 0f, 0f)) == -1;
            Console.WriteLine($"  ✓ Select fallback (empty/null/far → sky): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Fallback Exception: {ex.Message}");
            return false;
        }
    }

    private static StrataPack.PackFile ProbePack(
        string name = "P", float radius = 5f, int samples = 64, bool normalize = true)
    {
        var pack = StrataPackTests.SamplePack();
        var dirs = new float[samples * 3];
        var cols = new float[samples * 3];
        FibDirs(samples, dirs);
        for (int i = 0; i < samples; i++)
        {
            float s = normalize ? 1f : 2f; // denormalize x when asked
            dirs[i * 3] *= s;
            cols[i * 3] = 0.5f;
            cols[i * 3 + 1] = 0.5f;
            cols[i * 3 + 2] = 0.5f;
        }
        pack.Header.Probes.Add(new StrataPack.PackProbe
        {
            Name = name,
            Position = new[] { 0f, 1f, 0f },
            Radius = radius,
            SampleDirs = dirs,
            SampleColors = cols,
        });
        return pack;
    }

    private static bool ExpectProbeFail(StrataPack.PackFile pack, string what)
    {
        try
        {
            StrataPack.Decode(StrataPack.Encode(pack));
            Console.WriteLine($"  ❌ {what}: accepted, should reject");
            return false;
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine($"  ✓ {what}: PASSED");
            return true;
        }
    }

    private static bool TestProbeValidation()
    {
        try
        {
            bool ok = true;
            ok &= ExpectProbeFail(ProbePack(radius: 0f), "Zero radius rejected");
            ok &= ExpectProbeFail(ProbePack(radius: -2f), "Negative radius rejected");
            ok &= ExpectProbeFail(ProbePack(samples: 8), "Under-sampled probe rejected");
            ok &= ExpectProbeFail(ProbePack(normalize: false), "Denormalized dirs rejected");
            var dup = ProbePack();
            dup.Header.Probes.Add(new StrataPack.PackProbe
            {
                Name = "P", Position = new[] { 0f, 0f, 0f }, Radius = 1f,
                SampleDirs = dup.Header.Probes[0].SampleDirs,
                SampleColors = dup.Header.Probes[0].SampleColors,
            });
            ok &= ExpectProbeFail(dup, "Duplicate probe names rejected");
            var neg = ProbePack();
            neg.Header.Probes[0].SampleColors[0] = -1f;
            ok &= ExpectProbeFail(neg, "Negative sample color rejected");
            return ok;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Validation Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestProbeRoundTrip()
    {
        try
        {
            var pack = ProbePack();
            var decoded = StrataPack.Decode(StrataPack.Encode(pack));
            var p = decoded.Header.Probes[0];
            bool valid = decoded.Header.Probes.Count == 1 && p.Name == "P"
                && Math.Abs(p.Radius - 5f) < 1e-6f
                && p.SampleDirs.Length == 64 * 3 && p.SampleColors.Length == 64 * 3;
            // Legacy packs (no probes key) decode to empty.
            var legacy = StrataPackTests.SamplePack();
            bool legacyOk = StrataPack.Decode(StrataPack.Encode(legacy)).Header.Probes.Count == 0;
            valid &= legacyOk;
            Console.WriteLine($"  ✓ Probe round-trip + legacy empty: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Round-trip Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestProbeImportChain()
    {
        try
        {
            // Full Phase-B chain: pack with probe → import cites sourcePack →
            // header re-read → nearest selection → projection. This is exactly
            // what the renderer does per frame (minus the GPU upload).
            var pack = ProbePack();
            string dir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "StrataTest_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string packPath = System.IO.Path.Combine(dir, "probe.stratapack");
            System.IO.File.WriteAllBytes(packPath, StrataPack.Encode(pack));
            string target = System.IO.Path.Combine(dir, "Out");
            System.IO.Directory.CreateDirectory(target);
            var asset = new BlueSky.Core.Assets.BlueAsset { AssetName = "Probe" };
            var options = new BlueSky.Core.Assets.ImportOptions();
            options.Settings["TargetDirectory"] = target;
            var result = new BlueSky.Core.Assets.StratapackImportHandler().Import(packPath, asset, options);
            bool cited = result.Success
                && asset.Metadata.TryGetValue("sourcePack", out var sp) && sp == packPath;
            var (ph, pbase) = StrataPack.DecodeHeader(packPath);
            int sel = StrataSkyProbe.SelectProbe(ph.Probes, new Vector3(0f, 1f, 0f));
            var pr = ph.Probes[sel];
            var coeffs = StrataSkyProbe.ProjectSamples(
                pr.SampleDirs, pr.SampleColors, pr.SampleDirs.Length / 3);
            // Grey 0.5 samples → c0 ≈ 3.5449 × 0.5.
            bool valid = cited && sel == 0 && pbase > 28
                && Math.Abs(coeffs[0].X - 1.7725f) < 5e-2f;
            Console.WriteLine($"  ✓ Probe import chain (cite→select→project): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Chain Exception: {ex.Message}");
            return false;
        }
    }
}
