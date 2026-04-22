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

/// <summary>
/// Dirty rectangle tracking for optimized rendering
/// </summary>
public class DirtyRectTracker
{
    private readonly List<Rect> _dirtyRects = new();
    private bool _fullRedraw = true;
    
    public bool NeedsFullRedraw => _fullRedraw;
    public IReadOnlyList<Rect> DirtyRects => _dirtyRects;
    
    public struct Rect
    {
        public float X, Y, W, H;
        
        public Rect(float x, float y, float w, float h)
        {
            X = x; Y = y; W = w; H = h;
        }
        
        public bool Intersects(Rect other)
        {
            return X < other.X + other.W &&
                   X + W > other.X &&
                   Y < other.Y + other.H &&
                   Y + H > other.Y;
        }
        
        public Rect Union(Rect other)
        {
            float x1 = MathF.Min(X, other.X);
            float y1 = MathF.Min(Y, other.Y);
            float x2 = MathF.Max(X + W, other.X + other.W);
            float y2 = MathF.Max(Y + H, other.Y + other.H);
            return new Rect(x1, y1, x2 - x1, y2 - y1);
        }
    }
    
    public void MarkDirty(float x, float y, float w, float h)
    {
        var newRect = new Rect(x, y, w, h);
        
        // Try to merge with existing rects
        for (int i = 0; i < _dirtyRects.Count; i++)
        {
            if (_dirtyRects[i].Intersects(newRect))
            {
                newRect = _dirtyRects[i].Union(newRect);
                _dirtyRects.RemoveAt(i);
                i--;
            }
        }
        
        _dirtyRects.Add(newRect);
    }
    
    public void MarkFullRedraw()
    {
        _fullRedraw = true;
        _dirtyRects.Clear();
    }
    
    public void Clear()
    {
        _fullRedraw = false;
        _dirtyRects.Clear();
    }
}
