using System.Numerics;
using System.Runtime.InteropServices;
using NotBSRenderer;

namespace BlueSky.Editor.UI;

// ──────────────────────────────────────────────────────────────────────────────
// UIVertex — shared by both NotBSUIRenderer and SimpleUIRenderer.
//
// Layout (stride = 40 bytes, Pack = 1 so the struct is exactly as declared):
//   offset  0 :  float2  Position   (screen-space pixels)
//   offset  8 :  float4  Color      (RGBA, linear)
//   offset 24 :  float2  UV         (normalised atlas coords; (0,0) for solid)
//   offset 32 :  float   Mode       (0 = solid colour, 1 = glyph alpha-texture)
//   offset 36 :  float   _pad       (keeps stride at 40, aligns to float)
// ──────────────────────────────────────────────────────────────────────────────
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct UIVertex
{
    public Vector2 Position;   // 8 bytes
    public Vector4 Color;      // 16 bytes
    public Vector2 UV;         // 8 bytes
    public float   Mode;       // 4 bytes
    private float  _pad;       // 4 bytes  ← padding, do not remove

    internal const int Stride = 40;

    /// <summary>Solid-colour geometry vertex (rect, line, circle).</summary>
    public UIVertex(Vector2 position, Vector4 color)
    {
        Position = position;
        Color    = color;
        UV       = Vector2.Zero;
        Mode     = 0f;
        _pad     = 0f;
    }

    /// <summary>Glyph vertex — samples the font atlas at <paramref name="uv"/>.</summary>
    public UIVertex(Vector2 position, Vector4 color, Vector2 uv)
    {
        Position = position;
        Color    = color;
        UV       = uv;
        Mode     = 1f;
        _pad     = 0f;
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// SimpleUIRenderer — a lightweight single-pass renderer used for debug/test
// scenes where you just want coloured rectangles without the full NotBSUI stack.
// ──────────────────────────────────────────────────────────────────────────────
public sealed class SimpleUIRenderer : IDisposable
{
    private const int MaxVertices = 4_096;
    private const int MaxIndices  = 6_144;

    private readonly IRHIDevice    _device;
    private          IRHIPipeline? _pipeline;
    private          IRHIBuffer?   _vertexBuffer;
    private          IRHIBuffer?   _indexBuffer;
    private          IRHIBuffer?   _uniformBuffer;
    private          IRHITexture?  _whiteTexture;

    private readonly List<UIVertex> _vertices = new();
    private readonly List<ushort>   _indices  = new();
    private          Matrix4x4      _projection;

    public SimpleUIRenderer(IRHIDevice device)
    {
        _device = device;
        CreatePipeline();
        CreateBuffers();
        CreateWhiteTexture();
    }

    // ── pipeline ───────────────────────────────────────────────────────────

    private static VertexLayoutDesc BuildVertexLayout() => new()
    {
        Attributes = new[]
        {
            // float2 position  — offset 0
            new VertexAttribute
            {
                Location = 0, Binding = 0,
                Format   = TextureFormat.RG32Float,
                Offset   = 0,
            },
            // float4 color  — offset 8
            new VertexAttribute
            {
                Location = 1, Binding = 0,
                Format   = TextureFormat.RGBA32Float,
                Offset   = 8,
            },
            // float2 uv  — offset 24
            new VertexAttribute
            {
                Location = 2, Binding = 0,
                Format   = TextureFormat.RG32Float,
                Offset   = 24,
            },
            // float mode  — offset 32
            new VertexAttribute
            {
                Location = 3, Binding = 0,
                Format   = TextureFormat.R32Float,
                Offset   = 32,
            },
        },
        Bindings = new[]
        {
            new VertexBinding { Binding = 0, Stride = UIVertex.Stride, PerInstance = false },
        },
    };

    private void CreatePipeline()
    {
        _pipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
        {
            VertexShader = new ShaderDesc
            {
                Stage      = ShaderStage.Vertex,
                EntryPoint = "vs_ui",
                Bytecode   = Array.Empty<byte>(),
            },
            FragmentShader = new ShaderDesc
            {
                Stage      = ShaderStage.Fragment,
                EntryPoint = "fs_ui",
                Bytecode   = Array.Empty<byte>(),
            },
            VertexLayout      = BuildVertexLayout(),
            Topology          = PrimitiveTopology.TriangleList,
            BlendState        = BlendState.AlphaBlend,
            DepthStencilState = DepthStencilState.Disabled,
            RasterizerState   = new RasterizerState { CullMode = CullMode.None },
            ColorFormats      = new[] { TextureFormat.BGRA8Unorm },
            DepthFormat       = null,
            DebugName         = "SimpleUI",
        });
    }

    private void CreateBuffers()
    {
        _vertexBuffer  = _device.CreateBuffer(new BufferDesc
        {
            Size       = (ulong)(MaxVertices * UIVertex.Stride),
            Usage      = BufferUsage.Vertex,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "SimpleUI.VB",
        });

        _indexBuffer   = _device.CreateBuffer(new BufferDesc
        {
            Size       = (ulong)(MaxIndices * sizeof(ushort)),
            Usage      = BufferUsage.Index,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "SimpleUI.IB",
        });

        _uniformBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = 64,   // one float4x4
            Usage      = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "SimpleUI.UB",
        });
    }

    private void CreateWhiteTexture()
    {
        _whiteTexture = _device.CreateTexture(new TextureDesc
        {
            Width       = 1,
            Height      = 1,
            MipLevels   = 1,
            ArrayLayers = 1,
            Format      = TextureFormat.RGBA8Unorm,
            Usage       = TextureUsage.Sampled,
            DebugName   = "SimpleUI.White1x1",
        });

        Span<byte> pixel = stackalloc byte[4] { 255, 255, 255, 255 };
        _device.UploadTexture(_whiteTexture, pixel);
    }

    // ── public API ─────────────────────────────────────────────────────────

    public void Resize(int width, int height)
    {
        _projection = Matrix4x4.CreateOrthographicOffCenter(0, width, height, 0, -1, 1);
    }

    public void DrawRect(float x, float y, float w, float h, Vector4 color)
    {
        if (_vertices.Count + 4 > MaxVertices || _indices.Count + 6 > MaxIndices)
            return;

        ushort b = (ushort)_vertices.Count;
        _vertices.Add(new UIVertex(new Vector2(x,     y),     color));
        _vertices.Add(new UIVertex(new Vector2(x + w, y),     color));
        _vertices.Add(new UIVertex(new Vector2(x + w, y + h), color));
        _vertices.Add(new UIVertex(new Vector2(x,     y + h), color));

        _indices.Add(b); _indices.Add((ushort)(b + 1)); _indices.Add((ushort)(b + 2));
        _indices.Add(b); _indices.Add((ushort)(b + 2)); _indices.Add((ushort)(b + 3));
    }

    public void Render(IRHICommandBuffer cmd)
    {
        if (_vertices.Count == 0) return;

        var vertexSpan = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(_vertices));
        _device.UpdateBuffer(_vertexBuffer!, vertexSpan);

        var indexSpan = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(_indices));
        _device.UpdateBuffer(_indexBuffer!, indexSpan);

        var projBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref _projection, 1));
        _device.UpdateBuffer(_uniformBuffer!, projBytes);

        cmd.SetPipeline(_pipeline!);
        cmd.SetVertexBuffer(_vertexBuffer!, 0, 0);
        cmd.SetIndexBuffer(_indexBuffer!, IndexType.UInt16, 0);
        cmd.SetUniformBuffer(_uniformBuffer!, 1, 0);
        cmd.SetTexture(_whiteTexture!, 0, 0);
        cmd.DrawIndexed((uint)_indices.Count);

        _vertices.Clear();
        _indices.Clear();
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _uniformBuffer?.Dispose();
        _whiteTexture?.Dispose();
    }
}
