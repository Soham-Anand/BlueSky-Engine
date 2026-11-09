using System;
using BlueSky.Aegis.Detection.Probes;

namespace BlueSky.Aegis.Detection
{
    /// <summary>
    /// Collects host GPU and CPU information plus the Metal/DX/Vulkan hardware features
    /// used by the RHI and engine subsystems.
    /// </summary>
    public class IntelligentQueryLayer
    {
        private static IntelligentQueryLayer? _instance;
        private static readonly object _lock = new();

        /// <summary>
        /// Get the global instance. Throws if Probe() has not been called yet.
        /// </summary>
        public static IntelligentQueryLayer Instance
        {
            get
            {
                if (_instance == null)
                    throw new InvalidOperationException(
                        "IntelligentQueryLayer not probed. Call IntelligentQueryLayer.Probe() during startup.");
                return _instance;
            }
        }

        /// <summary>
        /// Probe all hardware capabilities and store as the global instance.
        /// Safe to call multiple times; subsequent calls return the cached probe result unless forceRefresh is true.
        /// </summary>
        public static IntelligentQueryLayer Probe(bool forceRefresh = false)
        {
            lock (_lock)
            {
                if (_instance != null && !forceRefresh) return _instance;

                var layer = new IntelligentQueryLayer();
                layer.Detect();
                _instance = layer;
                return _instance;
            }
        }

        // ── GPU & CPU Info ──
        public GpuCapabilities Gpu { get; private set; }
        public ProcessorCapabilities Cpu { get; private set; }

        // ── Metal-specific (macOS only) ──
        public bool SupportsMetal { get; private set; }
        public bool SupportsMetal3 { get; private set; }
        public bool SupportsMetal4 { get; private set; }
        public MetalGpuFamily MetalFamily { get; private set; }
        public MetalCapabilities? MetalCaps { get; private set; }

        // ── Feature queries (cross-platform, RHI-aware) ──
        public bool SupportsCompute { get; private set; }
        public bool SupportsIndirectDrawing { get; private set; }
        public bool SupportsRayTracing { get; private set; }
        public bool SupportsMeshShaders { get; private set; }
        public bool SupportsBCTextureCompression { get; private set; }
        public bool SupportsFloat32Filtering { get; private set; }
        public bool SupportsShaderBarycentrics { get; private set; }
        public bool HasUnifiedMemory => Gpu.HasUnifiedMemory;

        private IntelligentQueryLayer() { }

        private void Detect()
        {
            Console.WriteLine("================================================================================");
            Console.WriteLine("INTELLIGENT QUERY LAYER — Probing host capabilities");
            Console.WriteLine("================================================================================");

            // Step 1: CPU detection (runs sysctl/proc first so memory & topology are ready)
            Console.WriteLine("[1/3] Detecting CPU & Memory Topology...");
            Cpu = ProcessorCapabilities.Probe();

            // Step 2: GPU detection (cross-platform; on macOS this also populates MacOSProbe.LastMetalCapabilities)
            Console.WriteLine("[2/3] Detecting GPU...");
            Gpu = GpuDetector.Probe();

            // Step 3: Metal-specific capabilities (macOS only — reuses MacOSProbe's Metal probe to avoid double P/Invoke)
            Console.WriteLine("[3/3] Resolving RHI & Shader Feature Matrix...");
            if (OperatingSystem.IsMacOS())
            {
                MetalCaps = MacOSProbe.LastMetalCapabilities ?? MacMetalProbe.Probe();
                if (MetalCaps.HasValue)
                {
                    var m = MetalCaps.Value;
                    SupportsMetal = m.SupportsMetal;
                    SupportsMetal3 = m.SupportsMetal3;
                    SupportsMetal4 = m.SupportsMetal4;
                    MetalFamily = m.GpuFamily;
                    SupportsCompute = m.SupportsCompute;
                    SupportsIndirectDrawing = m.SupportsIndirectDrawing;
                    SupportsRayTracing = m.SupportsRaytracing;
                    SupportsMeshShaders = m.SupportsMeshShaders;
                    SupportsBCTextureCompression = m.SupportsBCTextureCompression;
                    SupportsFloat32Filtering = m.Supports32BitFloatFiltering;
                    SupportsShaderBarycentrics = m.SupportsShaderBarycentricCoordinates;
                }
            }
            else
            {
                SupportsCompute = Gpu.SupportsCompute;
                SupportsIndirectDrawing = Gpu.SupportsIndirectDrawing;
                SupportsRayTracing = Gpu.SupportsRayTracing;
                SupportsMeshShaders = Gpu.SupportsMeshShaders;
                SupportsBCTextureCompression = Gpu.SupportsBCTextureCompression;
                SupportsFloat32Filtering = Gpu.SupportsFloat32Filtering;
                SupportsShaderBarycentrics = Gpu.SupportsShaderBarycentrics;
            }

            PrintSummary();
            Console.WriteLine("================================================================================");
        }

        private void PrintSummary()
        {
            Console.WriteLine();
            Console.WriteLine("── Capability Summary ──");
            string gpuCores = Gpu.GpuCoreCount > 0 ? $", {Gpu.GpuCoreCount} GPU Cores" : "";
            string gpuArch = !string.IsNullOrEmpty(Gpu.Architecture) ? $" [{Gpu.Architecture}]" : "";
            Console.WriteLine($"  GPU: {Gpu.Name}{gpuArch} ({Gpu.Vendor}{gpuCores}), {Gpu.VramMB}MB {(Gpu.HasUnifiedMemory ? "Unified Working Set" : "VRAM")}, Tier={Gpu.Tier}");
            Console.WriteLine($"  CPU: {Cpu.BrandString} [{Cpu.Architecture}] — {Cpu.PhysicalCoreCount}P/{Cpu.LogicalCoreCount}L cores (Perf={Cpu.PerformanceCoreCount}, Eff={Cpu.EfficiencyCoreCount}), CacheLine={Cpu.CacheLineSize}B, RAM={Cpu.TotalSystemMemoryMB}MB");
            Console.WriteLine($"  SIMD: Vector128={Cpu.Vector128HardwareAccelerated}, Vector256={Cpu.Vector256HardwareAccelerated}, AVX={Cpu.SupportsAvx}, AVX2={Cpu.SupportsAvx2}, SSE2={Cpu.SupportsSse2}, ARM NEON={Cpu.SupportsArmSimd} (Optimal={Cpu.OptimalSimdWidthFloats}-wide float)");

            if (SupportsMetal)
            {
                Console.WriteLine($"  Metal Family: {MetalFamily.DisplayName()} (Metal3={SupportsMetal3}, Metal4={SupportsMetal4})");
                Console.WriteLine($"  Metal Features: Compute={SupportsCompute}, Indirect={SupportsIndirectDrawing}, MeshShaders={SupportsMeshShaders}, RayTracing={SupportsRayTracing}, BCCompression={SupportsBCTextureCompression}");
            }
        }
    }
}
