using BlueSky.Platform;
using System.Runtime.InteropServices;
using static NotBSRenderer.Metal.MetalInterop;

namespace NotBSRenderer.Metal;

internal class MetalDevice : IRHIDevice
{
    private IntPtr _device;
    private IntPtr _commandQueue;
    private bool _disposed;
    
    public RHIBackend Backend => RHIBackend.Metal;
    
    [DllImport("/System/Library/Frameworks/Metal.framework/Metal")]
    private static extern IntPtr MTLCopyAllDevices();

    public MetalDevice()
    {
        // MTLCreateSystemDefaultDevice is a C function, not ObjC
        _device = MTLCreateSystemDefaultDevice();
        if (_device == IntPtr.Zero)
        {
            // Fallback: try MTLCopyAllDevices() and pick the first entry
            var devicesArray = MTLCopyAllDevices();
            if (devicesArray != IntPtr.Zero)
            {
                var countSel = GetSelector("count");
                var count = MetalInterop.objc_msgSend_ret_ulong(devicesArray, countSel);
                if (count > 0)
                {
                    var objectAtIndexSel = GetSelector("objectAtIndex:");
                    _device = MetalInterop.objc_msgSend_ptr_ulong(devicesArray, objectAtIndexSel, 0);
                }
            }
        }

        if (_device == IntPtr.Zero)
            throw new PlatformNotSupportedException("Metal is not available on this system (no MTLDevice found)");
        
        Retain(_device);
        
        // Create command queue
        var newCommandQueueSel = GetSelector("newCommandQueue");
        _commandQueue = objc_msgSend(_device, newCommandQueueSel);
        if (_commandQueue == IntPtr.Zero)
            throw new Exception("Failed to create Metal command queue");
        
        var nameSel = GetSelector("name");
        var nsString = objc_msgSend(_device, nameSel);
        var utf8StringSel = GetSelector("UTF8String");
        var cString = objc_msgSend(nsString, utf8StringSel);
        var deviceName = Marshal.PtrToStringUTF8(cString);
    }
    
    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/Metal.framework/Metal")]
    private static extern IntPtr MTLCreateSystemDefaultDevice();
    
    public IRHISwapchain CreateSwapchain(IWindow window, PresentMode presentMode = PresentMode.Vsync)
    {
        return new MetalSwapchain(this, window, presentMode);
    }
    
    public IRHIBuffer CreateBuffer(BufferDesc desc)
    {
        return new MetalBuffer(this, desc);
    }
    
    public IRHITexture CreateTexture(TextureDesc desc)
    {
        return new MetalTexture(this, desc);
    }
    
    public IRHIPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        return new MetalPipeline(this, desc);
    }
    
    public IRHICommandBuffer CreateCommandBuffer()
    {
        return new MetalCommandBuffer(this);
    }
    
    public void Submit(IRHICommandBuffer commandBuffer)
    {
        if (commandBuffer is not MetalCommandBuffer metalCmd)
            throw new ArgumentException("Command buffer must be a Metal command buffer");
        
        metalCmd.Submit();
    }
    
    public void Submit(IRHICommandBuffer commandBuffer, IRHISwapchain swapchain)
    {
        if (commandBuffer is not MetalCommandBuffer metalCmd)
            throw new ArgumentException("Command buffer must be a Metal command buffer");
        
        if (swapchain is not MetalSwapchain metalSwapchain)
            throw new ArgumentException("Swapchain must be a Metal swapchain");
        
        // Set drawable for presentation
        metalCmd.SetDrawable(metalSwapchain.CurrentDrawable);
        metalCmd.Submit();
    }
    
    public void WaitIdle()
    {
        var commandBufferSel = GetSelector("commandBuffer");
        var cmdBuf = objc_msgSend(_commandQueue, commandBufferSel);
        
        var commitSel = GetSelector("commit");
        objc_msgSend(cmdBuf, commitSel);
        
        var waitUntilCompletedSel = GetSelector("waitUntilCompleted");
        objc_msgSend(cmdBuf, waitUntilCompletedSel);
    }
    
    public unsafe void UploadBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0)
    {
        if (buffer is not MetalBuffer metalBuffer)
            throw new ArgumentException("Buffer must be a Metal buffer");
            
        if (metalBuffer.MemoryType == MemoryType.GpuOnly)
        {
            // Use a staging buffer
            var newBufferSel = GetSelector("newBufferWithLength:options:");
            var stagingBuffer = NewBufferWithLengthOptions(_device, newBufferSel, (ulong)data.Length, 0); // 0 = MTLResourceStorageModeShared
            
            var contentsSel = GetSelector("contents");
            var stagingPtr = objc_msgSend(stagingBuffer, contentsSel);
            
            fixed (byte* dataPtr = data)
            {
                Buffer.MemoryCopy(dataPtr, (void*)stagingPtr, data.Length, data.Length);
            }
            
            // Perform blit
            var commandBufferSel = GetSelector("commandBuffer");
            var cmdBuf = objc_msgSend(_commandQueue, commandBufferSel);
            
            var blitEncoderSel = GetSelector("blitCommandEncoder");
            var blitEncoder = objc_msgSend(cmdBuf, blitEncoderSel);
            
            var copySel = GetSelector("copyFromBuffer:sourceOffset:toBuffer:destinationOffset:size:");
            CopyFromBuffer(blitEncoder, copySel, stagingBuffer, 0, metalBuffer.Handle, offset, (ulong)data.Length);
            
            var endEncodingSel = GetSelector("endEncoding");
            objc_msgSend(blitEncoder, endEncodingSel);
            
            var commitSel = GetSelector("commit");
            objc_msgSend(cmdBuf, commitSel);
            
            var waitUntilCompletedSel = GetSelector("waitUntilCompleted");
            objc_msgSend(cmdBuf, waitUntilCompletedSel);
            
            Release(stagingBuffer);
        }
        else
        {
            var contentsSel = GetSelector("contents");
            var bufferPtr = objc_msgSend(metalBuffer.Handle, contentsSel);
            
            if (bufferPtr == IntPtr.Zero)
                throw new Exception("Failed to get buffer contents pointer for CPU-visible buffer");
            
            fixed (byte* dataPtr = data)
            {
                Buffer.MemoryCopy(dataPtr, (byte*)bufferPtr + offset, (long)(metalBuffer.Size - offset), data.Length);
            }
        }
    }

    public void UpdateBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0)
    {
        // For Metal, we assume buffers that need high-frequency updates are created with MemoryType.CpuToGpu
        // which use the MTLResourceStorageModeShared. UploadBuffer already handles this.
        UploadBuffer(buffer, data, offset);
    }
    
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr NewBufferWithLengthOptions(IntPtr receiver, IntPtr selector, ulong length, ulong options);
    
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void CopyFromBuffer(IntPtr receiver, IntPtr selector, IntPtr sourceBuffer, ulong sourceOffset, IntPtr destinationBuffer, ulong destinationOffset, ulong size);
    
    public unsafe void UploadTexture(IRHITexture texture, ReadOnlySpan<byte> data, uint mipLevel = 0)
    {
        if (texture is not MetalTexture metalTexture)
            throw new ArgumentException("Texture must be a Metal texture");
            
        var replaceRegionSel = GetSelector("replaceRegion:mipmapLevel:withBytes:bytesPerRow:");
        MTLRegion region = new MTLRegion
        {
            origin = new MTLOrigin { x = 0, y = 0, z = 0 },
            size = new MTLSize { width = metalTexture.Width, height = metalTexture.Height, depth = 1 }
        };
        
        ulong bytesPerRow = metalTexture.Width;
        if (metalTexture.Format == TextureFormat.RGBA8Unorm || metalTexture.Format == TextureFormat.BGRA8Unorm)
            bytesPerRow *= 4;

        fixed (byte* pData = data)
        {
            ReplaceRegion(metalTexture.Handle, replaceRegionSel, region, (ulong)mipLevel, (IntPtr)pData, bytesPerRow);
        }
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct MTLOrigin { public ulong x, y, z; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MTLSize { public ulong width, height, depth; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MTLRegion { public MTLOrigin origin; public MTLSize size; }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ReplaceRegion(IntPtr receiver, IntPtr selector, MTLRegion region, ulong level, IntPtr bytes, ulong bytesPerRow);
    
    internal IntPtr Device => _device;
    internal IntPtr CommandQueue => _commandQueue;
    
    // Phase 2/3 additions - stub implementations
    public RHICapabilities Capabilities => RHICapabilities.ComputeShaders | RHICapabilities.BindlessResources | 
                                           RHICapabilities.IndirectDrawing | RHICapabilities.AsyncCompute;
    
    public DescriptorBindingMode BindingMode => DescriptorBindingMode.Bindless;
    
    public IRHIPipeline CreateComputePipeline(ComputePipelineDesc desc)
    {
        // TODO: Implement compute pipeline creation
        throw new NotImplementedException("Compute pipelines not yet implemented for Metal");
    }
    
    public BindlessResourceHandle RegisterBindlessTexture(IRHITexture texture)
    {
        // TODO: Implement bindless texture registration (Metal 3.0+)
        return new BindlessResourceHandle { Index = 0, Generation = 0 };
    }
    
    public BindlessResourceHandle RegisterBindlessBuffer(IRHIBuffer buffer)
    {
        // TODO: Implement bindless buffer registration (Metal 3.0+)
        return new BindlessResourceHandle { Index = 0, Generation = 0 };
    }
    
    public void UnregisterBindlessResource(BindlessResourceHandle handle)
    {
        // TODO: Implement bindless resource unregistration
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        if (_commandQueue != IntPtr.Zero)
        {
            Release(_commandQueue);
            _commandQueue = IntPtr.Zero;
        }
        
        if (_device != IntPtr.Zero)
        {
            Release(_device);
            _device = IntPtr.Zero;
        }
        
        _disposed = true;
    }
}
