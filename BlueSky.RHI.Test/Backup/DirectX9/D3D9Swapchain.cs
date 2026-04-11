using BlueSky.Platform;

namespace NotBSRenderer.DirectX9;

internal class D3D9Swapchain : IRHISwapchain
{
    private readonly D3D9Device _device;
    private D3D9Texture? _backBuffer;
    private readonly GetBackBufferDelegate _getBackBuffer;
    private readonly PresentDelegate _present;
    
    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public TextureFormat Format => TextureFormat.BGRA8Unorm;
    
    public IRHITexture CurrentRenderTarget => _backBuffer ?? throw new InvalidOperationException("No back buffer");
    
    public D3D9Swapchain(D3D9Device device, IWindow window, PresentMode presentMode)
    {
        _device = device;
        Width = (uint)window.Size.X;
        Height = (uint)window.Size.Y;
        
        _getBackBuffer = D3D9ComHelper.GetComMethod<GetBackBufferDelegate>(_device.Device, 18);
        _present = D3D9ComHelper.GetComMethod<PresentDelegate>(_device.Device, 17);
        
        Console.WriteLine($"[DX9] Swapchain created: {Width}x{Height}");
    }
    
    public void AcquireNextImage()
    {
        // Get back buffer from device
        IntPtr backBufferSurface;
        var hr = _getBackBuffer(_device.Device, 0, 0, 0 /* D3DBACKBUFFER_TYPE_MONO */, out backBufferSurface);
        if (hr != 0)
            throw new Exception($"Failed to get back buffer, HRESULT: {hr:X}");
        
        _backBuffer = new D3D9Texture(backBufferSurface, Width, Height, Format);
    }
    
    public void Resize(uint width, uint height)
    {
        Width = width;
        Height = height;
        Console.WriteLine($"[DX9] Swapchain resized: {Width}x{Height}");
    }
    
    public void Present()
    {
        var hr = _present(_device.Device, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hr != 0)
            Console.WriteLine($"[DX9] Present failed, HRESULT: {hr:X}");
    }
    
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate int GetBackBufferDelegate(IntPtr device, uint swapChain, uint backBuffer, uint type, out IntPtr surface);
    
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate int PresentDelegate(IntPtr device, IntPtr sourceRect, IntPtr destRect, IntPtr destWindow, IntPtr dirtyRegion);
    
    public void Dispose()
    {
        _backBuffer?.Dispose();
    }
}
