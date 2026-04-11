namespace BlueSky.Core.Platform.Detection
{
    internal interface IPlatformGpuProbe
    {
        /// <summary>
        /// Attempts to detect GPU capabilities using platform-specific methods.
        /// Returns a populated GpuCapabilities if successful, or null to fall back to heuristics.
        /// </summary>
        GpuCapabilities? Probe();
    }
}
