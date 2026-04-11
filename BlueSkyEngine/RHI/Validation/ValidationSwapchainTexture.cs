namespace NotBSRenderer;

internal sealed class ValidationSwapchainTexture : IRHITexture, IRHIWrapped<IRHITexture>
{
    public ValidationSwapchainTexture(ValidationDevice owner, IRHITexture inner, TextureDesc desc)
    {
        Owner = owner;
        Inner = inner;
        Desc = desc;
    }

    public ValidationDevice Owner { get; }
    public IRHITexture Inner { get; }
    public TextureDesc Desc { get; }

    public uint Width => Desc.Width;
    public uint Height => Desc.Height;
    public TextureFormat Format => Desc.Format;
    public TextureUsage Usage => Desc.Usage;

    public void Dispose()
    {
        // Swapchain render targets are owned by the swapchain.
    }
}
