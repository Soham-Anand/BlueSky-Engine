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
    private const int  CornerSegments = 8;  // Segments per rounded corner arc

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

        if (_device.Backend == RHIBackend.Metal)
        {
            // Metal uses compiled .metallib files
            string[] searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", "simple_ui.metallib"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Editor", "Shaders", "simple_ui.metallib"),
                Path.Combine(Directory.GetCurrentDirectory(), "Editor", "Shaders", "simple_ui.metallib"),
                Path.Combine(Directory.GetCurrentDirectory(), "BlueSkyEngine", "Editor", "Shaders", "simple_ui.metallib"),
            };

            string? found = Array.Find(searchPaths, File.Exists);
            if (found != null)
            {
                bytecode = File.ReadAllBytes(found);
                Console.WriteLine($"[NotBSUIRenderer] Loaded Metal library from {found} ({bytecode.Length} bytes)");
            }
            else
            {
                Console.WriteLine($"[NotBSUIRenderer] WARNING: simple_ui.metallib not found. Searched:");
                foreach (var p in searchPaths) Console.WriteLine($"  {p}");
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

    public void RenderTexture(IRHICommandBuffer cmd, IRHITexture texture, float x, float y, float w, float h)
    {
        // Draw an immediate quad with the given texture using the UI pipeline
        var verts = new UIVertex[4];
        var color = new Vector4(1, 1, 1, 1);
        // Mode = 2: full texture render
        verts[0] = new UIVertex(new Vector2(x, y),         color, new Vector2(0, 0)) { Mode = 2 }; // TL
        verts[1] = new UIVertex(new Vector2(x + w, y),     color, new Vector2(1, 0)) { Mode = 2 }; // TR
        verts[2] = new UIVertex(new Vector2(x + w, y + h), color, new Vector2(1, 1)) { Mode = 2 }; // BR
        verts[3] = new UIVertex(new Vector2(x, y + h),     color, new Vector2(0, 1)) { Mode = 2 }; // BL

        ushort[] indices = { 0, 1, 2, 0, 2, 3 };

        var vertexBytes = MemoryMarshal.AsBytes(verts.AsSpan());
        _device.UpdateBuffer(_vertexBuffer!, vertexBytes);

        var indexBytes = MemoryMarshal.AsBytes(indices.AsSpan());
        _device.UpdateBuffer(_indexBuffer!, indexBytes);

        var projBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref _projection, 1));
        _device.UpdateBuffer(_uniformBuffer!, projBytes);

        cmd.SetPipeline(_pipeline!);
        cmd.SetVertexBuffer(_vertexBuffer!, 0, 0);
        cmd.SetIndexBuffer(_indexBuffer!, IndexType.UInt16, 0);
        cmd.SetUniformBuffer(_uniformBuffer!, 1, 0);

        // Bind the viewport texture (slot 0, same as font atlas in Render())
        cmd.SetTexture(texture, 0, 0);
        cmd.DrawIndexed(6);
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

                case NotBSUI.DrawCommandType.GradientRectFilled:
                    AddGradientRect(cmd.Position, cmd.Size, cmd.Color, cmd.ColorEnd, cmd.GradientVertical);
                    break;

                case NotBSUI.DrawCommandType.RoundedRectFilled:
                    AddRoundedRect(cmd.Position, cmd.Size, cmd.Color, cmd.CornerRadius);
                    break;

                case NotBSUI.DrawCommandType.RoundedGradientRectFilled:
                    AddRoundedGradientRect(cmd.Position, cmd.Size, cmd.Color, cmd.ColorEnd, cmd.CornerRadius, cmd.GradientVertical);
                    break;
            }
        }

        if (_debugFrames-- > 0)
        {
            Console.WriteLine($"[NotBSUIRenderer] cmds={commands.Count} verts={_vertices.Count} indices={_indices.Count} overflow={_overflowed}");
        }
        
        if (_overflowed)
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

    // ── gradient quad (different colors at each corner for interpolation) ──
    private void AddGradientQuad(Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3,
                                  Vector4 c0, Vector4 c1, Vector4 c2, Vector4 c3)
    {
        if (!CanFit(4, 6)) return;

        ushort b = (ushort)_vertices.Count;
        _vertices.Add(new UIVertex(v0, c0));
        _vertices.Add(new UIVertex(v1, c1));
        _vertices.Add(new UIVertex(v2, c2));
        _vertices.Add(new UIVertex(v3, c3));

        _indices.Add(b);               _indices.Add((ushort)(b + 1)); _indices.Add((ushort)(b + 2));
        _indices.Add(b);               _indices.Add((ushort)(b + 2)); _indices.Add((ushort)(b + 3));
    }

    private void AddGradientRect(Vector2 pos, Vector2 size, Vector4 colorStart, Vector4 colorEnd, bool vertical)
    {
        var tl = pos;
        var tr = new Vector2(pos.X + size.X, pos.Y);
        var br = new Vector2(pos.X + size.X, pos.Y + size.Y);
        var bl = new Vector2(pos.X,          pos.Y + size.Y);

        if (vertical)
        {
            // Top = colorStart, Bottom = colorEnd
            AddGradientQuad(tl, tr, br, bl, colorStart, colorStart, colorEnd, colorEnd);
        }
        else
        {
            // Left = colorStart, Right = colorEnd
            AddGradientQuad(tl, tr, br, bl, colorStart, colorEnd, colorEnd, colorStart);
        }
    }

    // ── rounded rectangle (CPU-tessellated corner arcs) ────────────────────
    private void AddRoundedRect(Vector2 pos, Vector2 size, Vector4 color, float radius)
    {
        // Clamp radius so it doesn't exceed half the smallest dimension
        radius = MathF.Min(radius, MathF.Min(size.X * 0.5f, size.Y * 0.5f));
        if (radius < 0.5f) { AddFilledRect(pos, size, color); return; }

        AddRoundedRectInternal(pos, size, radius,
            color, color, color, color);
    }

    private void AddRoundedGradientRect(Vector2 pos, Vector2 size,
                                         Vector4 colorStart, Vector4 colorEnd,
                                         float radius, bool vertical)
    {
        radius = MathF.Min(radius, MathF.Min(size.X * 0.5f, size.Y * 0.5f));
        if (radius < 0.5f) { AddGradientRect(pos, size, colorStart, colorEnd, vertical); return; }

        Vector4 cTL, cTR, cBR, cBL;
        if (vertical)
        {
            cTL = colorStart; cTR = colorStart;
            cBL = colorEnd;   cBR = colorEnd;
        }
        else
        {
            cTL = colorStart; cBL = colorStart;
            cTR = colorEnd;   cBR = colorEnd;
        }

        AddRoundedRectInternal(pos, size, radius, cTL, cTR, cBR, cBL);
    }

    /// <summary>
    /// Tessellates a rounded rectangle with per-corner colors.
    /// Uses a centre vertex + triangle fan for each corner arc,
    /// plus fill quads for the body.
    /// </summary>
    private void AddRoundedRectInternal(Vector2 pos, Vector2 size, float r,
                                         Vector4 cTL, Vector4 cTR, Vector4 cBR, Vector4 cBL)
    {
        float x0 = pos.X, y0 = pos.Y;
        float x1 = pos.X + size.X, y1 = pos.Y + size.Y;
        int seg = CornerSegments;

        // Center color (average of corners)
        var cCenter = (cTL + cTR + cBR + cBL) * 0.25f;

        // ── 3 body fill rects (no corners) ─────────────────────────────
        // Top strip (between TL and TR arcs)
        AddGradientQuad(
            new Vector2(x0 + r, y0), new Vector2(x1 - r, y0),
            new Vector2(x1 - r, y0 + r), new Vector2(x0 + r, y0 + r),
            cTL, cTR, Vector4.Lerp(cTR, cBR, r / size.Y), Vector4.Lerp(cTL, cBL, r / size.Y));

        // Middle strip (full width, between corner rows)
        float midT = r / size.Y;
        float midB = (size.Y - r) / size.Y;
        AddGradientQuad(
            new Vector2(x0, y0 + r), new Vector2(x1, y0 + r),
            new Vector2(x1, y1 - r), new Vector2(x0, y1 - r),
            Vector4.Lerp(cTL, cBL, midT), Vector4.Lerp(cTR, cBR, midT),
            Vector4.Lerp(cTR, cBR, midB), Vector4.Lerp(cTL, cBL, midB));

        // Bottom strip (between BL and BR arcs)
        AddGradientQuad(
            new Vector2(x0 + r, y1 - r), new Vector2(x1 - r, y1 - r),
            new Vector2(x1 - r, y1), new Vector2(x0 + r, y1),
            Vector4.Lerp(cTL, cBL, midB), Vector4.Lerp(cTR, cBR, midB), cBR, cBL);

        // ── 4 corner arcs (triangle fans) ─────────────────────────────
        AddCornerArc(new Vector2(x0 + r, y0 + r), r, MathF.PI, MathF.PI * 1.5f, seg, cTL);       // TL
        AddCornerArc(new Vector2(x1 - r, y0 + r), r, MathF.PI * 1.5f, MathF.PI * 2f, seg, cTR);  // TR
        AddCornerArc(new Vector2(x1 - r, y1 - r), r, 0f, MathF.PI * 0.5f, seg, cBR);             // BR
        AddCornerArc(new Vector2(x0 + r, y1 - r), r, MathF.PI * 0.5f, MathF.PI, seg, cBL);       // BL
    }

    private void AddCornerArc(Vector2 centre, float radius, float startAngle, float endAngle, int segments, Vector4 color)
    {
        if (!CanFit(segments + 1, segments * 3)) return;

        float step = (endAngle - startAngle) / segments;
        ushort centreIdx = (ushort)_vertices.Count;
        _vertices.Add(new UIVertex(centre, color));

        for (int i = 0; i <= segments; i++)
        {
            float a = startAngle + step * i;
            _vertices.Add(new UIVertex(
                new Vector2(centre.X + MathF.Cos(a) * radius,
                            centre.Y + MathF.Sin(a) * radius), color));
        }

        for (int i = 0; i < segments; i++)
        {
            _indices.Add(centreIdx);
            _indices.Add((ushort)(centreIdx + 1 + i));
            _indices.Add((ushort)(centreIdx + 2 + i));
        }
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
