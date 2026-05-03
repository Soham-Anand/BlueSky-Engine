// BlueSky Engine - RHI Enumerations
// 
// ARCHITECTURAL DECISION: DirectX 11 Feature Levels (No DX9/DX10)
// ================================================================
// BlueSky Engine uses DX11 with feature levels as the minimum DirectX requirement.
// This provides modern rendering capabilities while maintaining broad hardware compatibility.
//
// Feature Level Support:
// - Level 10.0: Minimum (Shader Model 4.0, Geometry Shaders, Indirect Drawing)
// - Level 10.1: Enhanced (Tessellation, Cubemap Arrays)
// - Level 11.0: Full DX11 (Compute Shaders, UAVs, Multi-Draw Indirect)
// - Level 11.1: Enhanced DX11 (UAVs at all stages, Logical Blend Ops)
//
// Rendering Path Selection:
// - DX11 FL 10.x → CPU-based light culling, reduced clusters
// - DX11 FL 11.0+ → GPU compute-based light culling, full clusters
// - DX12/Vulkan/Metal → Bindless resources, async compute, full modern features
//
// This approach eliminates legacy DX9 complexity while ensuring compatibility with
// hardware from ~2008 onwards (GeForce 8/9 series, Radeon HD 2000/3000 series).

namespace NotBSRenderer;

public enum RHIBackend
{
    Metal,
    DirectX11,
    DirectX12,
    Vulkan,
    OpenGL
}

/// <summary>
/// DirectX 11 feature levels for hardware capability detection
/// Replaces legacy DX9/DX10 with modern feature-level based approach
/// </summary>
public enum D3D11FeatureLevel
{
    /// <summary>
    /// Feature Level 10.0 - Minimum for BlueSky Engine
    /// Shader Model 4.0, Geometry Shaders, Stream Output
    /// </summary>
    Level_10_0,
    
    /// <summary>
    /// Feature Level 10.1 - Enhanced DX10
    /// Cubemap arrays, extended formats
    /// </summary>
    Level_10_1,
    
    /// <summary>
    /// Feature Level 11.0 - Full DX11
    /// Shader Model 5.0, Compute Shaders, Tessellation, UAVs
    /// </summary>
    Level_11_0,
    
    /// <summary>
    /// Feature Level 11.1 - Enhanced DX11
    /// Logical blend operations, UAVs at all stages
    /// </summary>
    Level_11_1
}

public enum BufferUsage
{
    Vertex = 1 << 0,
    Index = 1 << 1,
    Uniform = 1 << 2,
    Storage = 1 << 3,
    Indirect = 1 << 4,
    TransferSrc = 1 << 5,
    TransferDst = 1 << 6
}

public enum MemoryType
{
    GpuOnly,
    CpuToGpu,
    GpuToCpu
}

public enum TextureFormat
{
    R8Unorm,
    R32Float,
    RGBA8Unorm,
    RGBA8Srgb,
    BGRA8Unorm,
    BGRA8Srgb,
    RG32Float,
    RGB32Float,
    RGBA16Float,
    RGBA32Float,
    Depth32Float,
    Depth24Stencil8,
    BC1,
    BC3,
    BC7
}

public enum TextureUsage
{
    Sampled = 1 << 0,
    Storage = 1 << 1,
    RenderTarget = 1 << 2,
    DepthStencil = 1 << 3,
    TransferSrc = 1 << 4,
    TransferDst = 1 << 5
}

public enum ShaderStage
{
    Vertex,
    Fragment,
    Compute
}

public enum PrimitiveTopology
{
    TriangleList,
    TriangleStrip,
    LineList,
    LineStrip,
    PointList
}

public enum CompareOp
{
    Never,
    Less,
    Equal,
    LessOrEqual,
    Greater,
    NotEqual,
    GreaterOrEqual,
    Always
}

public enum BlendFactor
{
    Zero,
    One,
    SrcColor,
    OneMinusSrcColor,
    DstColor,
    OneMinusDstColor,
    SrcAlpha,
    OneMinusSrcAlpha,
    DstAlpha,
    OneMinusDstAlpha
}

public enum BlendOp
{
    Add,
    Subtract,
    ReverseSubtract,
    Min,
    Max
}

public enum CullMode
{
    None,
    Front,
    Back
}

public enum FrontFace
{
    Clockwise,
    CounterClockwise
}

public enum FillMode
{
    Solid,
    Wireframe
}

public enum PresentMode
{
    Immediate,
    Vsync,
    Mailbox
}

public enum IndexType
{
    UInt16,
    UInt32
}

/// <summary>
/// RHI capability flags for feature detection
/// </summary>
[Flags]
public enum RHICapabilities
{
    None = 0,
    ComputeShaders = 1 << 0,
    BindlessResources = 1 << 1,
    RayTracing = 1 << 2,
    MeshShaders = 1 << 3,
    VariableRateShading = 1 << 4,
    AsyncCompute = 1 << 5,
    IndirectDrawing = 1 << 6,
    MultiDrawIndirect = 1 << 7,
    GeometryShaders = 1 << 8,
    TessellationShaders = 1 << 9
}

/// <summary>
/// Descriptor binding mode for resource management
/// </summary>
public enum DescriptorBindingMode
{
    /// <summary>
    /// Traditional slot-based binding (DX11 Feature Level 10.x/11.0)
    /// </summary>
    SlotBased,
    
    /// <summary>
    /// Bindless resources via descriptor indexing (Vulkan/Metal/DX12)
    /// </summary>
    Bindless
}
