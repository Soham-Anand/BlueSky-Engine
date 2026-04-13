using BlueSky.Platform;

namespace NotBSRenderer;

public static class RHIDevice
{
    public static IRHIDevice Create(RHIBackend backend, IWindow? window = null)
    {
        var device = backend switch
        {
            RHIBackend.Metal => (IRHIDevice)new Metal.MetalDevice(),
            RHIBackend.DirectX9 => window != null ? new DirectX9.D3D9Device(window) : throw new ArgumentException("DirectX9 requires a window"),
            RHIBackend.DirectX10 => throw new NotImplementedException("DirectX 10 backend not yet implemented"),
            RHIBackend.DirectX11 => throw new NotImplementedException("DirectX 11 backend not yet implemented"),
            RHIBackend.DirectX12 => throw new NotImplementedException("DirectX 12 backend not yet implemented"),
            RHIBackend.Vulkan => throw new NotImplementedException("Vulkan backend not yet implemented"),
            RHIBackend.OpenGL => throw new NotImplementedException("OpenGL backend not yet implemented"),
            _ => throw new ArgumentException($"Unknown backend: {backend}")
        };

        return ValidationDevice.Wrap(device);
    }
    
    public static IRHIDevice CreateDefault(IWindow? window = null)
    {
        var bestBackend = RHIDiscovery.DiscoverBestBackend();
        return Create(bestBackend, window);
    }
}
