using System.Numerics;

namespace NotBSRenderer;

internal sealed class ValidationCommandBuffer : IRHICommandBuffer, IRHIWrapped<IRHICommandBuffer>
{
    private bool _disposed;
    private bool _inRenderPass;
    private bool _pipelineSet;
    private bool _indexBufferSet;
    private bool _vertexBufferSet;

    public ValidationCommandBuffer(ValidationDevice owner, IRHICommandBuffer inner)
    {
        Owner = owner;
        Inner = inner;
    }

    public ValidationDevice Owner { get; }
    public IRHICommandBuffer Inner { get; }

    public void BeginRenderPass(IRHITexture renderTarget, ClearValue clearValue)
    {
        RequireNotDisposed();
        RHIValidation.RequireState(!_inRenderPass, "BeginRenderPass called while already in a render pass.");

        var validatedTarget = Owner.ExpectTexture(renderTarget);
        RHIValidation.Require((validatedTarget.Usage & TextureUsage.RenderTarget) != 0, "Render target texture must have RenderTarget usage.");

        Inner.BeginRenderPass(validatedTarget.Inner, clearValue);
        _inRenderPass = true;
        _pipelineSet = false;
        _indexBufferSet = false;
        _vertexBufferSet = false;
    }

    public void BeginRenderPass(IRHITexture[] colorTargets, IRHITexture? depthTarget, ClearValue clearValue)
    {
        RequireNotDisposed();
        RHIValidation.RequireState(!_inRenderPass, "BeginRenderPass called while already in a render pass.");
        ArgumentNullException.ThrowIfNull(colorTargets);
        RHIValidation.Require(colorTargets.Length > 0 || depthTarget != null, "Render pass requires at least one color target or a depth target.");

        var validatedColorTargets = new IRHITexture[colorTargets.Length];
        for (var i = 0; i < colorTargets.Length; i++)
        {
            var validated = Owner.ExpectTexture(colorTargets[i]);
            RHIValidation.Require((validated.Usage & TextureUsage.RenderTarget) != 0, "Color target texture must have RenderTarget usage.");
            validatedColorTargets[i] = validated.Inner;
        }

        IRHITexture? validatedDepth = null;
        if (depthTarget != null)
        {
            var validated = Owner.ExpectTexture(depthTarget);
            RHIValidation.Require((validated.Usage & TextureUsage.DepthStencil) != 0, "Depth texture must have DepthStencil usage.");
            validatedDepth = validated.Inner;
        }

        Inner.BeginRenderPass(validatedColorTargets, validatedDepth, clearValue);
        _inRenderPass = true;
        _pipelineSet = false;
        _indexBufferSet = false;
        _vertexBufferSet = false;
    }

    public void EndRenderPass()
    {
        RequireNotDisposed();
        RHIValidation.RequireState(_inRenderPass, "EndRenderPass called without an active render pass.");
        Inner.EndRenderPass();
        _inRenderPass = false;
    }

    public void SetPipeline(IRHIPipeline pipeline)
    {
        RequireNotDisposed();
        var validated = Owner.ExpectPipeline(pipeline);
        Inner.SetPipeline(validated.Inner);
        _pipelineSet = true;
    }

    public void SetViewport(Viewport viewport)
    {
        RequireNotDisposed();
        RHIValidation.ValidateViewport(viewport);
        Inner.SetViewport(viewport);
    }

    public void SetScissor(Scissor scissor)
    {
        RequireNotDisposed();
        RHIValidation.ValidateScissor(scissor);
        Inner.SetScissor(scissor);
    }

    public void SetVertexBuffer(IRHIBuffer buffer, uint binding = 0, ulong offset = 0)
    {
        RequireNotDisposed();
        var validated = Owner.ExpectBuffer(buffer);
        RHIValidation.Require((validated.Usage & BufferUsage.Vertex) != 0, "Buffer must have Vertex usage.");
        RHIValidation.Require(offset < validated.Size, "Vertex buffer offset out of range.");
        Inner.SetVertexBuffer(validated.Inner, binding, offset);
        _vertexBufferSet = true;
    }

    public void SetIndexBuffer(IRHIBuffer buffer, IndexType indexType, ulong offset = 0)
    {
        RequireNotDisposed();
        var validated = Owner.ExpectBuffer(buffer);
        RHIValidation.Require((validated.Usage & BufferUsage.Index) != 0, "Buffer must have Index usage.");
        RHIValidation.Require(offset < validated.Size, "Index buffer offset out of range.");
        Inner.SetIndexBuffer(validated.Inner, indexType, offset);
        _indexBufferSet = true;
    }

    public void SetUniformBuffer(IRHIBuffer buffer, uint binding, uint set = 0)
    {
        RequireNotDisposed();
        var validated = Owner.ExpectBuffer(buffer);
        RHIValidation.Require((validated.Usage & BufferUsage.Uniform) != 0, "Buffer must have Uniform usage.");
        Inner.SetUniformBuffer(validated.Inner, binding, set);
    }

    public void SetTexture(IRHITexture texture, uint binding, uint set = 0)
    {
        RequireNotDisposed();
        var validated = Owner.ExpectTexture(texture);
        RHIValidation.Require((validated.Usage & (TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.RenderTarget)) != 0,
            "Texture must be Sampled, Storage, or RenderTarget usage.");
        Inner.SetTexture(validated.Inner, binding, set);
    }

    public void SetVertexUniforms(uint binding, ReadOnlySpan<byte> data)
    {
        RequireNotDisposed();
        RHIValidation.Require(data.Length > 0, "Vertex uniform data must not be empty.");
        Inner.SetVertexUniforms(binding, data);
    }

    public void SetFragmentUniforms(uint binding, ReadOnlySpan<byte> data)
    {
        RequireNotDisposed();
        RHIValidation.Require(data.Length > 0, "Fragment uniform data must not be empty.");
        Inner.SetFragmentUniforms(binding, data);
    }

    public void SetVertexUniforms(uint binding, ref Matrix4x4 matrix)
    {
        RequireNotDisposed();
        Inner.SetVertexUniforms(binding, ref matrix);
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        RequireNotDisposed();
        RequireReadyForDraw();
        RHIValidation.Require(vertexCount > 0, "Draw vertexCount must be greater than zero.");
        RHIValidation.Require(instanceCount > 0, "Draw instanceCount must be greater than zero.");
        Inner.Draw(vertexCount, instanceCount, firstVertex, firstInstance);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0)
    {
        RequireNotDisposed();
        RequireReadyForDraw();
        RHIValidation.Require(_indexBufferSet, "DrawIndexed requires an index buffer.");
        RHIValidation.Require(indexCount > 0, "DrawIndexed indexCount must be greater than zero.");
        RHIValidation.Require(instanceCount > 0, "DrawIndexed instanceCount must be greater than zero.");
        Inner.DrawIndexed(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
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
        RHIValidation.RequireNotDisposed(_disposed, nameof(ValidationCommandBuffer));
    }

    internal void RequireReadyForSubmit()
    {
        RequireNotDisposed();
        RHIValidation.RequireState(!_inRenderPass, "Command buffer submitted with an active render pass.");
    }

    private void RequireReadyForDraw()
    {
        RHIValidation.RequireState(_inRenderPass, "Draw called outside of a render pass.");
        RHIValidation.RequireState(_pipelineSet, "Draw called without a pipeline set.");
        RHIValidation.RequireState(_vertexBufferSet, "Draw called without a vertex buffer set.");
    }
}
