namespace BlueSky.Aegis.Detection
{
    public enum GpuTier { Low, Mid, High }
    public enum OSPlatform { Windows, MacOS, Linux }

    public struct GpuCapabilities
    {
        public OSPlatform OS;
        public string Vendor;
        public string Name;
        public string Architecture;
        public int VramMB;
        public int SystemMemoryMB;
        public int RecommendedMaxWorkingSetMB;
        public int GpuCoreCount;
        public bool IsIntegrated;
        public bool HasUnifiedMemory;
        public string DriverVersion;

        // Graphics APIs
        public bool SupportsDX11;
        public bool SupportsDX12;
        public bool SupportsMetal;
        public bool SupportsMetal3;
        public bool SupportsMetal4;
        public bool SupportsVulkan;
        public bool SupportsOpenGL33;
        public bool SupportsOpenGL45;

        // Modern hardware feature capabilities
        public bool SupportsCompute;
        public bool SupportsIndirectDrawing;
        public bool SupportsRayTracing;
        public bool SupportsMeshShaders;
        public bool SupportsBCTextureCompression;
        public bool SupportsFloat32Filtering;
        public bool SupportsShaderBarycentrics;
        public int ArgumentBuffersTier;

        public GpuTier Tier;

        /// <summary>
        /// Diagnostic string indicating how this data was obtained.
        /// e.g. "native:metal+iokit", "native:dxgi", "sysfs+lspci", "heuristic:os+arch"
        /// </summary>
        public string DetectionMethod;
    }
}
