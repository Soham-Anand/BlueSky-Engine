using System.Numerics;
using System.Runtime.InteropServices;
using static BlueSky.Rendering.RHI.Metal.MetalInterop;

namespace BlueSky.Rendering.RHI.Metal;

internal class MetalCommandBuffer : IRHICommandBuffer
{
    private readonly MetalDevice _device;
    private IntPtr _commandBuffer;
    private IntPtr _renderEncoder;
    private IntPtr _computeEncoder;
    private IntPtr _currentDrawable;
    private bool _disposed;
    private IntPtr _indexBuffer;
    private ulong _indexBufferOffset;
    private ulong _indexType;
    private ulong _currentTopology = MTLPrimitiveTypeTriangle;

    public MetalCommandBuffer(MetalDevice device)
    {
        _device = device;

        // Create command buffer from queue
        var commandBufferSel = GetSelector("commandBuffer");
        _commandBuffer = objc_msgSend(_device.CommandQueue, commandBufferSel);
        if (_commandBuffer == IntPtr.Zero)
            throw new Exception("Failed to create Metal command buffer");

        Retain(_commandBuffer);
    }

    // ── Compute Encoder Management ──

    /// <summary>
    /// Lazily create the compute command encoder. Metal 1.0+ (all Intel Macs).
    /// If a render encoder is active, it must be ended first.
    /// </summary>
    private IntPtr GetOrCreateComputeEncoder()
    {
        if (_computeEncoder != IntPtr.Zero)
            return _computeEncoder;

        // End render encoder if active — Metal only allows one encoder at a time
        if (_renderEncoder != IntPtr.Zero)
        {
            var endEncodingSel = GetSelector("endEncoding");
            objc_msgSend_void(_renderEncoder, endEncodingSel);
            Release(_renderEncoder);
            _renderEncoder = IntPtr.Zero;
        }

        var computeEncoderSel = GetSelector("computeCommandEncoder");
        _computeEncoder = objc_msgSend(_commandBuffer, computeEncoderSel);
        if (_computeEncoder == IntPtr.Zero)
            throw new Exception("Failed to create Metal compute command encoder");

        Retain(_computeEncoder);
        return _computeEncoder;
    }

    /// <summary>
    /// End compute encoder if active. Called before starting a render pass.
    /// </summary>
    private void EndComputeEncoderIfNeeded()
    {
        if (_computeEncoder == IntPtr.Zero) return;

        var endEncodingSel = GetSelector("endEncoding");
        objc_msgSend_void(_computeEncoder, endEncodingSel);
        Release(_computeEncoder);
        _computeEncoder = IntPtr.Zero;
    }

    // ── Render Pass ──

    public void BeginRenderPass(IRHITexture renderTarget, ClearValue clearValue)
    {
        BeginRenderPass([renderTarget], null, clearValue);
    }

    public void BeginRenderPass(IRHITexture[] colorTargets, IRHITexture? depthTarget, ClearValue clearValue)
    {
        if (_renderEncoder != IntPtr.Zero)
            throw new InvalidOperationException("Render pass already active");

        // End compute encoder if active — Metal only allows one encoder at a time
        EndComputeEncoderIfNeeded();

        // Create render pass descriptor
        var descriptorClass = GetClass("MTLRenderPassDescriptor");
        var allocSel = GetSelector("alloc");
        var initSel = GetSelector("init");
        var descriptor = objc_msgSend(objc_msgSend(descriptorClass, allocSel), initSel);

        // Set up color attachments
        var colorAttachmentsSel = GetSelector("colorAttachments");
        var colorAttachments = objc_msgSend(descriptor, colorAttachmentsSel);

        for (int i = 0; i < colorTargets.Length; i++)
        {
            if (colorTargets[i] is not MetalTexture metalTexture)
                throw new ArgumentException("Color target must be a Metal texture");

            var objectAtIndexSel = GetSelector("objectAtIndexedSubscript:");
            var attachment = objc_msgSend_ulong(colorAttachments, objectAtIndexSel, (ulong)i);

            var setTextureSel = GetSelector("setTexture:");
            objc_msgSend_void_ptr(attachment, setTextureSel, metalTexture.Handle);

            var setLoadActionSel = GetSelector("setLoadAction:");
            var loadAction = clearValue.LoadInsteadOfClear ? MTLLoadActionLoad : MTLLoadActionClear;
            objc_msgSend_void_ulong(attachment, setLoadActionSel, loadAction);

            var setStoreActionSel = GetSelector("setStoreAction:");
            objc_msgSend_void_ulong(attachment, setStoreActionSel, MTLStoreActionStore);

            // Set clear color
            var setClearColorSel = GetSelector("setClearColor:");
            SetClearColor(attachment, setClearColorSel, clearValue.Color);
        }

        // Set up depth attachment if provided
        if (depthTarget != null)
        {
            if (depthTarget is not MetalTexture metalDepth)
                throw new ArgumentException("Depth target must be a Metal texture");

            var depthAttachmentSel = GetSelector("depthAttachment");
            var depthAttachment = objc_msgSend(descriptor, depthAttachmentSel);

            var setTextureSel = GetSelector("setTexture:");
            objc_msgSend_void_ptr(depthAttachment, setTextureSel, metalDepth.Handle);

            var setLoadActionSel = GetSelector("setLoadAction:");
            var loadAction = clearValue.LoadInsteadOfClear ? MTLLoadActionLoad : MTLLoadActionClear;
            objc_msgSend_void_ulong(depthAttachment, setLoadActionSel, loadAction);

            var setStoreActionSel = GetSelector("setStoreAction:");
            objc_msgSend_void_ulong(depthAttachment, setStoreActionSel, MTLStoreActionStore);

            var setClearDepthSel = GetSelector("setClearDepth:");
            SetClearDepth(depthAttachment, setClearDepthSel, clearValue.Depth);
        }

        // Create render command encoder
        var renderCommandEncoderSel = GetSelector("renderCommandEncoderWithDescriptor:");
        _renderEncoder = objc_msgSend_ptr(_commandBuffer, renderCommandEncoderSel, descriptor);
        if (_renderEncoder == IntPtr.Zero)
            throw new Exception("Failed to create render command encoder");

        Retain(_renderEncoder);
        Release(descriptor);
    }

    public void EndRenderPass()
    {
        if (_renderEncoder == IntPtr.Zero)
            throw new InvalidOperationException("No active render pass");

        var endEncodingSel = GetSelector("endEncoding");
        objc_msgSend_void(_renderEncoder, endEncodingSel);

        Release(_renderEncoder);
        _renderEncoder = IntPtr.Zero;
    }

    // ── Pipeline Binding ──

    public void SetPipeline(IRHIPipeline pipeline)
    {
        if (_renderEncoder == IntPtr.Zero)
            throw new InvalidOperationException("No active render pass");

        if (pipeline is not MetalPipeline metalPipeline)
            throw new ArgumentException("Pipeline must be a Metal pipeline");

        var setRenderPipelineSel = GetSelector("setRenderPipelineState:");
        objc_msgSend_void_ptr(_renderEncoder, setRenderPipelineSel, metalPipeline.Handle);

        var setCullModeSel = GetSelector("setCullMode:");
        objc_msgSend_void_ulong(_renderEncoder, setCullModeSel, metalPipeline.RasterizerCullMode);

        var setFillModeSel = GetSelector("setTriangleFillMode:");
        objc_msgSend_void_ulong(_renderEncoder, setFillModeSel, metalPipeline.FillMode);

        if (metalPipeline.DepthStencilState != IntPtr.Zero)
        {
            var setDepthStencilStateSel = GetSelector("setDepthStencilState:");
            objc_msgSend_void_ptr(_renderEncoder, setDepthStencilStateSel, metalPipeline.DepthStencilState);
        }

        _currentTopology = metalPipeline.PrimitiveType;
    }

    public void SetViewport(Viewport viewport)
    {
        if (_renderEncoder == IntPtr.Zero)
            throw new InvalidOperationException("No active render pass");

        var setViewportSel = GetSelector("setViewport:");
        SetViewportNative(_renderEncoder, setViewportSel, viewport);
    }

    public void SetScissor(Scissor scissor)
    {
        if (_renderEncoder == IntPtr.Zero)
            throw new InvalidOperationException("No active render pass");

        var setScissorSel = GetSelector("setScissorRect:");
        SetScissorNative(_renderEncoder, setScissorSel, scissor);
    }

    // ── Resource Binding (Slot-Based — works on all Metal GPUs) ──

    public void SetVertexBuffer(IRHIBuffer buffer, uint binding = 0, ulong offset = 0)
    {
        if (_renderEncoder == IntPtr.Zero)
            throw new InvalidOperationException("No active render pass");

        if (buffer is not MetalBuffer metalBuffer)
            throw new ArgumentException("Buffer must be a Metal buffer");

        var setVertexBufferSel = GetSelector("setVertexBuffer:offset:atIndex:");
        SetVertexBufferNative(_renderEncoder, setVertexBufferSel, metalBuffer.Handle, offset, binding);
    }

    public void SetIndexBuffer(IRHIBuffer buffer, IndexType indexType, ulong offset = 0)
    {
        if (buffer is not MetalBuffer metalBuffer)
            throw new ArgumentException("Buffer must be a Metal buffer");

        _indexBuffer = metalBuffer.Handle;
        _indexBufferOffset = offset;
        _indexType = ToMTLIndexType(indexType);
    }

    public void SetUniformBuffer(IRHIBuffer buffer, uint binding, uint set = 0)
    {
        if (_renderEncoder == IntPtr.Zero)
            throw new InvalidOperationException("No active render pass");

        if (buffer is not MetalBuffer metalBuffer)
            throw new ArgumentException("Buffer must be a Metal buffer");

        // In Metal, uniform buffers bind as vertex and fragment buffer slots.
        var setVertexBufferSel   = GetSelector("setVertexBuffer:offset:atIndex:");
        SetVertexBufferNative(_renderEncoder, setVertexBufferSel, metalBuffer.Handle, 0, binding);

        var setFragmentBufferSel = GetSelector("setFragmentBuffer:offset:atIndex:");
        SetFragmentBufferNative(_renderEncoder, setFragmentBufferSel, metalBuffer.Handle, 0, binding);
    }

    public void SetTexture(IRHITexture texture, uint binding, uint set = 0)
    {
        if (_renderEncoder == IntPtr.Zero)
            throw new InvalidOperationException("No active render pass");

        if (texture is not MetalTexture metalTexture)
            throw new ArgumentException("Texture must be a Metal texture");

        var setTextureSel = GetSelector("setFragmentTexture:atIndex:");
        objc_msgSend_void_ptr_ulong(_renderEncoder, setTextureSel, metalTexture.Handle, binding);
    }

    // ── Storage Buffers/Textures (compute shader resource binding) ──
    // On Metal, storage buffers bind via setBuffer:offset:atIndex: on compute encoder
    // On render encoder, we fall back to uniform binding (Metal 1.0 doesn't distinguish)

    public void SetStorageBuffer(IRHIBuffer buffer, uint binding, uint set = 0)
    {
        if (buffer is not MetalBuffer metalBuffer)
            throw new ArgumentException("Buffer must be a Metal buffer");

        // If compute encoder is active, bind there
        if (_computeEncoder != IntPtr.Zero)
        {
            var setBufferSel = GetSelector("setBuffer:offset:atIndex:");
            SetBufferNative(_computeEncoder, setBufferSel, metalBuffer.Handle, 0, binding);
            return;
        }

        // If render encoder is active, bind as vertex+fragment (Metal 1.0 doesn't distinguish)
        if (_renderEncoder != IntPtr.Zero)
        {
            SetUniformBuffer(buffer, binding, set);
            return;
        }

        throw new InvalidOperationException("No active encoder — call BeginRenderPass or Dispatch first");
    }

    public void SetStorageTexture(IRHITexture texture, uint binding, uint set = 0)
    {
        if (texture is not MetalTexture metalTexture)
            throw new ArgumentException("Texture must be a Metal texture");

        // If compute encoder is active, bind there
        if (_computeEncoder != IntPtr.Zero)
        {
            var setTextureSel = GetSelector("setTexture:atIndex:");
            objc_msgSend_void_ptr_ulong(_computeEncoder, setTextureSel, metalTexture.Handle, binding);
            return;
        }

        // If render encoder is active, bind as fragment texture
        if (_renderEncoder != IntPtr.Zero)
        {
            SetTexture(texture, binding, set);
            return;
        }

        throw new InvalidOperationException("No active encoder — call BeginRenderPass or Dispatch first");
    }

    // ── Uniforms ──

    public unsafe void SetVertexUniforms(uint binding, ReadOnlySpan<byte> data)
    {
        if (_renderEncoder == IntPtr.Zero) return;
        var setBytesSel = GetSelector("setVertexBytes:length:atIndex:");
        fixed (byte* pData = data)
        {
            SetBytesNative(_renderEncoder, setBytesSel, (IntPtr)pData, (ulong)data.Length, (ulong)binding);
        }
    }

    public unsafe void SetFragmentUniforms(uint binding, ReadOnlySpan<byte> data)
    {
        if (_renderEncoder == IntPtr.Zero) return;
        var setBytesSel = GetSelector("setFragmentBytes:length:atIndex:");
        fixed (byte* pData = data)
        {
            SetBytesNative(_renderEncoder, setBytesSel, (IntPtr)pData, (ulong)data.Length, (ulong)binding);
        }
    }

    public void SetVertexUniforms(uint binding, ref System.Numerics.Matrix4x4 matrix)
    {
        System.Span<System.Numerics.Matrix4x4> span = stackalloc System.Numerics.Matrix4x4[1];
        span[0] = matrix;
        SetVertexUniforms(binding, System.Runtime.InteropServices.MemoryMarshal.AsBytes(span));
    }

    /// <summary>
    /// Compute shader uniforms — Metal 1.0+ (all Intel Macs).
    /// Uses setBytes:length:atIndex: on the compute encoder.
    /// </summary>
    public unsafe void SetComputeUniforms(uint binding, ReadOnlySpan<byte> data)
    {
        var encoder = GetOrCreateComputeEncoder();
        var setBytesSel = GetSelector("setBytes:length:atIndex:");
        fixed (byte* pData = data)
        {
            SetBytesNative(encoder, setBytesSel, (IntPtr)pData, (ulong)data.Length, (ulong)binding);
        }
    }

    // ── Draw Commands ──

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        if (_renderEncoder == IntPtr.Zero)
            throw new InvalidOperationException("No active render pass");

        var drawPrimitivesSel = GetSelector("drawPrimitives:vertexStart:vertexCount:instanceCount:baseInstance:");
        DrawPrimitivesNative(_renderEncoder, drawPrimitivesSel, _currentTopology,
            firstVertex, vertexCount, instanceCount, firstInstance);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0,
        int vertexOffset = 0, uint firstInstance = 0)
    {
        if (_renderEncoder == IntPtr.Zero)
            throw new InvalidOperationException("No active render pass");
        if (_indexBuffer == IntPtr.Zero)
            throw new InvalidOperationException("No index buffer set — call SetIndexBuffer before DrawIndexed.");

        ulong indexSize  = _indexType == MTLIndexTypeUInt16 ? 2ul : 4ul;
        ulong byteOffset = _indexBufferOffset + (firstIndex * indexSize);

        var sel = GetSelector("drawIndexedPrimitives:indexCount:indexType:indexBuffer:indexBufferOffset:instanceCount:baseVertex:baseInstance:");
        DrawIndexedPrimitivesNative(_renderEncoder, sel, _currentTopology,
            indexCount, _indexType, _indexBuffer, byteOffset, instanceCount, vertexOffset, firstInstance);
    }

    /// <summary>
    /// Indirect draw — Metal 1.0+ (all Intel Macs).
    /// drawPrimitives:indirectBuffer:indirectBufferOffset:
    /// The buffer must contain IndirectDrawCommand structs.
    /// </summary>
    public void DrawIndirect(IRHIBuffer buffer, ulong offset, uint drawCount, uint stride)
    {
        if (_renderEncoder == IntPtr.Zero)
            throw new InvalidOperationException("No active render pass");

        if (buffer is not MetalBuffer metalBuffer)
            throw new ArgumentException("Buffer must be a Metal buffer");

        var drawPrimitivesIndirectSel = GetSelector("drawPrimitives:indirectBuffer:indirectBufferOffset:");
        DrawPrimitivesIndirectNative(_renderEncoder, drawPrimitivesIndirectSel, _currentTopology,
            metalBuffer.Handle, offset);
    }

    /// <summary>
    /// Indexed indirect draw — Metal 1.0+ (all Intel Macs).
    /// drawIndexedPrimitives:indirectBuffer:indirectBufferOffset:
    /// The buffer must contain IndirectDrawIndexedCommand structs.
    /// </summary>
    public void DrawIndexedIndirect(IRHIBuffer buffer, ulong offset, uint drawCount, uint stride)
    {
        if (_renderEncoder == IntPtr.Zero)
            throw new InvalidOperationException("No active render pass");

        if (buffer is not MetalBuffer metalBuffer)
            throw new ArgumentException("Buffer must be a Metal buffer");

        var drawIndexedPrimitivesIndirectSel = GetSelector("drawIndexedPrimitives:indirectBuffer:indirectBufferOffset:");
        DrawIndexedPrimitivesIndirectNative(_renderEncoder, drawIndexedPrimitivesIndirectSel, _currentTopology,
            _indexType, _indexBuffer, _indexBufferOffset, metalBuffer.Handle, offset);
    }

    // ── Compute Dispatch (Metal 1.0+ — all Intel Macs) ──

    /// <summary>
    /// Dispatch compute work — Metal 1.0+ (Intel HD 4000+).
    /// Creates a compute encoder if needed, binds the pipeline, and dispatches threadgroups.
    /// </summary>
    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        var encoder = GetOrCreateComputeEncoder();

        // Dispatch threadgroups — Metal 1.0+
        // dispatchThreadgroups:threadsPerThreadgroup:
        var dispatchSel = GetSelector("dispatchThreadgroups:threadsPerThreadgroup:");
        DispatchThreadgroupsNative(encoder, dispatchSel, groupCountX, groupCountY, groupCountZ, 1, 1, 1);
    }

    /// <summary>
    /// Indirect compute dispatch — Metal 1.0+ (Intel HD 4000+).
    /// dispatchThreadgroups:indirectBuffer:indirectBufferOffset:
    /// The buffer must contain MTLDispatchThreadgroups struct (3 x uint32_t for group counts).
    /// </summary>
    public void DispatchIndirect(IRHIBuffer buffer, ulong offset)
    {
        if (buffer is not MetalBuffer metalBuffer)
            throw new ArgumentException("Buffer must be a Metal buffer");

        var encoder = GetOrCreateComputeEncoder();

        var dispatchIndirectSel = GetSelector("dispatchThreadgroups:indirectBuffer:indirectBufferOffset:");
        DispatchThreadgroupsIndirectNative(encoder, dispatchIndirectSel, metalBuffer.Handle, offset);
    }

    // ── Memory Barriers (Metal's implicit sync model) ──
    // Metal handles synchronization within a command buffer automatically.
    // These are no-ops — the correct behavior for Metal.

    public void MemoryBarrier()
    {
        // Metal handles memory ordering within a command buffer.
        // No explicit barrier needed — Metal's execution model guarantees this.
    }

    public void BufferBarrier(IRHIBuffer buffer)
    {
        // Metal handles buffer synchronization implicitly within a command buffer.
    }

    public void TextureBarrier(IRHITexture texture)
    {
        // Metal handles texture synchronization implicitly within a command buffer.
    }

    // ── Submit ──

    internal void Submit()
    {
        if (_renderEncoder != IntPtr.Zero)
            throw new InvalidOperationException("Render pass still active — call EndRenderPass first.");

        if (_currentDrawable != IntPtr.Zero)
        {
            var presentDrawableSel = GetSelector("presentDrawable:");
            objc_msgSend_void_ptr(_commandBuffer, presentDrawableSel, _currentDrawable);
        }

        var commitSel = GetSelector("commit");
        objc_msgSend_void(_commandBuffer, commitSel);

        var waitSel = GetSelector("waitUntilCompleted");
        objc_msgSend_void(_commandBuffer, waitSel);
    }

    internal void SetDrawable(IntPtr drawable)
    {
        _currentDrawable = drawable;
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_renderEncoder != IntPtr.Zero)
        {
            Release(_renderEncoder);
            _renderEncoder = IntPtr.Zero;
        }

        if (_computeEncoder != IntPtr.Zero)
        {
            Release(_computeEncoder);
            _computeEncoder = IntPtr.Zero;
        }

        if (_commandBuffer != IntPtr.Zero)
        {
            Release(_commandBuffer);
            _commandBuffer = IntPtr.Zero;
        }

        _disposed = true;
    }

    // ── Native Interop Helpers ──

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MTLClearColor
    {
        public double red, green, blue, alpha;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MTLViewport
    {
        public double originX, originY, width, height, znear, zfar;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MTLScissorRect
    {
        public nuint x, y, width, height;
    }

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetClearColor(IntPtr receiver, IntPtr selector, MTLClearColor color);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetClearDepth(IntPtr receiver, IntPtr selector, double depth);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_ptr_ulong(IntPtr receiver, IntPtr selector, IntPtr arg1, ulong arg2);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetViewportNative(IntPtr receiver, IntPtr selector, MTLViewport viewport);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetScissorNative(IntPtr receiver, IntPtr selector, MTLScissorRect rect);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetVertexBufferNative(IntPtr receiver, IntPtr selector,
        IntPtr buffer, ulong offset, ulong index);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetFragmentBufferNative(IntPtr receiver, IntPtr selector,
        IntPtr buffer, ulong offset, ulong index);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetBufferNative(IntPtr receiver, IntPtr selector,
        IntPtr buffer, ulong offset, ulong index);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetBytesNative(IntPtr receiver, IntPtr selector, IntPtr bytes, ulong length, ulong index);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void DrawPrimitivesNative(IntPtr receiver, IntPtr selector,
        ulong primitiveType, ulong vertexStart, ulong vertexCount, ulong instanceCount, ulong baseInstance);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void DrawIndexedPrimitivesNative(IntPtr receiver, IntPtr selector,
        ulong primitiveType, ulong indexCount, ulong indexType, IntPtr indexBuffer, ulong indexBufferOffset,
        ulong instanceCount, long baseVertex, ulong baseInstance);

    /// <summary>
    /// drawPrimitives:indirectBuffer:indirectBufferOffset: — Metal 1.0+
    /// </summary>
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void DrawPrimitivesIndirectNative(IntPtr receiver, IntPtr selector,
        ulong primitiveType, IntPtr indirectBuffer, ulong indirectBufferOffset);

    /// <summary>
    /// drawIndexedPrimitives:indexType:indexBuffer:indexBufferOffset:indirectBuffer:indirectBufferOffset: — Metal 1.0+
    /// </summary>
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void DrawIndexedPrimitivesIndirectNative(IntPtr receiver, IntPtr selector,
        ulong primitiveType, ulong indexType, IntPtr indexBuffer, ulong indexBufferOffset,
        IntPtr indirectBuffer, ulong indirectBufferOffset);

    /// <summary>
    /// dispatchThreadgroups:threadsPerThreadgroup: — Metal 1.0+
    /// MTLSize passed as 6 x double (3 for threadgroups, 3 for threadsPerThreadgroup).
    /// </summary>
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void DispatchThreadgroupsNative(IntPtr receiver, IntPtr selector,
        ulong threadgroupsX, ulong threadgroupsY, ulong threadgroupsZ,
        ulong threadsPerThreadgroupX, ulong threadsPerThreadgroupY, ulong threadsPerThreadgroupZ);

    /// <summary>
    /// dispatchThreadgroups:indirectBuffer:indirectBufferOffset: — Metal 1.0+
    /// </summary>
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void DispatchThreadgroupsIndirectNative(IntPtr receiver, IntPtr selector,
        IntPtr indirectBuffer, ulong indirectBufferOffset);

    private void SetClearColor(IntPtr attachment, IntPtr selector, Vector4 color)
    {
        var clearColor = new MTLClearColor
        {
            red   = color.X,
            green = color.Y,
            blue  = color.Z,
            alpha = color.W,
        };
        SetClearColorByValue(attachment, selector, clearColor);
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetClearColorByValue(IntPtr receiver, IntPtr selector,
        MTLClearColor color);

    private void SetViewportNative(IntPtr encoder, IntPtr selector, Viewport viewport)
    {
        SetViewportNative(encoder, selector, new MTLViewport { originX = viewport.X, originY = viewport.Y, width = viewport.Width, height = viewport.Height, znear = viewport.MinDepth, zfar = viewport.MaxDepth });
    }

    private void SetScissorNative(IntPtr encoder, IntPtr selector, Scissor scissor)
    {
        SetScissorNative(encoder, selector, new MTLScissorRect { x = (nuint)scissor.X, y = (nuint)scissor.Y, width = (nuint)scissor.Width, height = (nuint)scissor.Height });
    }

    // ── Screenshot capture (Strata benchmark) ──────────────────────────────
    // Encodes drawable → shared staging blit. Caller must invoke
    // CompleteScreenshot AFTER Submit (which blocks until completion).

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MTLOrigin
    {
        public ulong x, y, z;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MTLSize
    {
        public ulong width, height, depth;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MTLRegion
    {
        public MTLOrigin origin;
        public MTLSize size;
    }

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr Texture2DDescriptorNative(IntPtr cls, IntPtr selector,
        ulong pixelFormat, ulong width, ulong height,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)] bool mipmapped);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void CopyFromTextureNative(IntPtr encoder, IntPtr selector,
        IntPtr srcTexture, ulong srcSlice, ulong srcLevel, MTLOrigin srcOrigin, MTLSize srcSize,
        IntPtr dstTexture, ulong dstSlice, ulong dstLevel, MTLOrigin dstOrigin);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void GetBytesNative(IntPtr texture, IntPtr selector, IntPtr pixels,
        ulong bytesPerRow, MTLRegion region, ulong mipmapLevel);

    /// <summary>
    /// Encodes a blit of the swapchain drawable into a CPU-readable staging
    /// texture. Returns IntPtr.Zero on failure. The staging texture is owned
    /// by the caller (released in CompleteScreenshot).
    /// </summary>
    internal IntPtr EncodeScreenshotBlit(MetalSwapchain swapchain, out uint width, out uint height)
    {
        width = 0; height = 0;
        IntPtr src;
        try
        {
            src = ((MetalTexture)swapchain.CurrentRenderTarget).Handle;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Screenshot] No drawable render target: {ex.Message}");
            return IntPtr.Zero;
        }
        if (src == IntPtr.Zero) { Console.WriteLine("[Screenshot] Drawable handle is zero."); return IntPtr.Zero; }
        width = swapchain.Width; height = swapchain.Height;
        if (width == 0 || height == 0) { Console.WriteLine("[Screenshot] Swapchain has zero size."); return IntPtr.Zero; }

        try
        {
            // Shared staging texture (BGRA8, matching the swapchain format).
            var descClass = GetClass("MTLTextureDescriptor");
            var descSel = GetSelector("texture2DDescriptorWithPixelFormat:width:height:mipmapped:");
            IntPtr desc = Texture2DDescriptorNative(descClass, descSel, ToMTLPixelFormat(TextureFormat.BGRA8Unorm), width, height, false);
            if (desc == IntPtr.Zero) { Console.WriteLine("[Screenshot] Descriptor creation failed."); return IntPtr.Zero; }

            IntPtr staging = objc_msgSend_ptr(_device.Device, GetSelector("newTextureWithDescriptor:"), desc);
            if (staging == IntPtr.Zero) { Console.WriteLine("[Screenshot] Staging texture creation failed."); return IntPtr.Zero; }

            // Blit encoder (only one encoder alive at a time).
            EndComputeEncoderIfNeeded();
            if (_renderEncoder != IntPtr.Zero)
            {
                objc_msgSend_void(_renderEncoder, GetSelector("endEncoding"));
                Release(_renderEncoder);
                _renderEncoder = IntPtr.Zero;
            }
            IntPtr blit = objc_msgSend(_commandBuffer, GetSelector("blitCommandEncoder"));
            if (blit == IntPtr.Zero) { Console.WriteLine("[Screenshot] Blit encoder creation failed."); Release(staging); return IntPtr.Zero; }

            var origin = new MTLOrigin { x = 0, y = 0, z = 0 };
            var size = new MTLSize { width = width, height = height, depth = 1 };
            CopyFromTextureNative(blit, GetSelector("copyFromTexture:sourceSlice:sourceLevel:sourceOrigin:sourceSize:toTexture:destinationSlice:destinationLevel:destinationOrigin:"),
                src, 0, 0, origin, size, staging, 0, 0, origin);

            objc_msgSend_void(blit, GetSelector("endEncoding"));
            Release(blit);
            return staging;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Screenshot] Encode failed: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Reads back a staging texture produced by EncodeScreenshotBlit and
    /// writes it as a 32-bit BMP. Must run after Submit (GPU work complete).
    /// </summary>
    internal static void CompleteScreenshot(IntPtr staging, uint width, uint height, string path)
    {
        try
        {
            int rowBytes = (int)width * 4;
            byte[] pixels = new byte[rowBytes * (int)height];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                var region = new MTLRegion
                {
                    origin = new MTLOrigin { x = 0, y = 0, z = 0 },
                    size = new MTLSize { width = width, height = height, depth = 1 }
                };
                GetBytesNative(staging, GetSelector("getBytes:bytesPerRow:fromRegion:mipmapLevel:"),
                    handle.AddrOfPinnedObject(), (ulong)rowBytes, region, 0);
            }
            finally
            {
                handle.Free();
            }
            BlueSky.Rendering.Strata.StrataBenchmark.WriteBmp32(path, pixels, width, height);
            Console.WriteLine($"[Screenshot] Saved {path} ({width}x{height})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Screenshot] Readback failed: {ex.Message}");
        }
        finally
        {
            if (staging != IntPtr.Zero) Release(staging);
        }
    }
}
