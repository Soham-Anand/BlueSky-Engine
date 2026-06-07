using System;

namespace BlueSky.Core.ECS.Builtin;

/// <summary>
/// ECS component referencing a skeletal mesh asset by file path.
/// The referenced mesh must contain the required bone hierarchy
/// (e.g. RightFront_Wheel, LeftFront_Wheel, etc. for vehicle entities).
/// </summary>
public unsafe struct SkeletalMeshComponent
{
    private const int PathCapacity = 320;
    private const int MaxInlineSlots = 8;

    private fixed char _meshAssetPath[PathCapacity];
    private fixed char _slots[MaxInlineSlots * PathCapacity];
    private int _inlineSlotCount;

    /// <summary>
    /// File path to the skeletal mesh asset (FBX, GLB, etc.)
    /// </summary>
    public string MeshAssetPath
    {
        get { fixed (char* p = _meshAssetPath) return ReadFixed(p, PathCapacity); }
        set { fixed (char* p = _meshAssetPath) WriteFixed(p, PathCapacity, value); }
    }

    /// <summary>
    /// Whether the skeletal mesh has been loaded and validated
    /// </summary>
    public bool IsLoaded;

    public SkeletalMeshComponent(string meshAssetPath)
    {
        _meshAssetPath[0] = '\0';
        MeshAssetPath = meshAssetPath ?? string.Empty;
        IsLoaded = false;
        _inlineSlotCount = 0;
    }

    /// <summary>
    /// Returns the material asset path for the inline material slot, or empty if unset.
    /// Slots 8+ are resolved from asset metadata by render-time systems.
    /// </summary>
    public string GetMaterialSlot(int slotIndex)
    {
        if ((uint)slotIndex >= MaxInlineSlots) return string.Empty;
        fixed (char* p0 = _slots)
            return ReadFixed(p0 + slotIndex * PathCapacity, PathCapacity);
    }

    /// <summary>
    /// Assigns a material asset path to an inline material slot.
    /// </summary>
    public void SetMaterialSlot(int slotIndex, string path)
    {
        if ((uint)slotIndex >= MaxInlineSlots) return;
        fixed (char* p0 = _slots)
            WriteFixed(p0 + slotIndex * PathCapacity, PathCapacity, path);
        _inlineSlotCount = System.Math.Max(_inlineSlotCount, slotIndex + 1);
    }

    public int InlineSlotCount => _inlineSlotCount;

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
