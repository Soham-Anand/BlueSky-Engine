using System;
using System.IO;
using System.Numerics;
using System.Text;
using BlueSky.Core.Assets;
using BlueSky.Editor.UI;

namespace BlueSky.Tests;

public static class EditorStabilityTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Editor Stability Tests...");
        bool passed = true;
        passed &= NarrowSliderWidthIsSafe();
        passed &= TinyDockSplitterIsSafe();
        passed &= TruncatedAssetPayloadIsRejected();
        passed &= UnsupportedAssetVersionIsRejected();
        return passed;
    }

    private static bool NarrowSliderWidthIsSafe()
    {
        try
        {
            var ui = new EditorUI(320, 200);
            ui.BeginFrame(Vector2.Zero, false);
            ui.SetCursor(0, 0);
            float value = 0.5f;
            ui.Slider(ref value, 0, 1, -36.25f, 20);
            bool valid = ui.GetDrawCommands().Count > 0 &&
                         ui.GetDrawCommands()[0].Size.X >= 1f &&
                         float.IsFinite(value);
            Console.WriteLine($"  {(valid ? "✓" : "✗")} Narrow slider width: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Narrow slider width threw: {ex.Message}");
            return false;
        }
    }

    private static bool TinyDockSplitterIsSafe()
    {
        try
        {
            var docking = new DockingSystem(100, 100);
            docking.AddPanel("a", "A", (_, _) => { });
            docking.AddPanel("b", "B", (_, _) => { });
            docking.DockTo("a", DockPosition.Center);
            docking.DockTo("b", "a", DockPosition.Right);
            docking.Resize(10, 10);

            var ui = new EditorUI(20, 20);
            ui.BeginFrame(new Vector2(8, 5), true);
            docking.Update(ui, new Vector2(8, 5), true); // grab the splitter
            ui.BeginFrame(new Vector2(9, 5), true);
            docking.Update(ui, new Vector2(9, 5), true); // drag with undersized bounds

            Console.WriteLine("  ✓ Tiny dock splitter drag: PASSED");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Tiny dock splitter drag threw: {ex.Message}");
            return false;
        }
    }

    private static bool TruncatedAssetPayloadIsRejected()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bluesky-truncated-{Guid.NewGuid():N}.blueskyasset");
        try
        {
            byte[] json = Encoding.UTF8.GetBytes("{}");
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { (byte)'B', (byte)'S', (byte)'A', (byte)'S' });
                writer.Write(1);
                writer.Write(json.Length);
                writer.Write(json);
                writer.Write(1024); // Claims a payload that is not present in the file.
            }

            bool valid = BlueAsset.Load(path) == null && BlueAsset.LoadHeader(path) != null;
            Console.WriteLine($"  {(valid ? "✓" : "✗")} Truncated asset payload rejected: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Truncated asset payload test threw: {ex.Message}");
            return false;
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static bool UnsupportedAssetVersionIsRejected()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bluesky-version-{Guid.NewGuid():N}.blueskyasset");
        try
        {
            byte[] json = Encoding.UTF8.GetBytes("{}");
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { (byte)'B', (byte)'S', (byte)'A', (byte)'S' });
                writer.Write(2); // Only format version 1 is currently supported.
                writer.Write(json.Length);
                writer.Write(json);
            }

            bool valid = BlueAsset.Load(path) == null && BlueAsset.LoadHeader(path) == null;
            Console.WriteLine($"  {(valid ? "✓" : "✗")} Unsupported asset version rejected: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Unsupported asset version test threw: {ex.Message}");
            return false;
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
