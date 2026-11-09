using System;

namespace BlueSky.Aegis.Detection
{
    internal static class GpuClassifier
    {
        /// <summary>
        /// Classifies GPU tier based on vendor, name, and VRAM (or unified memory working set).
        /// </summary>
        internal static GpuTier ClassifyTier(string? vendor, string? name, int vramMB)
        {
            vendor = vendor?.ToUpperInvariant() ?? "";
            name = name?.ToUpperInvariant() ?? "";

            // Apple Silicon (M1..M5+) is always High tier
            if (vendor.Contains("APPLE") || name.Contains("APPLE M"))
                return GpuTier.High;

            // Intel Arc discrete GPUs (Alchemist A-series & Battlemage B-series)
            if (vendor.Contains("INTEL") && name.Contains("ARC"))
            {
                if (name.Contains("A7") || name.Contains("A5") || name.Contains("B5"))
                    return GpuTier.High;
                return GpuTier.Mid;
            }

            // Modern Intel Iris Xe / Core Ultra Arc iGPUs are Mid if >= 4GB shared budget, legacy HD/UHD are Low
            if (vendor.Contains("INTEL"))
            {
                if ((name.Contains("IRIS XE") || name.Contains("GRAPHICS")) && vramMB >= 4096)
                    return GpuTier.Mid;
                return GpuTier.Low;
            }

            // Known high-tier cards by model name (protects against WMI 4GB uint32 wrap on Windows)
            if (IsKnownHighTier(name))
                return GpuTier.High;

            // NVIDIA / AMD discrete — use VRAM + name heuristics
            if (vramMB >= 6144) // 6GB+
                return GpuTier.High;

            if (vramMB >= 2048) // 2-6GB
                return GpuTier.Mid;

            if (vramMB > 0 && vramMB < 2048)
                return GpuTier.Low;

            // VRAM unknown (0) — use name-based heuristics
            if (IsKnownMidTier(name))
                return GpuTier.Mid;

            return GpuTier.Low;
        }

        private static bool IsKnownHighTier(string name)
        {
            // NVIDIA high-tier (RTX 20/30/40/50 series, RTX Ada/Workstation, GTX 1060+/1660+)
            if (name.Contains("RTX")) return true;
            if (name.Contains("GTX 1060") || name.Contains("GTX 1070") || name.Contains("GTX 1080") || name.Contains("TITAN")) return true;
            if (name.Contains("GTX 1660") || name.Contains("GTX 1650 SUPER")) return true;

            // AMD high-tier (RX 5000/6000/7000/8000/9000, RX 580/590, Radeon VII, Vega 56/64, Radeon 780M/890M)
            if (name.Contains("RX 580") || name.Contains("RX 590") || name.Contains("RADEON VII")) return true;
            if (name.Contains("RX 5") && !name.Contains("RX 550") && !name.Contains("RX 560") && !name.Contains("RX 570")) return true;
            if (name.Contains("RX 6") || name.Contains("RX 7") || name.Contains("RX 8") || name.Contains("RX 9")) return true;
            if (name.Contains("RADEON PRO W") || name.Contains("RADEON PRO 5")) return true;
            if (name.Contains("VEGA") && !name.Contains("GRAPHICS")) return true;

            return false;
        }

        private static bool IsKnownMidTier(string name)
        {
            // NVIDIA mid-tier
            if (name.Contains("GT 1030") || name.Contains("GTX 750") || name.Contains("GTX 950") || name.Contains("GTX 960") || name.Contains("GTX 970") || name.Contains("GTX 980")) return true;
            if (name.Contains("GTX 1050") || name.Contains("GTX 1630") || name.Contains("GTX 1650")) return true;
            if (name.Contains("MX450") || name.Contains("MX550") || name.Contains("MX570")) return true;

            // AMD mid-tier (including RDNA2/RDNA3 APUs like 680M, 760M, 780M, 880M, 890M)
            if (name.Contains("RX 550") || name.Contains("RX 560") || name.Contains("RX 570")) return true;
            if (name.Contains("R7 370") || name.Contains("R9 270") || name.Contains("R9 380") || name.Contains("R9 390")) return true;
            if (name.Contains("680M") || name.Contains("760M") || name.Contains("780M") || name.Contains("880M") || name.Contains("890M")) return true;
            if (name.Contains("RADEON PRO 4") || name.Contains("RADEON PRO 555") || name.Contains("RADEON PRO 560")) return true;

            return false;
        }

        /// <summary>
        /// Determines if a GPU is likely an integrated GPU.
        /// Note: Apple Silicon uses high-bandwidth Unified Memory architecture (HasUnifiedMemory=true)
        /// rather than a low-power PC northbridge iGPU, so IsLikelyIntegrated returns false for Apple Silicon
        /// to prevent false downgrades to low-tier settings.
        /// </summary>
        internal static bool IsLikelyIntegrated(string? vendor, string? name)
        {
            vendor = vendor?.ToUpperInvariant() ?? "";
            name = name?.ToUpperInvariant() ?? "";

            if (vendor.Contains("APPLE") || name.Contains("APPLE M"))
                return false;

            if (vendor.Contains("INTEL") && !name.Contains("ARC"))
                return true;

            if (name.Contains("INTEGRATED") || name.Contains("UHD") || name.Contains("HD GRAPHICS") || name.Contains("IRIS"))
                return true;

            // AMD APU integrated GPUs
            if (vendor.Contains("AMD") &&
                ((name.Contains("VEGA") && name.Contains("GRAPHICS")) ||
                 name.Contains("RADEON GRAPHICS") ||
                 name.Contains("610M") || name.Contains("660M") || name.Contains("680M") ||
                 name.Contains("740M") || name.Contains("760M") || name.Contains("780M") ||
                 name.Contains("860M") || name.Contains("880M") || name.Contains("890M")))
            {
                return true;
            }

            return false;
        }
    }
}
