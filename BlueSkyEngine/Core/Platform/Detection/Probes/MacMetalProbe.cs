using System;
using System.Runtime.InteropServices;

namespace BlueSky.Core.Platform.Detection.Probes
{
    /// <summary>
    /// Probes Metal GPU capabilities via ObjC runtime P/Invoke.
    /// Detects GPU family, compute support, and indirect drawing support.
    /// Works on all Metal-capable Macs (Intel HD 4000+ through Apple Silicon M4).
    /// </summary>
    internal static class MacMetalProbe
    {
        // ── ObjC runtime ──
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
        private static extern IntPtr sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern bool objc_msgSend_bool_ulong(IntPtr receiver, IntPtr selector, ulong arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern ulong objc_msgSend_ulong(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector);

        // Metal framework
        [DllImport("/System/Library/Frameworks/Metal.framework/Metal")]
        private static extern IntPtr MTLCreateSystemDefaultDevice();

        private static IntPtr GetSelector(string name) => sel_registerName(name);

        private static void Release(IntPtr obj)
        {
            if (obj != IntPtr.Zero)
                objc_msgSend_void(obj, GetSelector("release"));
        }

        private static string GetDeviceName(IntPtr device)
        {
            var nameSel = GetSelector("name");
            var nsString = objc_msgSend(device, nameSel);
            if (nsString == IntPtr.Zero) return "Unknown Metal GPU";

            var utf8Sel = GetSelector("UTF8String");
            var cString = objc_msgSend(nsString, utf8Sel);
            return Marshal.PtrToStringAnsi(cString) ?? "Unknown Metal GPU";
        }

        /// <summary>
        /// Detect Metal GPU capabilities by querying the actual device.
        /// Returns null if Metal is not available.
        /// </summary>
        public static MetalCapabilities? Probe()
        {
            if (!OperatingSystem.IsMacOS()) return null;

            IntPtr device = IntPtr.Zero;
            try
            {
                device = MTLCreateSystemDefaultDevice();
                if (device == IntPtr.Zero) return null;

                var caps = new MetalCapabilities
                {
                    DeviceName = GetDeviceName(device),
                    SupportsMetal = true
                };

                // Detect GPU family via supportsFamily: (Metal 1.0+)
                caps.GpuFamily = DetectGpuFamily(device);

                // Detect feature support based on GPU family
                caps.SupportsCompute = caps.GpuFamily.SupportsCompute();
                caps.SupportsIndirectDrawing = caps.GpuFamily.SupportsIndirectDrawing();
                Console.WriteLine($"[MetalProbe] GPU: {caps.DeviceName}");
                Console.WriteLine($"[MetalProbe] Family: {caps.GpuFamily.DisplayName()}");
                Console.WriteLine($"[MetalProbe] Compute: {caps.SupportsCompute}");
                Console.WriteLine($"[MetalProbe] IndirectDrawing: {caps.SupportsIndirectDrawing}");

                return caps;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MetalProbe] Detection failed: {ex.Message}");
                return null;
            }
            finally
            {
                if (device != IntPtr.Zero)
                    Release(device);
            }
        }

        /// <summary>
        /// Detect GPU family by checking supportsFamily: for each known family.
        /// Falls back to name-based heuristic if supportsFamily: is not available.
        /// </summary>
        private static MetalGpuFamily DetectGpuFamily(IntPtr device)
        {
            var supportsFamilySel = GetSelector("supportsFamily:");

            // MTLGPUFamily enum values (from Metal headers)
            // macOS families start at 1000, Apple families at 3001
            // We check from highest to lowest for best match

            // Apple Silicon families (Metal 3.0+)
            // MTLGPUFamilyApple5 = 1005 (M4)
            // MTLGPUFamilyApple4 = 1004 (M3)
            // MTLGPUFamilyApple3 = 1003 (M2)
            // MTLGPUFamilyApple2 = 1002 (M1 Pro/Max/Ultra)
            // MTLGPUFamilyApple1 = 1001 (M1)
            if (CheckFamily(device, supportsFamilySel, 1005)) return MetalGpuFamily.Apple5;
            if (CheckFamily(device, supportsFamilySel, 1004)) return MetalGpuFamily.Apple4;
            if (CheckFamily(device, supportsFamilySel, 1003)) return MetalGpuFamily.Apple3;
            if (CheckFamily(device, supportsFamilySel, 1002)) return MetalGpuFamily.Apple2;
            if (CheckFamily(device, supportsFamilySel, 1001)) return MetalGpuFamily.Apple1;

            // macOS families (Metal 1.0-2.0)
            // MTLGPUMacFamily4 = 1004 (Coffee Lake, Metal 2.0)
            // MTLGPUMacFamily3 = 1003 (Broadwell, Metal 1.2)
            // MTLGPUMacFamily2 = 1002 (Haswell, Metal 1.1)
            // MTLGPUMacFamily1 = 1001 (Ivy Bridge, Metal 1.0)
            // Note: these overlap with Apple families in the enum, so we need to check
            // if supportsFamily: even exists first. On older macOS, it might not.
            // The MTLGPUMacFamily values are different from MTLGPUFamilyApple values
            // in the actual Metal headers:
            // MTLGPUMacFamily1 = 10001
            // MTLGPUMacFamily2 = 10002
            // MTLGPUMacFamily3 = 10003
            // MTLGPUMacFamily4 = 10004
            if (CheckFamily(device, supportsFamilySel, 10004)) return MetalGpuFamily.Mac4;
            if (CheckFamily(device, supportsFamilySel, 10003)) return MetalGpuFamily.Mac3;
            if (CheckFamily(device, supportsFamilySel, 10002)) return MetalGpuFamily.Mac2;
            if (CheckFamily(device, supportsFamilySel, 10001)) return MetalGpuFamily.Mac1;

            // Fallback: name-based heuristic
            return FallbackDetectByDeviceName(device);
        }

        private static bool CheckFamily(IntPtr device, IntPtr supportsFamilySel, long family)
        {
            try
            {
                return objc_msgSend_bool_ulong(device, supportsFamilySel, (ulong)family);
            }
            catch
            {
                // supportsFamily: not available on this macOS version
                return false;
            }
        }

        /// <summary>
        /// Fallback detection using device name string matching.
        /// Used when supportsFamily: is not available (very old macOS).
        /// </summary>
        private static MetalGpuFamily FallbackDetectByDeviceName(IntPtr device)
        {
            string name = GetDeviceName(device).ToUpperInvariant();

            // Apple Silicon
            if (name.Contains("APPLE M4")) return MetalGpuFamily.Apple5;
            if (name.Contains("APPLE M3")) return MetalGpuFamily.Apple4;
            if (name.Contains("APPLE M2")) return MetalGpuFamily.Apple3;
            if (name.Contains("APPLE M1") && (name.Contains("MAX") || name.Contains("ULTRA") || name.Contains("PRO")))
                return MetalGpuFamily.Apple2;
            if (name.Contains("APPLE M1")) return MetalGpuFamily.Apple1;

            // Intel Macs
            if (name.Contains("UHD") || name.Contains("IRIS PLUS") || name.Contains("IRIS XE"))
                return MetalGpuFamily.Mac4; // Coffee Lake or newer
            if (name.Contains("IRIS"))
                return MetalGpuFamily.Mac3; // Broadwell Iris
            if (name.Contains("HD GRAPHICS 5") || name.Contains("HD GRAPHICS 6") || name.Contains("HD 5") || name.Contains("HD 6"))
                return MetalGpuFamily.Mac2; // Haswell or newer

            // Oldest Metal-capable Intel Mac (Ivy Bridge, Intel HD 4000)
            if (name.Contains("HD GRAPHICS 4") || name.Contains("HD 4") || name.Contains("INTEL"))
                return MetalGpuFamily.Mac1;

            return MetalGpuFamily.Unknown;
        }

    }

    /// <summary>
    /// Metal-specific capabilities detected via MacMetalProbe.
    /// </summary>
    public struct MetalCapabilities
    {
        public string DeviceName;
        public bool SupportsMetal;
        public MetalGpuFamily GpuFamily;

        // Feature support
        public bool SupportsCompute;         // Metal 1.0+ (all Intel Macs)
        public bool SupportsIndirectDrawing;  // Metal 1.0+ (all Intel Macs)
    }
}
