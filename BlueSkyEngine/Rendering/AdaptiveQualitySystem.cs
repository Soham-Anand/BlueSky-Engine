using System;
using System.Diagnostics;
using System.Collections.Generic;
using NotBSRenderer;

namespace BlueSky.Rendering;

/// <summary>
/// Adaptive Quality System - The secret sauce for "Ultra Graphics on Integrated Graphics at 120fps"
/// 
/// Philosophy: "Fake it till you make it"
/// - Use every trick in the book to LOOK ultra while being cheap
/// - Prioritize what the player actually sees
/// - Aggressively optimize what they don't notice
/// - Dynamic quality scaling based on real-time performance
/// 
/// Techniques:
/// - Temporal upscaling (render at 50% res, reconstruct to 100%)
/// - Aggressive LOD with smooth transitions
/// - Async compute for parallel work
/// - Checkerboard rendering for expensive effects
/// - Smart culling (frustum, occlusion, distance)
/// - Variable rate shading (VRS) on supported hardware
/// - Fake details using clever shaders instead of geometry
/// </summary>
public class AdaptiveQualitySystem
{
    private readonly IRHIDevice _device;
    private readonly PerformanceMonitor _perfMonitor;
    private readonly TemporalUpscaler _upscaler;
    
    // Target performance
    private float _targetFrameTime = 1000.0f / 120.0f; // 8.33ms for 120fps
    private float _currentFrameTime = 0;
    
    // Quality settings (dynamically adjusted)
    private float _renderScale = 0.75f;        // Start at 75% resolution
    private int _shadowQuality = 2;            // 0-3 scale
    private int _effectQuality = 2;            // 0-3 scale
    private bool _useCheckerboard = false;
    private bool _useAsyncCompute = true;
    
    // Performance budget (in milliseconds)
    private const float GEOMETRY_BUDGET = 2.0f;
    private const float LIGHTING_BUDGET = 2.5f;
    private const float SHADOW_BUDGET = 1.5f;
    private const float POSTFX_BUDGET = 1.5f;
    private const float OVERHEAD_BUDGET = 0.83f;
    
    // Frame timing history
    private readonly Queue<float> _frameTimeHistory = new();
    private const int HISTORY_SIZE = 60; // 1 second at 60fps
    
    public AdaptiveQualitySystem(IRHIDevice device)
    {
        _device = device;
        _perfMonitor = new PerformanceMonitor();
        _upscaler = new TemporalUpscaler(device);
        
        DetectHardwareCapabilities();
        InitializeOptimalSettings();
    }
    
    /// <summary>
    /// Called at the start of each frame
    /// </summary>
    public void BeginFrame()
    {
        _perfMonitor.BeginFrame();
    }
    
    /// <summary>
    /// Called at the end of each frame - adjusts quality based on performance
    /// </summary>
    public void EndFrame()
    {
        _currentFrameTime = _perfMonitor.EndFrame();
        _frameTimeHistory.Enqueue(_currentFrameTime);
        
        if (_frameTimeHistory.Count > HISTORY_SIZE)
            _frameTimeHistory.Dequeue();
        
        // Adjust quality every 60 frames (1 second)
        if (_frameTimeHistory.Count >= HISTORY_SIZE)
        {
            AdjustQuality();
        }
    }
    
    /// <summary>
    /// Get current render resolution scale
    /// </summary>
    public float GetRenderScale() => _renderScale;
    
    /// <summary>
    /// Get effective render resolution
    /// </summary>
    public (uint width, uint height) GetRenderResolution(uint targetWidth, uint targetHeight)
    {
        return (
            (uint)(targetWidth * _renderScale),
            (uint)(targetHeight * _renderScale)
        );
    }
    
    /// <summary>
    /// Should use checkerboard rendering for expensive effects?
    /// </summary>
    public bool UseCheckerboard() => _useCheckerboard;
    
    /// <summary>
    /// Should use async compute for parallel work?
    /// </summary>
    public bool UseAsyncCompute() => _useAsyncCompute;
    
    /// <summary>
    /// Get shadow quality level (0-3)
    /// </summary>
    public int GetShadowQuality() => _shadowQuality;
    
    /// <summary>
    /// Get effect quality level (0-3)
    /// </summary>
    public int GetEffectQuality() => _effectQuality;
    
    /// <summary>
    /// Get LOD bias (higher = more aggressive culling)
    /// </summary>
    public float GetLODBias()
    {
        // If we're struggling, increase LOD bias to use lower detail models sooner
        float avgFrameTime = GetAverageFrameTime();
        if (avgFrameTime > _targetFrameTime * 1.2f)
            return 2.0f; // Very aggressive
        if (avgFrameTime > _targetFrameTime * 1.1f)
            return 1.5f; // Aggressive
        if (avgFrameTime > _targetFrameTime)
            return 1.0f; // Normal
        return 0.5f; // Can afford higher quality
    }
    
    /// <summary>
    /// Get maximum draw distance
    /// </summary>
    public float GetDrawDistance()
    {
        float avgFrameTime = GetAverageFrameTime();
        if (avgFrameTime > _targetFrameTime * 1.2f)
            return 50.0f;  // Very close
        if (avgFrameTime > _targetFrameTime * 1.1f)
            return 100.0f; // Close
        if (avgFrameTime > _targetFrameTime)
            return 200.0f; // Normal
        return 500.0f; // Far
    }
    
    /// <summary>
    /// Should cull small objects?
    /// </summary>
    public float GetSmallObjectCullThreshold()
    {
        float avgFrameTime = GetAverageFrameTime();
        if (avgFrameTime > _targetFrameTime * 1.2f)
            return 0.05f; // Cull objects < 5% screen space
        if (avgFrameTime > _targetFrameTime * 1.1f)
            return 0.02f; // Cull objects < 2% screen space
        return 0.01f; // Cull objects < 1% screen space
    }
    
    private void DetectHardwareCapabilities()
    {
        var caps = _device.Capabilities;
        
        // Check for async compute support
        _useAsyncCompute = caps.HasFlag(RHICapabilities.ComputeShaders);
        
        // Check for variable rate shading
        bool hasVRS = caps.HasFlag(RHICapabilities.VariableRateShading);
        
        Console.WriteLine($"[AdaptiveQuality] Hardware capabilities:");
        Console.WriteLine($"  - Async Compute: {_useAsyncCompute}");
        Console.WriteLine($"  - Variable Rate Shading: {hasVRS}");
    }
    
    private void InitializeOptimalSettings()
    {
        // Start conservative, then scale up if we have headroom
        _renderScale = 0.75f;
        _shadowQuality = 2;
        _effectQuality = 2;
        _useCheckerboard = true;
        
        Console.WriteLine($"[AdaptiveQuality] Target: {1000.0f / _targetFrameTime:F0} fps ({_targetFrameTime:F2}ms)");
        Console.WriteLine($"[AdaptiveQuality] Initial settings:");
        Console.WriteLine($"  - Render Scale: {_renderScale * 100:F0}%");
        Console.WriteLine($"  - Shadow Quality: {_shadowQuality}/3");
        Console.WriteLine($"  - Effect Quality: {_effectQuality}/3");
        Console.WriteLine($"  - Checkerboard: {_useCheckerboard}");
    }
    
    private void AdjustQuality()
    {
        float avgFrameTime = GetAverageFrameTime();
        float percentile99 = GetPercentile(0.99f); // 99th percentile (worst 1%)
        
        // We care about consistency - if 99th percentile is bad, lower quality
        float targetTime = _targetFrameTime;
        
        if (percentile99 > targetTime * 1.3f)
        {
            // Way too slow - aggressive reduction
            DecreaseQuality(2);
            Console.WriteLine($"[AdaptiveQuality] CRITICAL: {percentile99:F2}ms (target {targetTime:F2}ms) - Aggressive quality reduction");
        }
        else if (percentile99 > targetTime * 1.15f)
        {
            // Too slow - reduce quality
            DecreaseQuality(1);
            Console.WriteLine($"[AdaptiveQuality] Slow: {percentile99:F2}ms (target {targetTime:F2}ms) - Reducing quality");
        }
        else if (avgFrameTime < targetTime * 0.8f && percentile99 < targetTime * 0.95f)
        {
            // Lots of headroom - increase quality
            IncreaseQuality(1);
            Console.WriteLine($"[AdaptiveQuality] Fast: {avgFrameTime:F2}ms (target {targetTime:F2}ms) - Increasing quality");
        }
    }
    
    private void DecreaseQuality(int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            // Priority order: render scale > effects > shadows
            if (_renderScale > 0.5f)
            {
                _renderScale -= 0.05f; // Reduce by 5%
            }
            else if (_effectQuality > 0)
            {
                _effectQuality--;
            }
            else if (_shadowQuality > 0)
            {
                _shadowQuality--;
            }
            else if (!_useCheckerboard)
            {
                _useCheckerboard = true;
            }
        }
    }
    
    private void IncreaseQuality(int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            // Reverse priority: shadows > effects > render scale
            if (_shadowQuality < 3)
            {
                _shadowQuality++;
            }
            else if (_effectQuality < 3)
            {
                _effectQuality++;
            }
            else if (_renderScale < 1.0f)
            {
                _renderScale += 0.05f; // Increase by 5%
            }
            else if (_useCheckerboard)
            {
                _useCheckerboard = false;
            }
        }
    }
    
    private float GetAverageFrameTime()
    {
        if (_frameTimeHistory.Count == 0) return 0;
        
        float sum = 0;
        foreach (var time in _frameTimeHistory)
            sum += time;
        
        return sum / _frameTimeHistory.Count;
    }
    
    private float GetPercentile(float percentile)
    {
        if (_frameTimeHistory.Count == 0) return 0;
        
        var sorted = new List<float>(_frameTimeHistory);
        sorted.Sort();
        
        int index = (int)(sorted.Count * percentile);
        index = Math.Clamp(index, 0, sorted.Count - 1);
        
        return sorted[index];
    }
    
    /// <summary>
    /// Get performance report
    /// </summary>
    public string GetPerformanceReport()
    {
        float avg = GetAverageFrameTime();
        float p99 = GetPercentile(0.99f);
        float p1 = GetPercentile(0.01f);
        
        return $@"Performance Report:
  Average: {avg:F2}ms ({1000.0f / avg:F0} fps)
  Best 1%: {p1:F2}ms ({1000.0f / p1:F0} fps)
  Worst 1%: {p99:F2}ms ({1000.0f / p99:F0} fps)
  Target: {_targetFrameTime:F2}ms ({1000.0f / _targetFrameTime:F0} fps)
  
Current Settings:
  Render Scale: {_renderScale * 100:F0}%
  Shadow Quality: {_shadowQuality}/3
  Effect Quality: {_effectQuality}/3
  Checkerboard: {_useCheckerboard}
  LOD Bias: {GetLODBias():F1}x
  Draw Distance: {GetDrawDistance():F0}m";
    }
}

/// <summary>
/// Performance monitor using high-precision timing
/// </summary>
public class PerformanceMonitor
{
    private readonly Stopwatch _frameTimer = new();
    private readonly Dictionary<string, Stopwatch> _sectionTimers = new();
    private readonly Dictionary<string, float> _sectionTimes = new();
    
    public void BeginFrame()
    {
        _frameTimer.Restart();
    }
    
    public float EndFrame()
    {
        _frameTimer.Stop();
        return (float)_frameTimer.Elapsed.TotalMilliseconds;
    }
    
    public void BeginSection(string name)
    {
        if (!_sectionTimers.ContainsKey(name))
            _sectionTimers[name] = new Stopwatch();
        
        _sectionTimers[name].Restart();
    }
    
    public void EndSection(string name)
    {
        if (_sectionTimers.TryGetValue(name, out var timer))
        {
            timer.Stop();
            _sectionTimes[name] = (float)timer.Elapsed.TotalMilliseconds;
        }
    }
    
    public float GetSectionTime(string name)
    {
        return _sectionTimes.TryGetValue(name, out var time) ? time : 0;
    }
    
    public Dictionary<string, float> GetAllSectionTimes() => new(_sectionTimes);
}

/// <summary>
/// Temporal upscaler - render at low res, reconstruct to high res using temporal data
/// This is THE key technique for "Ultra on Integrated Graphics"
/// </summary>
public class TemporalUpscaler
{
    private readonly IRHIDevice _device;
    private IRHITexture? _historyBuffer;
    private IRHITexture? _motionVectors;
    
    public TemporalUpscaler(IRHIDevice device)
    {
        _device = device;
    }
    
    public void Initialize(uint targetWidth, uint targetHeight)
    {
        // History buffer stores previous frame at full resolution
        _historyBuffer = _device.CreateTexture(new TextureDesc
        {
            Width = targetWidth,
            Height = targetHeight,
            Depth = 1,
            Format = TextureFormat.RGBA16Float,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "TemporalHistory"
        });
        
        // Motion vectors for reprojection
        _motionVectors = _device.CreateTexture(new TextureDesc
        {
            Width = targetWidth,
            Height = targetHeight,
            Depth = 1,
            Format = TextureFormat.RGBA16Float, // Changed from RG32Float to RGBA16Float for Metal compatibility
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "MotionVectors"
        });
        
        Console.WriteLine("[TemporalUpscaler] Initialized for temporal reconstruction");
    }
    
    /// <summary>
    /// Upscale from render resolution to target resolution using temporal data
    /// This is similar to DLSS/FSR but simpler
    /// </summary>
    public void Upscale(IRHICommandBuffer cmd, IRHITexture lowResInput, IRHITexture output)
    {
        // TODO: Implement temporal upscaling shader
        // 1. Reproject previous frame using motion vectors
        // 2. Blend with current low-res frame
        // 3. Apply sharpening filter
        // 4. Handle disocclusion (areas not visible in previous frame)
    }
}
