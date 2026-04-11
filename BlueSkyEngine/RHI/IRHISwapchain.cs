namespace NotBSRenderer;

public interface IRHISwapchain : IDisposable
{
    uint Width { get; }
    uint Height { get; }
    TextureFormat Format { get; }
    
    IRHITexture CurrentRenderTarget { get; }
    
    void AcquireNextImage();
    void Resize(uint width, uint height);
    void Present();
}
