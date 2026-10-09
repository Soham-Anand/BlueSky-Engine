using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace BlueSky.Rendering.Strata;

/// <summary>
/// Strata benchmark harness — hard numbers for every material step.
/// Records CPU frame time, per-section times, draw/triangle/texture-bind
/// counters, VRAM estimates, and rolling avg / best-1% / worst-1% stats.
/// GPU time on Metal: Submit() blocks (waitUntilCompleted), so the "submit"
/// section is GPU-inclusive there; on other backends it is CPU-submit only.
/// Screenshots are requested here and fulfilled by the backend capture path.
/// </summary>
public static class StrataBenchmark
{
    private const int HistorySize = 120;

    private static readonly Stopwatch _frameTimer = new();
    private static readonly Queue<float> _frameHistory = new(HistorySize);
    private static readonly Dictionary<string, Stopwatch> _sectionTimers = new();
    private static readonly Dictionary<string, float> _sectionTimes = new();
    private static readonly Dictionary<string, float> _sectionMax = new();

    // Per-frame counters (reset in BeginFrame)
    public static int MeshDraws { get; private set; }
    public static long MeshTris { get; private set; }
    public static int TextureBinds { get; private set; }
    public static int ShadowDraws { get; private set; }

    // VRAM estimate (bytes tracked at bind/upload sites + streaming stats)
    public static long TextureBytesTracked { get; private set; }

    public static float CurrentFrameMs { get; private set; }
    public static float AverageFrameMs { get; private set; }
    public static float Best1PercentMs { get; private set; }
    public static float Worst1PercentMs { get; private set; }
    public static float FPS => CurrentFrameMs > 0 ? 1000f / CurrentFrameMs : 0f;

    public static long FrameIndex { get; private set; }

    // Screenshot request (consumed by the backend capture path once per frame)
    private static string? _pendingScreenshotPath;
    public static void RequestScreenshot(string path) => _pendingScreenshotPath = path;
    public static string? ConsumeScreenshotRequest()
    {
        var p = _pendingScreenshotPath;
        _pendingScreenshotPath = null;
        return p;
    }

    public static void BeginFrame()
    {
        MeshDraws = 0;
        MeshTris = 0;
        TextureBinds = 0;
        ShadowDraws = 0;
        _frameTimer.Restart();
    }

    public static void EndFrame()
    {
        _frameTimer.Stop();
        CurrentFrameMs = (float)_frameTimer.Elapsed.TotalMilliseconds;
        FrameIndex++;

        _frameHistory.Enqueue(CurrentFrameMs);
        if (_frameHistory.Count > HistorySize)
            _frameHistory.Dequeue();

        if (_frameHistory.Count > 0)
        {
            float sum = 0f;
            var sorted = new List<float>(_frameHistory);
            sorted.Sort();
            foreach (var t in sorted) sum += t;
            AverageFrameMs = sum / sorted.Count;
            int onePercent = Math.Max(1, sorted.Count / 100);
            float bestSum = 0f, worstSum = 0f;
            for (int i = 0; i < onePercent; i++) bestSum += sorted[i];
            for (int i = sorted.Count - onePercent; i < sorted.Count; i++) worstSum += sorted[i];
            Best1PercentMs = bestSum / onePercent;
            Worst1PercentMs = worstSum / onePercent;
        }
    }

    public static void BeginSection(string name)
    {
        if (!_sectionTimers.TryGetValue(name, out var timer))
        {
            timer = new Stopwatch();
            _sectionTimers[name] = timer;
        }
        timer.Restart();
    }

    public static void EndSection(string name)
    {
        if (_sectionTimers.TryGetValue(name, out var timer))
        {
            timer.Stop();
            float ms = (float)timer.Elapsed.TotalMilliseconds;
            _sectionTimes[name] = ms;
            if (!_sectionMax.TryGetValue(name, out var max) || ms > max)
                _sectionMax[name] = ms;
        }
    }

    public static float GetSectionTime(string name)
        => _sectionTimes.TryGetValue(name, out var t) ? t : 0f;

    public static void RecordMeshDraw(long triangleCount)
    {
        MeshDraws++;
        MeshTris += triangleCount;
    }

    public static void RecordTextureBind(int count = 1) => TextureBinds += count;
    public static void RecordShadowDraw() => ShadowDraws++;
    public static void RecordTextureBytes(long bytes) => TextureBytesTracked += bytes;

    /// <summary>
    /// Static texture-fetch census: counts hard-coded sampling sites in a
    /// shader source file. Honest upper bound, not a dynamic count.
    /// </summary>
    public static int CountFetchSites(string shaderPath)
    {
        try
        {
            string src = File.ReadAllText(shaderPath);
            int n = 0, idx = 0;
            while ((idx = src.IndexOf(".sample(", idx, StringComparison.Ordinal)) >= 0) { n++; idx++; }
            idx = 0;
            while ((idx = src.IndexOf(".Sample", idx, StringComparison.Ordinal)) >= 0) { n++; idx++; }
            idx = 0;
            while ((idx = src.IndexOf("sample_compare(", idx, StringComparison.Ordinal)) >= 0) { n++; idx++; }
            return n;
        }
        catch
        {
            return -1;
        }
    }

    public static string GetSummary()
    {
        return $"FPS: {FPS:F1} | Frame: {CurrentFrameMs:F2}ms (avg: {AverageFrameMs:F2}ms, " +
               $"best1%: {Best1PercentMs:F2}ms, worst1%: {Worst1PercentMs:F2}ms) | " +
               $"MeshDraws: {MeshDraws} | Tris: {MeshTris} | TexBinds: {TextureBinds} | " +
               $"ShadowDraws: {ShadowDraws} | TexBytes: {TextureBytesTracked / 1024}KB | " +
               $"Mesh: {GetSectionTime("mesh"):F2}ms | Submit: {GetSectionTime("submit"):F2}ms";
    }

    /// <summary>
    /// Appends one benchmark line to Benchmarks/strata_bench.log (created on demand).
    /// Call once per proof step (or every N frames) — not every frame.
    /// </summary>
    public static void LogReport(string label)
    {
        try
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Benchmarks");
            Directory.CreateDirectory(dir);
            string line = $"{DateTime.UtcNow:O} | {label} | {GetSummary()}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(dir, "strata_bench.log"), line);
            Console.WriteLine("[StrataBench] " + line.TrimEnd());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StrataBench] Log failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes BGRA8 pixels as a 32-bit BMP (rows flipped to bottom-up).
    /// Zero dependencies — benchmark screenshots without an image library.
    /// </summary>
    public static void WriteBmp32(string path, byte[] bgra, uint width, uint height)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            int rowBytes = (int)width * 4;
            int pixelBytes = rowBytes * (int)height;
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);
            bw.Write((ushort)0x4D42);          // BM
            bw.Write(54 + pixelBytes);         // file size
            bw.Write((ushort)0); bw.Write((ushort)0);
            bw.Write(54);                      // pixel offset
            bw.Write(40);                      // DIB header size
            bw.Write((int)width);
            bw.Write((int)height);             // positive = bottom-up
            bw.Write((ushort)1);               // planes
            bw.Write((ushort)32);              // bpp
            bw.Write(0);                       // BI_RGB
            bw.Write(pixelBytes);
            bw.Write(2835); bw.Write(2835);    // ppm
            bw.Write(0); bw.Write(0);
            for (int y = (int)height - 1; y >= 0; y--)
                bw.Write(bgra, y * rowBytes, rowBytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StrataBench] BMP write failed: {ex.Message}");
        }
    }
}
