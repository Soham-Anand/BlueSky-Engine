using System.Runtime.InteropServices;

namespace NotBSRenderer.DirectX9;

internal class D3D9Buffer : IRHIBuffer
{
    private IntPtr _buffer;
    private bool _disposed;
    private readonly IndexType? _indexType;
    private readonly byte[]? _uniformData;
    
    public ulong Size { get; }
    public BufferUsage Usage { get; }
    public MemoryType MemoryType { get; }
    
    internal IntPtr Handle => _buffer;
    internal IndexType? IndexFormat => _indexType;
    internal byte[]? UniformData => _uniformData;
    
    public D3D9Buffer(D3D9Device device, BufferDesc desc)
    {
        Size = desc.Size;
        Usage = desc.Usage;
        MemoryType = desc.MemoryType;
        
        // DX9 buffer creation rules:
        // - DYNAMIC buffers MUST use D3DPOOL_DEFAULT
        // - D3DPOOL_DEFAULT requires DYNAMIC or WRITEONLY usage
        // - D3DPOOL_MANAGED is for static data (no DYNAMIC flag)
        uint usage;
        uint pool;
        
        if (MemoryType == MemoryType.CpuToGpu)
        {
            // Dynamic buffers for frequent CPU updates
            usage = D3D9Interop.D3DUSAGE_DYNAMIC | D3D9Interop.D3DUSAGE_WRITEONLY;
            pool = D3D9Interop.D3DPOOL_DEFAULT;
        }
        else if (MemoryType == MemoryType.GpuOnly)
        {
            // Static buffers
            usage = 0;
            pool = D3D9Interop.D3DPOOL_DEFAULT;
        }
        else
        {
            // Managed pool for readback (rarely used)
            usage = 0;
            pool = D3D9Interop.D3DPOOL_MANAGED;
        }
        
        if (Usage.HasFlag(BufferUsage.Vertex))
        {
            // IDirect3DDevice9::CreateVertexBuffer is at vtable offset 26
            var createVB = D3D9ComHelper.GetComMethod<CreateVertexBufferDelegate>(device.Device, 26);
            var hr = createVB(device.Device, (uint)desc.Size, usage, 0, pool, out _buffer, IntPtr.Zero);
            if (hr != 0)
                throw new Exception($"Failed to create vertex buffer, HRESULT: 0x{hr:X8}");
        }
        else if (Usage.HasFlag(BufferUsage.Index))
        {
            // Default to 32-bit indices, can be overridden later
            _indexType = IndexType.UInt32;
            
            // IDirect3DDevice9::CreateIndexBuffer is at vtable offset 27
            var createIB = D3D9ComHelper.GetComMethod<CreateIndexBufferDelegate>(device.Device, 27);
            var format = D3D9Interop.ToD3DIndexFormat(_indexType.Value);
            var hr = createIB(device.Device, (uint)desc.Size, usage, format, pool, out _buffer, IntPtr.Zero);
            if (hr != 0)
                throw new Exception($"Failed to create index buffer, HRESULT: 0x{hr:X8}");
        }
        else if (Usage.HasFlag(BufferUsage.Uniform))
        {
            _uniformData = new byte[desc.Size];
        }
        else
        {
            throw new NotSupportedException("Only vertex, index, and uniform buffers supported in DX9");
        }
    }
    
    public unsafe void Upload(ReadOnlySpan<byte> data, ulong offset)
    {
        if (_uniformData != null)
        {
            data.CopyTo(new Span<byte>(_uniformData, (int)offset, data.Length));
            return;
        }
        // IDirect3DVertexBuffer9::Lock is at vtable offset 11
        var lockMethod = D3D9ComHelper.GetComMethod<LockDelegate>(_buffer, 11);
        
        IntPtr lockedData;
        var hr = lockMethod(_buffer, (uint)offset, (uint)data.Length, out lockedData, 0);
        if (hr != 0)
            throw new Exception($"Failed to lock buffer, HRESULT: 0x{hr:X8}");
        
        fixed (byte* src = data)
        {
            Buffer.MemoryCopy(src, (void*)lockedData, data.Length, data.Length);
        }
        
        // IDirect3DVertexBuffer9::Unlock is at vtable offset 12
        var unlockMethod = D3D9ComHelper.GetComMethod<UnlockDelegate>(_buffer, 12);
        unlockMethod(_buffer);
    }
    
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateVertexBufferDelegate(IntPtr device, uint length, uint usage, uint fvf, uint pool, out IntPtr buffer, IntPtr sharedHandle);
    
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateIndexBufferDelegate(IntPtr device, uint length, uint usage, uint format, uint pool, out IntPtr buffer, IntPtr sharedHandle);
    
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int LockDelegate(IntPtr buffer, uint offsetToLock, uint sizeToLock, out IntPtr lockedData, uint flags);
    
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int UnlockDelegate(IntPtr buffer);
    
    public void Dispose()
    {
        if (_disposed) return;
        
        if (_buffer != IntPtr.Zero)
        {
            Marshal.Release(_buffer);
            _buffer = IntPtr.Zero;
        }
        
        _disposed = true;
    }
}
