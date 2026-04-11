using System.Runtime.InteropServices;

namespace NotBSRenderer.DirectX9;

internal static class D3D9Interop
{
    private const string D3D9Lib = "d3d9.dll";
    
    // D3D9 Creation
    [DllImport(D3D9Lib)]
    public static extern IntPtr Direct3DCreate9(uint sdkVersion);
    
    public const uint D3D_SDK_VERSION = 32;
    
    // Adapter
    public const uint D3DADAPTER_DEFAULT = 0;
    
    // Device Types
    public const uint D3DDEVTYPE_HAL = 1;
    public const uint D3DDEVTYPE_REF = 2;
    
    // Behavior Flags
    public const uint D3DCREATE_SOFTWARE_VERTEXPROCESSING = 32;
    public const uint D3DCREATE_HARDWARE_VERTEXPROCESSING = 64;
    public const uint D3DCREATE_MIXED_VERTEXPROCESSING = 128;
    
    // HRESULT
    public const int D3D_OK = 0;
    public const uint D3DERR_DEVICELOST = 0x88760868;
    public const uint D3DERR_DEVICENOTRESET = 0x88760869;
    public const uint D3DERR_INVALIDCALL = 0x8876086c;
    
    // D3DFORMAT
    public const uint D3DFMT_UNKNOWN = 0;
    public const uint D3DFMT_L8 = 50;
    public const uint D3DFMT_A8R8G8B8 = 21;
    public const uint D3DFMT_X8R8G8B8 = 22;
    public const uint D3DFMT_R5G6B5 = 23;
    public const uint D3DFMT_D24S8 = 75;
    public const uint D3DFMT_D32 = 71;
    public const uint D3DFMT_INDEX16 = 101;
    public const uint D3DFMT_INDEX32 = 102;
    
    // D3DPOOL
    public const uint D3DPOOL_DEFAULT = 0;
    public const uint D3DPOOL_MANAGED = 1;
    public const uint D3DPOOL_SYSTEMMEM = 2;
    
    // D3DUSAGE
    public const uint D3DUSAGE_RENDERTARGET = 0x00000001;
    public const uint D3DUSAGE_DEPTHSTENCIL = 0x00000002;
    public const uint D3DUSAGE_DYNAMIC = 0x00000200;
    public const uint D3DUSAGE_WRITEONLY = 0x00000008;
    
    // D3DDECLTYPE
    public const byte D3DDECLTYPE_FLOAT1 = 0;
    public const byte D3DDECLTYPE_FLOAT2 = 1;
    public const byte D3DDECLTYPE_FLOAT3 = 2;
    public const byte D3DDECLTYPE_FLOAT4 = 3;
    
    // D3DDECLUSAGE
    public const byte D3DDECLUSAGE_POSITION = 0;
    public const byte D3DDECLUSAGE_COLOR = 10;
    public const byte D3DDECLUSAGE_TEXCOORD = 5;
    
    // D3DPRIMITIVETYPE
    public const uint D3DPT_POINTLIST = 1;
    public const uint D3DPT_LINELIST = 2;
    public const uint D3DPT_LINESTRIP = 3;
    public const uint D3DPT_TRIANGLELIST = 4;
    public const uint D3DPT_TRIANGLESTRIP = 5;
    
    // D3DCLEAR
    public const uint D3DCLEAR_TARGET = 0x00000001;
    public const uint D3DCLEAR_ZBUFFER = 0x00000002;
    public const uint D3DCLEAR_STENCIL = 0x00000004;
    
    // D3DBLEND
    public const uint D3DBLEND_ZERO = 1;
    public const uint D3DBLEND_ONE = 2;
    public const uint D3DBLEND_SRCCOLOR = 3;
    public const uint D3DBLEND_INVSRCCOLOR = 4;
    public const uint D3DBLEND_SRCALPHA = 5;
    public const uint D3DBLEND_INVSRCALPHA = 6;
    public const uint D3DBLEND_DESTALPHA = 7;
    public const uint D3DBLEND_INVDESTALPHA = 8;
    public const uint D3DBLEND_DESTCOLOR = 9;
    public const uint D3DBLEND_INVDESTCOLOR = 10;
    
    // D3DBLENDOP
    public const uint D3DBLENDOP_ADD = 1;
    public const uint D3DBLENDOP_SUBTRACT = 2;
    public const uint D3DBLENDOP_REVSUBTRACT = 3;
    public const uint D3DBLENDOP_MIN = 4;
    public const uint D3DBLENDOP_MAX = 5;
    
    // D3DCMPFUNC
    public const uint D3DCMP_NEVER = 1;
    public const uint D3DCMP_LESS = 2;
    public const uint D3DCMP_EQUAL = 3;
    public const uint D3DCMP_LESSEQUAL = 4;
    public const uint D3DCMP_GREATER = 5;
    public const uint D3DCMP_NOTEQUAL = 6;
    public const uint D3DCMP_GREATEREQUAL = 7;
    public const uint D3DCMP_ALWAYS = 8;
    
    // D3DCULL
    public const uint D3DCULL_NONE = 1;
    public const uint D3DCULL_CW = 2;
    public const uint D3DCULL_CCW = 3;
    
    // Structures
    [StructLayout(LayoutKind.Sequential)]
    public struct D3DPRESENT_PARAMETERS
    {
        public uint BackBufferWidth;
        public uint BackBufferHeight;
        public uint BackBufferFormat;
        public uint BackBufferCount;
        public uint MultiSampleType;
        public uint MultiSampleQuality;
        public uint SwapEffect;
        public IntPtr hDeviceWindow;
        public int Windowed;
        public int EnableAutoDepthStencil;
        public uint AutoDepthStencilFormat;
        public uint Flags;
        public uint FullScreen_RefreshRateInHz;
        public uint PresentationInterval;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct D3DVIEWPORT9
    {
        public uint X;
        public uint Y;
        public uint Width;
        public uint Height;
        public float MinZ;
        public float MaxZ;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct D3DVERTEXELEMENT9
    {
        public ushort Stream;
        public ushort Offset;
        public byte Type;
        public byte Method;
        public byte Usage;
        public byte UsageIndex;
    }
    
    public static readonly D3DVERTEXELEMENT9 D3DDECL_END = new D3DVERTEXELEMENT9
    {
        Stream = 0xFF,
        Offset = 0,
        Type = 17, // D3DDECLTYPE_UNUSED
        Method = 0,
        Usage = 0,
        UsageIndex = 0
    };
    
    public static uint D3DCOLOR_ARGB(byte a, byte r, byte g, byte b)
    {
        return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    }
    
    public static uint ToD3DFormat(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.R8Unorm => D3DFMT_L8,
            TextureFormat.RGBA8Unorm => D3DFMT_A8R8G8B8,
            TextureFormat.BGRA8Unorm => D3DFMT_A8R8G8B8,
            TextureFormat.Depth32Float => D3DFMT_D32,
            TextureFormat.Depth24Stencil8 => D3DFMT_D24S8,
            _ => throw new NotSupportedException($"Format {format} not supported on DX9")
        };
    }
    
    public static uint ToD3DPrimitiveType(PrimitiveTopology topology)
    {
        return topology switch
        {
            PrimitiveTopology.PointList => D3DPT_POINTLIST,
            PrimitiveTopology.LineList => D3DPT_LINELIST,
            PrimitiveTopology.LineStrip => D3DPT_LINESTRIP,
            PrimitiveTopology.TriangleList => D3DPT_TRIANGLELIST,
            PrimitiveTopology.TriangleStrip => D3DPT_TRIANGLESTRIP,
            _ => throw new NotSupportedException($"Topology {topology} not supported")
        };
    }
    
    public static int GetBytesPerPixel(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.R8Unorm => 1,
            TextureFormat.RGBA8Unorm => 4,
            TextureFormat.RGBA8Srgb => 4,
            TextureFormat.BGRA8Unorm => 4,
            TextureFormat.BGRA8Srgb => 4,
            TextureFormat.RG32Float => 8,
            TextureFormat.RGB32Float => 12,
            TextureFormat.RGBA16Float => 8,
            TextureFormat.RGBA32Float => 16,
            _ => throw new NotSupportedException($"Format {format} bytes per pixel not defined")
        };
    }
    
    public static uint ToD3DIndexFormat(IndexType indexType)
    {
        return indexType switch
        {
            IndexType.UInt16 => D3DFMT_INDEX16,
            IndexType.UInt32 => D3DFMT_INDEX32,
            _ => throw new NotSupportedException($"Index type {indexType} not supported")
        };
    }
}
