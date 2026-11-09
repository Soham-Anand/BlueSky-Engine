namespace BlueSky.Aegis.Detection
{
    /// <summary>
    /// Metal GPU family detection — maps to Apple's official [MTLDevice supportsFamily:]
    /// constants in &lt;Metal/MTLDevice.h&gt; while retaining legacy enum aliases so existing
    /// callers remain source-compatible.
    /// </summary>
    public enum MetalGpuFamily
    {
        Unknown = 0,

        // ── Intel / x86_64 Macs (Metal 1.0 – 2.x) ──
        /// <summary>MTLGPUFamilyMac1 (Ivy Bridge / Haswell / Broadwell baseline)</summary>
        Mac1 = 1000,
        /// <summary>Legacy Mac tier alias (Haswell / Metal 1.1)</summary>
        Mac2 = 1001,
        /// <summary>Legacy Mac tier alias (Broadwell / Skylake)</summary>
        Mac3 = 1002,
        /// <summary>MTLGPUFamilyMac2 (Coffee Lake / Vega / Navi, Metal 2+)</summary>
        Mac4 = 1003,

        // ── Apple Family GPUs (MTLGPUFamilyApple1..Apple10) ──
        /// <summary>MTLGPUFamilyApple1 (A7) / Legacy M1 alias</summary>
        Apple1 = 2000,
        /// <summary>MTLGPUFamilyApple2 (A8) / Legacy M1 Pro/Max/Ultra alias</summary>
        Apple2 = 2001,
        /// <summary>MTLGPUFamilyApple3 (A9/A10) / Legacy M2 alias</summary>
        Apple3 = 2002,
        /// <summary>MTLGPUFamilyApple4 (A11) / Legacy M3 alias</summary>
        Apple4 = 2003,
        /// <summary>MTLGPUFamilyApple5 (A12) / Legacy M4 alias</summary>
        Apple5 = 2004,
        /// <summary>MTLGPUFamilyApple6 (A13 Bionic)</summary>
        Apple6 = 2005,
        /// <summary>MTLGPUFamilyApple7 (M1 / M1 Pro / M1 Max / M1 Ultra / A14)</summary>
        Apple7 = 2006,
        /// <summary>MTLGPUFamilyApple8 (M2 / M2 Pro / M2 Max / M2 Ultra / A15 / A16)</summary>
        Apple8 = 2007,
        /// <summary>MTLGPUFamilyApple9 (M3 &amp; M4 families / A17 Pro — Hardware Ray Tracing, Mesh Shaders, Dynamic Caching)</summary>
        Apple9 = 2008,
        /// <summary>MTLGPUFamilyApple10 (M5+ / A18 Pro+ next-gen Apple GPU architecture)</summary>
        Apple10 = 2009,
    }

    /// <summary>
    /// Official raw MTLGPUFamily integer constants from &lt;Metal/MTLDevice.h&gt; for [MTLDevice supportsFamily:].
    /// </summary>
    public static class NativeMetalGpuFamily
    {
        public const long Apple1 = 1001;
        public const long Apple2 = 1002;
        public const long Apple3 = 1003;
        public const long Apple4 = 1004;
        public const long Apple5 = 1005;
        public const long Apple6 = 1006;
        public const long Apple7 = 1007; // M1 family
        public const long Apple8 = 1008; // M2 family
        public const long Apple9 = 1009; // M3 & M4 families
        public const long Apple10 = 1010; // M5+ / A18+

        public const long Mac1 = 2001;
        public const long Mac2 = 2002;

        public const long Common1 = 3001;
        public const long Common2 = 3002;
        public const long Common3 = 3003;

        public const long Metal3 = 5001;
        public const long Metal4 = 5002;
    }

    public static class MetalGpuFamilyExtensions
    {
        /// <summary>
        /// Whether this GPU family supports compute shaders (Metal 1.0+).
        /// </summary>
        public static bool SupportsCompute(this MetalGpuFamily family) =>
            family >= MetalGpuFamily.Mac1;

        /// <summary>
        /// Whether this GPU family supports indirect command buffers / indirect drawing.
        /// </summary>
        public static bool SupportsIndirectDrawing(this MetalGpuFamily family) =>
            family >= MetalGpuFamily.Mac1;

        /// <summary>
        /// Whether this GPU family supports tile shaders / imageblocks (Apple4+ / Apple Silicon).
        /// </summary>
        public static bool SupportsTileShaders(this MetalGpuFamily family) =>
            family >= MetalGpuFamily.Apple4;

        /// <summary>
        /// Whether this GPU family supports Metal 3 mesh shaders (Apple7+ / M1+ or Mac4 / Mac2 native).
        /// </summary>
        public static bool SupportsMeshShaders(this MetalGpuFamily family) =>
            family >= MetalGpuFamily.Apple7 || family == MetalGpuFamily.Mac4;

        /// <summary>
        /// Whether this GPU family has hardware-accelerated ray tracing &amp; dynamic caching (Apple9+ / M3 / M4+).
        /// </summary>
        public static bool SupportsHardwareRayTracing(this MetalGpuFamily family) =>
            family >= MetalGpuFamily.Apple9;

        /// <summary>
        /// Whether this GPU family supports Argument Buffers Tier 2 (Apple6+ / M1+ or Mac4).
        /// </summary>
        public static bool SupportsArgumentBuffersTier2(this MetalGpuFamily family) =>
            family >= MetalGpuFamily.Apple6 || family == MetalGpuFamily.Mac4;

        /// <summary>
        /// Human-readable name for logging.
        /// </summary>
        public static string DisplayName(this MetalGpuFamily family) => family switch
        {
            MetalGpuFamily.Mac1 => "Mac1 (Intel Ivy Bridge+, Metal 1.0)",
            MetalGpuFamily.Mac2 => "Mac1/Haswell (Intel HD 5000+, Metal 1.1)",
            MetalGpuFamily.Mac3 => "Mac2/Broadwell (Intel Iris, Metal 1.2)",
            MetalGpuFamily.Mac4 => "Mac2 (Modern Intel/AMD macOS, Metal 2.0+)",
            MetalGpuFamily.Apple1 => "Apple1 (A7)",
            MetalGpuFamily.Apple2 => "Apple2 (A8)",
            MetalGpuFamily.Apple3 => "Apple3 (A9/A10)",
            MetalGpuFamily.Apple4 => "Apple4 (A11)",
            MetalGpuFamily.Apple5 => "Apple5 (A12)",
            MetalGpuFamily.Apple6 => "Apple6 (A13)",
            MetalGpuFamily.Apple7 => "Apple7 (Apple M1 Family, Metal 3)",
            MetalGpuFamily.Apple8 => "Apple8 (Apple M2 Family, Metal 3)",
            MetalGpuFamily.Apple9 => "Apple9 (Apple M3/M4 Family, Metal 3/4 + HW Ray Tracing)",
            MetalGpuFamily.Apple10 => "Apple10 (Apple M5+ Family, Metal 4)",
            _ => "Unknown Metal GPU"
        };
    }
}
