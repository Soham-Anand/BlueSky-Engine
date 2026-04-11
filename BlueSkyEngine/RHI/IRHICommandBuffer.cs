using System.Numerics;

namespace NotBSRenderer;

public interface IRHICommandBuffer : IDisposable
{
    // Render pass
    void BeginRenderPass(IRHITexture renderTarget, ClearValue clearValue);
    void BeginRenderPass(IRHITexture[] colorTargets, IRHITexture? depthTarget, ClearValue clearValue);
    void EndRenderPass();
    
    // Pipeline binding
    void SetPipeline(IRHIPipeline pipeline);
    void SetViewport(Viewport viewport);
    void SetScissor(Scissor scissor);
    
    // Resource binding
    void SetVertexBuffer(IRHIBuffer buffer, uint binding = 0, ulong offset = 0);
    void SetIndexBuffer(IRHIBuffer buffer, IndexType indexType, ulong offset = 0);
    void SetUniformBuffer(IRHIBuffer buffer, uint binding, uint set = 0);
    void SetTexture(IRHITexture texture, uint binding, uint set = 0);
    
    // Uniforms (Direct constants for DX9 / Small buffers for Metal)
    void SetVertexUniforms(uint binding, ReadOnlySpan<byte> data);
    void SetFragmentUniforms(uint binding, ReadOnlySpan<byte> data);
    
    // Matrix helpers (convenience)
    void SetVertexUniforms(uint binding, ref Matrix4x4 matrix);
    
    // Draw commands
    void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0);
    void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0);
}
