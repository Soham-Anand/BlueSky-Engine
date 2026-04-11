using System;
using System.Text.RegularExpressions;

namespace BlueSky.Core.Platform.Detection.Probes
{
    internal class MacOSProbe : IPlatformGpuProbe
    {
        public GpuCapabilities? Probe()
        {
            var output = ShellCommandRunner.Run("system_profiler", "SPDisplaysDataType");
            if (string.IsNullOrWhiteSpace(output))
                return null;

            var caps = new GpuCapabilities
            {
                OS = OSPlatform.MacOS,
                DetectionMethod = "shell:system_profiler"
            };

            // Parse GPU name from "Chipset Model: ..."
            caps.Name = ParseField(output, @"Chipset Model:\s*(.+)") ?? "Unknown GPU";

            // Derive vendor from name
            caps.Vendor = DeriveVendor(caps.Name);

            // Parse VRAM - handles "VRAM (Total): X MB/GB" and "VRAM (Dynamic, Max): X MB/GB"
            var vramStr = ParseField(output, @"VRAM\s*\([^)]*\):\s*(\d+)\s*(MB|GB)");
            if (vramStr != null)
            {
                var vramMatch = Regex.Match(output, @"VRAM\s*\([^)]*\):\s*(\d+)\s*(MB|GB)", RegexOptions.IgnoreCase);
                if (vramMatch.Success)
                {
                    int vram = int.Parse(vramMatch.Groups[1].Value);
                    if (vramMatch.Groups[2].Value.Equals("GB", StringComparison.OrdinalIgnoreCase))
                        vram *= 1024;
                    caps.VramMB = vram;
                }
            }

            // Metal support
            var metalFamily = ParseField(output, @"Metal\s*(?:Family|Support):\s*(.+)");
            caps.SupportsMetal = metalFamily != null;

            // macOS always supports OpenGL 3.3 (deprecated but available up to 4.1)
            caps.SupportsOpenGL33 = true;
            caps.SupportsOpenGL45 = false; // macOS caps at 4.1

            // No native Vulkan or DX on macOS
            caps.SupportsVulkan = false;
            caps.SupportsDX11 = false;
            caps.SupportsDX12 = false;

            // Integrated detection
            caps.IsIntegrated = GpuClassifier.IsLikelyIntegrated(caps.Vendor, caps.Name);

            // Tier classification
            caps.Tier = GpuClassifier.ClassifyTier(caps.Vendor, caps.Name, caps.VramMB);

            return caps;
        }

        private static string DeriveVendor(string gpuName)
        {
            var upper = gpuName.ToUpperInvariant();
            if (upper.Contains("APPLE")) return "Apple";
            if (upper.Contains("AMD") || upper.Contains("RADEON")) return "AMD";
            if (upper.Contains("NVIDIA") || upper.Contains("GEFORCE")) return "NVIDIA";
            if (upper.Contains("INTEL")) return "Intel";
            return "Unknown";
        }

        private static string? ParseField(string text, string pattern)
        {
            try
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                return match.Success ? match.Groups[1].Value.Trim() : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
