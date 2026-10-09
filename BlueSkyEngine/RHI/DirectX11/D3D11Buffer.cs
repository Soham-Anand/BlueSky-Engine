using System.Runtime.InteropServices;

namespace BlueSky.Rendering.RHI.DirectX11;

internal sealed class D3D11Buffer : IRHIBuffer
{
    private IntPtr _buffer;
    private bool _disposed;

    public ulong Size { get; }
    public BufferUsage Usage { get; }
    public MemoryType MemoryType { get; }
    public string DebugName { get; }
    
    internal IntPtr NativePtr => _buffer;

    internal D3D11Buffer(IntPtr device, BufferDesc desc)
    {
        Size = desc.Size;
        Usage = desc.Usage;
        MemoryType = desc.MemoryType;
        DebugName = desc.DebugName ?? "D3D11Buffer";

        uint bindFlags = 0;
        if (Usage.HasFlag(BufferUsage.Vertex))  bindFlags |= D3D11Interop.D3D11_BIND_VERTEX_BUFFER;
        if (Usage.HasFlag(BufferUsage.Index))   bindFlags |= D3D11Interop.D3D11_BIND_INDEX_BUFFER;
        if (Usage.HasFlag(BufferUsage.Uniform)) bindFlags |= D3D11Interop.D3D11_BIND_CONSTANT_BUFFER;
        if (Usage.HasFlag(BufferUsage.Storage)) bindFlags |= D3D11Interop.D3D11_BIND_SHADER_RESOURCE;

        uint usage = MemoryType == MemoryType.GpuOnly
            ? D3D11Interop.D3D11_USAGE_DEFAULT
            : D3D11Interop.D3D11_USAGE_DYNAMIC;

        uint cpuAccess = MemoryType == MemoryType.GpuOnly
            ? 0u
            : D3D11Interop.D3D11_CPU_ACCESS_WRITE;

        uint byteWidth = (uint)Size;
        if (Usage.HasFlag(BufferUsage.Uniform))
            byteWidth = (byteWidth + 15u) & ~15u;

        var bufferDesc = new D3D11Interop.D3D11_BUFFER_DESC
        {
            ByteWidth = byteWidth,
            Usage = usage,
            BindFlags = bindFlags,
            CPUAccessFlags = cpuAccess,
            MiscFlags = 0,
            StructureByteStride = 0
        };

        if (device == IntPtr.Zero)
            throw new InvalidOperationException($"Cannot create D3D11 buffer '{DebugName}' without a valid device.");

        int hr = D3D11DeviceAPI.CreateBuffer(device, ref bufferDesc, IntPtr.Zero, out _buffer);
        if (hr < 0)
            throw new InvalidOperationException($"[D3D11] CreateBuffer failed for '{DebugName}': HRESULT 0x{hr:X8}");
    }

    internal void UpdateData(IntPtr deviceContext, ReadOnlySpan<byte> data, ulong offset = 0)
    {
        if (_buffer == IntPtr.Zero || deviceContext == IntPtr.Zero) return;

        long available = (long)Size - (long)offset;
        if (data.Length > available)
            data = data.Slice(0, (int)Math.Min(data.Length, available));
        if (data.Length == 0) return;

        if (MemoryType == MemoryType.GpuOnly)
        {
            unsafe
            {
                fixed (byte* pData = data)
                {
                    D3D11DeviceAPI.UpdateSubresource(deviceContext, _buffer, 0, IntPtr.Zero, (IntPtr)pData, 0, 0);
                }
            }
        }
        else
        {
            int hr = D3D11DeviceAPI.Map(deviceContext, _buffer, 0, 4 /* D3D11_MAP_WRITE_DISCARD */, 0, out var mapped);
            if (hr >= 0 && mapped.pData != IntPtr.Zero)
            {
                unsafe
                {
                    fixed (byte* pSrc = data)
                    {
                        Buffer.MemoryCopy(pSrc, (void*)((nint)mapped.pData + (nint)offset),
                            available, data.Length);
                    }
                }
                D3D11DeviceAPI.Unmap(deviceContext, _buffer, 0);
            }
        }
    }

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

internal static class D3D11DeviceAPI
{
    public static int CreateBuffer(IntPtr device, ref D3D11Interop.D3D11_BUFFER_DESC desc,
        IntPtr initialData, out IntPtr buffer)
    {
        buffer = IntPtr.Zero;
        if (device == IntPtr.Zero) return -1;

        unsafe
        {
            IntPtr vtable = *(IntPtr*)device;
            IntPtr fnPtr = *((IntPtr*)vtable + 3);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, ref D3D11Interop.D3D11_BUFFER_DESC, IntPtr, out IntPtr, int>)fnPtr;
            
            fixed (D3D11Interop.D3D11_BUFFER_DESC* pDesc = &desc)
            fixed (IntPtr* pBuffer = &buffer)
            {
                return fn(device, ref desc, initialData, out buffer);
            }
        }
    }

    public static void UpdateSubresource(IntPtr context, IntPtr resource, uint subresource,
        IntPtr dstBox, IntPtr srcData, uint srcRowPitch, uint srcDepthPitch)
    {
        if (context == IntPtr.Zero || resource == IntPtr.Zero) return;
        unsafe
        {
            IntPtr vtable = *(IntPtr*)context;
            IntPtr fnPtr = *((IntPtr*)vtable + 48);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, IntPtr, IntPtr, uint, uint, void>)fnPtr;
            fn(context, resource, subresource, dstBox, srcData, srcRowPitch, srcDepthPitch);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_MAPPED_SUBRESOURCE
    {
        public IntPtr pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    public static int Map(IntPtr context, IntPtr resource, uint subresource, uint mapType,
        uint mapFlags, out D3D11_MAPPED_SUBRESOURCE mapped)
    {
        mapped = default;
        if (context == IntPtr.Zero) return -1;
        unsafe
        {
            IntPtr vtable = *(IntPtr*)context;
            IntPtr fnPtr = *((IntPtr*)vtable + 14);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, out D3D11_MAPPED_SUBRESOURCE, int>)fnPtr;
            return fn(context, resource, subresource, mapType, mapFlags, out mapped);
        }
    }

    public static void Unmap(IntPtr context, IntPtr resource, uint subresource)
    {
        if (context == IntPtr.Zero) return;
        unsafe
        {
            IntPtr vtable = *(IntPtr*)context;
            IntPtr fnPtr = *((IntPtr*)vtable + 15);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)fnPtr;
            fn(context, resource, subresource);
        }
    }
}
