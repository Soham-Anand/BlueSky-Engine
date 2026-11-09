using System.Numerics;
using System.Runtime.InteropServices;
using BlueSky.BSR.RHI;

namespace BlueSky.Editor.UI;

/// <summary>
/// GPU renderer for EditorUI draw commands.
///
/// One pipeline handles both solid geometry (rects, lines, circles) and
/// glyph quads.  The per-vertex <c>Mode</c> field tells the fragment shader
/// whether to use the vertex colour directly (Mode=0) or to sample the font
/// atlas for coverage and multiply against the colour (Mode=1).
///
/// Every frame the renderer bins all draw commands into a single vertex +
/// index buffer, uploads once, and draws once — keeping GPU overhead minimal.
/// </summary>
public sealed class EditorUIRenderer : IDisposable
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
    // M4: per-frame dynamic buffers are ringed ×FramesInFlight so frame N+1
    // CPU uploads never race frame N GPU reads once submits go no-wait.
    // A second UI renderer (the Echo window) gets its own three slots so the
    // two windows never write the same buffer in one frame.
    private const int FrameRing = 6;
    private readonly int _slotBase;
    private readonly IRHIBuffer?[] _vertexBuffers        = new IRHIBuffer?[FrameRing];
    private readonly IRHIBuffer?[] _indexBuffers         = new IRHIBuffer?[FrameRing];
    private readonly IRHIBuffer?[] _textureVertexBuffers = new IRHIBuffer?[FrameRing];
    private readonly IRHIBuffer?[] _textureIndexBuffers  = new IRHIBuffer?[FrameRing];
    private readonly IRHIBuffer?[] _uniformBuffers       = new IRHIBuffer?[FrameRing];
    private          IRHITexture?  _whiteTexture;
    private          IRHISampler?  _uiSampler; // M2: RHI sampler bound at slot 0
    
    // ── font rendering ──────────────────────────────────────────────────────
    public           FontAtlas?    FontAtlas { get; set; }

    // ── per-frame CPU buffers ──────────────────────────────────────────────
    private readonly List<UIVertex> _vertices = new(4096);
    private readonly List<ushort>   _indices  = new(8192);
    private          Matrix4x4      _projection;
    private          bool           _overflowed;
    // Projection upload dedup: Render() and RenderTexture() share one UB per
    // frame — a second upload would rewrite identical bytes.
    private          long           _projUploadedFrame = -1;

    // ─────────────────────────────────────────────────────────────────────
    // Construction
    // ─────────────────────────────────────────────────────────────────────

    public EditorUIRenderer(IRHIDevice device, int slotBase = 0)
    {
        _device = device;
        _slotBase = slotBase;
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
            DebugName         = "EditorUI",
        });
    }

    private const string SimpleUI_HLSL = @"
cbuffer Uniforms : register(b1) {
    float4x4 projection;
};

Texture2D fontAtlasTex : register(t0);
SamplerState fontAtlasSampler : register(s0);

struct VS_INPUT {
    float2 position : POSITION;
    float4 color    : NORMAL;
    float2 uv       : TEXCOORD0;
    float  mode     : COLOR0;
};

struct VS_OUTPUT {
    float4 position : SV_POSITION;
    float4 color    : COLOR0;
    float2 uv       : TEXCOORD0;
    float  mode     : TEXCOORD1;
};

VS_OUTPUT vs_ui(VS_INPUT input) {
    VS_OUTPUT output;
    output.position = mul(projection, float4(input.position, 0.0, 1.0));
    output.color = input.color;
    output.uv = input.uv;
    output.mode = input.mode;
    return output;
}

float4 fs_ui(VS_OUTPUT input) : SV_Target {
    if (input.mode > 1.5) {
        return fontAtlasTex.Sample(fontAtlasSampler, input.uv);
    } else if (input.mode > 0.5) {
        float coverage = fontAtlasTex.Sample(fontAtlasSampler, input.uv).r;
        if (coverage < 0.01) discard;
        return float4(input.color.rgb, input.color.a * coverage);
    }
    return input.color;
}
";

    private ShaderDesc LoadShader(ShaderStage stage, string entryPoint)
    {
        byte[] bytecode = Array.Empty<byte>();

        if (_device.Backend == RHIBackend.Metal)
        {
            // Metal uses compiled .metallib files
            string[] searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Editor", "Shaders", "simple_ui.metallib"),
                Path.Combine(Directory.GetCurrentDirectory(), "Editor", "Shaders", "simple_ui.metallib"),
                Path.Combine(Directory.GetCurrentDirectory(), "BlueSkyEngine", "Editor", "Shaders", "simple_ui.metallib"),
            };

            string? found = Array.Find(searchPaths, File.Exists);
            if (found != null)
            {
                bytecode = File.ReadAllBytes(found);
                Console.WriteLine($"[EditorUIRenderer] Loaded Metal library from {found} ({bytecode.Length} bytes)");
            }
            else
            {
                Console.WriteLine($"[EditorUIRenderer] WARNING: simple_ui.metallib not found. Searched:");
                foreach (var p in searchPaths) Console.WriteLine($"  {p}");
            }
        }
        else if (_device.Backend == RHIBackend.DirectX11)
        {
            // DX11: Compile shader from embedded HLSL at runtime using D3DCompile
            string profile = stage == ShaderStage.Vertex ? "vs_4_0" : "ps_4_0";
            IntPtr pSrc = Marshal.StringToHGlobalAnsi(SimpleUI_HLSL);
            
            int hr = BlueSky.BSR.RHI.DirectX11.D3D11Interop.D3DCompile(
                pSrc,
                (nuint)SimpleUI_HLSL.Length,
                "simple_ui.hlsl",
                IntPtr.Zero,
                IntPtr.Zero,
                entryPoint,
                profile,
                0, 0,
                out IntPtr blob,
                out IntPtr errorBlob);
                
            Marshal.FreeHGlobal(pSrc);

            if (hr < 0)
            {
                if (errorBlob != IntPtr.Zero)
                {
                    IntPtr pErrorStr = BlueSky.BSR.RHI.DirectX11.D3D11Interop.GetBufferPointer(errorBlob);
                    string? errorMsg = Marshal.PtrToStringAnsi(pErrorStr);
                    Console.WriteLine($"[EditorUIRenderer] DX11 Shader Compile Error ({entryPoint}):\n{errorMsg}");
                    Marshal.Release(errorBlob);
                }
                Console.WriteLine($"[EditorUIRenderer] D3DCompile failed for {entryPoint} with HRESULT 0x{hr:X8}");
            }
            else
            {
                if (blob != IntPtr.Zero)
                {
                    IntPtr pCode = BlueSky.BSR.RHI.DirectX11.D3D11Interop.GetBufferPointer(blob);
                    nuint size = BlueSky.BSR.RHI.DirectX11.D3D11Interop.GetBufferSize(blob);
                    bytecode = new byte[size];
                    Marshal.Copy(pCode, bytecode, 0, (int)size);
                    Marshal.Release(blob);
                }
                if (errorBlob != IntPtr.Zero) Marshal.Release(errorBlob);
            }
        }
        else if (_device.Backend == RHIBackend.Vulkan)
        {
            string stageSuffix = stage == ShaderStage.Vertex ? "vert" : "frag";
            string fileName = $"simple_ui.{stageSuffix}.spv";
            string[] searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Editor", "Shaders", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "Editor", "Shaders", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "BlueSkyEngine", "Editor", "Shaders", fileName),
            };

            string? found = Array.Find(searchPaths, File.Exists);
            if (found != null)
            {
                bytecode = File.ReadAllBytes(found);
                Console.WriteLine($"[EditorUIRenderer] Loaded Vulkan shader {fileName} from {found} ({bytecode.Length} bytes)");
            }
            else
            {
                Console.WriteLine($"[EditorUIRenderer] WARNING: {fileName} not found. Vulkan UI rendering requires SPIR-V bytecode.");
            }
        }

        return new ShaderDesc
        {
            Stage      = stage,
            EntryPoint = entryPoint,
            Bytecode   = bytecode,
        };
    }

    private int FrameSlot()
    {
        var inner = (_device as IRHIWrapped<IRHIDevice>)?.Inner;
        if (inner is BlueSky.BSR.RHI.Metal.MetalDevice metal)
            return (_slotBase + metal.FrameIndex) % FrameRing;
        return _slotBase % FrameRing;
    }

    private void UploadProjectionOnce(int slot)
    {
        long frame = BlueSky.BSR.Strata.StrataBenchmark.FrameIndex;
        if (_projUploadedFrame == frame) return;
        var projBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref _projection, 1));
        _device.UpdateBuffer(_uniformBuffers[slot]!, projBytes);
        _projUploadedFrame = frame;
    }

    private void CreateBuffers()
    {
        for (int i = 0; i < FrameRing; i++)
        {
            _vertexBuffers[i] = _device.CreateBuffer(new BufferDesc
            {
                Size       = (ulong)(MaxVertices * UIVertex.Stride),
                Usage      = BufferUsage.Vertex,
                MemoryType = MemoryType.CpuToGpu,
                DebugName  = $"EditorUI.VB{i}",
            });

            _indexBuffers[i] = _device.CreateBuffer(new BufferDesc
            {
                Size       = (ulong)(MaxIndices * sizeof(ushort)),
                Usage      = BufferUsage.Index,
                MemoryType = MemoryType.CpuToGpu,
                DebugName  = $"EditorUI.IB{i}",
            });

            _textureVertexBuffers[i] = _device.CreateBuffer(new BufferDesc
            {
                Size       = (ulong)(4 * UIVertex.Stride),
                Usage      = BufferUsage.Vertex,
                MemoryType = MemoryType.CpuToGpu,
                DebugName  = $"EditorUI.TextureVB{i}",
            });

            _textureIndexBuffers[i] = _device.CreateBuffer(new BufferDesc
            {
                Size       = 6 * sizeof(ushort),
                Usage      = BufferUsage.Index,
                MemoryType = MemoryType.CpuToGpu,
                DebugName  = $"EditorUI.TextureIB{i}",
            });

            _uniformBuffers[i] = _device.CreateBuffer(new BufferDesc
            {
                Size       = 64,
                Usage      = BufferUsage.Uniform,
                MemoryType = MemoryType.CpuToGpu,
                DebugName  = $"EditorUI.UB{i}",
            });
        }
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
            DebugName   = "EditorUI.White1x1",
        });

        Span<byte> pixel = stackalloc byte[4] { 255, 255, 255, 255 };
        _device.UploadTexture(_whiteTexture, pixel);

        // M2: one linear-clamp sampler for font glyphs + viewport blits (all backends).
        _uiSampler = _device.CreateSampler(SamplerDesc.LinearClamp);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Per-frame render
    // ─────────────────────────────────────────────────────────────────────

    public void Resize(int width, int height)
    {
        _projection = Matrix4x4.CreateOrthographicOffCenter(0, width, height, 0, -1, 1);
    }

    private int _debugFrames = 1; // brief logging window

    public void Render(IRHICommandBuffer cmd, EditorUI ui)
    {
        if (FontAtlas != null)
        {
            ui.MeasureTextWidth = text => FontAtlas.MeasureWidth(text.AsSpan());
            ui.TextLineHeight = FontAtlas.LineHeight;
        }

        var commands = ui.GetDrawCommands();
        TessellateDrawCommands(commands);

        if (_vertices.Count == 0)
        {
            if (_debugFrames-- > 0)
                Console.WriteLine("[EditorUIRenderer] No UI vertices this frame.");
            _overflowed = false;
            return;
        }

        // ── upload geometry (M4: this frame's ring slot) ────────────────────
        int slot = FrameSlot();
        var vertexBytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(_vertices));
        _device.UpdateBuffer(_vertexBuffers[slot]!, vertexBytes);

        var indexBytes  = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(_indices));
        _device.UpdateBuffer(_indexBuffers[slot]!, indexBytes);

        UploadProjectionOnce(slot);

        // ── bind pipeline + buffers ───────────────────────────────────────
        cmd.SetPipeline(_pipeline!);
        cmd.SetVertexBuffer(_vertexBuffers[slot]!, 0, 0);
        cmd.SetIndexBuffer(_indexBuffers[slot]!, IndexType.UInt16, 0);
        cmd.SetUniformBuffer(_uniformBuffers[slot]!, 1, 0);
        
        if (FontAtlas != null)
            cmd.SetTexture(FontAtlas.AtlasTexture, 0, 0);
        else if (_whiteTexture != null)
            cmd.SetTexture(_whiteTexture, 0, 0);

        if (_uiSampler != null)
            cmd.SetSampler(_uiSampler, 0, 0);

        cmd.DrawIndexed((uint)_indices.Count);

        if (_debugFrames-- > 0)
        {
            Console.WriteLine($"[EditorUIRenderer] cmds={commands.Count} verts={_vertices.Count} indices={_indices.Count} overflow={_overflowed}");
        }

        // ── reset ─────────────────────────────────────────────────────────
        _vertices.Clear();
        _indices.Clear();
        _overflowed = false;
    }

    public void RenderTexture(IRHICommandBuffer cmd, IRHITexture texture, float x, float y, float w, float h)
    {
        // Snap the composited viewport quad to whole pixels so the editor does
        // not introduce a shimmer/heat-haze look when sampling the offscreen
        // render target into the docked panel.
        x = MathF.Round(x);
        y = MathF.Round(y);
        w = MathF.Round(w);
        h = MathF.Round(h);

        // Draw an immediate quad with the given texture using the UI pipeline.
        // Stack-only verts/indices — this runs every frame, no heap allocs.
        Span<UIVertex> verts = stackalloc UIVertex[4];
        var color = new Vector4(1, 1, 1, 1);
        // Mode = 2: full texture render
        verts[0] = new UIVertex(new Vector2(x, y),         color, new Vector2(0, 0)) { Mode = 2 }; // TL
        verts[1] = new UIVertex(new Vector2(x + w, y),     color, new Vector2(1, 0)) { Mode = 2 }; // TR
        verts[2] = new UIVertex(new Vector2(x + w, y + h), color, new Vector2(1, 1)) { Mode = 2 }; // BR
        verts[3] = new UIVertex(new Vector2(x, y + h),     color, new Vector2(0, 1)) { Mode = 2 }; // BL

        Span<ushort> indices = stackalloc ushort[6] { 0, 1, 2, 0, 2, 3 };

        var vertexBytes = MemoryMarshal.AsBytes(verts);
        int tslot = FrameSlot();
        _device.UpdateBuffer(_textureVertexBuffers[tslot]!, vertexBytes);

        var indexBytes = MemoryMarshal.AsBytes(indices);
        _device.UpdateBuffer(_textureIndexBuffers[tslot]!, indexBytes);

        // Projection is uploaded by Render() in the same frame — skip the
        // duplicate upload, but always (re)bind: same slot, same pass.
        UploadProjectionOnce(tslot);

        cmd.SetPipeline(_pipeline!);
        cmd.SetVertexBuffer(_textureVertexBuffers[tslot]!, 0, 0);
        cmd.SetIndexBuffer(_textureIndexBuffers[tslot]!, IndexType.UInt16, 0);
        cmd.SetUniformBuffer(_uniformBuffers[tslot]!, 1, 0);

        // Bind the viewport texture (slot 0, same as font atlas in Render())
        cmd.SetTexture(texture, 0, 0);

        if (_uiSampler != null)
            cmd.SetSampler(_uiSampler, 0, 0);

        cmd.DrawIndexed(6);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Tessellation
    // ─────────────────────────────────────────────────────────────────────

    private void TessellateDrawCommands(IReadOnlyList<EditorUI.DrawCommand> commands)
    {
        foreach (var cmd in commands)
        {
            switch (cmd.Type)
            {
                case EditorUI.DrawCommandType.RectFilled:
                    if (TryClipQuad(cmd.Position, cmd.Size, cmd.ClipRect, out var rfPos, out var rfSize))
                        AddFilledRect(rfPos, rfSize, cmd.Color);
                    break;

                case EditorUI.DrawCommandType.Rect:
                    if (TryClipQuad(cmd.Position, cmd.Size, cmd.ClipRect, out var roPos, out var roSize))
                        AddRectOutline(roPos, roSize, cmd.Color);
                    break;

                case EditorUI.DrawCommandType.CircleFilled:
                    if (CircleVisible(cmd.Position, cmd.Radius, cmd.ClipRect))
                        AddFilledCircle(cmd.Position, cmd.Radius, cmd.Color);
                    break;

                case EditorUI.DrawCommandType.Circle:
                    if (CircleVisible(cmd.Position, cmd.Radius, cmd.ClipRect))
                        AddCircleOutline(cmd.Position, cmd.Radius, cmd.Color);
                    break;

                case EditorUI.DrawCommandType.Line:
                    if (LineVisible(cmd.Position, cmd.Size, cmd.ClipRect))
                        AddLine(cmd.Position, cmd.Size, cmd.Color);
                    break;

                case EditorUI.DrawCommandType.Text:
                    if (cmd.Text is { Length: > 0 })
                        AddText(cmd.Position, cmd.Text, cmd.Color, cmd.ClipRect);
                    break;

                case EditorUI.DrawCommandType.GradientRectFilled:
                    if (TryClipQuad(cmd.Position, cmd.Size, cmd.ClipRect, out var gPos, out var gSize))
                        AddGradientRect(gPos, gSize, cmd.Color, cmd.ColorEnd, cmd.GradientVertical);
                    break;

                case EditorUI.DrawCommandType.RoundedRectFilled:
                    if (TryClipQuad(cmd.Position, cmd.Size, cmd.ClipRect, out var rrPos, out var rrSize))
                        AddRoundedRect(rrPos, rrSize, cmd.Color, cmd.CornerRadius);
                    break;

                case EditorUI.DrawCommandType.RoundedGradientRectFilled:
                    if (TryClipQuad(cmd.Position, cmd.Size, cmd.ClipRect, out var rgPos, out var rgSize))
                        AddRoundedGradientRect(rgPos, rgSize, cmd.Color, cmd.ColorEnd, cmd.CornerRadius, cmd.GradientVertical);
                    break;

                case EditorUI.DrawCommandType.TriangleFilled:
                    AddFilledTriangle(cmd.Position, cmd.P1, cmd.P2, cmd.Color);
                    break;
            }
        }

        if (_debugFrames-- > 0)
        {
            Console.WriteLine($"[EditorUIRenderer] cmds={commands.Count} verts={_vertices.Count} indices={_indices.Count} overflow={_overflowed}");
        }
        
        if (_overflowed)
        {
            Console.WriteLine("[EditorUIRenderer] Vertex/index budget exceeded — some draw calls were skipped.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // CPU clipping (scroll areas have no GPU scissor; cull here so content
    // cannot break out of its panel window).
    // ─────────────────────────────────────────────────────────────────────

    private static bool TryClipQuad(Vector2 pos, Vector2 size, Vector4? clip, out Vector2 cpos, out Vector2 csize)
    {
        if (!clip.HasValue)
        {
            cpos = pos;
            csize = size;
            return size.X > 0f && size.Y > 0f;
        }
        var c = clip.Value;
        float x1 = Math.Max(pos.X, c.X);
        float y1 = Math.Max(pos.Y, c.Y);
        float x2 = Math.Min(pos.X + size.X, c.X + c.Z);
        float y2 = Math.Min(pos.Y + size.Y, c.Y + c.W);
        cpos = new Vector2(x1, y1);
        csize = new Vector2(Math.Max(0f, x2 - x1), Math.Max(0f, y2 - y1));
        return csize.X > 0.5f && csize.Y > 0.5f;
    }

    private static bool CircleVisible(Vector2 centre, float radius, Vector4? clip)
    {
        if (radius <= 0f) return false;
        if (!clip.HasValue) return true;
        var c = clip.Value;
        return centre.X + radius >= c.X && centre.X - radius <= c.X + c.Z &&
               centre.Y + radius >= c.Y && centre.Y - radius <= c.Y + c.W;
    }

    private static bool LineVisible(Vector2 start, Vector2 end, Vector4? clip)
    {
        if (!clip.HasValue) return true;
        var c = clip.Value;
        float minX = Math.Min(start.X, end.X), maxX = Math.Max(start.X, end.X);
        float minY = Math.Min(start.Y, end.Y), maxY = Math.Max(start.Y, end.Y);
        return maxX >= c.X && minX <= c.X + c.Z && maxY >= c.Y && minY <= c.Y + c.W;
    }

    private static bool GlyphVisible(Vector2 p0, Vector2 p1, Vector4? clip)
    {
        if (!clip.HasValue) return true;
        var c = clip.Value;
        return p1.X >= c.X && p0.X <= c.X + c.Z && p1.Y >= c.Y && p0.Y <= c.Y + c.W;
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

    // ── solid triangle (icons: play, chevrons-as-fills, carets) ────────────
    private void AddFilledTriangle(Vector2 p0, Vector2 p1, Vector2 p2, Vector4 color)
    {
        if (!CanFit(3, 3)) return;

        ushort b = (ushort)_vertices.Count;
        _vertices.Add(new UIVertex(p0, color));
        _vertices.Add(new UIVertex(p1, color));
        _vertices.Add(new UIVertex(p2, color));

        _indices.Add(b); _indices.Add((ushort)(b + 1)); _indices.Add((ushort)(b + 2));
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
    private void AddText(Vector2 pos, string text, Vector4 color, Vector4? clip = null)
    {
        if (FontAtlas == null)
        {
            AddTextFallback(pos, text, color, clip);
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
                // if (_debugFrames > 0)
                // {
                //     Console.WriteLine($"[Text] '{c}' bounds: p0={p0.X},{p0.Y} p1={p1.X},{p1.Y} uv0={uv0.X},{uv0.Y} uv1={uv1.X},{uv1.Y}");
                // }
                if (GlyphVisible(p0, p1, clip))
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
    private void AddTextFallback(Vector2 pos, string text, Vector4 color, Vector4? clip = null)
    {
        const float GlyphW = 8f;
        const float GlyphH = 12f;
        const float Advance = 9f;

        float x = pos.X;
        foreach (char c in text)
        {
            if (c == '\n') { x = pos.X; pos.Y += GlyphH + 2f; continue; }
            var gp0 = new Vector2(x, pos.Y);
            var gp1 = new Vector2(x + GlyphW, pos.Y + GlyphH);
            if (GlyphVisible(gp0, gp1, clip))
                AddFilledRect(gp0, new Vector2(GlyphW, GlyphH), color);
            x += Advance;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // IDisposable
    // ─────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _pipeline?.Dispose();
        foreach (var b in _vertexBuffers) b?.Dispose();
        foreach (var b in _indexBuffers) b?.Dispose();
        foreach (var b in _textureVertexBuffers) b?.Dispose();
        foreach (var b in _textureIndexBuffers) b?.Dispose();
        foreach (var b in _uniformBuffers) b?.Dispose();
        _whiteTexture?.Dispose();
        _uiSampler?.Dispose();
    }
}
