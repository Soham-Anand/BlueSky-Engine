using System;
using System.Numerics;
using BlueSky.Rendering.Strata;

namespace BlueSky.Tests;

public static class StrataSurfaceUploadTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Strata Surface Upload Tests...");
        bool passed = true;

        passed &= TestMaskMapping();
        passed &= TestWrapOnlyWhenFlagged();
        passed &= TestDemoMaterialBuilds();
        passed &= TestDemoSurvivesCodec();
        passed &= TestDefaultsSane();

        return passed;
    }

    private static bool TestMaskMapping()
    {
        try
        {
            var mat = new StrataMaterial
            {
                Features = StrataFeature.BakedAO | StrataFeature.Clearcoat | StrataFeature.ScreenDoor
            };
            var p = StrataSurfaceUpload.BuildParams(mat);
            bool valid = p.FeatureMask == ((1u << 0) | (1u << 6) | (1u << 10));
            Console.WriteLine($"  ✓ Mask bits map 1:1 (0x{p.FeatureMask:X}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Mask Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestWrapOnlyWhenFlagged()
    {
        try
        {
            var plain = StrataSurfaceUpload.BuildParams(new StrataMaterial());
            var wrapped = StrataSurfaceUpload.BuildParams(new StrataMaterial
            {
                Features = StrataFeature.WrappedDiffuse
            });
            bool valid = Math.Abs(plain.Wrap) < 1e-6f
                && Math.Abs(wrapped.Wrap - StrataSurfaceUpload.DefaultWrap) < 1e-6f
                && Math.Abs(wrapped.DetailTile - StrataSurfaceUpload.DefaultDetailTile) < 1e-6f;
            Console.WriteLine($"  ✓ Wrap gated by flag: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Wrap Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestDemoMaterialBuilds()
    {
        try
        {
            var mat = StrataSurfaceUpload.BuildDemoVehicleMaterial();
            var p = StrataSurfaceUpload.BuildParams(mat);
            bool bits = mat.Features.HasFlag(StrataFeature.BakedAO)
                && mat.Features.HasFlag(StrataFeature.WrappedDiffuse)
                && mat.Features.HasFlag(StrataFeature.Toksvig)
                && mat.Features.HasFlag(StrataFeature.DetailNormal)
                && mat.Features.HasFlag(StrataFeature.BumpOffset)
                && mat.Features.HasFlag(StrataFeature.Clearcoat)
                && mat.Features.HasFlag(StrataFeature.Iridescence)
                && mat.Features.HasFlag(StrataFeature.Sheen)
                && mat.Features.HasFlag(StrataFeature.Anisotropy); // Tier-2 brushed proof
            bool tex = mat.Textures.Count == 4 && mat.Payload.Length == 4 * 128 * 128 * 4;
            bool color = p.BaseColor.X > 0.05f && p.BaseColor.X < 0.3f; // linearized deep red
            bool valid = bits && tex && color;
            Console.WriteLine($"  ✓ Demo vehicle material (bits+payload+linear red): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Demo Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestDemoSurvivesCodec()
    {
        try
        {
            var mat = StrataSurfaceUpload.BuildDemoVehicleMaterial();
            var decoded = StrataMaterial.FromBytes(mat.ToBytes());
            bool valid = decoded.Features == mat.Features
                && decoded.Payload.Length == mat.Payload.Length
                && decoded.AlbedoLinear == mat.AlbedoLinear;
            Console.WriteLine($"  ✓ Demo survives codec: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Demo codec Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestDefaultsSane()
    {
        try
        {
            var p = StrataSurfaceUpload.BuildParams(new StrataMaterial());
            bool valid = p.Roughness >= 0f && p.Roughness <= 1f
                && p.Metallic >= 0f && p.Metallic <= 1f
                && p.DetailTile >= 1f && p.ToksvigK >= 0f;
            Console.WriteLine($"  ✓ Sane defaults: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Defaults Exception: {ex.Message}");
            return false;
        }
    }
}
