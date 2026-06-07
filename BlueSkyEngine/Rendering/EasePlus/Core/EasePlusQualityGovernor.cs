using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BlueSky.Rendering.EasePlus;

/// <summary>
/// Ease+ Quality Governor — Adaptive per-pass frame budget manager.
/// Dynamically adjusts rendering quality to maintain target FPS on HD 3000.
/// </summary>
public class EasePlusQualityGovernor
{
    private readonly Stopwatch _frameTimer = new();
    private readonly Dictionary<string, Stopwatch> _passTimers = new();
    private readonly Dictionary<string, float> _passTimes = new();
    private readonly Queue<float> _frameHistory = new();

    private float _targetFrameTime = 16.67f; // 60fps
    private int _frameCount;

    // ── Quality Knobs ────────────────────────────────────────────────────
    public float LightingResolutionScale { get; private set; } = 0.5f; // Half-res
    public int SDFMaxSteps { get; private set; } = 32;
    public int ShadowMapSize { get; private set; } = 1024;
    public bool EnableSDF { get; private set; } = true;
    public bool EnableGI { get; private set; } = true;
    public bool EnableFXAA { get; private set; } = true;
    public float DrawDistance { get; private set; } = 200f;

    public void SetTargetFPS(int fps) => _targetFrameTime = 1000f / fps;

    public void ConfigureForLegacyGpu()
    {
        _targetFrameTime = 33.33f; // Prefer stable 30fps over unstable 60fps on HD 3000-class hardware.
        LightingResolutionScale = 0.25f;
        SDFMaxSteps = 8;
        ShadowMapSize = 256;
        EnableSDF = false;
        EnableGI = false;
        EnableFXAA = false;
        DrawDistance = 90f;
        Console.WriteLine("[Ease+Gov] Legacy GPU profile: quarter-res lighting, SDF/GI off, 30fps budget");
    }

    public void BeginFrame() => _frameTimer.Restart();

    public void EndFrame()
    {
        _frameTimer.Stop();
        float ms = (float)_frameTimer.Elapsed.TotalMilliseconds;
        _frameHistory.Enqueue(ms);
        if (_frameHistory.Count > 60) _frameHistory.Dequeue();
        _frameCount++;

        if (_frameCount % 60 == 0) AdjustQuality();
    }

    public void BeginPass(string name)
    {
        if (!_passTimers.ContainsKey(name)) _passTimers[name] = new Stopwatch();
        _passTimers[name].Restart();
    }

    public void EndPass(string name)
    {
        if (_passTimers.TryGetValue(name, out var sw))
        {
            sw.Stop();
            _passTimes[name] = (float)sw.Elapsed.TotalMilliseconds;
        }
    }

    private void AdjustQuality()
    {
        float avg = GetAverage();
        if (avg > _targetFrameTime * 1.3f)
        {
            // Critical — aggressive downgrade
            if (EnableSDF) { SDFMaxSteps = Math.Max(8, SDFMaxSteps - 8); }
            if (SDFMaxSteps <= 8) EnableSDF = false;
            ShadowMapSize = Math.Max(256, ShadowMapSize / 2);
            DrawDistance = Math.Max(50, DrawDistance - 30);
        }
        else if (avg > _targetFrameTime * 1.1f)
        {
            SDFMaxSteps = Math.Max(16, SDFMaxSteps - 4);
            DrawDistance = Math.Max(80, DrawDistance - 15);
        }
        else if (avg < _targetFrameTime * 0.7f)
        {
            // Headroom — upgrade
            SDFMaxSteps = Math.Min(64, SDFMaxSteps + 4);
            if (!EnableSDF && SDFMaxSteps >= 16) EnableSDF = true;
            ShadowMapSize = Math.Min(2048, ShadowMapSize * 2);
            DrawDistance = Math.Min(500, DrawDistance + 20);
        }
    }

    private float GetAverage()
    {
        if (_frameHistory.Count == 0) return 0;
        float sum = 0;
        foreach (var t in _frameHistory) sum += t;
        return sum / _frameHistory.Count;
    }

    public void LogStats()
    {
        float avg = GetAverage();
        Console.WriteLine($"[Ease+Gov] {avg:F1}ms ({1000f / Math.Max(avg, 0.1f):F0}fps) " +
            $"SDF:{(EnableSDF ? $"{SDFMaxSteps}steps" : "OFF")} " +
            $"Shadow:{ShadowMapSize} Draw:{DrawDistance:F0}m");
        foreach (var (name, time) in _passTimes)
            Console.WriteLine($"  {name}: {time:F2}ms");
    }
}
