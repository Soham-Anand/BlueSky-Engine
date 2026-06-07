using System.Runtime.InteropServices;

namespace NotBSRenderer.DirectX11;

/// <summary>
/// DirectX 11 texture implementation wrapping ID3D11Texture2D + shader resource view.
/// Supports sampled, render target, and depth-stencil textures.
/// </summary>
internal sealed class D3D11Texture : IRHITexture
{
    private IntPtr _texture;  // ID3D11Texture2D*
    private IntPtr _srv;      // ID3D11ShaderResourceView*
    private IntPtr _rtv;      // ID3D11RenderTargetView*
    private IntPtr _dsv;      // ID3D11DepthStencilView*
    private bool _disposed;

    public uint Width { get; }
    public uint Height { get; }
    public TextureFormat Format { get; }
    public TextureUsage Usage { get; }
    public string DebugName { get; }

    internal IntPtr NativeTexture => _texture;
    internal IntPtr SRV => _srv;
    internal IntPtr RTV => _rtv;
    internal IntPtr DSV => _dsv;

    internal D3D11Texture(IntPtr device, TextureDesc desc)
    {
        Width = desc.Width;
        Height = desc.Height;
        Format = desc.Format;
        Usage = desc.Usage;
        DebugName = desc.DebugName ?? "D3D11Texture";

        if (device == IntPtr.Zero)
        {
            Console.WriteLine($"[D3D11Texture] Warning: null device, texture '{DebugName}' is placeholder");
            return;
        }

        uint dxgiFormat = D3D11Interop.ToDXGIFormat(Format);
        bool isDepth = Format == TextureFormat.Depth32Float || Format == TextureFormat.Depth24Stencil8;

        // Build bind flags
        uint bindFlags = 0;
        if (Usage.HasFlag(TextureUsage.Sampled))      bindFlags |= D3D11Interop.D3D11_BIND_SHADER_RESOURCE;
        if (Usage.HasFlag(TextureUsage.RenderTarget))  bindFlags |= D3D11Interop.D3D11_BIND_RENDER_TARGET;
        if (Usage.HasFlag(TextureUsage.DepthStencil))  bindFlags |= D3D11Interop.D3D11_BIND_DEPTH_STENCIL;

        // For depth textures that also need SRV, use typeless format
        uint texFormat = dxgiFormat;
        if (isDepth && Usage.HasFlag(TextureUsage.Sampled))
        {
            // D32_FLOAT → R32_TYPELESS so we can create both DSV and SRV
            if (Format == TextureFormat.Depth32Float)
                texFormat = 39; // DXGI_FORMAT_R32_TYPELESS
            else
                texFormat = 44; // DXGI_FORMAT_R24G8_TYPELESS
        }

        var texDesc = new D3D11_TEXTURE2D_DESC
        {
            Width = Width,
            Height = Height,
            MipLevels = desc.MipLevels > 0 ? desc.MipLevels : 1,
            ArraySize = desc.ArrayLayers > 0 ? desc.ArrayLayers : 1,
            Format = texFormat,
            SampleCount = 1,
            SampleQuality = 0,
            Usage = D3D11Interop.D3D11_USAGE_DEFAULT,
            BindFlags = bindFlags,
            CPUAccessFlags = 0,
            MiscFlags = 0
        };

        int hr = CreateTexture2D(device, ref texDesc, IntPtr.Zero, out _texture);
        if (hr < 0)
            throw new InvalidOperationException($"[D3D11] CreateTexture2D failed for '{DebugName}': HRESULT 0x{hr:X8}");

        // Create SRV if sampled
        if (Usage.HasFlag(TextureUsage.Sampled) && _texture != IntPtr.Zero)
        {
            uint srvFormat = dxgiFormat;
            if (isDepth && Format == TextureFormat.Depth32Float)
                srvFormat = 41; // DXGI_FORMAT_R32_FLOAT
            else if (isDepth)
                srvFormat = 46; // DXGI_FORMAT_R24_UNORM_X8_TYPELESS

            CreateSRV(device, _texture, srvFormat, texDesc.MipLevels, out _srv);
        }

        // Create RTV if render target
        if (Usage.HasFlag(TextureUsage.RenderTarget) && _texture != IntPtr.Zero)
            CreateRTV(device, _texture, dxgiFormat, out _rtv);

        // Create DSV if depth stencil
        if (Usage.HasFlag(TextureUsage.DepthStencil) && _texture != IntPtr.Zero)
            CreateDSV(device, _texture, dxgiFormat, out _dsv);
    }

    /// <summary>
    /// Constructor for wrapping an existing texture (e.g. swapchain backbuffer).
    /// </summary>
    internal D3D11Texture(IntPtr texture, IntPtr rtv, uint width, uint height, TextureFormat format)
    {
        _texture = texture;
        _rtv = rtv;
        Width = width;
        Height = height;
        Format = format;
        Usage = TextureUsage.RenderTarget;
        DebugName = "SwapchainBackbuffer";
    }

    /// <summary>
    /// Upload pixel data to the texture via UpdateSubresource.
    /// </summary>
    internal void UploadData(IntPtr deviceContext, ReadOnlySpan<byte> data)
    {
        if (_texture == IntPtr.Zero || deviceContext == IntPtr.Zero) return;

        uint bytesPerPixel = Format switch
        {
            TextureFormat.RGBA8Unorm or TextureFormat.RGBA8Srgb or
            TextureFormat.BGRA8Unorm or TextureFormat.BGRA8Srgb => 4,
            TextureFormat.R8Unorm => 1,
            TextureFormat.RGBA16Float => 8,
            TextureFormat.RGBA32Float => 16,
            _ => 4
        };

        uint rowPitch = Width * bytesPerPixel;

        unsafe
        {
            fixed (byte* pData = data)
            {
                D3D11DeviceAPI.UpdateSubresource(deviceContext, _texture, 0, IntPtr.Zero,
                    (IntPtr)pData, rowPitch, 0);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_dsv != IntPtr.Zero) { Marshal.Release(_dsv); _dsv = IntPtr.Zero; }
        if (_rtv != IntPtr.Zero) { Marshal.Release(_rtv); _rtv = IntPtr.Zero; }
        if (_srv != IntPtr.Zero) { Marshal.Release(_srv); _srv = IntPtr.Zero; }
        if (_texture != IntPtr.Zero) { Marshal.Release(_texture); _texture = IntPtr.Zero; }
        _disposed = true;
    }

    // ── COM vtable calls ─────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEXTURE2D_DESC
    {
        public uint Width, Height, MipLevels, ArraySize, Format;
        public uint SampleCount, SampleQuality, Usage, BindFlags;
        public uint CPUAccessFlags, MiscFlags;
    }

    // ID3D11Device::CreateTexture2D - vtable slot 5
    private static int CreateTexture2D(IntPtr device, ref D3D11_TEXTURE2D_DESC desc,
        IntPtr initialData, out IntPtr texture)
    {
        texture = IntPtr.Zero;
        unsafe
        {
            IntPtr vtable = *(IntPtr*)device;
            IntPtr fnPtr = *((IntPtr*)vtable + 5);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, ref D3D11_TEXTURE2D_DESC, IntPtr, out IntPtr, int>)fnPtr;
            return fn(device, ref desc, initialData, out texture);
        }
    }

    // ID3D11Device::CreateShaderResourceView - vtable slot 7
    private static void CreateSRV(IntPtr device, IntPtr resource, uint format, uint mipLevels, out IntPtr srv)
    {
        srv = IntPtr.Zero;
        // Pass null desc to use default (entire resource)
        unsafe
        {
            IntPtr vtable = *(IntPtr*)device;
            IntPtr fnPtr = *((IntPtr*)vtable + 7);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, out IntPtr, int>)fnPtr;
            fn(device, resource, IntPtr.Zero, out srv);
        }
    }

    // ID3D11Device::CreateRenderTargetView - vtable slot 9
    private static void CreateRTV(IntPtr device, IntPtr resource, uint format, out IntPtr rtv)
    {
        rtv = IntPtr.Zero;
        unsafe
        {
            IntPtr vtable = *(IntPtr*)device;
            IntPtr fnPtr = *((IntPtr*)vtable + 9);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, out IntPtr, int>)fnPtr;
            fn(device, resource, IntPtr.Zero, out rtv);
        }
    }

    // ID3D11Device::CreateDepthStencilView - vtable slot 10
    private static void CreateDSV(IntPtr device, IntPtr resource, uint format, out IntPtr dsv)
    {
        dsv = IntPtr.Zero;
        unsafe
        {
            IntPtr vtable = *(IntPtr*)device;
            IntPtr fnPtr = *((IntPtr*)vtable + 10);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, out IntPtr, int>)fnPtr;
            fn(device, resource, IntPtr.Zero, out dsv);
        }
    }
}
