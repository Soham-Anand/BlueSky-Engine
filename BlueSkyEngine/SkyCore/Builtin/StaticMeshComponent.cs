using System;

namespace BlueSky.SkyCore.Builtin
{
    public unsafe struct StaticMeshComponent
    {
        private const int PathCapacity = 320;
        private fixed char _meshAssetId[PathCapacity];

        public string MeshAssetId
        {
            get { fixed (char* p = _meshAssetId) return ReadFixed(p, PathCapacity); }
            set { fixed (char* p = _meshAssetId) WriteFixed(p, PathCapacity, value, nameof(MeshAssetId)); }
        }

        public bool IsStatic { get; set; }

        private static string ReadFixed(char* ptr, int capacity)
        {
            int len = 0;
            while (len < capacity && ptr[len] != '\0') len++;
            return len == 0 ? string.Empty : new string(ptr, 0, len);
        }

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
