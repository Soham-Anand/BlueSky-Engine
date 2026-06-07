using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NotBSRenderer;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Platform.Detection;
using BlueSky.Platform;

// Disambiguate from BlueSky.Rendering.Viewport class
using RHIViewport = NotBSRenderer.Viewport;
using RHIScissor = NotBSRenderer.Scissor;

namespace BlueSky.Rendering.EasePlus;

/// <summary>
/// Ease+ Ultimate Renderer — Deferred-quality visuals at forward-rendering speed.
///
/// Pipeline: PrePass(Depth+Normal) → LightPass(half-res tiled PBR+SH GI) → MaterialPass(forward combine) → PostFX
///
/// Target: 120fps-oriented fallback path on Intel HD 3000 (DX10.1 / SM 4.1 / shared memory).
/// </summary>
public class EasePlusRenderer : IRenderer
{
    private const int MaxDeferredInstances = 1024; // 64 KB of matrices: DX11 constant-buffer safe.

    private readonly IRHIDevice _device;
    private readonly IWindow _window;

    // ── Subsystems ───────────────────────────────────────────────────────
    private readonly EasePlusMemoryManager _memory;
    private readonly EasePlusLightCuller _lightCuller;
    private readonly EasePlusQualityGovernor _governor;
    private readonly bool _legacyGpuMode;
    private readonly bool _polarisAvailable;
    private readonly int _maxDynamicLights;

    // ── Pipelines ────────────────────────────────────────────────────────
    private IRHIPipeline? _prePassPipeline;
    private IRHIPipeline? _prePassMaskedPipeline;  // For foliage/fences with alpha clip
    private IRHIPipeline? _lightPassPipeline;
    private IRHIPipeline? _materialPassPipeline;
    private IRHIPipeline? _transparentPassPipeline;
    private IRHIPipeline? _postFXPipeline;
    private IRHIPipeline? _skyPassPipeline;
    private IRHIPipeline? _gridPassPipeline;
    
    private BlueSky.Rendering.RayTracing.Polaris.PolarisRayTracer? _polarisRayTracer;
    
    private BlueSky.Editor.ViewportRenderer? _viewportRenderer;
    private bool _hasDeferredPipelines;
    private IRHIBuffer? _deferredInstanceBuffer;
    private readonly List<DeferredMeshItem> _deferredItems = new(512);
    private readonly List<EasePlusInstanceUniforms> _deferredInstances = new(512);

    // ── Frame State ──────────────────────────────────────────────────────
    private uint _frameIndex;
    private bool _initialized;
    private uint _screenWidth = 1280, _screenHeight = 720;
    private uint _requestedViewportWidth;
    private uint _requestedViewportHeight;
    private bool _hasRequestedViewportSize;
    
    // ── Test Geometry (until mesh loading is implemented) ────────────────
    private EasePlusTestGeometry.Mesh _testCube;
    private EasePlusTestGeometry.Mesh _testSphere;
    private EasePlusTestGeometry.Mesh _testPlane;

    // ── Resource Tracking (legacy IRenderer) ─────────────────────────────
    private readonly Dictionary<int, IRHIBuffer> _vertexBuffers = new();
    private readonly Dictionary<int, IRHIBuffer> _indexBuffers = new();
    private readonly Dictionary<int, uint> _indexCounts = new(); // Track index count per mesh
    private readonly Dictionary<int, IRHIPipeline> _shaders = new();
    private readonly Dictionary<int, IRHITexture> _textures = new();
    private int _nextResourceId = 1;

    // ── Uniform Structs ──────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct EasePlusViewUniforms
    {
        public Matrix4x4 View, Proj, ViewProj, InvViewProj;
        public Vector4 CameraPos; // xyz = pos, w = time
        public Vector2 ScreenSize;
        public float NearPlane, FarPlane;
        public Vector3 SunDirection;
        public float SunIntensity;
        public Vector3 SunColor;
        public int TilesX;

        // GI Grid Uniforms
        public Vector3 GridOrigin;
        private float _pad0;
        public Vector3 GridSpacing;
        private float _pad1;
        public int GridSizeX;
        public int GridSizeY;
        public int GridSizeZ;
        public int TotalProbes; // Unused, keeping for struct layout compat
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EasePlusPostFXUniforms
    {
        public Vector2 ScreenSize;
        public Vector2 InvScreenSize;
        public float Time;
        public float FXAAThreshold;
        public float FilmGrainIntensity;
        public float VignetteIntensity;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EasePlusObjectUniforms
    {
        public Matrix4x4 Model;
        public Vector4 AlbedoColor; // RGB + alpha
        public float Metallic, Roughness, AO, Emission;
        public int UseAlbedoTex;
        public int UseNormalTex;
        public int UseRMATex;
        public int UseInstanceBuffer;
        public int InstanceBase;
        public int _pad0;
        public int _pad1;
        public int _pad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EasePlusInstanceUniforms
    {
        public Matrix4x4 Model;
    }

    private struct DeferredMeshItem
    {
        public BlueSky.Editor.ViewportRenderer.MeshGPUData GpuData;
        public BlueSky.Editor.ViewportRenderer.SubmeshInfo Submesh;
        public Matrix4x4 Model;
        public string MaterialPath;
        public BlueSky.Core.Assets.MaterialAsset? Material;
        public IRHITexture? AlbedoTexture;
        public IRHITexture? NormalTexture;
        public IRHITexture? RmaTexture;
        public Vector3 EntityPosition;
        public float DistanceToCameraSq;
        public int InstanceIndex;
    }
    
    public void SetViewportRenderer(BlueSky.Editor.ViewportRenderer renderer)
    {
        _viewportRenderer = renderer;
    }

    public IRHITexture? FinalTarget => _memory.PostFXTarget;

    public EasePlusRenderer(IWindow window, IRHIDevice device)
    {
        _window = window;
        _device = device;
        _memory = new EasePlusMemoryManager(device);
        _lightCuller = new EasePlusLightCuller();
        _governor = new EasePlusQualityGovernor();

        var gpuCaps = ProbeGpuSafe();
        _legacyGpuMode = IsLegacyGpu(gpuCaps) || IsEnvEnabled("BLUESKY_LEGACY_GPU");
        _polarisAvailable = _legacyGpuMode && ProbeAvxSafe();
        _maxDynamicLights = _legacyGpuMode ? 24 : EasePlusLightCuller.MAX_LIGHTS;

        if (_legacyGpuMode)
        {
            _memory.ConfigureLightingResolution(4);
            _governor.ConfigureForLegacyGpu();
            Console.WriteLine("[Ease+] Legacy GPU mode enabled (HD 3000/i5-2410m class)");
            Console.WriteLine(_polarisAvailable
                ? "[Ease+] Polaris assist available: AVX CPU RT can be used for low-res RT features"
                : "[Ease+] Polaris assist unavailable: AVX not detected");
        }

        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              Ease+ Ultimate Renderer v1.0                   ║");
        Console.WriteLine("║    Deferred Quality • Forward Speed • HD 3000 Ready         ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
    }

    private static GpuCapabilities ProbeGpuSafe()
    {
        try
        {
            return GpuDetector.Probe();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Ease+] GPU probe failed: {ex.Message}");
            return default;
        }
    }

    private static bool ProbeAvxSafe()
    {
        try
        {
            return BlueSky.Core.Platform.Detection.ProcessorCapabilities.Probe().SupportsAvx;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLegacyGpu(GpuCapabilities caps)
    {
        string name = caps.Name ?? "";
        string vendor = caps.Vendor ?? "";
        bool hd3000 = name.Contains("HD Graphics 3000", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("HD 3000", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("Sandy Bridge", StringComparison.OrdinalIgnoreCase);
        bool weakIntegrated = caps.Tier == GpuTier.Low
                           && (caps.IsIntegrated || vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                           && (caps.VramMB <= 512 || caps.VramMB == 0);
        return hd3000 || weakIntegrated;
    }

    private static bool IsEnvEnabled(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return value == "1"
            || value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true
            || value?.Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
    }

    public void Initialize()
    {
        if (_initialized) return;

        var desiredSize = GetDesiredRenderSize();
        _screenWidth = desiredSize.Width;
        _screenHeight = desiredSize.Height;

        Console.WriteLine($"[Ease+] Initializing at {_screenWidth}×{_screenHeight}");

        // Allocate all render targets
        _memory.Allocate(_screenWidth, _screenHeight);

        // Initialize subsystems
        _lightCuller.SetScreenSize(_screenWidth, _screenHeight);

        // Create pipelines
        CreatePipelines();

        _deferredInstanceBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)(MaxDeferredInstances * Marshal.SizeOf<EasePlusInstanceUniforms>()),
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "Ease+.DeferredInstances"
        });
        
        if (_viewportRenderer == null)
        {
            // Standalone renderer smoke-test geometry. The editor path should show
            // the user's scene only, never debug balls/cubes.
            _testCube = EasePlusTestGeometry.CreateCube(_device);
            _testSphere = EasePlusTestGeometry.CreateSphere(_device, 24, 12);
            _testPlane = EasePlusTestGeometry.CreatePlane(_device, 20.0f, 20);
        }
        else
        {
            Console.WriteLine("[Ease+] Editor viewport mode: debug test geometry disabled");
        }

        _initialized = true;
        _governor.SetTargetFPS(120);

        if (_polarisAvailable)
        {
            try
            {
                _polarisRayTracer = new BlueSky.Rendering.RayTracing.Polaris.PolarisRayTracer(_device);
                Console.WriteLine("[Ease+] Polaris Ray Tracer initialized successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ease+] Failed to initialize Polaris Ray Tracer: {ex.Message}");
                Console.WriteLine("[Ease+] No Ray-Tracing System is supported on this hardware (Requires AVX or GPU RT Cores).");
            }
        }
        else
        {
            Console.WriteLine("[Ease+] No Ray-Tracing System is supported on this hardware (Requires AVX or GPU RT Cores).");
        }

        Console.WriteLine("[Ease+] ✓ Initialization complete");
        Console.WriteLine($"[Ease+] Memory: ~{EstimateTotalMemory():F1} MB");
        Console.WriteLine($"[Ease+] Backend: {_device.Backend}");
        BlueSky.Core.Platform.Detection.ProcessorCapabilities.Probe().LogRayTracingSummary();
    }

    // ── Frame Lifecycle ──────────────────────────────────────────────────

    public void BeginFrame(float r, float g, float b, float a = 1.0f)
    {
        _governor.BeginFrame();
        _lightCuller.ClearLights();
        _frameIndex++;
    }

    public void EndFrame()
    {
        _governor.EndFrame();

        // Log stats periodically
        if (_frameIndex % 300 == 0)
        {
            _governor.LogStats();
            _lightCuller.LogStats();
        }
    }

    public void RenderScene(World world, CameraComponent camera, TransformComponent cameraTransform)
    {
        var dir = new Core.Math.Vector3(0.5f, 0.7f, 0.3f);
        var pos = new Core.Math.Vector3(-15f, 21f, 9f);
        RenderSceneWithShadows(world, camera, cameraTransform, pos, dir);
    }

    public void RenderSceneWithShadows(World world, CameraComponent camera, TransformComponent cameraTransform,
        Core.Math.Vector3 lightPos, Core.Math.Vector3 lightDir)
    {
        if (!_initialized) return;

        var desiredSize = GetDesiredRenderSize();
        EnsureRenderTargetSize(desiredSize.Width, desiredSize.Height);

        var cmd = _device.CreateCommandBuffer();
        var sunDir = new Vector3(lightDir.X, lightDir.Y, lightDir.Z);

        // Build camera matrices using the ENGINE's math — same as UltraRenderer.
        // This ensures identical camera behavior between Forward+ and Ease+ modes.
        // We convert to System.Numerics.Matrix4x4 only for GPU upload.
        var bsView = Core.Math.Matrix4x4.CreateLookAt(
            cameraTransform.Position,
            cameraTransform.Position + cameraTransform.Forward,
            Core.Math.Vector3.Up);
        var bsProj = camera.GetProjectionMatrix();
        var view = ToSystemMatrix4x4(bsView);
        var proj = ToSystemMatrix4x4(bsProj);
        var camPos = new Vector3(cameraTransform.Position.X, cameraTransform.Position.Y, cameraTransform.Position.Z);

        if (UseEditorViewportFallback() || !_hasDeferredPipelines)
        {
            RenderViewportRendererFallback(cmd, view, proj, camPos, sunDir);
            _device.Submit(cmd);
            cmd.Dispose();
            return;
        }
        var viewProj = view * proj;
        Matrix4x4.Invert(viewProj, out var invViewProj);

        // ── Upload view uniforms ─────────────────────────────────────────
        var viewUniforms = new EasePlusViewUniforms
        {
            View = view, Proj = proj, ViewProj = viewProj, InvViewProj = invViewProj,
            CameraPos = new Vector4(camPos, (float)_window.Time),
            ScreenSize = new Vector2(_screenWidth, _screenHeight),
            NearPlane = camera.NearPlane, FarPlane = camera.FarPlane,
            SunDirection = Vector3.Normalize(sunDir),
            SunIntensity = 0.0f, // Will be set by CollectLights
            SunColor = Vector3.Zero,
            TilesX = _lightCuller.GetTileGridSize().x,
            GridOrigin = Vector3.Zero,
            GridSpacing = Vector3.One,
            GridSizeX = 1,
            GridSizeY = 1,
            GridSizeZ = 1,
            TotalProbes = 0
        };

        // ── Collect lights from ECS ──────────────────────────────────────
        // This will update viewUniforms with the sun direction/color
        CollectLightsFromWorld(world, ref viewUniforms);

        var viewSpan = MemoryMarshal.CreateSpan(ref viewUniforms, 1);
        var viewBytes = MemoryMarshal.AsBytes(viewSpan);
        _device.UpdateBuffer(_memory.ViewUniformBuffer!, viewBytes);

        bool logFrame = ShouldLogDetailedFrame();
        BuildDeferredMeshItems(world, view * proj, camPos, logFrame);
        UploadDeferredInstances();

        // ── CPU Light Culling ────────────────────────────────────────────
        _governor.BeginPass("LightCull");
        _lightCuller.Cull(view, proj, _screenWidth, _screenHeight);
        UploadTileData();
        _governor.EndPass("LightCull");

        // ── Pass 1: Depth + Normal Pre-Pass ──────────────────────────────
        _governor.BeginPass("PrePass");
        ExecutePrePass(cmd, world, view, proj);
        _governor.EndPass("PrePass");

        // ── Pass 2: Half-Res Light Accumulation ──────────────────────────
        _governor.BeginPass("LightPass");
        ExecuteLightPass(cmd);
        _governor.EndPass("LightPass");

        // ── Pass 2.5: Background Clear ──────────────────────────────────
        // MaterialPass must load the pre-pass depth buffer, so color is
        // initialized separately. Clearing in PostFX erased rendered meshes.
        _governor.BeginPass("Background");
        ExecuteBackgroundPass(cmd);
        _governor.EndPass("Background");

        // ── Pass 3: Forward Material Combine ─────────────────────────────
        _governor.BeginPass("MaterialPass");
        ExecuteMaterialPass(cmd, world, view, proj, camPos);
        _governor.EndPass("MaterialPass");

        // ── Pass 4: Cheap Editor Grid ────────────────────────────────────
        _governor.BeginPass("Grid");
        ExecuteGridPass(cmd);
        _governor.EndPass("Grid");

        // ── Pass 5: Post-Processing ──────────────────────────────────────
        _governor.BeginPass("PostFX");
        ExecutePostFX(cmd);
        _governor.EndPass("PostFX");

        // Submit
        _device.Submit(cmd);
        cmd.Dispose();
    }

    // ── Pass Implementations ─────────────────────────────────────────────

    private void ExecutePrePass(IRHICommandBuffer cmd, World world, Matrix4x4 view, Matrix4x4 proj)
    {
        // Render all solid geometry writing ONLY depth + normals (no material data)
        cmd.BeginRenderPass(
            new[] { _memory.GBufferNormal! },
            _memory.GBufferDepth,
            ClearValue.FromColor(0, 0, 0, 0));

        cmd.SetViewport(new RHIViewport { Width = _screenWidth, Height = _screenHeight, MinDepth = 0, MaxDepth = 1 });
        cmd.SetScissor(new RHIScissor { Width = _screenWidth, Height = _screenHeight });

        if (_prePassPipeline != null)
        {
            cmd.SetPipeline(_prePassPipeline);
            cmd.SetUniformBuffer(_memory.ViewUniformBuffer!, 10u);
            if (_deferredInstanceBuffer != null)
                cmd.SetUniformBuffer(_deferredInstanceBuffer, 12u);
            
            bool renderedAny = DrawDeferredItems(cmd, prePass: true);
            
            // Fallback to test geometry if no entities
            if (!renderedAny)
            {
                if (_viewportRenderer == null)
                {
                    Console.WriteLine("[Ease+] PrePass: No entities rendered, falling back to test geometry");
                    DrawTestScene(cmd, _prePassPipeline, isPrePass: true);
                }
                else if (ShouldLogDetailedFrame())
                {
                    Console.WriteLine("[Ease+] PrePass: Empty editor scene");
                }
            }
            else if (ShouldLogDetailedFrame())
            {
                Console.WriteLine($"[Ease+] PrePass: {_deferredItems.Count} submesh instances");
            }
        }

        cmd.EndRenderPass();
    }

    private void ExecuteLightPass(IRHICommandBuffer cmd)
    {
        // Fullscreen quad at half resolution — accumulate lighting
        cmd.BeginRenderPass(
            new[] { _memory.LightBuffer! },
            _memory.LightDepth,
            ClearValue.FromColor(0, 0, 0, 0));

        uint hw = _memory.HalfWidth, hh = _memory.HalfHeight;
        cmd.SetViewport(new RHIViewport { Width = hw, Height = hh, MinDepth = 0, MaxDepth = 1 });
        cmd.SetScissor(new RHIScissor { Width = hw, Height = hh });

        if (_lightPassPipeline != null)
        {
            cmd.SetPipeline(_lightPassPipeline);
            cmd.SetUniformBuffer(_memory.ViewUniformBuffer!, 10u);
            cmd.SetUniformBuffer(_memory.TileLightBuffer!, 1);
            cmd.SetUniformBuffer(_memory.LightDataBuffer!, 2);
            cmd.SetUniformBuffer(_memory.SHProbeBuffer!, 3);
            cmd.SetTexture(_memory.GBufferNormal!, 0);
            cmd.SetTexture(_memory.GBufferDepth!, 1);
            cmd.Draw(3); // Fullscreen triangle
        }

        cmd.EndRenderPass();
    }

    private void ExecuteBackgroundPass(IRHICommandBuffer cmd)
    {
        cmd.BeginRenderPass(
            new[] { _memory.PostFXTarget! },
            null,
            ClearValue.FromColor(0.42f, 0.62f, 0.88f, 1.0f));

        cmd.EndRenderPass();
    }

    private void ExecuteGridPass(IRHICommandBuffer cmd)
    {
        if (_gridPassPipeline == null) return;

        cmd.BeginRenderPass(
            new[] { _memory.PostFXTarget! },
            _memory.GBufferDepth,
            ClearValue.Load());

        cmd.SetViewport(new RHIViewport { Width = _screenWidth, Height = _screenHeight, MinDepth = 0, MaxDepth = 1 });
        cmd.SetScissor(new RHIScissor { Width = _screenWidth, Height = _screenHeight });
        cmd.SetPipeline(_gridPassPipeline);
        cmd.SetUniformBuffer(_memory.ViewUniformBuffer!, 10u);
        cmd.Draw(6);

        cmd.EndRenderPass();
    }

    private void ExecuteMaterialPass(IRHICommandBuffer cmd, World world, Matrix4x4 view, Matrix4x4 proj, Vector3 camPos)
    {
        // Forward re-render with depth-test=LessEqual, sampling the light buffer
        // Use Load() to preserve depth from PrePass instead of clearing it
        cmd.BeginRenderPass(
            new[] { _memory.PostFXTarget! },
            _memory.GBufferDepth, // Bind depth buffer for depth testing against PrePass
            ClearValue.Load()); // Load existing depth instead of clearing

        cmd.SetViewport(new RHIViewport { Width = _screenWidth, Height = _screenHeight, MinDepth = 0, MaxDepth = 1 });
        cmd.SetScissor(new RHIScissor { Width = _screenWidth, Height = _screenHeight });

        if (_materialPassPipeline != null)
        {
            cmd.SetPipeline(_materialPassPipeline);
            cmd.SetUniformBuffer(_memory.ViewUniformBuffer!, 10u);
            if (_deferredInstanceBuffer != null)
                cmd.SetUniformBuffer(_deferredInstanceBuffer, 12u);
            cmd.SetTexture(_memory.LightBuffer!, 0); // Bind pre-computed lighting
            cmd.SetTexture(_memory.GBufferDepth!, 1); // For bilateral upsample and custom depth discard
            
            // Bind default textures at start to prevent Metal driver issues on unbound slots
            if (_viewportRenderer != null)
            {
                cmd.SetTexture(_viewportRenderer.DefaultWhiteTexture, 2);
                cmd.SetTexture(_viewportRenderer.DefaultNormalTexture, 3);
                cmd.SetTexture(_viewportRenderer.DefaultRmaTexture, 4);
            }
            
            bool renderedAny = DrawDeferredItems(cmd, prePass: false);
            
            // Fallback to test geometry if no entities
            if (!renderedAny)
            {
                if (_viewportRenderer == null)
                {
                    Console.WriteLine("[Ease+] MaterialPass: No entities rendered, falling back to test geometry");
                    DrawTestScene(cmd, _materialPassPipeline, isPrePass: false);
                }
                else if (ShouldLogDetailedFrame())
                {
                    Console.WriteLine("[Ease+] MaterialPass: Empty editor scene");
                }
            }
            else if (ShouldLogDetailedFrame())
            {
                Console.WriteLine($"[Ease+] MaterialPass: {_deferredItems.Count} submesh instances");
            }
        }

        cmd.EndRenderPass();
    }

    private void ExecutePostFX(IRHICommandBuffer cmd)
    {
        // PostFX needs a separate destination texture before it can safely run.
        // PostFXTarget already contains the scene color from MaterialPass; clearing
        // it here was the reason meshes appeared to "not load" in Ease+.
    }

    private bool ShouldLogDetailedFrame() => !UseEditorViewportFallback() && (_frameIndex <= 2 || _frameIndex % 240 == 0);

    private bool UseEditorViewportFallback() =>
        _viewportRenderer != null && !IsEnvEnabled("BLUESKY_EASEPLUS_DEFERRED_EDITOR");

    private (uint Width, uint Height) GetDesiredRenderSize()
    {
        if (_hasRequestedViewportSize)
            return (_requestedViewportWidth, _requestedViewportHeight);

        uint width = (uint)Math.Max(1, _window.FramebufferSize.X);
        uint height = (uint)Math.Max(1, _window.FramebufferSize.Y);
        return (width, height);
    }

    private void EnsureRenderTargetSize(uint width, uint height)
    {
        width = Math.Max(width, 1u);
        height = Math.Max(height, 1u);

        if (width == _screenWidth && height == _screenHeight)
            return;

        _screenWidth = width;
        _screenHeight = height;
        _memory.Allocate(_screenWidth, _screenHeight);
        _lightCuller.SetScreenSize(_screenWidth, _screenHeight);
    }

    private void RenderViewportRendererFallback(IRHICommandBuffer cmd, Matrix4x4 view, Matrix4x4 proj, Vector3 camPos, Vector3 sunDir)
    {
        var normalizedSunDir = SafeNormalize(sunDir, new Vector3(0.5f, 0.8f, 0.4f));
        _viewportRenderer?.PreRender(cmd, normalizedSunDir);

        cmd.BeginRenderPass(
            new[] { _memory.PostFXTarget! },
            _memory.GBufferDepth,
            ClearValue.FromColor(0.02f, 0.025f, 0.03f, 1f));

        _viewportRenderer?.Render(cmd, view, proj, camPos, 0, 0, (int)_screenWidth, (int)_screenHeight, 0.016f);

        cmd.EndRenderPass();
    }

    // ── Helper Methods ───────────────────────────────────────────────────

    private void BuildDeferredMeshItems(World world, Matrix4x4 viewProj, Vector3 camPos, bool logFrame)
    {
        _deferredItems.Clear();
        if (_viewportRenderer == null) return;

        Span<Vector4> frustumPlanes = stackalloc Vector4[6];
        ExtractFrustumPlanes(viewProj, frustumPlanes);

        float drawDistance = MathF.Max(40f, _governor.DrawDistance);
        float drawDistanceSq = drawDistance * drawDistance;

        var query = world.CreateQuery()
            .All<TransformComponent>()
            .All<StaticMeshComponent>()
            .Build();

        int visibleEntities = 0;
        int submittedSubmeshes = 0;
        int culledEntities = 0;

        foreach (var chunk in world.GetQueryChunks(query))
        {
            int transformIndex = chunk.GetComponentIndex(typeof(TransformComponent));
            int meshIndex = chunk.GetComponentIndex(typeof(StaticMeshComponent));

            for (int i = 0; i < chunk.Count; i++)
            {
                var transform = chunk.GetComponent<TransformComponent>(i, transformIndex);
                var staticMesh = chunk.GetComponent<StaticMeshComponent>(i, meshIndex);
                string assetId = staticMesh.MeshAssetId;
                if (string.IsNullOrEmpty(assetId)) continue;

                var model = ToSystemMatrix4x4(transform.WorldMatrix);
                var entityPos = new Vector3(model.M41, model.M42, model.M43);
                float maxScale = MathF.Max(MathF.Max(MathF.Abs(transform.Scale.X), MathF.Abs(transform.Scale.Y)), MathF.Abs(transform.Scale.Z));
                float radius = MathF.Max(3.0f, maxScale * 15.0f);
                float distSq = Vector3.DistanceSquared(camPos, entityPos);

                if (distSq > drawDistanceSq || !IsSphereFrustumVisible(entityPos, radius, frustumPlanes))
                {
                    culledEntities++;
                    continue;
                }

                var gpuData = _viewportRenderer.GetCachedMesh(assetId);
                if (gpuData?.VertexBuffer == null || gpuData.IndexBuffer == null || gpuData.Submeshes.Count == 0)
                    continue;

                visibleEntities++;

                foreach (var submesh in gpuData.Submeshes)
                {
                    if (submesh.IndexCount <= 0) continue;

                    string matPath = ResolveMaterialPath(staticMesh, gpuData, submesh.MaterialSlot);
                    var material = _viewportRenderer.LoadCachedMaterial(matPath);
                    var item = new DeferredMeshItem
                    {
                        GpuData = gpuData,
                        Submesh = submesh,
                        Model = model,
                        MaterialPath = matPath ?? string.Empty,
                        Material = material,
                        EntityPosition = entityPos,
                        DistanceToCameraSq = distSq,
                        InstanceIndex = -1
                    };

                    LoadDeferredTextures(ref item);
                    _deferredItems.Add(item);
                    submittedSubmeshes++;
                }
            }
        }

        _deferredItems.Sort(CompareDeferredItems);
        AssignDeferredInstanceSlots();

        if (logFrame)
        {
            Console.WriteLine($"[Ease+] Deferred gather: visible={visibleEntities}, culled={culledEntities}, submeshes={submittedSubmeshes}, instanced={_deferredInstances.Count}");
        }
    }

    private string ResolveMaterialPath(
        StaticMeshComponent staticMesh,
        BlueSky.Editor.ViewportRenderer.MeshGPUData gpuData,
        int materialSlot)
    {
        string matPath = staticMesh.GetEffectiveMaterial(materialSlot);
        if (string.IsNullOrEmpty(matPath) && !gpuData.MaterialSlotPaths.TryGetValue(materialSlot, out matPath!))
        {
            matPath = staticMesh.MaterialAssetId;
        }

        return matPath ?? string.Empty;
    }

    private void LoadDeferredTextures(ref DeferredMeshItem item)
    {
        if (_viewportRenderer == null || item.Material == null) return;

        var material = item.Material;
        if (!string.IsNullOrEmpty(material.AlbedoTexturePath))
            item.AlbedoTexture = _viewportRenderer.LoadCachedTexture(material.AlbedoTexturePath, storedInSrgb: true);
        if (!string.IsNullOrEmpty(material.NormalTexturePath))
            item.NormalTexture = _viewportRenderer.LoadCachedTexture(material.NormalTexturePath, storedInSrgb: false);

        if (!string.IsNullOrEmpty(material.RMATexturePath))
            item.RmaTexture = _viewportRenderer.LoadCachedTexture(material.RMATexturePath, storedInSrgb: false);
        else if (!string.IsNullOrEmpty(material.RoughnessTexturePath))
            item.RmaTexture = _viewportRenderer.LoadCachedTexture(material.RoughnessTexturePath, storedInSrgb: false);
        else if (!string.IsNullOrEmpty(material.MetallicTexturePath))
            item.RmaTexture = _viewportRenderer.LoadCachedTexture(material.MetallicTexturePath, storedInSrgb: false);
    }

    private static int CompareDeferredItems(DeferredMeshItem a, DeferredMeshItem b)
    {
        int meshCompare = RuntimeHelpers.GetHashCode(a.GpuData).CompareTo(RuntimeHelpers.GetHashCode(b.GpuData));
        if (meshCompare != 0) return meshCompare;

        int slotCompare = a.Submesh.IndexOffset.CompareTo(b.Submesh.IndexOffset);
        if (slotCompare != 0) return slotCompare;

        int materialCompare = string.CompareOrdinal(a.MaterialPath, b.MaterialPath);
        if (materialCompare != 0) return materialCompare;

        return a.DistanceToCameraSq.CompareTo(b.DistanceToCameraSq);
    }

    private void AssignDeferredInstanceSlots()
    {
        _deferredInstances.Clear();

        int count = Math.Min(_deferredItems.Count, MaxDeferredInstances);
        for (int i = 0; i < _deferredItems.Count; i++)
        {
            var item = _deferredItems[i];
            if (i < count)
            {
                item.InstanceIndex = i;
                _deferredInstances.Add(new EasePlusInstanceUniforms { Model = item.Model });
            }
            else
            {
                item.InstanceIndex = -1;
            }

            _deferredItems[i] = item;
        }
    }

    private void UploadDeferredInstances()
    {
        if (_deferredInstanceBuffer == null || _deferredInstances.Count == 0) return;

        ReadOnlySpan<EasePlusInstanceUniforms> instances = CollectionsMarshal.AsSpan(_deferredInstances);
        _device.UpdateBuffer(_deferredInstanceBuffer, MemoryMarshal.AsBytes(instances));
    }

    private bool DrawDeferredItems(IRHICommandBuffer cmd, bool prePass)
    {
        if (_deferredItems.Count == 0) return false;

        bool renderedAny = false;
        int i = 0;
        while (i < _deferredItems.Count)
        {
            var first = _deferredItems[i];
            bool canInstance = first.InstanceIndex >= 0;
            int instanceCount = 1;

            if (canInstance)
            {
                while (i + instanceCount < _deferredItems.Count)
                {
                    var next = _deferredItems[i + instanceCount];
                    if (next.InstanceIndex != first.InstanceIndex + instanceCount || !CanBatch(first, next))
                        break;
                    instanceCount++;
                }
            }

            if (!prePass)
                BindDeferredTextures(cmd, first);

            var uniforms = CreateObjectUniforms(first, canInstance, first.InstanceIndex);
            BindObjectUniforms(cmd, ref uniforms);

            cmd.SetVertexBuffer(first.GpuData.VertexBuffer!, 0);
            cmd.SetIndexBuffer(first.GpuData.IndexBuffer!, IndexType.UInt32);
            cmd.DrawIndexed((uint)first.Submesh.IndexCount, (uint)instanceCount, (uint)first.Submesh.IndexOffset, 0, 0);

            renderedAny = true;
            i += instanceCount;
        }

        return renderedAny;
    }

    private static bool CanBatch(DeferredMeshItem a, DeferredMeshItem b)
    {
        return ReferenceEquals(a.GpuData, b.GpuData)
            && a.Submesh.IndexOffset == b.Submesh.IndexOffset
            && a.Submesh.IndexCount == b.Submesh.IndexCount
            && a.Submesh.MaterialSlot == b.Submesh.MaterialSlot
            && string.Equals(a.MaterialPath, b.MaterialPath, StringComparison.Ordinal);
    }

    private EasePlusObjectUniforms CreateObjectUniforms(DeferredMeshItem item, bool useInstanceBuffer, int instanceBase)
    {
        var material = item.Material;
        var albedo = material != null
            ? new Vector3(material.Albedo.X, material.Albedo.Y, material.Albedo.Z)
            : new Vector3(0.8f, 0.8f, 0.8f);

        return new EasePlusObjectUniforms
        {
            Model = useInstanceBuffer ? Matrix4x4.Identity : item.Model,
            AlbedoColor = new Vector4(albedo, material?.Opacity ?? 1.0f),
            Metallic = Math.Clamp(material?.Metallic ?? 0.08f, 0.0f, 1.0f),
            Roughness = Math.Clamp(material?.Roughness ?? 0.55f, 0.04f, 1.0f),
            AO = Math.Clamp(material?.AO ?? 1.0f, 0.0f, 1.0f),
            Emission = material != null
                ? (material.Emission.X + material.Emission.Y + material.Emission.Z) / 3.0f * material.EmissionIntensity
                : 0.0f,
            UseAlbedoTex = item.AlbedoTexture != null ? 1 : 0,
            UseNormalTex = item.NormalTexture != null ? 1 : 0,
            UseRMATex = item.RmaTexture != null ? 1 : 0,
            UseInstanceBuffer = useInstanceBuffer ? 1 : 0,
            InstanceBase = useInstanceBuffer ? instanceBase : 0
        };
    }

    private void BindDeferredTextures(IRHICommandBuffer cmd, DeferredMeshItem item)
    {
        if (_viewportRenderer == null) return;

        cmd.SetTexture(item.AlbedoTexture ?? _viewportRenderer.DefaultWhiteTexture, 2);
        cmd.SetTexture(item.NormalTexture ?? _viewportRenderer.DefaultNormalTexture, 3);
        cmd.SetTexture(item.RmaTexture ?? _viewportRenderer.DefaultRmaTexture, 4);
    }

    private void BindObjectUniforms(IRHICommandBuffer cmd, ref EasePlusObjectUniforms uniforms)
    {
        var span = MemoryMarshal.CreateSpan(ref uniforms, 1);
        var bytes = MemoryMarshal.AsBytes(span);

        if (_device.Backend == RHIBackend.Metal)
        {
            cmd.SetVertexUniforms(11u, bytes);
            cmd.SetFragmentUniforms(11u, bytes);
            return;
        }

        if (_memory.ObjectUniformBuffer == null) return;
        _device.UpdateBuffer(_memory.ObjectUniformBuffer, bytes);
        cmd.SetUniformBuffer(_memory.ObjectUniformBuffer, 11u);
    }
    
    /// <summary>
    /// Draw test scene with procedural geometry (until mesh loading is implemented)
    /// </summary>
    private void DrawTestScene(IRHICommandBuffer cmd, IRHIPipeline pipeline, bool isPrePass)
    {
        // Draw ground plane
        {
            var model = Matrix4x4.Identity;
            var objectUniforms = new EasePlusObjectUniforms
            {
                Model = model,
                AlbedoColor = new Vector4(0.3f, 0.4f, 0.3f, 1.0f), // Green-ish ground
                Metallic = 0.0f,
                Roughness = 0.8f,
                AO = 1.0f,
                Emission = 0.0f
            };
            BindObjectUniforms(cmd, ref objectUniforms);
            
            cmd.SetVertexBuffer(_testPlane.VertexBuffer!, 0);
            cmd.SetIndexBuffer(_testPlane.IndexBuffer!, IndexType.UInt32);
            cmd.DrawIndexed((uint)_testPlane.Indices.Length, 1, 0, 0, 0);
        }
        
        // Draw cube at origin
        {
            var model = Matrix4x4.CreateScale(1.5f) * Matrix4x4.CreateTranslation(0, 1.5f, 0);
            var objectUniforms = new EasePlusObjectUniforms
            {
                Model = model,
                AlbedoColor = new Vector4(0.9f, 0.3f, 0.3f, 1.0f), // Red cube
                Metallic = 0.1f,
                Roughness = 0.4f,
                AO = 1.0f,
                Emission = 0.0f
            };
            BindObjectUniforms(cmd, ref objectUniforms);
            
            cmd.SetVertexBuffer(_testCube.VertexBuffer!, 0);
            cmd.SetIndexBuffer(_testCube.IndexBuffer!, IndexType.UInt32);
            cmd.DrawIndexed((uint)_testCube.Indices.Length, 1, 0, 0, 0);
        }
        
        // Draw sphere to the right
        {
            var model = Matrix4x4.CreateScale(1.2f) * Matrix4x4.CreateTranslation(4, 1.2f, 0);
            var objectUniforms = new EasePlusObjectUniforms
            {
                Model = model,
                AlbedoColor = new Vector4(0.3f, 0.6f, 0.9f, 1.0f), // Blue sphere
                Metallic = 0.8f,
                Roughness = 0.2f,
                AO = 1.0f,
                Emission = 0.0f
            };
            BindObjectUniforms(cmd, ref objectUniforms);
            
            cmd.SetVertexBuffer(_testSphere.VertexBuffer!, 0);
            cmd.SetIndexBuffer(_testSphere.IndexBuffer!, IndexType.UInt32);
            cmd.DrawIndexed((uint)_testSphere.Indices.Length, 1, 0, 0, 0);
        }
        
        // Draw another sphere to the left
        {
            var model = Matrix4x4.CreateScale(1.0f) * Matrix4x4.CreateTranslation(-4, 1.0f, 0);
            var objectUniforms = new EasePlusObjectUniforms
            {
                Model = model,
                AlbedoColor = new Vector4(0.9f, 0.9f, 0.3f, 1.0f), // Yellow sphere
                Metallic = 0.0f,
                Roughness = 0.6f,
                AO = 1.0f,
                Emission = 0.0f
            };
            BindObjectUniforms(cmd, ref objectUniforms);
            
            cmd.SetVertexBuffer(_testSphere.VertexBuffer!, 0);
            cmd.SetIndexBuffer(_testSphere.IndexBuffer!, IndexType.UInt32);
            cmd.DrawIndexed((uint)_testSphere.Indices.Length, 1, 0, 0, 0);
        }
    }

    private static Matrix4x4 ToSystemMatrix4x4(Core.Math.Matrix4x4 m)
    {
        return new Matrix4x4(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44);
    }

    private void CollectLightsFromWorld(World world, ref EasePlusViewUniforms viewUniforms)
    {
        _lightCuller.ClearLights();
        int pointLightCount = 0;
        bool foundSun = false;

        var query = world.CreateQuery()
            .All<TransformComponent>()
            .All<LightComponent>()
            .Build();
        
        var chunks = world.GetQueryChunks(query);
        foreach (var chunk in chunks)
        {
            int transformIndex = chunk.GetComponentIndex(typeof(TransformComponent));
            int lightIndex = chunk.GetComponentIndex(typeof(LightComponent));

            if (lightIndex < 0) continue;

            for (int i = 0; i < chunk.Count; i++)
            {
                var transform = chunk.GetComponent<TransformComponent>(i, transformIndex);
                var light = chunk.GetComponent<LightComponent>(i, lightIndex);
                
                // Directional Lights (Sun)
                if (light.Type == LightComponent.LightType.Directional)
                {
                    if (!foundSun || light.Intensity > viewUniforms.SunIntensity)
                    {
                        var fwd = transform.Forward;
                        viewUniforms.SunDirection = SafeNormalize(new Vector3(fwd.X, fwd.Y, fwd.Z), viewUniforms.SunDirection);
                        viewUniforms.SunColor = new Vector3(light.Color.X, light.Color.Y, light.Color.Z);
                        viewUniforms.SunIntensity = light.Intensity;
                        foundSun = true;
                    }
                }

                // Point Lights
                if (light.Type == LightComponent.LightType.Point && pointLightCount < _maxDynamicLights)
                {
                    _lightCuller.AddPointLight(
                        new Vector3(transform.Position.X, transform.Position.Y, transform.Position.Z),
                        new Vector3(light.Color.X, light.Color.Y, light.Color.Z),
                        light.Intensity,
                        light.Range
                    );
                    pointLightCount++;
                }
            }
        }

        // Fallback sun if none in scene
        if (!foundSun)
        {
            viewUniforms.SunDirection = SafeNormalize(BlueSky.Core.WorldEnvironment.GlobalEnvironment.SunDirection, new Vector3(0.5f, 0.6f, 0.3f));
            viewUniforms.SunColor = BlueSky.Core.WorldEnvironment.GlobalEnvironment.SunColor;
            viewUniforms.SunIntensity = 4.5f;
        }
    }

    private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
    {
        float lenSq = value.LengthSquared();
        return lenSq > 0.000001f ? value / MathF.Sqrt(lenSq) : fallback;
    }

    private static void ExtractFrustumPlanes(Matrix4x4 vp, Span<Vector4> planes)
    {
        planes[0] = new Vector4(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41);
        planes[1] = new Vector4(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41);
        planes[2] = new Vector4(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42);
        planes[3] = new Vector4(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42);
        planes[4] = new Vector4(vp.M13, vp.M23, vp.M33, vp.M43);
        planes[5] = new Vector4(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43);

        for (int i = 0; i < 6; i++)
        {
            float length = MathF.Sqrt(planes[i].X * planes[i].X + planes[i].Y * planes[i].Y + planes[i].Z * planes[i].Z);
            if (length > 0.0001f)
                planes[i] /= length;
        }
    }

    private static bool IsSphereFrustumVisible(Vector3 center, float radius, ReadOnlySpan<Vector4> planes)
    {
        for (int i = 0; i < 6; i++)
        {
            float distance = planes[i].X * center.X + planes[i].Y * center.Y + planes[i].Z * center.Z + planes[i].W;
            if (distance < -radius)
                return false;
        }

        return true;
    }

    private void UploadTileData()
    {
        var tileData = _lightCuller.GetTileData();
        _device.UpdateBuffer(_memory.TileLightBuffer!,
            MemoryMarshal.AsBytes(tileData));

        var lightData = _lightCuller.GetLightData();
        _device.UpdateBuffer(_memory.LightDataBuffer!,
            MemoryMarshal.AsBytes(lightData));

        // Upload SH probe data - Dummy for HD 3000
        // (SDF/GI has been disabled to meet 120fps budget)
    }

    private void CreatePipelines()
    {
        Console.WriteLine("[Ease+] Creating render pipelines...");
        int created = 0;

        // Pre-pass: writes normal + depth only
        try
        {
            _prePassPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
            {
                VertexShader = MakeShader(ShaderStage.Vertex, "easeplus_vs_prepass"),
                FragmentShader = MakeShader(ShaderStage.Fragment, "easeplus_fs_prepass"),
                VertexLayout = StandardVertexLayout(),
                Topology = PrimitiveTopology.TriangleList,
                BlendState = BlendState.Opaque,
                DepthStencilState = new DepthStencilState
                {
                    DepthTestEnabled = true, DepthWriteEnabled = true,
                    DepthCompareOp = CompareOp.Less
                },
                RasterizerState = new RasterizerState { CullMode = CullMode.None },
                ColorFormats = new[] { TextureFormat.RGBA8Unorm },
                DepthFormat = TextureFormat.Depth32Float,
                DebugName = "Ease+.PrePass"
            });
            created++;
        }
        catch (Exception ex) { Console.WriteLine($"[Ease+] ⚠ PrePass pipeline skipped: {ex.Message}"); }

        // Light accumulation: fullscreen, no depth write, additive
        try
        {
            _lightPassPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
            {
                VertexShader = MakeShader(ShaderStage.Vertex, "easeplus_vs_fullscreen"),
                FragmentShader = MakeShader(ShaderStage.Fragment, "easeplus_fs_lighting"),
                VertexLayout = EmptyVertexLayout(),
                Topology = PrimitiveTopology.TriangleList,
                BlendState = BlendState.Opaque,
                DepthStencilState = new DepthStencilState
                {
                    DepthTestEnabled = false, DepthWriteEnabled = false
                },
                RasterizerState = new RasterizerState { CullMode = CullMode.None },
                ColorFormats = new[] { TextureFormat.RGBA8Unorm },
                DepthFormat = TextureFormat.Depth32Float,
                DebugName = "Ease+.LightPass"
            });
            created++;
        }
        catch (Exception ex) { Console.WriteLine($"[Ease+] ⚠ LightPass pipeline skipped: {ex.Message}"); }

        // Material combine: depth-test=LessEqual (allows re-rendering with proper depth testing)
        try
        {
            _materialPassPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
            {
                VertexShader = MakeShader(ShaderStage.Vertex, "easeplus_vs_material"),
                FragmentShader = MakeShader(ShaderStage.Fragment, "easeplus_fs_material"),
                VertexLayout = StandardVertexLayout(),
                Topology = PrimitiveTopology.TriangleList,
                BlendState = BlendState.Opaque,
                DepthStencilState = new DepthStencilState
                {
                    DepthTestEnabled = true,
                    DepthWriteEnabled = false,
                    DepthCompareOp = CompareOp.LessOrEqual
                },
                RasterizerState = new RasterizerState { CullMode = CullMode.None },
                ColorFormats = new[] { TextureFormat.RGBA8Unorm },
                DepthFormat = TextureFormat.Depth32Float,
                DebugName = "Ease+.MaterialPass"
            });
            created++;
        }
        catch (Exception ex) { Console.WriteLine($"[Ease+] ⚠ MaterialPass pipeline skipped: {ex.Message}"); }

        // Add Sky and Grid pipelines (simple fullscreen passes)
        try
        {
            _skyPassPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
            {
                VertexShader = MakeShader(ShaderStage.Vertex, "vs_sky"),
                FragmentShader = MakeShader(ShaderStage.Fragment, "fs_sky"),
                VertexLayout = EmptyVertexLayout(),
                Topology = PrimitiveTopology.TriangleList,
                BlendState = BlendState.Opaque,
                DepthStencilState = new DepthStencilState
                {
                    DepthTestEnabled = false,
                    DepthWriteEnabled = false
                },
                RasterizerState = new RasterizerState { CullMode = CullMode.None },
                ColorFormats = new[] { TextureFormat.RGBA8Unorm },
                DepthFormat = null,
                DebugName = "Ease+.SkyPass"
            });
            created++;
        }
        catch (Exception ex) { Console.WriteLine($"[Ease+] ⚠ SkyPass pipeline skipped: {ex.Message}"); }

        try
        {
            _gridPassPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
            {
                VertexShader = MakeShader(ShaderStage.Vertex, "easeplus_vs_grid"),
                FragmentShader = MakeShader(ShaderStage.Fragment, "easeplus_fs_grid"),
                VertexLayout = EmptyVertexLayout(),
                Topology = PrimitiveTopology.TriangleList,
                BlendState = BlendState.AlphaBlend,
                DepthStencilState = new DepthStencilState
                {
                    DepthTestEnabled = true,
                    DepthWriteEnabled = false,
                    DepthCompareOp = CompareOp.Less
                },
                RasterizerState = new RasterizerState { CullMode = CullMode.None },
                ColorFormats = new[] { TextureFormat.RGBA8Unorm },
                DepthFormat = TextureFormat.Depth32Float,
                DebugName = "Ease+.GridPass"
            });
            created++;
        }
        catch (Exception ex) { Console.WriteLine($"[Ease+] ⚠ GridPass pipeline skipped: {ex.Message}"); }

        _hasDeferredPipelines = _prePassPipeline != null && _lightPassPipeline != null && _materialPassPipeline != null;
        
        Console.WriteLine($"[Ease+] ✓ {created}/5 deferred pipelines created");
        Console.WriteLine(_hasDeferredPipelines
            ? "[Ease+] Deferred path active: PrePass + LightPass + MaterialPass ready"
            : "[Ease+] Deferred path unavailable; using ViewportRenderer fallback");
    }

     private ShaderDesc MakeShader(ShaderStage stage, string entryPoint)
     {
         // Same pattern as ViewportRenderer — load .cso for DX11, .metallib for Metal
         byte[] bytecode = Array.Empty<byte>();
         string fileName = entryPoint + ".cso";
         
         if (_device.Backend == RHIBackend.Metal)
         {
             if (entryPoint.Contains("prepass")) fileName = "easeplus_prepass.metallib";
             else if (entryPoint.Contains("lighting") || entryPoint.Contains("fullscreen")) fileName = "easeplus_lighting.metallib";
             else if (entryPoint.Contains("material")) fileName = "easeplus_material.metallib";
             else if (entryPoint.Contains("grid")) fileName = "easeplus_grid.metallib";
             else if (entryPoint.Contains("postfx")) fileName = "easeplus_postfx.metallib";
         }
 
         string[] searchPaths = new[]
         {
             System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", fileName),
             System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Editor", "Shaders", fileName),
             System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "BlueSkyEngine", "Rendering", "EasePlus", "Shaders", fileName),
             // Look for compiled shaders in the compiled directories (using original entryPoint)
             System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "BlueSkyEngine", "Rendering", "Shaders", "compiled", GetBackendDirectory(_device.Backend), entryPoint + ".bin"),
             System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Rendering", "Shaders", "compiled", GetBackendDirectory(_device.Backend), entryPoint + ".bin"),
         };
 
         string? found = Array.Find(searchPaths, System.IO.File.Exists);
         if (found != null)
         {
             bytecode = System.IO.File.ReadAllBytes(found);
             Console.WriteLine($"[Ease+] Loaded shader: {found} ({bytecode.Length}B)");
         }
 
         return new ShaderDesc { Stage = stage, EntryPoint = entryPoint, Bytecode = bytecode };
     }

    private static VertexLayoutDesc StandardVertexLayout() => new()
    {
        Attributes = new[]
        {
            new VertexAttribute { Location = 0, Binding = 0, Format = TextureFormat.RGB32Float, Offset = 0 },
            new VertexAttribute { Location = 1, Binding = 0, Format = TextureFormat.RGB32Float, Offset = 12 },
            new VertexAttribute { Location = 2, Binding = 0, Format = TextureFormat.RG32Float, Offset = 24 },
        },
        Bindings = new[] { new VertexBinding { Binding = 0, Stride = 32, PerInstance = false } }
    };

    private static VertexLayoutDesc EmptyVertexLayout() => new()
    {
        Attributes = Array.Empty<VertexAttribute>(),
        Bindings = Array.Empty<VertexBinding>()
    };

     private float EstimateTotalMemory() =>
         (_screenWidth * _screenHeight * 4 * 3 + // GBuffer + PostFX
          _memory.HalfWidth * _memory.HalfHeight * 12) / (1024f * 1024f); // LightBuffer + LightDepth

     private string GetBackendDirectory(RHIBackend backend)
     {
         return backend switch
         {
             RHIBackend.Vulkan => "vulkan",
             RHIBackend.DirectX11 => "dx11",
             RHIBackend.Metal => "metal",
             RHIBackend.OpenGL => "glsl",
             _ => "unknown"
         };
     }

    // ── IRenderer Legacy Interface ───────────────────────────────────────

    public void Clear(float r, float g, float b, float a = 1.0f) { }
    public void ClearDepth() { }
    public void SetViewport(int x, int y, int w, int h)
    {
        _requestedViewportWidth = (uint)Math.Max(1, w);
        _requestedViewportHeight = (uint)Math.Max(1, h);
        _hasRequestedViewportSize = true;
    }
    public void SetScissor(int x, int y, int w, int h) { }
    public void DrawLine(Core.Math.Vector3 s, Core.Math.Vector3 e, Core.Math.Vector3 c, Core.Math.Matrix4x4 v, Core.Math.Matrix4x4 p) { }
    public void DrawGrid(Core.Math.Matrix4x4 v, Core.Math.Matrix4x4 p, int size, float spacing) { }
    public void RenderSky(float time, Core.Math.Vector3 sunDir, Core.Math.Quaternion camRot, float aspect, float tanFov) { }

    public int CreateVertexBuffer(float[] v) { int id = _nextResourceId++; return id; }
    public int CreateIndexBuffer(uint[] i) { int id = _nextResourceId++; return id; }
    public int CreateShader(string vs, string fs) { return _nextResourceId++; }
    public int CreateTexture(int w, int h, byte[] d, bool srgb = true) { return _nextResourceId++; }
    public int CreateFramebuffer(int w, int h) { return _nextResourceId++; }
    public int GetFramebufferTexture(int fbo) { return 0; }
    public void SetShader(int id) { }
    public void SetTexture(int stage, int id) { }
    public void SetRenderTarget(int fbo) { }
    public void SetUniform(string n, Core.Math.Matrix4x4 m) { }
    public void SetUniform(string n, Core.Math.Vector3 v) { }
    public void SetUniform(string n, float v) { }
    public void DeleteResource(ResourceType t, int id) { }
    public int CreateMesh(float[] v, uint[] i)
    {
        int id = _nextResourceId++;
        
        // Create vertex buffer
        var vb = _device.CreateBuffer(new BufferDesc
        {
            Size = (uint)(v.Length * sizeof(float)),
            Usage = BufferUsage.Vertex,
            DebugName = $"Mesh{id}_VB"
        });
        _device.UpdateBuffer(vb, MemoryMarshal.AsBytes(v.AsSpan()));
        _vertexBuffers[id] = vb;
        
        // Create index buffer
        var ib = _device.CreateBuffer(new BufferDesc
        {
            Size = (uint)(i.Length * sizeof(uint)),
            Usage = BufferUsage.Index,
            DebugName = $"Mesh{id}_IB"
        });
        _device.UpdateBuffer(ib, MemoryMarshal.AsBytes(i.AsSpan()));
        _indexBuffers[id] = ib;
        
        // Track index count
        _indexCounts[id] = (uint)i.Length;
        
        return id;
    }
    public void UpdateMesh(int id, float[] v, uint[] i) { }
    public void DeleteMesh(int id) { }

    public void Dispose()
    {
        _polarisRayTracer?.Dispose();
        _memory.Dispose();
        _prePassPipeline?.Dispose();
        _lightPassPipeline?.Dispose();
        _materialPassPipeline?.Dispose();
        _postFXPipeline?.Dispose();
        _skyPassPipeline?.Dispose();
        _gridPassPipeline?.Dispose();
        _deferredInstanceBuffer?.Dispose();
        
        // Dispose test geometry
        _testCube.VertexBuffer?.Dispose();
        _testCube.IndexBuffer?.Dispose();
        _testSphere.VertexBuffer?.Dispose();
        _testSphere.IndexBuffer?.Dispose();
        _testPlane.VertexBuffer?.Dispose();
        _testPlane.IndexBuffer?.Dispose();
        
        Console.WriteLine("[Ease+] Renderer disposed");
    }
    
}
