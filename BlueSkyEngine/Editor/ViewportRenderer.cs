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
        public System.Numerics.Vector4   WindParams;   // x: speed, y: strength, z: frequency, w: unused
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
    // CRITICAL: Metal pads float3 to 16 bytes inside structs (same as float4).
    // C# Vector3 is 12 bytes. To avoid the 4-byte gap, we pack Metallic into
    // the w component of Albedo as a Vector4. The shader reads albedo.xyz and albedo.w.
    [StructLayout(LayoutKind.Sequential)]
    private struct MaterialData
    {
        public System.Numerics.Vector4 AlbedoAndMetallic; // xyz=albedo, w=metallic
        public float Roughness;
        public float Ao;
        public float Emission;
        public float Subsurface;
        public int UseAlbedoTex;
        public int UseNormalTex;
        public int UseRMATex;
        public int BlendMode; // 0=Opaque, 1=AlphaTest, 2=AlphaBlend
        public int UseOpacityTex; // Separate opacity/alpha map (map_d)
        private int _pad0;
        private int _pad1;
        private int _pad2;
    }
    
    // ── Gizmo uniforms (must match Metal GizmoUniforms) ──────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct GizmoUniforms
    {
        public System.Numerics.Matrix4x4 ViewProj;
        public System.Numerics.Matrix4x4 Model;
        public System.Numerics.Vector4   Color;
        public float GizmoType; // 0=translate, 1=rotate, 2=scale
        public float AxisId;    // 0=X, 1=Y, 2=Z, 3=center
        public float IsHovered; // 1.0 when hovered
        private float _pad;
    }
    
    // ── Gizmo Mode enum ─────────────────────────────────────────────────────────────
    public enum GizmoMode { Translate, Rotate, Scale }
    
    /// <summary>Current gizmo mode (set by editor toolbar W/E/R keys).</summary>
    public GizmoMode CurrentGizmoMode { get; set; } = GizmoMode.Translate;
    
    /// <summary>Entity ID the gizmo should draw at. 0 = none.</summary>
    public uint SelectedEntityId { get; set; }
    
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
        public ulong LastUsedFrame; // For LRU cache eviction
        
        // Cached material slot paths from asset metadata (covers all slots, not just 0-7)
        public Dictionary<int, string> MaterialSlotPaths = new();
        
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
    private          IRHIPipeline? _transparentMeshPipeline;
    private          IRHIPipeline? _doubleSidedMeshPipeline;
    private          IRHIPipeline? _wireframePipeline;
    private          IRHIPipeline? _shadowPipeline;
    private          IRHITexture?  _shadowMap;
    private          IRHIBuffer?   _uniformBuffer;
    private          IRHIBuffer?   _entityUniformBuffer;
    private          IRHIBuffer?   _instanceBuffer;
    private          const int     MaxInstancesPerBatch = 50000;
    
    // Horizon Lighting buffers
    private IRHIBuffer? _horizonViewUniformBuffer; // Separate buffer for Horizon shader
    private IRHIBuffer? _lightBuffer;
    private IRHIBuffer? _lightCountBuffer;
    private IRHIBuffer? _lightSettingsBuffer;
    private IRHIBuffer? _materialBuffer;
    
    private readonly Dictionary<string, MeshGPUData> _meshCache = new();
    private readonly Dictionary<string, BlueSky.Core.Assets.MaterialAsset?> _materialCache = new();
    
    // ── Texture cache for material textures ─────────────────────────────
    private readonly Dictionary<string, IRHITexture?> _textureCache = new();
    private IRHITexture? _defaultWhiteTexture;
    private IRHITexture? _defaultNormalTexture;
    private IRHITexture? _defaultRmaTexture;
    private IRHITexture? _defaultWhiteOpacityTexture;

    private ulong _frameCount = 0; // For LRU eviction

    // ── Gizmo resources ─────────────────────────────────────────────────
    private          IRHIPipeline? _gizmoPipeline;
    private          IRHIBuffer?[] _gizmoUniformBuffers = new IRHIBuffer?[4];
    private          IRHIBuffer?   _gizmoArrowVB;
    private          IRHIBuffer?   _gizmoArrowIB;
    private          int           _gizmoArrowIndexCount;
    private          IRHIBuffer?   _gizmoCubeVB;
    private          IRHIBuffer?   _gizmoCubeIB;
    private          int           _gizmoCubeIndexCount;
    private          IRHIBuffer?   _gizmoRingVB;
    private          IRHIBuffer?   _gizmoRingIB;
    private          int           _gizmoRingIndexCount;
    public          int           HoveredAxis = -1; // 0=X, 1=Y, 2=Z, 3=Center
    private          bool          _gizmoGeometryCreated;

    private float _elapsedTime;
    private bool  _disposed;

    public ViewportRenderer(IRHIDevice device, World world)
    {
        _device = device;
        _world = world;
        CreatePipelines();
        CreateBuffers();
        CreateDefaultTextures();
        CreateGizmoGeometry();
        
        // Clean up any corrupted mesh entities on startup
        CleanupCorruptedMeshes();
    }
    
    /// <summary>
    /// Detect and remove entities with corrupted mesh data (from old import format).
    /// </summary>
    private void CleanupCorruptedMeshes()
    {
        var query = _world.CreateQuery().All<TransformComponent>().All<BlueSky.Core.ECS.Builtin.StaticMeshComponent>().Build();
        var chunks = _world.GetQueryChunks(query);
        var entitiesToRemove = new List<Entity>();
        
        foreach (var chunk in chunks)
        {
            int meshIndex = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.StaticMeshComponent));
            var entities = chunk.GetEntities();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                var staticMesh = chunk.GetComponent<BlueSky.Core.ECS.Builtin.StaticMeshComponent>(i, meshIndex);
                
                if (string.IsNullOrEmpty(staticMesh.MeshAssetId)) continue;
                
                try
                {
                    var asset = BlueSky.Core.Assets.BlueAsset.Load(staticMesh.MeshAssetId);
                    if (asset != null && asset.PayloadData != null && asset.PayloadData.Length > 0)
                    {
                        using var ms = new System.IO.MemoryStream(asset.PayloadData);
                        using var reader = new System.IO.BinaryReader(ms);
                        
                        int vLen = reader.ReadInt32();
                        // Sanity check: vertex buffer should be reasonable size (< 100MB)
                        if (vLen < 0 || vLen > 100_000_000)
                        {
                            Console.WriteLine($"[ViewportRenderer] Detected corrupted mesh entity: {staticMesh.MeshAssetId}");
                            Console.WriteLine($"[ViewportRenderer] → Removing entity with invalid vertex buffer size: {vLen}");
                            entitiesToRemove.Add(entities[i]);
                        }
                    }
                }
                catch
                {
                    // Silently skip on error
                }
            }
        }
        
        // Remove corrupted entities
        foreach (var entity in entitiesToRemove)
        {
            _world.DestroyEntity(entity);
            Console.WriteLine($"[ViewportRenderer] Removed corrupted entity {entity.Id}");
        }
        
        if (entitiesToRemove.Count > 0)
        {
            Console.WriteLine($"[ViewportRenderer] Cleanup complete: removed {entitiesToRemove.Count} corrupted mesh entities");
        }
    }

    /// <summary>
    /// Invalidate all cached materials (e.g. on project reload).
    /// </summary>
    public void InvalidateAllMaterials()
    {
        _materialCache.Clear();
    }

    /// <summary>
    /// Create 1x1 default textures for fallback when no texture is assigned.
    /// </summary>
    private void CreateDefaultTextures()
    {
        // 1x1 white pixel (albedo fallback)
        _defaultWhiteTexture = _device.CreateTexture(new TextureDesc
        {
            Width = 1, Height = 1, Depth = 1, MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled,
            DebugName = "Default.White"
        });
        _device.UploadTexture(_defaultWhiteTexture, new byte[] { 255, 255, 255, 255 });
        
        // 1x1 default normal (pointing up — 128,128,255)
        _defaultNormalTexture = _device.CreateTexture(new TextureDesc
        {
            Width = 1, Height = 1, Depth = 1, MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled,
            DebugName = "Default.Normal"
        });
        _device.UploadTexture(_defaultNormalTexture, new byte[] { 128, 128, 255, 255 });
        
        // 1x1 default RMA (Roughness=0.5, Metallic=0.0, AO=1.0 — 128,0,255)
        _defaultRmaTexture = _device.CreateTexture(new TextureDesc
        {
            Width = 1, Height = 1, Depth = 1, MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled,
            DebugName = "Default.RMA"
        });
        _device.UploadTexture(_defaultRmaTexture, new byte[] { 128, 0, 255, 255 });
        
        // 1x1 white pixel (opacity fallback — fully opaque)
        _defaultWhiteOpacityTexture = _device.CreateTexture(new TextureDesc
        {
            Width = 1, Height = 1, Depth = 1, MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled,
            DebugName = "Default.Opacity"
        });
        _device.UploadTexture(_defaultWhiteOpacityTexture, new byte[] { 255, 255, 255, 255 });
    }

    /// <summary>
    /// Demand-load a texture from a .blueskyasset or raw image file.
    /// Results are cached by path. A missing file is NOT permanently cached —
    /// it will be retried next time (handles re-import without restart).
    /// </summary>
    private IRHITexture? LoadCachedTexture(string path)
    {
        if (string.IsNullOrEmpty(path)) 
        {
            Console.WriteLine($"[Viewport] Texture path is null or empty");
            return null;
        }

        // Return cached hit (including a known-good null for truly missing files
        // that were already logged — but we only cache null after a successful
        // existence check, so re-import will clear the entry).
        if (_textureCache.TryGetValue(path, out var cached)) 
        {
            Console.WriteLine($"[Viewport] Texture cache hit: {path} -> {(cached != null ? "LOADED" : "NULL")}");
            return cached;
        }

        if (!System.IO.File.Exists(path))
        {
            // Do NOT cache null here — the file may appear after a re-import.
            Console.WriteLine($"[Viewport] Texture file not found: {path}");
            return null;
        }

        try
        {
            Console.WriteLine($"[Viewport] Loading texture: {path}");
            IRHITexture? tex = null;

            if (path.EndsWith(".blueskyasset", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[Viewport] Loading BlueAsset texture: {path}");
                tex = LoadTextureFromBlueAsset(path);
            }
            else
            {
                Console.WriteLine($"[Viewport] Loading raw texture file: {path}");
                tex = LoadTextureFromRawFile(path);
            }

            if (tex != null)
            {
                Console.WriteLine($"[Viewport] ✓ Texture loaded successfully: {path}");
            }
            else
            {
                Console.WriteLine($"[Viewport] ✗ Texture loading failed: {path}");
            }

            // Cache the result (null = file exists but failed to decode — don't retry)
            _textureCache[path] = tex;
            return tex;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Viewport] Exception loading texture '{path}': {ex.Message}");
            Console.WriteLine($"[Viewport] Stack trace: {ex.StackTrace}");
            return null; // Not cached — will retry next frame
        }
    }

    private IRHITexture? LoadTextureFromBlueAsset(string path)
    {
        var asset = BlueSky.Core.Assets.BlueAsset.Load(path);
        if (asset == null || !asset.HasPayload)
        {
            Console.WriteLine($"[Viewport] BlueAsset texture has no payload: {path}");
            return null;
        }

        using var ms     = new System.IO.MemoryStream(asset.PayloadData);
        using var reader = new System.IO.BinaryReader(ms);

        int width      = reader.ReadInt32();
        int height     = reader.ReadInt32();
        int components = reader.ReadInt32(); // stored but not used — always RGBA8
        int dataLen    = reader.ReadInt32();

        if (width <= 0 || height <= 0 || dataLen <= 0 || dataLen > asset.PayloadData.Length)
        {
            Console.WriteLine($"[Viewport] Corrupt texture payload in: {path}");
            return null;
        }

        byte[] data = reader.ReadBytes(dataLen);

        var tex = _device.CreateTexture(new TextureDesc
        {
            Width = (uint)width, Height = (uint)height,
            Depth = 1, MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage  = TextureUsage.Sampled,
            DebugName = asset.AssetName
        });
        _device.UploadTexture(tex, data);
        Console.WriteLine($"[Viewport] ✓ Texture: {asset.AssetName} ({width}×{height})");
        return tex;
    }

    private IRHITexture? LoadTextureFromRawFile(string path)
    {
        // Raw image files (png/jpg/etc.) — no vertical flip needed.
        // Modern DCC tools export OBJ/FBX with standard UV convention (V=0 at top).
        StbImageSharp.StbImage.stbi_set_flip_vertically_on_load(0);
        using var stream = System.IO.File.OpenRead(path);
        var image = StbImageSharp.ImageResult.FromStream(
            stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);

        if (image == null)
        {
            Console.WriteLine($"[Viewport] Failed to decode raw texture: {path}");
            return null;
        }

        var tex = _device.CreateTexture(new TextureDesc
        {
            Width = (uint)image.Width, Height = (uint)image.Height,
            Depth = 1, MipLevels = 1, ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage  = TextureUsage.Sampled,
            DebugName = System.IO.Path.GetFileNameWithoutExtension(path)
        });
        _device.UploadTexture(tex, image.Data);
        Console.WriteLine($"[Viewport] ✓ Raw texture: {System.IO.Path.GetFileName(path)} ({image.Width}×{image.Height})");
        return tex;
    }

    /// <summary>
    /// Demand-load a MaterialAsset from a .blueskyasset file.
    /// Results are cached by path. Missing files are NOT permanently cached.
    /// Does NOT mutate the loaded asset (no metallic hotfix — import correctly instead).
    /// </summary>
    private BlueSky.Core.Assets.MaterialAsset? LoadCachedMaterial(string path)
    {
        if (string.IsNullOrEmpty(path)) 
        {
            Console.WriteLine($"[Viewport] Material path is null or empty");
            return null;
        }
        
        if (_materialCache.TryGetValue(path, out var cached)) 
        {
            Console.WriteLine($"[Viewport] Material cache hit: {path} -> {(cached != null ? "LOADED" : "NULL")}");
            return cached;
        }

        if (!System.IO.File.Exists(path))
        {
            Console.WriteLine($"[Viewport] Material file not found: {path}");
            // Not cached — will retry after re-import
            return null;
        }

        try
        {
            Console.WriteLine($"[Viewport] Loading material asset: {path}");
            var mat = BlueSky.Core.Assets.MaterialAsset.Load(path);
            
            if (mat != null)
            {
                Console.WriteLine($"[Viewport] ✓ Material loaded: {mat.MaterialName}");
                Console.WriteLine($"[Viewport]   - Albedo texture: {mat.AlbedoTexturePath}");
                Console.WriteLine($"[Viewport]   - Normal texture: {mat.NormalTexturePath}");
                Console.WriteLine($"[Viewport]   - RMA texture: {mat.RMATexturePath}");
                Console.WriteLine($"[Viewport]   - Albedo color: ({mat.Albedo.X:F2}, {mat.Albedo.Y:F2}, {mat.Albedo.Z:F2})");
                Console.WriteLine($"[Viewport]   - Metallic: {mat.Metallic:F2}, Roughness: {mat.Roughness:F2}");
            }
            else
            {
                Console.WriteLine($"[Viewport] ✗ MaterialAsset.Load returned null for: {path}");
            }
            
            _materialCache[path] = mat; // cache even if null (decode failure)
            return mat;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Viewport] Exception loading material '{path}': {ex.Message}");
            Console.WriteLine($"[Viewport] Stack trace: {ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Evict a material from the cache so the next render picks up the saved version.
    /// Call this from the MaterialEditor after saving.
    /// </summary>
    public void InvalidateMaterial(string path)
    {
        _materialCache.Remove(path);
    }

    /// <summary>
    /// Evict a texture from the cache and dispose the GPU resource.
    /// Call this after re-importing a texture asset.
    /// </summary>
    public void InvalidateTexture(string path)
    {
        if (_textureCache.TryGetValue(path, out var tex))
        {
            tex?.Dispose();
            _textureCache.Remove(path);
        }
    }

    /// <summary>
    /// Evict all cached materials and textures for a given mesh asset directory.
    /// Call this after re-importing a mesh (which regenerates all its materials/textures).
    /// </summary>
    public void InvalidateAssetDirectory(string assetDir)
    {
        // Evict materials
        var matKeys = new System.Collections.Generic.List<string>(
            System.Linq.Enumerable.Where(_materialCache.Keys,
                k => k.StartsWith(assetDir, StringComparison.OrdinalIgnoreCase)));
        foreach (var k in matKeys) _materialCache.Remove(k);

        // Evict textures
        var texKeys = new System.Collections.Generic.List<string>(
            System.Linq.Enumerable.Where(_textureCache.Keys,
                k => k.StartsWith(assetDir, StringComparison.OrdinalIgnoreCase)));
        foreach (var k in texKeys)
        {
            _textureCache[k]?.Dispose();
            _textureCache.Remove(k);
        }

        // Evict mesh GPU data
        var meshKeys = new System.Collections.Generic.List<string>(
            System.Linq.Enumerable.Where(_meshCache.Keys,
                k => k.StartsWith(assetDir, StringComparison.OrdinalIgnoreCase)));
        foreach (var k in meshKeys)
        {
            _meshCache[k].Dispose();
            _meshCache.Remove(k);
        }

        Console.WriteLine($"[Viewport] Invalidated cache for: {assetDir}");
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
        
        var shadowItems = new System.Collections.Generic.List<(MeshGPUData GpuData, SubmeshInfo Submesh, TransformComponent Transform)>();

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

                foreach (var submesh in gpuData.Submeshes)
                {
                    if (submesh.IndexCount == 0) continue;
                    shadowItems.Add((gpuData, submesh, transform));
                }
            }
        }
        
        if (shadowItems.Count > 0)
        {
            var batches = shadowItems.GroupBy(i => new { i.GpuData, i.Submesh.IndexOffset });
            var instancesArray = new EntityUniforms[MaxInstancesPerBatch];
            
            cmd.SetUniformBuffer(_instanceBuffer!, 30);
            
            foreach (var batch in batches)
            {
                var batchItems = batch.ToList();
                var firstItem = batchItems[0];
                
                cmd.SetVertexBuffer(firstItem.GpuData.VertexBuffer!, 0);
                cmd.SetIndexBuffer(firstItem.GpuData.IndexBuffer!, IndexType.UInt32);
                
                for (int i = 0; i < batchItems.Count; i += MaxInstancesPerBatch)
                {
                    int count = Math.Min(MaxInstancesPerBatch, batchItems.Count - i);
                    
                    for (int j = 0; j < count; j++)
                    {
                        var item = batchItems[i + j];
                        instancesArray[j] = new EntityUniforms
                        {
                            Model = ToSystemMatrix4x4(item.Transform.WorldMatrix),
                            Color = System.Numerics.Vector4.One
                        };
                    }
                    
                    var span = new ReadOnlySpan<EntityUniforms>(instancesArray, 0, count);
                    _device.UpdateBuffer(_instanceBuffer!, MemoryMarshal.AsBytes(span));
                    
                    cmd.DrawIndexed((uint)firstItem.Submesh.IndexCount, (uint)count, (uint)firstItem.Submesh.IndexOffset, 0, 0);
                }
            }
        }

        cmd.EndRenderPass();
    }

    private static readonly System.Numerics.Vector3 DefaultAlbedo = new(0.85f, 0.85f, 0.85f); // Clean neutral grey default
    
    public void Render(IRHICommandBuffer cmd, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 proj,
        System.Numerics.Vector3 cameraPos, int viewportX, int viewportY, int viewportW, int viewportH, float deltaTime)
    {
        _frameCount++;
        _elapsedTime += deltaTime;
        
        // DEBUG: Log viewport dimensions every 60 frames
        if (_frameCount % 60 == 0)
        {
            Console.WriteLine($"[ViewportRenderer] Frame {_frameCount}: viewport=({viewportX},{viewportY}) size={viewportW}x{viewportH}");
            Console.WriteLine($"[ViewportRenderer] Pipelines: sky={_skyPipeline != null}, grid={_gridPipeline != null}, mesh={_meshPipeline != null}");
        }

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
            SunDirection = BlueSky.Core.WorldEnvironment.GlobalEnvironment.SunDirection,
            WindParams   = BlueSky.Core.WorldEnvironment.GlobalEnvironment.WindParams
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
        Span<LightData> lightDataArray = stackalloc LightData[64];
        lightDataArray[0] = new LightData
        {
            Position = System.Numerics.Vector3.Zero,
            Range = 1000f,
            Direction = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.5f, 0.7f, 0.3f)), // Higher sun angle
            Intensity = 4.5f, // Increased for better visibility
            Color = new System.Numerics.Vector3(1.0f, 0.98f, 0.92f), // Warmer, more natural sunlight
            Type = 0, // Directional
            InnerAngle = 0f,
            OuterAngle = 0f,
            Attenuation = 1f,
            CastShadows = 1,
            Volumetric = 0,
        };
        
        _device.UpdateBuffer(_lightBuffer!, MemoryMarshal.AsBytes(lightDataArray));
        
        // Update light count
        int lightCount = 1;
        var lightCountSpan = MemoryMarshal.CreateSpan(ref lightCount, 1);
        _device.UpdateBuffer(_lightCountBuffer!, MemoryMarshal.AsBytes(lightCountSpan));
        
        // Update lighting settings with improved ambient
        var lightSettings = new LightingSettings
        {
            Quality = 2, // High
            MaxLights = 64,
            EnableIBL = 1, // Enable IBL for better ambient
            EnableVolumetrics = 0,
            EnableContactShadows = 1,
            Exposure = 1.2f, // Slightly brighter exposure
            AmbientColor = new System.Numerics.Vector3(0.15f, 0.18f, 0.25f), // Cooler, more realistic sky ambient
        };
        var lightSettingsSpan = MemoryMarshal.CreateSpan(ref lightSettings, 1);
        _device.UpdateBuffer(_lightSettingsBuffer!, MemoryMarshal.AsBytes(lightSettingsSpan));
        
        // Default material data (will be overridden per-submesh in RenderEntities)
        var material = new MaterialData
        {
            AlbedoAndMetallic = new System.Numerics.Vector4(DefaultAlbedo, 0.0f),
            Roughness = 0.5f,
            Ao = 1.0f,
            Emission = 0.0f,
            Subsurface = 0.0f,
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

        // ── 2. Entities (BEFORE grid for correct transparency) ───────────
        RenderEntities(cmd, view, proj, cameraPos);

        // ── 3. Grid (AFTER entities for proper alpha blending) ───────────
        cmd.SetPipeline(_gridPipeline!);
        cmd.SetUniformBuffer(_uniformBuffer!, 10);
        cmd.Draw(6); // fullscreen quad (2 tris)
        
        // ── 4. Editor Gizmos (LAST — always on top) ──────────────────────
        RenderGizmos(cmd, viewProj, cameraPos);
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
            ColorFormats    = new[] { TextureFormat.RGBA8Unorm },
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
                DepthWriteEnabled = false,  // CRITICAL: Don't write depth for transparent grid!
                DepthCompareOp    = CompareOp.Less,
            },
            RasterizerState = new RasterizerState { CullMode = CullMode.None },
            ColorFormats    = new[] { TextureFormat.RGBA8Unorm },
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
            ColorFormats    = new[] { TextureFormat.RGBA8Unorm },
            DepthFormat     = TextureFormat.Depth32Float,
            DebugName       = "ViewportMesh_HorizonLighting",
        });

        // Transparent mesh pipeline — alpha blend, no depth write
        _transparentMeshPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
        {
            VertexShader   = MakeShader(ShaderStage.Vertex, "vs_mesh"),
            FragmentShader = MakeShader(ShaderStage.Fragment, "fs_mesh"),
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
                DepthTestEnabled  = true,
                DepthWriteEnabled = false,
                DepthCompareOp    = CompareOp.Less,
            },
            RasterizerState = new RasterizerState { CullMode = CullMode.None },
            ColorFormats    = new[] { TextureFormat.RGBA8Unorm },
            DepthFormat     = TextureFormat.Depth32Float,
            DebugName       = "ViewportMesh_Transparent",
        });

        // Double-sided mesh pipeline (optional for opaque parts that need it, though current _meshPipeline is none anyway)
        // For now, let's keep _meshPipeline as None and just use it for everything. 
        // But if we want to be correct, we should have Opaque-BackfaceCull too.
        _doubleSidedMeshPipeline = _meshPipeline;

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
            ColorFormats    = new[] { TextureFormat.RGBA8Unorm },
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
        
        // Gizmo pipeline — alpha blend, depth test but no depth write (renders on top)
        // Wrapped in try-catch: gizmo is optional; if the metallib is stale and doesn't
        // contain vs_gizmo/fs_gizmo the rest of the renderer still works fine.
        try
        {
            _gizmoPipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDesc
            {
                VertexShader   = MakeShader(ShaderStage.Vertex, "vs_gizmo"),
                FragmentShader = MakeShader(ShaderStage.Fragment, "fs_gizmo"),
                VertexLayout   = new VertexLayoutDesc
                {
                    Attributes = new[]
                    {
                        new VertexAttribute { Location = 0, Binding = 0, Format = TextureFormat.RGB32Float, Offset = 0 },   // Position
                        new VertexAttribute { Location = 1, Binding = 0, Format = TextureFormat.RGB32Float, Offset = 12 },  // Normal
                        new VertexAttribute { Location = 2, Binding = 0, Format = TextureFormat.RG32Float, Offset = 24 },   // UV
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
                    DepthTestEnabled  = true,
                    DepthWriteEnabled = false,
                    DepthCompareOp    = CompareOp.Always, // Always draw on top!
                },
                RasterizerState = new RasterizerState { CullMode = CullMode.Back },
                ColorFormats    = new[] { TextureFormat.RGBA8Unorm },
                DepthFormat     = TextureFormat.Depth32Float,
                DebugName       = "ViewportGizmo",
            });
            Console.WriteLine("[ViewportRenderer] Gizmo pipeline created successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ViewportRenderer] Gizmo pipeline creation failed (non-fatal): {ex.Message}");
            Console.WriteLine("[ViewportRenderer] Gizmos will be disabled. Recompile shaders to enable.");
            _gizmoPipeline = null;
        }
    }

    private ShaderDesc MakeShader(ShaderStage stage, string entryPoint)
    {
        byte[] bytecode = Array.Empty<byte>();

        if (_device.Backend == RHIBackend.Metal)
        {
            // For Metal, load the compiled .metallib
            string baseName = "viewport_3d";
            if (entryPoint.Contains("horizon")) baseName = "horizon_lighting";

            string[] searchPaths = new[]
            {
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", baseName + ".metallib"),
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Editor", "Shaders", baseName + ".metallib"),
                System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Editor", "Shaders", baseName + ".metallib"),
                System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "BlueSkyEngine", "Editor", "Shaders", baseName + ".metallib"),
            };

            string? found = System.Array.Find(searchPaths, System.IO.File.Exists);
            if (found != null)
            {
                bytecode = System.IO.File.ReadAllBytes(found);
                Console.WriteLine($"[ViewportRenderer] Loaded Metal library: {found} ({bytecode.Length} bytes)");
            }
            else
            {
                Console.WriteLine($"[ViewportRenderer] WARNING: {baseName}.metallib not found. Searched:");
                foreach (var p in searchPaths) Console.WriteLine($"  {p}");
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
        
        _instanceBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = (ulong)Marshal.SizeOf<EntityUniforms>() * MaxInstancesPerBatch,
            Usage      = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "Viewport.InstanceUB",
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
        // Bind Horizon Lighting buffers for all entities
        cmd.SetUniformBuffer(_uniformBuffer!, 10);
        cmd.SetUniformBuffer(_horizonViewUniformBuffer!, 12);
        cmd.SetUniformBuffer(_lightBuffer!, 13);
        cmd.SetUniformBuffer(_lightCountBuffer!, 14);
        cmd.SetUniformBuffer(_lightSettingsBuffer!, 15);

        // Extract frustum planes from ViewProj for CPU culling
        var viewProj = view * proj;
        Span<System.Numerics.Vector4> frustumPlanes = stackalloc System.Numerics.Vector4[6];
        ExtractFrustumPlanes(viewProj, frustumPlanes);

        var query = _world.CreateQuery()
            .All<TransformComponent>()
            .All<BlueSky.Core.ECS.Builtin.StaticMeshComponent>()
            .Build();

        // 1. Gather all submeshes to be rendered
        var opaqueItems = new List<RenderItem>();
        var transparentItems = new List<RenderItem>();

        var chunks = _world.GetQueryChunks(query);
        foreach (var chunk in chunks)
        {
            int transformIndex = chunk.GetComponentIndex(typeof(TransformComponent));
            int meshIndex = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.StaticMeshComponent));

            for (int i = 0; i < chunk.Count; i++)
            {
                var transform = chunk.GetComponent<TransformComponent>(i, transformIndex);
                var staticMesh = chunk.GetComponent<BlueSky.Core.ECS.Builtin.StaticMeshComponent>(i, meshIndex);
                
                // CPU frustum culling
                var posMatrix = transform.WorldMatrix;
                var entityPos = new System.Numerics.Vector3(posMatrix.M41, posMatrix.M42, posMatrix.M43);
                float maxScale = Math.Max(Math.Max(Math.Abs(transform.Scale.X), Math.Abs(transform.Scale.Y)), Math.Abs(transform.Scale.Z));
                float boundingRadius = maxScale * 5.0f;
                
                if (!IsSphereFrustumVisible(entityPos, boundingRadius, frustumPlanes))
                    continue;

                string assetId = staticMesh.MeshAssetId;
                if (string.IsNullOrEmpty(assetId)) continue;

                if (!_meshCache.TryGetValue(assetId, out var gpuData))
                {
                    // Demand-load the mesh
                    gpuData = LoadGpuMesh(assetId);
                }

                if (gpuData != null)
                {
                    gpuData.LastUsedFrame = _frameCount;
                    float distSq = System.Numerics.Vector3.DistanceSquared(cameraPos, entityPos);

                    foreach (var submesh in gpuData.Submeshes)
                    {
                        if (submesh.IndexCount == 0) continue;

                        string matPath = staticMesh.GetEffectiveMaterial(submesh.MaterialSlot);
                        if (string.IsNullOrEmpty(matPath))
                        {
                            if (!gpuData.MaterialSlotPaths.TryGetValue(submesh.MaterialSlot, out matPath!))
                            {
                                matPath = staticMesh.MaterialAssetId;
                            }
                        }

                        var material = LoadCachedMaterial(matPath);
                        var item = new RenderItem
                        {
                            Entity = chunk.GetEntities()[i],
                            Transform = transform,
                            StaticMesh = staticMesh,
                            GpuData = gpuData,
                            Submesh = submesh,
                            Material = material,
                            DistanceToCameraSq = distSq
                        };

                        if (material != null && material.BlendMode == BlueSky.Rendering.Materials.BlendMode.AlphaBlend)
                            transparentItems.Add(item);
                        else
                            opaqueItems.Add(item);
                    }
                }
            }
        }

        // 2. Draw Opaque Pass (Opaque + AlphaTest)
        cmd.SetPipeline(_meshPipeline!);
        DrawBatched(cmd, opaqueItems);

        // 3. Draw Transparent Pass (AlphaBlend) - Sorted Back-to-Front
        transparentItems.Sort((a, b) => b.DistanceToCameraSq.CompareTo(a.DistanceToCameraSq));
        cmd.SetPipeline(_transparentMeshPipeline!);
        
        // Cannot strictly batch transparent items if they need strict sorting and overlap differently, 
        // but for basic rendering we can still batch adjacent items with the same material.
        // For production, transparency sorting overrides batching, but for simple tests, we use the same batcher.
        DrawBatched(cmd, transparentItems);

        // Evict old meshes if cache is too large (VRAM optimization)
        if (_frameCount % 60 == 0 && _meshCache.Count > 64)
        {
            EvictOldMeshes(64);
        }
    }

    private MeshGPUData? LoadGpuMesh(string assetId)
    {
        try
        {
            var asset = BlueSky.Core.Assets.BlueAsset.Load(assetId);
            if (asset == null || asset.PayloadData == null) return null;

            using var ms = new System.IO.MemoryStream(asset.PayloadData);
            using var reader = new System.IO.BinaryReader(ms);
            
            int vLen = reader.ReadInt32();
            if (vLen < 0 || vLen > asset.PayloadData.Length) return null;
            
            byte[] vData = reader.ReadBytes(vLen);
            uint iLen = reader.ReadUInt32();
            if (iLen > asset.PayloadData.Length) return null;
            
            byte[] iData = reader.ReadBytes((int)iLen);

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

            var submeshes = new List<SubmeshInfo>();
            try
            {
                int submeshCount = reader.ReadInt32();
                for (int s = 0; s < submeshCount; s++)
                {
                    submeshes.Add(new SubmeshInfo
                    {
                        IndexOffset = reader.ReadInt32(),
                        IndexCount = reader.ReadInt32(),
                        MaterialSlot = reader.ReadInt32()
                    });
                }
            }
            catch
            {
                submeshes.Add(new SubmeshInfo { IndexOffset = 0, IndexCount = (int)(iLen / 4), MaterialSlot = 0 });
            }

            var gpuData = new MeshGPUData 
            { 
                VertexBuffer = vb, 
                IndexBuffer = ib, 
                IndexCount = (int)(iLen / 4),
                Submeshes = submeshes
            };
            
            var fullHeader = BlueSky.Core.Assets.BlueAsset.LoadHeader(assetId);
            if (fullHeader != null)
            {
                // Scan ALL material slots — DO NOT break on gaps!
                // GLTF material indices can be sparse (e.g., slots 0,1,5,12,46)
                // so we must scan the full range, not stop at the first missing one.
                int maxSlotToScan = 256;
                if (fullHeader.Metadata.TryGetValue("materialSlotCount", out var slotCountStr) &&
                    int.TryParse(slotCountStr, out int declaredCount))
                {
                    maxSlotToScan = declaredCount + 1; // +1 for safety
                }
                
                for (int s = 0; s < maxSlotToScan; s++)
                {
                    if (fullHeader.Metadata.TryGetValue($"materialSlot{s}", out var slotPath) && !string.IsNullOrEmpty(slotPath))
                    {
                        gpuData.MaterialSlotPaths[s] = slotPath;
                    }
                    // DON'T break — continue scanning for sparse indices
                }
            }
            
            _meshCache[assetId] = gpuData;
            return gpuData;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Viewport] Failed to load GPU mesh: {ex.Message}");
            return null;
        }
    }

    private struct RenderItem
    {
        public Entity Entity;
        public TransformComponent Transform;
        public BlueSky.Core.ECS.Builtin.StaticMeshComponent StaticMesh;
        public MeshGPUData GpuData;
        public SubmeshInfo Submesh;
        public BlueSky.Core.Assets.MaterialAsset? Material;
        public float DistanceToCameraSq;
    }

    private void BindMaterial(IRHICommandBuffer cmd, RenderItem item)
    {
        var materialAsset = item.Material;

        IRHITexture? albedoTex = null;
        IRHITexture? normalTex = null;
        IRHITexture? rmaTex    = null;
        IRHITexture? opacityTex = null;
        
        if (materialAsset != null)
        {
            if (!string.IsNullOrEmpty(materialAsset.AlbedoTexturePath))
                albedoTex = LoadCachedTexture(materialAsset.AlbedoTexturePath);
            if (!string.IsNullOrEmpty(materialAsset.NormalTexturePath))
                normalTex = LoadCachedTexture(materialAsset.NormalTexturePath);
            
            // RMA texture: try RMATexturePath first, fall back to RoughnessTexturePath or MetallicTexturePath
            if (!string.IsNullOrEmpty(materialAsset.RMATexturePath))
                rmaTex = LoadCachedTexture(materialAsset.RMATexturePath);
            else if (!string.IsNullOrEmpty(materialAsset.RoughnessTexturePath))
                rmaTex = LoadCachedTexture(materialAsset.RoughnessTexturePath);
            else if (!string.IsNullOrEmpty(materialAsset.MetallicTexturePath))
                rmaTex = LoadCachedTexture(materialAsset.MetallicTexturePath);
            
            if (!string.IsNullOrEmpty(materialAsset.OpacityTexturePath))
                opacityTex = LoadCachedTexture(materialAsset.OpacityTexturePath);
        }

        var submeshMaterial = new MaterialData
        {
            AlbedoAndMetallic = new System.Numerics.Vector4(
                materialAsset != null ? materialAsset.Albedo.X : DefaultAlbedo.X,
                materialAsset != null ? materialAsset.Albedo.Y : DefaultAlbedo.Y,
                materialAsset != null ? materialAsset.Albedo.Z : DefaultAlbedo.Z,
                materialAsset?.Metallic ?? 0.0f),
            Roughness = materialAsset?.Roughness ?? 0.5f,
            Ao = materialAsset?.AO ?? 1.0f,
            Emission = materialAsset != null
                ? (materialAsset.Emission.X + materialAsset.Emission.Y + materialAsset.Emission.Z) / 3.0f * materialAsset.EmissionIntensity
                : 0.0f,
            Subsurface = 0.0f,
            UseAlbedoTex = albedoTex != null ? 1 : 0,
            UseNormalTex = normalTex != null ? 1 : 0,
            UseRMATex = rmaTex != null ? 1 : 0,
            BlendMode = (int)(materialAsset?.BlendMode ?? BlueSky.Rendering.Materials.BlendMode.Opaque),
            UseOpacityTex = opacityTex != null ? 1 : 0
        };
        
        var matSpan = MemoryMarshal.CreateSpan(ref submeshMaterial, 1);
        _device.UpdateBuffer(_materialBuffer!, MemoryMarshal.AsBytes(matSpan));
        cmd.SetUniformBuffer(_materialBuffer!, 11);

        cmd.SetTexture(albedoTex ?? _defaultWhiteTexture!, 2);
        cmd.SetTexture(normalTex ?? _defaultNormalTexture!, 3);
        cmd.SetTexture(rmaTex ?? _defaultRmaTexture!, 4);
        cmd.SetTexture(opacityTex ?? _defaultWhiteOpacityTexture!, 5);
    }
    
    private void DrawBatched(IRHICommandBuffer cmd, System.Collections.Generic.List<RenderItem> items)
    {
        if (items.Count == 0) return;
        
        var batches = items.GroupBy(i => new { i.GpuData, i.Submesh.IndexOffset, i.Material });
        var instancesArray = new EntityUniforms[MaxInstancesPerBatch];
        
        cmd.SetUniformBuffer(_instanceBuffer!, 30);
        
        foreach (var batch in batches)
        {
            var batchItems = batch.ToList();
            var firstItem = batchItems[0];
            
            cmd.SetVertexBuffer(firstItem.GpuData.VertexBuffer!, 0);
            cmd.SetIndexBuffer(firstItem.GpuData.IndexBuffer!, IndexType.UInt32);
            BindMaterial(cmd, firstItem);
            
            for (int i = 0; i < batchItems.Count; i += MaxInstancesPerBatch)
            {
                int count = Math.Min(MaxInstancesPerBatch, batchItems.Count - i);
                
                for (int j = 0; j < count; j++)
                {
                    var item = batchItems[i + j];
                    var color = item.Material != null
                        ? new System.Numerics.Vector4(item.Material.Albedo.X, item.Material.Albedo.Y, item.Material.Albedo.Z, item.Material.Opacity)
                        : new System.Numerics.Vector4(DefaultAlbedo, 1.0f);
                    
                    instancesArray[j] = new EntityUniforms
                    {
                        Model = ToSystemMatrix4x4(item.Transform.WorldMatrix),
                        Color = color
                    };
                }
                
                var span = new ReadOnlySpan<EntityUniforms>(instancesArray, 0, count);
                _device.UpdateBuffer(_instanceBuffer!, MemoryMarshal.AsBytes(span));
                
                cmd.DrawIndexed((uint)firstItem.Submesh.IndexCount, (uint)count, (uint)firstItem.Submesh.IndexOffset, 0, 0);
            }
        }
    }
    
    private void EvictOldMeshes(int maxCacheSize)
    {
        if (_meshCache.Count <= maxCacheSize) return;
        
        var sortedByUsage = _meshCache.ToList();
        sortedByUsage.Sort((a, b) => a.Value.LastUsedFrame.CompareTo(b.Value.LastUsedFrame));
        
        int toRemove = _meshCache.Count - maxCacheSize;
        for (int i = 0; i < toRemove; i++)
        {
            var kvp = sortedByUsage[i];
            kvp.Value.Dispose();
            _meshCache.Remove(kvp.Key);
        }
    }

    // ── Gizmo Geometry ─────────────────────────────────────────────────────

    private void CreateGizmoGeometry()
    {
        // Create a simple arrow shaft (cylinder) + cone tip for translate gizmo
        // The arrow is along +Y axis and will be rotated per-axis via model matrix
        var arrowVerts = new List<Vertex>();
        var arrowIndices = new List<ushort>();
        
        int segments = 12;
        float shaftR = 0.025f;
        float shaftH = 0.8f;
        float coneR = 0.06f;
        float coneH = 0.2f;
        
        // Shaft (cylinder along Y)
        for (int i = 0; i <= segments; i++)
        {
            float a = i * MathF.PI * 2f / segments;
            float cos = MathF.Cos(a), sin = MathF.Sin(a);
            // Bottom ring
            arrowVerts.Add(new Vertex
            {
                Position = new System.Numerics.Vector3(cos * shaftR, 0, sin * shaftR),
                Normal = new System.Numerics.Vector3(cos, 0, sin),
                UV = System.Numerics.Vector2.Zero
            });
            // Top ring
            arrowVerts.Add(new Vertex
            {
                Position = new System.Numerics.Vector3(cos * shaftR, shaftH, sin * shaftR),
                Normal = new System.Numerics.Vector3(cos, 0, sin),
                UV = System.Numerics.Vector2.Zero
            });
        }
        // Shaft indices
        for (int i = 0; i < segments; i++)
        {
            ushort b = (ushort)(i * 2);
            arrowIndices.Add(b); arrowIndices.Add((ushort)(b + 1)); arrowIndices.Add((ushort)(b + 2));
            arrowIndices.Add((ushort)(b + 1)); arrowIndices.Add((ushort)(b + 3)); arrowIndices.Add((ushort)(b + 2));
        }
        
        // Cone tip
        ushort coneCenterIdx = (ushort)arrowVerts.Count;
        arrowVerts.Add(new Vertex
        {
            Position = new System.Numerics.Vector3(0, shaftH + coneH, 0),
            Normal = new System.Numerics.Vector3(0, 1, 0),
            UV = System.Numerics.Vector2.Zero
        });
        
        for (int i = 0; i <= segments; i++)
        {
            float a = i * MathF.PI * 2f / segments;
            float cos = MathF.Cos(a), sin = MathF.Sin(a);
            arrowVerts.Add(new Vertex
            {
                Position = new System.Numerics.Vector3(cos * coneR, shaftH, sin * coneR),
                Normal = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(cos, 0.3f, sin)),
                UV = System.Numerics.Vector2.Zero
            });
        }
        for (int i = 0; i < segments; i++)
        {
            arrowIndices.Add(coneCenterIdx);
            arrowIndices.Add((ushort)(coneCenterIdx + 1 + i));
            arrowIndices.Add((ushort)(coneCenterIdx + 2 + i));
        }
        
        // Upload arrow geometry
        var arrowVertBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(arrowVerts));
        _gizmoArrowVB = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)arrowVertBytes.Length, Usage = BufferUsage.Vertex,
            MemoryType = MemoryType.CpuToGpu, DebugName = "Gizmo.ArrowVB"
        });
        _device.UpdateBuffer(_gizmoArrowVB, arrowVertBytes);
        
        var arrowIdxBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(arrowIndices));
        _gizmoArrowIB = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)arrowIdxBytes.Length, Usage = BufferUsage.Index,
            MemoryType = MemoryType.CpuToGpu, DebugName = "Gizmo.ArrowIB"
        });
        _device.UpdateBuffer(_gizmoArrowIB, arrowIdxBytes);
        _gizmoArrowIndexCount = arrowIndices.Count;
        
        // Create small cube for scale gizmo (0.08 size)
        float cs = 0.04f;
        var cubeVerts = new Vertex[]
        {
            // Front
            new() { Position = new(-cs,-cs, cs), Normal = new(0,0,1), UV = default },
            new() { Position = new( cs,-cs, cs), Normal = new(0,0,1), UV = default },
            new() { Position = new( cs, cs, cs), Normal = new(0,0,1), UV = default },
            new() { Position = new(-cs, cs, cs), Normal = new(0,0,1), UV = default },
            // Back
            new() { Position = new(-cs,-cs,-cs), Normal = new(0,0,-1), UV = default },
            new() { Position = new(-cs, cs,-cs), Normal = new(0,0,-1), UV = default },
            new() { Position = new( cs, cs,-cs), Normal = new(0,0,-1), UV = default },
            new() { Position = new( cs,-cs,-cs), Normal = new(0,0,-1), UV = default },
            // Top
            new() { Position = new(-cs, cs,-cs), Normal = new(0,1,0), UV = default },
            new() { Position = new(-cs, cs, cs), Normal = new(0,1,0), UV = default },
            new() { Position = new( cs, cs, cs), Normal = new(0,1,0), UV = default },
            new() { Position = new( cs, cs,-cs), Normal = new(0,1,0), UV = default },
            // Bottom
            new() { Position = new(-cs,-cs,-cs), Normal = new(0,-1,0), UV = default },
            new() { Position = new( cs,-cs,-cs), Normal = new(0,-1,0), UV = default },
            new() { Position = new( cs,-cs, cs), Normal = new(0,-1,0), UV = default },
            new() { Position = new(-cs,-cs, cs), Normal = new(0,-1,0), UV = default },
            // Right
            new() { Position = new( cs,-cs,-cs), Normal = new(1,0,0), UV = default },
            new() { Position = new( cs, cs,-cs), Normal = new(1,0,0), UV = default },
            new() { Position = new( cs, cs, cs), Normal = new(1,0,0), UV = default },
            new() { Position = new( cs,-cs, cs), Normal = new(1,0,0), UV = default },
            // Left
            new() { Position = new(-cs,-cs,-cs), Normal = new(-1,0,0), UV = default },
            new() { Position = new(-cs,-cs, cs), Normal = new(-1,0,0), UV = default },
            new() { Position = new(-cs, cs, cs), Normal = new(-1,0,0), UV = default },
            new() { Position = new(-cs, cs,-cs), Normal = new(-1,0,0), UV = default },
        };
        ushort[] cubeIdx = {
            0,1,2, 0,2,3,   4,5,6, 4,6,7,   8,9,10, 8,10,11,
            12,13,14, 12,14,15,  16,17,18, 16,18,19,  20,21,22, 20,22,23
        };
        
        var cubeVertBytes = MemoryMarshal.AsBytes(cubeVerts.AsSpan());
        _gizmoCubeVB = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)cubeVertBytes.Length, Usage = BufferUsage.Vertex,
            MemoryType = MemoryType.CpuToGpu, DebugName = "Gizmo.CubeVB"
        });
        _device.UpdateBuffer(_gizmoCubeVB, cubeVertBytes);
        
        var cubeIdxBytes = MemoryMarshal.AsBytes(cubeIdx.AsSpan());
        _gizmoCubeIB = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)cubeIdxBytes.Length, Usage = BufferUsage.Index,
            MemoryType = MemoryType.CpuToGpu, DebugName = "Gizmo.CubeIB"
        });
        _device.UpdateBuffer(_gizmoCubeIB, cubeIdxBytes);
        _gizmoCubeIndexCount = cubeIdx.Length;
        
        // Create torus (ring) for rotate gizmo
        var ringVerts = new List<Vertex>();
        var ringIndices = new List<ushort>();
        
        int ringSegments = 48;
        int tubeSegments = 12;
        float ringRadius = 0.8f;
        float tubeRadius = 0.02f;
        
        for (int i = 0; i <= ringSegments; i++)
        {
            float u = i * MathF.PI * 2f / ringSegments;
            float cosU = MathF.Cos(u), sinU = MathF.Sin(u);
            
            for (int j = 0; j <= tubeSegments; j++)
            {
                float v = j * MathF.PI * 2f / tubeSegments;
                float cosV = MathF.Cos(v), sinV = MathF.Sin(v);
                
                // Ring is flat on XZ plane by default (Y is normal)
                float x = (ringRadius + tubeRadius * cosV) * cosU;
                float y = tubeRadius * sinV;
                float z = (ringRadius + tubeRadius * cosV) * sinU;
                
                // Normal
                float nx = cosV * cosU;
                float ny = sinV;
                float nz = cosV * sinU;
                
                ringVerts.Add(new Vertex
                {
                    Position = new System.Numerics.Vector3(x, y, z),
                    Normal = new System.Numerics.Vector3(nx, ny, nz),
                    UV = System.Numerics.Vector2.Zero
                });
            }
        }
        
        for (int i = 0; i < ringSegments; i++)
        {
            for (int j = 0; j < tubeSegments; j++)
            {
                ushort a = (ushort)(i * (tubeSegments + 1) + j);
                ushort b = (ushort)(a + 1);
                ushort c = (ushort)(a + (tubeSegments + 1));
                ushort d = (ushort)(c + 1);
                
                ringIndices.Add(a); ringIndices.Add(c); ringIndices.Add(b);
                ringIndices.Add(b); ringIndices.Add(c); ringIndices.Add(d);
            }
        }
        
        var ringVertBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(ringVerts));
        _gizmoRingVB = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)ringVertBytes.Length, Usage = BufferUsage.Vertex,
            MemoryType = MemoryType.CpuToGpu, DebugName = "Gizmo.RingVB"
        });
        _device.UpdateBuffer(_gizmoRingVB, ringVertBytes);
        
        var ringIdxBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(ringIndices));
        _gizmoRingIB = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)ringIdxBytes.Length, Usage = BufferUsage.Index,
            MemoryType = MemoryType.CpuToGpu, DebugName = "Gizmo.RingIB"
        });
        _device.UpdateBuffer(_gizmoRingIB, ringIdxBytes);
        _gizmoRingIndexCount = ringIndices.Count;
        
        // Gizmo uniform buffers (one per axis + center)
        for (int i = 0; i < 4; i++)
        {
            _gizmoUniformBuffers[i] = _device.CreateBuffer(new BufferDesc
            {
                Size = (ulong)Marshal.SizeOf<GizmoUniforms>(),
                Usage = BufferUsage.Uniform,
                MemoryType = MemoryType.CpuToGpu,
                DebugName = $"Gizmo.UB.{i}"
            });
        }
        
        _gizmoGeometryCreated = true;
        Console.WriteLine("[ViewportRenderer] Gizmo geometry created (arrow + cube)");
    }

    /// <summary>
    /// Render editor gizmos (translate arrows / rotate rings / scale cubes) 
    /// at the currently selected entity's position.
    /// </summary>
    private void RenderGizmos(IRHICommandBuffer cmd, System.Numerics.Matrix4x4 viewProj, System.Numerics.Vector3 cameraPos)
    {
        if (!_gizmoGeometryCreated || _gizmoPipeline == null || SelectedEntityId == 0)
            return;
            
        // Find the selected entity's world position
        System.Numerics.Vector3 entityPos = System.Numerics.Vector3.Zero;
        bool found = false;
        
        var query = _world.CreateQuery().All<TransformComponent>().Build();
        var chunks = _world.GetQueryChunks(query);
        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();
            int transIdx = chunk.GetComponentIndex(typeof(TransformComponent));
            for (int i = 0; i < chunk.Count; i++)
            {
                if ((uint)entities[i].Id == SelectedEntityId)
                {
                    var t = chunk.GetComponent<TransformComponent>(i, transIdx);
                    entityPos = new System.Numerics.Vector3(t.Position.X, t.Position.Y, t.Position.Z);
                    found = true;
                    break;
                }
            }
            if (found) break;
        }
        
        if (!found) return;
        
        // Scale gizmo based on camera distance for constant screen-space size
        float dist = System.Numerics.Vector3.Distance(cameraPos, entityPos);
        float gizmoScale = MathF.Max(0.5f, dist * 0.15f);
        
        cmd.SetPipeline(_gizmoPipeline!);
        
        // Axis definitions: direction, color, rotation matrix
        var axes = new (System.Numerics.Vector4 color, System.Numerics.Matrix4x4 rotation, float axisId)[]
        {
            // X axis (Red) — rotate arrow from +Y to +X (90° around Z)
            (new System.Numerics.Vector4(0.9f, 0.2f, 0.15f, 1f),
             System.Numerics.Matrix4x4.CreateRotationZ(-MathF.PI / 2f), 0f),
            // Y axis (Green) — arrow already along +Y, no rotation
            (new System.Numerics.Vector4(0.2f, 0.85f, 0.15f, 1f),
             System.Numerics.Matrix4x4.Identity, 1f),
            // Z axis (Blue) — rotate arrow from +Y to +Z (90° around X)
            (new System.Numerics.Vector4(0.2f, 0.35f, 0.92f, 1f),
             System.Numerics.Matrix4x4.CreateRotationX(MathF.PI / 2f), 2f),
        };
        
        int ubIndex = 0;
        foreach (var (color, rotation, axisId) in axes)
        {
            var model = rotation
                      * System.Numerics.Matrix4x4.CreateScale(gizmoScale)
                      * System.Numerics.Matrix4x4.CreateTranslation(entityPos);
            
            var gizmoUniforms = new GizmoUniforms
            {
                ViewProj = viewProj,
                Model = model,
                Color = color,
                GizmoType = (float)CurrentGizmoMode,
                AxisId = axisId,
                IsHovered = (HoveredAxis == (int)axisId) ? 1f : 0f,
            };
            
            var span = MemoryMarshal.CreateSpan(ref gizmoUniforms, 1);
            _device.UpdateBuffer(_gizmoUniformBuffers[ubIndex]!, MemoryMarshal.AsBytes(span));
            cmd.SetUniformBuffer(_gizmoUniformBuffers[ubIndex]!, 10);
            
            if (CurrentGizmoMode == GizmoMode.Translate)
            {
                cmd.SetVertexBuffer(_gizmoArrowVB!, 0);
                cmd.SetIndexBuffer(_gizmoArrowIB!, IndexType.UInt16);
                cmd.DrawIndexed((uint)_gizmoArrowIndexCount);
            }
            else if (CurrentGizmoMode == GizmoMode.Scale)
            {
                // Draw shaft + cube at tip
                cmd.SetVertexBuffer(_gizmoArrowVB!, 0);
                cmd.SetIndexBuffer(_gizmoArrowIB!, IndexType.UInt16);
                cmd.DrawIndexed((uint)_gizmoArrowIndexCount);
                
                // Draw cube at tip position
                var cubeOffset = CurrentGizmoMode == GizmoMode.Scale
                    ? System.Numerics.Matrix4x4.CreateTranslation(0, 0.85f * gizmoScale, 0)
                    : System.Numerics.Matrix4x4.Identity;
                var cubeModel = System.Numerics.Matrix4x4.CreateScale(gizmoScale)
                              * rotation
                              * cubeOffset
                              * System.Numerics.Matrix4x4.CreateTranslation(entityPos);
                
                gizmoUniforms.Model = cubeModel;
                _device.UpdateBuffer(_gizmoUniformBuffers[ubIndex]!, MemoryMarshal.AsBytes(span));
                
                cmd.SetVertexBuffer(_gizmoCubeVB!, 0);
                cmd.SetIndexBuffer(_gizmoCubeIB!, IndexType.UInt16);
                cmd.DrawIndexed((uint)_gizmoCubeIndexCount);
            }
            else // Rotate — draw the ring torus
            {
                cmd.SetVertexBuffer(_gizmoRingVB!, 0);
                cmd.SetIndexBuffer(_gizmoRingIB!, IndexType.UInt16);
                cmd.DrawIndexed((uint)_gizmoRingIndexCount);
            }
            
            ubIndex++;
        }
        
        // Draw center cube (white/yellow) for multi-axis
        {
            var centerModel = System.Numerics.Matrix4x4.CreateScale(gizmoScale * 1.5f)
                            * System.Numerics.Matrix4x4.CreateTranslation(entityPos);
            var centerUniforms = new GizmoUniforms
            {
                ViewProj = viewProj,
                Model = centerModel,
                Color = new System.Numerics.Vector4(1, 1, 1, 1),
                GizmoType = (float)CurrentGizmoMode,
                AxisId = 3f,
                IsHovered = (HoveredAxis == 3) ? 1f : 0f,
            };
            var centerSpan = MemoryMarshal.CreateSpan(ref centerUniforms, 1);
            _device.UpdateBuffer(_gizmoUniformBuffers[3]!, MemoryMarshal.AsBytes(centerSpan));
            cmd.SetUniformBuffer(_gizmoUniformBuffers[3]!, 10);
            
            cmd.SetVertexBuffer(_gizmoCubeVB!, 0);
            cmd.SetIndexBuffer(_gizmoCubeIB!, IndexType.UInt16);
            cmd.DrawIndexed((uint)_gizmoCubeIndexCount);
        }
    }

    /// <summary>
    /// Performs hit-testing against gizmo geometry proxies (spheres/cylinders).
    /// Returns 0=X, 1=Y, 2=Z, 3=Center, or -1 if no hit.
    /// </summary>
    public int HitTestGizmo(Ray ray, BlueSky.Core.Math.Vector3 entityPos, float gizmoScale)
    {
        // 1. Check center cube
        var centerSphere = new BlueSky.Core.Math.BoundingSphere(entityPos, 0.15f * gizmoScale);
        if (ray.Intersects(centerSphere, out _)) return 3;

        // 2. Check axes
        BlueSky.Core.Math.Vector3[] directions = { 
            BlueSky.Core.Math.Vector3.Right, 
            BlueSky.Core.Math.Vector3.Up, 
            BlueSky.Core.Math.Vector3.Back 
        };
        
        for (int i = 0; i < 3; i++)
        {
            if (CurrentGizmoMode == GizmoMode.Rotate)
            {
                var planeNormal = directions[i];
                var plane = new BlueSky.Core.Math.Plane(planeNormal, -BlueSky.Core.Math.Vector3.Dot(planeNormal, entityPos));
                if (ray.Intersects(plane, out float t))
                {
                    var hitPoint = ray.GetPoint(t);
                    float dist = BlueSky.Core.Math.Vector3.Distance(hitPoint, entityPos);
                    if (MathF.Abs(dist - 0.8f * gizmoScale) < 0.1f * gizmoScale)
                        return i;
                }
            }
            else
            {
                var tipPos = entityPos + directions[i] * (0.85f * gizmoScale);
                var tipSphere = new BlueSky.Core.Math.BoundingSphere(tipPos, 0.15f * gizmoScale);
                if (ray.Intersects(tipSphere, out _)) return i;

                for (float s = 0.2f; s < 0.8f; s += 0.2f)
                {
                    var shaftSphere = new BlueSky.Core.Math.BoundingSphere(entityPos + directions[i] * (s * gizmoScale), 0.08f * gizmoScale);
                    if (ray.Intersects(shaftSphere, out _)) return i;
                }
            }
        }
        
        return -1;
    }

    // ── Frustum Culling Helpers ─────────────────────────────────────────
    
    private void ExtractFrustumPlanes(System.Numerics.Matrix4x4 vp, Span<System.Numerics.Vector4> planes)
    {
        // Left
        planes[0] = new System.Numerics.Vector4(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41);
        // Right
        planes[1] = new System.Numerics.Vector4(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41);
        // Bottom
        planes[2] = new System.Numerics.Vector4(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42);
        // Top
        planes[3] = new System.Numerics.Vector4(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42);
        // Near
        planes[4] = new System.Numerics.Vector4(vp.M13, vp.M23, vp.M33, vp.M43);
        // Far
        planes[5] = new System.Numerics.Vector4(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43);

        // Normalize planes
        for (int i = 0; i < 6; i++)
        {
            float length = MathF.Sqrt(planes[i].X * planes[i].X + planes[i].Y * planes[i].Y + planes[i].Z * planes[i].Z);
            if (length > 0.0001f)
                planes[i] /= length;
        }
    }

    private bool IsSphereFrustumVisible(System.Numerics.Vector3 center, float radius, ReadOnlySpan<System.Numerics.Vector4> planes)
    {
        for (int i = 0; i < 6; i++)
        {
            float distance = planes[i].X * center.X + planes[i].Y * center.Y + planes[i].Z * center.Z + planes[i].W;
            if (distance < -radius)
                return false; // Completely outside this plane
        }
        return true;
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
        _gizmoPipeline?.Dispose();
        _shadowMap?.Dispose();
        _uniformBuffer?.Dispose();
        
        if (_gizmoUniformBuffers != null)
        {
            for (int i = 0; i < 4; i++)
                _gizmoUniformBuffers[i]?.Dispose();
        }
        _entityUniformBuffer?.Dispose();
        _horizonViewUniformBuffer?.Dispose();
        _lightBuffer?.Dispose();
        _lightCountBuffer?.Dispose();
        _lightSettingsBuffer?.Dispose();
        _materialBuffer?.Dispose();
        _materialBuffer?.Dispose();
        _gizmoArrowVB?.Dispose();
        _gizmoArrowIB?.Dispose();
        _gizmoCubeVB?.Dispose();
        _gizmoCubeIB?.Dispose();
        _gizmoRingVB?.Dispose();
        _gizmoRingIB?.Dispose();
        
        foreach (var tex in _textureCache.Values)
        {
            tex?.Dispose();
        }
        _textureCache.Clear();

        _defaultWhiteTexture?.Dispose();
        _defaultNormalTexture?.Dispose();
        
        foreach (var mesh in _meshCache.Values)
        {
            mesh.Dispose();
        }
        _meshCache.Clear();
        _materialCache.Clear();

        _disposed = true;
    }
}
