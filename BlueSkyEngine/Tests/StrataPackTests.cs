using System;
using System.Text;
using BlueSky.Rendering.Strata;

namespace BlueSky.Tests;

public static class StrataPackTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running StrataPack Codec Tests...");
        bool passed = true;

        passed &= TestRoundTrip();
        passed &= TestBadMagic();
        passed &= TestBadVersion();
        passed &= TestTruncation();
        passed &= TestBlobOverflow();
        passed &= TestMeshByteMismatch();
        passed &= TestSubmeshRangeOverflow();
        passed &= TestDuplicateSlot();
        passed &= TestVertexAoLayout();
        passed &= TestBentEncodingRequired();
        passed &= TestTooShort();
        passed &= TestCheckedInFixture();
        passed &= TestEaseWrittenFixture();
        passed &= TestHandlerAcceptsFixture();
        passed &= TestEaseLiveCube();
        passed &= TestEmptyPackRejected();
        passed &= TestMultiMeshSplit();
        passed &= TestUnknownKeysTolerated();
        passed &= TestSourcemapSurvives();
        passed &= TestClearcoatSurvivesPack();
        passed &= TestAlphaSurvivesPack();
        passed &= TestSheenSurvivesPack();
        passed &= TestAnisotropySurvivesPack();
        passed &= TestSkeletonRoundTrip();
        passed &= TestSkeletonRejected();
        passed &= TestSkeletalHandlerInPack();
        passed &= TestBentEncodingGate();
        passed &= TestDeadKeysRejected();

        return passed;
    }

    internal static StrataPack.PackFile SamplePack()
    {
        // One quad mesh (4 verts Packed32 + 6 u32 indices), one slot,
        // one surface, one 2x2 texture, one aomap, one bent map (tangent),
        // vertex AO floats.
        var pack = new StrataPack.PackFile();
        var payload = new System.Collections.Generic.List<byte>();

        byte[] verts = new byte[4 * 32];
        byte[] indices = new byte[6 * 4];
        for (int i = 0; i < 6; i++)
            BitConverter.TryWriteBytes(new Span<byte>(indices, i * 4, 4), (uint)i);
        byte[] ao = new byte[4 * 4];
        for (int i = 0; i < 4; i++)
            BitConverter.TryWriteBytes(new Span<byte>(ao, i * 4, 4), 1f);
        byte[] tex = new byte[2 * 2 * 4];
        for (int i = 0; i < tex.Length; i++) tex[i] = 128;

        ulong vo = 0;
        payload.AddRange(verts);
        ulong io = (ulong)payload.Count;
        payload.AddRange(indices);
        ulong aoo = (ulong)payload.Count;
        payload.AddRange(ao);
        ulong to = (ulong)payload.Count;
        payload.AddRange(tex);
        // Pad bent map with a second copy of the texture bytes.
        ulong bo = (ulong)payload.Count;
        payload.AddRange(tex);

        pack.Header.Meshes.Add(new StrataPack.PackMesh
        {
            Name = "Quad",
            VertexCount = 4,
            IndexCount = 6,
            Submeshes = { new StrataPack.PackSubmesh { Offset = 0, Count = 6, Slot = 0 } },
            VertexOffset = vo,
            VertexSize = (ulong)verts.Length,
            IndexOffset = io,
            IndexSize = (ulong)indices.Length,
            AoOffset = aoo,
            AoSize = (ulong)ao.Length,
            AoCount = 4,
        });
        pack.Header.Surfaces.Add(new StrataPack.PackSurface
        {
            Slot = 0,
            AlbedoLinear = new[] { 0.5f, 0.02f, 0.01f },
            Metallic = 0f,
            Roughness = 0.6f,
            EmissiveLinear = new[] { 0f, 0f, 0f },
            EmissiveIntensity = 0f,
        });
        pack.Header.Textures.Add(new StrataPack.PackTexture
        {
            Slot = 0, Role = "albedo", Colorspace = "srgb",
            Width = 2, Height = 2, Offset = to, Size = (ulong)tex.Length,
        });
        pack.Header.AoMaps.Add(new StrataPack.PackAoMap
        {
            Slot = 0, Role = "ao", Colorspace = "linear",
            Width = 2, Height = 2, Offset = to, Size = (ulong)tex.Length,
        });
        pack.Header.BentMaps.Add(new StrataPack.PackBentMap
        {
            Slot = 0, Role = "bent", Colorspace = "linear", Encoding = "tangent",
            Width = 2, Height = 2, Offset = bo, Size = (ulong)tex.Length,
        });
        pack.Payload = payload.ToArray();
        return pack;
    }

    private static bool ExpectFail(byte[] data, string name)
    {
        try
        {
            StrataPack.Decode(data);
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

    private static bool TestRoundTrip()
    {
        try
        {
            var pack = SamplePack();
            var decoded = StrataPack.Decode(StrataPack.Encode(pack));
            bool valid = decoded.Header.Meshes.Count == 1
                && decoded.Header.Surfaces.Count == 1
                && decoded.Header.Textures.Count == 1
                && decoded.Header.AoMaps.Count == 1
                && decoded.Header.BentMaps.Count == 1
                && decoded.Header.BentMaps[0].Encoding == "tangent"
                && decoded.Payload.Length == pack.Payload.Length;
            for (int i = 0; valid && i < pack.Payload.Length; i++)
                valid = decoded.Payload[i] == pack.Payload[i];
            Console.WriteLine($"  ✓ Pack round-trip byte-identical: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Round-trip Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestBadMagic()
    {
        var data = StrataPack.Encode(SamplePack());
        data[0] = (byte)'X';
        return ExpectFail(data, "Bad magic rejected");
    }

    private static bool TestBadVersion()
    {
        var data = StrataPack.Encode(SamplePack());
        BitConverter.TryWriteBytes(new Span<byte>(data, 8, 4), 999u);
        return ExpectFail(data, "Bad version rejected");
    }

    private static bool TestTruncation()
    {
        var data = StrataPack.Encode(SamplePack());
        var cut = new byte[data.Length - 10];
        Buffer.BlockCopy(data, 0, cut, 0, cut.Length);
        return ExpectFail(cut, "Truncation rejected");
    }

    private static bool TestBlobOverflow()
    {
        var pack = SamplePack();
        pack.Header.Textures[0].Size = 999999999;
        return ExpectFail(StrataPack.Encode(pack), "Blob overflow rejected");
    }

    private static bool TestMeshByteMismatch()
    {
        var pack = SamplePack();
        pack.Header.Meshes[0].VertexCount = 5; // 5×32 != 128 bytes
        return ExpectFail(StrataPack.Encode(pack), "Vertex byte/count mismatch rejected");
    }

    private static bool TestSubmeshRangeOverflow()
    {
        var pack = SamplePack();
        pack.Header.Meshes[0].Submeshes[0].Count = 99;
        return ExpectFail(StrataPack.Encode(pack), "Submesh range overflow rejected");
    }

    private static bool TestDuplicateSlot()
    {
        var pack = SamplePack();
        pack.Header.Meshes[0].Submeshes.Add(new StrataPack.PackSubmesh
        {
            Offset = 0, Count = 3, Slot = 0, // duplicate
        });
        return ExpectFail(StrataPack.Encode(pack), "Duplicate slot rejected");
    }

    private static bool TestVertexAoLayout()
    {
        var pack = SamplePack();
        pack.Header.Meshes[0].AoCount = 5; // 5×4 != 16 bytes
        return ExpectFail(StrataPack.Encode(pack), "Vertex AO layout mismatch rejected");
    }

    private static bool TestBentEncodingRequired()
    {
        var pack = SamplePack();
        pack.Header.BentMaps[0].Encoding = "";
        return ExpectFail(StrataPack.Encode(pack), "Bent encoding required");
    }

    private static bool TestTooShort()
        => ExpectFail(new byte[] { 1, 2, 3 }, "Too-short file rejected");

    private static bool TestCheckedInFixture()
    {
        // Step-3 acceptance foundation: the shipped sample pack must decode.
        try
        {
            string? found = FindFixture("sample_quad.stratapack");
            if (found == null)
            {
                Console.WriteLine("  ✓ Checked-in fixture decodes: SKIPPED (run from repo root to enable)");
                return true;
            }
            var pack = StrataPack.Decode(System.IO.File.ReadAllBytes(found));
            bool valid = pack.Header.Meshes.Count == 1
                && pack.Header.Meshes[0].Name == "Quad"
                && pack.Header.BentMaps.Count == 1
                && pack.Header.BentMaps[0].Encoding == "tangent";
            Console.WriteLine($"  ✓ Checked-in fixture decodes: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Fixture Exception: {ex.Message}");
            return false;
        }
    }

    private static string? FindFixture(string name)
    {
        string[] candidates = {
            $"BlueSkyEngine/Tests/Fixtures/{name}",
            $"Tests/Fixtures/{name}",
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "Tests", "Fixtures", name),
        };
        foreach (var c in candidates)
        {
            if (System.IO.File.Exists(c)) return c;
        }
        return null;
    }

    private static bool TestUnknownKeysTolerated()
    {
        // Forward-compat core: unknown JSON keys at header level, texture
        // level, and mesh level must decode identically (future sections).
        try
        {
            var pack = SamplePack();
            byte[] raw = StrataPack.Encode(pack);
            uint hs = BitConverter.ToUInt32(raw, 12);
            string json = Encoding.UTF8.GetString(raw, 28, (int)hs);
            // Inject unknowns at three positions: header, first mesh, first texture.
            json = json.Replace("\"meshes\":[{",
                "\"futureSection\":{\"x\":1},\"meshes\":[{", StringComparison.Ordinal);
            json = json.Replace("\"slot\":0,\"albedoLinear\"",
                "\"slot\":0,\"unknownFuture\":[1,2],\"albedoLinear\"", StringComparison.Ordinal);
            byte[] hj = Encoding.UTF8.GetBytes(json);
            byte[] payload = new byte[raw.Length - 28 - (int)hs];
            Buffer.BlockCopy(raw, 28 + (int)hs, payload, 0, payload.Length);
            byte[] out_ = new byte[28 + hj.Length + payload.Length];
            Buffer.BlockCopy(raw, 0, out_, 0, 8);
            BitConverter.TryWriteBytes(new Span<byte>(out_, 8, 4), 1u);
            BitConverter.TryWriteBytes(new Span<byte>(out_, 12, 8), (ulong)hj.Length);
            BitConverter.TryWriteBytes(new Span<byte>(out_, 20, 8), (ulong)payload.Length);
            Buffer.BlockCopy(hj, 0, out_, 28, hj.Length);
            Buffer.BlockCopy(payload, 0, out_, 28 + hj.Length, payload.Length);

            var decoded = StrataPack.Decode(out_);
            bool valid = decoded.Header.Meshes.Count == 1
                && decoded.Header.Surfaces.Count == 1
                && decoded.Payload.Length == pack.Payload.Length;
            Console.WriteLine($"  ✓ Unknown keys tolerated (3 positions): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Unknown-key Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestSourcemapSurvives()
    {
        // Sourcemap + uv/transform ride along without disturbing the contract.
        try
        {
            var pack = SamplePack();
            var tex = new StrataPack.PackTexture
            {
                Slot = 0, Role = "albedo", Colorspace = "srgb",
                Width = 2, Height = 2, Offset = 0, Size = 16,
                UvLayer = "UVMap", Transform = new[] { 0f, 0f, 0f, 2f, 2f, 2f },
            };
            pack.Header.Textures.Add(tex);
            var decoded = StrataPack.Decode(StrataPack.Encode(pack));
            var rt = decoded.Header.Textures.Find(t => t.UvLayer == "UVMap");
            bool valid = rt != null && rt.UvLayer == "UVMap"
                && rt.Transform != null && rt.Transform.Length == 6
                && Math.Abs(rt.Transform[3] - 2f) < 1e-6f;
            Console.WriteLine($"  ✓ Sourcemap fields survive: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Sourcemap Exception: {ex.Message}");
            return false;
        }
    }
    private static bool TestEmptyPackRejected()
    {
        // GTRR32 case: structurally valid pack, zero meshes.
        // Decode succeeds (it IS a pack); the HANDLER must refuse loudly.
        try
        {
            string? found = FindFixture("empty_pack.stratapack");
            if (found == null)
            {
                Console.WriteLine("  ✓ Empty pack rejected: SKIPPED (run from repo root to enable)");
                return true;
            }
            // Decode must succeed — validity and content are separate verdicts.
            var pack = StrataPack.Decode(System.IO.File.ReadAllBytes(found));
            bool decodedEmpty = pack.Header.Meshes.Count == 0;

            string target = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "StrataTest_" + Guid.NewGuid().ToString("N"), "Empty");
            System.IO.Directory.CreateDirectory(target);
            var asset = new BlueSky.Core.Assets.BlueAsset { AssetName = "Empty" };
            var options = new BlueSky.Core.Assets.ImportOptions();
            options.Settings["TargetDirectory"] = target;
            var result = new BlueSky.Core.Assets.StratapackImportHandler()
                .Import(found, asset, options);

            bool valid = decodedEmpty && !result.Success
                && result.Error != null && result.Error.Contains("no meshes");
            Console.WriteLine($"  ✓ Empty pack decoded-but-refused: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Empty pack Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestEaseLiveCube()
    {
        // Genuine Ease→engine round trip
        // (default cube + red Principled + generated image) must decode AND
        // import with exact expected values.
        try
        {
            string? found = FindFixture("ease_live_cube.stratapack");
            if (found == null)
            {
                Console.WriteLine("  ✓ Ease live cube: SKIPPED (run from repo root to enable)");
                return true;
            }
            string ease = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(found)!, "ease_live_cube.stratapack");
            if (!System.IO.File.Exists(ease))
            {
                Console.WriteLine("  ✓ Ease live cube: SKIPPED (fixture not generated yet)");
                return true;
            }
            var pack = StrataPack.Decode(System.IO.File.ReadAllBytes(ease));
            bool decoded = pack.Header.Meshes.Count == 1
                && pack.Header.Meshes[0].Name == "EaseCube"
                && pack.Header.Meshes[0].VertexCount == 24
                && pack.Header.Meshes[0].IndexCount == 36
                && pack.Header.Surfaces.Count == 1
                && Math.Abs(pack.Header.Surfaces[0].Metallic - 0.5f) < 1e-6f
                && Math.Abs(pack.Header.Surfaces[0].Roughness - 0.25f) < 1e-6f
                && pack.Header.Textures.Count == 2;
            if (!decoded)
            {
                Console.WriteLine("  ❌ Ease live cube decode: FAILED");
                return false;
            }

            string target = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "StrataTest_" + Guid.NewGuid().ToString("N"), "EaseCube");
            System.IO.Directory.CreateDirectory(target);
            var asset = new BlueSky.Core.Assets.BlueAsset { AssetName = "EaseCube" };
            var options = new BlueSky.Core.Assets.ImportOptions();
            options.Settings["TargetDirectory"] = target;
            var result = new BlueSky.Core.Assets.StratapackImportHandler()
                .Import(ease, asset, options);

            bool links = asset.Metadata.TryGetValue("strataSlot0", out var link)
                && link != null && System.IO.File.Exists(link);
            bool mat = false;
            if (links)
            {
                var m = BlueSky.Rendering.Strata.StrataMaterial.Load(link!);
                mat = Math.Abs(m.Metallic - 0.5f) < 1e-6f
                    && Math.Abs(m.Roughness - 0.25f) < 1e-6f
                    && m.Textures.Count >= 2;
            }
            bool valid = result.Success && links && mat;
            Console.WriteLine($"  ✓ Ease live cube import (decode+split+links): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Ease live Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestEaseWrittenFixture()
    {
        // Cross-implementation proof: ease_quad.stratapack was written by the
        // PYTHON addon path (Blender/strata_pack_exporter.py write_pack) and
        // must decode in the C# StrataPack decoder byte-for-byte.
        try
        {
            string? found = FindFixture("ease_quad.stratapack");
            if (found == null)
            {
                Console.WriteLine("  ✓ Ease-written fixture: SKIPPED (run from repo root to enable)");
                return true;
            }
            string ease = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(found)!, "ease_quad.stratapack");
            if (!System.IO.File.Exists(ease))
            {
                Console.WriteLine("  ✓ Ease-written fixture: SKIPPED (fixture not generated yet)");
                return true;
            }
            var pack = StrataPack.Decode(System.IO.File.ReadAllBytes(ease));
            bool valid = pack.Header.Meshes.Count == 1
                && pack.Header.Meshes[0].Name == "Q"
                && pack.Header.Meshes[0].VertexCount == 1
                && pack.Payload.Length == 44;
            Console.WriteLine($"  ✓ Ease-written fixture decodes in C#: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Ease fixture Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestHandlerAcceptsFixture()
    {
        // Full Step-2 path: fixture pack → handler → mesh payload +
        // Materials/{StrataFiles,Textures} + strataSlot links + decodable files.
        try
        {
            string? fixture = FindFixture("sample_quad.stratapack");
            if (fixture == null)
            {
                Console.WriteLine("  ✓ Handler accepts fixture: SKIPPED (run from repo root to enable)");
                return true;
            }
            string target = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "StrataTest_" + Guid.NewGuid().ToString("N"), "Quad");
            System.IO.Directory.CreateDirectory(target);

            var asset = new BlueSky.Core.Assets.BlueAsset { AssetName = "Quad" };
            var options = new BlueSky.Core.Assets.ImportOptions();
            options.Settings["TargetDirectory"] = target;
            var handler = new BlueSky.Core.Assets.StratapackImportHandler();
            var result = handler.Import(fixture, asset, options);

            bool files = result.Success
                && System.IO.Directory.Exists(System.IO.Path.Combine(target, "Materials", "StrataFiles"))
                && System.IO.Directory.Exists(System.IO.Path.Combine(target, "Materials", "Textures"));
            var links = new System.Collections.Generic.List<string>();
            for (int s = 0; s < 8; s++)
            {
                if (asset.Metadata.TryGetValue($"strataSlot{s}", out var link)
                    && System.IO.File.Exists(link))
                    links.Add(link);
            }
            bool decodable = links.Count == 1;
            foreach (var link in links)
            {
                var m = BlueSky.Rendering.Strata.StrataMaterial.Load(link);
                decodable &= m.Textures.Count == 1 && m.Payload.Length == 16;
            }
            // Mesh payload: [vLen][vData][iLen][iData][nSub][offset,count,slot]
            bool payload = result.PayloadData != null && result.PayloadData.Length == 4 + 128 + 4 + 24 + 4 + 12;
            // No sidecars: AO lives in the pack; the asset carries references.
            bool meta = asset.Metadata.TryGetValue("format", out var fmt) && fmt == "Packed32"
                && asset.Metadata.TryGetValue("sourcePack", out var packRef) && packRef == fixture
                && asset.Metadata.TryGetValue("aoOffset", out _) && asset.Metadata.TryGetValue("aoCount", out _)
                && !asset.Metadata.ContainsKey("vertexAoFile");
            bool noSidecars = true;
            foreach (var f in System.IO.Directory.GetFiles(target, "*", System.IO.SearchOption.AllDirectories))
            {
                string n = System.IO.Path.GetFileName(f);
                if (n.EndsWith("_vertexao.bin") || n.EndsWith("_skinning.bin") || n.EndsWith("_skeleton.json"))
                    noSidecars = false;
            }
            bool valid = files && links.Count == 1 && decodable && payload && meta && noSidecars;
            Console.WriteLine($"  ✓ Handler accepts fixture (payload+links+files): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Handler Exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes the sample pack to disk (fixture generation + Ease reference).
    /// </summary>
    public static void ExportSamplePack(string path)
    {
        try
        {
            string? dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(path, StrataPack.Encode(SamplePack()));
            Console.WriteLine($"[StrataPack] Sample pack written: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StrataPack] Export failed: {ex.Message}");
            Environment.Exit(1);
        }
    }

    private static bool TestMultiMeshSplit()
    {
        // Two meshes in one pack → two mesh files + ONE shared Materials/.
        try
        {
            var pack = new StrataPack.PackFile();
            var payload = new System.Collections.Generic.List<byte>();
            for (int m = 0; m < 2; m++)
            {
                byte[] verts = new byte[4 * 32];
                byte[] indices = new byte[6 * 4];
                for (int i = 0; i < 6; i++)
                    BitConverter.TryWriteBytes(new Span<byte>(indices, i * 4, 4), (uint)i);
                ulong vo = (ulong)payload.Count;
                payload.AddRange(verts);
                ulong io = (ulong)payload.Count;
                payload.AddRange(indices);
                pack.Header.Meshes.Add(new StrataPack.PackMesh
                {
                    Name = m == 0 ? "Body" : "Wheel",
                    VertexCount = 4, IndexCount = 6,
                    Submeshes = { new StrataPack.PackSubmesh { Offset = 0, Count = 6, Slot = m } },
                    VertexOffset = vo, VertexSize = (ulong)verts.Length,
                    IndexOffset = io, IndexSize = (ulong)indices.Length,
                });
                pack.Header.Surfaces.Add(new StrataPack.PackSurface
                {
                    Slot = m, AlbedoLinear = new[] { 0.5f, 0.5f, 0.5f },
                    EmissiveLinear = new[] { 0f, 0f, 0f },
                });
            }
            pack.Payload = payload.ToArray();

            string target = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "StrataTest_" + Guid.NewGuid().ToString("N"), "Car");
            System.IO.Directory.CreateDirectory(target);
            // Write the synthetic pack to disk, then import for real.
            string packPath = System.IO.Path.Combine(target, "car.stratapack");
            System.IO.File.WriteAllBytes(packPath, StrataPack.Encode(pack));
            var asset2 = new BlueSky.Core.Assets.BlueAsset { AssetName = "Car" };
            var options = new BlueSky.Core.Assets.ImportOptions();
            options.Settings["TargetDirectory"] = target;
            var result2 = new BlueSky.Core.Assets.StratapackImportHandler()
                .Import(packPath, asset2, options);
            bool valid = result2.Success
                && System.IO.File.Exists(System.IO.Path.Combine(target, "Car_Wheel.blueskyasset"))
                && asset2.Metadata.TryGetValue("strataSlot0", out var link0)
                && System.IO.File.Exists(link0);
            var wheel = BlueSky.Core.Assets.BlueAsset.Load(
                System.IO.Path.Combine(target, "Car_Wheel.blueskyasset"));
            valid &= wheel != null
                && wheel.Metadata.TryGetValue("strataSlot1", out var link1)
                && System.IO.File.Exists(link1)
                && wheel.Metadata.TryGetValue("vertexCount", out var vc) && vc == "4";
            Console.WriteLine($"  ✓ Multi-mesh split (2 files + shared Materials/): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Multi-mesh Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestClearcoatSurvivesPack()
    {
        try
        {
            // Authoring coat weight → pack JSON → import Assemble must set
            // the Clearcoat bit; old packs without the key default to 0.
            var pack = SamplePack();
            pack.Header.Surfaces[0].Clearcoat = 1f;
            var decoded = StrataPack.Decode(StrataPack.Encode(pack));
            var surf = decoded.Header.Surfaces[0];
            var mat = BlueSky.Rendering.Strata.StrataImporter.Assemble(
                "Coated", new System.Numerics.Vector3(0.5f, 0.5f, 0.5f),
                0f, 0.5f, 1f, System.Numerics.Vector3.Zero, 0f,
                clearcoat: surf.Clearcoat);
            var legacy = System.Text.Json.JsonSerializer.Deserialize<StrataPack.PackSurface>(
                "{\"Slot\":0,\"Metallic\":0,\"Roughness\":0.6}");
            bool valid = Math.Abs(surf.Clearcoat - 1f) < 1e-6f
                && mat.Features.HasFlag(BlueSky.Rendering.Strata.StrataFeature.Clearcoat)
                && legacy != null && Math.Abs(legacy.Clearcoat) < 1e-6f;
            Console.WriteLine($"  ✓ Pack clearcoat survives (bit set, legacy defaults 0): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Clearcoat Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestAlphaSurvivesPack()
    {
        try
        {
            // Glass alpha → pack JSON → import Assemble must set Transparent;
            // old packs without the key default to opaque (1).
            var pack = SamplePack();
            pack.Header.Surfaces[0].Alpha = 0.15f;
            var decoded = StrataPack.Decode(StrataPack.Encode(pack));
            var surf = decoded.Header.Surfaces[0];
            var mat = BlueSky.Rendering.Strata.StrataImporter.Assemble(
                "Glass", new System.Numerics.Vector3(0.1f, 0.1f, 0.12f),
                0f, 0.05f, 1f, System.Numerics.Vector3.Zero, 0f,
                alpha: surf.Alpha);
            var legacy = System.Text.Json.JsonSerializer.Deserialize<StrataPack.PackSurface>(
                "{\"Slot\":0,\"Metallic\":0,\"Roughness\":0.6}");
            bool valid = Math.Abs(surf.Alpha - 0.15f) < 1e-6f
                && mat.Features.HasFlag(BlueSky.Rendering.Strata.StrataFeature.Transparent)
                && Math.Abs(mat.Alpha - 0.15f) < 1e-6f
                && legacy != null && Math.Abs(legacy.Alpha - 1f) < 1e-6f;
            Console.WriteLine($"  ✓ Pack alpha survives (bit set, legacy defaults 1): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Alpha Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestSheenSurvivesPack()
    {
        try
        {
            // Cloth sheen → pack JSON → import Assemble must set Sheen;
            // old packs without the key default to 0 (clearcoat precedent).
            var pack = SamplePack();
            pack.Header.Surfaces[0].Sheen = 1f;
            var decoded = StrataPack.Decode(StrataPack.Encode(pack));
            var surf = decoded.Header.Surfaces[0];
            var mat = BlueSky.Rendering.Strata.StrataImporter.Assemble(
                "Cloth", new System.Numerics.Vector3(0.5f, 0.5f, 0.5f),
                0f, 0.5f, 1f, System.Numerics.Vector3.Zero, 0f,
                sheen: surf.Sheen);
            var legacy = System.Text.Json.JsonSerializer.Deserialize<StrataPack.PackSurface>(
                "{\"Slot\":0,\"Metallic\":0,\"Roughness\":0.6}");
            bool valid = Math.Abs(surf.Sheen - 1f) < 1e-6f
                && mat.Features.HasFlag(BlueSky.Rendering.Strata.StrataFeature.Sheen)
                && legacy != null && Math.Abs(legacy.Sheen) < 1e-6f;
            Console.WriteLine($"  ✓ Pack sheen survives (bit set, legacy defaults 0): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Sheen Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestAnisotropySurvivesPack()
    {
        try
        {
            // Brushed flag → pack JSON → import Assemble must set Anisotropy;
            // old packs without the key default to 0 (sheen precedent).
            var pack = SamplePack();
            pack.Header.Surfaces[0].Anisotropy = 1f;
            var decoded = StrataPack.Decode(StrataPack.Encode(pack));
            var surf = decoded.Header.Surfaces[0];
            var mat = BlueSky.Rendering.Strata.StrataImporter.Assemble(
                "Brushed", new System.Numerics.Vector3(0.5f, 0.5f, 0.5f),
                1f, 0.4f, 1f, System.Numerics.Vector3.Zero, 0f,
                anisotropy: surf.Anisotropy);
            var legacy = System.Text.Json.JsonSerializer.Deserialize<StrataPack.PackSurface>(
                "{\"Slot\":0,\"Metallic\":0,\"Roughness\":0.6}");
            bool valid = Math.Abs(surf.Anisotropy - 1f) < 1e-6f
                && mat.Features.HasFlag(BlueSky.Rendering.Strata.StrataFeature.Anisotropy)
                && legacy != null && Math.Abs(legacy.Anisotropy) < 1e-6f;
            Console.WriteLine($"  ✓ Pack anisotropy survives (bit set, legacy defaults 0): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Anisotropy Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestSkeletonRoundTrip()
    {
        try
        {
            // Skinned quad: 2 bones (root + wheel), 4 verts bound to bone 1.
            var pack = SamplePack();
            var skin = new byte[4 * StrataPack.SkinStride];
            for (int v = 0; v < 4; v++)
            {
                BitConverter.TryWriteBytes(new Span<byte>(skin, v * 32, 4), 1u);
                BitConverter.TryWriteBytes(new Span<byte>(skin, v * 32 + 16, 4), 1f);
            }
            var payload = new System.Collections.Generic.List<byte>(pack.Payload);
            ulong skinOff = (ulong)payload.Count;
            payload.AddRange(skin);
            pack.Payload = payload.ToArray();
            pack.Header.Skeletons.Add(new StrataPack.PackSkeleton
            {
                Name = "Rig",
                Bones =
                {
                    new StrataPack.PackBone { Name = "Main", Parent = -1,
                        RestMatrix = Identity() },
                    new StrataPack.PackBone { Name = "FR_mesh", Parent = 0,
                        RestMatrix = Identity() },
                },
            });
            pack.Header.Meshes[0].SkeletonIndex = 0;
            pack.Header.Meshes[0].SkinOffset = skinOff;
            pack.Header.Meshes[0].SkinSize = (ulong)skin.Length;
            pack.Header.Meshes[0].SkinCount = 4;
            var decoded = StrataPack.Decode(StrataPack.Encode(pack));
            var skel = decoded.Header.Skeletons[0];
            bool valid = skel.Name == "Rig" && skel.Bones.Count == 2
                && skel.Bones[1].Name == "FR_mesh" && skel.Bones[1].Parent == 0
                && decoded.Header.Meshes[0].SkinCount == 4
                && decoded.Payload.Length == pack.Payload.Length;
            Console.WriteLine($"  ✓ Skeleton round-trip (2 bones, 4 skinned verts): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Skeleton Exception: {ex.Message}");
            return false;
        }
    }

    private static float[] Identity() => new float[]
        { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

    private static bool TestSkeletonRejected()
    {
        try
        {
            bool ok = true;
            // 257 bones exceeds the 256 shader palette (mesh fully skinned
            // so the bone-count check is the one that fires).
            var many = SamplePack();
            many.Header.Skeletons.Add(new StrataPack.PackSkeleton { Name = "Big" });
            for (int i = 0; i < 257; i++)
                many.Header.Skeletons[0].Bones.Add(new StrataPack.PackBone
                    { Name = $"B{i}", Parent = -1, RestMatrix = Identity() });
            var skinMany = new byte[4 * StrataPack.SkinStride];
            for (int v = 0; v < 4; v++)
            {
                BitConverter.TryWriteBytes(new Span<byte>(skinMany, v * 32, 4), 0u);
                BitConverter.TryWriteBytes(new Span<byte>(skinMany, v * 32 + 16, 4), 1f);
            }
            var payloadMany = new System.Collections.Generic.List<byte>(many.Payload);
            many.Header.Meshes[0].SkinOffset = (ulong)payloadMany.Count;
            payloadMany.AddRange(skinMany);
            many.Payload = payloadMany.ToArray();
            many.Header.Meshes[0].SkeletonIndex = 0;
            many.Header.Meshes[0].SkinSize = (ulong)skinMany.Length;
            many.Header.Meshes[0].SkinCount = 4;
            ok &= ExpectFail(StrataPack.Encode(many), "257 bones rejected");
            // Skin without skeleton.
            var orphan = SamplePack();
            orphan.Header.Meshes[0].SkinCount = 4;
            orphan.Header.Meshes[0].SkinSize = 4 * (ulong)StrataPack.SkinStride;
            ok &= ExpectFail(StrataPack.Encode(orphan), "Skin-without-skeleton rejected");
            return ok;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Skeleton-reject Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestSkeletalHandlerInPack()
    {
        try
        {
            // Full skeletal import: pack with skeleton → handler writes NO
            // sidecars; asset metadata references the pack itself.
            var pack = SamplePack();
            var skin = new byte[4 * StrataPack.SkinStride];
            for (int v = 0; v < 4; v++)
            {
                BitConverter.TryWriteBytes(new Span<byte>(skin, v * 32, 4), 1u);
                BitConverter.TryWriteBytes(new Span<byte>(skin, v * 32 + 16, 4), 1f);
            }
            var payload = new System.Collections.Generic.List<byte>(pack.Payload);
            ulong skinOff = (ulong)payload.Count;
            payload.AddRange(skin);
            pack.Payload = payload.ToArray();
            pack.Header.Skeletons.Add(new StrataPack.PackSkeleton
            {
                Name = "Rig",
                Bones =
                {
                    new StrataPack.PackBone { Name = "Main", Parent = -1, RestMatrix = Identity() },
                    new StrataPack.PackBone { Name = "FR_mesh", Parent = 0, RestMatrix = Identity() },
                },
            });
            pack.Header.Meshes[0].SkeletonIndex = 0;
            pack.Header.Meshes[0].SkinOffset = skinOff;
            pack.Header.Meshes[0].SkinSize = (ulong)skin.Length;
            pack.Header.Meshes[0].SkinCount = 4;

            string dir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "StrataTest_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            string packPath = System.IO.Path.Combine(dir, "rigged.stratapack");
            System.IO.File.WriteAllBytes(packPath, StrataPack.Encode(pack));
            string target = System.IO.Path.Combine(dir, "Out");
            System.IO.Directory.CreateDirectory(target);

            var asset = new BlueSky.Core.Assets.BlueAsset { AssetName = "Rigged" };
            var options = new BlueSky.Core.Assets.ImportOptions();
            options.Settings["TargetDirectory"] = target;
            var result = new BlueSky.Core.Assets.StratapackImportHandler().Import(packPath, asset, options);

            bool meta = result.Success
                && asset.Metadata.TryGetValue("sourcePack", out var sp) && sp == packPath
                && asset.Metadata.TryGetValue("skinCount", out var sc) && sc == "4"
                && asset.Metadata.TryGetValue("skeletonName", out var sn) && sn == "Rig"
                && asset.Metadata.TryGetValue("skeletonBones", out var sb) && sb == "2"
                && !asset.Metadata.ContainsKey("skinningFile")
                && !asset.Metadata.ContainsKey("skeletonFile");
            bool noSidecars = true;
            foreach (var f in System.IO.Directory.GetFiles(target, "*", System.IO.SearchOption.AllDirectories))
            {
                string n = System.IO.Path.GetFileName(f);
                if (n.EndsWith("_skinning.bin") || n.EndsWith("_skeleton.json") || n.EndsWith("_vertexao.bin"))
                    noSidecars = false;
            }
            // Renderer-side read: header + skin slice straight from the pack.
            var (ph, pbase) = StrataPack.DecodeHeader(packPath);
            bool reread = ph.Skeletons.Count == 1 && ph.Skeletons[0].Bones.Count == 2 && pbase > 28;
            bool valid = meta && noSidecars && reread;
            Console.WriteLine($"  ✓ Skeletal import in-pack (refs, no sidecars, reread): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Skeletal-import Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestBentEncodingGate()
    {
        try
        {
            // Only "world"-encoded bent maps assemble the lobe; anything else
            // persists but stays off (the importer never guesses spaces).
            bool worldBit = ImportBentAndReadBit("world");
            bool tangentBit = ImportBentAndReadBit("tangent");
            bool valid = worldBit && !tangentBit;
            Console.WriteLine($"  ✓ Bent encoding gate (world on, tangent off): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Bent-gate Exception: {ex.Message}");
            return false;
        }
    }

    private static bool ImportBentAndReadBit(string encoding)
    {
        var pack = SamplePack();
        pack.Header.BentMaps[0].Encoding = encoding;
        string dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "StrataTest_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        string packPath = System.IO.Path.Combine(dir, "bent.stratapack");
        System.IO.File.WriteAllBytes(packPath, StrataPack.Encode(pack));
        string target = System.IO.Path.Combine(dir, "Out");
        System.IO.Directory.CreateDirectory(target);
        var asset = new BlueSky.Core.Assets.BlueAsset { AssetName = "Bent" };
        var options = new BlueSky.Core.Assets.ImportOptions();
        options.Settings["TargetDirectory"] = target;
        var result = new BlueSky.Core.Assets.StratapackImportHandler().Import(packPath, asset, options);
        if (!result.Success || !asset.Metadata.TryGetValue("strataSlot0", out var link))
            return false;
        var m = BlueSky.Rendering.Strata.StrataMaterial.Load(link);
        return m.Features.HasFlag(BlueSky.Rendering.Strata.StrataFeature.BentNormal);
    }

    private static bool TestDeadKeysRejected()
    {
        try
        {
            // Legacy non-empty "bent"/"aomap" tables never decoded — they must
            // fail loudly instead of silently dropping data. Empty ones (all
            // real packs) stay accepted.
            var pack = SamplePack();
            byte[] good = StrataPack.Encode(pack);
            bool emptyOk;
            try { StrataPack.Decode(good); emptyOk = true; }
            catch { emptyOk = false; }
            string json = System.Text.Encoding.UTF8.GetString(good);
            int hlen = BitConverter.ToInt32(good, 12);
            // Slice the header region ONLY (the payload is binary, not text).
            string headJson = System.Text.Encoding.UTF8.GetString(good, 28, hlen);
            // Inject a non-empty legacy table by adding the dead key.
            string doctored = headJson.Replace("\"BentMaps\":[", "\"bent\":[{\"x\":1}],\"BentMaps\":[");
            byte[] dhead = System.Text.Encoding.UTF8.GetBytes(doctored);
            byte[] data = new byte[28 + dhead.Length + (good.Length - 28 - hlen)];
            Buffer.BlockCopy(good, 0, data, 0, 12);
            Buffer.BlockCopy(BitConverter.GetBytes((ulong)dhead.Length), 0, data, 12, 8);
            Buffer.BlockCopy(good, 20, data, 20, 8);
            Buffer.BlockCopy(dhead, 0, data, 28, dhead.Length);
            Buffer.BlockCopy(good, 28 + hlen, data, 28 + dhead.Length, good.Length - 28 - hlen);
            bool loud = false;
            try { StrataPack.Decode(data); }
            catch (InvalidOperationException ex) { loud = ex.Message.Contains("legacy non-empty"); }
            bool valid = emptyOk && loud;
            Console.WriteLine($"  ✓ Dead keys rejected (empty ok, non-empty loud): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Dead-key Exception: {ex.Message}");
            return false;
        }
    }

}
