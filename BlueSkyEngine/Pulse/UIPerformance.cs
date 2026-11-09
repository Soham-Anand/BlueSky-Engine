using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BlueSky.Editor.UI;

/// <summary>
/// Performance monitoring for UI rendering
/// </summary>
public class UIPerformanceMonitor
{
    private readonly Stopwatch _frameTimer = new();
    private readonly Queue<float> _frameTimes = new(120);
    private readonly Dictionary<string, float> _sectionTimes = new();
    private readonly Dictionary<string, Stopwatch> _sectionTimers = new();
    
    public int DrawCallCount { get; private set; }
    public int VertexCount { get; private set; }
    public int TriangleCount { get; private set; }
    public int PanelCount { get; private set; }
    public int TextCount { get; private set; }
    
    public float AverageFrameTime { get; private set; }
    public float MinFrameTime { get; private set; }
    public float MaxFrameTime { get; private set; }
    public float CurrentFrameTime { get; private set; }
    
    public float FPS => CurrentFrameTime > 0 ? 1000f / CurrentFrameTime : 0f;
    
    public void BeginFrame()
    {
        _frameTimer.Restart();
        DrawCallCount = 0;
        VertexCount = 0;
        TriangleCount = 0;
        PanelCount = 0;
        TextCount = 0;
    }
    
    public void EndFrame()
    {
        _frameTimer.Stop();
        CurrentFrameTime = (float)_frameTimer.Elapsed.TotalMilliseconds;
        
        _frameTimes.Enqueue(CurrentFrameTime);
        if (_frameTimes.Count > 120)
            _frameTimes.Dequeue();
        
        // Calculate statistics
        if (_frameTimes.Count > 0)
        {
            float sum = 0f;
            float min = float.MaxValue;
            float max = float.MinValue;
            
            foreach (var time in _frameTimes)
            {
                sum += time;
                if (time < min) min = time;
                if (time > max) max = time;
            }
            
            AverageFrameTime = sum / _frameTimes.Count;
            MinFrameTime = min;
            MaxFrameTime = max;
        }
    }
    
    public void BeginSection(string name)
    {
        if (!_sectionTimers.TryGetValue(name, out var timer))
        {
            timer = new Stopwatch();
            _sectionTimers[name] = timer;
        }
        timer.Restart();
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
        return _sectionTimes.TryGetValue(name, out var time) ? time : 0f;
    }
    
    public void RecordDrawCall(int vertexCount, int triangleCount)
    {
        DrawCallCount++;
        VertexCount += vertexCount;
        TriangleCount += triangleCount;
    }
    
    public void RecordPanel()
    {
        PanelCount++;
    }
    
    public void RecordText()
    {
        TextCount++;
    }
    
    public string GetSummary()
    {
        return $"FPS: {FPS:F1} | Frame: {CurrentFrameTime:F2}ms (avg: {AverageFrameTime:F2}ms) | " +
               $"Draw Calls: {DrawCallCount} | Verts: {VertexCount} | Tris: {TriangleCount} | " +
               $"Panels: {PanelCount} | Text: {TextCount}";
    }
}
