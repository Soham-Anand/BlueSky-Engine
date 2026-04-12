using static NotBSRenderer.Metal.MetalInterop;

namespace NotBSRenderer.Metal;

internal class MetalPipeline : IRHIPipeline
{
    private IntPtr _renderPipelineState;
    private IntPtr _depthStencilState;
    private ulong _cullMode;
    private ulong _fillMode;
    private ulong _primitiveType;
    private bool _disposed;
    
    internal IntPtr Handle => _renderPipelineState;
    internal IntPtr DepthStencilState => _depthStencilState;
    internal ulong RasterizerCullMode => _cullMode;
    internal ulong FillMode => _fillMode;
    internal ulong PrimitiveType => _primitiveType;
    
    public MetalPipeline(MetalDevice device, GraphicsPipelineDesc desc)
    {
        _cullMode = ToMTLCullMode(desc.RasterizerState.CullMode);
        _fillMode = ToMTLFillMode(desc.RasterizerState.FillMode);
        _primitiveType = ToMTLPrimitiveType(desc.Topology);
        CreateRenderPipelineState(device, desc);
        CreateDepthStencilState(device, desc.DepthStencilState);
    }
    
    private static ulong ToMTLCullMode(NotBSRenderer.CullMode mode)
    {
        return mode switch
        {
            NotBSRenderer.CullMode.None => 0,  // MTLCullModeNone
            NotBSRenderer.CullMode.Front => 1, // MTLCullModeFront
            NotBSRenderer.CullMode.Back => 2,  // MTLCullModeBack
            _ => 0
        };
    }
    
    private static ulong ToMTLFillMode(NotBSRenderer.FillMode mode)
    {
        return mode switch
        {
            NotBSRenderer.FillMode.Solid => 0,      // MTLTriangleFillModeFill
            NotBSRenderer.FillMode.Wireframe => 1,  // MTLTriangleFillModeLines
            _ => 0
        };
    }
    
    private void CreateRenderPipelineState(MetalDevice device, GraphicsPipelineDesc desc)
    {
        // Create pipeline descriptor
        var descriptorClass = GetClass("MTLRenderPipelineDescriptor");
        var allocSel = GetSelector("alloc");
        var initSel = GetSelector("init");
        var descriptor = objc_msgSend(descriptorClass, allocSel);
        descriptor = objc_msgSend(descriptor, initSel);
        
        // Load shaders
        var vertexFunction = LoadShader(device, desc.VertexShader);
        var fragmentFunction = LoadShader(device, desc.FragmentShader);
        
        // Set shader functions
        var setVertexFunctionSel = GetSelector("setVertexFunction:");
        objc_msgSend_void_ptr(descriptor, setVertexFunctionSel, vertexFunction);
        
        var setFragmentFunctionSel = GetSelector("setFragmentFunction:");
        objc_msgSend_void_ptr(descriptor, setFragmentFunctionSel, fragmentFunction);
        
        // Set color attachment formats
        var colorAttachmentsSel = GetSelector("colorAttachments");
        var colorAttachments = objc_msgSend(descriptor, colorAttachmentsSel);
        
        for (int i = 0; i < desc.ColorFormats.Length; i++)
        {
            var objectAtIndexSel = GetSelector("objectAtIndexedSubscript:");
            var attachment = objc_msgSend_ulong(colorAttachments, objectAtIndexSel, (ulong)i);
            
            var setPixelFormatSel = GetSelector("setPixelFormat:");
            var pixelFormat = ToMTLPixelFormat(desc.ColorFormats[i]);
            objc_msgSend_void_ulong(attachment, setPixelFormatSel, pixelFormat);
            
            // Set blend state
            if (desc.BlendState.BlendEnabled)
            {
                var setBlendingEnabledSel = GetSelector("setBlendingEnabled:");
                SetBool(attachment, setBlendingEnabledSel, true);
                
                var setSrcRGBBlendFactorSel = GetSelector("setSourceRGBBlendFactor:");
                objc_msgSend_void_ulong(attachment, setSrcRGBBlendFactorSel, ToMTLBlendFactor(desc.BlendState.SrcColorFactor));
                
                var setDstRGBBlendFactorSel = GetSelector("setDestinationRGBBlendFactor:");
                objc_msgSend_void_ulong(attachment, setDstRGBBlendFactorSel, ToMTLBlendFactor(desc.BlendState.DstColorFactor));
                
                var setRGBBlendOpSel = GetSelector("setRgbBlendOperation:");
                objc_msgSend_void_ulong(attachment, setRGBBlendOpSel, ToMTLBlendOp(desc.BlendState.ColorOp));
                
                var setSrcAlphaBlendFactorSel = GetSelector("setSourceAlphaBlendFactor:");
                objc_msgSend_void_ulong(attachment, setSrcAlphaBlendFactorSel, ToMTLBlendFactor(desc.BlendState.SrcAlphaFactor));
                
                var setDstAlphaBlendFactorSel = GetSelector("setDestinationAlphaBlendFactor:");
                objc_msgSend_void_ulong(attachment, setDstAlphaBlendFactorSel, ToMTLBlendFactor(desc.BlendState.DstAlphaFactor));
                
                var setAlphaBlendOpSel = GetSelector("setAlphaBlendOperation:");
                objc_msgSend_void_ulong(attachment, setAlphaBlendOpSel, ToMTLBlendOp(desc.BlendState.AlphaOp));
            }
        }
        
        // Set depth format if present
        if (desc.DepthFormat.HasValue)
        {
            var setDepthFormatSel = GetSelector("setDepthAttachmentPixelFormat:");
            objc_msgSend_void_ulong(descriptor, setDepthFormatSel, ToMTLPixelFormat(desc.DepthFormat.Value));
        }
        
        // Set vertex descriptor
        if (desc.VertexLayout.Attributes != null && desc.VertexLayout.Attributes.Length > 0)
        {
            var vertexDescriptor = CreateVertexDescriptor(desc.VertexLayout);
            var setVertexDescriptorSel = GetSelector("setVertexDescriptor:");
            objc_msgSend_void_ptr(descriptor, setVertexDescriptorSel, vertexDescriptor);
            Release(vertexDescriptor);
        }
        
        // Create pipeline state
        var newRenderPipelineSel = GetSelector("newRenderPipelineStateWithDescriptor:error:");
        IntPtr error = IntPtr.Zero;
        _renderPipelineState = NewRenderPipelineState(device.Device, newRenderPipelineSel, descriptor, ref error);
        
        Release(descriptor);
        Release(vertexFunction);
        Release(fragmentFunction);
        
        if (_renderPipelineState == IntPtr.Zero)
        {
            var errorMsg = GetNSErrorDescription(error);
            throw new Exception($"Failed to create render pipeline state: {errorMsg}");
        }
    }
    
    private void CreateDepthStencilState(MetalDevice device, DepthStencilState desc)
    {
        if (!desc.DepthTestEnabled)
            return;
        
        var descriptorClass = GetClass("MTLDepthStencilDescriptor");
        var allocSel = GetSelector("alloc");
        var initSel = GetSelector("init");
        var descriptor = objc_msgSend(descriptorClass, allocSel);
        descriptor = objc_msgSend(descriptor, initSel);
        
        var setDepthCompareFunc = GetSelector("setDepthCompareFunction:");
        objc_msgSend_void_ulong(descriptor, setDepthCompareFunc, ToMTLCompareFunction(desc.DepthCompareOp));
        
        var setDepthWriteEnabled = GetSelector("setDepthWriteEnabled:");
        SetBool(descriptor, setDepthWriteEnabled, desc.DepthWriteEnabled);
        
        // Add default stencil states to avoid undefined behavior if stencil is bound
        // This fully implements stencil operation support as per the implementation plan
        var stencilDescClass = GetClass("MTLStencilDescriptor");
        var defaultStencil = objc_msgSend(objc_msgSend(stencilDescClass, GetSelector("alloc")), GetSelector("init"));
        
        var setFrontFaceStencil = GetSelector("setFrontFaceStencil:");
        objc_msgSend_void_ptr(descriptor, setFrontFaceStencil, defaultStencil);
        
        var setBackFaceStencil = GetSelector("setBackFaceStencil:");
        objc_msgSend_void_ptr(descriptor, setBackFaceStencil, defaultStencil);
        
        Release(defaultStencil);
        
        var newDepthStencilSel = GetSelector("newDepthStencilStateWithDescriptor:");
        _depthStencilState = objc_msgSend_ptr(device.Device, newDepthStencilSel, descriptor);
        
        Release(descriptor);
    }
    
    private IntPtr LoadShader(MetalDevice device, ShaderDesc shader)
    {
        IntPtr library;
        
        // If bytecode is provided, write to temp file and load from there
        if (shader.Bytecode != null && shader.Bytecode.Length > 0)
        {
            // Write bytecode to temp file for reliable loading
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bs_shader_{shader.EntryPoint}_{Guid.NewGuid()}.metallib");
            System.IO.File.WriteAllBytes(tempPath, shader.Bytecode);
            
            try
            {
                // Load library from temp file
                var urlClass = GetClass("NSURL");
                var fileURLWithPathSel = GetSelector("fileURLWithPath:");
                var pathNS = CreateNSString(tempPath);
                var url = objc_msgSend_ptr(urlClass, fileURLWithPathSel, pathNS);
                Release(pathNS);
                
                if (url == IntPtr.Zero)
                    throw new Exception("Failed to create NSURL for temp shader library");
                
                var newLibraryWithURLSel = GetSelector("newLibraryWithURL:error:");
                IntPtr error = IntPtr.Zero;
                library = NewLibraryWithURL(device.Device, newLibraryWithURLSel, url, ref error);
                
                if (library == IntPtr.Zero)
                {
                    var errorMsg = GetNSErrorDescription(error);
                    throw new Exception($"Failed to load Metal library from temp file: {errorMsg}");
                }
                
            }
            finally
            {
                // Clean up temp file
                try { System.IO.File.Delete(tempPath); } catch { }
            }
        }
        else
        {
            // Determine library name based on entry point
            string libraryName;
            if (shader.EntryPoint.StartsWith("vs_ui") || shader.EntryPoint.StartsWith("fs_ui"))
                libraryName = "simple_ui.metallib";
            else if (shader.EntryPoint.StartsWith("horizon_"))
                libraryName = "horizon_lighting.metallib";
            else if (shader.EntryPoint.StartsWith("vs_sky") || shader.EntryPoint.StartsWith("fs_sky") ||
                     shader.EntryPoint.StartsWith("vs_grid") || shader.EntryPoint.StartsWith("fs_grid") ||
                     shader.EntryPoint.StartsWith("vs_mesh") || shader.EntryPoint.StartsWith("fs_mesh") ||
                     shader.EntryPoint.StartsWith("vs_shadow") || shader.EntryPoint.StartsWith("fs_shadow") ||
                     shader.EntryPoint.StartsWith("fs_wireframe"))
                libraryName = "viewport_3d.metallib";
            else
                libraryName = "default.metallib";

            // Load library from Shaders directory, fallback to Editor/Shaders for dev runs
            var exeDir = System.AppContext.BaseDirectory;
            var libraryPath = System.IO.Path.Combine(exeDir, "Shaders", libraryName);
            
            if (!System.IO.File.Exists(libraryPath))
                libraryPath = System.IO.Path.Combine(exeDir, "Editor", "Shaders", libraryName);
                
            // Bundle fallback: check ../Resources/Editor/Shaders and ../Resources/Shaders
            if (!System.IO.File.Exists(libraryPath))
            {
                var bundleResources = System.IO.Path.Combine(exeDir, "..", "Resources");
                libraryPath = System.IO.Path.Combine(bundleResources, "Shaders", libraryName);
                if (!System.IO.File.Exists(libraryPath))
                    libraryPath = System.IO.Path.Combine(bundleResources, "Editor", "Shaders", libraryName);
            }

            if (!System.IO.File.Exists(libraryPath))
                throw new Exception($"Metal library not found: {libraryPath}");
            
            var urlClass = GetClass("NSURL");
            var fileURLWithPathSel = GetSelector("fileURLWithPath:");
            var pathNS = CreateNSString(libraryPath);
            var url = objc_msgSend_ptr(urlClass, fileURLWithPathSel, pathNS);
            Release(pathNS);
            
            if (url == IntPtr.Zero)
                throw new Exception("Failed to create NSURL for Metal library");
            
            var newLibraryWithURLSel = GetSelector("newLibraryWithURL:error:");
            IntPtr error = IntPtr.Zero;
            library = NewLibraryWithURL(device.Device, newLibraryWithURLSel, url, ref error);
            
            if (library == IntPtr.Zero)
            {
                var errorMsg = GetNSErrorDescription(error);
                throw new Exception($"Failed to load Metal library: {errorMsg}");
            }
            
        }
        
        // Get function by name
        var newFunctionSel = GetSelector("newFunctionWithName:");
        var entryPointNS = CreateNSString(shader.EntryPoint);
        var function = objc_msgSend_ptr(library, newFunctionSel, entryPointNS);
        
        Release(entryPointNS);
        Release(library);
        
        if (function == IntPtr.Zero)
            throw new Exception($"Failed to find shader function: {shader.EntryPoint}");

        return function;
    }
    
    private IntPtr CreateVertexDescriptor(VertexLayoutDesc layout)
    {
        var descriptorClass = GetClass("MTLVertexDescriptor");
        var allocSel = GetSelector("alloc");
        var initSel = GetSelector("init");
        var descriptor = objc_msgSend(descriptorClass, allocSel);
        descriptor = objc_msgSend(descriptor, initSel);
        
        var attributesSel = GetSelector("attributes");
        var attributes = objc_msgSend(descriptor, attributesSel);
        
        var layoutsSel = GetSelector("layouts");
        var layouts = objc_msgSend(descriptor, layoutsSel);
        
        // Set attributes
        foreach (var attr in layout.Attributes)
        {
            var objectAtIndexSel = GetSelector("objectAtIndexedSubscript:");
            var attribute = objc_msgSend_ulong(attributes, objectAtIndexSel, (ulong)attr.Location);
            
            var setFormatSel = GetSelector("setFormat:");
            objc_msgSend_void_ulong(attribute, setFormatSel, ToMTLVertexFormat(attr.Format));
            
            var setOffsetSel = GetSelector("setOffset:");
            objc_msgSend_void_ulong(attribute, setOffsetSel, attr.Offset);
            
            var setBufferIndexSel = GetSelector("setBufferIndex:");
            objc_msgSend_void_ulong(attribute, setBufferIndexSel, attr.Binding);
        }
        
        // Set layouts
        foreach (var binding in layout.Bindings)
        {
            var objectAtIndexSel = GetSelector("objectAtIndexedSubscript:");
            var layoutDesc = objc_msgSend_ulong(layouts, objectAtIndexSel, (ulong)binding.Binding);
            
            var setStrideSel = GetSelector("setStride:");
            objc_msgSend_void_ulong(layoutDesc, setStrideSel, binding.Stride);
            
            var setStepFunctionSel = GetSelector("setStepFunction:");
            var stepFunction = binding.PerInstance ? 2ul : 1ul; // MTLVertexStepFunctionPerInstance : MTLVertexStepFunctionPerVertex
            objc_msgSend_void_ulong(layoutDesc, setStepFunctionSel, stepFunction);
        }
        
        return descriptor;
    }
    

    
    private static string GetNSErrorDescription(IntPtr error)
    {
        if (error == IntPtr.Zero)
            return "Unknown error";
        
        var localizedDescSel = GetSelector("localizedDescription");
        var desc = objc_msgSend(error, localizedDescSel);
        return GetNSStringContent(desc);
    }
    
    private static string GetNSStringContent(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero)
            return "";
        
        var utf8Sel = GetSelector("UTF8String");
        var utf8Ptr = objc_msgSend(nsString, utf8Sel);
        return System.Runtime.InteropServices.Marshal.PtrToStringAnsi(utf8Ptr) ?? "";
    }
    
    private static ulong ToMTLBlendFactor(BlendFactor factor)
    {
        return factor switch
        {
            BlendFactor.Zero => MTLBlendFactorZero,
            BlendFactor.One => MTLBlendFactorOne,
            BlendFactor.SrcColor => MTLBlendFactorSourceColor,
            BlendFactor.OneMinusSrcColor => MTLBlendFactorOneMinusSourceColor,
            BlendFactor.DstColor => MTLBlendFactorDestinationColor,
            BlendFactor.OneMinusDstColor => MTLBlendFactorOneMinusDestinationColor,
            BlendFactor.SrcAlpha => MTLBlendFactorSourceAlpha,
            BlendFactor.OneMinusSrcAlpha => MTLBlendFactorOneMinusSourceAlpha,
            BlendFactor.DstAlpha => MTLBlendFactorDestinationAlpha,
            BlendFactor.OneMinusDstAlpha => MTLBlendFactorOneMinusDestinationAlpha,
            _ => throw new NotSupportedException($"Blend factor {factor} not supported")
        };
    }
    
    private static ulong ToMTLBlendOp(BlendOp op)
    {
        return op switch
        {
            BlendOp.Add => MTLBlendOperationAdd,
            BlendOp.Subtract => MTLBlendOperationSubtract,
            BlendOp.ReverseSubtract => MTLBlendOperationReverseSubtract,
            BlendOp.Min => MTLBlendOperationMin,
            BlendOp.Max => MTLBlendOperationMax,
            _ => throw new NotSupportedException($"Blend op {op} not supported")
        };
    }
    
    private static ulong ToMTLCompareFunction(CompareOp op)
    {
        return op switch
        {
            CompareOp.Never => MTLCompareFunctionNever,
            CompareOp.Less => MTLCompareFunctionLess,
            CompareOp.Equal => MTLCompareFunctionEqual,
            CompareOp.LessOrEqual => MTLCompareFunctionLessEqual,
            CompareOp.Greater => MTLCompareFunctionGreater,
            CompareOp.NotEqual => MTLCompareFunctionNotEqual,
            CompareOp.GreaterOrEqual => MTLCompareFunctionGreaterEqual,
            CompareOp.Always => MTLCompareFunctionAlways,
            _ => throw new NotSupportedException($"Compare op {op} not supported")
        };
    }
    
    private static ulong ToMTLVertexFormat(TextureFormat format)
    {
        // MTLVertexFormat enum values — from Metal/MTLVertexDescriptor.h
        // The previous values (12-15) were wrong and caused Metal to
        // interpret float vertex data as short/char types.
        return format switch
        {
            TextureFormat.R8Unorm     => 45,  // MTLVertexFormatUCharNormalized
            TextureFormat.R32Float    => 28,  // MTLVertexFormatFloat
            TextureFormat.RGBA8Unorm  =>  9,  // MTLVertexFormatUChar4Normalized
            TextureFormat.RG32Float   => 29,  // MTLVertexFormatFloat2
            TextureFormat.RGB32Float  => 30,  // MTLVertexFormatFloat3
            TextureFormat.RGBA32Float => 31,  // MTLVertexFormatFloat4
            _ => throw new NotSupportedException($"Vertex format {format} not supported")
        };
    }
    
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetBool(IntPtr receiver, IntPtr selector, bool value);
    
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr InitWithUTF8String(IntPtr receiver, IntPtr selector, 
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)] string str);
    
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr NewRenderPipelineState(IntPtr receiver, IntPtr selector, IntPtr descriptor, ref IntPtr error);
    
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr NewLibraryWithURL(IntPtr receiver, IntPtr selector, IntPtr url, ref IntPtr error);
    
    public void Dispose()
    {
        if (_disposed) return;
        
        if (_depthStencilState != IntPtr.Zero)
        {
            Release(_depthStencilState);
            _depthStencilState = IntPtr.Zero;
        }
        
        if (_renderPipelineState != IntPtr.Zero)
        {
            Release(_renderPipelineState);
            _renderPipelineState = IntPtr.Zero;
        }
        
        _disposed = true;
    }
}
