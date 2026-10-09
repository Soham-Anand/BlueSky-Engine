using System;
using System.Numerics;
using BlueSky.Rendering.Strata;

namespace BlueSky.Tests;

public static class StrataCodecTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Strata Codec Tests...");
        bool passed = true;

        passed &= TestRoundTrip();
        passed &= TestColorspaceAndMaskPreserved();
        passed &= TestPayloadBytesIdentical();
        passed &= TestBadMagicRejected();
        passed &= TestBadVersionRejected();
        passed &= TestTruncationRejected();
        passed &= TestOffsetOverflowRejected();
        passed &= TestEmptyMaterialRoundTrip();

        return passed;
    }

    private static StrataMaterial SampleMaterial()
    {
        var payload = new byte[16 * 16 * 4];
        new Random(1337).NextBytes(payload);
        return new StrataMaterial
        {
            Name = "TestCarPaint",
            AlbedoLinear = new Vector3(0.45f, 0.02f, 0.01f),
            Metallic = 0.9f,
            Roughness = 0.35f,
            AO = 0.85f,
            EmissiveLinear = new Vector3(0f, 0f, 0f),
            EmissiveIntensity = 0f,
            Features = StrataFeature.BakedAO | StrataFeature.Clearcoat | StrataFeature.Toksvig,
            Textures = new System.Collections.Generic.List<StrataTextureRef>
            {
                new() { Slot = "albedo", Colorspace = StrataColorspace.Srgb, Width = 8, Height = 8, Format = "RGBA8", PayloadOffset = 0, PayloadSize = 256 },
                new() { Slot = "normal", Colorspace = StrataColorspace.Linear, Width = 8, Height = 8, Format = "RGBA8", PayloadOffset = 256, PayloadSize = 256 },
                new() { Slot = "rma", Colorspace = StrataColorspace.Linear, Width = 8, Height = 8, Format = "RGBA8", PayloadOffset = 512, PayloadSize = 512 },
            },
            Payload = payload,
        };
    }

    private static bool TestRoundTrip()
    {
        try
        {
            var original = SampleMaterial();
            var decoded = StrataMaterial.FromBytes(original.ToBytes());

            bool valid = decoded.Name == original.Name
                && decoded.AlbedoLinear == original.AlbedoLinear
                && Math.Abs(decoded.Metallic - original.Metallic) < 1e-6f
                && Math.Abs(decoded.Roughness - original.Roughness) < 1e-6f
                && Math.Abs(decoded.AO - original.AO) < 1e-6f
                && decoded.Features == original.Features
                && decoded.Textures.Count == 3;
            Console.WriteLine($"  ✓ Codec round-trip: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Round-trip Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestColorspaceAndMaskPreserved()
    {
        try
        {
            var decoded = StrataMaterial.FromBytes(SampleMaterial().ToBytes());
            bool valid = decoded.Textures[0].Colorspace == StrataColorspace.Srgb
                && decoded.Textures[1].Colorspace == StrataColorspace.Linear
                && decoded.Features.HasFlag(StrataFeature.Clearcoat)
                && !decoded.Features.HasFlag(StrataFeature.Sheen)
                && (uint)decoded.Features == ((1u << 0) | (1u << 4) | (1u << 6));
            Console.WriteLine($"  ✓ Colorspace + mask preserved: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Colorspace Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestPayloadBytesIdentical()
    {
        try
        {
            var original = SampleMaterial();
            var decoded = StrataMaterial.FromBytes(original.ToBytes());
            bool valid = decoded.Payload.Length == original.Payload.Length;
            for (int i = 0; valid && i < original.Payload.Length; i++)
                valid = decoded.Payload[i] == original.Payload[i];
            Console.WriteLine($"  ✓ Payload byte-identical ({original.Payload.Length}B): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Payload Exception: {ex.Message}");
            return false;
        }
    }

    private static bool ExpectDecodeFailure(byte[] data, string name)
    {
        try
        {
            StrataMaterial.FromBytes(data);
            Console.WriteLine($"  ❌ {name}: decoded garbage instead of failing");
            return false;
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine($"  ✓ {name}: PASSED");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ {name}: wrong exception ({ex.GetType().Name})");
            return false;
        }
    }

    private static bool TestBadMagicRejected()
    {
        var data = SampleMaterial().ToBytes();
        data[0] = (byte)'X';
        return ExpectDecodeFailure(data, "Bad magic rejected");
    }

    private static bool TestBadVersionRejected()
    {
        var data = SampleMaterial().ToBytes();
        BitConverter.TryWriteBytes(new Span<byte>(data, 8, 4), 999u);
        return ExpectDecodeFailure(data, "Bad version rejected");
    }

    private static bool TestTruncationRejected()
    {
        var data = SampleMaterial().ToBytes();
        var cut = new byte[data.Length / 2];
        Buffer.BlockCopy(data, 0, cut, 0, cut.Length);
        return ExpectDecodeFailure(cut, "Truncation rejected");
    }

    private static bool TestOffsetOverflowRejected()
    {
        try
        {
            var mat = SampleMaterial();
            mat.Textures[0].PayloadOffset = 999999;
            byte[] encoded = mat.ToBytes(); // encode itself must refuse
            Console.WriteLine("  ❌ Offset overflow: encode accepted a bad range");
            return false;
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("  ✓ Offset overflow rejected at encode: PASSED");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Offset overflow: wrong exception ({ex.GetType().Name})");
            return false;
        }
    }

    private static bool TestEmptyMaterialRoundTrip()
    {
        try
        {
            var mat = new StrataMaterial { Name = "Empty" };
            var decoded = StrataMaterial.FromBytes(mat.ToBytes());
            bool valid = decoded.Name == "Empty" && decoded.Textures.Count == 0
                && decoded.Payload.Length == 0 && decoded.Features == StrataFeature.BakedAO;
            Console.WriteLine($"  ✓ Empty material round-trip: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Empty Exception: {ex.Message}");
            return false;
        }
    }
}
