using System.Runtime.InteropServices;

namespace NotBSRenderer.DirectX11;

/// <summary>
/// DirectX 11 pipeline state — bundles vertex/pixel shaders, input layout,
/// blend state, depth-stencil state, and rasterizer state into a single object.
/// Unlike DX12/Vulkan, DX11 doesn't have a monolithic PSO — we store individual
/// state objects and bind them together during command recording.
/// </summary>
internal sealed class D3D11Pipeline : IRHIPipeline
{
    internal IntPtr VertexShader;       // ID3D11VertexShader*
    internal IntPtr PixelShader;        // ID3D11PixelShader*
    internal IntPtr InputLayout;        // ID3D11InputLayout*
    internal IntPtr BlendState;         // ID3D11BlendState*
    internal IntPtr DepthStencilState;  // ID3D11DepthStencilState*
    internal IntPtr RasterizerState;    // ID3D11RasterizerState*
    internal uint Topology;            // D3D11_PRIMITIVE_TOPOLOGY
    internal uint[] VertexStrides = Array.Empty<uint>();
    internal string DebugName;

    private bool _disposed;

    internal D3D11Pipeline(string debugName)
    {
        DebugName = debugName ?? "D3D11Pipeline";
    }

    public void Dispose()
    {
        if (_disposed) return;
        SafeRelease(ref VertexShader);
        SafeRelease(ref PixelShader);
        SafeRelease(ref InputLayout);
        SafeRelease(ref BlendState);
        SafeRelease(ref DepthStencilState);
        SafeRelease(ref RasterizerState);
        _disposed = true;
    }

    private static void SafeRelease(ref IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
        {
            Marshal.Release(ptr);
            ptr = IntPtr.Zero;
        }
    }
}
