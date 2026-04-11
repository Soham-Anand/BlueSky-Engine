using System.Runtime.InteropServices;

namespace NotBSRenderer.DirectX9;

internal class D3D9Texture : IRHITexture
{
    private IntPtr _texture;
    private IntPtr _surface;
    private bool _disposed;
    
    public uint Width { get; }
    public uint Height { get; }
    public TextureFormat Format { get; }
    public TextureUsage Usage { get; }
    
    internal IntPtr Handle => _texture;
    internal IntPtr Surface => _surface;
    
    // Constructor for wrapping back buffer surface
    public D3D9Texture(IntPtr surface, uint width, uint height, TextureFormat format)
    {
        _surface = surface;
        _texture = IntPtr.Zero;
        Width = width;
        Height = height;
        Format = format;
        Usage = TextureUsage.RenderTarget;
    }
    
    public D3D9Texture(D3D9Device device, TextureDesc desc)
    {
        Width = desc.Width;
        Height = desc.Height;
        Format = desc.Format;
        Usage = desc.Usage;
        
        uint usage = 0;
        if (Usage.HasFlag(TextureUsage.RenderTarget))
            usage |= D3D9Interop.D3DUSAGE_RENDERTARGET;
        
        // Determine pool based on usage
        uint pool;
        if (Usage.HasFlag(TextureUsage.RenderTarget) || Usage.HasFlag(TextureUsage.DepthStencil))
            pool = D3D9Interop.D3DPOOL_DEFAULT;  // GPU-only resources
        else
            pool = D3D9Interop.D3DPOOL_MANAGED;  // CPU-accessible resources
        
        var hr = CreateTexture(device.Device, desc.Width, desc.Height, 1, usage,
            D3D9Interop.ToD3DFormat(desc.Format), pool, out _texture, IntPtr.Zero);
        
        if (hr != 0)
            throw new Exception($"Failed to create texture, HRESULT: {hr:X}");
    }
    
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate int CreateTextureDelegate(IntPtr device, uint width, uint height, uint levels, uint usage, uint format, uint pool, out IntPtr texture, IntPtr sharedHandle);

    private static int CreateTexture(IntPtr device, uint width, uint height, uint levels, uint usage, uint format, uint pool, out IntPtr texture, IntPtr sharedHandle) => D3D9ComHelper.GetComMethod<CreateTextureDelegate>(device, 23)(device, width, height, levels, usage, format, pool, out texture, sharedHandle);
    
    public void Dispose()
    {
        if (_disposed) return;
        
        if (_texture != IntPtr.Zero)
        {
            Marshal.Release(_texture);
            _texture = IntPtr.Zero;
        }
        
        _disposed = true;
    }
}
