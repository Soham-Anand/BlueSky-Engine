namespace NotBSRenderer;

internal sealed class ValidationTexture : IRHITexture, IRHIWrapped<IRHITexture>
{
    private bool _disposed;

    public ValidationTexture(ValidationDevice owner, IRHITexture inner, TextureDesc desc)
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
        if (_disposed)
            return;

        Inner.Dispose();
        _disposed = true;
    }

    internal void RequireNotDisposed()
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationTexture));
    }
}
