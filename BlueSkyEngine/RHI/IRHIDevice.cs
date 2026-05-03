using BlueSky.Platform;

namespace NotBSRenderer;

public interface IRHIDevice : IDisposable
{
    RHIBackend Backend { get; }
    RHICapabilities Capabilities { get; }
    DescriptorBindingMode BindingMode { get; }
    
    // Swapchain
    IRHISwapchain CreateSwapchain(IWindow window, PresentMode presentMode = PresentMode.Vsync);
    
    // Resources
    IRHIBuffer CreateBuffer(BufferDesc desc);
    IRHITexture CreateTexture(TextureDesc desc);
    
    // Pipeline
    IRHIPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc);
    IRHIPipeline CreateComputePipeline(ComputePipelineDesc desc);
    
    // Commands
    IRHICommandBuffer CreateCommandBuffer();
    void Submit(IRHICommandBuffer commandBuffer);
    void Submit(IRHICommandBuffer commandBuffer, IRHISwapchain swapchain);
    void WaitIdle();
    
    // Data upload
    void UploadBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0);
    void UpdateBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0);
    void UploadTexture(IRHITexture texture, ReadOnlySpan<byte> data, uint mipLevel = 0);
    
    // Bindless resource management (only available if Capabilities includes BindlessResources)
    BindlessResourceHandle RegisterBindlessTexture(IRHITexture texture);
    BindlessResourceHandle RegisterBindlessBuffer(IRHIBuffer buffer);
    void UnregisterBindlessResource(BindlessResourceHandle handle);
}
