using System;

namespace BlueSky.Core.Platform.Detection
{
    internal static class GpuClassifier
    {
        /// <summary>
        /// Classifies GPU tier based on vendor, name, and VRAM.
        /// </summary>
        internal static GpuTier ClassifyTier(string? vendor, string? name, int vramMB)
        {
            vendor = vendor?.ToUpperInvariant() ?? "";
            name = name?.ToUpperInvariant() ?? "";

            // Apple Silicon is always High
            if (vendor.Contains("APPLE") || name.Contains("APPLE M"))
                return GpuTier.High;

            // Intel Arc discrete GPUs
            if (vendor.Contains("INTEL") && name.Contains("ARC"))
            {
                if (name.Contains("A7") || name.Contains("A770") || name.Contains("A750"))
                    return GpuTier.High;
                return GpuTier.Mid;
            }

            // Intel integrated (non-Arc)
            if (vendor.Contains("INTEL"))
                return GpuTier.Low;

            // NVIDIA / AMD discrete - use VRAM + name heuristics
            if (vramMB >= 6144) // 6GB+
                return GpuTier.High;

            if (vramMB >= 2048) // 2-6GB
            {
                // Known high-tier cards with 4GB
                if (IsKnownHighTier(name))
                    return GpuTier.High;

                return GpuTier.Mid;
            }

            if (vramMB > 0 && vramMB < 2048)
                return GpuTier.Low;

            // VRAM unknown (0) - use name-based heuristics
            if (IsKnownHighTier(name))
                return GpuTier.High;

            if (IsKnownMidTier(name))
                return GpuTier.Mid;

            // Default to Low if we can't determine
            return GpuTier.Low;
        }

        private static bool IsKnownHighTier(string name)
        {
            // NVIDIA high-tier
            if (name.Contains("RTX")) return true;
            if (name.Contains("GTX 1060") || name.Contains("GTX 1070") || name.Contains("GTX 1080")) return true;
            if (name.Contains("GTX 1660") || name.Contains("GTX 1650 SUPER")) return true;

            // AMD high-tier
            if (name.Contains("RX 580") || name.Contains("RX 590")) return true;
            if (name.Contains("RX 5") && !name.Contains("RX 550") && !name.Contains("RX 560")) return true; // RX 5600, 5700
            if (name.Contains("RX 6") || name.Contains("RX 7")) return true; // RX 6000/7000 series
            if (name.Contains("VEGA")) return true;

            return false;
        }

        private static bool IsKnownMidTier(string name)
        {
            // NVIDIA mid-tier
            if (name.Contains("GT 1030") || name.Contains("GTX 750") || name.Contains("GTX 950") || name.Contains("GTX 960")) return true;
            if (name.Contains("GTX 1050") && !name.Contains("1050 TI")) return true; // 1050 non-Ti
            if (name.Contains("GTX 1650") && !name.Contains("SUPER")) return true;

            // AMD mid-tier
            if (name.Contains("RX 550") || name.Contains("RX 560") || name.Contains("RX 570")) return true;
            if (name.Contains("R7 370") || name.Contains("R9 270") || name.Contains("R9 380")) return true;

            return false;
        }

        /// <summary>
        /// Determines if a GPU is likely integrated based on vendor and name.
        /// </summary>
        internal static bool IsLikelyIntegrated(string? vendor, string? name)
        {
            vendor = vendor?.ToUpperInvariant() ?? "";
            name = name?.ToUpperInvariant() ?? "";

            if (vendor.Contains("INTEL") && !name.Contains("ARC"))
                return true;

            if (name.Contains("INTEGRATED") || name.Contains("UHD") || name.Contains("HD GRAPHICS"))
                return true;

            // AMD APU integrated GPUs
            if (vendor.Contains("AMD") && (name.Contains("VEGA") && name.Contains("GRAPHICS")))
                return true;

            return false;
        }
    }
}
