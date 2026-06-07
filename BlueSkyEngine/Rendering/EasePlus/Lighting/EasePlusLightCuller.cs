using System;
using System.Numerics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BlueSky.Rendering.EasePlus;

/// <summary>
/// Ease+ CPU Tile-Based Light Culler — SIMD-accelerated light assignment.
///
/// Since Intel HD 3000 lacks compute shaders (SM 4.1), we perform tile-based
/// light culling entirely on the CPU using System.Numerics SIMD intrinsics.
///
/// Algorithm:
///   1. Divide screen into 16×16 pixel tiles
///   2. For each tile: compute a frustum from the tile's screen-space bounds
///   3. Test each light's bounding sphere against the tile frustum
///   4. Store up to MAX_LIGHTS_PER_TILE indices per tile
///   5. Upload the tile→light mapping to GPU as a uniform buffer
///
/// Performance target: &lt;2ms for 128 lights on 720p (3600 tiles)
/// </summary>
public class EasePlusLightCuller
{
    /// <summary>Maximum lights per tile. SM 4.1 constant buffer = 4096 vectors.</summary>
    public const int MAX_LIGHTS_PER_TILE = 7;
    
    /// <summary>Tile size in pixels. 16×16 is the sweet spot for HD 3000.</summary>
    public const int TILE_SIZE = 16;
    
    /// <summary>Maximum total dynamic lights in the scene.</summary>
    public const int MAX_LIGHTS = 128;
    
    /// <summary>GPU-uploadable light data.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LightGPU
    {
        public Vector3 Position;
        public float Range;
        public Vector3 Color;
        public float Intensity;
        public Vector3 Direction;  // For directional/spot lights
        public float SpotAngle;    // 0 = point light, >0 = spot
    }
    
    /// <summary>Per-tile data uploaded to GPU.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TileData
    {
        public int LightCount;
        public int Light0, Light1, Light2, Light3;
        public int Light4, Light5, Light6;
        
        public void SetLight(int slot, int lightIndex)
        {
            switch (slot)
            {
                case 0: Light0 = lightIndex; break;
                case 1: Light1 = lightIndex; break;
                case 2: Light2 = lightIndex; break;
                case 3: Light3 = lightIndex; break;
                case 4: Light4 = lightIndex; break;
                case 5: Light5 = lightIndex; break;
                case 6: Light6 = lightIndex; break;
            }
        }
    }
    
    // ── Internal State ───────────────────────────────────────────────────
    private readonly LightGPU[] _lights = new LightGPU[MAX_LIGHTS];
    private int _activeLightCount;
    
    private TileData[] _tileData = Array.Empty<TileData>();
    private int _tilesX, _tilesY;
    
    // Frustum planes per tile (cached to avoid recomputing if camera doesn't move)
    private Vector4[,][] _tileFrustums = new Vector4[0, 0][];
    private Matrix4x4 _lastViewProj;
    
    // Cached arrays for multi-threading
    private readonly Vector4[] _viewSpaceLightPos = new Vector4[MAX_LIGHTS];
    private readonly float[] _lightRanges = new float[MAX_LIGHTS];
    private int _totalTests;
    private int _passedTests;
    
    // ── Stats ────────────────────────────────────────────────────────────
    public float CullTimeMs { get; private set; }
    public int TotalTiles => _tilesX * _tilesY;
    public int ActiveLights => _activeLightCount;
    public int TotalLightTileTests { get; private set; }
    public int PassedLightTileTests { get; private set; }
    
    /// <summary>
    /// Set the screen dimensions. Call on startup and resize.
    /// </summary>
    public void SetScreenSize(uint width, uint height)
    {
        _tilesX = ((int)width + TILE_SIZE - 1) / TILE_SIZE;
        _tilesY = ((int)height + TILE_SIZE - 1) / TILE_SIZE;
        _tileData = new TileData[_tilesX * _tilesY];
        _tileFrustums = new Vector4[_tilesX, _tilesY][];
        _lastViewProj = default; // Force recompute
        
        Console.WriteLine($"[Ease+Culler] Grid: {_tilesX}×{_tilesY} = {_tilesX * _tilesY} tiles ({TILE_SIZE}px each)");
    }
    
    /// <summary>
    /// Clear all lights. Call at the start of each frame.
    /// </summary>
    public void ClearLights()
    {
        _activeLightCount = 0;
    }
    
    /// <summary>
    /// Add a point light. Returns the light index.
    /// </summary>
    public int AddPointLight(Vector3 position, Vector3 color, float intensity, float range)
    {
        if (_activeLightCount >= MAX_LIGHTS) return -1;
        
        int idx = _activeLightCount++;
        _lights[idx] = new LightGPU
        {
            Position = position,
            Range = range,
            Color = color,
            Intensity = intensity,
            Direction = Vector3.Zero,
            SpotAngle = 0 // Point light
        };
        return idx;
    }
    
    /// <summary>
    /// Add a spot light. Returns the light index.
    /// </summary>
    public int AddSpotLight(Vector3 position, Vector3 direction, Vector3 color, 
                            float intensity, float range, float spotAngle)
    {
        if (_activeLightCount >= MAX_LIGHTS) return -1;
        
        int idx = _activeLightCount++;
        _lights[idx] = new LightGPU
        {
            Position = position,
            Range = range,
            Color = color,
            Intensity = intensity,
            Direction = Vector3.Normalize(direction),
            SpotAngle = spotAngle
        };
        return idx;
    }
    
    /// <summary>
    /// Perform tile-based light culling for the current frame.
    /// This is the hot path — every instruction matters.
    /// </summary>
    public void Cull(Matrix4x4 view, Matrix4x4 proj, uint screenWidth, uint screenHeight)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        _totalTests = 0;
        _passedTests = 0;
        
        var viewProj = view * proj;
        Matrix4x4.Invert(proj, out var invProj);
        
        // Rebuild tile frustums if the camera moved
        bool cameraChanged = viewProj != _lastViewProj;
        if (cameraChanged)
        {
            BuildTileFrustums(view, invProj, screenWidth, screenHeight);
            _lastViewProj = viewProj;
        }
        
        // Transform light positions to view space (batch for cache efficiency)
        for (int i = 0; i < _activeLightCount; i++)
        {
            var worldPos = new Vector4(_lights[i].Position, 1.0f);
            var viewPos4 = Vector4.Transform(worldPos, view);
            _viewSpaceLightPos[i] = new Vector4(viewPos4.X, viewPos4.Y, viewPos4.Z, 1.0f);
            _lightRanges[i] = _lights[i].Range;
        }
        
        // ── Main culling loop — iterate tiles, test lights (Parallel) ────
        int totalTiles = _tilesX * _tilesY;
        System.Threading.Tasks.Parallel.For(0, totalTiles, tileIdx =>
        {
            int tx = tileIdx % _tilesX;
            int ty = tileIdx / _tilesX;
            
            ref TileData tile = ref _tileData[tileIdx];
            tile.LightCount = 0;
            
            var frustum = _tileFrustums[tx, ty];
            if (frustum == null) return;
            
            int localTests = 0;
            int localPassed = 0;
            
            // Test each active light against this tile's frustum
            for (int li = 0; li < _activeLightCount && tile.LightCount < MAX_LIGHTS_PER_TILE; li++)
            {
                localTests++;
                
                if (TestSphereAgainstFrustum(frustum, _viewSpaceLightPos[li], _lightRanges[li]))
                {
                    tile.SetLight(tile.LightCount, li);
                    tile.LightCount++;
                    localPassed++;
                }
            }
            
            System.Threading.Interlocked.Add(ref _totalTests, localTests);
            System.Threading.Interlocked.Add(ref _passedTests, localPassed);
        });
        
        TotalLightTileTests = _totalTests;
        PassedLightTileTests = _passedTests;
        
        sw.Stop();
        CullTimeMs = (float)sw.Elapsed.TotalMilliseconds;
    }
    
    /// <summary>
    /// Get the raw tile data for GPU upload.
    /// </summary>
    public ReadOnlySpan<TileData> GetTileData() => _tileData.AsSpan(0, _tilesX * _tilesY);
    
    /// <summary>
    /// Get the raw light data for GPU upload.
    /// </summary>
    public ReadOnlySpan<LightGPU> GetLightData() => _lights.AsSpan(0, _activeLightCount);
    
    /// <summary>
    /// Get tile grid dimensions.
    /// </summary>
    public (int x, int y) GetTileGridSize() => (_tilesX, _tilesY);
    
    // ── Frustum Construction ─────────────────────────────────────────────
    
    /// <summary>
    /// Build 4-plane frustums for each tile in view space.
    /// Each tile frustum has 4 side planes (left, right, top, bottom).
    /// Near/far are handled by the depth range test.
    /// </summary>
    private void BuildTileFrustums(Matrix4x4 view, Matrix4x4 invProj, uint screenW, uint screenH)
    {
        float invW = 1.0f / screenW;
        float invH = 1.0f / screenH;
        
        for (int ty = 0; ty < _tilesY; ty++)
        {
            for (int tx = 0; tx < _tilesX; tx++)
            {
                // Tile bounds in NDC [-1, 1]
                float ndcLeft   = (tx * TILE_SIZE) * invW * 2.0f - 1.0f;
                float ndcRight  = Math.Min((tx + 1) * TILE_SIZE, screenW) * invW * 2.0f - 1.0f;
                float ndcTop    = 1.0f - (ty * TILE_SIZE) * invH * 2.0f;
                float ndcBottom = 1.0f - Math.Min((ty + 1) * TILE_SIZE, screenH) * invH * 2.0f;
                
                // Convert NDC corners to view space using inverse projection
                Vector3 tlView = NDCToView(invProj, ndcLeft, ndcTop);
                Vector3 trView = NDCToView(invProj, ndcRight, ndcTop);
                Vector3 blView = NDCToView(invProj, ndcLeft, ndcBottom);
                Vector3 brView = NDCToView(invProj, ndcRight, ndcBottom);
                
                // Build 4 side planes (normal pointing inward)
                var planes = new Vector4[4];
                planes[0] = MakePlane(Vector3.Zero, blView, tlView); // Left
                planes[1] = MakePlane(Vector3.Zero, trView, brView); // Right
                planes[2] = MakePlane(Vector3.Zero, tlView, trView); // Top
                planes[3] = MakePlane(Vector3.Zero, brView, blView); // Bottom
                
                _tileFrustums[tx, ty] = planes;
            }
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 NDCToView(Matrix4x4 invProj, float ndcX, float ndcY)
    {
        var clip = new Vector4(ndcX, ndcY, 1.0f, 1.0f);
        var view = Vector4.Transform(clip, invProj);
        return new Vector3(view.X / view.W, view.Y / view.W, view.Z / view.W);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector4 MakePlane(Vector3 p0, Vector3 p1, Vector3 p2)
    {
        var normal = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
        float d = -Vector3.Dot(normal, p0);
        return new Vector4(normal, d);
    }
    
    /// <summary>
    /// Test a bounding sphere against 4 frustum planes.
    /// Returns true if the sphere is at least partially inside.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TestSphereAgainstFrustum(Vector4[] planes, Vector4 center, float radius)
    {
        // Vector4.Dot is hardware-accelerated via SIMD
        if (Vector4.Dot(planes[0], center) < -radius) return false;
        if (Vector4.Dot(planes[1], center) < -radius) return false;
        if (Vector4.Dot(planes[2], center) < -radius) return false;
        if (Vector4.Dot(planes[3], center) < -radius) return false;
        return true;
    }
    
    /// <summary>
    /// Print culling statistics.
    /// </summary>
    public void LogStats()
    {
        if (_activeLightCount > 0)
        {
            float cullRate = TotalLightTileTests > 0 
                ? (1.0f - (float)PassedLightTileTests / TotalLightTileTests) * 100f 
                : 0;
            Console.WriteLine($"[Ease+Culler] {_activeLightCount} lights × {TotalTiles} tiles = " +
                            $"{TotalLightTileTests} tests, {PassedLightTileTests} passed " +
                            $"({cullRate:F1}% culled) in {CullTimeMs:F2}ms");
        }
    }
}
