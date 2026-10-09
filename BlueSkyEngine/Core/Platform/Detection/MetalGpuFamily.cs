namespace BlueSky.Core.Platform.Detection
{
    /// <summary>
    /// Metal GPU family detection — maps to [MTLDevice supportsFamily:] on macOS.
    /// Used by IntelligentQueryLayer to determine compute and indirect support.
    /// </summary>
    public enum MetalGpuFamily
    {
        Unknown = 0,

        // ── Intel Macs (2012+) ──
        /// <summary>Intel HD 4000 (Ivy Bridge, 2012) — Metal 1.0</summary>
        Mac1 = 1000,
        /// <summary>Intel HD 5000+ (Haswell, 2013) — Metal 1.1</summary>
        Mac2 = 1001,
        /// <summary>Intel Iris Plus (Broadwell, 2015) — Metal 1.2</summary>
        Mac3 = 1002,
        /// <summary>Intel UHD 630 (Coffee Lake, 2017) — Metal 2.0</summary>
        Mac4 = 1003,

        // ── Apple Silicon ──
        /// <summary>M1 — Metal 3.0</summary>
        Apple1 = 2000,
        /// <summary>M1 Pro/Max/Ultra — Metal 3.0</summary>
        Apple2 = 2001,
        /// <summary>M2 — Metal 3.1</summary>
        Apple3 = 2002,
        /// <summary>M3 — Metal 3.2</summary>
        Apple4 = 2003,
        /// <summary>M4 — Metal 3.3</summary>
        Apple5 = 2004,
    }

    public static class MetalGpuFamilyExtensions
    {
        /// <summary>
        /// Whether this GPU family supports compute shaders.
        /// All Metal GPUs (Intel HD 4000+) support compute (Metal 1.0).
        /// </summary>
        public static bool SupportsCompute(this MetalGpuFamily family) =>
            family >= MetalGpuFamily.Mac1;

        /// <summary>
        /// Whether this GPU family supports indirect drawing.
        /// All Metal GPUs (Intel HD 4000+) support indirect drawing (Metal 1.0).
        /// </summary>
        public static bool SupportsIndirectDrawing(this MetalGpuFamily family) =>
            family >= MetalGpuFamily.Mac1;

        /// <summary>
        /// Human-readable name for logging.
        /// </summary>
        public static string DisplayName(this MetalGpuFamily family) => family switch
        {
            MetalGpuFamily.Mac1 => "Mac1 (Intel HD 4000, Metal 1.0)",
            MetalGpuFamily.Mac2 => "Mac2 (Intel HD 5000+, Metal 1.1)",
            MetalGpuFamily.Mac3 => "Mac3 (Intel Iris Plus, Metal 1.2)",
            MetalGpuFamily.Mac4 => "Mac4 (Intel UHD 630, Metal 2.0)",
            MetalGpuFamily.Apple1 => "Apple1 (M1, Metal 3.0)",
            MetalGpuFamily.Apple2 => "Apple2 (M1 Pro/Max/Ultra, Metal 3.0)",
            MetalGpuFamily.Apple3 => "Apple3 (M2, Metal 3.1)",
            MetalGpuFamily.Apple4 => "Apple4 (M3, Metal 3.2)",
            MetalGpuFamily.Apple5 => "Apple5 (M4, Metal 3.3)",
            _ => "Unknown Metal GPU"
        };
    }
}
