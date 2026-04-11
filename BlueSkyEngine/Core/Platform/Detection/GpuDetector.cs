using System;
using System.Runtime.InteropServices;
using BlueSky.Core.Platform.Detection.Probes;

namespace BlueSky.Core.Platform.Detection
{
    public static class GpuDetector
    {
        public static GpuCapabilities Probe()
        {
            // Determine platform and dispatch to the appropriate probe
            IPlatformGpuProbe? probe = null;

            if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                probe = new WindowsProbe();
            else if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                probe = new MacOSProbe();
            else if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
                probe = new LinuxProbe();

            GpuCapabilities? result = null;
            if (probe != null)
            {
                try
                {
                    result = probe.Probe();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GPU] Platform probe failed: {ex.Message}");
                }
            }

            // Fall back to heuristics if platform probe didn't produce results
            var caps = result ?? HeuristicFallback();

            // Log detection summary
            Console.WriteLine($"[GPU] Detection method: {caps.DetectionMethod ?? "unknown"}");
            Console.WriteLine($"[GPU] Detected: {caps.Name ?? "Unknown"} ({caps.Vendor ?? "Unknown"}), {caps.VramMB}MB VRAM, Tier={caps.Tier}");
            Console.WriteLine($"[GPU] Supports: Metal={caps.SupportsMetal}, Vulkan={caps.SupportsVulkan}, " +
                              $"OpenGL3.3={caps.SupportsOpenGL33}, OpenGL4.5={caps.SupportsOpenGL45}, " +
                              $"DX11={caps.SupportsDX11}, DX12={caps.SupportsDX12}");

            return caps;
        }

        /// <summary>
        /// Original heuristic-based detection used as a fallback when shell commands fail.
        /// </summary>
        private static GpuCapabilities HeuristicFallback()
        {
            var caps = new GpuCapabilities
            {
                DetectionMethod = "heuristic:os+arch"
            };

            if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                caps.OS = OSPlatform.Windows;
                caps.SupportsDX11 = true;
                caps.SupportsDX12 = Environment.OSVersion.Version.Major >= 10;
                caps.SupportsVulkan = true;
                caps.SupportsOpenGL33 = true;
                caps.Tier = GpuTier.Mid; // Assume Mid since we can't determine
                caps.Name = "Unknown GPU (Windows)";
                caps.Vendor = "Unknown";
            }
            else if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                caps.OS = OSPlatform.MacOS;
                caps.SupportsMetal = true;
                caps.SupportsOpenGL33 = true;

                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                {
                    caps.Name = "Apple Silicon GPU";
                    caps.Vendor = "Apple";
                    caps.Tier = GpuTier.High;
                }
                else
                {
                    caps.Name = "Intel Graphics (macOS)";
                    caps.Vendor = "Intel";
                    caps.IsIntegrated = true;
                    caps.Tier = GpuTier.Low;
                }
            }
            else
            {
                caps.OS = OSPlatform.Linux;
                caps.SupportsOpenGL33 = true;
                caps.Tier = GpuTier.Low;
                caps.Name = "Unknown GPU (Linux)";
                caps.Vendor = "Unknown";
            }

            return caps;
        }
    }
}
