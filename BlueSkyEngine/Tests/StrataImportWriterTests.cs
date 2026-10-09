using System;
using System.Collections.Generic;
using System.IO;
using BlueSky.Core.Assets;

namespace BlueSky.Tests;

public static class StrataImportWriterTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Strata Import Writer Tests...");
        bool passed = true;

        passed &= TestLayoutCreatesDirs();
        passed &= TestLinkRoundTrip();
        passed &= TestDecodeImageBytesInvalid();

        return passed;
    }

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "StrataTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static bool TestLayoutCreatesDirs()
    {
        try
        {
            string dir = TempDir();
            var (strata, textures) = StrataImportWriter.EnsureMaterialsLayout(dir);
            bool valid = Directory.Exists(strata) && Directory.Exists(textures)
                && strata.EndsWith(Path.Combine("Materials", "StrataFiles"))
                && textures.EndsWith(Path.Combine("Materials", "Textures"));
            Console.WriteLine($"  ✓ Materials/{{StrataFiles,Textures}} layout: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Layout Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestLinkRoundTrip()
    {
        try
        {
            var meta = new Dictionary<string, string>();
            StrataImportWriter.LinkSlot(meta, 2, "/x/slot_2.stratamat");
            bool valid = StrataImportWriter.TryGetSlotLink(meta, 2) == "/x/slot_2.stratamat"
                && StrataImportWriter.TryGetSlotLink(meta, 0) == null
                && StrataImportWriter.TryGetSlotLink(meta, 9) == null;
            Console.WriteLine($"  ✓ Slot link round-trip: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Link Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestDecodeImageBytesInvalid()
    {
        try
        {
            bool valid = StrataImportWriter.DecodeImageBytes(new byte[] { 0, 1, 2, 3 }) == null
                && StrataImportWriter.DecodeImageFile("/nonexistent/x.png") == null;
            Console.WriteLine($"  ✓ Invalid image decode → null: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Decode Exception: {ex.Message}");
            return false;
        }
    }
}
