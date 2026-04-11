namespace BlueSky.Core.Platform.Detection
{
    public enum GpuTier { Low, Mid, High }
    public enum OSPlatform { Windows, MacOS, Linux }
    public enum RendererBackend { OpenGL, Vulkan, DX11, DX12, Metal }
    
    public struct GpuCapabilities
    {
        public OSPlatform OS;
        public string Vendor;
        public string Name;
        public int VramMB;
        public bool IsIntegrated;
        public string DriverVersion;
        
        public bool SupportsDX11;
        public bool SupportsDX12;
        public bool SupportsMetal;
        public bool SupportsVulkan;
        public bool SupportsOpenGL33;
        public bool SupportsOpenGL45;
        
        public GpuTier Tier;
        
        /// <summary>
        /// Diagnostic string indicating how this data was obtained.
        /// e.g. "shell:system_profiler", "shell:wmic", "heuristic:os+arch"
        /// </summary>
        public string DetectionMethod;
    }
}
