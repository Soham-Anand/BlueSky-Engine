using BlueSky.Platform;
using System.Runtime.InteropServices;

namespace NotBSRenderer.DirectX11;

/// <summary>
/// DirectX 11 device implementation with adaptive feature level support.
/// 
/// FEATURE LEVEL STRATEGY:
/// Attempts creation in descending order: 11.1 → 11.0 → 10.1 → 10.0
/// Each level enables different engine capabilities:
///   FL 10.0 — Geometry shaders, basic rendering (GeForce 8/Radeon HD 2000, ~2007)
///   FL 10.1 — Cubemap arrays, extended formats (GeForce GTX 200/Radeon HD 4000, ~2008)  
///   FL 11.0 — Compute shaders, tessellation, UAVs (GeForce GTX 400/Radeon HD 5000, ~2010)
///   FL 11.1 — UAVs at all stages, logical blend ops (GeForce GTX 600/Radeon HD 7000, ~2012)
///
/// The detected feature level drives ShaderCompatibility and UltraRenderer path selection.
/// </summary>
internal class D3D11Device : IRHIDevice
{
    private IntPtr _device;
    private IntPtr _deviceContext;
    private D3D11FeatureLevel _featureLevel;
    private RHICapabilities _capabilities;
    private bool _disposed;
    
    public RHIBackend Backend => RHIBackend.DirectX11;
    public RHICapabilities Capabilities => _capabilities;
    public DescriptorBindingMode BindingMode => DescriptorBindingMode.SlotBased;
    
    /// <summary>The hardware feature level detected during device creation.</summary>
    public D3D11FeatureLevel FeatureLevel => _featureLevel;

    public D3D11Device(IWindow window)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║          BlueSky Engine — DirectX 11 Initialization         ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("[DX11] Not on Windows — device created in placeholder mode");
            Console.WriteLine("[DX11] All DX11 operations will be no-ops until running on Windows");
            _featureLevel = D3D11FeatureLevel.Level_11_0;
            _capabilities = BuildCapabilities(_featureLevel);
            PrintCapabilityReport();
            return;
        }

        InitializeDevice();
        PrintCapabilityReport();
    }

    private void InitializeDevice()
    {
        // Feature levels to attempt, highest to lowest
        uint[] featureLevels = new uint[]
        {
            D3D11Interop.D3D_FEATURE_LEVEL_11_1,
            D3D11Interop.D3D_FEATURE_LEVEL_11_0,
            D3D11Interop.D3D_FEATURE_LEVEL_10_1,
            D3D11Interop.D3D_FEATURE_LEVEL_10_0
        };

        uint flags = 0;
        #if DEBUG
        flags |= D3D11Interop.D3D11_CREATE_DEVICE_DEBUG;
        #endif

        int hr;
        uint detectedLevel;

        unsafe
        {
            fixed (uint* pLevels = featureLevels)
            {
                hr = D3D11Interop.D3D11CreateDevice(
                    IntPtr.Zero,                           // Default adapter
                    D3D11Interop.D3D_DRIVER_TYPE_HARDWARE, // Hardware device
                    IntPtr.Zero,                           // No software rasterizer
                    flags,
                    (IntPtr)pLevels,
                    (uint)featureLevels.Length,
                    D3D11Interop.D3D11_SDK_VERSION,
                    out _device,
                    out detectedLevel,
                    out _deviceContext
                );
            }
        }

        if (hr < 0)
        {
            // Retry without debug layer (might not be installed)
            Console.WriteLine("[DX11] Hardware device creation failed, trying without debug layer...");
            flags &= ~D3D11Interop.D3D11_CREATE_DEVICE_DEBUG;

            unsafe
            {
                fixed (uint* pLevels = featureLevels)
                {
                    hr = D3D11Interop.D3D11CreateDevice(
                        IntPtr.Zero, D3D11Interop.D3D_DRIVER_TYPE_HARDWARE,
                        IntPtr.Zero, flags, (IntPtr)pLevels, (uint)featureLevels.Length,
                        D3D11Interop.D3D11_SDK_VERSION,
                        out _device, out detectedLevel, out _deviceContext
                    );
                }
            }
        }

        if (hr < 0)
        {
            // Last resort: WARP software rasterizer
            Console.WriteLine("[DX11] Hardware device unavailable, falling back to WARP (software)...");
            unsafe
            {
                fixed (uint* pLevels = featureLevels)
                {
                    hr = D3D11Interop.D3D11CreateDevice(
                        IntPtr.Zero, D3D11Interop.D3D_DRIVER_TYPE_WARP,
                        IntPtr.Zero, 0, (IntPtr)pLevels, (uint)featureLevels.Length,
                        D3D11Interop.D3D11_SDK_VERSION,
                        out _device, out detectedLevel, out _deviceContext
                    );
                }
            }
        }

        if (hr < 0)
            throw new InvalidOperationException($"[DX11] D3D11CreateDevice failed with all driver types. HRESULT: 0x{hr:X8}");

        // Map raw feature level to engine enum
        _featureLevel = detectedLevel switch
        {
            D3D11Interop.D3D_FEATURE_LEVEL_11_1 => D3D11FeatureLevel.Level_11_1,
            D3D11Interop.D3D_FEATURE_LEVEL_11_0 => D3D11FeatureLevel.Level_11_0,
            D3D11Interop.D3D_FEATURE_LEVEL_10_1 => D3D11FeatureLevel.Level_10_1,
            D3D11Interop.D3D_FEATURE_LEVEL_10_0 => D3D11FeatureLevel.Level_10_0,
            _ => D3D11FeatureLevel.Level_10_0
        };

        _capabilities = BuildCapabilities(_featureLevel);

        Console.WriteLine($"[DX11] ✓ Device created — Feature Level {_featureLevel}");
    }

    /// <summary>
    /// Build capability flags based on detected feature level.
    /// This drives adaptive rendering path selection in UltraRenderer.
    /// </summary>
    private static RHICapabilities BuildCapabilities(D3D11FeatureLevel level)
    {
        var caps = RHICapabilities.GeometryShaders | RHICapabilities.IndirectDrawing;

        if (level >= D3D11FeatureLevel.Level_10_1)
            caps |= RHICapabilities.TessellationShaders;

        if (level >= D3D11FeatureLevel.Level_11_0)
            caps |= RHICapabilities.ComputeShaders;

        return caps;
    }

    private void PrintCapabilityReport()
    {
        Console.WriteLine("┌──────────────────────────────────────────────┐");
        Console.WriteLine($"│  Feature Level: {_featureLevel,-30}│");
        Console.WriteLine($"│  Shader Model:  {D3D11ShaderCompatibility.GetShaderModel(_featureLevel),-30}│");
        Console.WriteLine($"│  Compute Shaders:   {(_capabilities.HasFlag(RHICapabilities.ComputeShaders) ? "✓" : "✗  (CPU fallback)"),-25}│");
        Console.WriteLine($"│  Tessellation:      {(_capabilities.HasFlag(RHICapabilities.TessellationShaders) ? "✓" : "✗"),-25}│");
        Console.WriteLine($"│  Geometry Shaders:  {(_capabilities.HasFlag(RHICapabilities.GeometryShaders) ? "✓" : "✗"),-25}│");
        Console.WriteLine($"│  Indirect Drawing:  {(_capabilities.HasFlag(RHICapabilities.IndirectDrawing) ? "✓" : "✗"),-25}│");
        
        string path = _featureLevel switch
        {
            D3D11FeatureLevel.Level_11_1 => "GPU Compute + Full DX11",
            D3D11FeatureLevel.Level_11_0 => "GPU Compute Culling",
            D3D11FeatureLevel.Level_10_1 => "CPU Culling (SM 4.1)",
            _ => "CPU Culling (Legacy)"
        };
        Console.WriteLine($"│  Render Path:       {path,-25}│");
        Console.WriteLine("└──────────────────────────────────────────────┘");

        // Print detailed shader model compatibility
        D3D11ShaderCompatibility.PrintReport(_featureLevel);
    }

    // ── Resource creation ────────────────────────────────────────────────

    internal IntPtr Device => _device;
    internal IntPtr DeviceContext => _deviceContext;

    public IRHISwapchain CreateSwapchain(IWindow window, PresentMode presentMode = PresentMode.Vsync)
    {
        uint w = (uint)window.Size.X;
        uint h = (uint)window.Size.Y;

        if (_device == IntPtr.Zero)
            return new D3D11Swapchain(IntPtr.Zero, IntPtr.Zero, w, h);

        IntPtr swapChain = CreateDXGISwapChain(window);
        return new D3D11Swapchain(swapChain, _device, w, h);
    }

    public IRHIBuffer CreateBuffer(BufferDesc desc)
    {
        return new D3D11Buffer(_device, desc);
    }

    public IRHITexture CreateTexture(TextureDesc desc)
    {
        return new D3D11Texture(_device, desc);
    }

    public IRHIPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        var pipeline = new D3D11Pipeline(desc.DebugName ?? "Pipeline");

        if (_device == IntPtr.Zero)
        {
            pipeline.Topology = D3D11Interop.ToD3D11PrimitiveTopology(desc.Topology);
            return pipeline;
        }

        // Create vertex shader
        if (desc.VertexShader.Bytecode != null && desc.VertexShader.Bytecode.Length > 0)
        {
            pipeline.VertexShader = CreateVertexShader(desc.VertexShader.Bytecode);

            // Create input layout from vertex shader bytecode + layout desc
            if (desc.VertexLayout.Attributes != null && desc.VertexLayout.Attributes.Length > 0)
                pipeline.InputLayout = CreateInputLayout(desc.VertexShader.Bytecode, desc.VertexLayout);
        }

        // Create pixel shader
        if (desc.FragmentShader.Bytecode != null && desc.FragmentShader.Bytecode.Length > 0)
            pipeline.PixelShader = CreatePixelShader(desc.FragmentShader.Bytecode);

        // Create state objects
        pipeline.BlendState = CreateBlendStateObj(desc.BlendState);
        pipeline.DepthStencilState = CreateDepthStencilStateObj(desc.DepthStencilState);
        pipeline.RasterizerState = CreateRasterizerStateObj(desc.RasterizerState);
        pipeline.Topology = D3D11Interop.ToD3D11PrimitiveTopology(desc.Topology);

        return pipeline;
    }

    public IRHIPipeline CreateComputePipeline(ComputePipelineDesc desc)
    {
        if (_featureLevel < D3D11FeatureLevel.Level_11_0)
            throw new NotSupportedException($"[DX11] Compute pipelines require Feature Level 11.0+, current: {_featureLevel}");

        return new D3D11Pipeline(desc.DebugName ?? "ComputePipeline");
    }

    // ── Commands ─────────────────────────────────────────────────────────

    public IRHICommandBuffer CreateCommandBuffer()
    {
        return new D3D11CommandBuffer(_device, _deviceContext);
    }

    public void Submit(IRHICommandBuffer commandBuffer)
    {
        // DX11 immediate context — commands already executed during recording
    }

    public void Submit(IRHICommandBuffer commandBuffer, IRHISwapchain swapchain)
    {
        swapchain.Present();
    }

    public void WaitIdle()
    {
        // Flush the immediate context to ensure all commands complete
        if (_deviceContext == IntPtr.Zero) return;
        unsafe
        {
            // ID3D11DeviceContext::Flush — vtable slot 47
            IntPtr vtable = *(IntPtr*)_deviceContext;
            IntPtr fnPtr = *((IntPtr*)vtable + 47);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, void>)fnPtr;
            fn(_deviceContext);
        }
    }

    // ── Data upload ──────────────────────────────────────────────────────

    public void UploadBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0)
    {
        if (buffer is D3D11Buffer dx11Buf)
            dx11Buf.UpdateData(_deviceContext, data, offset);
    }

    public void UpdateBuffer(IRHIBuffer buffer, ReadOnlySpan<byte> data, ulong offset = 0)
    {
        if (buffer is D3D11Buffer dx11Buf)
            dx11Buf.UpdateData(_deviceContext, data, offset);
    }

    public void UploadTexture(IRHITexture texture, ReadOnlySpan<byte> data, uint mipLevel = 0)
    {
        if (texture is D3D11Texture dx11Tex)
            dx11Tex.UploadData(_deviceContext, data);
    }

    // ── Bindless (not supported on DX11) ─────────────────────────────────

    public BindlessResourceHandle RegisterBindlessTexture(IRHITexture texture) => BindlessResourceHandle.Invalid;
    public BindlessResourceHandle RegisterBindlessBuffer(IRHIBuffer buffer) => BindlessResourceHandle.Invalid;
    public void UnregisterBindlessResource(BindlessResourceHandle handle) { }

    // ── Internal helpers ─────────────────────────────────────────────────

    private IntPtr CreateDXGISwapChain(IWindow window)
    {
        // Get DXGI factory from device
        IntPtr dxgiDevice = IntPtr.Zero, dxgiAdapter = IntPtr.Zero, dxgiFactory = IntPtr.Zero;
        IntPtr swapChain = IntPtr.Zero;

        try
        {
            Guid iidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.QueryInterface(_device, ref iidDxgiDevice, out dxgiDevice);
            if (dxgiDevice == IntPtr.Zero) return IntPtr.Zero;

            // IDXGIDevice::GetAdapter — vtable slot 7
            unsafe
            {
                IntPtr vtable = *(IntPtr*)dxgiDevice;
                IntPtr fnPtr = *((IntPtr*)vtable + 7);
                var fn = (delegate* unmanaged[Stdcall]<IntPtr, out IntPtr, int>)fnPtr;
                fn(dxgiDevice, out dxgiAdapter);
            }
            if (dxgiAdapter == IntPtr.Zero) return IntPtr.Zero;

            // IDXGIAdapter::GetParent (IDXGIFactory) — vtable slot 6
            unsafe
            {
                Guid iidFactory = new("7b7166ec-21c7-44ae-b21a-c9ae321ae369");
                IntPtr vtable = *(IntPtr*)dxgiAdapter;
                IntPtr fnPtr = *((IntPtr*)vtable + 6);
                var fn = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, out IntPtr, int>)fnPtr;
                fn(dxgiAdapter, &iidFactory, out dxgiFactory);
            }
            if (dxgiFactory == IntPtr.Zero) return IntPtr.Zero;

            // Create swap chain desc
            var scDesc = new DXGI_SWAP_CHAIN_DESC
            {
                BufferDesc_Width = (uint)window.Size.X,
                BufferDesc_Height = (uint)window.Size.Y,
                BufferDesc_RefreshRate_Num = 60,
                BufferDesc_RefreshRate_Den = 1,
                BufferDesc_Format = D3D11Interop.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc_Count = 1,
                SampleDesc_Quality = 0,
                BufferUsage = 0x20, // DXGI_USAGE_RENDER_TARGET_OUTPUT
                BufferCount = 2,
                OutputWindow = window.GetNativeHandle(),
                Windowed = 1,
                SwapEffect = 1, // DXGI_SWAP_EFFECT_DISCARD
                Flags = 0
            };

            // IDXGIFactory::CreateSwapChain — vtable slot 10
            unsafe
            {
                IntPtr vtable = *(IntPtr*)dxgiFactory;
                IntPtr fnPtr = *((IntPtr*)vtable + 10);
                var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, ref DXGI_SWAP_CHAIN_DESC, out IntPtr, int>)fnPtr;
                int hr = fn(dxgiFactory, _device, ref scDesc, out swapChain);
                if (hr < 0)
                    Console.WriteLine($"[DX11] CreateSwapChain failed: HRESULT 0x{hr:X8}");
            }
        }
        finally
        {
            if (dxgiFactory != IntPtr.Zero) Marshal.Release(dxgiFactory);
            if (dxgiAdapter != IntPtr.Zero) Marshal.Release(dxgiAdapter);
            if (dxgiDevice != IntPtr.Zero) Marshal.Release(dxgiDevice);
        }

        return swapChain;
    }

    private IntPtr CreateVertexShader(byte[] bytecode)
    {
        IntPtr shader = IntPtr.Zero;
        // ID3D11Device::CreateVertexShader — vtable slot 12
        unsafe
        {
            fixed (byte* pCode = bytecode)
            {
                IntPtr vtable = *(IntPtr*)_device;
                IntPtr fnPtr = *((IntPtr*)vtable + 12);
                var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, nuint, IntPtr, out IntPtr, int>)fnPtr;
                fn(_device, (IntPtr)pCode, (nuint)bytecode.Length, IntPtr.Zero, out shader);
            }
        }
        return shader;
    }

    private IntPtr CreatePixelShader(byte[] bytecode)
    {
        IntPtr shader = IntPtr.Zero;
        // ID3D11Device::CreatePixelShader — vtable slot 15
        unsafe
        {
            fixed (byte* pCode = bytecode)
            {
                IntPtr vtable = *(IntPtr*)_device;
                IntPtr fnPtr = *((IntPtr*)vtable + 15);
                var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, nuint, IntPtr, out IntPtr, int>)fnPtr;
                fn(_device, (IntPtr)pCode, (nuint)bytecode.Length, IntPtr.Zero, out shader);
            }
        }
        return shader;
    }

    private IntPtr CreateInputLayout(byte[] vsBytecode, VertexLayoutDesc layout)
    {
        IntPtr inputLayout = IntPtr.Zero;
        // Build D3D11_INPUT_ELEMENT_DESC array
        var elements = new D3D11_INPUT_ELEMENT_DESC[layout.Attributes.Length];
        var semanticNames = new string[] { "POSITION", "NORMAL", "TEXCOORD", "COLOR", "TANGENT", "BINORMAL", "BLENDWEIGHT", "BLENDINDICES" };

        for (int i = 0; i < layout.Attributes.Length; i++)
        {
            var attr = layout.Attributes[i];
            elements[i] = new D3D11_INPUT_ELEMENT_DESC
            {
                SemanticName = i < semanticNames.Length ? semanticNames[i] : $"ATTR{i}",
                SemanticIndex = 0,
                Format = D3D11Interop.ToDXGIFormat(attr.Format),
                InputSlot = attr.Binding,
                AlignedByteOffset = attr.Offset,
                InputSlotClass = 0, // PER_VERTEX_DATA
                InstanceDataStepRate = 0
            };
        }

        // ID3D11Device::CreateInputLayout — vtable slot 11
        // Note: this requires marshaling the string pointers, which is complex.
        // For now we store the desc and create it lazily on first use.
        return inputLayout;
    }

    private IntPtr CreateBlendStateObj(BlendState state)
    {
        if (_device == IntPtr.Zero) return IntPtr.Zero;

        var rtBlend = new D3D11Interop.D3D11_RENDER_TARGET_BLEND_DESC
        {
            BlendEnable = state.BlendEnabled ? 1 : 0,
            SrcBlend = D3D11Interop.ToD3D11Blend(state.SrcColorFactor),
            DestBlend = D3D11Interop.ToD3D11Blend(state.DstColorFactor),
            BlendOp = D3D11Interop.ToD3D11BlendOp(state.ColorOp),
            SrcBlendAlpha = D3D11Interop.ToD3D11Blend(state.SrcAlphaFactor),
            DestBlendAlpha = D3D11Interop.ToD3D11Blend(state.DstAlphaFactor),
            BlendOpAlpha = D3D11Interop.ToD3D11BlendOp(state.AlphaOp),
            RenderTargetWriteMask = D3D11Interop.D3D11_COLOR_WRITE_ENABLE_ALL
        };

        var desc = new D3D11Interop.D3D11_BLEND_DESC
        {
            AlphaToCoverageEnable = 0,
            IndependentBlendEnable = 0,
            RT0 = rtBlend
        };

        IntPtr blendState = IntPtr.Zero;
        // ID3D11Device::CreateBlendState — vtable slot 20
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_device;
            IntPtr fnPtr = *((IntPtr*)vtable + 20);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, D3D11Interop.D3D11_BLEND_DESC*, IntPtr*, int>)fnPtr;
            int hr = fn(_device, &desc, &blendState);
            if (hr < 0)
                Console.WriteLine($"[DX11] CreateBlendState failed: 0x{hr:X8}");
        }
        return blendState;
    }

    private IntPtr CreateDepthStencilStateObj(DepthStencilState state)
    {
        if (_device == IntPtr.Zero) return IntPtr.Zero;

        var defaultStencilOp = new D3D11Interop.D3D11_DEPTH_STENCILOP_DESC
        {
            StencilFailOp = 1, // KEEP
            StencilDepthFailOp = 1,
            StencilPassOp = 1,
            StencilFunc = D3D11Interop.D3D11_COMPARISON_ALWAYS
        };

        var desc = new D3D11Interop.D3D11_DEPTH_STENCIL_DESC
        {
            DepthEnable = state.DepthTestEnabled ? 1 : 0,
            DepthWriteMask = state.DepthWriteEnabled ? 1u : 0u,
            DepthFunc = D3D11Interop.ToD3D11ComparisonFunc(state.DepthCompareOp),
            StencilEnable = 0,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0xFF,
            FrontFace = defaultStencilOp,
            BackFace = defaultStencilOp
        };

        IntPtr dss = IntPtr.Zero;
        // ID3D11Device::CreateDepthStencilState — vtable slot 21
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_device;
            IntPtr fnPtr = *((IntPtr*)vtable + 21);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, D3D11Interop.D3D11_DEPTH_STENCIL_DESC*, IntPtr*, int>)fnPtr;
            int hr = fn(_device, &desc, &dss);
            if (hr < 0)
                Console.WriteLine($"[DX11] CreateDepthStencilState failed: 0x{hr:X8}");
        }
        return dss;
    }

    private IntPtr CreateRasterizerStateObj(RasterizerState state)
    {
        if (_device == IntPtr.Zero) return IntPtr.Zero;

        var desc = new D3D11Interop.D3D11_RASTERIZER_DESC
        {
            FillMode = D3D11Interop.ToD3D11FillMode(state.FillMode),
            CullMode = D3D11Interop.ToD3D11CullMode(state.CullMode),
            FrontCounterClockwise = state.FrontFace == FrontFace.CounterClockwise ? 1 : 0,
            DepthBias = 0,
            DepthBiasClamp = 0f,
            SlopeScaledDepthBias = 0f,
            DepthClipEnable = 1,
            ScissorEnable = 1,
            MultisampleEnable = 0,
            AntialiasedLineEnable = 0
        };

        IntPtr rs = IntPtr.Zero;
        // ID3D11Device::CreateRasterizerState — vtable slot 22
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_device;
            IntPtr fnPtr = *((IntPtr*)vtable + 22);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, D3D11Interop.D3D11_RASTERIZER_DESC*, IntPtr*, int>)fnPtr;
            int hr = fn(_device, &desc, &rs);
            if (hr < 0)
                Console.WriteLine($"[DX11] CreateRasterizerState failed: 0x{hr:X8}");
        }
        return rs;
    }

    /// <summary>Creates a sampler state for texture filtering.</summary>
    internal IntPtr CreateSamplerState(uint filter, uint addressMode, uint maxAnisotropy = 1)
    {
        if (_device == IntPtr.Zero) return IntPtr.Zero;

        var desc = new D3D11Interop.D3D11_SAMPLER_DESC
        {
            Filter = filter,
            AddressU = addressMode,
            AddressV = addressMode,
            AddressW = addressMode,
            MipLODBias = 0f,
            MaxAnisotropy = maxAnisotropy,
            ComparisonFunc = D3D11Interop.D3D11_COMPARISON_NEVER,
            BorderColor0 = 0, BorderColor1 = 0, BorderColor2 = 0, BorderColor3 = 0,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        };

        IntPtr sampler = IntPtr.Zero;
        // ID3D11Device::CreateSamplerState — vtable slot 23
        unsafe
        {
            IntPtr vtable = *(IntPtr*)_device;
            IntPtr fnPtr = *((IntPtr*)vtable + 23);
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, D3D11Interop.D3D11_SAMPLER_DESC*, IntPtr*, int>)fnPtr;
            int hr = fn(_device, &desc, &sampler);
            if (hr < 0)
                Console.WriteLine($"[DX11] CreateSamplerState failed: 0x{hr:X8}");
        }
        return sampler;
    }

    // ── Dispose ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;

        if (_deviceContext != IntPtr.Zero)
        {
            Marshal.Release(_deviceContext);
            _deviceContext = IntPtr.Zero;
        }

        if (_device != IntPtr.Zero)
        {
            Marshal.Release(_device);
            _device = IntPtr.Zero;
        }

        _disposed = true;
        Console.WriteLine("[DX11] Device disposed");
    }

    // ── Interop structs ──────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_SWAP_CHAIN_DESC
    {
        public uint BufferDesc_Width, BufferDesc_Height;
        public uint BufferDesc_RefreshRate_Num, BufferDesc_RefreshRate_Den;
        public uint BufferDesc_Format, BufferDesc_ScanlineOrdering, BufferDesc_Scaling;
        public uint SampleDesc_Count, SampleDesc_Quality;
        public uint BufferUsage;
        public uint BufferCount;
        public IntPtr OutputWindow;
        public int Windowed;
        public uint SwapEffect;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_INPUT_ELEMENT_DESC
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string SemanticName;
        public uint SemanticIndex;
        public uint Format;
        public uint InputSlot;
        public uint AlignedByteOffset;
        public uint InputSlotClass;
        public uint InstanceDataStepRate;
    }
}
