using System;

namespace BlueSky.SkyCore.Builtin;

public unsafe struct TerrainComponent
{
    private const int PathCapacity = 320;
    private fixed char _terrainAssetPath[PathCapacity];

    public int Width;
    public int Height;
    
    public float WorldWidth;
    public float WorldHeight;
    public float MaxElevation;
    
    public bool NeedsRebuild;
    public uint MeshHandle;
    public int ChunkSize;
    public int LodCount;
    public int SurfaceMode;
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
        SurfaceMode = 0;
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

public enum TerrainSurfaceMode
{
    SimpleTwoLayer = 0,
    FourLayer = 1
}
