namespace NotBSRenderer;

public enum RHIBackend
{
    Metal,
    DirectX9,
    DirectX10,
    DirectX11,
    DirectX12,
    Vulkan,
    OpenGL
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
