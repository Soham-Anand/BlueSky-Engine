using System.Numerics;

namespace NotBSRenderer;

public struct BufferDesc
{
    public ulong Size;
    public BufferUsage Usage;
    public MemoryType MemoryType;
    public string? DebugName;
}

public struct TextureDesc
{
    public uint Width;
    public uint Height;
    public uint Depth;
    public uint MipLevels;
    public uint ArrayLayers;
    public TextureFormat Format;
    public TextureUsage Usage;
    public string? DebugName;
}

public struct ShaderDesc
{
    public ShaderStage Stage;
    public byte[] Bytecode;
    public string EntryPoint;
    public string? DebugName;
}

public struct VertexAttribute
{
    public uint Location;
    public uint Binding;
    public TextureFormat Format;
    public uint Offset;
}

public struct VertexBinding
{
    public uint Binding;
    public uint Stride;
    public bool PerInstance;
}

public struct VertexLayoutDesc
{
    public VertexAttribute[] Attributes;
    public VertexBinding[] Bindings;
}

public struct BlendState
{
    public bool BlendEnabled;
    public BlendFactor SrcColorFactor;
    public BlendFactor DstColorFactor;
    public BlendOp ColorOp;
    public BlendFactor SrcAlphaFactor;
    public BlendFactor DstAlphaFactor;
    public BlendOp AlphaOp;
    
    public static BlendState Opaque => new()
    {
        BlendEnabled = false
    };
    
    public static BlendState AlphaBlend => new()
    {
        BlendEnabled = true,
        SrcColorFactor = BlendFactor.SrcAlpha,
        DstColorFactor = BlendFactor.OneMinusSrcAlpha,
        ColorOp = BlendOp.Add,
        SrcAlphaFactor = BlendFactor.One,
        DstAlphaFactor = BlendFactor.Zero,
        AlphaOp = BlendOp.Add
    };
}

public struct DepthStencilState
{
    public bool DepthTestEnabled;
    public bool DepthWriteEnabled;
    public CompareOp DepthCompareOp;
    
    public static DepthStencilState Default => new()
    {
        DepthTestEnabled = true,
        DepthWriteEnabled = true,
        DepthCompareOp = CompareOp.Less
    };
    
    public static DepthStencilState Disabled => new()
    {
        DepthTestEnabled = false,
        DepthWriteEnabled = false
    };
}

public struct RasterizerState
{
    public CullMode CullMode;
    public FrontFace FrontFace;
    public bool DepthClampEnabled;
    public bool ScissorEnabled;
    
    public static RasterizerState Default => new()
    {
        CullMode = CullMode.Back,
        FrontFace = FrontFace.CounterClockwise,
        DepthClampEnabled = false,
        ScissorEnabled = false
    };
}

public struct GraphicsPipelineDesc
{
    public ShaderDesc VertexShader;
    public ShaderDesc FragmentShader;
    public VertexLayoutDesc VertexLayout;
    public PrimitiveTopology Topology;
    public BlendState BlendState;
    public DepthStencilState DepthStencilState;
    public RasterizerState RasterizerState;
    public TextureFormat[] ColorFormats;
    public TextureFormat? DepthFormat;
    public string? DebugName;
}

public struct Viewport
{
    public float X;
    public float Y;
    public float Width;
    public float Height;
    public float MinDepth;
    public float MaxDepth;
}

public struct Scissor
{
    public int X;
    public int Y;
    public uint Width;
    public uint Height;
}

public struct ClearValue
{
    public Vector4 Color;
    public float Depth;
    public uint Stencil;
    public bool LoadInsteadOfClear;
    
    public static ClearValue FromColor(float r, float g, float b, float a = 1.0f) => new()
    {
        Color = new Vector4(r, g, b, a),
        Depth = 1.0f,
        LoadInsteadOfClear = false
    };
    
    public static ClearValue FromDepth(float depth = 1.0f) => new()
    {
        Depth = depth,
        LoadInsteadOfClear = false
    };
    
    public static ClearValue Load() => new()
    {
        LoadInsteadOfClear = true
    };
}
