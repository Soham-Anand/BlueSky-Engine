using System.Runtime.InteropServices;

namespace NotBSRenderer.DirectX11;

/// <summary>
/// DirectX 11 P/Invoke declarations and constants
/// </summary>
internal static class D3D11Interop
{
    private const string D3D11Lib = "d3d11.dll";
    private const string DXGILib = "dxgi.dll";
    
    // D3D11 Device Creation
    [DllImport(D3D11Lib)]
    public static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        uint DriverType,
        IntPtr Software,
        uint Flags,
        IntPtr pFeatureLevels,
        uint FeatureLevels,
        uint SDKVersion,
        out IntPtr ppDevice,
        out uint pFeatureLevel,
        out IntPtr ppImmediateContext);
    
    // Constants
    public const uint D3D11_SDK_VERSION = 7;
    
    // Driver Types
    public const uint D3D_DRIVER_TYPE_HARDWARE = 1;
    public const uint D3D_DRIVER_TYPE_WARP = 5;
    public const uint D3D_DRIVER_TYPE_REFERENCE = 2;
    
    // Create Device Flags
    public const uint D3D11_CREATE_DEVICE_DEBUG = 0x2;
    public const uint D3D11_CREATE_DEVICE_SINGLETHREADED = 0x1;
    
    // Feature Levels
    public const uint D3D_FEATURE_LEVEL_11_1 = 0xb100;
    public const uint D3D_FEATURE_LEVEL_11_0 = 0xb000;
    public const uint D3D_FEATURE_LEVEL_10_1 = 0xa100;
    public const uint D3D_FEATURE_LEVEL_10_0 = 0xa000;
    public const uint D3D_FEATURE_LEVEL_9_3 = 0x9300;
    
    // DXGI Formats
    public const uint DXGI_FORMAT_UNKNOWN = 0;
    public const uint DXGI_FORMAT_R8_UNORM = 61;
    public const uint DXGI_FORMAT_R8G8B8A8_UNORM = 28;
    public const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    public const uint DXGI_FORMAT_D24_UNORM_S8_UINT = 45;
    public const uint DXGI_FORMAT_D32_FLOAT = 40;
    
    // Usage
    public const uint D3D11_USAGE_DEFAULT = 0;
    public const uint D3D11_USAGE_IMMUTABLE = 1;
    public const uint D3D11_USAGE_DYNAMIC = 2;
    public const uint D3D11_USAGE_STAGING = 3;
    
    // Bind Flags
    public const uint D3D11_BIND_VERTEX_BUFFER = 0x1;
    public const uint D3D11_BIND_INDEX_BUFFER = 0x2;
    public const uint D3D11_BIND_CONSTANT_BUFFER = 0x4;
    public const uint D3D11_BIND_SHADER_RESOURCE = 0x8;
    public const uint D3D11_BIND_RENDER_TARGET = 0x20;
    public const uint D3D11_BIND_DEPTH_STENCIL = 0x40;
    
    // CPU Access Flags
    public const uint D3D11_CPU_ACCESS_WRITE = 0x10000;
    public const uint D3D11_CPU_ACCESS_READ = 0x20000;
    
    // Primitive Topology
    public const uint D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST = 4;
    public const uint D3D11_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP = 5;
    public const uint D3D11_PRIMITIVE_TOPOLOGY_LINELIST = 2;
    public const uint D3D11_PRIMITIVE_TOPOLOGY_LINESTRIP = 3;
    public const uint D3D11_PRIMITIVE_TOPOLOGY_POINTLIST = 1;
    
    // Structures
    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_BUFFER_DESC
    {
        public uint ByteWidth;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
        public uint StructureByteStride;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_SUBRESOURCE_DATA
    {
        public IntPtr pSysMem;
        public uint SysMemPitch;
        public uint SysMemSlicePitch;
    }
    
    // Helper methods
    public static uint ToDXGIFormat(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.R8Unorm => DXGI_FORMAT_R8_UNORM,
            TextureFormat.RGBA8Unorm => DXGI_FORMAT_R8G8B8A8_UNORM,
            TextureFormat.BGRA8Unorm => DXGI_FORMAT_B8G8R8A8_UNORM,
            TextureFormat.Depth32Float => DXGI_FORMAT_D32_FLOAT,
            TextureFormat.Depth24Stencil8 => DXGI_FORMAT_D24_UNORM_S8_UINT,
            _ => throw new NotSupportedException($"Format {format} not supported on DX11")
        };
    }
    
    public static uint ToD3D11PrimitiveTopology(PrimitiveTopology topology)
    {
        return topology switch
        {
            PrimitiveTopology.PointList => D3D11_PRIMITIVE_TOPOLOGY_POINTLIST,
            PrimitiveTopology.LineList => D3D11_PRIMITIVE_TOPOLOGY_LINELIST,
            PrimitiveTopology.LineStrip => D3D11_PRIMITIVE_TOPOLOGY_LINESTRIP,
            PrimitiveTopology.TriangleList => D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST,
            PrimitiveTopology.TriangleStrip => D3D11_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP,
            _ => throw new NotSupportedException($"Topology {topology} not supported")
        };
    }
}
