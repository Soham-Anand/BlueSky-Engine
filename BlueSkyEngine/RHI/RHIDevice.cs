using BlueSky.Platform;
using BlueSky.Core.Platform.Detection;

namespace BlueSky.Rendering.RHI;

public static class RHIDevice
{
    public static IRHIDevice Create(RHIBackend backend, IWindow? window = null)
    {
        var device = backend switch
        {
            RHIBackend.Metal => (IRHIDevice)new Metal.MetalDevice(IntelligentQueryLayer.Probe()),
            RHIBackend.DirectX11 => window != null
                ? (IRHIDevice)new DirectX11.D3D11Device(window)
                : throw new ArgumentException("DirectX 11 requires a window for device creation"),
            RHIBackend.Vulkan => window != null
                ? (IRHIDevice)new Vulkan.VulkanDevice(window)
                : throw new ArgumentException("Vulkan requires a window for surface creation"),
            _ => throw new ArgumentException($"Unknown backend: {backend}")
        };

        return ValidationDevice.Wrap(device);
    }

    public static IRHIDevice CreateDefault(IWindow? window = null)
    {
        var bestBackend = RHIDiscovery.DiscoverBestBackend();
        return Create(bestBackend, window);
    }

    public static IRHIDevice CreateDefault(IWindow? window, string[]? cliArgs)
    {
        var bestBackend = RHIDiscovery.DiscoverBestBackend(cliArgs);
        return Create(bestBackend, window);
    }
}
