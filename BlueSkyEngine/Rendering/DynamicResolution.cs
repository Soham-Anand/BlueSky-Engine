using System;

namespace BlueSky.Rendering;

/// <summary>
/// Dynamic resolution scaling for maintaining target FPS on lower-end hardware.
/// Automatically adjusts render resolution based on performance metrics.
/// </summary>
public class DynamicResolution
{
    private float _currentScale = 1.0f;
    private float _targetScale = 1.0f;
    private readonly float _minScale = 0.5f; // Don't go below 50%
    private readonly float _maxScale = 1.0f;
    private readonly float _scaleSpeed = 0.1f; // How fast to adjust (per second)
    
    private readonly PerformanceMetrics _metrics;
    private readonly DynamicResolutionSettings _settings;
    
    public float CurrentScale => _currentScale;
    public float TargetScale => _targetScale;
    
    public DynamicResolution(PerformanceMetrics metrics, DynamicResolutionSettings settings)
    {
        _metrics = metrics;
        _settings = settings;
    }
    
    /// <summary>
    /// Update resolution scale based on current performance.
    /// Call this once per frame.
    /// </summary>
    public void Update(float deltaTime)
    {
        // Calculate target scale based on FPS
        float targetFPS = _settings.TargetFPS;
        float currentFPS = _metrics.CurrentFPS;
        
        // Add hysteresis to prevent flickering
        if (currentFPS < targetFPS - _settings.FPSThresholdLow)
        {
            // FPS too low, reduce resolution
            float fpsDelta = targetFPS - currentFPS;
            float scaleReduction = (fpsDelta / targetFPS) * _settings.Sensitivity;
            _targetScale = Math.Max(_minScale, _targetScale - scaleReduction);
        }
        else if (currentFPS > targetFPS + _settings.FPSThresholdHigh)
        {
            // FPS good, can increase resolution
            float fpsDelta = currentFPS - targetFPS;
            float scaleIncrease = (fpsDelta / targetFPS) * _settings.Sensitivity;
            _targetScale = Math.Min(_maxScale, _targetScale + scaleIncrease);
        }
        
        // Smoothly interpolate to target scale with adaptive speed
        float scaleDelta = _targetScale - _currentScale;
        float adaptiveSpeed = _scaleSpeed;
        
        // Faster adjustment when far from target, slower when close
        if (Math.Abs(scaleDelta) > 0.2f)
            adaptiveSpeed *= 2.0f;
        else if (Math.Abs(scaleDelta) < 0.05f)
            adaptiveSpeed *= 0.5f;
        
        float maxChange = adaptiveSpeed * deltaTime;
        
        if (Math.Abs(scaleDelta) > maxChange)
        {
            _currentScale += Math.Sign(scaleDelta) * maxChange;
        }
        else
        {
            _currentScale = _targetScale;
        }
    }
    
    /// <summary>
    /// Get the actual render resolution based on current scale.
    /// </summary>
    public (int width, int height) GetRenderResolution(int baseWidth, int baseHeight)
    {
        int scaledWidth = (int)(baseWidth * _currentScale);
        int scaledHeight = (int)(baseHeight * _currentScale);
        
        // Ensure even dimensions for better texture alignment
        scaledWidth = (scaledWidth / 2) * 2;
        scaledHeight = (scaledHeight / 2) * 2;
        
        // Minimum resolution clamp
        scaledWidth = Math.Max(640, scaledWidth);
        scaledHeight = Math.Max(360, scaledHeight);
        
        return (scaledWidth, scaledHeight);
    }
    
    /// <summary>
    /// Force a specific resolution scale (for testing or user override).
    /// </summary>
    public void SetScale(float scale)
    {
        _targetScale = Math.Clamp(scale, _minScale, _maxScale);
    }
    
    /// <summary>
    /// Reset to full resolution.
    /// </summary>
    public void Reset()
    {
        _targetScale = 1.0f;
    }
    
    /// <summary>
    /// Performance metrics for dynamic resolution.
    /// </summary>
    public class PerformanceMetrics
    {
        public float CurrentFPS { get; set; }
        public float FrameTime { get; set; }
        public float GPUTime { get; set; }
        public float CPUTime { get; set; }
        
        // Rolling average for stability
        private readonly float[] _fpsHistory = new float[30];
        private int _historyIndex = 0;
        
        public void UpdateFPS(float fps)
        {
            _fpsHistory[_historyIndex] = fps;
            _historyIndex = (_historyIndex + 1) % _fpsHistory.Length;
            
            // Calculate average
            float sum = 0;
            foreach (var f in _fpsHistory)
                sum += f;
            CurrentFPS = sum / _fpsHistory.Length;
        }
    }
    
    /// <summary>
    /// Settings for dynamic resolution.
    /// </summary>
    public class DynamicResolutionSettings
    {
        public float TargetFPS { get; set; } = 60.0f;
        public float FPSThresholdLow { get; set; } = 5.0f; // Below target - start reducing
        public float FPSThresholdHigh { get; set; } = 10.0f; // Above target - start increasing
        public float Sensitivity { get; set; } = 0.5f; // How aggressively to adjust
        public bool Enabled { get; set; } = true;
        
        // Quality presets
        public static DynamicResolutionSettings Conservative => new()
        {
            TargetFPS = 60.0f,
            FPSThresholdLow = 3.0f,
            FPSThresholdHigh = 15.0f,
            Sensitivity = 0.3f,
            Enabled = true
        };
        
        public static DynamicResolutionSettings Balanced => new()
        {
            TargetFPS = 60.0f,
            FPSThresholdLow = 5.0f,
            FPSThresholdHigh = 10.0f,
            Sensitivity = 0.5f,
            Enabled = true
        };
        
        public static DynamicResolutionSettings Aggressive => new()
        {
            TargetFPS = 60.0f,
            FPSThresholdLow = 8.0f,
            FPSThresholdHigh = 5.0f,
            Sensitivity = 0.7f,
            Enabled = true
        };
    }
}
