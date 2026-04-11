using System.Numerics;
using System.Runtime.InteropServices;
using NotBSRenderer;

namespace BlueSky.Editor.UI;

/// <summary>
/// GPU renderer for NotBSUI draw commands.
///
/// One pipeline handles both solid geometry (rects, lines, circles) and
/// glyph quads.  The per-vertex <c>Mode</c> field tells the fragment shader
/// whether to use the vertex colour directly (Mode=0) or to sample the font
/// atlas for coverage and multiply against the colour (Mode=1).
///
/// Every frame the renderer bins all draw commands into a single vertex +
/// index buffer, uploads once, and draws once — keeping GPU overhead minimal.
/// </summary>
public sealed class NotBSUIRenderer : IDisposable
{
    // ── geometry budget ────────────────────────────────────────────────────
    private const int MaxVertices     = 65_535;
    private const int MaxIndices      = 150_000;
    private const float LineThickness = 1.5f;
    private const int  CircleSegments = 24;

    // ── RHI resources ─────────────────────────────────────────────────────
    private readonly IRHIDevice    _device;
    private          IRHIPipeline? _pipeline;
    private          IRHIBuffer?   _vertexBuffer;
    private          IRHIBuffer?   _indexBuffer;
    private          IRHIBuffer?   _uniformBuffer;
    private          IRHITexture?  _whiteTexture;
    
    // ── font rendering ──────────────────────────────────────────────────────
    public           FontAtlas?    FontAtlas { get; set; }

    // ── per-frame CPU buffers ──────────────────────────────────────────────
    private readonly List<UIVertex> _vertices = new(4096);
    private readonly List<ushort>   _indices  = new(8192);
    private          Matrix4x4      _projection;
    private          bool           _overflowed;

    // ─────────────────────────────────────────────────────────────────────
    // Construction
    // ─────────────────────────────────────────────────────────────────────

    public NotBSUIRenderer(IRHIDevice device)
    {
        _device = device;
        CreatePipeline();
        CreateBuffers();
        CreateWhiteTexture();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Setup
    // ─────────────────────────────────────────────────────────────────────

    private static VertexLayoutDesc BuildVertexLayout() => new()
    {
        Attributes = new[]
        {
            new VertexAttribute { Location = 0, Binding = 0, Format = TextureFormat.RG32Float,   Offset = 0  },  // position
            new VertexAttribute { Location = 1, Binding = 0, Format = TextureFormat.RGBA32Float, Offset = 8  },  // color
            new VertexAttribute { Location = 2, Binding = 0, Format = TextureFormat.RG32Float,   Offset = 24 }, // uv
            new VertexAttribute { Location = 3, Binding = 0, Format = TextureFormat.R32Float,    Offset = 32 }, // mode
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
            VertexShader      = LoadShader(ShaderStage.Vertex, "vs_ui"),
            FragmentShader    = LoadShader(ShaderStage.Fragment, "fs_ui"),
            VertexLayout      = BuildVertexLayout(),
            Topology          = PrimitiveTopology.TriangleList,
            BlendState        = BlendState.AlphaBlend,
            DepthStencilState = DepthStencilState.Disabled,
            RasterizerState   = new RasterizerState { CullMode = CullMode.None },
            ColorFormats      = new[] { TextureFormat.BGRA8Unorm },
            DepthFormat       = TextureFormat.Depth32Float, // Must match render pass depth attachment
            DebugName         = "NotBSUI",
        });
    }

    private ShaderDesc LoadShader(ShaderStage stage, string entryPoint)
    {
        byte[] bytecode = Array.Empty<byte>();

        if (_device.Backend == RHIBackend.DirectX9)
        {
            // DX9 requires pre-compiled bytecode for programmable shaders
            string ext = stage == ShaderStage.Vertex ? ".vs.fxc" : ".fs.fxc";
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", "simple_ui" + ext);
            
            if (!File.Exists(path))
                path = Path.Combine(Directory.GetCurrentDirectory(), "Editor", "Shaders", "simple_ui" + ext);

            if (File.Exists(path))
            {
                bytecode = File.ReadAllBytes(path);
            }
            else
            {
                Console.WriteLine($"[NotBSUIRenderer] WARNING: DX9 bytecode not found at {path}. UI may not render on DX9.");
            }
        }

        return new ShaderDesc
        {
            Stage      = stage,
            EntryPoint = entryPoint,
            Bytecode   = bytecode,
        };
    }

    private void CreateBuffers()
    {
        _vertexBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = (ulong)(MaxVertices * UIVertex.Stride),
            Usage      = BufferUsage.Vertex,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "NotBSUI.VB",
        });

        _indexBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = (ulong)(MaxIndices * sizeof(ushort)),
            Usage      = BufferUsage.Index,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "NotBSUI.IB",
        });

        _uniformBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = 64,
            Usage      = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "NotBSUI.UB",
        });
    }

    private void CreateWhiteTexture()
    {
        _whiteTexture = _device.CreateTexture(new TextureDesc
        {
            Width       = 1,
            Height      = 1,
            Depth       = 1,
            MipLevels   = 1,
            ArrayLayers = 1,
            Format      = TextureFormat.RGBA8Unorm,
            Usage       = TextureUsage.Sampled,
            DebugName   = "NotBSUI.White1x1",
        });

        Span<byte> pixel = stackalloc byte[4] { 255, 255, 255, 255 };
        _device.UploadTexture(_whiteTexture, pixel);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Per-frame render
    // ─────────────────────────────────────────────────────────────────────

    public void Resize(int width, int height)
    {
        _projection = Matrix4x4.CreateOrthographicOffCenter(0, width, height, 0, -1, 1);
    }

    private int _debugFrames = 120; // brief logging window

    public void Render(IRHICommandBuffer cmd, NotBSUI ui)
    {
        var commands = ui.GetDrawCommands();
        TessellateDrawCommands(commands);

        if (_vertices.Count == 0)
        {
            if (_debugFrames-- > 0)
                Console.WriteLine("[NotBSUIRenderer] No UI vertices this frame.");
            _overflowed = false;
            return;
        }

        // ── upload geometry ───────────────────────────────────────────────
        var vertexBytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(_vertices));
        _device.UpdateBuffer(_vertexBuffer!, vertexBytes);

        var indexBytes  = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(_indices));
        _device.UpdateBuffer(_indexBuffer!, indexBytes);

        var projBytes   = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref _projection, 1));
        _device.UpdateBuffer(_uniformBuffer!, projBytes);

        // ── bind pipeline + buffers ───────────────────────────────────────
        cmd.SetPipeline(_pipeline!);
        cmd.SetVertexBuffer(_vertexBuffer!, 0, 0);
        cmd.SetIndexBuffer(_indexBuffer!, IndexType.UInt16, 0);
        cmd.SetUniformBuffer(_uniformBuffer!, 1, 0);
        
        if (FontAtlas != null)
            cmd.SetTexture(FontAtlas.AtlasTexture, 0, 0);
        else if (_whiteTexture != null)
            cmd.SetTexture(_whiteTexture, 0, 0);

        cmd.DrawIndexed((uint)_indices.Count);

        if (_debugFrames-- > 0)
        {
            Console.WriteLine($"[NotBSUIRenderer] cmds={commands.Count} verts={_vertices.Count} indices={_indices.Count} overflow={_overflowed}");
        }

        // ── reset ─────────────────────────────────────────────────────────
        _vertices.Clear();
        _indices.Clear();
        _overflowed = false;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Tessellation
    // ─────────────────────────────────────────────────────────────────────

    private void TessellateDrawCommands(IReadOnlyList<NotBSUI.DrawCommand> commands)
    {
        foreach (var cmd in commands)
        {
            switch (cmd.Type)
            {
                case NotBSUI.DrawCommandType.RectFilled:
                    AddFilledRect(cmd.Position, cmd.Size, cmd.Color);
                    break;

                case NotBSUI.DrawCommandType.Rect:
                    AddRectOutline(cmd.Position, cmd.Size, cmd.Color);
                    break;

                case NotBSUI.DrawCommandType.CircleFilled:
                    AddFilledCircle(cmd.Position, cmd.Radius, cmd.Color);
                    break;

                case NotBSUI.DrawCommandType.Circle:
                    AddCircleOutline(cmd.Position, cmd.Radius, cmd.Color);
                    break;

                case NotBSUI.DrawCommandType.Line:
                    AddLine(cmd.Position, cmd.Size, cmd.Color);
                    break;

                case NotBSUI.DrawCommandType.Text:
                    if (cmd.Text is { Length: > 0 })
                        AddText(cmd.Position, cmd.Text, cmd.Color);
                    break;
            }
        }

        if (_debugFrames-- > 0)
        {
            Console.WriteLine($"[NotBSUIRenderer] cmds={commands.Count} verts={_vertices.Count} indices={_indices.Count} overflow={_overflowed}");
            // draw a debug overlay to guarantee something visible
            AddFilledRect(new Vector2(50, 50), new Vector2(300, 180), new Vector4(0.1f, 0.8f, 0.3f, 1f));
        }
        else if (_overflowed)
        {
            Console.WriteLine("[NotBSUIRenderer] Vertex/index budget exceeded — some draw calls were skipped.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Geometry helpers
    // ─────────────────────────────────────────────────────────────────────

    private bool CanFit(int vertCount, int idxCount)
    {
        bool ok = _vertices.Count + vertCount <= MaxVertices
               && _indices.Count  + idxCount  <= MaxIndices;
        if (!ok) _overflowed = true;
        return ok;
    }

    // ── solid quad (two triangles) ─────────────────────────────────────────
    private void AddSolidQuad(Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3, Vector4 color)
    {
        if (!CanFit(4, 6)) return;

        ushort b = (ushort)_vertices.Count;
        _vertices.Add(new UIVertex(v0, color));
        _vertices.Add(new UIVertex(v1, color));
        _vertices.Add(new UIVertex(v2, color));
        _vertices.Add(new UIVertex(v3, color));

        _indices.Add(b);               _indices.Add((ushort)(b + 1)); _indices.Add((ushort)(b + 2));
        _indices.Add(b);               _indices.Add((ushort)(b + 2)); _indices.Add((ushort)(b + 3));
    }

    private void AddFilledRect(Vector2 pos, Vector2 size, Vector4 color)
    {
        AddSolidQuad(
            pos,
            new Vector2(pos.X + size.X, pos.Y),
            new Vector2(pos.X + size.X, pos.Y + size.Y),
            new Vector2(pos.X,          pos.Y + size.Y),
            color);
    }

    private void AddRectOutline(Vector2 pos, Vector2 size, Vector4 color)
    {
        float t = LineThickness;
        // top / bottom / left / right borders
        AddFilledRect(new Vector2(pos.X,                      pos.Y),          new Vector2(size.X, t),         color);
        AddFilledRect(new Vector2(pos.X,                      pos.Y + size.Y - t), new Vector2(size.X, t),     color);
        AddFilledRect(new Vector2(pos.X,                      pos.Y),          new Vector2(t, size.Y),         color);
        AddFilledRect(new Vector2(pos.X + size.X - t,         pos.Y),          new Vector2(t, size.Y),         color);
    }

    // ── thick line (quaded) ───────────────────────────────────────────────
    private void AddLine(Vector2 start, Vector2 end, Vector4 color)
    {
        var dir = end - start;
        if (dir.LengthSquared() < 0.0001f) return;

        var normal = Vector2.Normalize(new Vector2(-dir.Y, dir.X)) * (LineThickness * 0.5f);
        AddSolidQuad(start + normal, end + normal, end - normal, start - normal, color);
    }

    // ── circle ────────────────────────────────────────────────────────────
    private void AddFilledCircle(Vector2 centre, float radius, Vector4 color)
    {
        if (radius <= 0) return;
        int  seg  = CircleSegments;
        if (!CanFit(seg + 1, seg * 3)) return;

        float step        = MathF.PI * 2f / seg;
        ushort centreIdx  = (ushort)_vertices.Count;
        _vertices.Add(new UIVertex(centre, color));

        for (int i = 0; i < seg; i++)
        {
            float a = step * i;
            _vertices.Add(new UIVertex(
                new Vector2(centre.X + MathF.Cos(a) * radius,
                            centre.Y + MathF.Sin(a) * radius), color));
        }

        for (int i = 0; i < seg; i++)
        {
            _indices.Add(centreIdx);
            _indices.Add((ushort)(centreIdx + 1 + i));
            _indices.Add((ushort)(centreIdx + 1 + (i + 1) % seg));
        }
    }

    private void AddCircleOutline(Vector2 centre, float radius, Vector4 color)
    {
        if (radius <= 0) return;
        int   seg  = CircleSegments;
        float step = MathF.PI * 2f / seg;
        var   prev = new Vector2(centre.X + radius, centre.Y);
        for (int i = 1; i <= seg; i++)
        {
            float a    = step * i;
            var   next = new Vector2(centre.X + MathF.Cos(a) * radius,
                                     centre.Y + MathF.Sin(a) * radius);
            AddLine(prev, next, color);
            prev = next;
        }
    }

    // ── text ──────────────────────────────────────────────────────────────
    private void AddText(Vector2 pos, string text, Vector4 color)
    {
        if (FontAtlas == null)
        {
            AddTextFallback(pos, text, color);
            return;
        }

        float x = pos.X;
        // Text is anchored top-left. Font atlas metrics are based on baseline.
        // We push the baseline down by LineHeight so text renders below 'pos'.
        float yBaseline = pos.Y + FontAtlas.LineHeight;

        foreach (char c in text)
        {
            if (c == '\n')
            {
                x = pos.X;
                yBaseline += FontAtlas.LineHeight;
                continue;
            }

            if (FontAtlas.TryGetGlyphQuad(c, ref x, yBaseline, out var p0, out var p1, out var uv0, out var uv1))
            {
                if (_debugFrames > 0)
                {
                    Console.WriteLine($"[Text] '{c}' bounds: p0={p0.X},{p0.Y} p1={p1.X},{p1.Y} uv0={uv0.X},{uv0.Y} uv1={uv1.X},{uv1.Y}");
                }
                AddGlyphQuad(p0, p1, uv0, uv1, color);
            }
        }
    }

    /// <summary>
    /// Emits a textured quad for a single glyph using Mode=1.
    /// </summary>
    private void AddGlyphQuad(Vector2 p0, Vector2 p1, Vector2 uv0, Vector2 uv1, Vector4 color)
    {
        if (!CanFit(4, 6)) return;

        ushort b = (ushort)_vertices.Count;
        
        // Mode=1 ensures the fragment shader uses the atlas texture
        // p0 is top-left, p1 is bottom-right
        _vertices.Add(new UIVertex(new Vector2(p0.X, p0.Y), color, new Vector2(uv0.X, uv0.Y))); // TL
        _vertices.Add(new UIVertex(new Vector2(p1.X, p0.Y), color, new Vector2(uv1.X, uv0.Y))); // TR
        _vertices.Add(new UIVertex(new Vector2(p1.X, p1.Y), color, new Vector2(uv1.X, uv1.Y))); // BR
        _vertices.Add(new UIVertex(new Vector2(p0.X, p1.Y), color, new Vector2(uv0.X, uv1.Y))); // BL

        _indices.Add(b);               _indices.Add((ushort)(b + 1)); _indices.Add((ushort)(b + 2));
        _indices.Add(b);               _indices.Add((ushort)(b + 2)); _indices.Add((ushort)(b + 3));
    }

    /// <summary>
    /// Fallback used when no font atlas is loaded: each character is a small
    /// solid-coloured rectangle so layout is still visible.
    /// </summary>
    private void AddTextFallback(Vector2 pos, string text, Vector4 color)
    {
        const float GlyphW = 8f;
        const float GlyphH = 12f;
        const float Advance = 9f;

        float x = pos.X;
        foreach (char c in text)
        {
            if (c == '\n') { x = pos.X; pos.Y += GlyphH + 2f; continue; }
            AddFilledRect(new Vector2(x, pos.Y), new Vector2(GlyphW, GlyphH), color);
            x += Advance;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // IDisposable
    // ─────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _pipeline?.Dispose();
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _uniformBuffer?.Dispose();
        _whiteTexture?.Dispose();
    }
}
