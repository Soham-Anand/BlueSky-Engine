using System;

namespace BlueSky.Core.ECS.Builtin
{
    /// <summary>
    /// Material slot reference - maps a material asset to a specific slot index.
    /// Slots correspond to material indices assigned in Blender/other DCC tools.
    /// </summary>
    public unsafe struct MaterialSlot
    {
        private fixed char _materialAssetId[128];
        private int _slotIndex;
        
        public string MaterialAssetId
        {
            get
            {
                fixed (char* ptr = _materialAssetId)
                {
                    return new string(ptr).TrimEnd('\0');
                }
            }
            set
            {
                value ??= string.Empty;
                int length = System.Math.Min(127, value.Length);
                for (int i = 0; i < length; i++)
                {
                    _materialAssetId[i] = value[i];
                }
                _materialAssetId[length] = '\0';
            }
        }
        
        public int SlotIndex
        {
            get => _slotIndex;
            set => _slotIndex = value;
        }
        
        public bool IsEmpty => string.IsNullOrEmpty(MaterialAssetId);
    }

    public unsafe struct StaticMeshComponent
    {
        private fixed char _meshAssetId[128];
        private fixed char _materialAssetId[128]; // Legacy single material support
        
        // Material slots - up to 8 materials per mesh (Blender supports many, we limit to 8 for perf)
        private fixed char _materialSlot0[128];
        private fixed char _materialSlot1[128];
        private fixed char _materialSlot2[128];
        private fixed char _materialSlot3[128];
        private fixed char _materialSlot4[128];
        private fixed char _materialSlot5[128];
        private fixed char _materialSlot6[128];
        private fixed char _materialSlot7[128];
        private int _slotCount;
        
        public string MeshAssetId
        {
            get
            {
                fixed (char* ptr = _meshAssetId)
                {
                    return new string(ptr).TrimEnd('\0');
                }
            }
            set
            {
                value ??= string.Empty;
                int length = System.Math.Min(127, value.Length);
                for (int i = 0; i < length; i++)
                {
                    _meshAssetId[i] = value[i];
                }
                _meshAssetId[length] = '\0';
            }
        }
        
        /// <summary>
        /// Legacy single material. Returns first slot material or empty if none assigned.
        /// </summary>
        public string MaterialAssetId
        {
            get
            {
                fixed (char* ptr = _materialAssetId)
                {
                    return new string(ptr).TrimEnd('\0');
                }
            }
            set
            {
                value ??= string.Empty;
                int length = System.Math.Min(127, value.Length);
                for (int i = 0; i < length; i++)
                {
                    _materialAssetId[i] = value[i];
                }
                _materialAssetId[length] = '\0';
                
                // Also set as first slot for consistency
                SetMaterialSlot(0, value);
            }
        }
        
        /// <summary>
        /// Get material asset ID for a specific slot. Returns empty string if slot is unassigned.
        /// </summary>
        public string GetMaterialSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= 8) return string.Empty;
            
            fixed (char* ptr0 = _materialSlot0)
            {
                char* ptr = ptr0 + slotIndex * 128;
                return new string(ptr).TrimEnd('\0');
            }
        }
        
        /// <summary>
        /// Set material asset ID for a specific slot index.
        /// </summary>
        public void SetMaterialSlot(int slotIndex, string materialAssetId)
        {
            if (slotIndex < 0 || slotIndex >= 8) return;
            
            materialAssetId ??= string.Empty;
            int length = System.Math.Min(127, materialAssetId.Length);
            
            fixed (char* ptr0 = _materialSlot0)
            {
                char* ptr = ptr0 + slotIndex * 128;
                for (int i = 0; i < length; i++)
                {
                    ptr[i] = materialAssetId[i];
                }
                ptr[length] = '\0';
            }
            
            _slotCount = System.Math.Max(_slotCount, slotIndex + 1);
        }
        
        /// <summary>
        /// Number of material slots that have been assigned.
        /// </summary>
        public int SlotCount => _slotCount;
        
        /// <summary>
        /// Get the effective material for a slot index.
        /// If the slot is empty, returns empty string (renderer will use default white).
        /// </summary>
        public string GetEffectiveMaterial(int slotIndex)
        {
            var material = GetMaterialSlot(slotIndex);
            return material; // Empty means use default white material
        }
    }
}
