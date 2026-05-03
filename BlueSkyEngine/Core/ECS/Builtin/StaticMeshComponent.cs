/*
 * StaticMeshComponent — ECS component for static mesh rendering.
 *
 * DESIGN CONSTRAINTS:
 *   - Must be an unmanaged struct so the ECS archetype system can store it in
 *     contiguous blobs without GC pressure.
 *   - Fixed char arrays give us inline string storage with zero heap allocation
 *     during iteration.
 *
 * MATERIAL SLOT STRATEGY:
 *   - Slots 0-7 are stored inline in this struct (PathCapacity chars each).
 *   - Slots 8+ cannot fit here without blowing the struct size past what the ECS
 *     chunk allocator can handle.  They are stored in the mesh asset's metadata
 *     and resolved at render time via MeshGPUData.MaterialSlotPaths (cached once
 *     on first GPU upload, zero per-frame cost).
 *
 * PATH CAPACITY:
 *   - 320 chars covers the longest realistic absolute path on macOS/Windows
 *     (260-char Windows MAX_PATH + some headroom).
 *   - If a path is still too long we log a hard warning — silent truncation was
 *     the root cause of the "black car" bug.
 */

using System;

namespace BlueSky.Core.ECS.Builtin
{
    public unsafe struct StaticMeshComponent
    {
        // ── Constants ────────────────────────────────────────────────────────
        private const int PathCapacity = 320;   // chars per path (covers Windows MAX_PATH + headroom)
        private const int MaxInlineSlots = 8;   // slots stored inline; 8+ live in asset metadata

        // ── Storage ──────────────────────────────────────────────────────────
        private fixed char _meshAssetId[PathCapacity];
        private fixed char _legacyMaterialId[PathCapacity]; // kept for scene-file back-compat
        private fixed char _slots[MaxInlineSlots * PathCapacity];
        private int        _inlineSlotCount;    // highest slot index written + 1

        // ── MeshAssetId ──────────────────────────────────────────────────────
        public string MeshAssetId
        {
            get { fixed (char* p = _meshAssetId) return ReadFixed(p, PathCapacity); }
            set { fixed (char* p = _meshAssetId) WriteFixed(p, PathCapacity, value, nameof(MeshAssetId)); }
        }

        // ── IsStatic ─────────────────────────────────────────────────────────
        public bool IsStatic { get; set; }

        // ── Legacy single-material (back-compat) ─────────────────────────────
        /// <summary>
        /// Legacy single-material field kept for old scene files.
        /// Setting this also writes slot 0 so new code sees it.
        /// </summary>
        public string MaterialAssetId
        {
            get { fixed (char* p = _legacyMaterialId) return ReadFixed(p, PathCapacity); }
            set
            {
                fixed (char* p = _legacyMaterialId) WriteFixed(p, PathCapacity, value, nameof(MaterialAssetId));
                SetMaterialSlot(0, value ?? string.Empty);
            }
        }

        // ── Per-slot API ─────────────────────────────────────────────────────
        /// <summary>
        /// Returns the material asset path for <paramref name="slotIndex"/>.
        /// Returns <see cref="string.Empty"/> for out-of-range or unassigned slots.
        /// Slots ≥ 8 are NOT stored here — the renderer reads them from
        /// <c>MeshGPUData.MaterialSlotPaths</c>.
        /// </summary>
        public string GetMaterialSlot(int slotIndex)
        {
            if ((uint)slotIndex >= MaxInlineSlots) return string.Empty;
            fixed (char* p0 = _slots)
                return ReadFixed(p0 + slotIndex * PathCapacity, PathCapacity);
        }

        /// <summary>
        /// Assigns a material asset path to <paramref name="slotIndex"/>.
        /// Silently ignores slots ≥ 8 (those are handled via asset metadata).
        /// </summary>
        public void SetMaterialSlot(int slotIndex, string path)
        {
            if ((uint)slotIndex >= MaxInlineSlots) return;
            fixed (char* p0 = _slots)
                WriteFixed(p0 + slotIndex * PathCapacity, PathCapacity, path, $"slot[{slotIndex}]");
            _inlineSlotCount = System.Math.Max(_inlineSlotCount, slotIndex + 1);
        }

        /// <summary>Number of inline slots that have been written (0-8).</summary>
        public int InlineSlotCount => _inlineSlotCount;

        /// <summary>
        /// Returns the material path for <paramref name="slotIndex"/>, or
        /// <see cref="string.Empty"/> if unassigned.
        /// </summary>
        public string GetEffectiveMaterial(int slotIndex) => GetMaterialSlot(slotIndex);

        // ── Helpers ──────────────────────────────────────────────────────────
        /// <summary>Read a null-terminated string from a fixed char buffer.</summary>
        private static string ReadFixed(char* ptr, int capacity)
        {
            // Find null terminator manually — avoids allocating a full-capacity string
            int len = 0;
            while (len < capacity && ptr[len] != '\0') len++;
            return len == 0 ? string.Empty : new string(ptr, 0, len);
        }

        /// <summary>Write a string into a fixed char buffer, null-terminating it.</summary>
        private static void WriteFixed(char* ptr, int capacity, string? value, string fieldName)
        {
            value ??= string.Empty;
            int len = value.Length;
            if (len >= capacity)
            {
                Console.WriteLine(
                    $"[StaticMeshComponent] PATH TOO LONG for '{fieldName}' " +
                    $"({len} chars, max {capacity - 1}). Truncating: {value}");
                len = capacity - 1;
            }
            for (int i = 0; i < len; i++) ptr[i] = value[i];
            ptr[len] = '\0';
        }
    }
}
