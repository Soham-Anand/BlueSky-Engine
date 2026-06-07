using System;
using System.Collections.Generic;
using System.Diagnostics;
using BlueSky.Core.Platform.Detection;

namespace BlueSky.Rendering.RayTracing;

/// <summary>
/// Adaptive RT manager that dynamically adjusts quality for target FPS
/// </summary>
public class AdaptiveRTManager
{
    private readonly GpuCapabilities _gpu;
    private RTTier _currentTier;
    private RTTierConfig _currentConfig;
    
    // Performance monitoring
    private readonly Queue<float> _frameTimeHistory = new(60);
    private float _averageFrameTime = 16.67f; // 60 FPS
    private int _framesSinceAdjustment = 0;
    private const int ADJUSTMENT_COOLDOWN = 120; // 2 seconds at 60 FPS
    
    // Target FPS
    private int _targetFPS = 60;
    private float _targetFrameTime => 1000.0f / _targetFPS;
    
    // Quality adjustment
    private bool _allowUpgrade = true;
    private bool _allowDowngrade = true;
    private int _manualOverride = -1; // -1 = auto, else tier index
    
    // Statistics
    private int _upgradeCount = 0;
    private int _downgradeCount = 0;
    private float _minFPS = float.MaxValue;
    private float _maxFPS = 0.0f;
    
    public RTTier CurrentTier => _currentTier;
    public RTTierConfig CurrentConfig => _currentConfig;
    public float AverageFPS => 1000.0f / _averageFrameTime;
    public float MinFPS => _minFPS;
    public float MaxFPS => _maxFPS;
    public int UpgradeCount => _upgradeCount;
    public int DowngradeCount => _downgradeCount;
    
    public AdaptiveRTManager(GpuCapabilities gpu, RTTier initialTier, int targetFPS = 60)
    {
        _gpu = gpu;
        _currentTier = initialTier;
        _currentConfig = RTTierSelector.GetConfig(initialTier);
        _targetFPS = targetFPS;
        
        Console.WriteLine("[AdaptiveRT] Initialized");
        Console.WriteLine($"  Initial Tier: {_currentConfig.Name}");
        Console.WriteLine($"  Target FPS: {_targetFPS}");
        Console.WriteLine($"  Resolution: {_currentConfig.RenderWidth}×{_currentConfig.RenderHeight} → {_currentConfig.OutputWidth}×{_currentConfig.OutputHeight}");
    }
    
    /// <summary>
    /// Update with current frame time and adjust quality if needed
    /// </summary>
    public void UpdateFrameTime(float frameTimeMs)
    {
        // Add to history
        _frameTimeHistory.Enqueue(frameTimeMs);
        if (_frameTimeHistory.Count > 60)
            _frameTimeHistory.Dequeue();
        
        // Calculate average
        float sum = 0.0f;
        foreach (var time in _frameTimeHistory)
            sum += time;
        _averageFrameTime = sum / _frameTimeHistory.Count;
        
        // Update statistics
        float currentFPS = 1000.0f / frameTimeMs;
        _minFPS = Math.Min(_minFPS, currentFPS);
        _maxFPS = Math.Max(_maxFPS, currentFPS);
        
        // Increment cooldown
        _framesSinceAdjustment++;
        
        // Check if adjustment needed (only after cooldown)
        if (_framesSinceAdjustment >= ADJUSTMENT_COOLDOWN && _manualOverride == -1)
        {
            float avgFPS = AverageFPS;
            
            // Downgrade if too slow
            if (avgFPS < _targetFPS - 5 && _allowDowngrade)
            {
                if (TryDowngradeTier())
                {
                    _downgradeCount++;
                    _framesSinceAdjustment = 0;
                    Console.WriteLine($"[AdaptiveRT] Downgraded to {_currentConfig.Name} (FPS: {avgFPS:F1} < {_targetFPS - 5})");
                }
            }
            // Upgrade if too fast
            else if (avgFPS > _targetFPS + 10 && _allowUpgrade)
            {
                if (TryUpgradeTier())
                {
                    _upgradeCount++;
                    _framesSinceAdjustment = 0;
                    Console.WriteLine($"[AdaptiveRT] Upgraded to {_currentConfig.Name} (FPS: {avgFPS:F1} > {_targetFPS + 10})");
                }
            }
        }
    }
    
    /// <summary>
    /// Try to downgrade to lower quality tier
    /// </summary>
    private bool TryDowngradeTier()
    {
        int currentIndex = (int)_currentTier;
        if (currentIndex <= 0)
            return false; // Already at lowest tier
        
        RTTier newTier = (RTTier)(currentIndex - 1);
        _currentTier = newTier;
        _currentConfig = RTTierSelector.GetConfig(newTier, _currentConfig.OutputWidth, _currentConfig.OutputHeight);
        return true;
    }
    
    /// <summary>
    /// Try to upgrade to higher quality tier
    /// </summary>
    private bool TryUpgradeTier()
    {
        int currentIndex = (int)_currentTier;
        int maxIndex = Enum.GetValues<RTTier>().Length - 1;
        
        if (currentIndex >= maxIndex)
            return false; // Already at highest tier
        
        RTTier newTier = (RTTier)(currentIndex + 1);
        _currentTier = newTier;
        _currentConfig = RTTierSelector.GetConfig(newTier, _currentConfig.OutputWidth, _currentConfig.OutputHeight);
        return true;
    }
    
    /// <summary>
    /// Manually set tier (disables auto-adjustment)
    /// </summary>
    public void SetTier(RTTier tier)
    {
        _currentTier = tier;
        _currentConfig = RTTierSelector.GetConfig(tier, _currentConfig.OutputWidth, _currentConfig.OutputHeight);
        _manualOverride = (int)tier;
        _framesSinceAdjustment = 0;
        
        Console.WriteLine($"[AdaptiveRT] Manual override: {_currentConfig.Name}");
    }
    
    /// <summary>
    /// Re-enable auto-adjustment
    /// </summary>
    public void EnableAutoAdjustment()
    {
        _manualOverride = -1;
        Console.WriteLine("[AdaptiveRT] Auto-adjustment enabled");
    }
    
    /// <summary>
    /// Set target FPS
    /// </summary>
    public void SetTargetFPS(int fps)
    {
        _targetFPS = fps;
        Console.WriteLine($"[AdaptiveRT] Target FPS: {_targetFPS}");
    }
    
    /// <summary>
    /// Enable/disable quality upgrades
    /// </summary>
    public void SetAllowUpgrade(bool allow)
    {
        _allowUpgrade = allow;
    }
    
    /// <summary>
    /// Enable/disable quality downgrades
    /// </summary>
    public void SetAllowDowngrade(bool allow)
    {
        _allowDowngrade = allow;
    }
    
    /// <summary>
    /// Reset statistics
    /// </summary>
    public void ResetStatistics()
    {
        _minFPS = float.MaxValue;
        _maxFPS = 0.0f;
        _upgradeCount = 0;
        _downgradeCount = 0;
        _frameTimeHistory.Clear();
    }
    
    /// <summary>
    /// Print current status
    /// </summary>
    public void PrintStatus()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("ADAPTIVE RT STATUS");
        Console.WriteLine("================================================================================");
        Console.WriteLine($"Current Tier:     {_currentConfig.Name} (Tier {(int)_currentTier})");
        Console.WriteLine($"Backend:          {_currentConfig.Backend}");
        Console.WriteLine($"Resolution:       {_currentConfig.RenderWidth}×{_currentConfig.RenderHeight} → {_currentConfig.OutputWidth}×{_currentConfig.OutputHeight}");
        Console.WriteLine($"Upscale Factor:   {_currentConfig.GetUpscaleFactor():F2}x");
        Console.WriteLine($"Rays Per Pixel:   {_currentConfig.RaysPerPixel:F2}");
        Console.WriteLine($"Total Rays:       {_currentConfig.GetEffectiveRayCount():N0}");
        Console.WriteLine();
        Console.WriteLine("Performance:");
        Console.WriteLine($"  Target FPS:     {_targetFPS}");
        Console.WriteLine($"  Average FPS:    {AverageFPS:F1}");
        Console.WriteLine($"  Min FPS:        {MinFPS:F1}");
        Console.WriteLine($"  Max FPS:        {MaxFPS:F1}");
        Console.WriteLine($"  Frame Time:     {_averageFrameTime:F2}ms");
        Console.WriteLine();
        Console.WriteLine("Adjustments:");
        Console.WriteLine($"  Upgrades:       {_upgradeCount}");
        Console.WriteLine($"  Downgrades:     {_downgradeCount}");
        Console.WriteLine($"  Auto-Adjust:    {(_manualOverride == -1 ? "Enabled" : "Disabled")}");
        Console.WriteLine($"  Allow Upgrade:  {_allowUpgrade}");
        Console.WriteLine($"  Allow Downgrade: {_allowDowngrade}");
        Console.WriteLine("================================================================================");
    }
}
