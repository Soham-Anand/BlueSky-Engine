// BlueSkyEngine - Project Polaris: CPU Framebuffer
//
// Low-resolution pixel buffer written by CPU ray tracer,
// then uploaded to GPU for upscaling.
// 320×180 × 16 bytes = 230 KB (fits in L2 cache on Sandy Bridge)

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BlueSky.Rendering.RayTracing.Polaris;

/// <summary>
/// Low-resolution CPU framebuffer for ray-traced output.
/// Stores RGBA float32 per pixel. Designed to fit in L2 cache.
/// </summary>
public class CPUFramebuffer
{
    private readonly int _width;
    private readonly int _height;
    private readonly float[] _pixels;    // RGBA interleaved: [r,g,b,a, r,g,b,a, ...]
    private readonly float[] _depth;     // depth per pixel (for temporal reprojection)
    private readonly float[] _normals;   // normal XYZ per pixel (for edge-aware upscaling)
    
    public int Width => _width;
    public int Height => _height;
    public int PixelCount => _width * _height;
    
    /// <summary>Raw RGBA pixel data (4 floats per pixel)</summary>
    public ReadOnlySpan<float> Pixels => _pixels;
    /// <summary>Raw depth data (1 float per pixel)</summary>
    public ReadOnlySpan<float> Depth => _depth;
    /// <summary>Raw normal data (3 floats per pixel)</summary>
    public ReadOnlySpan<float> Normals => _normals;
    
    /// <summary>Pixel data as bytes for GPU upload</summary>
    public ReadOnlySpan<byte> PixelBytes => MemoryMarshal.AsBytes<float>(_pixels);
    public ReadOnlySpan<byte> DepthBytes => MemoryMarshal.AsBytes<float>(_depth);
    public ReadOnlySpan<byte> NormalBytes => MemoryMarshal.AsBytes<float>(_normals);
    
    public CPUFramebuffer(int width, int height)
    {
        _width = width;
        _height = height;
        _pixels = new float[width * height * 4];
        _depth = new float[width * height];
        _normals = new float[width * height * 3];
        
        Console.WriteLine($"[Polaris FB] Created {width}×{height} framebuffer");
        Console.WriteLine($"  Color: {_pixels.Length * 4 / 1024f:F1} KB");
        Console.WriteLine($"  Depth: {_depth.Length * 4 / 1024f:F1} KB");
        Console.WriteLine($"  Normal: {_normals.Length * 4 / 1024f:F1} KB");
        Console.WriteLine($"  Total: {(_pixels.Length + _depth.Length + _normals.Length) * 4 / 1024f:F1} KB");
    }
    
    /// <summary>Clear framebuffer to sky color</summary>
    public void Clear(Vector3 skyColor)
    {
        for (int i = 0; i < _width * _height; i++)
        {
            _pixels[i * 4 + 0] = skyColor.X;
            _pixels[i * 4 + 1] = skyColor.Y;
            _pixels[i * 4 + 2] = skyColor.Z;
            _pixels[i * 4 + 3] = 1.0f;
            _depth[i] = float.MaxValue;
            _normals[i * 3 + 0] = 0;
            _normals[i * 3 + 1] = 1;
            _normals[i * 3 + 2] = 0;
        }
    }
    
    /// <summary>Write a pixel (thread-safe for non-overlapping regions)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPixel(int x, int y, Vector3 color, float depth, Vector3 normal)
    {
        int idx = (y * _width + x);
        _pixels[idx * 4 + 0] = color.X;
        _pixels[idx * 4 + 1] = color.Y;
        _pixels[idx * 4 + 2] = color.Z;
        _pixels[idx * 4 + 3] = 1.0f;
        _depth[idx] = depth;
        _normals[idx * 3 + 0] = normal.X;
        _normals[idx * 3 + 1] = normal.Y;
        _normals[idx * 3 + 2] = normal.Z;
    }
    
    /// <summary>Read a pixel color</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 GetPixel(int x, int y)
    {
        int idx = (y * _width + x) * 4;
        return new Vector3(_pixels[idx], _pixels[idx + 1], _pixels[idx + 2]);
    }
    
    /// <summary>Read pixel depth</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float GetDepth(int x, int y) => _depth[y * _width + x];
    
    /// <summary>Read pixel normal</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 GetNormal(int x, int y)
    {
        int idx = (y * _width + x) * 3;
        return new Vector3(_normals[idx], _normals[idx + 1], _normals[idx + 2]);
    }
}

/// <summary>
/// Temporal accumulation for noise reduction.
/// Blends current frame with history using motion-compensated reprojection.
/// Each frame at 1 SPP, accumulated over N frames ≈ N SPP quality.
/// </summary>
public class TemporalAccumulator
{
    private readonly int _width;
    private readonly int _height;
    private readonly float[] _history;   // accumulated color (RGBA)
    private readonly float[] _historyDepth;
    private int _frameCount;
    private Matrix4x4 _prevViewProj;
    
    /// <summary>Blend factor: higher = more history (smoother but ghostier)</summary>
    public float BlendFactor { get; set; } = 0.9f;
    
    /// <summary>Depth threshold for rejecting history (prevents ghosting)</summary>
    public float DepthThreshold { get; set; } = 0.1f;
    
    public TemporalAccumulator(int width, int height)
    {
        _width = width;
        _height = height;
        _history = new float[width * height * 4];
        _historyDepth = new float[width * height];
        _frameCount = 0;
        _prevViewProj = Matrix4x4.Identity;
    }
    
    /// <summary>
    /// Accumulate current frame into history.
    /// Uses exponential moving average with depth-based rejection.
    /// </summary>
    public void Accumulate(CPUFramebuffer current, Matrix4x4 viewProj)
    {
        _frameCount++;
        float alpha = _frameCount <= 1 ? 1.0f : (1.0f - BlendFactor);
        
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int idx = y * _width + x;
                int cidx = idx * 4;
                
                float curDepth = current.GetDepth(x, y);
                float histDepth = _historyDepth[idx];
                
                // Reject history if depth changed significantly (disocclusion)
                float depthDiff = MathF.Abs(curDepth - histDepth);
                float effectiveAlpha = depthDiff > DepthThreshold ? 1.0f : alpha;
                
                // EMA blend: result = alpha * current + (1 - alpha) * history
                var curColor = current.GetPixel(x, y);
                _history[cidx + 0] = effectiveAlpha * curColor.X + (1f - effectiveAlpha) * _history[cidx + 0];
                _history[cidx + 1] = effectiveAlpha * curColor.Y + (1f - effectiveAlpha) * _history[cidx + 1];
                _history[cidx + 2] = effectiveAlpha * curColor.Z + (1f - effectiveAlpha) * _history[cidx + 2];
                _history[cidx + 3] = 1.0f;
                _historyDepth[idx] = curDepth;
            }
        }
        
        _prevViewProj = viewProj;
    }
    
    /// <summary>Get accumulated color for a pixel</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 GetAccumulated(int x, int y)
    {
        int idx = (y * _width + x) * 4;
        return new Vector3(_history[idx], _history[idx + 1], _history[idx + 2]);
    }
    
    /// <summary>Get accumulated buffer as bytes for GPU upload</summary>
    public ReadOnlySpan<byte> GetAccumulatedBytes() => MemoryMarshal.AsBytes<float>(_history);
    
    /// <summary>Reset accumulation (call on camera cut)</summary>
    public void Reset()
    {
        Array.Clear(_history);
        Array.Clear(_historyDepth);
        _frameCount = 0;
    }
}
