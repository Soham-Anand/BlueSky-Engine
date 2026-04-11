namespace NotBSRenderer;

internal sealed class ValidationSwapchain : IRHISwapchain, IRHIWrapped<IRHISwapchain>
{
    private bool _disposed;
    private bool _hasImage;
    private ValidationSwapchainTexture? _currentRenderTarget;
    private IRHITexture? _currentInner;

    public ValidationSwapchain(ValidationDevice owner, IRHISwapchain inner)
    {
        Owner = owner;
        Inner = inner;
    }

    public ValidationDevice Owner { get; }
    public IRHISwapchain Inner { get; }

    public uint Width => Inner.Width;
    public uint Height => Inner.Height;
    public TextureFormat Format => Inner.Format;
    public IRHITexture CurrentRenderTarget
    {
        get
        {
            RequireNotDisposed();
            var innerTarget = Inner.CurrentRenderTarget;
            if (_currentRenderTarget == null || !ReferenceEquals(innerTarget, _currentInner))
            {
                _currentInner = innerTarget;
                _currentRenderTarget = new ValidationSwapchainTexture(
                    Owner,
                    innerTarget,
                    new TextureDesc
                    {
                        Width = Inner.Width,
                        Height = Inner.Height,
                        Depth = 1,
                        MipLevels = 1,
                        ArrayLayers = 1,
                        Format = Inner.Format,
                        Usage = TextureUsage.RenderTarget,
                        DebugName = "SwapchainRenderTarget"
                    });
            }

            return _currentRenderTarget;
        }
    }

    public void AcquireNextImage()
    {
        RequireNotDisposed();
        Inner.AcquireNextImage();
        _hasImage = true;
    }

    public void Resize(uint width, uint height)
    {
        RequireNotDisposed();
        RHIValidation.Require(width > 0 && height > 0, "Swapchain resize dimensions must be greater than zero.");
        Inner.Resize(width, height);
        _hasImage = false;
        _currentRenderTarget = null;
        _currentInner = null;
    }

    public void Present()
    {
        RequireNotDisposed();
        RHIValidation.RequireState(_hasImage, "Swapchain Present called before AcquireNextImage.");
        Inner.Present();
        _hasImage = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Inner.Dispose();
        _disposed = true;
    }

    internal void RequireNotDisposed()
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationSwapchain));
    }
}
