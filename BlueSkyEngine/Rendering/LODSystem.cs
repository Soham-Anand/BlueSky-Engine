using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Rendering;

/// <summary>
/// Level of Detail (LOD) system for optimizing mesh rendering on lower-end hardware.
/// Automatically switches between detail levels based on distance to camera.
/// </summary>
public class LODSystem
{
    private readonly Dictionary<ulong, LODData> _meshLODs = new();
    private readonly Vector3 _cameraPosition;
    private readonly float _lodBias = 1.0f;
    
    /// <summary>
    /// LOD configuration for a single mesh.
    /// </summary>
    public class LODData
    {
        public uint MeshId;
        public LODLevel[] Levels;
        public float ScreenSizeTransition = 0.5f; // Screen size percentage for LOD transition
    }
    
    /// <summary>
    /// Single LOD level with distance thresholds.
    /// </summary>
    public class LODLevel
    {
        public int Level; // 0 = highest detail, higher = lower detail
        public float Distance; // Distance at which this LOD becomes active
        public uint MeshId; // Mesh ID for this LOD level
        public float ScreenSize; // Minimum screen size to use this LOD
    }
    
    public LODSystem(Vector3 cameraPosition, float lodBias = 1.0f)
    {
        _cameraPosition = cameraPosition;
        _lodBias = lodBias;
    }
    
    /// <summary>
    /// Register a mesh with its LOD levels.
    /// </summary>
    public void RegisterLOD(ulong meshId, LODData data)
    {
        _meshLODs[meshId] = data;
        
        // Sort levels by distance (farthest = lowest detail)
        Array.Sort(data.Levels, (a, b) => b.Distance.CompareTo(a.Distance));
    }
    
    /// <summary>
    /// Get the appropriate LOD level for a mesh at a given position with smoothing.
    /// </summary>
    public int GetLODLevel(ulong meshId, Vector3 meshPosition, float screenSize)
    {
        if (!_meshLODs.TryGetValue(meshId, out var data))
            return 0; // Default to highest detail if no LOD data
        
        float distance = Vector3.Distance(_cameraPosition, meshPosition);
        distance *= _lodBias;
        
        // Find the highest quality LOD that satisfies the distance constraint
        int targetLOD = 0;
        for (int i = 0; i < data.Levels.Length; i++)
        {
            var level = data.Levels[i];
            if (distance <= level.Distance && screenSize >= level.ScreenSize)
            {
                targetLOD = level.Level;
                break;
            }
        }
        
        // Apply smoothing to prevent popping
        return ApplySmoothing(meshId, targetLOD);
    }
    
    private readonly Dictionary<ulong, int> _currentLODs = new();
    private readonly Dictionary<ulong, float> _lodTransitionTimers = new();
    private const float LODTransitionDelay = 0.1f; // Delay before switching LODs
    
    private int ApplySmoothing(ulong meshId, int targetLOD)
    {
        if (!_currentLODs.ContainsKey(meshId))
        {
            _currentLODs[meshId] = targetLOD;
            return targetLOD;
        }
        
        int currentLOD = _currentLODs[meshId];
        
        // If target is same or adjacent, switch immediately
        if (Math.Abs(targetLOD - currentLOD) <= 1)
        {
            _currentLODs[meshId] = targetLOD;
            return targetLOD;
        }
        
        // For larger jumps, use gradual transition
        if (!_lodTransitionTimers.ContainsKey(meshId))
            _lodTransitionTimers[meshId] = 0;
        
        _lodTransitionTimers[meshId] += 0.016f; // Approx 60fps
        
        if (_lodTransitionTimers[meshId] >= LODTransitionDelay)
        {
            _currentLODs[meshId] += Math.Sign(targetLOD - currentLOD);
            _lodTransitionTimers[meshId] = 0;
        }
        
        return _currentLODs[meshId];
    }
    
    /// <summary>
    /// Get the mesh ID for the current LOD level.
    /// </summary>
    public uint GetLODMeshId(ulong meshId, Vector3 meshPosition, float screenSize)
    {
        int lodLevel = GetLODLevel(meshId, meshPosition, screenSize);
        
        if (!_meshLODs.TryGetValue(meshId, out var data))
            return 0; // Should never happen
        
        foreach (var level in data.Levels)
        {
            if (level.Level == lodLevel)
                return level.MeshId;
        }
        
        return data.Levels[0].MeshId; // Fallback to highest detail
    }
    
    /// <summary>
    /// Update camera position for LOD calculations.
    /// </summary>
    public void UpdateCameraPosition(Vector3 position)
    {
        // This would be called each frame
        // In a real implementation, this would update a field
    }
    
    /// <summary>
    /// Calculate screen size of a mesh based on its bounding sphere and distance.
    /// </summary>
    public static float CalculateScreenSize(Vector3 position, float radius, Vector3 cameraPosition, float fov, float screenHeight)
    {
        float distance = Vector3.Distance(position, cameraPosition) - radius;
        if (distance <= 0.01f) return 1.0f;
        
        float projectedSize = (radius * 2.0f) / (distance * MathF.Tan(fov * 0.5f));
        float screenSize = projectedSize / screenHeight;
        
        return Math.Clamp(screenSize, 0.0f, 1.0f);
    }
    
    /// <summary>
    /// Automatic LOD settings based on GPU tier.
    /// </summary>
    public static class LODPresets
    {
        public static LODSettings Low => new LODSettings
        {
            LODCount = 3,
            LOD0Distance = 5.0f,
            LOD1Distance = 15.0f,
            LOD2Distance = 30.0f,
            ScreenSizeTransition = 0.3f,
            ForceLOD = 2 // Force lowest quality
        };
        
        public static LODSettings Medium => new LODSettings
        {
            LODCount = 4,
            LOD0Distance = 8.0f,
            LOD1Distance = 20.0f,
            LOD2Distance = 40.0f,
            LOD3Distance = 60.0f,
            ScreenSizeTransition = 0.4f,
            ForceLOD = -1 // Auto
        };
        
        public static LODSettings High => new LODSettings
        {
            LODCount = 5,
            LOD0Distance = 10.0f,
            LOD1Distance = 25.0f,
            LOD2Distance = 50.0f,
            LOD3Distance = 80.0f,
            LOD4Distance = 120.0f,
            ScreenSizeTransition = 0.5f,
            ForceLOD = -1 // Auto
        };
    }
    
    public class LODSettings
    {
        public int LODCount;
        public float LOD0Distance;
        public float LOD1Distance;
        public float LOD2Distance;
        public float LOD3Distance;
        public float LOD4Distance;
        public float ScreenSizeTransition;
        public int ForceLOD; // -1 = auto, 0-4 = force specific LOD
    }
}
