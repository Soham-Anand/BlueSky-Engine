using BlueSky.Platform;
using System;
using System.Runtime.InteropServices;

namespace NotBSRenderer.DirectX10;

/// <summary>
/// DirectX 10 device implementation stub - Modern rendering backend.
/// </summary>
internal class D3D10Device : IRHIDevice
{
    private IntPtr _device;
    private bool _disposed;
    
    public RHIBackend Backend => RHIBackend.DirectX10;
    
    public D3D10Device(IWindow window)
    {
        Console.WriteLine("[DX10] Initializing DirectX 10...");
        
        // TODO: Implement D3D10CreateDeviceAndSwapChain or D3D10CreateDevice
        
        throw new NotImplementedException("DirectX 10 backend not yet implemented");
    }
    
    public IRHISwapchain CreateSwapchain(IWindow window, PresentMode presentMode = PresentMode.Vsync)
    {
        throw new NotImplementedException();
    }
    
    public IRHIBuffer CreateBuffer(BufferDesc desc)
    {
        throw new NotImplementedException();
    }
    
    public IRHITexture CreateTexture(TextureDesc desc)
    {
        throw new NotImplementedException();
    }
    
    public IRHIPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        throw new NotImplementedException();
    }
    
    public IRHICommandBuffer CreateCommandBuffer()
    {
        throw new NotImplementedException();
    }
    
    public void Submit(IRHICommandBuffer commandBuffer)
    {
    }
    
    public void Submit(IRHICommandBuffer commandBuffer, IRHISwapchain swapchain)
    {
    }
    
    public void WaitIdle()
    {
    }
    
    public void UploadBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0)
    {
        throw new NotImplementedException();
    }

    public void UpdateBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0)
    {
        throw new NotImplementedException();
    }
    
    public void UploadTexture(IRHITexture texture, ReadOnlySpan<byte> data, uint mipLevel = 0)
    {
        throw new NotImplementedException();
    }
    
    internal IntPtr Device => _device;
    
    public void Dispose()
    {
        if (_disposed) return;
        
        if (_device != IntPtr.Zero)
        {
            Marshal.Release(_device);
            _device = IntPtr.Zero;
        }
        
        _disposed = true;
        Console.WriteLine("[DX10] Device disposed");
    }
}
