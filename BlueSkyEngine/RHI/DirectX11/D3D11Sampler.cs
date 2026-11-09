namespace BlueSky.BSR.RHI.DirectX11;

internal sealed class D3D11Sampler : IRHISampler
{
    private bool _disposed;

    public SamplerDesc Desc { get; }
    internal IntPtr Handle { get; private set; }

    public D3D11Sampler(D3D11Device device, SamplerDesc desc)
    {
        Desc = desc;
        // Untestable here (no Windows) — comparison func is best-effort.
        uint addr = ToD3DAddress(desc.AddressU);
        Handle = desc.CompareLess
            ? device.CreateSamplerStateWithComparison(ToD3DFilter(desc), addr,
                D3D11Interop.D3D11_COMPARISON_LESS_EQUAL,
                desc.Filter == SamplerFilter.Anisotropic ? Math.Max(1u, desc.MaxAnisotropy) : 1u)
            : device.CreateSamplerState(ToD3DFilter(desc), addr,
                desc.Filter == SamplerFilter.Anisotropic ? Math.Max(1u, desc.MaxAnisotropy) : 1u);
    }

    private static uint ToD3DFilter(SamplerDesc desc) => desc.Filter switch
    {
        SamplerFilter.Nearest => 0,      // D3D11_FILTER_MIN_MAG_MIP_POINT
        SamplerFilter.Anisotropic => 55, // D3D11_FILTER_ANISOTROPIC
        _ => 21,                          // D3D11_FILTER_MIN_MAG_MIP_LINEAR
    };

    private static uint ToD3DAddress(SamplerAddressMode mode) => mode switch
    {
        SamplerAddressMode.Repeat => 1, // WRAP
        SamplerAddressMode.Mirror => 2, // MIRROR
        SamplerAddressMode.Clamp => 3,  // CLAMP
        SamplerAddressMode.Border => 4, // BORDER
        _ => 3,
    };

    public void Dispose()
    {
        if (_disposed) return;
        if (Handle != IntPtr.Zero)
        {
            System.Runtime.InteropServices.Marshal.Release(Handle);
            Handle = IntPtr.Zero;
        }
        _disposed = true;
    }
}
