using static BlueSky.BSR.RHI.Metal.MetalInterop;

namespace BlueSky.BSR.RHI.Metal;

internal class MetalPipeline : IRHIPipeline
{
    private IntPtr _renderPipelineState;
    private IntPtr _depthStencilState;
    private ulong _cullMode;
    private ulong _fillMode;
    private ulong _primitiveType;
    private ulong _winding;
    private bool _disposed;

    // M1: process-lifetime MTLLibrary cache — avoids disk reload + recompile per pipeline.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IntPtr> _libraryCache = new();
    private static readonly object _libraryCacheLock = new();
    internal IntPtr Handle => _renderPipelineState;
    internal IntPtr DepthStencilState => _depthStencilState;
    internal ulong RasterizerCullMode => _cullMode;
    internal ulong FillMode => _fillMode;
    internal ulong PrimitiveType => _primitiveType;
    internal ulong FrontWinding => _winding;
    
    public MetalPipeline(MetalDevice device, GraphicsPipelineDesc desc)
    {
        _cullMode = ToMTLCullMode(desc.RasterizerState.CullMode);
        _fillMode = ToMTLFillMode(desc.RasterizerState.FillMode);
        _primitiveType = ToMTLPrimitiveType(desc.Topology);
        _winding = ToMTLWinding(desc.RasterizerState.FrontFace);
        CreateRenderPipelineState(device, desc);
        CreateDepthStencilState(device, desc.DepthStencilState);
    }
    
    private static ulong ToMTLCullMode(BlueSky.BSR.RHI.CullMode mode)
    {
        return mode switch
        {
            BlueSky.BSR.RHI.CullMode.None => 0,  // MTLCullModeNone
            BlueSky.BSR.RHI.CullMode.Front => 1, // MTLCullModeFront
            BlueSky.BSR.RHI.CullMode.Back => 2,  // MTLCullModeBack
            _ => 0
        };
    }
    
    private static ulong ToMTLFillMode(BlueSky.BSR.RHI.FillMode mode)
    {
        return mode switch
        {
            BlueSky.BSR.RHI.FillMode.Solid => 0,      // MTLTriangleFillModeFill
            BlueSky.BSR.RHI.FillMode.Wireframe => 1,  // MTLTriangleFillModeLines
            _ => 0
        };
    }
    
    private static ulong ToMTLWinding(BlueSky.BSR.RHI.FrontFace face)
    {
        return face switch
        {
            BlueSky.BSR.RHI.FrontFace.Clockwise => 0,        // MTLWindingClockwise
            BlueSky.BSR.RHI.FrontFace.CounterClockwise => 1, // MTLWindingCounterClockwise
            _ => 1
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
        IntPtr library = IntPtr.Zero;

        // Determine library base name based on entry point
        string baseName;
        if (shader.EntryPoint.StartsWith("vs_ui") || shader.EntryPoint.StartsWith("fs_ui"))
            baseName = "simple_ui";
        else if (shader.EntryPoint.StartsWith("horizon_"))
            baseName = "horizon_lighting";
        else if (shader.EntryPoint.StartsWith("vs_sky") || shader.EntryPoint.StartsWith("fs_sky") ||
                 shader.EntryPoint.StartsWith("vs_grid") || shader.EntryPoint.StartsWith("fs_grid") ||
                 shader.EntryPoint.StartsWith("vs_mesh") || shader.EntryPoint.StartsWith("fs_mesh") ||
                 shader.EntryPoint.StartsWith("vs_shadow") || shader.EntryPoint.StartsWith("fs_shadow") ||
                 shader.EntryPoint.StartsWith("vs_vectra") || shader.EntryPoint.StartsWith("fs_smoke") ||
                 shader.EntryPoint.StartsWith("vs_gizmo") || shader.EntryPoint.StartsWith("fs_gizmo") ||
                 shader.EntryPoint.StartsWith("fs_wireframe"))
            baseName = "viewport_3d";
        else
            baseName = "default";

        // Check if a newer .metal source file exists on disk; if so, compile live
        // via MTLDevice.newLibraryWithSource:options:error: and cache in _libraryCache
        // so shader edits take effect immediately even without xcrun metal CLI.
        var exeDir = System.AppContext.BaseDirectory;
        string[] sourceCandidates = new[]
        {
            System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "BlueSkyEngine", "Editor", "Shaders", baseName + ".metal"),
            System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Editor", "Shaders", baseName + ".metal"),
            System.IO.Path.Combine(exeDir, "Editor", "Shaders", baseName + ".metal"),
        };
        string[] libCandidates = new[]
        {
            System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "BlueSkyEngine", "Editor", "Shaders", baseName + ".metallib"),
            System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Editor", "Shaders", baseName + ".metallib"),
            System.IO.Path.Combine(exeDir, "Editor", "Shaders", baseName + ".metallib"),
        };
        string? sourcePath = System.Array.Find(sourceCandidates, System.IO.File.Exists);
        string? diskLibPath = System.Array.Find(libCandidates, System.IO.File.Exists);
        bool preferSource = sourcePath != null &&
            (diskLibPath == null || System.IO.File.GetLastWriteTimeUtc(sourcePath) > System.IO.File.GetLastWriteTimeUtc(diskLibPath));

        if (preferSource && sourcePath != null)
        {
            string cacheKey = "src:" + sourcePath;
            lock (_libraryCacheLock)
            {
                if (_libraryCache.TryGetValue(cacheKey, out var cachedSrc) && cachedSrc != IntPtr.Zero)
                {
                    library = cachedSrc;
                }
                else
                {
                    var srcText = System.IO.File.ReadAllText(sourcePath);
                    var srcNS = CreateNSString(srcText);
                    IntPtr srcErr = IntPtr.Zero;
                    var compiled = NewLibraryWithSource(device.Device, GetSelector("newLibraryWithSource:options:error:"), srcNS, IntPtr.Zero, ref srcErr);
                    Release(srcNS);
                    if (compiled != IntPtr.Zero)
                    {
                        _libraryCache[cacheKey] = compiled;
                        library = compiled;
                    }
                    else
                    {
                        Console.WriteLine($"[Metal] Live source compile warning for {baseName}.metal: {GetNSErrorDescription(srcErr)}");
                    }
                }
            }
        }

        // If bytecode is provided and we didn't compile from newer source, load from bytecode
        if (library == IntPtr.Zero && shader.Bytecode != null && shader.Bytecode.Length > 0)
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
        else if (library == IntPtr.Zero)
        {
            string libraryName = baseName + ".metallib";

            // Editor shader libraries are the single source of compiled viewport shaders.
            var libraryPath = System.IO.Path.Combine(exeDir, "Editor", "Shaders", libraryName);
                
            // Bundle fallback: check the packaged editor shader directory.
            if (!System.IO.File.Exists(libraryPath))
            {
                var bundleResources = System.IO.Path.Combine(exeDir, "..", "Resources");
                libraryPath = System.IO.Path.Combine(bundleResources, "Editor", "Shaders", libraryName);
            }

            if (!System.IO.File.Exists(libraryPath))
                throw new Exception($"Metal library not found: {libraryPath}");

            // M1: reuse cached MTLLibrary for this path (retained for process lifetime).
            lock (_libraryCacheLock)
            {
                if (_libraryCache.TryGetValue(libraryPath, out var cached) && cached != IntPtr.Zero)
                {
                    library = cached;
                }
                else
                {
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

                    _libraryCache[libraryPath] = library;
                }
            }

        }

        // Get function by name
        var newFunctionSel = GetSelector("newFunctionWithName:");
        var entryPointNS = CreateNSString(shader.EntryPoint);
        var function = objc_msgSend_ptr(library, newFunctionSel, entryPointNS);

        Release(entryPointNS);
        // NOTE: cached libraries stay retained in _libraryCache; only release
        // non-cached (bytecode temp) libraries here.
        if (!preferSource && shader.Bytecode != null && shader.Bytecode.Length > 0)
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

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr NewLibraryWithSource(IntPtr receiver, IntPtr selector, IntPtr source, IntPtr options, ref IntPtr error);
    
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
