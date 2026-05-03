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

    // ── Additional DXGI Formats ─────────────────────────────────────────
    public const uint DXGI_FORMAT_R32_FLOAT = 41;
    public const uint DXGI_FORMAT_R32G32_FLOAT = 16;
    public const uint DXGI_FORMAT_R32G32B32_FLOAT = 6;
    public const uint DXGI_FORMAT_R32G32B32A32_FLOAT = 2;
    public const uint DXGI_FORMAT_R16G16B16A16_FLOAT = 10;
    public const uint DXGI_FORMAT_R8G8B8A8_UNORM_SRGB = 29;
    public const uint DXGI_FORMAT_B8G8R8A8_UNORM_SRGB = 91;
    public const uint DXGI_FORMAT_R16_UINT = 57;
    public const uint DXGI_FORMAT_R32_UINT = 42;
    public const uint DXGI_FORMAT_BC1_UNORM = 71;
    public const uint DXGI_FORMAT_BC3_UNORM = 77;
    public const uint DXGI_FORMAT_BC7_UNORM = 98;

    // ── Extended format mapper ──────────────────────────────────────────
    public static uint ToDXGIFormatExtended(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.R8Unorm => DXGI_FORMAT_R8_UNORM,
            TextureFormat.R32Float => DXGI_FORMAT_R32_FLOAT,
            TextureFormat.RGBA8Unorm => DXGI_FORMAT_R8G8B8A8_UNORM,
            TextureFormat.RGBA8Srgb => DXGI_FORMAT_R8G8B8A8_UNORM_SRGB,
            TextureFormat.BGRA8Unorm => DXGI_FORMAT_B8G8R8A8_UNORM,
            TextureFormat.BGRA8Srgb => DXGI_FORMAT_B8G8R8A8_UNORM_SRGB,
            TextureFormat.RG32Float => DXGI_FORMAT_R32G32_FLOAT,
            TextureFormat.RGB32Float => DXGI_FORMAT_R32G32B32_FLOAT,
            TextureFormat.RGBA16Float => DXGI_FORMAT_R16G16B16A16_FLOAT,
            TextureFormat.RGBA32Float => DXGI_FORMAT_R32G32B32A32_FLOAT,
            TextureFormat.Depth32Float => DXGI_FORMAT_D32_FLOAT,
            TextureFormat.Depth24Stencil8 => DXGI_FORMAT_D24_UNORM_S8_UINT,
            TextureFormat.BC1 => DXGI_FORMAT_BC1_UNORM,
            TextureFormat.BC3 => DXGI_FORMAT_BC3_UNORM,
            TextureFormat.BC7 => DXGI_FORMAT_BC7_UNORM,
            _ => DXGI_FORMAT_UNKNOWN
        };
    }

    // ── Blend Constants ─────────────────────────────────────────────────
    public const uint D3D11_BLEND_ZERO = 1;
    public const uint D3D11_BLEND_ONE = 2;
    public const uint D3D11_BLEND_SRC_COLOR = 3;
    public const uint D3D11_BLEND_INV_SRC_COLOR = 4;
    public const uint D3D11_BLEND_SRC_ALPHA = 5;
    public const uint D3D11_BLEND_INV_SRC_ALPHA = 6;
    public const uint D3D11_BLEND_DEST_ALPHA = 7;
    public const uint D3D11_BLEND_INV_DEST_ALPHA = 8;
    public const uint D3D11_BLEND_DEST_COLOR = 9;
    public const uint D3D11_BLEND_INV_DEST_COLOR = 10;

    public const uint D3D11_BLEND_OP_ADD = 1;
    public const uint D3D11_BLEND_OP_SUBTRACT = 2;
    public const uint D3D11_BLEND_OP_REV_SUBTRACT = 3;
    public const uint D3D11_BLEND_OP_MIN = 4;
    public const uint D3D11_BLEND_OP_MAX = 5;

    public const byte D3D11_COLOR_WRITE_ENABLE_ALL = 0xF;

    // ── Comparison Functions ────────────────────────────────────────────
    public const uint D3D11_COMPARISON_NEVER = 1;
    public const uint D3D11_COMPARISON_LESS = 2;
    public const uint D3D11_COMPARISON_EQUAL = 3;
    public const uint D3D11_COMPARISON_LESS_EQUAL = 4;
    public const uint D3D11_COMPARISON_GREATER = 5;
    public const uint D3D11_COMPARISON_NOT_EQUAL = 6;
    public const uint D3D11_COMPARISON_GREATER_EQUAL = 7;
    public const uint D3D11_COMPARISON_ALWAYS = 8;

    // ── Fill / Cull Modes ───────────────────────────────────────────────
    public const uint D3D11_FILL_WIREFRAME = 2;
    public const uint D3D11_FILL_SOLID = 3;
    public const uint D3D11_CULL_NONE = 1;
    public const uint D3D11_CULL_FRONT = 2;
    public const uint D3D11_CULL_BACK = 3;

    // ── Sampler Filter / Address ────────────────────────────────────────
    public const uint D3D11_FILTER_MIN_MAG_MIP_POINT = 0;
    public const uint D3D11_FILTER_MIN_MAG_MIP_LINEAR = 0x15;
    public const uint D3D11_FILTER_ANISOTROPIC = 0x55;

    public const uint D3D11_TEXTURE_ADDRESS_WRAP = 1;
    public const uint D3D11_TEXTURE_ADDRESS_MIRROR = 2;
    public const uint D3D11_TEXTURE_ADDRESS_CLAMP = 3;
    public const uint D3D11_TEXTURE_ADDRESS_BORDER = 4;

    // ── Clear Flags ─────────────────────────────────────────────────────
    public const uint D3D11_CLEAR_DEPTH = 0x1;
    public const uint D3D11_CLEAR_STENCIL = 0x2;

    // ── Bind Flags (additions) ──────────────────────────────────────────
    public const uint D3D11_BIND_UNORDERED_ACCESS = 0x80;
    public const uint D3D11_BIND_STREAM_OUTPUT = 0x10;

    // ═══════════════════════════════════════════════════════════════════
    //  STATE CREATION STRUCTURES
    // ═══════════════════════════════════════════════════════════════════

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_RENDER_TARGET_BLEND_DESC
    {
        public int BlendEnable;
        public uint SrcBlend;
        public uint DestBlend;
        public uint BlendOp;
        public uint SrcBlendAlpha;
        public uint DestBlendAlpha;
        public uint BlendOpAlpha;
        public byte RenderTargetWriteMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct D3D11_BLEND_DESC
    {
        public int AlphaToCoverageEnable;
        public int IndependentBlendEnable;
        public D3D11_RENDER_TARGET_BLEND_DESC RT0;
        public D3D11_RENDER_TARGET_BLEND_DESC RT1;
        public D3D11_RENDER_TARGET_BLEND_DESC RT2;
        public D3D11_RENDER_TARGET_BLEND_DESC RT3;
        public D3D11_RENDER_TARGET_BLEND_DESC RT4;
        public D3D11_RENDER_TARGET_BLEND_DESC RT5;
        public D3D11_RENDER_TARGET_BLEND_DESC RT6;
        public D3D11_RENDER_TARGET_BLEND_DESC RT7;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_DEPTH_STENCILOP_DESC
    {
        public uint StencilFailOp;
        public uint StencilDepthFailOp;
        public uint StencilPassOp;
        public uint StencilFunc;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_DEPTH_STENCIL_DESC
    {
        public int DepthEnable;
        public uint DepthWriteMask; // 0 = ZERO, 1 = ALL
        public uint DepthFunc;
        public int StencilEnable;
        public byte StencilReadMask;
        public byte StencilWriteMask;
        public D3D11_DEPTH_STENCILOP_DESC FrontFace;
        public D3D11_DEPTH_STENCILOP_DESC BackFace;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_RASTERIZER_DESC
    {
        public uint FillMode;
        public uint CullMode;
        public int FrontCounterClockwise;
        public int DepthBias;
        public float DepthBiasClamp;
        public float SlopeScaledDepthBias;
        public int DepthClipEnable;
        public int ScissorEnable;
        public int MultisampleEnable;
        public int AntialiasedLineEnable;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_SAMPLER_DESC
    {
        public uint Filter;
        public uint AddressU;
        public uint AddressV;
        public uint AddressW;
        public float MipLODBias;
        public uint MaxAnisotropy;
        public uint ComparisonFunc;
        public float BorderColor0, BorderColor1, BorderColor2, BorderColor3;
        public float MinLOD;
        public float MaxLOD;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RHI ENUM → DX11 MAPPERS
    // ═══════════════════════════════════════════════════════════════════

    public static uint ToD3D11Blend(BlendFactor factor) => factor switch
    {
        BlendFactor.Zero => D3D11_BLEND_ZERO,
        BlendFactor.One => D3D11_BLEND_ONE,
        BlendFactor.SrcColor => D3D11_BLEND_SRC_COLOR,
        BlendFactor.OneMinusSrcColor => D3D11_BLEND_INV_SRC_COLOR,
        BlendFactor.DstColor => D3D11_BLEND_DEST_COLOR,
        BlendFactor.OneMinusDstColor => D3D11_BLEND_INV_DEST_COLOR,
        BlendFactor.SrcAlpha => D3D11_BLEND_SRC_ALPHA,
        BlendFactor.OneMinusSrcAlpha => D3D11_BLEND_INV_SRC_ALPHA,
        BlendFactor.DstAlpha => D3D11_BLEND_DEST_ALPHA,
        BlendFactor.OneMinusDstAlpha => D3D11_BLEND_INV_DEST_ALPHA,
        _ => D3D11_BLEND_ONE
    };

    public static uint ToD3D11BlendOp(BlendOp op) => op switch
    {
        BlendOp.Add => D3D11_BLEND_OP_ADD,
        BlendOp.Subtract => D3D11_BLEND_OP_SUBTRACT,
        BlendOp.ReverseSubtract => D3D11_BLEND_OP_REV_SUBTRACT,
        BlendOp.Min => D3D11_BLEND_OP_MIN,
        BlendOp.Max => D3D11_BLEND_OP_MAX,
        _ => D3D11_BLEND_OP_ADD
    };

    public static uint ToD3D11ComparisonFunc(CompareOp op) => op switch
    {
        CompareOp.Never => D3D11_COMPARISON_NEVER,
        CompareOp.Less => D3D11_COMPARISON_LESS,
        CompareOp.Equal => D3D11_COMPARISON_EQUAL,
        CompareOp.LessOrEqual => D3D11_COMPARISON_LESS_EQUAL,
        CompareOp.Greater => D3D11_COMPARISON_GREATER,
        CompareOp.NotEqual => D3D11_COMPARISON_NOT_EQUAL,
        CompareOp.GreaterOrEqual => D3D11_COMPARISON_GREATER_EQUAL,
        CompareOp.Always => D3D11_COMPARISON_ALWAYS,
        _ => D3D11_COMPARISON_LESS
    };

    public static uint ToD3D11CullMode(CullMode mode) => mode switch
    {
        CullMode.None => D3D11_CULL_NONE,
        CullMode.Front => D3D11_CULL_FRONT,
        CullMode.Back => D3D11_CULL_BACK,
        _ => D3D11_CULL_BACK
    };

    public static uint ToD3D11FillMode(FillMode mode) => mode switch
    {
        FillMode.Wireframe => D3D11_FILL_WIREFRAME,
        FillMode.Solid => D3D11_FILL_SOLID,
        _ => D3D11_FILL_SOLID
    };
}
