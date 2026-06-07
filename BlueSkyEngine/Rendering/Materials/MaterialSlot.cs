// Material slot system - maps submeshes to materials (Blender-style)
// Each submesh can have its own material assigned via slot index

using System;
using System.Collections.Generic;

namespace BlueSky.Rendering.Materials;

/// <summary>
/// Material slot - maps a submesh index to a material asset.
/// Matches Blender's material slot system.
/// </summary>
public class MaterialSlot
{
    public int SlotIndex { get; set; }
    public string MaterialAssetPath { get; set; } = "";
    public string SlotName { get; set; } = "Material";
    
    public bool IsAssigned => !string.IsNullOrEmpty(MaterialAssetPath);
}

/// <summary>
/// Material slot collection for a mesh.
/// Automatically detected from FBX material assignments.
/// </summary>
public class MaterialSlotCollection
{
    private readonly List<MaterialSlot> _slots = new();
    
    public int Count => _slots.Count;
    
    public MaterialSlot this[int index]
    {
        get
        {
            if (index < 0 || index >= _slots.Count)
                throw new IndexOutOfRangeException($"Material slot {index} out of range (0-{_slots.Count - 1})");
            return _slots[index];
        }
    }
    
    public void AddSlot(string name = "Material")
    {
        _slots.Add(new MaterialSlot
        {
            SlotIndex = _slots.Count,
            SlotName = name,
            MaterialAssetPath = ""
        });
    }
    
    public void SetMaterial(int slotIndex, string materialAssetPath)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Count)
            throw new IndexOutOfRangeException($"Material slot {slotIndex} out of range");
        
        _slots[slotIndex].MaterialAssetPath = materialAssetPath;
    }
    
    public string GetMaterial(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Count)
            return "";
        return _slots[slotIndex].MaterialAssetPath;
    }
    
    public IEnumerable<MaterialSlot> GetSlots() => _slots;
    
    public void Clear() => _slots.Clear();
}
