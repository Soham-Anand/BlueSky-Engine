// BlueSkyEngine - Texture VRAM Budget Manager
// Tracks VRAM usage and enforces budget limits

using System;

namespace BlueSky.Rendering.Textures;

/// <summary>
/// VRAM budget manager for texture streaming.
/// </summary>
internal class TextureBudget
{
    private long _budgetBytes;
    private long _currentUsage;
    private long _peakUsage;
    private DateTime _lastUpdate = DateTime.UtcNow;
    
    public long BudgetBytes => _budgetBytes;
    public long CurrentUsage => _currentUsage;
    public long PeakUsage => _peakUsage;
    public long AvailableBytes => Math.Max(0, _budgetBytes - _currentUsage);
    public float UsagePercent => _budgetBytes > 0 ? (float)_currentUsage / _budgetBytes * 100f : 0f;
    public bool IsOverBudget => _currentUsage > _budgetBytes;
    
    public TextureBudget(long budgetBytes)
    {
        _budgetBytes = budgetBytes;
    }
    
    /// <summary>
    /// Update current usage (call once per frame).
    /// </summary>
    public void Update(long currentUsage)
    {
        _currentUsage = currentUsage;
        _peakUsage = Math.Max(_peakUsage, currentUsage);
        _lastUpdate = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Set new budget.
    /// </summary>
    public void SetBudget(long budgetBytes)
    {
        _budgetBytes = budgetBytes;
    }
    
    /// <summary>
    /// Get budget statistics.
    /// </summary>
    public BudgetStats GetStats()
    {
        return new BudgetStats
        {
            BudgetBytes = _budgetBytes,
            CurrentUsage = _currentUsage,
            PeakUsage = _peakUsage,
            AvailableBytes = AvailableBytes,
            UsagePercent = UsagePercent,
            IsOverBudget = IsOverBudget
        };
    }
}

/// <summary>
/// Budget statistics.
/// </summary>
public struct BudgetStats
{
    public long BudgetBytes;
    public long CurrentUsage;
    public long PeakUsage;
    public long AvailableBytes;
    public float UsagePercent;
    public bool IsOverBudget;
    
    public string BudgetMB => $"{BudgetBytes / (1024 * 1024)} MB";
    public string CurrentUsageMB => $"{CurrentUsage / (1024 * 1024)} MB";
    public string PeakUsageMB => $"{PeakUsage / (1024 * 1024)} MB";
}
