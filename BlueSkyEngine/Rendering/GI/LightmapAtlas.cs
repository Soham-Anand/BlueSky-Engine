using System;
using System.Collections.Generic;
using System.Numerics;
using BlueSky.Core.ECS;

namespace BlueSky.Rendering.GI;

/// <summary>
/// Lightmap atlas - packs multiple lightmaps into a single texture.
/// This minimizes draw calls and texture switches for maximum performance.
/// </summary>
public class LightmapAtlas
{
    public int Width { get; }
    public int Height { get; }
    
    private readonly Vector3[] _pixels;
    private readonly List<LightmapRegion> _regions = new();
    
    public IReadOnlyList<LightmapRegion> Regions => _regions;
    public int RegionCount => _regions.Count;
    
    public LightmapAtlas(int width, int height)
    {
        Width = width;
        Height = height;
        _pixels = new Vector3[width * height];
    }
    
    public void AllocateRegion(Entity entity, int x, int y, int width, int height)
    {
        var region = new LightmapRegion
        {
            Entity = entity,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            UVMin = new Vector2((float)x / Width, (float)y / Height),
            UVMax = new Vector2((float)(x + width) / Width, (float)(y + height) / Height)
        };
        
        _regions.Add(region);
    }
    
    public Vector3 GetPixel(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return Vector3.Zero;
        
        return _pixels[y * Width + x];
    }
    
    public void SetPixel(int x, int y, Vector3 color)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return;
        
        _pixels[y * Width + x] = color;
    }
    
    /// <summary>
    /// Denoise the lightmap using a simple bilateral filter.
    /// </summary>
    public void Denoise()
    {
        var denoised = new Vector3[_pixels.Length];
        int kernelSize = 3;
        float spatialSigma = 1.0f;
        float colorSigma = 0.1f;
        
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var center = GetPixel(x, y);
                var sum = Vector3.Zero;
                float weightSum = 0f;
                
                for (int ky = -kernelSize; ky <= kernelSize; ky++)
                {
                    for (int kx = -kernelSize; kx <= kernelSize; kx++)
                    {
                        var sample = GetPixel(x + kx, y + ky);
                        
                        float spatialDist = MathF.Sqrt(kx * kx + ky * ky);
                        float colorDist = (sample - center).Length();
                        
                        float spatialWeight = MathF.Exp(-spatialDist * spatialDist / (2f * spatialSigma * spatialSigma));
                        float colorWeight = MathF.Exp(-colorDist * colorDist / (2f * colorSigma * colorSigma));
                        float weight = spatialWeight * colorWeight;
                        
                        sum += sample * weight;
                        weightSum += weight;
                    }
                }
                
                denoised[y * Width + x] = sum / weightSum;
            }
        }
        
        Array.Copy(denoised, _pixels, _pixels.Length);
    }
    
    /// <summary>
    /// Dilate edges to prevent seams between UV islands.
    /// </summary>
    public void DilateEdges()
    {
        var dilated = new Vector3[_pixels.Length];
        Array.Copy(_pixels, dilated, _pixels.Length);
        
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var pixel = GetPixel(x, y);
                
                // If pixel is black (unused), fill from neighbors
                if (pixel.LengthSquared() < 0.001f)
                {
                    var sum = Vector3.Zero;
                    int count = 0;
                    
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            
                            var neighbor = GetPixel(x + dx, y + dy);
                            if (neighbor.LengthSquared() > 0.001f)
                            {
                                sum += neighbor;
                                count++;
                            }
                        }
                    }
                    
                    if (count > 0)
                    {
                        dilated[y * Width + x] = sum / count;
                    }
                }
            }
        }
        
        Array.Copy(dilated, _pixels, _pixels.Length);
    }
    
    /// <summary>
    /// Apply exposure adjustment to all pixels.
    /// </summary>
    public void ApplyExposure(float exposure)
    {
        for (int i = 0; i < _pixels.Length; i++)
        {
            _pixels[i] *= exposure;
        }
    }
    
    /// <summary>
    /// Convert to RGBA8 byte array for GPU upload.
    /// </summary>
    public byte[] ToRGBA8()
    {
        var bytes = new byte[Width * Height * 4];
        
        for (int i = 0; i < _pixels.Length; i++)
        {
            var color = _pixels[i];
            
            // Apply gamma correction (linear to sRGB)
            color = new Vector3(
                MathF.Pow(color.X, 1f / 2.2f),
                MathF.Pow(color.Y, 1f / 2.2f),
                MathF.Pow(color.Z, 1f / 2.2f)
            );
            
            // Clamp and convert to bytes
            bytes[i * 4 + 0] = (byte)Math.Clamp(color.X * 255f, 0, 255);
            bytes[i * 4 + 1] = (byte)Math.Clamp(color.Y * 255f, 0, 255);
            bytes[i * 4 + 2] = (byte)Math.Clamp(color.Z * 255f, 0, 255);
            bytes[i * 4 + 3] = 255;
        }
        
        return bytes;
    }
    
    /// <summary>
    /// Save lightmap to file (PNG format).
    /// </summary>
    public void SaveToFile(string path)
    {
        // TODO: Implement PNG encoding or use external library
        Console.WriteLine($"[LightmapAtlas] Saving to {path}...");
    }
}

/// <summary>
/// Region in the lightmap atlas allocated to a specific mesh.
/// </summary>
public class LightmapRegion
{
    public Entity Entity { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public Vector2 UVMin { get; set; }
    public Vector2 UVMax { get; set; }
}

