using BlueSky.Aegis;

namespace BlueSky.BSR.RHI;

public interface IRHIDevice : IDisposable
{
    RHIBackend Backend { get; }
    RHICapabilities Capabilities { get; }
    
    // Swapchain
    IRHISwapchain CreateSwapchain(IWindow window, PresentMode presentMode = PresentMode.Vsync);
    
    // Resources
    IRHIBuffer CreateBuffer(BufferDesc desc);
    IRHITexture CreateTexture(TextureDesc desc);
    IRHISampler CreateSampler(SamplerDesc desc);
    /// <summary>
    /// M5: generate full mip chain for a texture created with MipLevels &gt; 1
    /// after level 0 was uploaded. Blocking; call during load, not per frame.
    /// Metal implements; Vulkan/DirectX11 throw until their paths are built.
    /// </summary>
    void GenerateMipmaps(IRHITexture texture);
    
    // Pipeline
    IRHIPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc);
    IRHIPipeline CreateComputePipeline(ComputePipelineDesc desc);
    
    // Commands
    IRHICommandBuffer CreateCommandBuffer();
    void Submit(IRHICommandBuffer commandBuffer);
    /// <summary>Submit with swapchain synchronization. Call <see cref="IRHISwapchain.Present"/> separately.</summary>
    void Submit(IRHICommandBuffer commandBuffer, IRHISwapchain swapchain);
    void WaitIdle();
    
    // Data upload
    void UploadBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0);
    void UpdateBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0);
    void UploadTexture(IRHITexture texture, ReadOnlySpan<byte> data, uint mipLevel = 0);
    
}
