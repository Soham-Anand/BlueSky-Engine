namespace NotBSRenderer;

internal sealed class ValidationBuffer : IRHIBuffer, IRHIWrapped<IRHIBuffer>
{
    private bool _disposed;

    public ValidationBuffer(ValidationDevice owner, IRHIBuffer inner, BufferDesc desc)
    {
        Owner = owner;
        Inner = inner;
        Desc = desc;
    }

    public ValidationDevice Owner { get; }
    public IRHIBuffer Inner { get; }
    public BufferDesc Desc { get; }

    public ulong Size => Desc.Size;
    public BufferUsage Usage => Desc.Usage;
    public MemoryType MemoryType => Desc.MemoryType;

    public void Dispose()
    {
        if (_disposed)
            return;

        Inner.Dispose();
        _disposed = true;
    }

    internal void RequireNotDisposed()
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationBuffer));
    }
}
