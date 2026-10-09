using System;
using System.Numerics;
using BlueSky.Rendering.Strata;

namespace BlueSky.Tests;

public static class StrataImporterTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Strata Importer Tests...");
        bool passed = true;

        passed &= TestLinearizeKnownValues();
        passed &= TestLinearizeRoundTripStability();
        passed &= TestRmaPackLayout();
        passed &= TestRmaLengthMismatchRejected();
        passed &= TestDetailPairValidation();
        passed &= TestToksvigChainShape();
        passed &= TestToksvigFlatStaysFlat();
        passed &= TestAssembleSetsBitsAndOffsets();
        passed &= TestAssembleClearcoatGate();
        passed &= TestAssembleAlphaGate();
        passed &= TestAssembleIridescenceGate();
        passed &= TestAssembleSheenGate();
        passed &= TestAssembleAnisotropyGate();
        passed &= TestAssembleBentGate();
        passed &= TestAssembleSkinGate();
        passed &= TestAssembleHairGate();

        return passed;
    }

    private static bool TestLinearizeKnownValues()
    {
        try
        {
            // Exact sRGB transfer function anchors.
            bool valid = Math.Abs(StrataImporter.LinearizeSrgb(0f)) < 1e-6f
                && Math.Abs(StrataImporter.LinearizeSrgb(1f) - 1f) < 1e-6f
                && Math.Abs(StrataImporter.LinearizeSrgb(0.04045f) - 0.04045f / 12.92f) < 1e-5f
                && Math.Abs(StrataImporter.LinearizeSrgb(0.5f) - 0.21404f) < 1e-4f
                && Math.Abs(StrataImporter.LinearizeSrgb(0.18f) - 0.02721f) < 1e-4f;
            Console.WriteLine($"  ✓ sRGB→linear anchors (0/0.18/0.5/1.0): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Linearize Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestLinearizeRoundTripStability()
    {
        try
        {
            // Linearize must be monotonic: brighter in => brighter out, no NaN.
            float prev = -1f;
            for (int i = 0; i <= 255; i++)
            {
                float v = StrataImporter.LinearizeSrgb(i / 255f);
                if (float.IsNaN(v) || v < prev) return Fail("Linearize monotonic");
                prev = v;
            }
            Console.WriteLine("  ✓ Linearize monotonic 0..255: PASSED");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Monotonic Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestRmaPackLayout()
    {
        try
        {
            var packed = StrataImporter.PackRMA(
                new[] { 0.5f }, new[] { 1.0f }, new[] { 0.25f });
            bool valid = packed.Length == 4
                && packed[0] == 127 && packed[1] == 255
                && packed[2] == 63 && packed[3] == 255;
            Console.WriteLine($"  ✓ RMA pack layout R=rough G=metal B=AO: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ RMA Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestRmaLengthMismatchRejected()
    {
        try
        {
            StrataImporter.PackRMA(new[] { 0.5f }, new[] { 1.0f, 0f }, new[] { 0.25f });
            return Fail("RMA mismatch accepted");
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("  ✓ RMA length mismatch rejected: PASSED");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ RMA mismatch wrong exception ({ex.GetType().Name})");
            return false;
        }
    }

    private static bool TestDetailPairValidation()
    {
        try
        {
            float tile = StrataImporter.PairDetailMaps(256, 256, 256, 256, 8f);
            bool threw = false;
            try { StrataImporter.PairDetailMaps(256, 256, 128, 128, 8f); }
            catch (InvalidOperationException) { threw = true; }
            bool valid = Math.Abs(tile - 8f) < 1e-6f && threw;
            Console.WriteLine($"  ✓ Detail pair validation: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Detail Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestToksvigChainShape()
    {
        try
        {
            // 4x4 flat-up normal map -> 3 levels (4,2,1), all unit length.
            var top = new byte[4 * 4 * 4];
            for (int i = 0; i < 16; i++)
            {
                top[i * 4] = 128; top[i * 4 + 1] = 128;
                top[i * 4 + 2] = 255; top[i * 4 + 3] = 255;
            }
            var (levels, lengths) = StrataImporter.BuildToksvigMips(top, 4, 4);
            bool valid = levels.Count == 3
                && levels[0].Length == 64 && levels[1].Length == 16 && levels[2].Length == 4
                && lengths.Count == 3;
            Console.WriteLine($"  ✓ Toksvig chain shape 4→2→1: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Toksvig Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestToksvigFlatStaysFlat()
    {
        try
        {
            // Flat map: mean length ~1 at every mip (no variance to correct).
            var top = new byte[8 * 8 * 4];
            for (int i = 0; i < 64; i++)
            {
                top[i * 4] = 128; top[i * 4 + 1] = 128;
                top[i * 4 + 2] = 255; top[i * 4 + 3] = 255;
            }
            var (_, lengths) = StrataImporter.BuildToksvigMips(top, 8, 8);
            bool valid = true;
            foreach (var l in lengths)
                valid &= Math.Abs(l - 1f) < 0.05f;
            // Noisy map must show variance decay (length < 1 at coarse mips).
            var noisy = new byte[8 * 8 * 4];
            var rng = new Random(7);
            for (int i = 0; i < 64; i++)
            {
                noisy[i * 4] = (byte)rng.Next(0, 256);
                noisy[i * 4 + 1] = (byte)rng.Next(0, 256);
                noisy[i * 4 + 2] = (byte)(128 + rng.Next(0, 128));
                noisy[i * 4 + 3] = 255;
            }
            var (_, noisyLengths) = StrataImporter.BuildToksvigMips(noisy, 8, 8);
            valid &= noisyLengths[^1] < 0.99f;
            Console.WriteLine($"  ✓ Toksvig variance signal (flat≈1, noisy<1): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Toksvig variance Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestAssembleSetsBitsAndOffsets()
    {
        try
        {
            var albedo = new byte[4 * 4 * 4];
            var normal = new byte[4 * 4 * 4];
            var mat = StrataImporter.Assemble(
                "Test", new Vector3(0.5f, 0.02f, 0.01f), 0.9f, 0.35f, 1f,
                Vector3.Zero, 0f,
                albedoSrgb: albedo, albedoW: 4, albedoH: 4,
                normalMap: normal, normalW: 4, normalH: 4,
                detailAlbedo: albedo, detailNormal: normal,
                detailW: 4, detailH: 4, detailTile: 8f,
                screenDoor: true);

            bool bits = mat.Features.HasFlag(StrataFeature.BakedAO)
                && mat.Features.HasFlag(StrataFeature.WrappedDiffuse)
                && mat.Features.HasFlag(StrataFeature.Toksvig)
                && mat.Features.HasFlag(StrataFeature.DetailNormal)
                && mat.Features.HasFlag(StrataFeature.BumpOffset)
                && mat.Features.HasFlag(StrataFeature.ScreenDoor)
                && mat.Features.HasFlag(StrataFeature.AlbedoMap)
                && !mat.Features.HasFlag(StrataFeature.Clearcoat);
            bool offsets = mat.Textures.Count == 4
                && mat.Textures[0].PayloadOffset == 0
                && mat.Textures[0].Colorspace == StrataColorspace.Srgb
                && mat.Textures[1].Colorspace == StrataColorspace.Linear
                && mat.Payload.Length == 4 * 64;
            // Assembled material must survive the codec untouched.
            var decoded = StrataMaterial.FromBytes(mat.ToBytes());
            bool valid = bits && offsets && decoded.Features == mat.Features
                && decoded.Payload.Length == mat.Payload.Length;
            Console.WriteLine($"  ✓ Assemble bits+offsets+codec: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Assemble Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestAssembleClearcoatGate()
    {
        try
        {
            var plain = StrataImporter.Assemble(
                "Plain", new Vector3(0.5f, 0.5f, 0.5f), 0f, 0.5f, 1f, Vector3.Zero, 0f);
            var coated = StrataImporter.Assemble(
                "Coated", new Vector3(0.5f, 0.5f, 0.5f), 0f, 0.5f, 1f, Vector3.Zero, 0f,
                clearcoat: 1f);
            bool valid = !plain.Features.HasFlag(StrataFeature.Clearcoat)
                && coated.Features.HasFlag(StrataFeature.Clearcoat)
                // Codec round trip must preserve the bit (weight is fixed
                // full-strength in-shader, same as detailTile precedent).
                && StrataMaterial.FromBytes(coated.ToBytes()).Features.HasFlag(StrataFeature.Clearcoat);
            Console.WriteLine($"  ✓ Assemble clearcoat gate (off/off, on/on): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Clearcoat Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestAssembleAlphaGate()
    {
        try
        {
            var opaque = StrataImporter.Assemble(
                "Opaque", new Vector3(0.5f, 0.5f, 0.5f), 0f, 0.5f, 1f, Vector3.Zero, 0f);
            var glass = StrataImporter.Assemble(
                "Glass", new Vector3(0.1f, 0.1f, 0.12f), 0f, 0.05f, 1f, Vector3.Zero, 0f,
                alpha: 0.15f);
            var decoded = StrataMaterial.FromBytes(glass.ToBytes());
            // Legacy file: header JSON with the Alpha key surgically removed.
            var modern = new StrataMaterial { Name = "Legacy" }.ToBytes();
            var legacy = StripHeaderKey(modern, "Alpha");
            var legacyMat = StrataMaterial.FromBytes(legacy);
            bool valid = !opaque.Features.HasFlag(StrataFeature.Transparent)
                && Math.Abs(opaque.Alpha - 1f) < 1e-6f
                && glass.Features.HasFlag(StrataFeature.Transparent)
                && Math.Abs(glass.Alpha - 0.15f) < 1e-6f
                // Codec must preserve value + bit; alpha clamps to [0,1].
                && decoded.Features.HasFlag(StrataFeature.Transparent)
                && Math.Abs(decoded.Alpha - 0.15f) < 1e-6f
                && Math.Abs(legacyMat.Alpha - 1f) < 1e-6f
                && !legacyMat.Features.HasFlag(StrataFeature.Transparent)
                && StrataSurfaceUpload.BuildParams(glass).Alpha < 0.2f;
            Console.WriteLine($"  ✓ Assemble alpha gate (opaque/glass/codec/params): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Alpha Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestAssembleIridescenceGate()
    {
        try
        {
            var plain = StrataImporter.Assemble(
                "Plain", new Vector3(0.5f, 0.5f, 0.5f), 0f, 0.5f, 1f, Vector3.Zero, 0f);
            var iri = StrataImporter.Assemble(
                "Irid", new Vector3(0.5f, 0.5f, 0.5f), 0f, 0.5f, 1f, Vector3.Zero, 0f,
                iridescence: 1f);
            bool valid = !plain.Features.HasFlag(StrataFeature.Iridescence)
                && iri.Features.HasFlag(StrataFeature.Iridescence)
                // Codec round trip must preserve the bit (fixed ramp in-shader,
                // same as the clearcoat precedent).
                && StrataMaterial.FromBytes(iri.ToBytes()).Features.HasFlag(StrataFeature.Iridescence);
            Console.WriteLine($"  ✓ Assemble iridescence gate (off/off, on/on): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Iridescence Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestAssembleSheenGate()
    {
        try
        {
            var plain = StrataImporter.Assemble(
                "Plain", new Vector3(0.5f, 0.5f, 0.5f), 0f, 0.5f, 1f, Vector3.Zero, 0f);
            var cloth = StrataImporter.Assemble(
                "Cloth", new Vector3(0.5f, 0.5f, 0.5f), 0f, 0.5f, 1f, Vector3.Zero, 0f,
                sheen: 1f);
            bool valid = !plain.Features.HasFlag(StrataFeature.Sheen)
                && cloth.Features.HasFlag(StrataFeature.Sheen)
                // Codec round trip must preserve the bit (fixed fabric
                // response in-shader, same as the clearcoat precedent).
                && StrataMaterial.FromBytes(cloth.ToBytes()).Features.HasFlag(StrataFeature.Sheen);
            Console.WriteLine($"  ✓ Assemble sheen gate (off/off, on/on): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Sheen Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestAssembleAnisotropyGate()
    {
        try
        {
            var plain = StrataImporter.Assemble(
                "Plain", new Vector3(0.5f, 0.5f, 0.5f), 0f, 0.5f, 1f, Vector3.Zero, 0f);
            var brushed = StrataImporter.Assemble(
                "Brushed", new Vector3(0.5f, 0.5f, 0.5f), 1f, 0.4f, 1f, Vector3.Zero, 0f,
                anisotropy: 1f);
            bool valid = !plain.Features.HasFlag(StrataFeature.Anisotropy)
                && brushed.Features.HasFlag(StrataFeature.Anisotropy)
                // Codec round trip must preserve the bit (derivative-frame
                // stretch in-shader, same as the clearcoat precedent).
                && StrataMaterial.FromBytes(brushed.ToBytes()).Features.HasFlag(StrataFeature.Anisotropy);
            Console.WriteLine($"  ✓ Assemble anisotropy gate (off/off, on/on): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Anisotropy Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestAssembleBentGate()
    {
        try
        {
            var plain = StrataImporter.Assemble(
                "Plain", new Vector3(0.5f, 0.5f, 0.5f), 0f, 0.5f, 1f, Vector3.Zero, 0f);
            // World-space bent RGB: up-vector everywhere (flat, no occlusion).
            var bent = new byte[4 * 4 * 4];
            for (int i = 0; i < 16; i++)
            {
                bent[i * 4] = 128; bent[i * 4 + 1] = 128;
                bent[i * 4 + 2] = 255; bent[i * 4 + 3] = 255;
            }
            var m = StrataImporter.Assemble(
                "Bent", new Vector3(0.5f, 0.5f, 0.5f), 0f, 0.5f, 1f, Vector3.Zero, 0f,
                bentMap: bent, bentW: 4, bentH: 4);
            var decoded = StrataMaterial.FromBytes(m.ToBytes());
            bool mismatch = false;
            try
            {
                StrataImporter.Assemble(
                    "Bad", Vector3.One, 0f, 0.5f, 1f, Vector3.Zero, 0f,
                    bentMap: new byte[10], bentW: 4, bentH: 4);
            }
            catch (InvalidOperationException) { mismatch = true; }
            bool valid = !plain.Features.HasFlag(StrataFeature.BentNormal)
                && m.Features.HasFlag(StrataFeature.BentNormal)
                && m.Textures.Exists(t => t.Slot == "bent" && t.Colorspace == StrataColorspace.Linear)
                && decoded.Features.HasFlag(StrataFeature.BentNormal)
                && decoded.Textures.Count == m.Textures.Count
                && mismatch;
            Console.WriteLine($"  ✓ Assemble bent gate (bit+slot+codec+mismatch): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Bent Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestAssembleSkinGate()
    {
        try
        {
            var paint = StrataImporter.Assemble(
                "Paint", new Vector3(0.5f, 0.02f, 0.01f), 0f, 0.35f, 1f, Vector3.Zero, 0f);
            var flesh = StrataImporter.Assemble(
                "Flesh", new Vector3(0.55f, 0.25f, 0.18f), 0f, 0.6f, 1f, Vector3.Zero, 0f,
                skin: 1f);
            bool valid = !paint.Features.HasFlag(StrataFeature.Skin)
                && flesh.Features.HasFlag(StrataFeature.Skin)
                // Codec round trip must preserve the bit (fixed skin response
                // in-shader, same as the clearcoat precedent).
                && StrataMaterial.FromBytes(flesh.ToBytes()).Features.HasFlag(StrataFeature.Skin);
            Console.WriteLine($"  ✓ Assemble skin gate (off/off, on/on): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Skin Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestAssembleHairGate()
    {
        try
        {
            var paint = StrataImporter.Assemble(
                "Paint", new Vector3(0.5f, 0.02f, 0.01f), 0f, 0.35f, 1f, Vector3.Zero, 0f);
            var hair = StrataImporter.Assemble(
                "Hair", new Vector3(0.15f, 0.08f, 0.05f), 0f, 0.6f, 1f, Vector3.Zero, 0f,
                hair: 1f);
            bool valid = !paint.Features.HasFlag(StrataFeature.Hair)
                && hair.Features.HasFlag(StrataFeature.Hair)
                // Codec round trip must preserve the bit (fixed Kajiya-Kay
                // response in-shader, same as the clearcoat precedent).
                && StrataMaterial.FromBytes(hair.ToBytes()).Features.HasFlag(StrataFeature.Hair);
            Console.WriteLine($"  ✓ Assemble hair gate (off/off, on/on): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Hair Exception: {ex.Message}");
            return false;
        }
    }

    private static byte[] StripHeaderKey(byte[] file, string key)
    {
        // Container: magic(8) + version(4) + headerSize(8) + payloadSize(8).
        ulong headerSize = BitConverter.ToUInt64(file, 12);
        var headerJson = System.Text.Json.JsonDocument.Parse(
            new ReadOnlyMemory<byte>(file, 28, (int)headerSize));
        using var ms = new System.IO.MemoryStream();
        using (var w = new System.Text.Json.Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var prop in headerJson.RootElement.EnumerateObject())
                if (!prop.NameEquals(key))
                    prop.WriteTo(w);
            w.WriteEndObject();
        }
        byte[] header = ms.ToArray();
        byte[] payload = new byte[file.Length - 28 - (int)headerSize];
        Buffer.BlockCopy(file, 28 + (int)headerSize, payload, 0, payload.Length);
        byte[] out_ = new byte[28 + header.Length + payload.Length];
        Buffer.BlockCopy(file, 0, out_, 0, 12);
        Buffer.BlockCopy(BitConverter.GetBytes((ulong)header.Length), 0, out_, 12, 8);
        Buffer.BlockCopy(file, 20, out_, 20, 8);
        Buffer.BlockCopy(header, 0, out_, 28, header.Length);
        Buffer.BlockCopy(payload, 0, out_, 28 + header.Length, payload.Length);
        return out_;
    }

    private static bool Fail(string what)
    {
        Console.WriteLine($"  ❌ {what}: FAILED");
        return false;
    }
}
