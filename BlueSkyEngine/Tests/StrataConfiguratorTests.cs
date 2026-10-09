using System;
using System.Collections.Generic;
using BlueSky.Editor;
using BlueSky.Rendering.Strata;

namespace BlueSky.Tests;

public static class StrataConfiguratorTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Strata Configurator Tests...");
        bool passed = true;

        passed &= TestSlidersLinearize();
        passed &= TestToggleMatrix();
        passed &= TestNoTexturesNoBits();
        passed &= TestDetailNeedsPair();
        passed &= TestSrgbRoundTrip();
        passed &= TestOpenSaveRoundTrip();

        return passed;
    }

    private static Dictionary<string, (byte[] Rgba, int W, int H)> NoBlobs()
        => new();

    private static bool TestSlidersLinearize()
    {
        try
        {
            // sRGB 140/30/25 must arrive linear (deep red, like the demo paint).
            var mat = Program.BuildConfiguredMaterial("t",
                140f, 30f, 25f, 0f, 0.35f, 1f,
                0f, 0f, 0f, 0f, 8f,
                true, false, true, false, false, false, NoBlobs());
            bool valid = mat.AlbedoLinear.X > 0.2f && mat.AlbedoLinear.X < 0.32f
                && mat.AlbedoLinear.Y < 0.02f && mat.AlbedoLinear.Z < 0.02f;
            Console.WriteLine($"  ✓ Slider sRGB→linear: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Sliders Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestToggleMatrix()
    {
        try
        {
            var blobs = new Dictionary<string, (byte[] Rgba, int W, int H)>
            {
                ["detailAlbedo"] = (new byte[4 * 4 * 4], 4, 4),
                ["detailNormal"] = (new byte[4 * 4 * 4], 4, 4),
            };
            var on = Program.BuildConfiguredMaterial("t",
                128f, 128f, 128f, 0f, 0.5f, 1f,
                0f, 0f, 0f, 0f, 8f,
                true, true, true, false, false, true, blobs);
            bool valid = on.Features.HasFlag(StrataFeature.BakedAO)
                && on.Features.HasFlag(StrataFeature.WrappedDiffuse)
                && on.Features.HasFlag(StrataFeature.DetailNormal)
                && on.Features.HasFlag(StrataFeature.BumpOffset)
                && on.Features.HasFlag(StrataFeature.ScreenDoor)
                && !on.Features.HasFlag(StrataFeature.Toksvig) // needs normal data
                && !on.Features.HasFlag(StrataFeature.Clearcoat);

            var off = Program.BuildConfiguredMaterial("t",
                128f, 128f, 128f, 0f, 0.5f, 1f,
                0f, 0f, 0f, 0f, 8f,
                false, false, false, false, false, false, NoBlobs());
            valid &= !off.Features.HasFlag(StrataFeature.BakedAO)
                && !off.Features.HasFlag(StrataFeature.WrappedDiffuse);
            Console.WriteLine($"  ✓ Toggle matrix: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Toggle Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestNoTexturesNoBits()
    {
        try
        {
            var mat = Program.BuildConfiguredMaterial("t",
                200f, 200f, 200f, 0f, 0.5f, 1f,
                0f, 0f, 0f, 0f, 8f,
                true, false, false, true, true, false, NoBlobs());
            // Toksvig/Bump requested but no data: bits must stay OFF (data rule wins).
            bool valid = !mat.Features.HasFlag(StrataFeature.Toksvig)
                && !mat.Features.HasFlag(StrataFeature.BumpOffset)
                && !mat.Features.HasFlag(StrataFeature.DetailNormal)
                && mat.Textures.Count == 0 && mat.Payload.Length == 0;
            Console.WriteLine($"  ✓ Data-gating beats toggles: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Gating Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestDetailNeedsPair()
    {
        try
        {
            var half = new Dictionary<string, (byte[] Rgba, int W, int H)>
            {
                ["detailAlbedo"] = (new byte[4 * 4 * 4], 4, 4),
            };
            var mat = Program.BuildConfiguredMaterial("t",
                200f, 200f, 200f, 0f, 0.5f, 1f,
                0f, 0f, 0f, 0f, 8f,
                true, true, false, false, true, false, half);
            // Half pair (albedo only): Assemble skips detail entirely.
            bool valid = !mat.Features.HasFlag(StrataFeature.DetailNormal)
                && !mat.Features.HasFlag(StrataFeature.BumpOffset)
                && mat.Textures.Count == 0;
            Console.WriteLine($"  ✓ Half detail pair rejected: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Pair Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestSrgbRoundTrip()
    {
        try
        {
            bool valid = true;
            foreach (float v in new[] { 0f, 0.18f, 0.5f, 1f })
            {
                float rt = StrataImporter.SrgbEncode(StrataImporter.LinearizeSrgb(v));
                valid &= Math.Abs(rt - v) < 1e-4f;
            }
            Console.WriteLine($"  ✓ sRGB encode∘decode round-trip: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ sRGB RT Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestOpenSaveRoundTrip()
    {
        try
        {
            // Demo file → configurator state → rebuilt material: features,
            // linear colors, and texture blobs must survive the loop.
            var demo = StrataSurfaceUpload.BuildDemoVehicleMaterial();
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "StrataTest_" + Guid.NewGuid().ToString("N") + ".stratamat");
            demo.Save(path);

            var loaded = StrataMaterial.Load(path);
            Program.LoadIntoState(loaded, path);
            var rebuilt = Program.BuildConfiguredMaterial("rt");

            bool valid = rebuilt.Features == demo.Features
                && (rebuilt.AlbedoLinear - demo.AlbedoLinear).Length() < 1e-3f
                && rebuilt.Textures.Count == demo.Textures.Count
                && rebuilt.Payload.Length == demo.Payload.Length;
            Console.WriteLine($"  ✓ Open→edit→save round-trip: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Open/save Exception: {ex.Message}");
            return false;
        }
    }
}
