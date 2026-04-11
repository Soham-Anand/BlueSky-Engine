using System.Runtime.InteropServices;
using static NotBSRenderer.DirectX9.D3D9RenderState;

namespace NotBSRenderer.DirectX9;

internal class D3D9Pipeline : IRHIPipeline
{
    private readonly D3D9Device _device;
    private readonly GraphicsPipelineDesc _desc;
    private IntPtr _vertexDeclaration;
    
    private readonly SetRenderStateDelegate _setRenderState;
    private readonly CreateVertexDeclarationDelegate _createVertexDeclaration;
    private readonly SetVertexDeclarationDelegate _setVertexDeclaration;
    private readonly CreateVertexShaderDelegate _createVertexShader;
    private readonly SetVertexShaderDelegate _setVertexShader;
    private readonly CreatePixelShaderDelegate _createPixelShader;
    private readonly SetPixelShaderDelegate _setPixelShader;
    
    private IntPtr _vertexShader;
    private IntPtr _pixelShader;
    
    internal PrimitiveTopology Topology => _desc.Topology;
    
    public D3D9Pipeline(D3D9Device device, GraphicsPipelineDesc desc)
    {
        _device = device;
        _desc = desc;
        
        var dev = _device.Device;
        _setRenderState = D3D9ComHelper.GetComMethod<SetRenderStateDelegate>(dev, 57);
        _createVertexDeclaration = D3D9ComHelper.GetComMethod<CreateVertexDeclarationDelegate>(dev, 86);
        _setVertexDeclaration = D3D9ComHelper.GetComMethod<SetVertexDeclarationDelegate>(dev, 87);
        _createVertexShader = D3D9ComHelper.GetComMethod<CreateVertexShaderDelegate>(dev, 91);
        _setVertexShader = D3D9ComHelper.GetComMethod<SetVertexShaderDelegate>(dev, 92);
        _createPixelShader = D3D9ComHelper.GetComMethod<CreatePixelShaderDelegate>(dev, 106);
        _setPixelShader = D3D9ComHelper.GetComMethod<SetPixelShaderDelegate>(dev, 107);
        
        // Create vertex declaration
        CreateVertexDeclaration();
        
        // Create Shaders
        if (_desc.VertexShader.Bytecode != null && _desc.VertexShader.Bytecode.Length > 0)
        {
            var hr = _createVertexShader(dev, _desc.VertexShader.Bytecode, out _vertexShader);
            if (hr != 0) throw new Exception($"Failed to create Vertex Shader, HRESULT: {hr:X}");
        }
        
        if (_desc.FragmentShader.Bytecode != null && _desc.FragmentShader.Bytecode.Length > 0)
        {
            var hr = _createPixelShader(dev, _desc.FragmentShader.Bytecode, out _pixelShader);
            if (hr != 0) throw new Exception($"Failed to create Pixel Shader, HRESULT: {hr:X}");
        }
        
        Console.WriteLine("[DX9] Pipeline created");
    }
    
    private void CreateVertexDeclaration()
    {
        var elements = new List<D3D9Interop.D3DVERTEXELEMENT9>();
        var usageCounts = new Dictionary<byte, byte>();
        
        foreach (var attr in _desc.VertexLayout.Attributes)
        {
            var binding = _desc.VertexLayout.Bindings.First(b => b.Binding == attr.Binding);
            var usage = LocationToUsage(attr.Location);
            
            if (!usageCounts.ContainsKey(usage))
                usageCounts[usage] = 0;
                
            var element = new D3D9Interop.D3DVERTEXELEMENT9
            {
                Stream = (ushort)attr.Binding,
                Offset = (ushort)attr.Offset,
                Type = FormatToDeclType(attr.Format),
                Method = 0, // D3DDECLMETHOD_DEFAULT
                Usage = usage,
                UsageIndex = usageCounts[usage]++
            };
            elements.Add(element);
        }
        
        elements.Add(D3D9Interop.D3DDECL_END);
        
        var hr = _createVertexDeclaration(_device.Device, elements.ToArray(), out _vertexDeclaration);
        if (hr != 0)
            throw new Exception($"Failed to create vertex declaration, HRESULT: {hr:X}");
    }
    
    private static byte FormatToDeclType(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.R32Float => D3D9Interop.D3DDECLTYPE_FLOAT1,
            TextureFormat.RG32Float => D3D9Interop.D3DDECLTYPE_FLOAT2,
            TextureFormat.RGB32Float => D3D9Interop.D3DDECLTYPE_FLOAT3,
            TextureFormat.RGBA32Float => D3D9Interop.D3DDECLTYPE_FLOAT4,
            TextureFormat.RGBA8Unorm => 4, // D3DDECLTYPE_D3DCOLOR
            TextureFormat.BGRA8Unorm => 4, // D3DDECLTYPE_D3DCOLOR
            TextureFormat.R8Unorm => D3D9Interop.D3DDECLTYPE_FLOAT1, // Single float component
            _ => throw new NotSupportedException($"Format {format} not supported for vertex attributes")
        };
    }
    
    private static byte LocationToUsage(uint location)
    {
        return location switch
        {
            0 => D3D9Interop.D3DDECLUSAGE_POSITION,
            1 => D3D9Interop.D3DDECLUSAGE_COLOR,
            2 => D3D9Interop.D3DDECLUSAGE_TEXCOORD,
            3 => D3D9Interop.D3DDECLUSAGE_TEXCOORD,
            _ => D3D9Interop.D3DDECLUSAGE_TEXCOORD
        };
    }
    
    internal VertexBinding[] GetVertexBindings()
    {
        return _desc.VertexLayout.Bindings;
    }
    
    internal void Apply(IntPtr device)
    {
        // Set vertex declaration
        var hr = _setVertexDeclaration(device, _vertexDeclaration);
        if (hr != 0)
            Console.WriteLine($"[DX9] WARNING: SetVertexDeclaration failed with HRESULT: 0x{hr:X8}");
        
        // Use programmable or fixed-function pipeline
        hr = _setVertexShader(device, _vertexShader);
        if (hr != 0 && _vertexShader != IntPtr.Zero)
            Console.WriteLine($"[DX9] WARNING: SetVertexShader failed with HRESULT: 0x{hr:X8}");
            
        hr = _setPixelShader(device, _pixelShader);
        if (hr != 0 && _pixelShader != IntPtr.Zero)
            Console.WriteLine($"[DX9] WARNING: SetPixelShader failed with HRESULT: 0x{hr:X8}");
        
        // Enable lighting off for fixed-function fallback
        _setRenderState(device, D3D9RenderState.D3DRS_LIGHTING, 0);
        
        // Apply blend state
        _setRenderState(device, D3D9RenderState.D3DRS_ALPHABLENDENABLE, _desc.BlendState.BlendEnabled ? 1u : 0u);
        
        if (_desc.BlendState.BlendEnabled)
        {
            // Color blend
            _setRenderState(device, D3D9RenderState.D3DRS_SRCBLEND, ToD3DBlend(_desc.BlendState.SrcColorFactor));
            _setRenderState(device, D3D9RenderState.D3DRS_DESTBLEND, ToD3DBlend(_desc.BlendState.DstColorFactor));
            _setRenderState(device, D3D9RenderState.D3DRS_BLENDOP, ToD3DBlendOp(_desc.BlendState.ColorOp));
            
            // Alpha blend
            _setRenderState(device, D3D9RenderState.D3DRS_SEPARATEALPHABLENDENABLE, 1);
            _setRenderState(device, D3D9RenderState.D3DRS_SRCBLENDALPHA, ToD3DBlend(_desc.BlendState.SrcAlphaFactor));
            _setRenderState(device, D3D9RenderState.D3DRS_DESTBLENDALPHA, ToD3DBlend(_desc.BlendState.DstAlphaFactor));
            _setRenderState(device, D3D9RenderState.D3DRS_BLENDOPALPHA, ToD3DBlendOp(_desc.BlendState.AlphaOp));
        }
        else
        {
            _setRenderState(device, D3D9RenderState.D3DRS_SEPARATEALPHABLENDENABLE, 0);
        }
        
        // Apply depth stencil state
        _setRenderState(device, D3D9RenderState.D3DRS_ZENABLE, _desc.DepthStencilState.DepthTestEnabled ? 1u : 0u);
        _setRenderState(device, D3D9RenderState.D3DRS_ZWRITEENABLE, _desc.DepthStencilState.DepthWriteEnabled ? 1u : 0u);
        _setRenderState(device, D3D9RenderState.D3DRS_ZFUNC, ToD3DCmp(_desc.DepthStencilState.DepthCompareOp));
        
        // Apply rasterizer state
        _setRenderState(device, D3D9RenderState.D3DRS_CULLMODE, ToD3DCull(_desc.RasterizerState.CullMode));
    }
    
    private static uint ToD3DBlend(BlendFactor factor)
    {
        return factor switch
        {
            BlendFactor.Zero => D3D9Interop.D3DBLEND_ZERO,
            BlendFactor.One => D3D9Interop.D3DBLEND_ONE,
            BlendFactor.SrcColor => D3D9Interop.D3DBLEND_SRCCOLOR,
            BlendFactor.OneMinusSrcColor => D3D9Interop.D3DBLEND_INVSRCCOLOR,
            BlendFactor.SrcAlpha => D3D9Interop.D3DBLEND_SRCALPHA,
            BlendFactor.OneMinusSrcAlpha => D3D9Interop.D3DBLEND_INVSRCALPHA,
            BlendFactor.DstAlpha => D3D9Interop.D3DBLEND_DESTALPHA,
            BlendFactor.OneMinusDstAlpha => D3D9Interop.D3DBLEND_INVDESTALPHA,
            _ => D3D9Interop.D3DBLEND_ONE
        };
    }
    
    private static uint ToD3DBlendOp(BlendOp op)
    {
        return op switch
        {
            BlendOp.Add => D3D9Interop.D3DBLENDOP_ADD,
            BlendOp.Subtract => D3D9Interop.D3DBLENDOP_SUBTRACT,
            BlendOp.ReverseSubtract => D3D9Interop.D3DBLENDOP_REVSUBTRACT,
            BlendOp.Min => D3D9Interop.D3DBLENDOP_MIN,
            BlendOp.Max => D3D9Interop.D3DBLENDOP_MAX,
            _ => D3D9Interop.D3DBLENDOP_ADD
        };
    }
    
    private static uint ToD3DCmp(CompareOp op)
    {
        return op switch
        {
            CompareOp.Never => D3D9Interop.D3DCMP_NEVER,
            CompareOp.Less => D3D9Interop.D3DCMP_LESS,
            CompareOp.Equal => D3D9Interop.D3DCMP_EQUAL,
            CompareOp.LessOrEqual => D3D9Interop.D3DCMP_LESSEQUAL,
            CompareOp.Greater => D3D9Interop.D3DCMP_GREATER,
            CompareOp.NotEqual => D3D9Interop.D3DCMP_NOTEQUAL,
            CompareOp.GreaterOrEqual => D3D9Interop.D3DCMP_GREATEREQUAL,
            CompareOp.Always => D3D9Interop.D3DCMP_ALWAYS,
            _ => D3D9Interop.D3DCMP_LESS
        };
    }
    
    private static uint ToD3DCull(CullMode mode)
    {
        return mode switch
        {
            CullMode.None => D3D9Interop.D3DCULL_NONE,
            CullMode.Front => D3D9Interop.D3DCULL_CCW,
            CullMode.Back => D3D9Interop.D3DCULL_CW,
            _ => D3D9Interop.D3DCULL_NONE
        };
    }
    
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetRenderStateDelegate(IntPtr device, uint state, uint value);
    
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateVertexDeclarationDelegate(IntPtr device, D3D9Interop.D3DVERTEXELEMENT9[] elements, out IntPtr declaration);
    
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetVertexDeclarationDelegate(IntPtr device, IntPtr declaration);
    
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateVertexShaderDelegate(IntPtr device, byte[] byteCode, out IntPtr shader);
    
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetVertexShaderDelegate(IntPtr device, IntPtr shader);
    
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreatePixelShaderDelegate(IntPtr device, byte[] byteCode, out IntPtr shader);
    
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetPixelShaderDelegate(IntPtr device, IntPtr shader);
    
    public void Dispose()
    {
        if (_vertexDeclaration != IntPtr.Zero)
        {
            Marshal.Release(_vertexDeclaration);
            _vertexDeclaration = IntPtr.Zero;
        }
        
        if (_vertexShader != IntPtr.Zero)
        {
            Marshal.Release(_vertexShader);
            _vertexShader = IntPtr.Zero;
        }
        
        if (_pixelShader != IntPtr.Zero)
        {
            Marshal.Release(_pixelShader);
            _pixelShader = IntPtr.Zero;
        }
    }
}
