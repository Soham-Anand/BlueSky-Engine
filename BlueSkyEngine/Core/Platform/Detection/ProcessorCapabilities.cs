using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace BlueSky.Core.Platform.Detection;

public readonly struct ProcessorCapabilities
{
    public ProcessorCapabilities(string architecture, bool supportsAvx, bool supportsSse, bool supportsArmSimd)
    {
        Architecture = architecture;
        SupportsAvx = supportsAvx;
        SupportsSse = supportsSse;
        SupportsArmSimd = supportsArmSimd;
    }

    public string Architecture { get; }
    public bool SupportsAvx { get; }
    public bool SupportsSse { get; }
    public bool SupportsArmSimd { get; }

    public static ProcessorCapabilities Probe()
    {
        return new ProcessorCapabilities(
            RuntimeInformation.ProcessArchitecture.ToString(),
            Avx.IsSupported,
            Sse.IsSupported,
            AdvSimd.IsSupported);
    }

    public void LogRayTracingSummary()
    {
        Console.WriteLine($"  CPU Architecture: {Architecture}");
        Console.WriteLine($"  CPU SIMD: AVX={(SupportsAvx ? "yes" : "no")}, SSE={(SupportsSse ? "yes" : "no")}, ARM SIMD={(SupportsArmSimd ? "yes" : "no")}");
        Console.WriteLine($"  AVX Ray Tracing: {(SupportsAvx ? "available" : "unavailable on this CPU/runtime")}");
    }
}
