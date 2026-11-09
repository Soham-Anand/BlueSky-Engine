namespace BlueSky.BSR.RHI;

internal sealed class ValidationSampler : IRHISampler, IRHIWrapped<IRHISampler>
{
    private bool _disposed;

    public ValidationSampler(ValidationDevice owner, IRHISampler inner, SamplerDesc desc)
    {
        Owner = owner;
        Inner = inner;
        Desc = desc;
    }

    public ValidationDevice Owner { get; }
    public IRHISampler Inner { get; }
    public SamplerDesc Desc { get; }

    public void Dispose()
    {
        if (_disposed) return;
        Inner.Dispose();
        _disposed = true;
    }

    internal void RequireNotDisposed()
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationSampler));
    }
}
