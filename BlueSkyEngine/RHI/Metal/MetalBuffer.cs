using static NotBSRenderer.Metal.MetalInterop;

namespace NotBSRenderer.Metal;

internal class MetalBuffer : IRHIBuffer
{
    private IntPtr _buffer;
    private bool _disposed;
    
    public ulong Size { get; }
    public BufferUsage Usage { get; }
    public MemoryType MemoryType { get; }
    
    internal IntPtr Handle => _buffer;
    
    public MetalBuffer(MetalDevice device, BufferDesc desc)
    {
        Size = desc.Size;
        Usage = desc.Usage;
        MemoryType = desc.MemoryType;
        
        // Determine Metal resource options based on memory type
        ulong resourceOptions = MemoryType switch
        {
            MemoryType.GpuOnly => MTLResourceStorageModePrivate,
            MemoryType.CpuToGpu => MTLResourceStorageModeShared | MTLResourceCPUCacheModeWriteCombined,
            MemoryType.GpuToCpu => MTLResourceStorageModeShared,
            _ => MTLResourceStorageModeShared
        };
        
        // Create buffer
        var newBufferSel = GetSelector("newBufferWithLength:options:");
        _buffer = NewBufferWithLengthOptions(device.Device, newBufferSel, desc.Size, resourceOptions);
        
        if (_buffer == IntPtr.Zero)
            throw new Exception($"Failed to create Metal buffer of size {desc.Size}");
        
        // Set debug name if provided
        if (!string.IsNullOrEmpty(desc.DebugName))
        {
            var labelSel = GetSelector("setLabel:");
            var nsString = CreateNSString(desc.DebugName);
            objc_msgSend_void_ptr(_buffer, labelSel, nsString);
            Release(nsString);
        }
    }
    

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr NewBufferWithLengthOptions(IntPtr receiver, IntPtr selector, ulong length, ulong options);
    
    public void Dispose()
    {
        if (_disposed) return;
        
        if (_buffer != IntPtr.Zero)
        {
            Release(_buffer);
            _buffer = IntPtr.Zero;
        }
        
        _disposed = true;
    }
}
