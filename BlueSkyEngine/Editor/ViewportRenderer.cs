using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NotBSRenderer;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Math;

namespace BlueSky.Editor;

/// <summary>
/// GPU-accelerated 3D viewport renderer.  Draws a procedural sky, an
/// infinite XZ-plane grid gizmo, and ECS entities using the RHI pipeline layer.
/// </summary>
public sealed class ViewportRenderer : IDisposable
{
    // ── Uniform structure (must match Metal ViewUniforms exactly) ────────
    [StructLayout(LayoutKind.Sequential)]
    private struct ViewUniforms
    {
        public System.Numerics.Matrix4x4 View;
        public System.Numerics.Matrix4x4 Proj;
        public System.Numerics.Matrix4x4 ViewProj;
        public System.Numerics.Matrix4x4 InvViewProj;
        public System.Numerics.Matrix4x4 LightSpaceMatrix;
        public System.Numerics.Vector4   CameraPos; // Padded to Vector4 for Metal float3 alignment
        public float     Time;
        public System.Numerics.Vector3   SunDirection; // Vector3 is size 12
        private float    _pad; // total 16 bytes combined with above
    }

    // ── Entity uniform structure (model matrix) ─────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct EntityUniforms
    {
        public System.Numerics.Matrix4x4 Model;
        public System.Numerics.Vector4   Color;
    }

    // ── Conversion helpers ───────────────────────────────────────────────
    private static System.Numerics.Matrix4x4 ToSystemMatrix4x4(BlueSky.Core.Math.Matrix4x4 m)
    {
        return new System.Numerics.Matrix4x4(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44
        );
    }

    // ── Mesh GPU cache struct ─────────────────────────────────────────────
    public class MeshGPUData : IDisposable
    {
        public IRHIBuffer? VertexBuffer;
        public IRHIBuffer? IndexBuffer;
        public int IndexCount;
        
        public void Dispose()
        {
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public System.Numerics.Vector3 Position;
        public System.Numerics.Vector3 Normal;
        public System.Numerics.Vector2 UV;
    }

    // ── RHI resources ───────────────────────────────────────────────────
    private readonly IRHIDevice    _device;
    private readonly World         _world;
    private          IRHIPipeline? _skyPipeline;
    private          IRHIPipeline? _gridPipeline;
    private          IRHIPipeline? _meshPipeline;
    private          IRHIPipeline? _shadowPipeline;
    private          IRHITexture?  _shadowMap;
    private          IRHIBuffer?   _uniformBuffer;
    private          IRHIBuffer?   _entityUniformBuffer;
    
    private readonly Dictionary<string, MeshGPUData> _meshCache = new();

    private float _elapsedTime;
    private bool  _disposed;

    public ViewportRenderer(IRHIDevice device, World world)
    {
        _device = device;
        _world = world;
        CreatePipelines();
        CreateBuffers();
    }

    // ── Public API ──────────────────────────────────────────────────────

    public void PreRender(IRHICommandBuffer cmd, System.Numerics.Vector3 sunDir)
    {
        // Compute Light space bounds
        var lightProj = System.Numerics.Matrix4x4.CreateOrthographicOffCenter(-20, 20, -20, 20, 0.1f, 100f);
        var lightView = System.Numerics.Matrix4x4.CreateLookAt(-sunDir * 30f, System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitY);
        var lightViewProj = lightView * lightProj;

        cmd.BeginRenderPass(Array.Empty<IRHITexture>(), _shadowMap, ClearValue.FromDepth(1.0f));
        cmd.SetViewport(new Viewport { X = 0, Y = 0, Width = 2048, Height = 2048, MinDepth = 0, MaxDepth = 1 });
        cmd.SetScissor(new Scissor { X = 0, Y = 0, Width = 2048, Height = 2048 });
        
        cmd.SetPipeline(_shadowPipeline!);
        
        var uniforms = new ViewUniforms
        {
            LightSpaceMatrix = lightViewProj
        };
        var viewUniformSpan = MemoryMarshal.CreateSpan(ref uniforms, 1);
        _device.UpdateBuffer(_uniformBuffer!, MemoryMarshal.AsBytes(viewUniformSpan));
        cmd.SetUniformBuffer(_uniformBuffer!, 10);

        var query = _world.CreateQuery().All<TransformComponent>().All<BlueSky.Core.ECS.Builtin.StaticMeshComponent>().Build();
        var chunks = _world.GetQueryChunks(query);

        foreach (var chunk in chunks)
        {
            int transformIndex = chunk.GetComponentIndex(typeof(TransformComponent));
            int meshIndex = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.StaticMeshComponent));
            for (int i = 0; i < chunk.Count; i++)
            {
                var transform = chunk.GetComponent<TransformComponent>(i, transformIndex);
                var staticMesh = chunk.GetComponent<BlueSky.Core.ECS.Builtin.StaticMeshComponent>(i, meshIndex);

                if (string.IsNullOrEmpty(staticMesh.MeshAssetId) || !_meshCache.TryGetValue(staticMesh.MeshAssetId, out var gpuData)) continue;

                cmd.SetVertexBuffer(gpuData.VertexBuffer!, 0);
                cmd.SetIndexBuffer(gpuData.IndexBuffer!, IndexType.UInt16);
                
                var entityUniforms = new EntityUniforms
                {
                    Model = ToSystemMatrix4x4(transform.WorldMatrix),
                    Color = System.Numerics.Vector4.One
                };
                
                var uniformSpan = MemoryMarshal.CreateSpan(ref entityUniforms, 1);
                _device.UpdateBuffer(_entityUniformBuffer!, MemoryMarshal.AsBytes(uniformSpan));
                
                cmd.SetUniformBuffer(_entityUniformBuffer!, 30);
                cmd.DrawIndexed((uint)gpuData.IndexCount);
            }
        }
        cmd.EndRenderPass();
    }

    public void Render(IRHICommandBuffer cmd,
                       System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 proj,
                       System.Numerics.Vector3 cameraPos,
                       float viewportX, float viewportY,
                       float viewportW, float viewportH,
                       float deltaTime)
    {

        _elapsedTime += deltaTime;

        // ── build uniforms ────────────────────────────────────────────────
        var viewProj = view * proj;
        System.Numerics.Matrix4x4.Invert(viewProj, out var invViewProj);
        var sunDir = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.5f, 0.6f, 0.3f));
        
        var lightProj = System.Numerics.Matrix4x4.CreateOrthographicOffCenter(-20, 20, -20, 20, 0.1f, 100f);
        var lightView = System.Numerics.Matrix4x4.CreateLookAt(-sunDir * 30f, System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitY);
        var lightViewProj = lightView * lightProj;

        var uniforms = new ViewUniforms
        {
            View         = view,
            Proj         = proj,
            ViewProj     = viewProj,
            InvViewProj  = invViewProj,
            LightSpaceMatrix = lightViewProj,
            CameraPos    = new System.Numerics.Vector4(cameraPos, 1.0f),
            Time         = _elapsedTime,
            SunDirection = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.5f, 0.6f, 0.3f)), // Sun at 45° elevation
        };

        var uniformSpan = MemoryMarshal.CreateSpan(ref uniforms, 1);
        _device.UpdateBuffer(_uniformBuffer!, MemoryMarshal.AsBytes(uniformSpan));

        // ── set viewport + scissor to the panel region ────────────────────
        cmd.SetViewport(new Viewport
        {
            X = viewportX, Y = viewportY,
            Width = viewportW, Height = viewportH,
            MinDepth = 0, MaxDepth = 1
        });
        cmd.SetScissor(new Scissor
        {
            X = (int)viewportX, Y = (int)viewportY,
            Width = (uint)viewportW, Height = (uint)viewportH
        });

        // ── 0. Bind Shadow Map ───────────────────────────────────────────
        cmd.SetTexture(_shadowMap!, 1);

        // ── 1. Sky ────────────────────────────────────────────────────────
        cmd.SetPipeline(_skyPipeline!);
        cmd.SetUniformBuffer(_uniformBuffer!, 10);
        cmd.Draw(3); // fullscreen triangle

        // ── 2. Grid ──────────────────────────────────────────────────────
        cmd.SetPipeline(_gridPipeline!);
        cmd.SetUniformBuffer(_uniformBuffer!, 10);
        cmd.Draw(6); // fullscreen quad (2 tris)

        // ── 3. Entities ──────────────────────────────────────────────────
        RenderEntities(cmd, view, proj);
    }

    // ── Pipeline creation ───────────────────────────────────────────────

    private void CreatePipelines()
    {
        // Sky pipeline — no depth, draws behind everything
        _skyPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
        {
            VertexShader   = MakeShader(ShaderStage.Vertex, "vs_sky"),
            FragmentShader = MakeShader(ShaderStage.Fragment, "fs_sky"),
            VertexLayout   = new VertexLayoutDesc
            {
                Attributes = Array.Empty<VertexAttribute>(),
                Bindings   = Array.Empty<VertexBinding>(),
            },
            Topology          = PrimitiveTopology.TriangleList,
            BlendState        = BlendState.Opaque,
            DepthStencilState = new DepthStencilState
            {
                DepthTestEnabled  = false,
                DepthWriteEnabled = false,
            },
            RasterizerState = new RasterizerState { CullMode = CullMode.None },
            ColorFormats    = new[] { TextureFormat.BGRA8Unorm },
            DepthFormat     = TextureFormat.Depth32Float,
            DebugName       = "ViewportSky",
        });

        // Grid pipeline — depth test + alpha blend for fadeout
        _gridPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
        {
            VertexShader   = MakeShader(ShaderStage.Vertex, "vs_grid"),
            FragmentShader = MakeShader(ShaderStage.Fragment, "fs_grid"),
            VertexLayout   = new VertexLayoutDesc
            {
                Attributes = Array.Empty<VertexAttribute>(),
                Bindings   = Array.Empty<VertexBinding>(),
            },
            Topology          = PrimitiveTopology.TriangleList,
            BlendState        = BlendState.AlphaBlend,
            DepthStencilState = new DepthStencilState
            {
                DepthTestEnabled  = true,
                DepthWriteEnabled = true,
                DepthCompareOp    = CompareOp.Less,
            },
            RasterizerState = new RasterizerState { CullMode = CullMode.None },
            ColorFormats    = new[] { TextureFormat.BGRA8Unorm },
            DepthFormat     = TextureFormat.Depth32Float,
            DebugName       = "ViewportGrid",
        });

        // Mesh pipeline — for rendering entities
        _meshPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
        {
            VertexShader   = MakeShader(ShaderStage.Vertex, "vs_mesh"),
            FragmentShader = MakeShader(ShaderStage.Fragment, "fs_mesh"),
            VertexLayout   = new VertexLayoutDesc
            {
                Attributes = new[]
                {
                    new VertexAttribute { Location = 0, Binding = 0, Format = TextureFormat.RGB32Float, Offset = 0 },  // Position
                    new VertexAttribute { Location = 1, Binding = 0, Format = TextureFormat.RGB32Float, Offset = 12 }, // Normal
                    new VertexAttribute { Location = 2, Binding = 0, Format = TextureFormat.RG32Float, Offset = 24 },  // UV
                },
                Bindings = new[]
                {
                    new VertexBinding { Binding = 0, Stride = 32, PerInstance = false },
                },
            },
            Topology          = PrimitiveTopology.TriangleList,
            BlendState        = BlendState.Opaque,
            DepthStencilState = new DepthStencilState
            {
                DepthTestEnabled  = true,
                DepthWriteEnabled = true,
                DepthCompareOp    = CompareOp.Less,
            },
            RasterizerState = new RasterizerState { CullMode = CullMode.Back },
            ColorFormats    = new[] { TextureFormat.BGRA8Unorm },
            DepthFormat     = TextureFormat.Depth32Float,
            DebugName       = "ViewportMesh",
        });

        // Shadow pipeline — writes only depth from light's perspective
        _shadowPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
        {
            VertexShader   = MakeShader(ShaderStage.Vertex, "vs_shadow"),
            FragmentShader = MakeShader(ShaderStage.Fragment, "fs_shadow"),
            VertexLayout   = new VertexLayoutDesc
            {
                Attributes = new[]
                {
                    new VertexAttribute { Location = 0, Binding = 0, Format = TextureFormat.RGB32Float, Offset = 0 },  // Position
                    // Normals and UVs are strictly ignored in shadow pass
                },
                Bindings = new[]
                {
                    new VertexBinding { Binding = 0, Stride = 32, PerInstance = false },
                },
            },
            Topology          = PrimitiveTopology.TriangleList,
            BlendState        = BlendState.Opaque,
            DepthStencilState = new DepthStencilState
            {
                DepthTestEnabled  = true,
                DepthWriteEnabled = true,
                DepthCompareOp    = CompareOp.LessOrEqual
            },
            RasterizerState = new RasterizerState { CullMode = CullMode.None }, // No culling to ensure shadows cast indiscriminately of winding order -> prevents teapot disappearing from shadow map
            ColorFormats    = Array.Empty<TextureFormat>(),
            DepthFormat     = TextureFormat.Depth32Float,
            DebugName       = "ViewportShadow",
        });
    }

    private ShaderDesc MakeShader(ShaderStage stage, string entryPoint)
    {
        byte[] bytecode = Array.Empty<byte>();

        if (_device.Backend == RHIBackend.DirectX9)
        {
            string ext = stage == ShaderStage.Vertex ? ".vs.cso" : ".ps.cso";
            
            // Map entryPoint back to the base name since DX9 compiles entry points specifically.
            // e.g. vs_sky -> viewport_sky
            string baseName = "viewport";
            if (entryPoint.Contains("sky")) baseName += "_sky";
            else if (entryPoint.Contains("grid")) baseName += "_grid";
            else if (entryPoint.Contains("mesh")) baseName += "_mesh";

            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", baseName + ext);
            
            if (!System.IO.File.Exists(path))
                path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Editor", "Shaders", baseName + ext);

            if (System.IO.File.Exists(path))
            {
                bytecode = System.IO.File.ReadAllBytes(path);
            }
            else
            {
                Console.WriteLine($"[ViewportRenderer] WARNING: DX9 bytecode not found at {path}. Viewport may not render on DX9.");
            }
        }

        return new()
        {
            Stage      = stage,
            EntryPoint = entryPoint,
            Bytecode   = bytecode,
        };
    }

    private void CreateBuffers()
    {
        _shadowMap = _device.CreateTexture(new TextureDesc
        {
            Width = 2048, Height = 2048, Depth = 1,
            MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.Depth32Float,
            Usage = TextureUsage.DepthStencil | TextureUsage.Sampled,
            DebugName = "Viewport.ShadowMap"
        });

        _uniformBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = (ulong)Marshal.SizeOf<ViewUniforms>(),
            Usage      = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "Viewport.UB",
        });

        _entityUniformBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = (ulong)Marshal.SizeOf<EntityUniforms>(),
            Usage      = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "Viewport.EntityUB",
        });
    }


    private void RenderEntities(IRHICommandBuffer cmd, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 proj)
    {
        cmd.SetPipeline(_meshPipeline!);

        // Query for entities with TransformComponent AND StaticMeshComponent
        var query = _world.CreateQuery().All<TransformComponent>().All<BlueSky.Core.ECS.Builtin.StaticMeshComponent>().Build();
        var chunks = _world.GetQueryChunks(query);

        foreach (var chunk in chunks)
        {
            int transformIndex = chunk.GetComponentIndex(typeof(TransformComponent));
            int meshIndex = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.StaticMeshComponent));

            for (int i = 0; i < chunk.Count; i++)
            {
                var transform = chunk.GetComponent<TransformComponent>(i, transformIndex);
                var staticMesh = chunk.GetComponent<BlueSky.Core.ECS.Builtin.StaticMeshComponent>(i, meshIndex);

                if (string.IsNullOrEmpty(staticMesh.MeshAssetId)) continue;
                
                string assetId = staticMesh.MeshAssetId;

                // Demand-load the mesh if it isn't in cache
                if (!_meshCache.TryGetValue(assetId, out var gpuData))
                {
                    try
                    {
                        var asset = BlueSky.Core.Assets.BlueAsset.Load(assetId);
                        if (asset != null && asset.PayloadData != null)
                        {
                            using var ms = new System.IO.MemoryStream(asset.PayloadData);
                            using var reader = new System.IO.BinaryReader(ms);
                            
                            int vLen = reader.ReadInt32();
                            byte[] vData = reader.ReadBytes(vLen);
                            int iLen = reader.ReadInt32();
                            byte[] iData = reader.ReadBytes(iLen);

                            var vb = _device.CreateBuffer(new BufferDesc
                            {
                                Size = (ulong)vLen, Usage = BufferUsage.Vertex,
                                MemoryType = MemoryType.CpuToGpu, DebugName = $"{asset.AssetName}.VB"
                            });
                            _device.UpdateBuffer(vb, vData);

                            var ib = _device.CreateBuffer(new BufferDesc
                            {
                                Size = (ulong)iLen, Usage = BufferUsage.Index,
                                MemoryType = MemoryType.CpuToGpu, DebugName = $"{asset.AssetName}.IB"
                            });
                            _device.UpdateBuffer(ib, iData);

                            gpuData = new MeshGPUData { VertexBuffer = vb, IndexBuffer = ib, IndexCount = iLen / 2 };
                            _meshCache[assetId] = gpuData;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Viewport] Failed to load GPU mesh: {ex.Message}");
                    }
                }

                if (gpuData != null)
                {
                    cmd.SetVertexBuffer(gpuData.VertexBuffer!, 0);
                    cmd.SetIndexBuffer(gpuData.IndexBuffer!, IndexType.UInt16);
                    
                    var model = transform.WorldMatrix;

                    var entityUniforms = new EntityUniforms
                    {
                        Model = ToSystemMatrix4x4(model),
                        Color = new System.Numerics.Vector4(0.8f, 0.8f, 0.8f, 1.0f) // Whitish object
                    };

                    var uniformSpan = MemoryMarshal.CreateSpan(ref entityUniforms, 1);
                    _device.UpdateBuffer(_entityUniformBuffer!, MemoryMarshal.AsBytes(uniformSpan));

                    cmd.SetUniformBuffer(_entityUniformBuffer!, 30);
                    cmd.DrawIndexed((uint)gpuData.IndexCount);
                }
            }
        }
    }

    // ── IDisposable ─────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _skyPipeline?.Dispose();
        _gridPipeline?.Dispose();
        _meshPipeline?.Dispose();
        _shadowPipeline?.Dispose();
        _shadowMap?.Dispose();
        _uniformBuffer?.Dispose();
        _entityUniformBuffer?.Dispose();
        
        foreach (var mesh in _meshCache.Values)
        {
            mesh.Dispose();
        }
        _meshCache.Clear();

        _disposed = true;
    }
}
