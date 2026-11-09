using static BlueSky.BSR.RHI.Metal.MetalInterop;

namespace BlueSky.BSR.RHI.Metal;

internal sealed class MetalSampler : IRHISampler
{
    private IntPtr _samplerState;
    private bool _disposed;

    public SamplerDesc Desc { get; }

    internal IntPtr Handle => _samplerState;

    public MetalSampler(MetalDevice device, SamplerDesc desc)
    {
        Desc = desc;

        var descClass = GetClass("MTLSamplerDescriptor");
        var descriptor = objc_msgSend(objc_msgSend(descClass, GetSelector("alloc")), GetSelector("init"));
        try
        {
            ulong minMag = desc.Filter == SamplerFilter.Nearest ? 0ul : 1ul; // 0=nearest 1=linear
            ulong mip = desc.Filter == SamplerFilter.Nearest ? 0ul : 2ul;    // 0=notMipmapped 2=linear
            objc_msgSend_void_ulong(descriptor, GetSelector("setMinFilter:"), minMag);
            objc_msgSend_void_ulong(descriptor, GetSelector("setMagFilter:"), minMag);
            objc_msgSend_void_ulong(descriptor, GetSelector("setMipFilter:"), mip);
            objc_msgSend_void_ulong(descriptor, GetSelector("setSAddressMode:"), ToMTLSamplerAddress(desc.AddressU));
            objc_msgSend_void_ulong(descriptor, GetSelector("setTAddressMode:"), ToMTLSamplerAddress(desc.AddressV));
            if (desc.Filter == SamplerFilter.Anisotropic)
                objc_msgSend_void_ulong(descriptor, GetSelector("setMaxAnisotropy:"), Math.Max(1u, desc.MaxAnisotropy));
            if (desc.CompareLess)
                objc_msgSend_void_ulong(descriptor, GetSelector("setCompareFunction:"), 3); // MTLCompareFunctionLessEqual
            SetFloat(descriptor, GetSelector("setLodMinClamp:"), desc.MinLod);
            SetFloat(descriptor, GetSelector("setLodMaxClamp:"), desc.MaxLod);

            IntPtr error = IntPtr.Zero;
            _samplerState = NewSamplerState(device.Device, GetSelector("newSamplerStateWithDescriptor:"), descriptor);
            if (_samplerState == IntPtr.Zero)
                throw new Exception("Failed to create Metal sampler state");
        }
        finally
        {
            Release(descriptor);
        }
    }

    private static ulong ToMTLSamplerAddress(SamplerAddressMode mode) => mode switch
    {
        SamplerAddressMode.Clamp => 0,  // MTLSamplerAddressModeClampToEdge
        SamplerAddressMode.Mirror => 1, // MTLSamplerAddressModeMirrorClampToEdge
        SamplerAddressMode.Repeat => 2, // MTLSamplerAddressModeRepeat
        SamplerAddressMode.Border => 0, // no border color wired — fall back to clamp
        _ => 0,
    };

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SetFloat(IntPtr receiver, IntPtr selector, float value);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr NewSamplerState(IntPtr receiver, IntPtr selector, IntPtr descriptor);

    public void Dispose()
    {
        if (_disposed) return;
        if (_samplerState != IntPtr.Zero)
        {
            Release(_samplerState);
            _samplerState = IntPtr.Zero;
        }
        _disposed = true;
    }
}
