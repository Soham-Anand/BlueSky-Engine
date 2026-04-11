using System;
using System.Numerics;
using BlueSky.Platform;
using NotBSRenderer;
using NotBSRenderer.DirectX9;
using NotBSUI.Rendering;
using System.Runtime.InteropServices;
using static NotBSRenderer.DirectX9.D3D9TransformState;

namespace BlueSky.RHI.Test;

class DX9Test
{
    public static void Run()
    {
        try
        {
            Console.WriteLine("=== DirectX 9 Test ===\n");
            
            var options = WindowOptions.Default;
            options.Title = "NotBSRenderer - DirectX 9";
            options.Width = 800;
            options.Height = 600;
            
            Console.WriteLine("[DX9 Test] Creating window...");
            var window = WindowFactory.Create(options);
            Console.WriteLine("[DX9 Test] Window created");
            
            Console.WriteLine("[DX9 Test] Creating device...");
            var device = RHIDevice.Create(RHIBackend.DirectX9, window);
            Console.WriteLine("[DX9 Test] Device created");
            
            Console.WriteLine("[DX9 Test] Creating swapchain...");
            var swapchain = device.CreateSwapchain(window);
            Console.WriteLine("[DX9 Test] Swapchain created");
            
            Console.WriteLine("[DX9 Test] Loading teapot model...");
            var (vertices, indices) = ObjParser.Parse("teapot.obj", 0xFFFF0000); // Red teapot
            
            var vertexBuffer = device.CreateBuffer(new BufferDesc
            {
                Size = (ulong)(vertices.Length * 16),
                Usage = BufferUsage.Vertex,
                MemoryType = MemoryType.GpuOnly
            });
            
            device.UploadBuffer(vertexBuffer, System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan()));
            
            var indexBuffer = device.CreateBuffer(new BufferDesc
            {
                Size = (ulong)(indices.Length * 4),
                Usage = BufferUsage.Index,
                MemoryType = MemoryType.GpuOnly
            });
            
            device.UploadBuffer(indexBuffer, System.Runtime.InteropServices.MemoryMarshal.AsBytes(indices.AsSpan()));
            
            Console.WriteLine($"[DX9 Test] Loaded {vertices.Length} vertices, {indices.Length} indices");
            
            // Create depth texture
            var depthTexture = device.CreateTexture(new TextureDesc
            {
                Width = (uint)window.Size.X,
                Height = (uint)window.Size.Y,
                Depth = 1,
                MipLevels = 1,
                ArrayLayers = 1,
                Format = TextureFormat.Depth24Stencil8,
                Usage = TextureUsage.DepthStencil
            });
            
            byte[]? vsBytecode = null;
            byte[]? psBytecode = null;
            
            var vsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", "triangle.vs.cso");
            var psPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", "triangle.ps.cso");
            
            if (System.IO.File.Exists(vsPath) && System.IO.File.Exists(psPath))
            {
                Console.WriteLine("[DX9 Test] Found compiled HLSL shaders, loading programmable pipeline...");
                vsBytecode = System.IO.File.ReadAllBytes(vsPath);
                psBytecode = System.IO.File.ReadAllBytes(psPath);
            }
            else
            {
                Console.WriteLine("[DX9 Test] No compiled shaders found, using Fixed-Function pipeline...");
            }
            
            // Create pipeline
            Console.WriteLine("[DX9 Test] Creating pipeline...");
            var pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDesc
            {
                VertexShader = new ShaderDesc { Stage = ShaderStage.Vertex, Bytecode = vsBytecode ?? Array.Empty<byte>() },
                FragmentShader = new ShaderDesc { Stage = ShaderStage.Fragment, Bytecode = psBytecode ?? Array.Empty<byte>() },
                VertexLayout = new VertexLayoutDesc
                {
                    Attributes = new[]
                    {
                        new VertexAttribute { Location = 0, Binding = 0, Format = TextureFormat.RGB32Float, Offset = 0 },
                        new VertexAttribute { Location = 1, Binding = 0, Format = TextureFormat.RGBA8Unorm, Offset = 12 }
                    },
                    Bindings = new[]
                    {
                        new VertexBinding { Binding = 0, Stride = 16, PerInstance = false }
                    }
                },
                Topology = PrimitiveTopology.TriangleList,
                BlendState = BlendState.Opaque,
                DepthStencilState = new DepthStencilState
                {
                    DepthTestEnabled = true,
                    DepthWriteEnabled = true,
                    DepthCompareOp = CompareOp.Less
                },
                RasterizerState = new RasterizerState { CullMode = CullMode.Back },
                ColorFormats = new[] { TextureFormat.BGRA8Unorm },
                DepthFormat = TextureFormat.Depth24Stencil8
            });
            Console.WriteLine("[DX9 Test] Pipeline created");

            // Setup UI
            var uiPipelineDesc = new GraphicsPipelineDesc();
            uiPipelineDesc.VertexShader = new ShaderDesc { EntryPoint = "vs_ui", Bytecode = Array.Empty<byte>() };
            uiPipelineDesc.FragmentShader = new ShaderDesc { EntryPoint = "fs_ui", Bytecode = Array.Empty<byte>() };
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
            
            Console.WriteLine("[DX9 Test] UI Renderer initialized");
            
            Console.WriteLine("[DX9 Test] Starting render loop...\n");
            
            int frameCount = 0;
            int fpsFrames = 0;
            double fpsTimer = 0;
            double totalTime = 0;
            
            // Setup Camera View/Proj
            var view = Matrix4x4.CreateLookAt(new Vector3(0, 1.5f, 5.0f), new Vector3(0, 0, 0), Vector3.UnitY);
            
            window.Render += (dt) =>
            {
                try
                {
                    totalTime += dt;
                    fpsTimer += dt;
                    fpsFrames++;
                    
                    if (fpsTimer >= 1.0)
                    {
                        window.Title = $"NotBSRenderer - DirectX 9 | FPS: {fpsFrames} | FT: {(fpsTimer / fpsFrames * 1000):F2}ms";
                        fpsFrames = 0;
                        fpsTimer = 0;
                    }
                    
                    if (frameCount == 0)
                        Console.WriteLine("[DX9 Test] First frame rendering...");
                    
                    swapchain.AcquireNextImage();
                    var cmd = device.CreateCommandBuffer();
                    
                    cmd.BeginRenderPass(new[] { swapchain.CurrentRenderTarget }, depthTexture, ClearValue.FromColor(0.1f, 0.1f, 0.2f, 1f));
                    cmd.SetPipeline(pipeline);
                    cmd.SetViewport(new Viewport
                    {
                        X = 0, Y = 0,
                        Width = swapchain.Width,
                        Height = swapchain.Height,
                        MinDepth = 0f, MaxDepth = 1f
                    });
                    
                    var rawCmd = cmd is IRHIWrapped<IRHICommandBuffer> wrapped ? wrapped.Inner : cmd;
                    if (rawCmd is D3D9CommandBuffer dx9cmd)
                    {
                        // Set matrices: D3DTS_WORLD=256, D3DTS_VIEW=2, D3DTS_PROJECTION=3
                        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, (float)swapchain.Width / swapchain.Height, 0.1f, 1000f);
                        var rotWorld = Matrix4x4.CreateRotationY((float)totalTime);
                        
                        if (vsBytecode != null)
                        {
                            // Programmable Pipeline: Upload MVP matrix directly to Constant Register 0
                            // In HLSL context matrix is column-major by default unless transposed, but System.Numerics is row-major. 
                            // Matrix4x4.Transpose is usually required for HLSL `mul(pos, mvp)`.
                            var mvp = Matrix4x4.Transpose(rotWorld * view * proj);
                            dx9cmd.SetVertexUniforms(0, ref mvp);
                        }
                        else
                        {
                            // Fixed-function Pipeline: Apply separate matrices
                            dx9cmd.SetTransform(D3D9TransformState.D3DTS_PROJECTION, ref proj);
                            dx9cmd.SetTransform(D3D9TransformState.D3DTS_VIEW, ref view);
                            dx9cmd.SetTransform(D3D9TransformState.D3DTS_WORLD, ref rotWorld);
                        }
                    }
                    
                    cmd.SetVertexBuffer(vertexBuffer);
                    cmd.SetIndexBuffer(indexBuffer, IndexType.UInt32);
                    cmd.DrawIndexed((uint)indices.Length);

                    // UI Pass
                    uiRenderer.Begin(cmd);
                    uiRenderer.DrawRect(new Vector2(10, 10), new Vector2(300, 120), new Vector4(0, 0, 0, 0.6f));
                    uiRenderer.DrawRect(new Vector2(10, 10), new Vector2(300, 30), new Vector4(0.2f, 0.4f, 0.8f, 0.9f));
                    uiRenderer.DrawString(new Vector2(20, 15), "BlueSky Engine - DX9 RHI Test", Vector4.One);
                    uiRenderer.DrawString(new Vector2(20, 50), $"FPS: {fpsFrames / Math.Max(0.01, fpsTimer):F2}", Vector4.One);
                    uiRenderer.DrawString(new Vector2(20, 80), "Backend: DirectX 9", new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
                    uiRenderer.End();

                    cmd.EndRenderPass();
                    
                    device.Submit(cmd, swapchain);
                    swapchain.Present();
                    cmd.Dispose();
                    
                    frameCount++;
                    if (frameCount == 1)
                        Console.WriteLine("[DX9 Test] First frame complete!");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DX9 Test] Render error: {ex.Message}");
                    Console.WriteLine($"[DX9 Test] Stack: {ex.StackTrace}");
                }
            };
            
            window.Closing += () =>
            {
                Console.WriteLine("[DX9 Test] Cleaning up...");
                uiRenderer.Dispose();
                fontAtlas.Dispose();
                pipeline.Dispose();
                vertexBuffer.Dispose();
                indexBuffer.Dispose();
                depthTexture.Dispose();
                swapchain.Dispose();
                device.Dispose();
            };
            
            window.Show();
            
            var frameTime = System.Diagnostics.Stopwatch.StartNew();
            const double targetFrameTime = 1.0 / 60.0;
            
            while (!window.IsClosing)
            {
                window.ProcessEvents();
                
                var elapsed = frameTime.Elapsed.TotalSeconds;
                if (elapsed < targetFrameTime)
                {
                    System.Threading.Thread.Sleep((int)((targetFrameTime - elapsed) * 1000));
                }
                frameTime.Restart();
            }
            
            Console.WriteLine("[DX9 Test] Complete!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[DX9 Test] FATAL ERROR: {ex.Message}");
            Console.WriteLine($"[DX9 Test] Type: {ex.GetType().Name}");
            Console.WriteLine($"[DX9 Test] Stack trace:\n{ex.StackTrace}");
            
            if (ex.InnerException != null)
            {
                Console.WriteLine($"\n[DX9 Test] Inner exception: {ex.InnerException.Message}");
                Console.WriteLine($"[DX9 Test] Inner stack:\n{ex.InnerException.StackTrace}");
            }
            
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
