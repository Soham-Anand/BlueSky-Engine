using BlueSky.Platform;
using System.Runtime.InteropServices;
using static NotBSRenderer.DirectX9.D3D9Interop;

namespace NotBSRenderer.DirectX9;

internal class D3D9Device : IRHIDevice
{
    private IntPtr _d3d9;
    private IntPtr _device;
    private D3DPRESENT_PARAMETERS _presentParams;
    private bool _disposed;
    
    public RHIBackend Backend => RHIBackend.DirectX9;
    
    public D3D9Device(IWindow window)
    {
        Console.WriteLine("[DX9] Initializing DirectX 9...");
        
        // Create D3D9
        _d3d9 = Direct3DCreate9(D3D_SDK_VERSION);
        if (_d3d9 == IntPtr.Zero)
        {
            Console.WriteLine("[DX9] ERROR: Direct3DCreate9 failed - DirectX 9 not available");
            throw new PlatformNotSupportedException("DirectX 9 is not available");
        }
        Console.WriteLine($"[DX9] Direct3D9 interface created: 0x{_d3d9:X}");
        
        // Get window handle (HWND)
        var hwnd = window.GetNativeHandle();
        Console.WriteLine($"[DX9] Window HWND: 0x{hwnd:X}");
        
        // Get actual client area size
        if (!GetClientRect(hwnd, out var clientRect))
        {
            Console.WriteLine("[DX9] WARNING: GetClientRect failed, using window size");
        }
        else
        {
            var clientWidth = clientRect.right - clientRect.left;
            var clientHeight = clientRect.bottom - clientRect.top;
            Console.WriteLine($"[DX9] Client rect: {clientWidth}x{clientHeight}");
            
            // Use client rect size if valid
            if (clientWidth > 0 && clientHeight > 0)
            {
                window.Size = new System.Numerics.Vector2(clientWidth, clientHeight);
            }
        }
        
        // Setup present parameters - try multiple configurations
        var formats = new[] { D3DFMT_X8R8G8B8, D3DFMT_A8R8G8B8, D3DFMT_R5G6B5 };
        var sizes = new[] 
        { 
            ((uint)window.Size.X, (uint)window.Size.Y),  // Explicit size
            (0u, 0u)  // Auto-size (let D3D choose from window)
        };
        
        foreach (var (width, height) in sizes)
        {
            foreach (var format in formats)
            {
                _presentParams = new D3DPRESENT_PARAMETERS
                {
                    BackBufferWidth = width,
                    BackBufferHeight = height,
                    BackBufferFormat = format,
                    BackBufferCount = 1,
                    MultiSampleType = 0,
                    MultiSampleQuality = 0,
                    SwapEffect = 1, // D3DSWAPEFFECT_DISCARD
                    hDeviceWindow = hwnd,
                    Windowed = 1,
                    EnableAutoDepthStencil = 1,
                    AutoDepthStencilFormat = D3DFMT_D24S8,
                    Flags = 0,
                    FullScreen_RefreshRateInHz = 0,
                    PresentationInterval = 0x80000000u // D3DPRESENT_INTERVAL_IMMEDIATE
                };
                
                var sizeStr = width == 0 ? "auto" : $"{width}x{height}";
                LogInfo($"[DX9] Trying format {format} with size {sizeStr}");
                
                // Try hardware vertex processing (fastest)
                var hr = CreateDevice(_d3d9, D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, hwnd, 
                    D3DCREATE_HARDWARE_VERTEXPROCESSING, ref _presentParams, out _device);
                if (hr == 0 && _device != IntPtr.Zero)
                {
                    LogInfo("[DX9] Device created with HARDWARE vertex processing. Peak performance active.");
                    LogInfo($"[DX9] Device pointer: 0x{_device:X}");
                    return;
                }
                
                LogWarning($"[DX9] HARDWARE vertex processing failed (HRESULT: 0x{hr:X8}). Your GPU (e.g. Intel HD 3000) may lack full hardware acceleration for this format.");
                LogInfo("[DX9] Attempting to fallback to MIXED vertex processing...");
                
                // Try mixed vertex processing
                hr = CreateDevice(_d3d9, D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, hwnd, 
                    D3DCREATE_MIXED_VERTEXPROCESSING, ref _presentParams, out _device);
                if (hr == 0 && _device != IntPtr.Zero)
                {
                    LogInfo("[DX9] WARNING: Device created with MIXED vertex processing. Performance will be somewhat reduced.");
                    LogInfo($"[DX9] Device pointer: 0x{_device:X}");
                    return;
                }
                
                LogWarning($"[DX9] MIXED vertex processing failed (HRESULT: 0x{hr:X8}).");
                LogInfo("[DX9] Attempting final fallback to SOFTWARE vertex processing for maximum compatibility...");
                
                // Try software vertex processing (most compatible fallback)
                hr = CreateDevice(_d3d9, D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, hwnd, 
                    D3DCREATE_SOFTWARE_VERTEXPROCESSING, ref _presentParams, out _device);
                if (hr == 0 && _device != IntPtr.Zero)
                {
                    LogInfo("[DX9] CRITICAL WARNING: Device created with SOFTWARE vertex processing! Expect low performance. Efficiency rules applied.");
                    LogInfo($"[DX9] Device pointer: 0x{_device:X}");
                    return;
                }
                
                LogWarning($"[DX9] Format {format} size {sizeStr} software fallback also failed with HRESULT: 0x{hr:X8}");
            }
        }
        
        var errorMsg = "[DX9] ERROR: All device creation attempts (Hardware, Mixed, and Software) failed for all formats and sizes. Your system does not support the required DirectX 9 footprint.";
        LogWarning(errorMsg);
        throw new PlatformNotSupportedException(errorMsg);
    }
    
    private void LogInfo(string message)
    {
        Console.WriteLine(message);
        try { System.IO.File.AppendAllText("bluesky_engine.log", message + "\n"); } catch { }
    }

    private void LogWarning(string message)
    {
        Console.WriteLine(message);
        try { System.IO.File.AppendAllText("bluesky_engine.log", "WARNING: " + message + "\n"); } catch { }
    }
    
    // COM-style call - we need to call through the vtable
    private int CreateDevice(IntPtr d3d9, uint adapter, uint deviceType, IntPtr focusWindow,
        uint behaviorFlags, ref D3DPRESENT_PARAMETERS presentParams, out IntPtr device)
    {
        // IDirect3D9::CreateDevice is at vtable offset 16
        var createDevice = D3D9ComHelper.GetComMethod<CreateDeviceDelegate>(d3d9, 16);
        return createDevice(d3d9, adapter, deviceType, focusWindow, behaviorFlags, ref presentParams, out device);
    }
    
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateDeviceDelegate(
        IntPtr d3d9,
        uint adapter,
        uint deviceType,
        IntPtr focusWindow,
        uint behaviorFlags,
        ref D3DPRESENT_PARAMETERS presentParams,
        out IntPtr device);
    
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
    
    public IRHISwapchain CreateSwapchain(IWindow window, PresentMode presentMode = PresentMode.Vsync)
    {
        return new D3D9Swapchain(this, window, presentMode);
    }
    
    public IRHIBuffer CreateBuffer(BufferDesc desc)
    {
        return new D3D9Buffer(this, desc);
    }
    
    public IRHITexture CreateTexture(TextureDesc desc)
    {
        return new D3D9Texture(this, desc);
    }
    
    public IRHIPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        return new D3D9Pipeline(this, desc);
    }
    
    public IRHICommandBuffer CreateCommandBuffer()
    {
        return new D3D9CommandBuffer(this);
    }
    
    public void Submit(IRHICommandBuffer commandBuffer)
    {
        // DX9 is immediate mode, nothing to submit
    }
    
    public void Submit(IRHICommandBuffer commandBuffer, IRHISwapchain swapchain)
    {
        // DX9 is immediate mode, nothing to submit
    }
    
    public void WaitIdle()
    {
        // No-op for DX9
    }
    
    internal void HandleDeviceLost()
    {
        var testCoop = D3D9ComHelper.GetComMethod<TestCooperativeLevelDelegate>(_device, 3);
        var hr = testCoop(_device);
        
        if (hr == unchecked((int)D3DERR_DEVICELOST))
            return; // Still lost
            
        if (hr == unchecked((int)D3DERR_DEVICENOTRESET))
        {
            Console.WriteLine("[DX9] Device not reset, resetting now...");
            var reset = D3D9ComHelper.GetComMethod<ResetDelegate>(_device, 16);
            var resetHr = reset(_device, ref _presentParams);
            if (resetHr == 0)
            {
                Console.WriteLine("[DX9] Device reset successfully");
            }
            else
            {
                Console.WriteLine($"[DX9] Device reset failed with HRESULT: 0x{resetHr:X8}");
            }
        }
    }
    
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TestCooperativeLevelDelegate(IntPtr device);
    
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ResetDelegate(IntPtr device, ref D3DPRESENT_PARAMETERS presentParams);

    public void UploadBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0)
    {
        if (buffer is not D3D9Buffer d3dBuffer)
            throw new ArgumentException("Buffer must be a D3D9 buffer");
            
        d3dBuffer.Upload(data, offset);
    }

    public void UpdateBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0)
    {
        // For DX9, Upload and Update are the same for now as we use SetData which locks the buffer
        UploadBuffer(buffer, data, offset);
    }
    
    public unsafe void UploadTexture(IRHITexture texture, ReadOnlySpan<byte> data, uint mipLevel = 0)
    {
        if (texture is not D3D9Texture d3dTexture)
            throw new ArgumentException("Texture must be a D3D9 texture");

        IntPtr surface = IntPtr.Zero;
        try
        {
            // Get surface from texture
            var getSurface = D3D9ComHelper.GetComMethod<GetSurfaceLevelDelegate>(d3dTexture.Handle, 18);
            var hr = getSurface(d3dTexture.Handle, mipLevel, out surface);
            if (hr != 0) throw new Exception($"Failed to get texture surface, HRESULT: 0x{hr:X8}");

            // Lock surface
            D3DLOCKED_RECT lockedRect;
            var lockRect = D3D9ComHelper.GetComMethod<LockRectDelegate>(surface, 13);
            hr = lockRect(surface, out lockedRect, IntPtr.Zero, 0);
            if (hr != 0) throw new Exception($"Failed to lock texture surface, HRESULT: 0x{hr:X8}");

            try
            {
                fixed (byte* pData = data)
                {
                    byte* pDest = (byte*)lockedRect.pBits;
                    int bytesPerPixel = D3D9Interop.GetBytesPerPixel(d3dTexture.Format);
                    int stride = (int)d3dTexture.Width * bytesPerPixel;
                    
                    for (int y = 0; y < (int)d3dTexture.Height; y++)
                    {
                        Buffer.MemoryCopy(
                            pData + (y * stride), 
                            pDest + (y * lockedRect.Pitch), 
                            lockedRect.Pitch, 
                            stride
                        );
                    }
                }
            }
            finally
            {
                var unlockRect = D3D9ComHelper.GetComMethod<UnlockRectDelegate>(surface, 14);
                unlockRect(surface);
            }
        }
        finally
        {
            if (surface != IntPtr.Zero)
                Marshal.Release(surface);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetSurfaceLevelDelegate(IntPtr texture, uint level, out IntPtr surface);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int LockRectDelegate(IntPtr surface, out D3DLOCKED_RECT lockedRect, IntPtr rect, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int UnlockRectDelegate(IntPtr surface);

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DLOCKED_RECT
    {
        public int Pitch;
        public IntPtr pBits;
    }
    
    internal IntPtr Device => _device;
    
    public void Dispose()
    {
        if (_disposed) return;
        
        if (_device != IntPtr.Zero)
        {
            Marshal.Release(_device);
            _device = IntPtr.Zero;
        }
        
        if (_d3d9 != IntPtr.Zero)
        {
            Marshal.Release(_d3d9);
            _d3d9 = IntPtr.Zero;
        }
        
        _disposed = true;
        Console.WriteLine("[DX9] Device disposed");
    }

    // Phase 2/3 additions - stub implementations (DX9 doesn't support these features)
    public RHICapabilities Capabilities => RHICapabilities.None;
    public DescriptorBindingMode BindingMode => DescriptorBindingMode.SlotBased;
    public IRHIPipeline CreateComputePipeline(ComputePipelineDesc desc) { throw new NotSupportedException("DX9 doesn't support compute shaders"); }
    public BindlessResourceHandle RegisterBindlessTexture(IRHITexture texture) { return BindlessResourceHandle.Invalid; }
    public BindlessResourceHandle RegisterBindlessBuffer(IRHIBuffer buffer) { return BindlessResourceHandle.Invalid; }
    public void UnregisterBindlessResource(BindlessResourceHandle handle) { }
}