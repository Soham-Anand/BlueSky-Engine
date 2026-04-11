using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BlueSky.Core.Platform.Detection.Probes
{
    internal class WindowsProbe : IPlatformGpuProbe
    {
        public GpuCapabilities? Probe()
        {
            var caps = new GpuCapabilities
            {
                OS = OSPlatform.Windows,
                SupportsOpenGL33 = true,
                DetectionMethod = "shell:wmic"
            };

            bool gotData = ProbeWmic(ref caps);

            // Fallback to PowerShell if wmic fails (wmic deprecated on Windows 11)
            if (!gotData)
            {
                gotData = ProbePowerShell(ref caps);
                if (gotData)
                    caps.DetectionMethod = "shell:powershell";
            }

            if (!gotData)
                return null;

            // DX support based on OS version
            caps.SupportsDX11 = true; // All modern Windows support DX11
            caps.SupportsDX12 = Environment.OSVersion.Version.Major >= 10;

            // Vulkan support heuristic - NVIDIA/AMD discrete GPUs typically have Vulkan
            if (!GpuClassifier.IsLikelyIntegrated(caps.Vendor, caps.Name))
            {
                // Check for vulkan-1.dll as a stronger signal
                var systemDir = Environment.SystemDirectory;
                caps.SupportsVulkan = File.Exists(Path.Combine(systemDir, "vulkan-1.dll"));
            }

            // OpenGL 4.5 for discrete NVIDIA/AMD
            caps.SupportsOpenGL45 = !GpuClassifier.IsLikelyIntegrated(caps.Vendor, caps.Name);

            // No Metal on Windows
            caps.SupportsMetal = false;

            caps.IsIntegrated = GpuClassifier.IsLikelyIntegrated(caps.Vendor, caps.Name);
            caps.Tier = GpuClassifier.ClassifyTier(caps.Vendor, caps.Name, caps.VramMB);

            return caps;
        }

        private static bool ProbeWmic(ref GpuCapabilities caps)
        {
            var output = ShellCommandRunner.Run("wmic",
                "path win32_VideoController get Name,AdapterRAM,DriverVersion /format:csv");
            if (string.IsNullOrWhiteSpace(output))
                return false;

            return ParseCsvOutput(output, ref caps);
        }

        private static bool ProbePowerShell(ref GpuCapabilities caps)
        {
            var output = ShellCommandRunner.Run("powershell",
                "-NoProfile -Command \"Get-CimInstance Win32_VideoController | Select-Object Name,AdapterRAM,DriverVersion | ConvertTo-Csv -NoTypeInformation\"");
            if (string.IsNullOrWhiteSpace(output))
                return false;

            return ParseCsvOutput(output, ref caps);
        }

        private static bool ParseCsvOutput(string output, ref GpuCapabilities caps)
        {
            try
            {
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToArray();

                // Find header line
                int headerIdx = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains("Name", StringComparison.OrdinalIgnoreCase) &&
                        lines[i].Contains("AdapterRAM", StringComparison.OrdinalIgnoreCase))
                    {
                        headerIdx = i;
                        break;
                    }
                }

                if (headerIdx < 0 || headerIdx + 1 >= lines.Length)
                    return false;

                var headers = lines[headerIdx].Split(',').Select(h => h.Trim().Trim('"')).ToArray();
                int nameIdx = Array.FindIndex(headers, h => h.Equals("Name", StringComparison.OrdinalIgnoreCase));
                int ramIdx = Array.FindIndex(headers, h => h.Equals("AdapterRAM", StringComparison.OrdinalIgnoreCase));
                int driverIdx = Array.FindIndex(headers, h => h.Equals("DriverVersion", StringComparison.OrdinalIgnoreCase));

                // Collect all GPUs, pick the best one (highest VRAM, prefer discrete)
                string bestName = "";
                long bestVram = 0;
                string bestDriver = "";

                for (int i = headerIdx + 1; i < lines.Length; i++)
                {
                    var fields = lines[i].Split(',').Select(f => f.Trim().Trim('"')).ToArray();

                    string name = nameIdx >= 0 && nameIdx < fields.Length ? fields[nameIdx] : "";
                    long vramBytes = 0;
                    string driver = "";

                    if (ramIdx >= 0 && ramIdx < fields.Length)
                        long.TryParse(fields[ramIdx], out vramBytes);
                    if (driverIdx >= 0 && driverIdx < fields.Length)
                        driver = fields[driverIdx];

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    // Prefer discrete GPU (higher VRAM or non-Intel)
                    bool currentIsIntegrated = GpuClassifier.IsLikelyIntegrated(DeriveVendor(name), name);
                    bool bestIsIntegrated = GpuClassifier.IsLikelyIntegrated(DeriveVendor(bestName), bestName);

                    if (string.IsNullOrEmpty(bestName) ||
                        (bestIsIntegrated && !currentIsIntegrated) ||
                        (!currentIsIntegrated && vramBytes > bestVram))
                    {
                        bestName = name;
                        bestVram = vramBytes;
                        bestDriver = driver;
                    }
                }

                if (string.IsNullOrEmpty(bestName))
                    return false;

                caps.Name = bestName;
                caps.Vendor = DeriveVendor(bestName);
                caps.VramMB = (int)(bestVram / (1024 * 1024));
                caps.DriverVersion = bestDriver;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string DeriveVendor(string gpuName)
        {
            var upper = (gpuName ?? "").ToUpperInvariant();
            if (upper.Contains("NVIDIA") || upper.Contains("GEFORCE") || upper.Contains("RTX") || upper.Contains("GTX")) return "NVIDIA";
            if (upper.Contains("AMD") || upper.Contains("RADEON")) return "AMD";
            if (upper.Contains("INTEL")) return "Intel";
            if (upper.Contains("QUALCOMM") || upper.Contains("ADRENO")) return "Qualcomm";
            return "Unknown";
        }
    }
}
