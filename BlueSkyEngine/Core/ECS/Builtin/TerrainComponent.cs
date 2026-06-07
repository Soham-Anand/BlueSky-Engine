using System;

namespace BlueSky.Core.ECS.Builtin;

/// <summary>
/// Terrain component - stores heightmap metadata.
/// Actual heightmap data is stored in TerrainSystem to keep component unmanaged.
/// </summary>
public unsafe struct TerrainComponent
{
    private const int PathCapacity = 320;
    private fixed char _terrainAssetPath[PathCapacity];

    // Terrain dimensions
    public int Width;           // Heightmap width (e.g., 256)
    public int Height;          // Heightmap height (e.g., 256)
    
    // World space dimensions
    public float WorldWidth;    // Terrain width in world units (e.g., 100.0)
    public float WorldHeight;   // Terrain height in world units (e.g., 100.0)
    public float MaxElevation;  // Maximum height in world units (e.g., 20.0)
    
    // Rendering
    public bool NeedsRebuild;   // Flag to regenerate mesh
    public uint MeshHandle;     // Handle to generated mesh (0 = none)
    public int ChunkSize;       // Quads per chunk edge. 32 is the HD 3000 default.
    public int LodCount;        // 1-3. Default: full, half, quarter.
    public int MaterialMode;    // 0=simple 2-layer, 1=4-layer data stored for later high quality.
    public bool CollisionEnabled;

    public string TerrainAssetPath
    {
        get { fixed (char* p = _terrainAssetPath) return ReadFixed(p, PathCapacity); }
        set { fixed (char* p = _terrainAssetPath) WriteFixed(p, PathCapacity, value, nameof(TerrainAssetPath)); }
    }
    
    public TerrainComponent()
    {
        Width = 256;
        Height = 256;
        WorldWidth = 100.0f;
        WorldHeight = 100.0f;
        MaxElevation = 20.0f;
        NeedsRebuild = true;
        MeshHandle = 0;
        ChunkSize = 32;
        LodCount = 3;
        MaterialMode = 0;
        CollisionEnabled = true;
    }

    private static string ReadFixed(char* ptr, int capacity)
    {
        int len = 0;
        while (len < capacity && ptr[len] != '\0') len++;
        return len == 0 ? string.Empty : new string(ptr, 0, len);
    }

    private static void WriteFixed(char* ptr, int capacity, string? value, string fieldName)
    {
        value ??= string.Empty;
        int len = System.Math.Min(value.Length, capacity - 1);
        if (value.Length >= capacity)
            Console.WriteLine($"[TerrainComponent] Path too long for {fieldName}; truncating to {capacity - 1} chars.");

        for (int i = 0; i < len; i++) ptr[i] = value[i];
        ptr[len] = '\0';
    }
}

public enum BrushMode
{
    Raise,
    Lower,
    Smooth,
    Flatten,
    Paint,
    Noise,
    Erode,
    Erase
}

public enum TerrainMaterialMode
{
    SimpleTwoLayer = 0,
    FourLayer = 1
}
