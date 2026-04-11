using System.Numerics;
using System.Runtime.InteropServices;
using static NotBSRenderer.DirectX9.D3D9Interop;
using static NotBSRenderer.DirectX9.D3D9RenderState;
using static NotBSRenderer.DirectX9.D3D9TransformState;

namespace NotBSRenderer.DirectX9;

public class D3D9CommandBuffer : IRHICommandBuffer
{
    private readonly D3D9Device _device;
    private D3D9Pipeline? _currentPipeline;
    private bool _disposed;
    
    private readonly BeginSceneDelegate _beginScene;
    private readonly EndSceneDelegate _endScene;
    private readonly ClearDelegate _clear;
    private readonly SetRenderTargetDelegate _setRenderTarget;
    private readonly SetViewportNativeDelegate _setViewport;
    private readonly SetScissorRectDelegate _setScissorRect;
    private readonly SetStreamSourceDelegate _setStreamSource;
    private readonly SetIndicesDelegate _setIndices;
    private readonly DrawPrimitiveDelegate _drawPrimitive;
    private readonly DrawIndexedPrimitiveDelegate _drawIndexedPrimitive;
    private readonly SetRenderStateDelegate _setRenderState;
    private readonly SetTransformDelegate _setTransform;
    private readonly SetTextureDelegate _setTexture;
    private readonly SetVertexShaderConstantFDelegate _setVertexShaderConstantF;
    private readonly SetVertexShaderConstantF_RawDelegate _setVertexShaderConstantF_Raw;
    private readonly SetPixelShaderConstantF_RawDelegate _setPixelShaderConstantF_Raw;
    
    internal D3D9CommandBuffer(D3D9Device device)
    {
        _device = device;
        var dev = _device.Device;
        _beginScene = D3D9ComHelper.GetComMethod<BeginSceneDelegate>(dev, 41);
        _endScene = D3D9ComHelper.GetComMethod<EndSceneDelegate>(dev, 42);
        _clear = D3D9ComHelper.GetComMethod<ClearDelegate>(dev, 43);
        _setRenderTarget = D3D9ComHelper.GetComMethod<SetRenderTargetDelegate>(dev, 37);
        _setViewport = D3D9ComHelper.GetComMethod<SetViewportNativeDelegate>(dev, 47);
        _setScissorRect = D3D9ComHelper.GetComMethod<SetScissorRectDelegate>(dev, 75);
        _setStreamSource = D3D9ComHelper.GetComMethod<SetStreamSourceDelegate>(dev, 100);
        _setIndices = D3D9ComHelper.GetComMethod<SetIndicesDelegate>(dev, 104);
        _drawPrimitive = D3D9ComHelper.GetComMethod<DrawPrimitiveDelegate>(dev, 81);
        _drawIndexedPrimitive = D3D9ComHelper.GetComMethod<DrawIndexedPrimitiveDelegate>(dev, 82);
        _setRenderState = D3D9ComHelper.GetComMethod<SetRenderStateDelegate>(dev, 57);
        _setTransform = D3D9ComHelper.GetComMethod<SetTransformDelegate>(dev, 44);
        _setTexture = D3D9ComHelper.GetComMethod<SetTextureDelegate>(dev, 65);
        _setVertexShaderConstantF = D3D9ComHelper.GetComMethod<SetVertexShaderConstantFDelegate>(dev, 94);
        _setVertexShaderConstantF_Raw = D3D9ComHelper.GetComMethod<SetVertexShaderConstantF_RawDelegate>(dev, 94);
        _setPixelShaderConstantF_Raw = D3D9ComHelper.GetComMethod<SetPixelShaderConstantF_RawDelegate>(dev, 109);
    }
    
    public void BeginRenderPass(IRHITexture renderTarget, ClearValue clearValue)
    {
        BeginRenderPass([renderTarget], null, clearValue);
    }
    
    public void BeginRenderPass(IRHITexture[] colorTargets, IRHITexture? depthTarget, ClearValue clearValue)
    {
        var device = _device.Device;
        
        // Set render target
        if (colorTargets.Length > 0 && colorTargets[0] is D3D9Texture dx9Texture)
        {
            var hr = _setRenderTarget(device, 0, dx9Texture.Surface);
            if (hr != 0)
                Console.WriteLine($"[DX9] WARNING: SetRenderTarget failed with HRESULT: 0x{hr:X8}");
        }
        
        // Clear
        uint clearFlags = 0;
        if (!clearValue.LoadInsteadOfClear)
        {
            clearFlags |= D3DCLEAR_TARGET;
            clearFlags |= D3DCLEAR_ZBUFFER | D3DCLEAR_STENCIL;
        }
        
        if (clearFlags != 0)
        {
            var color = ColorToD3D(clearValue.Color);
            var hr = _clear(device, 0, IntPtr.Zero, clearFlags, color, clearValue.Depth, clearValue.Stencil);
            if (hr != 0)
                Console.WriteLine($"[DX9] WARNING: Clear failed with HRESULT: 0x{hr:X8}");
        }
        
        // Begin scene
        var beginHr = _beginScene(device);
        if (beginHr != 0)
            Console.WriteLine($"[DX9] WARNING: BeginScene failed with HRESULT: 0x{beginHr:X8}");
    }
    
    public void EndRenderPass()
    {
        _endScene(_device.Device);
    }
    
    public void SetPipeline(IRHIPipeline pipeline)
    {
        if (pipeline is not D3D9Pipeline dx9Pipeline)
            throw new ArgumentException("Pipeline must be a DX9 pipeline");
        
        _currentPipeline = dx9Pipeline;
        _currentPipeline.Apply(_device.Device);
    }
    
    public void SetViewport(Viewport viewport)
    {
        var vp = new D3DVIEWPORT9
        {
            X = (uint)viewport.X,
            Y = (uint)viewport.Y,
            Width = (uint)viewport.Width,
            Height = (uint)viewport.Height,
            MinZ = viewport.MinDepth,
            MaxZ = viewport.MaxDepth
        };
        
        _setViewport(_device.Device, ref vp);
    }
    
    public void SetScissor(Scissor scissor)
    {
        // DX9 scissor test requires enabling render state
        _setRenderState(_device.Device, D3D9RenderState.D3DRS_SCISSORTESTENABLE, 1);
        
        var rect = new RECT
        {
            left = (int)scissor.X,
            top = (int)scissor.Y,
            right = (int)(scissor.X + scissor.Width),
            bottom = (int)(scissor.Y + scissor.Height)
        };
        
        var hr = _setScissorRect(_device.Device, ref rect);
        if (hr != 0)
            Console.WriteLine($"[DX9] WARNING: SetScissorRect failed with HRESULT: 0x{hr:X8}");
    }
    
    public void SetVertexBuffer(IRHIBuffer buffer, uint binding = 0, ulong offset = 0)
    {
        if (buffer is not D3D9Buffer dx9Buffer)
            throw new ArgumentException("Buffer must be a DX9 buffer");
        
        // Get stride from current pipeline
        uint stride = 0;
        if (_currentPipeline != null)
        {
            var bindings = _currentPipeline.GetVertexBindings();
            var bindingInfo = bindings.FirstOrDefault(b => b.Binding == binding);
            stride = bindingInfo.Stride;
        }
        
        _setStreamSource(_device.Device, binding, dx9Buffer.Handle, (uint)offset, stride);
    }
    
    public void SetIndexBuffer(IRHIBuffer buffer, IndexType indexType, ulong offset = 0)
    {
        if (buffer is not D3D9Buffer dx9Buffer)
            throw new ArgumentException("Buffer must be a DX9 buffer");
        
        _setIndices(_device.Device, dx9Buffer.Handle);
    }
    
    public unsafe void SetUniformBuffer(IRHIBuffer buffer, uint binding, uint set = 0)
    {
        if (buffer is not D3D9Buffer dx9Buffer || dx9Buffer.UniformData == null)
            throw new ArgumentException("Buffer must be a DX9 uniform buffer");
            
        fixed (byte* pData = dx9Buffer.UniformData)
        {
            // DX9 maps uniform buffers to shader constants (each constant is 4 floats = 16 bytes)
            uint vector4fCount = (uint)(dx9Buffer.UniformData.Length / 16);
            
            // Bind to both vertex and pixel shaders
            _setVertexShaderConstantF_Raw(_device.Device, binding, (IntPtr)pData, vector4fCount);
            _setPixelShaderConstantF_Raw(_device.Device, binding, (IntPtr)pData, vector4fCount);
        }
    }
    
    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        if (instanceCount != 1)
            throw new NotSupportedException("DX9 doesn't support instanced rendering");
        
        if (_currentPipeline == null)
            throw new InvalidOperationException("No pipeline bound");
        
        var primitiveType = D3D9Interop.ToD3DPrimitiveType(_currentPipeline.Topology);
        uint primitiveCount = GetPrimitiveCount(_currentPipeline.Topology, vertexCount);
        
        var hr = _drawPrimitive(_device.Device, primitiveType, firstVertex, primitiveCount);
        if (hr != 0)
            Console.WriteLine($"[DX9] WARNING: DrawPrimitive failed with HRESULT: 0x{hr:X8}");
    }
    
    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, 
        int vertexOffset = 0, uint firstInstance = 0)
    {
        if (instanceCount != 1)
            throw new NotSupportedException("DX9 doesn't support instanced rendering");
        
        if (_currentPipeline == null)
            throw new InvalidOperationException("No pipeline bound");
        
        var primitiveType = D3D9Interop.ToD3DPrimitiveType(_currentPipeline.Topology);
        uint primitiveCount = GetPrimitiveCount(_currentPipeline.Topology, indexCount);
        
        var hr = _drawIndexedPrimitive(_device.Device, primitiveType, vertexOffset, 0, 
            indexCount, firstIndex, primitiveCount);
        if (hr != 0)
            Console.WriteLine($"[DX9] WARNING: DrawIndexedPrimitive failed with HRESULT: 0x{hr:X8}");
    }
    
    private static uint GetPrimitiveCount(PrimitiveTopology topology, uint elementCount)
    {
        return topology switch
        {
            PrimitiveTopology.TriangleList => elementCount / 3,
            PrimitiveTopology.TriangleStrip => elementCount - 2,
            PrimitiveTopology.LineList => elementCount / 2,
            PrimitiveTopology.LineStrip => elementCount - 1,
            PrimitiveTopology.PointList => elementCount,
            _ => elementCount
        };
    }
    
    public void SetTransform(uint state, ref Matrix4x4 matrix)
    {
        var hr = _setTransform(_device.Device, state, ref matrix);
        if (hr != 0)
            Console.WriteLine($"[DX9] WARNING: SetTransform failed with HRESULT: 0x{hr:X8}");
    }

    public void SetTexture(IRHITexture texture, uint binding, uint set = 0)
    {
        if (texture is not D3D9Texture d3dTexture)
            throw new ArgumentException("Texture must be a D3D9 texture");
            
        _setTexture(_device.Device, binding, d3dTexture.Handle);
    }
    
    public void SetVertexUniforms(uint startRegister, ref Matrix4x4 matrix)
    {
        _setVertexShaderConstantF(_device.Device, startRegister, ref matrix, 4);
    }
    
    public unsafe void SetVertexUniforms(uint binding, ReadOnlySpan<byte> data)
    {
        fixed (byte* pData = data)
        {
            _setVertexShaderConstantF_Raw(_device.Device, binding, (IntPtr)pData, (uint)(data.Length / 16));
        }
    }

    public unsafe void SetFragmentUniforms(uint binding, ReadOnlySpan<byte> data)
    {
        fixed (byte* pData = data)
        {
            _setPixelShaderConstantF_Raw(_device.Device, binding, (IntPtr)pData, (uint)(data.Length / 16));
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
    
    private static uint ColorToD3D(Vector4 color)
    {
        byte a = (byte)(color.W * 255);
        byte r = (byte)(color.X * 255);
        byte g = (byte)(color.Y * 255);
        byte b = (byte)(color.Z * 255);
        return (uint)((a << 24) | (r << 16) | (g << 8) | b);
    }
    
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int BeginSceneDelegate(IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EndSceneDelegate(IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ClearDelegate(IntPtr device, uint count, IntPtr rects, uint flags, uint color, float z, uint stencil);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetRenderTargetDelegate(IntPtr device, uint index, IntPtr surface);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetViewportNativeDelegate(IntPtr device, ref D3DVIEWPORT9 viewport);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetScissorRectDelegate(IntPtr device, ref RECT rect);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetStreamSourceDelegate(IntPtr device, uint streamNumber, IntPtr streamData, uint offsetInBytes, uint stride);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetIndicesDelegate(IntPtr device, IntPtr indexData);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DrawPrimitiveDelegate(IntPtr device, uint primitiveType, uint startVertex, uint primitiveCount);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DrawIndexedPrimitiveDelegate(IntPtr device, uint primitiveType, int baseVertexIndex, uint minVertexIndex, uint numVertices, uint startIndex, uint primCount);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetRenderStateDelegate(IntPtr device, uint state, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetTransformDelegate(IntPtr device, uint state, ref Matrix4x4 matrix);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetTextureDelegate(IntPtr device, uint sampler, IntPtr texture);
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate int SetVertexShaderConstantFDelegate(IntPtr device, uint startRegister, ref Matrix4x4 data, uint vector4fCount);
    
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate int SetVertexShaderConstantF_RawDelegate(IntPtr device, uint startRegister, IntPtr data, uint vector4fCount);
    
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate int SetPixelShaderConstantF_RawDelegate(IntPtr device, uint startRegister, IntPtr data, uint vector4fCount);
}
