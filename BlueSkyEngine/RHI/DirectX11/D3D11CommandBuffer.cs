using System.Numerics;
using System.Runtime.InteropServices;

namespace BlueSky.Rendering.RHI.DirectX11;

/// <summary>
/// DirectX 11 command buffer wrapping ID3D11DeviceContext (immediate context).
/// DX11 uses an immediate context model — commands execute as soon as they're recorded.
/// </summary>
internal sealed class D3D11CommandBuffer : IRHICommandBuffer
{
    private readonly IntPtr _context;  // ID3D11DeviceContext* (not owned)
    private readonly IntPtr _device;   // ID3D11Device* (not owned)
    private D3D11Pipeline? _currentPipeline;

    // Precomputed vtable function pointers — resolved ONCE at construction to detect corruption early
    private readonly IntPtr _fnVS;   // VSSetConstantBuffers (slot 7)
    private readonly IntPtr _fnPS;   // PSSetConstantBuffers (slot 16)
    private readonly IntPtr _fnDraw; // Draw (slot 13)
    private readonly IntPtr _fnDrawIndexed; // DrawIndexed (slot 12)
    private readonly IntPtr _fnDrawInstanced; // DrawInstanced (slot 21)
    private readonly IntPtr _fnDrawIndexedInstanced; // DrawIndexedInstanced (slot 20)
    private readonly IntPtr _fnOMSetRT; // OMSetRenderTargets (slot 33)
    private readonly IntPtr _fnClearRTV; // ClearRenderTargetView (slot 50)
    private readonly IntPtr _fnClearDSV; // ClearDepthStencilView (slot 53)
    private readonly IntPtr _fnRSSetVP; // RSSetViewports (slot 44)
    private readonly IntPtr _fnRSSetScissor; // RSSetScissorRects (slot 45)
    private readonly IntPtr _fnIASetVB; // IASetVertexBuffers (slot 18)
    private readonly IntPtr _fnIASetIB; // IASetIndexBuffer (slot 19)
    private readonly IntPtr _fnIASetTopo; // IASetPrimitiveTopology (slot 24)
    private readonly IntPtr _fnVSSetS; // VSSetShader (slot 11)
    private readonly IntPtr _fnPSSetS; // PSSetShader (slot 9)
    private readonly IntPtr _fnIASetIL; // IASetInputLayout (slot 17)
    private readonly IntPtr _fnOMSetBS; // OMSetBlendState (slot 35)
    private readonly IntPtr _fnOMSetDSS; // OMSetDepthStencilState (slot 36)
    private readonly IntPtr _fnRSSetState; // RSSetState (slot 43)
    private readonly IntPtr _fnPSSetSRV; // PSSetShaderResources (slot 8)
    private readonly IntPtr _fnPSSetSamp; // PSSetSamplers (slot 10)
    private readonly IntPtr _fnVSSetSamp; // VSSetSamplers (slot 26)
    private readonly IntPtr _fnUpdateSub; // UpdateSubresource (slot 48)
    private readonly IntPtr _fnMap; // Map (slot 14)
    private readonly IntPtr _fnUnmap; // Unmap (slot 15)

    internal D3D11CommandBuffer(IntPtr device, IntPtr context)
    {
        _device = device;
        _context = context;

        // Resolve ALL vtable pointers upfront — if context or vtable is corrupt, we catch it here
        if (context != IntPtr.Zero)
        {
            unsafe
            {
                IntPtr vtable = *(IntPtr*)context;
                if (vtable == IntPtr.Zero)
                {
                    Console.WriteLine("[DX11] FATAL: Context vtable pointer is NULL!");
                    return;
                }
                _fnVS          = *((IntPtr*)vtable + 7);
                _fnPS          = *((IntPtr*)vtable + 16);
                _fnDraw        = *((IntPtr*)vtable + 13);
                _fnDrawIndexed = *((IntPtr*)vtable + 12);
                _fnDrawInstanced = *((IntPtr*)vtable + 21);
                _fnDrawIndexedInstanced = *((IntPtr*)vtable + 20);
                _fnOMSetRT     = *((IntPtr*)vtable + 33);
                _fnClearRTV    = *((IntPtr*)vtable + 50);
                _fnClearDSV    = *((IntPtr*)vtable + 53);
                _fnRSSetVP     = *((IntPtr*)vtable + 44);
                _fnRSSetScissor = *((IntPtr*)vtable + 45);
                _fnIASetVB     = *((IntPtr*)vtable + 18);
                _fnIASetIB     = *((IntPtr*)vtable + 19);
                _fnIASetTopo   = *((IntPtr*)vtable + 24);
                _fnVSSetS      = *((IntPtr*)vtable + 11);
                _fnPSSetS      = *((IntPtr*)vtable + 9);
                _fnIASetIL     = *((IntPtr*)vtable + 17);
                _fnOMSetBS     = *((IntPtr*)vtable + 35);
                _fnOMSetDSS    = *((IntPtr*)vtable + 36);
                _fnRSSetState  = *((IntPtr*)vtable + 43);
                _fnPSSetSRV    = *((IntPtr*)vtable + 8);
                _fnPSSetSamp   = *((IntPtr*)vtable + 10);
                _fnVSSetSamp   = *((IntPtr*)vtable + 26);
                _fnUpdateSub   = *((IntPtr*)vtable + 48);
                _fnMap         = *((IntPtr*)vtable + 14);
                _fnUnmap       = *((IntPtr*)vtable + 15);

                // Sanity check: all resolved pointers must be non-null
                if (_fnVS == IntPtr.Zero || _fnPS == IntPtr.Zero || _fnDraw == IntPtr.Zero)
                {
                    Console.WriteLine($"[DX11] WARNING: Some vtable pointers are NULL! vtable=0x{vtable:X}");
                }
            }
        }
    }

    // ── Render pass ──────────────────────────────────────────────────────

    public void BeginRenderPass(IRHITexture renderTarget, ClearValue clearValue)
    {
        BeginRenderPass(new[] { renderTarget }, null, clearValue);
    }

    public void BeginRenderPass(IRHITexture[] colorTargets, IRHITexture? depthTarget, ClearValue clearValue)
    {
        if (_context == IntPtr.Zero)
        {
            return;
        }

        // Collect RTVs
        var rtvs = new IntPtr[colorTargets.Length];
        for (int i = 0; i < colorTargets.Length; i++)
        {
            if (colorTargets[i] is D3D11Texture dx11Tex)
            {
                rtvs[i] = dx11Tex.RTV;
            }
        }

        IntPtr dsv = IntPtr.Zero;
        if (depthTarget is D3D11Texture depthTex)
        {
            dsv = depthTex.DSV;
        }

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
        if (pipeline is not D3D11Pipeline dx11Pipeline || _context == IntPtr.Zero)
        {
            return;
        }
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
        if (buffer is not D3D11Buffer dx11Buf)
        {
            return;
        }
        if (_context == IntPtr.Zero)
        {
            return;
        }
        IntPtr buf = dx11Buf.NativePtr;
        if (buf == IntPtr.Zero)
        {
            return;
        }
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
        if (buffer is not D3D11Buffer dx11Buf)
        {
            return;
        }
        if (_context == IntPtr.Zero)
        {
            return;
        }
        if (dx11Buf.NativePtr == IntPtr.Zero)
        {
            return;
        }
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
        if (buffer is not D3D11Buffer dx11Buf)
        {
            return;
        }
        if (_context == IntPtr.Zero)
        {
            return;
        }
        IntPtr buf = dx11Buf.NativePtr;
        if (buf == IntPtr.Zero)
        {
            return;
        }

        unsafe
        {
            // Pre-flight validation: re-read vtable pointer to detect corruption
            IntPtr vtable = *(IntPtr*)_context;
            if (vtable == IntPtr.Zero)
            {
                return;
            }

            // Bind to both VS and PS stages using precomputed or fresh pointers
            IntPtr fnVS = _fnVS != IntPtr.Zero ? _fnVS : *((IntPtr*)vtable + 7);
            IntPtr fnPS = _fnPS != IntPtr.Zero ? _fnPS : *((IntPtr*)vtable + 16);

            // Validate function pointers look reasonable (must be in module memory range)
            // We can't fully validate, but null check catches obvious corruption
            if (fnVS == IntPtr.Zero || fnPS == IntPtr.Zero)
            {
                return;
            }

            var vsSet = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)fnVS;
            vsSet(_context, binding, 1, &buf);
            var psSet = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)fnPS;
            psSet(_context, binding, 1, &buf);
        }
    }

    public void SetTexture(IRHITexture texture, uint binding, uint set = 0)
    {
        if (texture is not D3D11Texture dx11Tex)
        {
            return;
        }
        if (_context == IntPtr.Zero)
        {
            return;
        }
        IntPtr srv = dx11Tex.SRV;
        if (srv == IntPtr.Zero)
        {
            return;
        }

        // PSSetShaderResources — vtable slot 8
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            IntPtr fnPtr = *((IntPtr*)vtable + 8);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)fnPtr;
            fn(_context, binding, 1, &srv);
        }
    }

    public void SetStorageBuffer(IRHIBuffer buffer, uint binding, uint set = 0) =>
        throw new NotSupportedException("D3D11 storage-buffer binding is not implemented by this backend.");

    public void SetStorageTexture(IRHITexture texture, uint binding, uint set = 0) =>
        throw new NotSupportedException("D3D11 storage-texture binding is not implemented by this backend.");

    // ── Uniforms ─────────────────────────────────────────────────────────

    public void SetVertexUniforms(uint binding, ReadOnlySpan<byte> data)
    {
        SetStageUniforms(binding, data, vertexStage: true);
    }

    public void SetFragmentUniforms(uint binding, ReadOnlySpan<byte> data)
    {
        SetStageUniforms(binding, data, vertexStage: false);
    }

    private unsafe void SetStageUniforms(uint binding, ReadOnlySpan<byte> data, bool vertexStage)
    {
        if (data.Length == 0) return;
        if (_context == IntPtr.Zero || _device == IntPtr.Zero)
            throw new InvalidOperationException("Cannot bind D3D11 constants without a valid device context.");

        uint aligned = checked((uint)((data.Length + 15) & ~15));
        var desc = new D3D11Interop.D3D11_BUFFER_DESC
        {
            ByteWidth = aligned,
            Usage = D3D11Interop.D3D11_USAGE_DYNAMIC,
            BindFlags = D3D11Interop.D3D11_BIND_CONSTANT_BUFFER,
            CPUAccessFlags = D3D11Interop.D3D11_CPU_ACCESS_WRITE,
            MiscFlags = 0,
            StructureByteStride = 0
        };

        IntPtr deviceVtable = *(IntPtr*)_device;
        IntPtr createBufferFn = *((IntPtr*)deviceVtable + 3);
        var createBuffer = (delegate* unmanaged[Stdcall]<IntPtr, D3D11Interop.D3D11_BUFFER_DESC*, IntPtr, out IntPtr, int>)createBufferFn;
        int createResult = createBuffer(_device, &desc, IntPtr.Zero, out var buffer);
        if (createResult < 0 || buffer == IntPtr.Zero)
            throw new InvalidOperationException($"D3D11 constant-buffer creation failed: HRESULT 0x{createResult:X8}.");

        try
        {
            IntPtr contextVtable = *(IntPtr*)_context;
            IntPtr mapFn = *((IntPtr*)contextVtable + 14);
            var map = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, out D3D11DeviceAPI.D3D11_MAPPED_SUBRESOURCE, int>)mapFn;
            int mapResult = map(_context, buffer, 0, 4, 0, out var mapped);
            if (mapResult < 0 || mapped.pData == IntPtr.Zero)
                throw new InvalidOperationException($"D3D11 constant-buffer map failed: HRESULT 0x{mapResult:X8}.");

            fixed (byte* source = data)
                Buffer.MemoryCopy(source, (void*)mapped.pData, aligned, data.Length);

            IntPtr unmapFn = *((IntPtr*)contextVtable + 15);
            var unmap = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)unmapFn;
            unmap(_context, buffer, 0);

            int slot = vertexStage ? 7 : 16;
            IntPtr setConstantsFn = *((IntPtr*)contextVtable + slot);
            var setConstants = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)setConstantsFn;
            setConstants(_context, binding, 1, &buffer);
        }
        finally
        {
            Marshal.Release(buffer);
        }
    }

    public void SetComputeUniforms(uint binding, ReadOnlySpan<byte> data) =>
        throw new NotSupportedException("D3D11 compute pipelines are not implemented by this backend.");

    public void SetVertexUniforms(uint binding, ref Matrix4x4 matrix)
    {
        SetStageUniforms(binding, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref matrix, 1)), vertexStage: true);
    }

    // ── Draw commands ────────────────────────────────────────────────────

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        if (_context == IntPtr.Zero)
        {
            return;
        }
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
            // DrawInstanced — vtable slot 21
            unsafe
            {
                IntPtr vtable = *(IntPtr*)_context;
                IntPtr fnPtr = *((IntPtr*)vtable + 21);
                var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, uint, void>)fnPtr;
                fn(_context, vertexCount, instanceCount, firstVertex, firstInstance);
            }
        }
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0,
        int vertexOffset = 0, uint firstInstance = 0)
    {
        if (_context == IntPtr.Zero)
        {
            return;
        }
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
            // DrawIndexedInstanced — vtable slot 20
            unsafe
            {
                IntPtr vtable = *(IntPtr*)_context;
                IntPtr fnPtr = *((IntPtr*)vtable + 20);
                var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, int, uint, void>)fnPtr;
                fn(_context, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
            }
        }
    }

    public void DrawIndirect(IRHIBuffer buffer, ulong offset, uint drawCount, uint stride) =>
        DrawIndirectCore(buffer, offset, drawCount, stride, indexed: false);

    public void DrawIndexedIndirect(IRHIBuffer buffer, ulong offset, uint drawCount, uint stride) =>
        DrawIndirectCore(buffer, offset, drawCount, stride, indexed: true);

    private void DrawIndirectCore(IRHIBuffer buffer, ulong offset, uint drawCount, uint stride, bool indexed)
    {
        if (buffer is not D3D11Buffer d3dBuffer)
            throw new ArgumentException("Indirect draw requires a D3D11 buffer.", nameof(buffer));
        if (drawCount == 0) return;
        if (_context == IntPtr.Zero || d3dBuffer.NativePtr == IntPtr.Zero)
            throw new InvalidOperationException("Cannot draw indirectly without a valid D3D11 context and buffer.");

        uint argumentSize = indexed ? 20u : 16u;
        uint effectiveStride = stride == 0 && drawCount == 1 ? argumentSize : stride;
        if (effectiveStride < argumentSize)
            throw new ArgumentOutOfRangeException(nameof(stride), $"Indirect argument stride must be at least {argumentSize} bytes.");
        ulong lastOffset = checked(offset + (ulong)(drawCount - 1) * effectiveStride);
        if (lastOffset + argumentSize > d3dBuffer.Size || lastOffset > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(offset), "Indirect draw arguments exceed the D3D11 buffer range.");

        unsafe
        {
            IntPtr vtable = *(IntPtr*)_context;
            int slot = indexed ? 39 : 40;
            IntPtr function = *((IntPtr*)vtable + slot);
            var draw = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)function;
            for (uint i = 0; i < drawCount; i++)
                draw(_context, d3dBuffer.NativePtr, checked((uint)(offset + (ulong)i * effectiveStride)));
        }
    }

    // ── Compute ──────────────────────────────────────────────────────────

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        throw new NotSupportedException("D3D11 compute dispatch is not implemented by this backend.");
    }

    public void DispatchIndirect(IRHIBuffer buffer, ulong offset)
    {
        throw new NotSupportedException("D3D11 indirect compute dispatch is not implemented by this backend.");
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
        if (_context == IntPtr.Zero)
        {
            return;
        }
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
        if (_context == IntPtr.Zero)
        {
            return;
        }
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
        if (_context == IntPtr.Zero)
        {
            return;
        }
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
        if (_context == IntPtr.Zero)
        {
            return;
        }
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
        if (_context == IntPtr.Zero)
        {
            return;
        }
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
        if (_context == IntPtr.Zero)
        {
            return;
        }
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
        if (_context == IntPtr.Zero)
        {
            return;
        }
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
    }
}
