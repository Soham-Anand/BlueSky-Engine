using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using BlueSky.BSR.RHI;

namespace BlueSky.BSR.Vectra
{
    public enum VectraQualityProfile
    {
        Low,
        Balanced,
        High,
    }

    /// <summary>
    /// GPU side of Vectra: a four-corner quad plus one compact per-instance
    /// record. One indexed instanced draw renders the whole sorted batch; CPU
    /// upload is 44 bytes per puff rather than four repeated vertex records.
    /// </summary>
    public sealed class VectraRenderer : IDisposable
    {
        public const int VertsPerParticle = 4;
        public const int IndicesPerParticle = 6;
        public const int InstanceStride = 44;
        private const int CornerStride = 8;

        private readonly IRHIDevice _device;
        private IRHIPipeline? _smokePipeline;
        private IRHIPipeline? _smokeAdditivePipeline;
        private IRHIBuffer?[] _vertexBufferRing = new IRHIBuffer?[3]; // M4b: ringed
        private IRHIBuffer? _vertexBuffer => _vertexBufferRing[MetalFrame()];
        private IRHISampler? _linearSampler; // fs_smoke [[sampler(0)]]

        private int MetalFrame()
        {
            var inner = (_device as BlueSky.BSR.RHI.IRHIWrapped<BlueSky.BSR.RHI.IRHIDevice>)?.Inner;
            if (inner is BlueSky.BSR.RHI.Metal.MetalDevice metal)
                return metal.FrameIndex;
            return 0;
        }

        private void DrainMetalFlight()
        {
            var inner = (_device as BlueSky.BSR.RHI.IRHIWrapped<BlueSky.BSR.RHI.IRHIDevice>)?.Inner;
            (inner as BlueSky.BSR.RHI.Metal.MetalDevice)?.DrainPending();
        }
        private IRHIBuffer? _cornerBuffer;
        private IRHIBuffer? _indexBuffer;
        private IRHITexture? _sprite;
        private IRHITexture? _lightMapPositive;
        private IRHITexture? _lightMapNegative;
        private IRHITexture? _depthSnapshot;
        private uint _depthSnapshotWidth;
        private uint _depthSnapshotHeight;
        private readonly int _maxDrawParticles;
        private readonly int _lightMapMaxDimension;

        private readonly VectraVertex[] _staging = new VectraVertex[VectraPool.MaxParticles];
        private bool _disposed;

        [StructLayout(LayoutKind.Sequential)]
        private struct VectraVertex
        {
            public Vector3 Center;
            public Vector4 Color;
            public Vector4 Prm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VectraDepthUniforms
        {
            public Matrix4x4 InverseProjection;
            public Vector4 Parameters; // x=fade distance
        }

        private static readonly Vector2[] QuadCorners =
        {
            new(-1f, -1f), new(1f, -1f), new(-1f, 1f), new(1f, 1f),
        };
        // QuadCorners is in Z-order: 0=(-1,-1), 1=(1,-1), 2=(-1,1), 3=(1,1).
        // Shared diagonal is (1,2); {0,1,2, 0,2,3} drew a bow-tie that double-blended
        // the left wedge along 45° diagonals and left the right wedge uncovered.
        public static ReadOnlySpan<Vector2> Corners => QuadCorners;
        public static ReadOnlySpan<ushort> Indices => QuadIndices;
        private static readonly ushort[] QuadIndices = { 0, 1, 2, 2, 1, 3 };

        public VectraRenderer(IRHIDevice device, Func<ShaderStage, string, ShaderDesc> shaderLoader,
            TextureFormat colorFormat = TextureFormat.RGBA8Unorm,
            VectraQualityProfile? quality = null)
        {
            _device = device;
            Quality = quality ?? (device.Backend switch
            {
                RHIBackend.Metal => VectraQualityProfile.High,
                RHIBackend.DirectX11 => VectraQualityProfile.Low,
                _ => VectraQualityProfile.Balanced,
            });
            _maxDrawParticles = Quality switch
            {
                VectraQualityProfile.Low => 768,
                VectraQualityProfile.Balanced => 2048,
                _ => 4096,
            };
            _lightMapMaxDimension = Quality switch
            {
                VectraQualityProfile.Low => 64,
                VectraQualityProfile.Balanced => 96,
                _ => 128,
            };
            SoftParticleFadeDistance = Quality switch
            {
                VectraQualityProfile.Low => 0.45f,
                VectraQualityProfile.Balanced => 0.65f,
                _ => 0.85f,
            };
            _smokePipeline = CreatePipeline(shaderLoader, colorFormat, BlendState.AlphaBlend);
            // Additive glow variant (sparks/fire): same shaders, src-alpha/one.
            // Null when creation fails — Draw falls back to alpha for all.
            try { _smokeAdditivePipeline = CreatePipeline(shaderLoader, colorFormat, BlendState.Additive); }
            catch { _smokeAdditivePipeline = null; }
            CreateBuffers();
        }

        public bool Ready => !_disposed && _smokePipeline != null && _vertexBuffer != null && _indexBuffer != null;
        public int LastDrawn { get; private set; }
        public VectraQualityProfile Quality { get; }
        public float SoftParticleFadeDistance { get; set; }

        // Back-to-front order scratch (reused; paired Array.Sort, no allocation).
        private readonly int[] _order = new int[VectraPool.MaxParticles];
        private readonly float[] _keys = new float[VectraPool.MaxParticles];

        /// <summary>
        /// Back-to-front index order for a key array: order[k] = original index
        /// of the k-th NEAREST particle (fillers consume from the far end).
        /// Pure + allocation-free; unit-tested (sort is load-bearing for alpha).
        /// </summary>
        internal static void ComputeBackToFrontOrder(float[] keys, int[] order, int live)
        {
            for (int i = 0; i < live; i++)
                order[i] = i;
            Array.Sort(keys, order, 0, live);
        }

        private IRHIPipeline? CreatePipeline(Func<ShaderStage, string, ShaderDesc> loader, TextureFormat colorFormat, BlendState blend)
        {
            try
            {
                var vs = loader(ShaderStage.Vertex, "vs_vectra");
                var fs = loader(ShaderStage.Fragment, "fs_smoke");
                if (vs.Bytecode == null || vs.Bytecode.Length == 0) return null;
                if (fs.Bytecode == null || fs.Bytecode.Length == 0) return null;
                return _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
                {
                    VertexShader = vs,
                    FragmentShader = fs,
                    VertexLayout = new VertexLayoutDesc
                    {
                        Attributes = new[]
                        {
                            new VertexAttribute { Location = 0, Binding = 0, Format = TextureFormat.RG32Float, Offset = 0 },
                            new VertexAttribute { Location = 1, Binding = 1, Format = TextureFormat.RGB32Float, Offset = 0 },
                            new VertexAttribute { Location = 2, Binding = 1, Format = TextureFormat.RGBA32Float, Offset = 12 },
                            new VertexAttribute { Location = 3, Binding = 1, Format = TextureFormat.RGBA32Float, Offset = 28 },
                        },
                        Bindings = new[]
                        {
                            new VertexBinding { Binding = 0, Stride = CornerStride, PerInstance = false },
                            new VertexBinding { Binding = 1, Stride = InstanceStride, PerInstance = true },
                        },
                    },
                    Topology = PrimitiveTopology.TriangleList,
                    BlendState = blend,
                    DepthStencilState = new DepthStencilState
                    {
                        DepthTestEnabled = true,
                        DepthWriteEnabled = false,
                        DepthCompareOp = CompareOp.Less,
                    },
                    RasterizerState = new RasterizerState { CullMode = CullMode.None },
                    ColorFormats = new[] { colorFormat },
                    DepthFormat = TextureFormat.Depth32Float,
                    DebugName = "VectraSmoke",
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Vectra] Pipeline creation failed: {ex.Message}");
                return null;
            }
        }

        private void CreateBuffers()
        {
            for (int r = 0; r < 3; r++)
            _vertexBufferRing[r] = _device.CreateBuffer(new BufferDesc
            {
                Size = (ulong)(VectraPool.MaxParticles * InstanceStride),
                Usage = BufferUsage.Vertex | BufferUsage.TransferDst,
                MemoryType = MemoryType.CpuToGpu,
                DebugName = $"Vectra.Instances{r}",
            });
            _linearSampler = _device.CreateSampler(SamplerDesc.LinearClamp);
            _cornerBuffer = _device.CreateBuffer(new BufferDesc
            {
                Size = (ulong)(QuadCorners.Length * CornerStride),
                Usage = BufferUsage.Vertex | BufferUsage.TransferDst,
                MemoryType = MemoryType.CpuToGpu,
                DebugName = "Vectra.QuadCorners",
            });
            _device.UpdateBuffer(_cornerBuffer, MemoryMarshal.AsBytes(QuadCorners.AsSpan()));
            _indexBuffer = _device.CreateBuffer(new BufferDesc
            {
                Size = (ulong)(QuadIndices.Length * sizeof(ushort)),
                Usage = BufferUsage.Index | BufferUsage.TransferDst,
                MemoryType = MemoryType.CpuToGpu,
                DebugName = "Vectra.QuadIndices",
            });
            _device.UpdateBuffer(_indexBuffer, MemoryMarshal.AsBytes(QuadIndices.AsSpan()));
            LoadDefaultSprite();
        }

        /// <summary>Custom sprite PNG filename searched near the working directory.</summary>
        public const string DefaultSpriteFileName = "smoke.png";
        public string? SpritePath { get; private set; }

        private void LoadDefaultSprite()
        {
            foreach (var dir in new[]
            {
                Directory.GetCurrentDirectory(),
                Path.Combine(Directory.GetCurrentDirectory(), ".."),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."),
            })
            {
                try
                {
                    string candidate = Path.Combine(dir, DefaultSpriteFileName);
                    if (File.Exists(candidate))
                    {
                        SetSprite(candidate);
                        return;
                    }
                }
                catch { }
            }
            SetBakedSprite();
        }

        /// <summary>
        /// Points the renderer at a PNG (editor texture row calls this).
        /// Null/empty/missing/garbage resolves to the baked sprite, loudly.
        /// </summary>
        public void SetSprite(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                var loaded = VectraSprite.TryLoadCustomPng(path);
                if (loaded.HasValue)
                {
                    UploadSprite(loaded.Value.Rgba, loaded.Value.W, loaded.Value.H);
                    SpritePath = path;
                    Console.WriteLine($"[Vectra] Custom smoke sprite: {path} ({loaded.Value.W}x{loaded.Value.H})");
                    return;
                }
                Console.WriteLine($"[Vectra] Sprite '{path}' unusable — baked fallback.");
            }
            SetBakedSprite();
        }

        private void SetBakedSprite()
        {
            UploadSprite(VectraSprite.Bake(), VectraSprite.Size, VectraSprite.Size);
            SpritePath = null;
        }

        private void UploadSprite(byte[] rgba, int w, int h)
        {
            var maps = VectraSprite.BakeDirectionalLightMaps(rgba, w, h, _lightMapMaxDimension);
            IRHITexture? sprite = null, positive = null, negative = null;
            try
            {
                sprite = CreateSampledTexture(rgba, w, h, "Vectra.SmokeDensity");
                positive = CreateSampledTexture(maps.Positive, maps.W, maps.H, "Vectra.LightMapPositive");
                negative = CreateSampledTexture(maps.Negative, maps.W, maps.H, "Vectra.LightMapNegative");
            }
            catch
            {
                sprite?.Dispose(); positive?.Dispose(); negative?.Dispose();
                throw;
            }
            // In-flight frames may sample the old sprite — drain first.
            DrainMetalFlight();
            _sprite?.Dispose();
            _lightMapPositive?.Dispose();
            _lightMapNegative?.Dispose();
            _sprite = sprite;
            _lightMapPositive = positive;
            _lightMapNegative = negative;
        }

        private IRHITexture CreateSampledTexture(byte[] rgba, int w, int h, string debugName)
        {
            // Mip chains on Metal kill minification shimmer on distant puffs;
            // capped at 4 levels (tiny textures, +33% memory worst case).
            uint levels = 1;
            if (_device.Backend == RHIBackend.Metal)
            {
                uint maxDim = (uint)Math.Max(1, Math.Max(w, h));
                levels = 1;
                while (levels < 4 && (maxDim >> (int)levels) >= 8)
                    levels++;
            }
            var texture = _device.CreateTexture(new TextureDesc
            {
                Width = (uint)Math.Max(1, w),
                Height = (uint)Math.Max(1, h),
                Depth = 1,
                MipLevels = levels,
                ArrayLayers = 1,
                Format = TextureFormat.RGBA8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDst,
                DebugName = debugName,
            });
            try
            {
                _device.UploadTexture(texture, rgba);
                if (levels > 1)
                    _device.GenerateMipmaps(texture);
                return texture;
            }
            catch
            {
                texture.Dispose();
                throw;
            }
        }

        private IRHITexture EnsureDepthSnapshot(IRHITexture sceneDepth)
        {
            if (_depthSnapshot != null && _depthSnapshotWidth == sceneDepth.Width &&
                _depthSnapshotHeight == sceneDepth.Height && _depthSnapshot.Format == sceneDepth.Format)
                return _depthSnapshot;

            // M4b: recreate disposes a live texture — drain in-flight frames first.
            DrainMetalFlight();
            _depthSnapshot?.Dispose();
            _depthSnapshot = _device.CreateTexture(new TextureDesc
            {
                Width = sceneDepth.Width,
                Height = sceneDepth.Height,
                Depth = 1,
                MipLevels = 1,
                ArrayLayers = 1,
                Format = sceneDepth.Format,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDst,
                DebugName = "Vectra.SceneDepthSnapshot",
            });
            _depthSnapshotWidth = sceneDepth.Width;
            _depthSnapshotHeight = sceneDepth.Height;
            return _depthSnapshot;
        }

        /// <summary>
        /// Fills quads back-to-front by camera distance (painter's algorithm for
        /// the alpha bucket) and draws once. Sort is Array.Sort over reused scratch
        /// with a cached comparer: zero per-frame allocation, ~microseconds at
        /// smoke counts. Returns live count drawn (0 when nothing to do).
        /// </summary>
        public int Draw(IRHICommandBuffer cmd, IRHIBuffer viewUniformBuffer, VectraPool pool, Vector3 cameraPos,
            IRHITexture? colorTarget, IRHITexture? sceneDepth, int viewportX, int viewportY,
            int viewportW, int viewportH, Matrix4x4 inverseProjection)
        {
            LastDrawn = 0;
            if (!Ready)
                return 0;
            int live = Math.Min(pool.LiveCount, _maxDrawParticles);
            if (live <= 0)
                return 0;
            for (int i = 0; i < live; i++)
            {
                pool.Read(i, out float px, out float py, out float pz,
                    out _, out _, out _, out _, out _, out _);
                float dx = px - cameraPos.X, dy = py - cameraPos.Y, dz = pz - cameraPos.Z;
                _keys[i] = dx * dx + dy * dy + dz * dz;
            }
            ComputeBackToFrontOrder(_keys, _order, live);
            // Partition far-first order into alpha [0,countA) + additive
            // [countA,live): order within each mode stays far-first, so one
            // instanced draw per mode with correct layering. Two linear passes
            // over sorted indices — still microseconds, zero allocations.
            int countA = 0;
            for (int s = 0; s < live; s++)
            {
                int i = _order[live - 1 - s];
                pool.Read(i, out float px, out float py, out float pz,
                    out float size, out float r, out float g, out float b, out float a, out float seed, out float add);
                if (add >= 0.5f) continue;
                float age01 = pool.Age(i) / pool.Life(i);
                _staging[countA++] = new VectraVertex
                {
                    Center = new Vector3(px, py, pz),
                    Color = new Vector4(r, g, b, a),
                    Prm = new Vector4(size, age01, seed, 0f),
                };
            }
            int countB = 0;
            for (int s = 0; s < live; s++)
            {
                int i = _order[live - 1 - s];
                pool.Read(i, out float px, out float py, out float pz,
                    out float size, out float r, out float g, out float b, out float a, out float seed, out float add);
                if (add < 0.5f) continue;
                float age01 = pool.Age(i) / pool.Life(i);
                _staging[countA + countB++] = new VectraVertex
                {
                    Center = new Vector3(px, py, pz),
                    Color = new Vector4(r, g, b, a),
                    Prm = new Vector4(size, age01, seed, 1f),
                };
            }
            _device.UpdateBuffer(_vertexBuffer!,
                MemoryMarshal.AsBytes(new ReadOnlySpan<VectraVertex>(_staging, 0, live)));

            IRHITexture? depthSnapshot = null;
            if (colorTarget != null && sceneDepth != null && sceneDepth.Format == TextureFormat.Depth32Float &&
                sceneDepth.Width == colorTarget.Width && sceneDepth.Height == colorTarget.Height)
            {
                depthSnapshot = EnsureDepthSnapshot(sceneDepth);
                cmd.EndRenderPass();
                cmd.CopyTexture(sceneDepth, depthSnapshot);
                cmd.BeginRenderPass(new[] { colorTarget }, sceneDepth, ClearValue.Load());
                cmd.SetViewport(new BlueSky.BSR.RHI.Viewport
                {
                    X = viewportX, Y = viewportY,
                    Width = viewportW, Height = viewportH,
                    MinDepth = 0f, MaxDepth = 1f,
                });
                cmd.SetScissor(new Scissor
                {
                    X = viewportX, Y = viewportY,
                    Width = (uint)Math.Max(1, viewportW), Height = (uint)Math.Max(1, viewportH),
                });
            }

            cmd.SetPipeline(_smokePipeline!);
            cmd.SetVertexBuffer(_cornerBuffer!, 0, 0);
            cmd.SetVertexBuffer(_vertexBuffer!, 1, 0);
            cmd.SetIndexBuffer(_indexBuffer!, IndexType.UInt16, 0);
            cmd.SetUniformBuffer(viewUniformBuffer, 10);
            if (_sprite != null)
                cmd.SetTexture(_sprite, 0, 0);
            if (_lightMapPositive != null)
                cmd.SetTexture(_lightMapPositive, _device.Backend == RHIBackend.DirectX11 ? 10u : 1u, 0);
            if (_lightMapNegative != null)
                cmd.SetTexture(_lightMapNegative, _device.Backend == RHIBackend.DirectX11 ? 11u : 2u, 0);
            // Bound sampler for fs_smoke [[sampler(0)]] (slot mirrors texture).
            if (_linearSampler != null && _device.Backend != RHIBackend.DirectX11)
            {
                if (_sprite != null)
                    cmd.SetSampler(_linearSampler, 0, 0);
                if (_lightMapPositive != null)
                    cmd.SetSampler(_linearSampler, 1, 0);
                if (_lightMapNegative != null)
                    cmd.SetSampler(_linearSampler, 2, 0);
            }
            if (depthSnapshot != null)
            {
                cmd.SetTexture(depthSnapshot, _device.Backend == RHIBackend.DirectX11 ? 12u : 3u, 0);
                Span<VectraDepthUniforms> depthUniform = stackalloc VectraDepthUniforms[1];
                depthUniform[0] = new VectraDepthUniforms
                {
                    InverseProjection = inverseProjection,
                    Parameters = new Vector4(Math.Max(1e-3f, SoftParticleFadeDistance), 0f, 0f, 0f),
                };
                cmd.SetFragmentUniforms(11, MemoryMarshal.AsBytes(depthUniform));
            }
            // Alpha batch always; additive batch only when particles need it.
            // DX11 FL10.x ignores firstInstance — single alpha draw there.
            if (_device.Backend == RHIBackend.DirectX11 || _smokeAdditivePipeline == null || countB == 0)
            {
                cmd.DrawIndexed(IndicesPerParticle, (uint)live);
            }
            else
            {
                if (countA > 0)
                    cmd.DrawIndexed(IndicesPerParticle, (uint)countA, 0, 0, 0);
                cmd.SetPipeline(_smokeAdditivePipeline);
                cmd.DrawIndexed(IndicesPerParticle, (uint)countB, 0, 0, (uint)countA);
            }
            LastDrawn = live;
            return live;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _smokePipeline?.Dispose();
            _smokeAdditivePipeline?.Dispose();
            foreach (var b in _vertexBufferRing) b?.Dispose();
            _linearSampler?.Dispose();
            _cornerBuffer?.Dispose();
            _indexBuffer?.Dispose();
            _sprite?.Dispose();
            _lightMapPositive?.Dispose();
            _lightMapNegative?.Dispose();
            _depthSnapshot?.Dispose();
        }
    }
}
