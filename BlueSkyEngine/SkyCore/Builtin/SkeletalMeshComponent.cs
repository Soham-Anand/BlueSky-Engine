using System;

namespace BlueSky.SkyCore.Builtin;

public unsafe struct SkeletalMeshComponent
{
    private const int PathCapacity = 320;
    private fixed char _meshAssetPath[PathCapacity];

    public string MeshAssetPath
    {
        get { fixed (char* p = _meshAssetPath) return ReadFixed(p, PathCapacity); }
        set { fixed (char* p = _meshAssetPath) WriteFixed(p, PathCapacity, value); }
    }

    public bool IsLoaded;

    public SkeletalMeshComponent(string meshAssetPath)
    {
        _meshAssetPath[0] = '\0';
        MeshAssetPath = meshAssetPath ?? string.Empty;
        IsLoaded = false;
    }

    private static string ReadFixed(char* ptr, int capacity)
    {
        int len = 0;
        while (len < capacity && ptr[len] != '\0') len++;
        return len == 0 ? string.Empty : new string(ptr, 0, len);
    }

    private static void WriteFixed(char* ptr, int capacity, string? value)
    {
        value ??= string.Empty;
        int len = System.Math.Min(value.Length, capacity - 1);
        for (int i = 0; i < len; i++) ptr[i] = value[i];
        ptr[len] = '\0';
    }
}
