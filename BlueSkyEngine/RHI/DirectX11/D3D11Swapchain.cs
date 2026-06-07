using System.Runtime.InteropServices;

namespace NotBSRenderer.DirectX11;

/// <summary>
/// DirectX 11 swapchain wrapping IDXGISwapChain.
/// Manages the backbuffer render target for presentation.
/// </summary>
internal sealed class D3D11Swapchain : IRHISwapchain
{
    private IntPtr _swapChain;    // IDXGISwapChain*
    private IntPtr _device;       // ID3D11Device* (not owned)
    private D3D11Texture? _backbufferTexture;
    private bool _disposed;

    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public TextureFormat Format => TextureFormat.BGRA8Unorm;

    public IRHITexture CurrentRenderTarget =>
        _backbufferTexture ?? throw new InvalidOperationException("Swapchain not initialized");

    internal IntPtr NativeSwapChain => _swapChain;

    internal D3D11Swapchain(IntPtr swapChain, IntPtr device, uint width, uint height)
    {
        _swapChain = swapChain;
        _device = device;
        Width = width;
        Height = height;

        if (_swapChain != IntPtr.Zero)
            AcquireBackbuffer();
    }

    public void AcquireNextImage()
    {
        // DX11 swapchain doesn't need explicit acquire — Present() handles it
    }

    public void Resize(uint width, uint height)
    {
        if (width == 0 || height == 0) return;

        // Release old backbuffer
        _backbufferTexture?.Dispose();
        _backbufferTexture = null;

        Width = width;
        Height = height;

        if (_swapChain == IntPtr.Zero) return;

        // IDXGISwapChain::ResizeBuffers - vtable slot 13
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_swapChain;
            IntPtr fnPtr = *((IntPtr*)vtable + 13);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, uint, uint, int>)fnPtr;
            int hr = fn(_swapChain, 1, width, height, 0 /* keep format */, 0);
            if (hr < 0)
                Console.WriteLine($"[D3D11Swapchain] ResizeBuffers failed: HRESULT 0x{hr:X8}");
        }

        AcquireBackbuffer();
    }

    public void Present()
    {
        if (_swapChain == IntPtr.Zero) return;

        // IDXGISwapChain::Present - vtable slot 8
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_swapChain;
            IntPtr fnPtr = *((IntPtr*)vtable + 8);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, int>)fnPtr;
            fn(_swapChain, 1 /* VSync */, 0);
        }
    }

    private void AcquireBackbuffer()
    {
        if (_swapChain == IntPtr.Zero) return;

        // IDXGISwapChain::GetBuffer - vtable slot 9
        IntPtr backbuffer = IntPtr.Zero;
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_swapChain;
            IntPtr fnPtr = *((IntPtr*)vtable + 9);
            // GetBuffer(this, bufferIndex, riid, ppSurface)
            Guid iid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c"); // IID_ID3D11Texture2D
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, out IntPtr, int>)fnPtr;
            int hr = fn(_swapChain, 0, &iid, out backbuffer);
            if (hr < 0)
            {
                Console.WriteLine($"[D3D11Swapchain] GetBuffer failed: HRESULT 0x{hr:X8}");
                return;
            }
        }

        // Create RTV from backbuffer — ID3D11Device::CreateRenderTargetView (vtable slot 9)
        IntPtr rtv = IntPtr.Zero;
        if (backbuffer != IntPtr.Zero && _device != IntPtr.Zero)
        {
            unsafe
            {
                IntPtr vtable = *(IntPtr*)_device;
                IntPtr fnPtr = *((IntPtr*)vtable + 9);
                var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, out IntPtr, int>)fnPtr;
                fn(_device, backbuffer, IntPtr.Zero, out rtv);
            }
        }

        _backbufferTexture = new D3D11Texture(backbuffer, rtv, Width, Height, Format);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _backbufferTexture?.Dispose();
        _backbufferTexture = null;
        if (_swapChain != IntPtr.Zero)
        {
            Marshal.Release(_swapChain);
            _swapChain = IntPtr.Zero;
        }
        _disposed = true;
    }
}
