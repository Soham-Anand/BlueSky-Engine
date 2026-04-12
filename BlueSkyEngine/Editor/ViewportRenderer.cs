using System;
using System.Linq;
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
    // ── Uniform structure (must match Metal ViewUniforms exactly for sky/grid) ────────
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
    
    // ── Horizon shader ViewUniforms (different structure for horizon_lighting.metal) ────────
    [StructLayout(LayoutKind.Sequential)]
    private struct HorizonViewUniforms
    {
        public System.Numerics.Matrix4x4 ViewProj;
        public System.Numerics.Matrix4x4 View;
        public System.Numerics.Matrix4x4 InvView;
        public System.Numerics.Vector3   CameraPos;
        public float     Time;
        public System.Numerics.Vector2   ScreenSize;
        public float     NearPlane;
        public float     FarPlane;
    }

    // ── Entity uniform structure (model matrix + material) ─────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct EntityUniforms
    {
        public System.Numerics.Matrix4x4 Model;
        public System.Numerics.Vector4   Color;
    }
    
    // ── Shadow pass uniform structure ────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct ShadowUniforms
    {
        public System.Numerics.Matrix4x4 LightSpaceMatrix;
    }
    
    // ── Material data for Horizon shader (must match Metal MaterialData) ───────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct MaterialData
    {
        public System.Numerics.Vector3 Albedo;
        public float Metallic;
        public float Roughness;
        public float Ao;
        public float Emission;
        public int UseAlbedoTex;
        public int UseNormalTex;
        public int UseRMATex;
        private float Pad1;
        private float Pad2;
    }
    
    // ── Light data for Horizon shader (must match Metal LightData) ────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct LightData
    {
        public System.Numerics.Vector3 Position;
        public float Range;
        public System.Numerics.Vector3 Direction;
        public float Intensity;
        public System.Numerics.Vector3 Color;
        public int Type;
        public float InnerAngle;
        public float OuterAngle;
        public float Attenuation;
        public int CastShadows;
        public int Volumetric;
        private float Pad1;
        private float Pad2;
    }
    
    // ── Lighting settings for Horizon shader ────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct LightingSettings
    {
        public int Quality;
        public int MaxLights;
        public int EnableIBL;
        public int EnableVolumetrics;
        public int EnableContactShadows;
        public float Exposure;
        public System.Numerics.Vector3 AmbientColor;
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

    // ── Submesh info with material slot index ─────────────────────────────
    public struct SubmeshInfo
    {
        public int IndexOffset;     // Starting index in the index buffer
        public int IndexCount;      // Number of indices for this submesh
        public int MaterialSlot;    // Material slot index (0-7)
    }

    // ── Mesh GPU cache struct ─────────────────────────────────────────────
    public class MeshGPUData : IDisposable
    {
        public IRHIBuffer? VertexBuffer;
        public IRHIBuffer? IndexBuffer;
        public int IndexCount;
        public List<SubmeshInfo> Submeshes = new(); // One per material slot
        
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
    private          IRHIPipeline? _wireframePipeline;
    private          IRHIPipeline? _shadowPipeline;
    private          IRHITexture?  _shadowMap;
    private          IRHIBuffer?   _uniformBuffer;
    private          IRHIBuffer?   _entityUniformBuffer;
    
    // Horizon Lighting buffers
    private IRHIBuffer? _horizonViewUniformBuffer; // Separate buffer for Horizon shader
    private IRHIBuffer? _lightBuffer;
    private IRHIBuffer? _lightCountBuffer;
    private IRHIBuffer? _lightSettingsBuffer;
    private IRHIBuffer? _materialBuffer;
    
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
        
        var shadowUniforms = new ShadowUniforms
        {
            LightSpaceMatrix = lightViewProj
        };
        var shadowUniformSpan = MemoryMarshal.CreateSpan(ref shadowUniforms, 1);
        _device.UpdateBuffer(_uniformBuffer!, MemoryMarshal.AsBytes(shadowUniformSpan));
        cmd.SetUniformBuffer(_uniformBuffer!, 10); // LightSpaceMatrix at slot 10

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
                
                // Ensure submeshes list is initialized
                if (gpuData.Submeshes == null || gpuData.Submeshes.Count == 0)
                {
                    gpuData.Submeshes = new List<SubmeshInfo>
                    {
                        new SubmeshInfo { IndexOffset = 0, IndexCount = gpuData.IndexCount, MaterialSlot = 0 }
                    };
                }

                cmd.SetVertexBuffer(gpuData.VertexBuffer!, 0);
                cmd.SetIndexBuffer(gpuData.IndexBuffer!, IndexType.UInt16);
                
                var entityUniforms = new EntityUniforms
                {
                    Model = ToSystemMatrix4x4(transform.WorldMatrix),
                    Color = System.Numerics.Vector4.One
                };
                
                var uniformSpan = MemoryMarshal.CreateSpan(ref entityUniforms, 1);
                _device.UpdateBuffer(_entityUniformBuffer!, MemoryMarshal.AsBytes(uniformSpan));
                
                cmd.SetUniformBuffer(_entityUniformBuffer!, 30); // Model matrix at slot 30
                
                // Draw each submesh for shadow
                foreach (var submesh in gpuData.Submeshes)
                {
                    if (submesh.IndexCount == 0) continue; // Skip empty submeshes
                    cmd.DrawIndexed((uint)submesh.IndexCount, 1, (uint)submesh.IndexOffset, 0, 0);
                }
            }
        }
        cmd.EndRenderPass();
    }

    private static readonly System.Numerics.Vector3 DefaultAlbedo = new(0.8f, 0.3f, 0.2f); // Rust/orange color for visibility
    
    public void Render(IRHICommandBuffer cmd, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 proj,
        System.Numerics.Vector3 cameraPos, int viewportX, int viewportY, int viewportW, int viewportH, float deltaTime)
    {
        _elapsedTime += deltaTime;

        // ── build uniforms for sky/grid (old ViewUniforms) ────────────────────────────────
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

        // ── build uniforms for Horizon Lighting (new HorizonViewUniforms) ─────────────────
        System.Numerics.Matrix4x4.Invert(view, out var invView);
        
        var horizonUniforms = new HorizonViewUniforms
        {
            ViewProj = viewProj,
            View = view,
            InvView = invView,
            CameraPos = cameraPos,
            Time = _elapsedTime,
            ScreenSize = new System.Numerics.Vector2(viewportW, viewportH),
            NearPlane = 0.1f,
            FarPlane = 1000f,
        };

        var horizonUniformSpan = MemoryMarshal.CreateSpan(ref horizonUniforms, 1);
        _device.UpdateBuffer(_horizonViewUniformBuffer!, MemoryMarshal.AsBytes(horizonUniformSpan));

        // ── Prepare Horizon Lighting buffers ─────────────────────────────
        // Manually create a directional light (sun)
        var lightDataArray = new LightData[64];
        lightDataArray[0] = new LightData
        {
            Position = System.Numerics.Vector3.Zero,
            Range = 1000f,
            Direction = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.5f, 0.6f, 0.3f)),
            Intensity = 3.0f,
            Color = new System.Numerics.Vector3(1.0f, 0.95f, 0.8f),
            Type = 0, // Directional
            InnerAngle = 0f,
            OuterAngle = 0f,
            Attenuation = 1f,
            CastShadows = 1,
            Volumetric = 0,
        };
        
        // Convert to byte array manually
        int lightDataSize = Marshal.SizeOf<LightData>();
        byte[] lightBytes = new byte[lightDataSize * 64];
        for (int i = 0; i < 64; i++)
        {
            IntPtr ptr = Marshal.AllocHGlobal(lightDataSize);
            try
            {
                Marshal.StructureToPtr(lightDataArray[i], ptr, false);
                Marshal.Copy(ptr, lightBytes, i * lightDataSize, lightDataSize);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        _device.UpdateBuffer(_lightBuffer!, lightBytes);
        
        // Update light count
        int lightCount = 1;
        var lightCountSpan = MemoryMarshal.CreateSpan(ref lightCount, 1);
        _device.UpdateBuffer(_lightCountBuffer!, MemoryMarshal.AsBytes(lightCountSpan));
        
        // Update lighting settings
        var lightSettings = new LightingSettings
        {
            Quality = 2, // High
            MaxLights = 64,
            EnableIBL = 0, // Disabled until IBL textures are provided
            EnableVolumetrics = 0,
            EnableContactShadows = 1,
            Exposure = 1.0f,
            AmbientColor = new System.Numerics.Vector3(0.1f, 0.1f, 0.15f),
        };
        var lightSettingsSpan = MemoryMarshal.CreateSpan(ref lightSettings, 1);
        _device.UpdateBuffer(_lightSettingsBuffer!, MemoryMarshal.AsBytes(lightSettingsSpan));
        
        // Update material data
        var material = new MaterialData
        {
            Albedo = DefaultAlbedo,
            Metallic = 0.1f,
            Roughness = 0.7f,
            Ao = 1.0f,
            Emission = 0.0f,
            UseAlbedoTex = 0,
            UseNormalTex = 0,
            UseRMATex = 0,
        };
        var materialSpan = MemoryMarshal.CreateSpan(ref material, 1);
        _device.UpdateBuffer(_materialBuffer!, MemoryMarshal.AsBytes(materialSpan));

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
        RenderEntities(cmd, view, proj, cameraPos);
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

        // Mesh pipeline — Simple lighting (compatible with existing uniforms)
        _meshPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
        {
            VertexShader   = MakeShader(ShaderStage.Vertex, "vs_mesh"),
            FragmentShader = MakeShader(ShaderStage.Fragment, "fs_mesh"),
            VertexLayout   = new VertexLayoutDesc
            {
                Attributes = new[]
                {
                    new VertexAttribute { Location = 0, Binding = 0, Format = TextureFormat.RGB32Float, Offset = 0 },   // Position
                    new VertexAttribute { Location = 1, Binding = 0, Format = TextureFormat.RGB32Float, Offset = 12 },  // Normal
                    new VertexAttribute { Location = 2, Binding = 0, Format = TextureFormat.RG32Float, Offset = 24 }, // UV
                },
                Bindings = new[]
                {
                    new VertexBinding { Binding = 0, Stride = 32, PerInstance = false }, // 32 bytes: pos+normal+uv
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
            RasterizerState = new RasterizerState { CullMode = CullMode.None },
            ColorFormats    = new[] { TextureFormat.BGRA8Unorm },
            DepthFormat     = TextureFormat.Depth32Float,
            DebugName       = "ViewportMesh_HorizonLighting",
        });

        // Wireframe pipeline — super thin outline for 3D depth perception
        _wireframePipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
        {
            VertexShader   = MakeShader(ShaderStage.Vertex, "vs_mesh"),
            FragmentShader = MakeShader(ShaderStage.Fragment, "fs_wireframe"),
            VertexLayout   = new VertexLayoutDesc
            {
                Attributes = new[]
                {
                    new VertexAttribute { Location = 0, Binding = 0, Format = TextureFormat.RGB32Float, Offset = 0 },
                    new VertexAttribute { Location = 1, Binding = 0, Format = TextureFormat.RGB32Float, Offset = 12 },
                    new VertexAttribute { Location = 2, Binding = 0, Format = TextureFormat.RG32Float, Offset = 24 },
                },
                Bindings = new[]
                {
                    new VertexBinding { Binding = 0, Stride = 32, PerInstance = false },
                },
            },
            Topology          = PrimitiveTopology.TriangleList,
            BlendState        = BlendState.AlphaBlend,
            DepthStencilState = new DepthStencilState
            {
                DepthTestEnabled  = true,   // Test against depth
                DepthWriteEnabled = false,  // Don't write to depth (draw on top)
                DepthCompareOp    = CompareOp.LessOrEqual,
            },
            RasterizerState = new RasterizerState 
            { 
                CullMode = CullMode.None,
                FillMode = FillMode.Wireframe, // Wireframe fill
                LineWidth = 1.0f, // Super thin lines
            },
            ColorFormats    = new[] { TextureFormat.BGRA8Unorm },
            DepthFormat     = TextureFormat.Depth32Float,
            DebugName       = "ViewportWireframe",
        });

        // Shadow pipeline — writes only depth from light's perspective
        _shadowPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
        {
            VertexShader   = MakeShader(ShaderStage.Vertex, "horizon_shadow_vertex"),
            FragmentShader = MakeShader(ShaderStage.Fragment, "horizon_shadow_fragment"),
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
            else if (entryPoint.Contains("horizon")) baseName = "horizon_lighting";

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
        else if (_device.Backend == RHIBackend.Metal)
        {
            // For Metal, load the compiled .metallib
            string baseName = "viewport_3d";
            if (entryPoint.Contains("sky")) baseName = "viewport_3d";
            else if (entryPoint.Contains("grid")) baseName = "viewport_3d";
            else if (entryPoint.Contains("horizon")) baseName = "horizon_lighting";
            
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", baseName + ".metallib");
            
            if (!System.IO.File.Exists(path))
                path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Editor", "Shaders", baseName + ".metallib");

            if (System.IO.File.Exists(path))
            {
                bytecode = System.IO.File.ReadAllBytes(path);
            }
            else
            {
                Console.WriteLine($"[ViewportRenderer] WARNING: Metal library not found at {path}. Viewport may not render on Metal.");
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
        
        // Horizon Lighting buffers
        _horizonViewUniformBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = (ulong)Marshal.SizeOf<HorizonViewUniforms>(),
            Usage      = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "Viewport.HorizonViewUB",
        });
        
        _lightBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = 5120, // Space for up to 64 lights (72 bytes each = 4608, rounded to 5120)
            Usage      = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "Viewport.LightBuffer",
        });
        
        _lightSettingsBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = 64, // Lighting settings
            Usage      = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "Viewport.LightSettings",
        });
        
        _lightCountBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = 16, // int with padding
            Usage      = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "Viewport.LightCount",
        });
        
        _materialBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = (ulong)Marshal.SizeOf<MaterialData>(),
            Usage      = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "Viewport.MaterialUB",
        });
    }


    private void RenderEntities(IRHICommandBuffer cmd, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 proj, System.Numerics.Vector3 cameraPos)
    {
        cmd.SetPipeline(_meshPipeline!);
        
        // Use simple uniform buffer
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

                            // Read submesh data (material slots)
                            var submeshes = new List<SubmeshInfo>();
                            try
                            {
                                int submeshCount = reader.ReadInt32();
                                for (int s = 0; s < submeshCount; s++)
                                {
                                    int indexOffset = reader.ReadInt32();
                                    int indexCount = reader.ReadInt32();
                                    int materialSlot = reader.ReadInt32();
                                    submeshes.Add(new SubmeshInfo
                                    {
                                        IndexOffset = indexOffset,
                                        IndexCount = indexCount,
                                        MaterialSlot = materialSlot
                                    });
                                }
                            }
                            catch
                            {
                                // No submesh data - create single submesh with all indices
                                submeshes.Add(new SubmeshInfo
                                {
                                    IndexOffset = 0,
                                    IndexCount = iLen / 2,
                                    MaterialSlot = 0
                                });
                            }

                            gpuData = new MeshGPUData 
                            { 
                                VertexBuffer = vb, 
                                IndexBuffer = ib, 
                                IndexCount = iLen / 2,
                                Submeshes = submeshes
                            };
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
                    // Ensure submeshes list is initialized for cached meshes
                    if (gpuData.Submeshes == null || gpuData.Submeshes.Count == 0)
                    {
                        gpuData.Submeshes = new List<SubmeshInfo>
                        {
                            new SubmeshInfo { IndexOffset = 0, IndexCount = gpuData.IndexCount, MaterialSlot = 0 }
                        };
                    }

                    // Filter out empty submeshes
                    var validSubmeshes = gpuData.Submeshes.Where(s => s.IndexCount > 0).ToList();
                    if (validSubmeshes.Count == 0)
                    {
                        Console.WriteLine($"[Viewport] Warning: Mesh {assetId} has no valid submeshes with indices");
                        continue;
                    }

                    var model = transform.WorldMatrix;
                    cmd.SetVertexBuffer(gpuData.VertexBuffer!, 0);
                    cmd.SetIndexBuffer(gpuData.IndexBuffer!, IndexType.UInt16);

                    // Draw each submesh with its material slot
                    foreach (var submesh in validSubmeshes)
                    {
                        // Bright clay/ceramic color for visibility
                        var color = new System.Numerics.Vector4(0.95f, 0.6f, 0.4f, 1.0f); // Light orange/peach

                        var entityUniforms = new EntityUniforms
                        {
                            Model = ToSystemMatrix4x4(model),
                            Color = color
                        };

                        var uniformSpan = MemoryMarshal.CreateSpan(ref entityUniforms, 1);
                        _device.UpdateBuffer(_entityUniformBuffer!, MemoryMarshal.AsBytes(uniformSpan));

                        cmd.SetUniformBuffer(_entityUniformBuffer!, 30); // Entity at slot 30
                        cmd.DrawIndexed((uint)submesh.IndexCount, 1, (uint)submesh.IndexOffset, 0, 0);
                    }
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
        _wireframePipeline?.Dispose();
        _shadowPipeline?.Dispose();
        _shadowMap?.Dispose();
        _uniformBuffer?.Dispose();
        _entityUniformBuffer?.Dispose();
        _horizonViewUniformBuffer?.Dispose();
        _lightBuffer?.Dispose();
        _lightCountBuffer?.Dispose();
        _lightSettingsBuffer?.Dispose();
        _materialBuffer?.Dispose();
        
        foreach (var mesh in _meshCache.Values)
        {
            mesh.Dispose();
        }
        _meshCache.Clear();

        _disposed = true;
    }
}
