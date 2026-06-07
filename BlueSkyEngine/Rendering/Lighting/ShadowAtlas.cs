using System;
using System.Collections.Generic;
using System.Numerics;
using NotBSRenderer;

namespace BlueSky.Rendering.Lighting;

/// <summary>
/// Shadow Atlas - Manages dynamic shadow map allocation for Horizon Lighting
/// Packs multiple shadow maps into a single large texture for efficiency
/// </summary>
public class ShadowAtlas : IDisposable
{
    private readonly IRHIDevice _device;
    private IRHITexture? _atlasTexture;
    private int _atlasSize;
    private readonly List<ShadowSlot> _slots = new();
    private readonly Dictionary<int, ShadowSlot> _lightToSlot = new();
    
    public ShadowAtlas(IRHIDevice device, int atlasSize = 4096)
    {
        _device = device;
        _atlasSize = atlasSize;
        CreateAtlas();
    }
    
    public void SetResolution(int size)
    {
        if (_atlasSize == size) return;
        
        _atlasSize = size;
        _atlasTexture?.Dispose();
        CreateAtlas();
        _slots.Clear();
        _lightToSlot.Clear();
    }
    
    /// <summary>
    /// Allocate a shadow map slot for a light
    /// </summary>
    public bool AllocateShadow(int lightIndex, HorizonLight light)
    {
        // Check if already allocated
        if (_lightToSlot.ContainsKey(lightIndex))
            return true;
        
        // Determine shadow map size based on light importance
        int shadowSize = light.ShadowResolution;
        shadowSize = Math.Clamp(shadowSize, 256, _atlasSize / 2);
        
        // Find free space in atlas
        var slot = FindFreeSlot(shadowSize);
        if (slot == null)
        {
            // Atlas full - evict lowest priority shadow
            EvictLowestPriority();
            slot = FindFreeSlot(shadowSize);
        }
        
        if (slot != null)
        {
            slot.LightIndex = lightIndex;
            slot.IsOccupied = true;
            _lightToSlot[lightIndex] = slot;
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Render all shadow maps in the atlas
    /// </summary>
    public void RenderShadows(IRHICommandBuffer cmd, ReadOnlySpan<HorizonLight> lights)
    {
        foreach (var slot in _slots)
        {
            if (!slot.IsOccupied) continue;
            
            var light = lights[slot.LightIndex];
            if (!light.CastShadows) continue;
            
            RenderShadowMap(cmd, light, slot);
        }
    }
    
    /// <summary>
    /// Bind shadow atlas to the pipeline
    /// </summary>
    public void BindShadowMaps(IRHICommandBuffer cmd, uint binding)
    {
        if (_atlasTexture != null)
        {
            cmd.SetTexture(_atlasTexture, binding);
        }
    }
    
    /// <summary>
    /// Get shadow map UV transform for a light
    /// </summary>
    public Vector4 GetShadowUVTransform(int lightIndex)
    {
        if (!_lightToSlot.TryGetValue(lightIndex, out var slot))
            return Vector4.Zero;
        
        float scaleX = (float)slot.Size / _atlasSize;
        float scaleY = (float)slot.Size / _atlasSize;
        float offsetX = (float)slot.X / _atlasSize;
        float offsetY = (float)slot.Y / _atlasSize;
        
        return new Vector4(scaleX, scaleY, offsetX, offsetY);
    }
    
    private void CreateAtlas()
    {
        _atlasTexture = _device.CreateTexture(new TextureDesc
        {
            Width = (uint)_atlasSize,
            Height = (uint)_atlasSize,
            Depth = 1,
            Format = TextureFormat.Depth32Float,
            Usage = TextureUsage.DepthStencil | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1,
            DebugName = "Horizon_ShadowAtlas"
        });
        
        // Initialize with common slot sizes
        InitializeSlots();
    }
    
    private void InitializeSlots()
    {
        // Create a grid of slots with common sizes
        // 4x 1024x1024 (high priority lights)
        AddSlots(1024, 2, 2);
        
        // 16x 512x512 (medium priority)
        AddSlots(512, 4, 4);
        
        // 64x 256x256 (low priority)
        AddSlots(256, 8, 8);
    }
    
    private void AddSlots(int size, int countX, int countY)
    {
        for (int y = 0; y < countY; y++)
        {
            for (int x = 0; x < countX; x++)
            {
                _slots.Add(new ShadowSlot
                {
                    X = x * size,
                    Y = y * size,
                    Size = size,
                    IsOccupied = false,
                    LightIndex = -1
                });
            }
        }
    }
    
    private ShadowSlot? FindFreeSlot(int minSize)
    {
        // Find smallest available slot that fits
        ShadowSlot? bestSlot = null;
        
        foreach (var slot in _slots)
        {
            if (slot.IsOccupied) continue;
            if (slot.Size < minSize) continue;
            
            if (bestSlot == null || slot.Size < bestSlot.Size)
                bestSlot = slot;
        }
        
        return bestSlot;
    }
    
    private void EvictLowestPriority()
    {
        // Find occupied slot with lowest priority
        ShadowSlot? lowestSlot = null;
        int lowestSize = int.MaxValue;
        
        foreach (var slot in _slots)
        {
            if (!slot.IsOccupied) continue;
            
            if (slot.Size < lowestSize)
            {
                lowestSize = slot.Size;
                lowestSlot = slot;
            }
        }
        
        if (lowestSlot != null)
        {
            _lightToSlot.Remove(lowestSlot.LightIndex);
            lowestSlot.IsOccupied = false;
            lowestSlot.LightIndex = -1;
        }
    }
    
    private void RenderShadowMap(IRHICommandBuffer cmd, HorizonLight light, ShadowSlot slot)
    {
        // Calculate shadow view-projection matrix
        Matrix4x4 shadowViewProj = CalculateShadowMatrix(light);
        
        // Set viewport to shadow slot region
        cmd.SetViewport(new NotBSRenderer.Viewport
        {
            X = slot.X,
            Y = slot.Y,
            Width = (uint)slot.Size,
            Height = (uint)slot.Size,
            MinDepth = 0.0f,
            MaxDepth = 1.0f
        });
        
        // TODO: Render shadow casters with depth-only shader
        // This would be done by the main renderer
    }
    
    private Matrix4x4 CalculateShadowMatrix(HorizonLight light)
    {
        return light.Type switch
        {
            LightType.Directional => CalculateDirectionalShadowMatrix(light),
            LightType.Spot => CalculateSpotShadowMatrix(light),
            LightType.Point => CalculatePointShadowMatrix(light),
            _ => Matrix4x4.Identity
        };
    }
    
    private Matrix4x4 CalculateDirectionalShadowMatrix(HorizonLight light)
    {
        // Create orthographic projection for directional light
        Vector3 lightDir = Vector3.Normalize(light.Direction);
        Vector3 up = Math.Abs(lightDir.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY;
        
        Matrix4x4 view = Matrix4x4.CreateLookAt(
            -lightDir * 50.0f, // Position far back
            Vector3.Zero,
            up
        );
        
        Matrix4x4 proj = Matrix4x4.CreateOrthographic(
            light.Range * 2,
            light.Range * 2,
            0.1f,
            100.0f
        );
        
        return view * proj;
    }
    
    private Matrix4x4 CalculateSpotShadowMatrix(HorizonLight light)
    {
        Vector3 lightDir = Vector3.Normalize(light.Direction);
        Vector3 up = Math.Abs(lightDir.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY;
        
        Matrix4x4 view = Matrix4x4.CreateLookAt(
            light.Position,
            light.Position + lightDir,
            up
        );
        
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(
            light.OuterAngle * 2,
            1.0f, // Square shadow map
            0.1f,
            light.Range
        );
        
        return view * proj;
    }
    
    private Matrix4x4 CalculatePointShadowMatrix(HorizonLight light)
    {
        // Point lights need cubemap shadows - simplified to single face for now
        Matrix4x4 view = Matrix4x4.CreateLookAt(
            light.Position,
            light.Position + Vector3.UnitZ,
            Vector3.UnitY
        );
        
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 2, // 90 degrees for cubemap face
            1.0f,
            0.1f,
            light.Range
        );
        
        return view * proj;
    }
    
    public void Dispose()
    {
        _atlasTexture?.Dispose();
    }
}

class ShadowSlot
{
    public int X;
    public int Y;
    public int Size;
    public bool IsOccupied;
    public int LightIndex;
}
