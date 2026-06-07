using System.Numerics;
using System.Runtime.InteropServices;

namespace NotBSRenderer.DirectX11;

/// <summary>
/// DirectX 11 command buffer wrapping ID3D11DeviceContext (immediate context).
/// DX11 uses an immediate context model — commands execute as soon as they're recorded.
/// </summary>
internal sealed class D3D11CommandBuffer : IRHICommandBuffer
{
    private readonly IntPtr _context;  // ID3D11DeviceContext* (not owned)
    private readonly IntPtr _device;   // ID3D11Device* (not owned)
    private D3D11Pipeline? _currentPipeline;
    private bool _disposed;

    internal D3D11CommandBuffer(IntPtr device, IntPtr context)
    {
        _device = device;
        _context = context;
    }

    // ── Render pass ──────────────────────────────────────────────────────

    public void BeginRenderPass(IRHITexture renderTarget, ClearValue clearValue)
    {
        BeginRenderPass(new[] { renderTarget }, null, clearValue);
    }

    public void BeginRenderPass(IRHITexture[] colorTargets, IRHITexture? depthTarget, ClearValue clearValue)
    {
        if (_context == IntPtr.Zero) return;

        // Collect RTVs
        var rtvs = new IntPtr[colorTargets.Length];
        for (int i = 0; i < colorTargets.Length; i++)
        {
            if (colorTargets[i] is D3D11Texture dx11Tex)
                rtvs[i] = dx11Tex.RTV;
        }

        IntPtr dsv = IntPtr.Zero;
        if (depthTarget is D3D11Texture depthTex)
            dsv = depthTex.DSV;

        // OMSetRenderTargets — vtable slot 33
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 33);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, IntPtr, void>)fnPtr;
            fixed (IntPtr* pRtvs = rtvs)
            {
                fn(_context, (uint)rtvs.Length, pRtvs, dsv);
            }
        }

        // Clear
        if (!clearValue.LoadInsteadOfClear)
        {
            foreach (var rtv in rtvs)
            {
                if (rtv != IntPtr.Zero)
                    ClearRTV(rtv, clearValue.Color);
            }
            if (dsv != IntPtr.Zero)
                ClearDSV(dsv, clearValue.Depth);
        }
    }

    public void EndRenderPass()
    {
        // DX11 doesn't have explicit render pass end — state persists
    }

    // ── Pipeline binding ─────────────────────────────────────────────────

    public void SetPipeline(IRHIPipeline pipeline)
    {
        if (pipeline is not D3D11Pipeline dx11Pipeline || _context == IntPtr.Zero) return;
        _currentPipeline = dx11Pipeline;

        // Bind all state objects via immediate context
        ContextSetVS(dx11Pipeline.VertexShader);
        ContextSetPS(dx11Pipeline.PixelShader);
        ContextSetInputLayout(dx11Pipeline.InputLayout);
        ContextSetBlendState(dx11Pipeline.BlendState);
        ContextSetDepthStencilState(dx11Pipeline.DepthStencilState);
        ContextSetRasterizerState(dx11Pipeline.RasterizerState);
        ContextSetTopology(dx11Pipeline.Topology);
    }

    public void SetViewport(Viewport viewport)
    {
        if (_context == IntPtr.Zero) return;
        var vp = new D3D11_VIEWPORT
        {
            TopLeftX = viewport.X, TopLeftY = viewport.Y,
            Width = viewport.Width, Height = viewport.Height,
            MinDepth = viewport.MinDepth, MaxDepth = viewport.MaxDepth
        };
        // RSSetViewports — vtable slot 44
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 44);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, D3D11_VIEWPORT*, void>)fnPtr;
            fn(_context, 1, &vp);
        }
    }

    public void SetScissor(Scissor scissor)
    {
        if (_context == IntPtr.Zero) return;
        var rect = new D3D11_RECT
        {
            Left = scissor.X, Top = scissor.Y,
            Right = scissor.X + (int)scissor.Width,
            Bottom = scissor.Y + (int)scissor.Height
        };
        // RSSetScissorRects — vtable slot 45
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 45);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, D3D11_RECT*, void>)fnPtr;
            fn(_context, 1, &rect);
        }
    }

    // ── Resource binding ─────────────────────────────────────────────────

    public void SetVertexBuffer(IRHIBuffer buffer, uint binding = 0, ulong offset = 0)
    {
        if (buffer is not D3D11Buffer dx11Buf || _context == IntPtr.Zero) return;
        IntPtr buf = dx11Buf.NativePtr;
        uint stride = 32; // Fallback default
        if (_currentPipeline != null && binding < _currentPipeline.VertexStrides.Length)
            stride = _currentPipeline.VertexStrides[binding];
            
        uint uOffset = (uint)offset;

        // IASetVertexBuffers — vtable slot 18
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 18);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, uint*, uint*, void>)fnPtr;
            fn(_context, binding, 1, &buf, &stride, &uOffset);
        }
    }

    public void SetIndexBuffer(IRHIBuffer buffer, IndexType indexType, ulong offset = 0)
    {
        if (buffer is not D3D11Buffer dx11Buf || _context == IntPtr.Zero) return;
        uint format = indexType == IndexType.UInt16 ? 57u /* R16_UINT */ : 42u /* R32_UINT */;

        // IASetIndexBuffer — vtable slot 19
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 19);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, void>)fnPtr;
            fn(_context, dx11Buf.NativePtr, format, (uint)offset);
        }
    }

    public void SetUniformBuffer(IRHIBuffer buffer, uint binding, uint set = 0)
    {
        if (buffer is not D3D11Buffer dx11Buf || _context == IntPtr.Zero) return;
        IntPtr buf = dx11Buf.NativePtr;

        // Bind to both VS and PS constant buffer slots
        // VSSetConstantBuffers — vtable slot 7
        // PSSetConstantBuffers — vtable slot 16 (after IASet*)
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            // VS
            IntPtr fnVS = *((IntPtr*)vtable + 7);
            var vsSet = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)fnVS;
            vsSet(_context, binding, 1, &buf);
            // PS
            IntPtr fnPS = *((IntPtr*)vtable + 16);
            var psSet = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)fnPS;
            psSet(_context, binding, 1, &buf);
        }
    }

    public void SetTexture(IRHITexture texture, uint binding, uint set = 0)
    {
        if (texture is not D3D11Texture dx11Tex || _context == IntPtr.Zero) return;
        IntPtr srv = dx11Tex.SRV;

        // PSSetShaderResources — vtable slot 8
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 8);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)fnPtr;
            fn(_context, binding, 1, &srv);
        }
    }

    public void SetStorageBuffer(IRHIBuffer buffer, uint binding, uint set = 0) { /* DX11 UAV binding — FL 11.0+ */ }
    public void SetStorageTexture(IRHITexture texture, uint binding, uint set = 0) { /* DX11 UAV binding */ }
    public void SetBindlessResourceTable(uint set, ReadOnlySpan<BindlessResourceHandle> handles) { /* Not supported on DX11 */ }

    // ── Uniforms ─────────────────────────────────────────────────────────

    public void SetVertexUniforms(uint binding, ReadOnlySpan<byte> data)
    {
        // DX11 uses constant buffers, not push constants. For small per-draw data,
        // we create a temporary constant buffer or use a pre-allocated staging buffer.
        // For now, this is handled by UpdateBuffer + SetUniformBuffer in the renderer.
    }

    public void SetFragmentUniforms(uint binding, ReadOnlySpan<byte> data) { }
    public void SetComputeUniforms(uint binding, ReadOnlySpan<byte> data) { }
    public void SetVertexUniforms(uint binding, ref Matrix4x4 matrix) { }

    // ── Draw commands ────────────────────────────────────────────────────

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        if (_context == IntPtr.Zero) return;
        if (instanceCount <= 1)
        {
            // Draw — vtable slot 13
            unsafe
            {
                IntPtr vtable = *(IntPtr*)_context;
                IntPtr fnPtr = *((IntPtr*)vtable + 13);
                var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, void>)fnPtr;
                fn(_context, vertexCount, firstVertex);
            }
        }
        else
        {
            // DrawInstanced — vtable slot 20
            unsafe
            {
                IntPtr vtable = *(IntPtr*)_context;
                IntPtr fnPtr = *((IntPtr*)vtable + 20);
                var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, uint, void>)fnPtr;
                fn(_context, vertexCount, instanceCount, firstVertex, firstInstance);
            }
        }
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0,
        int vertexOffset = 0, uint firstInstance = 0)
    {
        if (_context == IntPtr.Zero) return;
        if (instanceCount <= 1)
        {
            // DrawIndexed — vtable slot 12
            unsafe
            {
                IntPtr vtable = *(IntPtr*)_context;
                IntPtr fnPtr = *((IntPtr*)vtable + 12);
                var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, int, void>)fnPtr;
                fn(_context, indexCount, firstIndex, vertexOffset);
            }
        }
        else
        {
            // DrawIndexedInstanced — vtable slot 21
            unsafe
            {
                IntPtr vtable = *(IntPtr*)_context;
                IntPtr fnPtr = *((IntPtr*)vtable + 21);
                var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, int, uint, void>)fnPtr;
                fn(_context, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
            }
        }
    }

    public void DrawIndirect(IRHIBuffer buffer, ulong offset, uint drawCount, uint stride) { }
    public void DrawIndexedIndirect(IRHIBuffer buffer, ulong offset, uint drawCount, uint stride) { }

    // ── Compute ──────────────────────────────────────────────────────────

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        if (_context == IntPtr.Zero) return;
        // ID3D11DeviceContext::Dispatch — vtable slot 41
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 41);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, void>)fnPtr;
            fn(_context, groupCountX, groupCountY, groupCountZ);
        }
    }

    public void DispatchIndirect(IRHIBuffer buffer, ulong offset)
    {
        if (buffer is not D3D11Buffer dx11Buf || _context == IntPtr.Zero) return;
        // ID3D11DeviceContext::DispatchIndirect — vtable slot 42
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 42);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)fnPtr;
            fn(_context, dx11Buf.NativePtr, (uint)offset);
        }
    }

    // ── Sampler binding ─────────────────────────────────────────────────

    /// <summary>Binds a sampler to both VS and PS stages at the given slot.</summary>
    internal void SetSampler(IntPtr sampler, uint slot)
    {
        if (_context == IntPtr.Zero || sampler == IntPtr.Zero) return;
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            // PSSetSamplers — vtable slot 10
            IntPtr fnPS = *((IntPtr*)vtable + 10);
            var psSet = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)fnPS;
            psSet(_context, slot, 1, &sampler);
            // VSSetSamplers — vtable slot 26
            IntPtr fnVS = *((IntPtr*)vtable + 26);
            var vsSet = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)fnVS;
            vsSet(_context, slot, 1, &sampler);
        }
    }

    // ── Barriers ─────────────────────────────────────────────────────────

    public void MemoryBarrier() { }     // DX11 handles barriers implicitly
    public void BufferBarrier(IRHIBuffer buffer) { }
    public void TextureBarrier(IRHITexture texture) { }

    // ── Internal helpers ─────────────────────────────────────────────────

    private void ClearRTV(IntPtr rtv, Vector4 color)
    {
        // ClearRenderTargetView — vtable slot 50
        unsafe
        {
            float* c = stackalloc float[4] { color.X, color.Y, color.Z, color.W };
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 50);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, float*, void>)fnPtr;
            fn(_context, rtv, c);
        }
    }

    private void ClearDSV(IntPtr dsv, float depth)
    {
        // ClearDepthStencilView — vtable slot 53
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 53);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, float, byte, void>)fnPtr;
            fn(_context, dsv, 1 /* D3D11_CLEAR_DEPTH */, depth, 0);
        }
    }

    private void ContextSetVS(IntPtr vs)
    {
        if (_context == IntPtr.Zero) return;
        // VSSetShader — vtable slot 11
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 11);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, uint, void>)fnPtr;
            fn(_context, vs, IntPtr.Zero, 0);
        }
    }

    private void ContextSetPS(IntPtr ps)
    {
        if (_context == IntPtr.Zero) return;
        // PSSetShader — vtable slot 9
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 9);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, uint, void>)fnPtr;
            fn(_context, ps, IntPtr.Zero, 0);
        }
    }

    private void ContextSetInputLayout(IntPtr layout)
    {
        if (_context == IntPtr.Zero) return;
        // IASetInputLayout — vtable slot 17
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 17);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)fnPtr;
            fn(_context, layout);
        }
    }

    private void ContextSetBlendState(IntPtr blendState)
    {
        if (_context == IntPtr.Zero) return;
        // OMSetBlendState — vtable slot 35
        unsafe
        {
            float* factor = stackalloc float[4] { 1, 1, 1, 1 };
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 35);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, float*, uint, void>)fnPtr;
            fn(_context, blendState, factor, 0xFFFFFFFF);
        }
    }

    private void ContextSetDepthStencilState(IntPtr dss)
    {
        if (_context == IntPtr.Zero) return;
        // OMSetDepthStencilState — vtable slot 36
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 36);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)fnPtr;
            fn(_context, dss, 0);
        }
    }

    private void ContextSetRasterizerState(IntPtr rs)
    {
        if (_context == IntPtr.Zero) return;
        // RSSetState — vtable slot 43
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 43);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)fnPtr;
            fn(_context, rs);
        }
    }

    private void ContextSetTopology(uint topology)
    {
        if (_context == IntPtr.Zero) return;
        // IASetPrimitiveTopology — vtable slot 24
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 24);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, void>)fnPtr;
            fn(_context, topology);
        }
    }

    // ── Structs ──────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_VIEWPORT
    {
        public float TopLeftX, TopLeftY, Width, Height, MinDepth, MaxDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_RECT
    {
        public int Left, Top, Right, Bottom;
    }

    public void Dispose()
    {
        // We don't own the context — it belongs to the device
        _disposed = true;
    }
}
