namespace NotBSRenderer;

internal sealed class ValidationPipeline : IRHIPipeline, IRHIWrapped<IRHIPipeline>
{
    private bool _disposed;

    public ValidationPipeline(ValidationDevice owner, IRHIPipeline inner, GraphicsPipelineDesc desc)
    {
        Owner = owner;
        Inner = inner;
        Desc = desc;
    }

    public ValidationDevice Owner { get; }
    public IRHIPipeline Inner { get; }
    public GraphicsPipelineDesc Desc { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        Inner.Dispose();
        _disposed = true;
    }

    internal void RequireNotDisposed()
    {
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationPipeline));
    }
}
