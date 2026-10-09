using System;
using BlueSky.Core.Platform.Detection.Probes;

namespace BlueSky.Core.Platform.Detection
{
    /// <summary>
    /// Collects host GPU and CPU information plus the Metal features used by
    /// the Metal RHI. It does not select settings for unrelated subsystems.
    /// </summary>
    public class IntelligentQueryLayer
    {
        private static IntelligentQueryLayer? _instance;
        private static readonly object _lock = new();

        /// <summary>
        /// Get the global instance. Returns null if not yet probed.
        /// Call Probe() during startup before any engine subsystem initializes.
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
        /// Called once at engine startup, before RHI device creation.
        /// </summary>
        public static IntelligentQueryLayer Probe()
        {
            lock (_lock)
            {
                if (_instance != null) return _instance;

                var layer = new IntelligentQueryLayer();
                layer.Detect();
                _instance = layer;
                return _instance;
            }
        }

        // ── GPU Info ──
        public GpuCapabilities Gpu { get; private set; }
        public ProcessorCapabilities Cpu { get; private set; }

        // ── Metal-specific (macOS only) ──
        public bool SupportsMetal { get; private set; }
        public MetalGpuFamily MetalFamily { get; private set; }
        public MetalCapabilities? MetalCaps { get; private set; }

        // ── Feature queries (cross-platform, Metal-aware) ──
        public bool SupportsCompute { get; private set; }
        public bool SupportsIndirectDrawing { get; private set; }

        private IntelligentQueryLayer() { }

        private void Detect()
        {
            Console.WriteLine("================================================================================");
            Console.WriteLine("INTELLIGENT QUERY LAYER — Probing host capabilities");
            Console.WriteLine("================================================================================");

            // Step 1: Basic GPU detection (cross-platform)
            Console.WriteLine("[1/3] Detecting GPU...");
            Gpu = GpuDetector.Probe();

            // Step 2: CPU detection
            Console.WriteLine("[2/3] Detecting CPU...");
            Cpu = ProcessorCapabilities.Probe();

            // Step 3: Metal-specific detection (macOS only)
            Console.WriteLine("[3/3] Probing Metal capabilities...");
            if (OperatingSystem.IsMacOS())
            {
                MetalCaps = MacMetalProbe.Probe();
                if (MetalCaps.HasValue)
                {
                    SupportsMetal = MetalCaps.Value.SupportsMetal;
                    MetalFamily = MetalCaps.Value.GpuFamily;
                    SupportsCompute = MetalCaps.Value.SupportsCompute;
                    SupportsIndirectDrawing = MetalCaps.Value.SupportsIndirectDrawing;
                }
            }

            PrintSummary();
            Console.WriteLine("================================================================================");
        }

        private void PrintSummary()
        {
            Console.WriteLine();
            Console.WriteLine("── Capability Summary ──");
            Console.WriteLine($"  GPU: {Gpu.Name} ({Gpu.Vendor}), {Gpu.VramMB}MB, Tier={Gpu.Tier}");
            Console.WriteLine($"  CPU: {Cpu.Architecture} (AVX={Cpu.SupportsAvx}, SSE={Cpu.SupportsSse}, ARM SIMD={Cpu.SupportsArmSimd})");

            if (SupportsMetal)
            {
                Console.WriteLine($"  Metal Family: {MetalFamily.DisplayName()}");
                Console.WriteLine($"  Metal Compute: {SupportsCompute}");
                Console.WriteLine($"  Metal Indirect Drawing: {SupportsIndirectDrawing}");
            }

        }
    }
}
