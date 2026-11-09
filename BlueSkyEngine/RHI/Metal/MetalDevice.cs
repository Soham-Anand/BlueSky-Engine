using BlueSky.Aegis;
using BlueSky.Aegis.Detection;
using System.Runtime.InteropServices;
using static BlueSky.BSR.RHI.Metal.MetalInterop;

namespace BlueSky.BSR.RHI.Metal;

internal class MetalDevice : IRHIDevice
{
    private IntPtr _device;
    private IntPtr _commandQueue;
    private readonly IntelligentQueryLayer _queryLayer;
    private bool _disposed;

    public RHIBackend Backend => RHIBackend.Metal;

    [DllImport("/System/Library/Frameworks/Metal.framework/Metal")]
    private static extern IntPtr MTLCopyAllDevices();

    public MetalDevice(IntelligentQueryLayer queryLayer)
    {
        _queryLayer = queryLayer;

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

        Console.WriteLine($"[MetalDevice] Initialized: {deviceName}");
        Console.WriteLine($"[MetalDevice] Compute: {_queryLayer.SupportsCompute}, Indirect: {_queryLayer.SupportsIndirectDrawing}");
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

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IRHISampler> _samplerCache = new();

    public IRHISampler CreateSampler(SamplerDesc desc)
    {
        string key = $"{desc.Filter}|{desc.AddressU}|{desc.AddressV}|{desc.AddressW}|{desc.MaxAnisotropy}|{desc.MinLod}|{desc.MaxLod}|{desc.CompareLess}";
        return _samplerCache.GetOrAdd(key, _ => new MetalSampler(this, desc));
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

    // ── M5: mips + GPU clocks ────────────────────────────────────────────
    /// <summary>
    /// M5: generate the full mip chain (level 0 must already be uploaded).
    /// Blocking; call during load. Requires MipLevels &gt; 1, color format.
    /// </summary>
    public void GenerateMipmaps(IRHITexture texture)
    {
        if (texture is not MetalTexture metalTexture)
            throw new ArgumentException("Texture must be a Metal texture");
        if (metalTexture.MipLevels <= 1)
            throw new ArgumentException("Texture was created with MipLevels = 1.");
        if (metalTexture.Format == TextureFormat.Depth32Float ||
            metalTexture.Format == TextureFormat.Depth24Stencil8)
            throw new ArgumentException("Cannot generate mipmaps for depth textures.");

        var cmdBuf = objc_msgSend(_commandQueue, GetSelector("commandBuffer"));
        if (cmdBuf == IntPtr.Zero)
            throw new Exception("Failed to create Metal command buffer for mip generation.");
        Retain(cmdBuf);
        try
        {
            var blit = objc_msgSend(cmdBuf, GetSelector("blitCommandEncoder"));
            if (blit == IntPtr.Zero)
                throw new Exception("Failed to create Metal blit encoder for mip generation.");
            Retain(blit);
            try
            {
                objc_msgSend_void_ptr(blit, GetSelector("generateMipmapsForTexture:"), metalTexture.Handle);
            }
            finally
            {
                objc_msgSend_void(blit, GetSelector("endEncoding"));
                Release(blit);
            }
            objc_msgSend_void(cmdBuf, GetSelector("commit"));
            objc_msgSend_void(cmdBuf, GetSelector("waitUntilCompleted"));
        }
        finally
        {
            Release(cmdBuf);
        }
    }

    /// <summary>M5: GPU milliseconds measured on the most recently drained
    /// command buffer (GPUStartTime/GPUEndTime). -1 when unknown.</summary>
    public double LastFrameGpuMs { get; private set; } = -1;

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern double GetDoubleProperty(IntPtr receiver, IntPtr selector);

    private void SampleGpuTime(IntPtr commandBuffer)
    {
        try
        {
            double start = GetDoubleProperty(commandBuffer, GetSelector("GPUStartTime"));
            double end = GetDoubleProperty(commandBuffer, GetSelector("GPUEndTime"));
            if (start > 0 && end >= start)
                LastFrameGpuMs = (end - start) * 1000.0;
        }
        catch { }
    }
    // Up to FramesInFlight command buffers may be committed but uncompleted.
    // Dynamic per-frame buffers MUST be versioned by FrameIndex (the UI
    // renderer rings its buffers; the blocking scene pass stays single).
    // ── M4: async frame overlap ──────────────────────────────────────────
    // Up to FramesInFlight command buffers may be committed but uncompleted.
    // Dynamic per-frame buffers MUST be versioned by FrameIndex (the UI
    // renderer rings its buffers; the blocking scene pass stays single).
    public const int FramesInFlight = 3;
    public int FrameIndex { get; private set; }
    private readonly List<IntPtr> _pendingBuffers = new();
    private readonly object _pendingLock = new();
    private const ulong MTLCommandBufferStatusCompleted = 4;
    private const ulong MTLCommandBufferStatusError = 5;

    /// <summary>
    /// M4: call once per frame before AcquireNextImage. Blocks only when the
    /// GPU is more than FramesInFlight behind, then advances FrameIndex.
    /// </summary>
    public int BeginFrame()
    {
        lock (_pendingLock)
        {
            while (_pendingBuffers.Count >= FramesInFlight)
            {
                var oldest = _pendingBuffers[0];
                ulong status = GetBufferStatus(oldest);
                if (status != MTLCommandBufferStatusCompleted &&
                    status != MTLCommandBufferStatusError)
                {
                    // GPU genuinely behind — block on the oldest only.
                    WaitBufferUntilCompleted(oldest);
                }
                SampleGpuTime(oldest);
                _pendingBuffers.RemoveAt(0);
                Release(oldest);
            }
        }
        FrameIndex = (FrameIndex + 1) % FramesInFlight;
        return FrameIndex;
    }

    /// <summary>M4: commit without CPU wait. Enqueues for throttling.</summary>
    public void SubmitNoWait(IRHICommandBuffer commandBuffer)
    {
        if (commandBuffer is not MetalCommandBuffer metalCmd)
            throw new ArgumentException("Command buffer must be a Metal command buffer");
        metalCmd.CommitNoWait();
        lock (_pendingLock)
        {
            Retain(metalCmd.NativeHandle);
            _pendingBuffers.Add(metalCmd.NativeHandle);
        }
    }

    /// <summary>M4: commit + present without CPU wait.</summary>
    public void SubmitNoWait(IRHICommandBuffer commandBuffer, IRHISwapchain swapchain)
    {
        if (commandBuffer is not MetalCommandBuffer metalCmd)
            throw new ArgumentException("Command buffer must be a Metal command buffer");
        if (swapchain is not MetalSwapchain metalSwapchain)
            throw new ArgumentException("Swapchain must be a Metal swapchain");
        metalCmd.SetDrawable(metalSwapchain.CurrentDrawable);
        SubmitNoWait(commandBuffer);
    }

    /// <summary>M4: block until all in-flight GPU work completes.</summary>
    public void DrainPending()
    {
        IntPtr[] pending;
        lock (_pendingLock)
        {
            pending = _pendingBuffers.ToArray();
            _pendingBuffers.Clear();
        }
        foreach (var handle in pending)
        {
            WaitBufferUntilCompleted(handle);
            SampleGpuTime(handle);
            Release(handle);
        }
    }

    // ── M4b: cross-command-buffer scene → composite dependency ─────────────
    // The scene pass renders offscreen (FinalTarget) on its own command buffer;
    // the UI pass samples it on another. A shared event orders them without
    // any CPU wait: scene signals value N, composite waits for value N.
    private IntPtr _sceneEvent = IntPtr.Zero;
    private ulong _sceneSignal;
    public ulong LastSceneSignal
    {
        get { lock (_pendingLock) { return _sceneSignal; } }
    }

    private IntPtr SceneEvent
    {
        get
        {
            if (_sceneEvent == IntPtr.Zero)
            {
                _sceneEvent = objc_msgSend(_device, GetSelector("newSharedEvent"));
                if (_sceneEvent == IntPtr.Zero)
                    throw new Exception("Failed to create Metal shared event");
                Retain(_sceneEvent);
            }
            return _sceneEvent;
        }
    }

    /// <summary>M4b: signal scene completion, then commit without CPU wait.
    /// Does NOT push into _pendingBuffers because the frame's composite command
    /// buffer waits on SceneEvent and tracks the full frame lifecycle (1 slot per frame).</summary>
    public ulong SubmitSceneNoWait(IRHICommandBuffer commandBuffer)
    {
        if (commandBuffer is not MetalCommandBuffer metalCmd)
            throw new ArgumentException("Command buffer must be a Metal command buffer");
        ulong value;
        lock (_pendingLock) { _sceneSignal++; value = _sceneSignal; }
        metalCmd.EncodeSignalEvent(SceneEvent, value);
        metalCmd.CommitNoWait();
        return value;
    }

    /// <summary>M4b: order this command buffer after the latest scene signal.</summary>
    public void EncodeWaitForScene(IRHICommandBuffer commandBuffer)
    {
        if (commandBuffer is not MetalCommandBuffer metalCmd)
            throw new ArgumentException("Command buffer must be a Metal command buffer");
        ulong value = LastSceneSignal;
        if (value == 0) return; // no scene submitted yet — nothing to wait for
        metalCmd.EncodeWaitEvent(SceneEvent, value);
    }

    private static ulong GetBufferStatus(IntPtr commandBuffer)
    {
        var statusSel = GetSelector("status");
        return objc_msgSend_ret_ulong(commandBuffer, statusSel);
    }

    private static void WaitBufferUntilCompleted(IntPtr commandBuffer)
    {
        var waitSel = GetSelector("waitUntilCompleted");
        objc_msgSend_void(commandBuffer, waitSel);
    }

    public void WaitIdle()
    {
        DrainPending();
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
        uint levelW = Math.Max(1u, metalTexture.Width >> (int)Math.Min(mipLevel, 31));
        uint levelH = Math.Max(1u, metalTexture.Height >> (int)Math.Min(mipLevel, 31));
        MTLRegion region = new MTLRegion
        {
            origin = new MTLOrigin { x = 0, y = 0, z = 0 },
            size = new MTLSize { width = levelW, height = levelH, depth = 1 }
        };

        ulong bytesPerRow = levelW;
        if (metalTexture.Format == TextureFormat.RGBA8Unorm || metalTexture.Format == TextureFormat.BGRA8Unorm ||
            metalTexture.Format == TextureFormat.RGBA8Srgb || metalTexture.Format == TextureFormat.BGRA8Srgb)
            bytesPerRow *= 4;

        if (levelH > 0 && data.Length > 0)
        {
            ulong actualRowBytes = (ulong)data.Length / levelH;
            if (actualRowBytes >= bytesPerRow)
                bytesPerRow = actualRowBytes;
        }

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
    // ── Capabilities derived from IntelligentQueryLayer ──
    public RHICapabilities Capabilities
    {
        get
        {
            var caps = RHICapabilities.AsyncCompute;
            if (_queryLayer.SupportsCompute) caps |= RHICapabilities.ComputeShaders;
            if (_queryLayer.SupportsIndirectDrawing) caps |= RHICapabilities.IndirectDrawing | RHICapabilities.MultiDrawIndirect;
            if (_queryLayer.SupportsRayTracing) caps |= RHICapabilities.RayTracing;
            if (_queryLayer.SupportsMeshShaders) caps |= RHICapabilities.MeshShaders;
            return caps;
        }
    }

    // ── Compute Pipeline (Metal 1.0+ — all Intel Macs) ──
    public IRHIPipeline CreateComputePipeline(ComputePipelineDesc desc)
    {
        if (!_queryLayer.SupportsCompute)
            throw new PlatformNotSupportedException(
                $"Compute shaders not supported on this GPU (Metal family: {_queryLayer.MetalFamily.DisplayName()})");

        return new MetalComputePipeline(this, desc);
    }

    public void Dispose()
    {
        if (_disposed) return;

        try { DrainPending(); } catch { }
        if (_sceneEvent != IntPtr.Zero)
        {
            Release(_sceneEvent);
            _sceneEvent = IntPtr.Zero;
        }

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
