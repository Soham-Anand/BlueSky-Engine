using System;
using System.Runtime.InteropServices;
using BlueSky.Aegis.Detection.Probes;
using BlueSky.BSR.RHI;

namespace BlueSky.Aegis.Detection
{
    /// <summary>
    /// Unified cross-platform hardware, OS, CPU, GPU, and RHI capability detector.
    /// Bridges <see cref="IntelligentQueryLayer"/>, <see cref="GpuDetector"/>,
    /// <see cref="ProcessorCapabilities"/>, and <see cref="MacMetalProbe"/> across macOS, Windows, and Linux.
    /// </summary>
    public static class PlatformDetector
    {
        /// <summary>
        /// Gets the cached <see cref="IntelligentQueryLayer"/> instance, probing on first access if needed.
        /// </summary>
        public static IntelligentQueryLayer Capabilities => IntelligentQueryLayer.Probe();

        /// <summary>
        /// Probes (or refreshes) the full platform hardware profile (CPU, GPU, Memory, SIMD, and RHI feature matrix).
        /// </summary>
        public static IntelligentQueryLayer Detect(bool forceRefresh = false) =>
            IntelligentQueryLayer.Probe(forceRefresh);

        /// <summary>
        /// Probes GPU capabilities directly across macOS (Metal + IOKit), Windows (DXGI 1.6 + D3D12), and Linux (DRM sysfs + pci.ids).
        /// </summary>
        public static GpuCapabilities DetectGpu() => GpuDetector.Probe();

        /// <summary>
        /// Probes CPU topology (P-cores, E-cores, cache hierarchy, system RAM, and x86/ARM64 SIMD intrinsics).
        /// </summary>
        public static ProcessorCapabilities DetectCpu() => ProcessorCapabilities.Probe();

        /// <summary>
        /// Probes native Metal capabilities on macOS (returns null on Windows/Linux).
        /// </summary>
        public static MetalCapabilities? DetectMetal() =>
            OperatingSystem.IsMacOS() ? (MacOSProbe.LastMetalCapabilities ?? MacMetalProbe.Probe()) : null;

        /// <summary>
        /// Returns the current host <see cref="OSPlatform"/> enum value.
        /// </summary>
        public static OSPlatform CurrentOS =>
            OperatingSystem.IsMacOS() ? OSPlatform.MacOS :
            OperatingSystem.IsWindows() ? OSPlatform.Windows :
            OSPlatform.Linux;

        /// <summary>
        /// True when running on Apple Silicon (macOS ARM64 — M1, M2, M3, M4, M5+).
        /// </summary>
        public static bool IsAppleSilicon =>
            OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

        /// <summary>
        /// Returns the recommended <see cref="RHIBackend"/> for the current host platform.
        /// </summary>
        public static RHIBackend RecommendedBackend => RHIDiscovery.DiscoverBestBackend();
    }
}
