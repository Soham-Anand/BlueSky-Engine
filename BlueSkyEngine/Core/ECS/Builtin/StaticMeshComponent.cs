/*
 * StaticMeshComponent — ECS component for static mesh rendering.
 *
 * DESIGN CONSTRAINTS:
 *   - Must be an unmanaged struct so the ECS archetype system can store it in
 *     contiguous blobs without GC pressure.
 *   - Fixed char arrays give us inline string storage with zero heap allocation
 *     during iteration.
 *
 * NOTE: No surface/color data lives here. Meshes carry geometry only;
 * every surface shades with the single global clay until Strata lands.
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

        // ── Storage ──────────────────────────────────────────────────────────
        private fixed char _meshAssetId[PathCapacity];

        // ── MeshAssetId ──────────────────────────────────────────────────────
        public string MeshAssetId
        {
            get { fixed (char* p = _meshAssetId) return ReadFixed(p, PathCapacity); }
            set { fixed (char* p = _meshAssetId) WriteFixed(p, PathCapacity, value, nameof(MeshAssetId)); }
        }

        // ── IsStatic ─────────────────────────────────────────────────────────
        public bool IsStatic { get; set; }

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
