using System;
using System.Numerics;
using System.Runtime.InteropServices;
using BlueSky.Platform;
using NotBSRenderer;
using NotBSUI.Rendering;

namespace BlueSky.RHI.Test;

class Program
{
    struct Uniforms
    {
        public Matrix4x4 Mvp;
    }
    
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "texture-lock-bug")
        {
            TextureLockBugExplorationTest.Run();
            return;
        }
        
        if (args.Length > 0 && args[0] == "texture-pool-preservation")
        {
            TexturePoolPreservationTest.Run();
            return;
        }
        
        if (args.Length > 0 && args[0] == "dx9")
        {
            DX9Test.Run();
            return;
        }
        
        if (OperatingSystem.IsWindows())
        {
            DX9Test.Run();
            return;
        }
        
        Console.WriteLine("=== BlueSky RHI Test - Metal Backend ===\n");
        
        var options = WindowOptions.Default;
        options.Title = "BlueSky RHI Test - Metal";
        options.Width = 1280;
        options.Height = 720;
        options.VSync = true;
        
        var window = WindowFactory.Create(options);
        var device = RHIDevice.Create(RHIBackend.Metal, window);
        var swapchain = device.CreateSwapchain(window, PresentMode.Vsync);
        
        var (vertices, indices) = ObjParser.Parse("teapot.obj", 0xFFFF0000);
        
        var depthTexture = device.CreateTexture(new TextureDesc
        {
            Width = (uint)window.Size.X,
            Height = (uint)window.Size.Y,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.Depth32Float,
            Usage = TextureUsage.DepthStencil
        });
        
        var vbo = device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)(vertices.Length * 16),
            Usage = BufferUsage.Vertex,
            MemoryType = MemoryType.GpuOnly
        });
        device.UploadBuffer(vbo, MemoryMarshal.AsBytes(vertices.AsSpan()));
        
        var ibo = device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)(indices.Length * 4),
            Usage = BufferUsage.Index,
            MemoryType = MemoryType.GpuOnly
        });
        device.UploadBuffer(ibo, MemoryMarshal.AsBytes(indices.AsSpan()));
        
        var pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDesc
        {
            VertexShader = new ShaderDesc { EntryPoint = "vertex_main" },
            FragmentShader = new ShaderDesc { EntryPoint = "fragment_main" },
            VertexLayout = new VertexLayoutDesc
            {
                Attributes = new[]
                {
                    new VertexAttribute { Location = 0, Binding = 0, Format = TextureFormat.RGB32Float, Offset = 0 },
                    new VertexAttribute { Location = 1, Binding = 0, Format = TextureFormat.RGBA8Unorm, Offset = 12 }
                },
                Bindings = new[] { new VertexBinding { Binding = 0, Stride = 16 } }
            },
            Topology = PrimitiveTopology.TriangleList,
            DepthStencilState = DepthStencilState.Default,
            RasterizerState = RasterizerState.Default,
            ColorFormats = new[] { TextureFormat.BGRA8Unorm },
            DepthFormat = TextureFormat.Depth32Float
        });
        
        var uniformBuffer = device.CreateBuffer(new BufferDesc
        {
            Size = 64,
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu
        });

        // Setup UI
        var uiPipelineDesc = new GraphicsPipelineDesc();
        uiPipelineDesc.VertexShader = new ShaderDesc { EntryPoint = "vs_ui" };
        uiPipelineDesc.FragmentShader = new ShaderDesc { EntryPoint = "fs_ui" };
        uiPipelineDesc.VertexLayout = new VertexLayoutDesc
        {
            Attributes = new[]
            {
                new VertexAttribute { Location = 0, Binding = 0, Format = TextureFormat.RG32Float, Offset = 0 },
                new VertexAttribute { Location = 1, Binding = 0, Format = TextureFormat.RG32Float, Offset = 8 },
                new VertexAttribute { Location = 2, Binding = 0, Format = TextureFormat.RGBA32Float, Offset = 16 },
                new VertexAttribute { Location = 3, Binding = 0, Format = TextureFormat.R32Float, Offset = 32 },
            },
            Bindings = new[] { new VertexBinding { Binding = 0, Stride = (uint)Marshal.SizeOf<UIVertex>() } }
        };
        uiPipelineDesc.BlendState = BlendState.AlphaBlend;
        uiPipelineDesc.DepthStencilState = DepthStencilState.Disabled;
        uiPipelineDesc.RasterizerState = new RasterizerState { CullMode = CullMode.None };
        uiPipelineDesc.ColorFormats = new[] { TextureFormat.BGRA8Unorm };
        
        var uiRenderer = new UIRenderer(device, uiPipelineDesc);
        var fontAtlas = new FontAtlas(device, "roboto.ttf", 24);
        uiRenderer.GlobalFontAtlas = fontAtlas;
        uiRenderer.Resize(window.Size.X, window.Size.Y);

        double totalTime = 0;
        int frameCount = 0;
        double fpsTime = 0;
        float fps = 0;

        while (!window.IsClosing)
        {
            window.ProcessEvents();
            
            double dt = 1.0 / 60.0; // Assume 60 FPS for now or get from OS
            totalTime += dt;
            fpsTime += dt;
            frameCount++;
            
            if (fpsTime >= 1.0)
            {
                fps = (float)(frameCount / fpsTime);
                frameCount = 0;
                fpsTime = 0;
            }

            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, (float)window.Size.X / window.Size.Y, 0.1f, 1000f);
            var view = Matrix4x4.CreateLookAt(new Vector3(0, 1.5f, 5.0f), new Vector3(0, 0, 0), Vector3.UnitY);
            var model = Matrix4x4.CreateRotationY((float)totalTime);
            var mvp = model * view * proj;
            
            var uniforms = new Uniforms { Mvp = mvp };
            device.UploadBuffer(uniformBuffer, MemoryMarshal.AsBytes(new Span<Uniforms>(ref uniforms)));

            swapchain.AcquireNextImage();
            var cmd = device.CreateCommandBuffer();
            cmd.BeginRenderPass(new[] { swapchain.CurrentRenderTarget }, depthTexture, ClearValue.FromColor(0.1f, 0.1f, 0.2f, 1.0f));
            
            cmd.SetPipeline(pipeline);
            cmd.SetViewport(new Viewport { Width = window.Size.X, Height = window.Size.Y, MaxDepth = 1.0f });
            cmd.SetVertexBuffer(vbo);
            cmd.SetIndexBuffer(ibo, IndexType.UInt32);
            cmd.SetUniformBuffer(uniformBuffer, 1);
            cmd.DrawIndexed((uint)indices.Length);

            // UI Pass
            uiRenderer.Begin(cmd);
            uiRenderer.DrawRect(new Vector2(10, 10), new Vector2(300, 120), new Vector4(0, 0, 0, 0.6f));
            uiRenderer.DrawRect(new Vector2(10, 10), new Vector2(300, 30), new Vector4(0.2f, 0.4f, 0.8f, 0.9f));
            uiRenderer.DrawString(new Vector2(20, 15), "BlueSky Engine - RHI Test", Vector4.One);
            uiRenderer.DrawString(new Vector2(20, 50), $"FPS: {fps:F2}", Vector4.One);
            uiRenderer.DrawString(new Vector2(20, 80), $"Backend: {device.Backend}", new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
            uiRenderer.End();

            cmd.EndRenderPass();
            device.Submit(cmd, swapchain);
            swapchain.Present();
            
            cmd.Dispose();
        }

        pipeline.Dispose();
        vbo.Dispose();
        ibo.Dispose();
        uniformBuffer.Dispose();
        uiRenderer.Dispose();
        fontAtlas.Dispose();
        swapchain.Dispose();
        device.Dispose();
    }
}
