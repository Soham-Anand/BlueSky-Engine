using BlueSky.Platform;
using System.Runtime.InteropServices;
using static NotBSRenderer.Metal.MetalInterop;

namespace NotBSRenderer.Metal;

internal class MetalSwapchain : IRHISwapchain
{
    private readonly MetalDevice _device;
    private readonly IWindow _window;
    private IntPtr _metalLayer;
    private IntPtr _currentDrawable;
    private MetalTexture? _currentRenderTarget;
    private bool _disposed;
    
    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public TextureFormat Format { get; }
    
    public IRHITexture CurrentRenderTarget => _currentRenderTarget ?? throw new InvalidOperationException("No current render target - window may be minimized or hidden");
    
    public MetalSwapchain(MetalDevice device, IWindow window, PresentMode presentMode)
    {
        _device = device;
        _window = window;
        Width = (uint)window.Size.X;
        Height = (uint)window.Size.Y;
        Format = TextureFormat.BGRA8Unorm;
        
        // Get the NSWindow from the window
        var nsWindow = window.GetNativeHandle();
        
        // Get content view
        var contentViewSel = GetSelector("contentView");
        var contentView = objc_msgSend(nsWindow, contentViewSel);
        
        // Get the CAMetalLayer (CocoaWindow already creates one)
        var layerSel = GetSelector("layer");
        _metalLayer = objc_msgSend(contentView, layerSel);
        
        if (_metalLayer == IntPtr.Zero)
            throw new Exception("Failed to get CAMetalLayer from window");
        
        // Set Metal device on the layer
        var setDeviceSel = GetSelector("setDevice:");
        objc_msgSend_void_ptr(_metalLayer, setDeviceSel, device.Device);
        
        // Set pixel format
        var setPixelFormatSel = GetSelector("setPixelFormat:");
        objc_msgSend_void_ulong(_metalLayer, setPixelFormatSel, ToMTLPixelFormat(Format));
        
        // Set framebufferOnly to optimize GPU performance
        var setFramebufferOnlySel = GetSelector("setFramebufferOnly:");
        SetBoolNative(_metalLayer, setFramebufferOnlySel, true);
    }
    
    public void AcquireNextImage()
    {
        var nextDrawableSel = GetSelector("nextDrawable");
        var drawable        = objc_msgSend(_metalLayer, nextDrawableSel);

        if (drawable == IntPtr.Zero)
        {
            // Can happen if the window is minimised / occluded — just skip the frame.
            _currentRenderTarget?.Dispose();
            _currentRenderTarget = null;
            _currentDrawable     = IntPtr.Zero;
            return;
        }

        var textureSel = GetSelector("texture");
        var texture    = objc_msgSend(drawable, textureSel);

        if (texture == IntPtr.Zero)
            throw new Exception("Failed to get texture from drawable.");

        _currentRenderTarget?.Dispose();
        _currentRenderTarget = new MetalTexture(texture, Width, Height, Format, TextureUsage.RenderTarget);
        _currentDrawable     = Retain(drawable);
    }
    
    public void Resize(uint width, uint height)
    {
        Width = width;
        Height = height;
        
        // Update drawable size using CGSize struct
        var setDrawableSizeSel = GetSelector("setDrawableSize:");
        SetDrawableSize(_metalLayer, setDrawableSizeSel, new CGSize { width = width, height = height });
    }
    
    public void Present()
    {
        if (_currentDrawable == IntPtr.Zero)
            return;

        // Actual presentation was already scheduled by MetalCommandBuffer.Submit
        // via presentDrawable:. We just release our retain here.
        Release(_currentDrawable);
        _currentDrawable = IntPtr.Zero;
    }
    
    internal IntPtr CurrentDrawable => _currentDrawable;
    
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct CGSize
    {
        public double width;
        public double height;
    }
    
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static unsafe extern void SetDrawableSize(IntPtr receiver, IntPtr selector, CGSize* size);
    
    private static void SetDrawableSize(IntPtr receiver, IntPtr selector, CGSize size)
    {
        unsafe
        {
            SetDrawableSize(receiver, selector, &size);
        }
    }
    
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetBoolNative(IntPtr receiver, IntPtr selector, bool value);
    
    internal IntPtr MetalLayer => _metalLayer;
    
    public void Dispose()
    {
        if (_disposed) return;
        
        _currentRenderTarget?.Dispose();
        
        if (_metalLayer != IntPtr.Zero)
        {
            Release(_metalLayer);
            _metalLayer = IntPtr.Zero;
        }
        
        _disposed = true;
    }
}
