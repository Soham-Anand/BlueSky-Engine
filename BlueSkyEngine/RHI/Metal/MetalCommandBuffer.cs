using System.Numerics;
using System.Runtime.InteropServices;
using static NotBSRenderer.Metal.MetalInterop;

namespace NotBSRenderer.Metal;

internal class MetalCommandBuffer : IRHICommandBuffer
{
    private readonly MetalDevice _device;
    private IntPtr _commandBuffer;
    private IntPtr _renderEncoder;
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
    
    public void BeginRenderPass(IRHITexture renderTarget, ClearValue clearValue)
    {
        BeginRenderPass([renderTarget], null, clearValue);
    }
    
    public void BeginRenderPass(IRHITexture[] colorTargets, IRHITexture? depthTarget, ClearValue clearValue)
    {
        if (_renderEncoder != IntPtr.Zero)
            throw new InvalidOperationException("Render pass already active");
        
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
        
        if (_commandBuffer != IntPtr.Zero)
        {
            Release(_commandBuffer);
            _commandBuffer = IntPtr.Zero;
        }
        
        _disposed = true;
    }
    
    // Native interop helpers
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
    /// setClearColor: expects MTLClearColor by value.  On ARM64 the four doubles
    /// are passed in d0-d3 as a Homogeneous Floating-point Aggregate (HFA).
    /// The previous pointer-based approach silently corrupted the call.
    /// </summary>
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
}
