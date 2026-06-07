using BlueSky.Platform;

namespace NotBSRenderer;

public static class RHIDevice
{
    public static IRHIDevice Create(RHIBackend backend, IWindow? window = null)
    {
        var device = backend switch
        {
            RHIBackend.Metal => (IRHIDevice)new Metal.MetalDevice(),
            RHIBackend.DirectX11 => window != null
                ? (IRHIDevice)new DirectX11.D3D11Device(window)
                : throw new ArgumentException("DirectX 11 requires a window for device creation"),
            RHIBackend.DirectX12 => throw new NotImplementedException("DirectX 12 backend not yet implemented"),
            RHIBackend.Vulkan => window != null
                ? (IRHIDevice)new Vulkan.VulkanDevice(window)
                : throw new ArgumentException("Vulkan requires a window for surface creation"),
            RHIBackend.OpenGL => throw new NotImplementedException("OpenGL backend not yet implemented"),
            _ => throw new ArgumentException($"Unknown backend: {backend}")
        };

        return ValidationDevice.Wrap(device);
    }
    
    public static IRHIDevice CreateDefault(IWindow? window = null, bool forceCompatibility = false)
    {
        var bestBackend = RHIDiscovery.DiscoverBestBackend(forceCompatibility);
        return Create(bestBackend, window);
    }

    public static IRHIDevice CreateDefault(IWindow? window, bool forceCompatibility, string[]? cliArgs)
    {
        var bestBackend = RHIDiscovery.DiscoverBestBackend(cliArgs, forceCompatibility);
        return Create(bestBackend, window);
    }
}
