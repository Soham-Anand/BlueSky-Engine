using BlueSky.Platform;

namespace NotBSRenderer;

internal sealed class ValidationDevice : IRHIDevice, IRHIWrapped<IRHIDevice>
{
    private readonly IRHIDevice _inner;
    private bool _disposed;

    private ValidationDevice(IRHIDevice inner)
    {
        _inner = inner;
    }

    public static IRHIDevice Wrap(IRHIDevice device)
    {
        if (!RHIValidation.Enabled)
            return device;

        if (device is ValidationDevice)
            return device;

        return new ValidationDevice(device);
    }

    public IRHIDevice Inner => _inner;
    public RHIBackend Backend => _inner.Backend;

    public IRHISwapchain CreateSwapchain(IWindow window, PresentMode presentMode = PresentMode.Vsync)
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationDevice));
        ArgumentNullException.ThrowIfNull(window);

        var swapchain = _inner.CreateSwapchain(window, presentMode);
        return new ValidationSwapchain(this, swapchain);
    }

    public IRHIBuffer CreateBuffer(BufferDesc desc)
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationDevice));
        RHIValidation.ValidateBufferDesc(desc);

        var buffer = _inner.CreateBuffer(desc);
        return new ValidationBuffer(this, buffer, desc);
    }

    public IRHITexture CreateTexture(TextureDesc desc)
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationDevice));
        RHIValidation.ValidateTextureDesc(desc);

        var texture = _inner.CreateTexture(desc);
        return new ValidationTexture(this, texture, desc);
    }

    public IRHIPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationDevice));
        RHIValidation.ValidatePipelineDesc(desc);

        var pipeline = _inner.CreateGraphicsPipeline(desc);
        return new ValidationPipeline(this, pipeline, desc);
    }

    public IRHICommandBuffer CreateCommandBuffer()
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationDevice));
        var cmd = _inner.CreateCommandBuffer();
        return new ValidationCommandBuffer(this, cmd);
    }

    public void Submit(IRHICommandBuffer commandBuffer)
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationDevice));
        var cmd = ExpectCommandBuffer(commandBuffer);
        cmd.RequireReadyForSubmit();
        _inner.Submit(cmd.Inner);
    }

    public void Submit(IRHICommandBuffer commandBuffer, IRHISwapchain swapchain)
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationDevice));
        var cmd = ExpectCommandBuffer(commandBuffer);
        var validatedSwapchain = ExpectSwapchain(swapchain);
        cmd.RequireReadyForSubmit();
        _inner.Submit(cmd.Inner, validatedSwapchain.Inner);
    }

    public void WaitIdle()
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationDevice));
        _inner.WaitIdle();
    }

    public void UploadBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0)
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationDevice));
        var validated = ExpectBuffer(buffer);
        RHIValidation.ValidateBufferRange(validated, data, offset);
        _inner.UploadBuffer(validated.Inner, data, offset);
    }

    public void UpdateBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0)
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationDevice));
        var validated = ExpectBuffer(buffer);
        RHIValidation.ValidateBufferRange(validated, data, offset);
        _inner.UpdateBuffer(validated.Inner, data, offset);
    }

    public void UploadTexture(IRHITexture texture, ReadOnlySpan<byte> data, uint mipLevel = 0)
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationDevice));
        var validated = ExpectTexture(texture);
        RHIValidation.ValidateTextureUpload(validated, data, mipLevel);
        _inner.UploadTexture(validated.Inner, data, mipLevel);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _inner.Dispose();
        _disposed = true;
    }

    internal ValidationBuffer ExpectBuffer(IRHIBuffer buffer)
    {
        if (buffer is not ValidationBuffer validated || validated.Owner != this)
            throw new ArgumentException("Buffer must be created by this device.");
        validated.RequireNotDisposed();
        return validated;
    }

    internal ValidationTexture ExpectTexture(IRHITexture texture)
    {
        if (texture is ValidationTexture validated)
        {
            if (validated.Owner != this)
                throw new ArgumentException("Texture must be created by this device.");
            validated.RequireNotDisposed();
            return validated;
        }

        if (texture is ValidationSwapchainTexture swapchainTexture)
        {
            if (swapchainTexture.Owner != this)
                throw new ArgumentException("Texture must be created by this device.");
            return new ValidationTexture(this, swapchainTexture.Inner, swapchainTexture.Desc);
        }

        throw new ArgumentException("Texture must be created by this device.");
    }

    internal ValidationSwapchain ExpectSwapchain(IRHISwapchain swapchain)
    {
        if (swapchain is not ValidationSwapchain validated || validated.Owner != this)
            throw new ArgumentException("Swapchain must be created by this device.");
        validated.RequireNotDisposed();
        return validated;
    }

    internal ValidationCommandBuffer ExpectCommandBuffer(IRHICommandBuffer commandBuffer)
    {
        if (commandBuffer is not ValidationCommandBuffer validated || validated.Owner != this)
            throw new ArgumentException("Command buffer must be created by this device.");
        validated.RequireNotDisposed();
        return validated;
    }

    internal ValidationPipeline ExpectPipeline(IRHIPipeline pipeline)
    {
        if (pipeline is not ValidationPipeline validated || validated.Owner != this)
            throw new ArgumentException("Pipeline must be created by this device.");
        validated.RequireNotDisposed();
        return validated;
    }
}
