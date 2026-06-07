using System;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using NotBSRenderer;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Math;
using BlueSky.Core.Gameplay;

namespace BlueSky.Editor;

/// <summary>
/// GPU-accelerated 3D viewport renderer.  Draws a procedural sky, an
/// infinite XZ-plane grid gizmo, and ECS entities using the RHI pipeline layer.
/// </summary>
public sealed class ViewportRenderer : IDisposable
{
    /// <summary>F10 in the editor queues a one-shot console dump of per-submesh material resolution.</summary>
    bool _materialDebugDumpPending;
    private const bool VerboseViewportLogging = false;

    // ── Uniform structure (must match Metal ViewUniforms exactly for sky/grid) ────────
    [StructLayout(LayoutKind.Sequential)]
    private struct ViewUniforms
    {
        public System.Numerics.Matrix4x4 View;
        public System.Numerics.Matrix4x4 Proj;
        public System.Numerics.Matrix4x4 ViewProj;
        public System.Numerics.Matrix4x4 InvViewProj;
        public System.Numerics.Matrix4x4 LightSpaceMatrix;
        public System.Numerics.Vector4 CameraPos; // 16 bytes (offset 320)
        public float Time;                        // 4 bytes (offset 336)
        
        // Metal aligns float3 to 16 bytes, so we need 12 bytes padding here
        private float _pad1;
        private float _pad2;
        private float _pad3;
        
        public System.Numerics.Vector3 SunDirection; // 12 bytes (offset 352)
        private float _pad4;                         // 4 bytes to complete 16-byte alignment
        
        public System.Numerics.Vector4 WindParams;   // 16 bytes (offset 368)
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

    public void SetTerrainBrushPreview(bool visible, System.Numerics.Vector3 position, System.Numerics.Vector3 normal, float radius, BrushMode mode)
    {
        _terrainBrushPreviewVisible = visible;
        _terrainBrushPreviewPosition = position;
        _terrainBrushPreviewNormal = normal.LengthSquared() > 0.0001f
            ? System.Numerics.Vector3.Normalize(normal)
            : System.Numerics.Vector3.UnitY;
        _terrainBrushPreviewRadius = MathF.Max(0.05f, radius);
        _terrainBrushPreviewMode = mode;
    }

    private bool _terrainBrushPreviewVisible;
    private System.Numerics.Vector3 _terrainBrushPreviewPosition;
    private System.Numerics.Vector3 _terrainBrushPreviewNormal = System.Numerics.Vector3.UnitY;
    private float _terrainBrushPreviewRadius = 1.0f;
    private BrushMode _terrainBrushPreviewMode = BrushMode.Raise;
    
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

        /// <summary>
        /// CPU-side copy of the index buffer (uint32).
        /// Used for bone detection in skeletal mesh rendering:
        /// submesh.IndexOffset indexes into THIS array, not skelMesh.Indices.
        /// </summary>
        public uint[]? RawIndices;

        /// <summary>
        /// CPU-side copy of vertex positions (parsed from the packed vertex buffer).
        /// Used for centroid-based submesh→wheel mapping on static meshes.
        /// </summary>
        public System.Numerics.Vector3[]? RawVertexPositions;
        
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
    private readonly BlueSky.Rendering.TerrainSystem? _terrainSystem;
    private readonly BlueSky.Rendering.TerrainRenderer _terrainRenderer;
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
    private          const int     MaxInstancesPerBatch = 512;
    // Per-frame instance buffer: large enough for ALL entities in the scene.
    // Uploaded ONCE per frame before any draw calls so every draw can read its
    // own unique slice via the firstInstance offset — eliminates the shared-
    // buffer aliasing bug where entities would snap to each other's position.
    private          IRHIBuffer?   _frameInstanceBuffer;
    private          const int     MaxFrameInstances    = 4096; // covers very large scenes
    private          int           _debugFrameCounter   = 0;
    
    // Horizon Lighting buffers
    private IRHIBuffer? _horizonViewUniformBuffer; // Separate buffer for Horizon shader
    private IRHIBuffer? _lightBuffer;
    private IRHIBuffer? _lightCountBuffer;
    private IRHIBuffer? _lightSettingsBuffer;
    private IRHIBuffer? _materialBuffer;
    
    private readonly Dictionary<string, MeshGPUData> _meshCache = new();
    private readonly Dictionary<string, BlueSky.Core.Assets.MaterialAsset?> _materialCache = new();

    // ── Wheel animation for static meshes (no skeleton) ─────────────────
    // Maps (meshAssetId, entityId) → submeshIndex→wheelIndex (-1 = not a wheel).
    // Built lazily on first encounter by analysing submesh vertex centroids
    // against the car controller's wheel positions.
    private readonly Dictionary<(string, uint), int[]> _submeshWheelMap = new();
    
    // ── Texture cache for material textures ─────────────────────────────
    // Key includes sRGB flag: same file path must not be shared between albedo (sRGB) and data (linear) textures.
    private readonly Dictionary<(string Path, bool Srgb), IRHITexture?> _textureCache = new();
    private IRHITexture? _defaultWhiteTexture;
    private IRHITexture? _defaultNormalTexture;
    private IRHITexture? _defaultRmaTexture;
    private IRHITexture? _defaultWhiteOpacityTexture;

    public IRHITexture DefaultWhiteTexture => _defaultWhiteTexture!;
    public IRHITexture DefaultNormalTexture => _defaultNormalTexture!;
    public IRHITexture DefaultRmaTexture => _defaultRmaTexture!;

    private ulong _frameCount = 0; // For LRU eviction

    // ── Skeletal mesh bone detection helper ─────────────────────────────
    /// <summary>
    /// Accumulate a bone weight vote for dominant-bone detection per submesh.
    /// </summary>
    private static void AccumulateBoneVote(Dictionary<int, float> votes, int boneIndex, float weight)
    {
        if (boneIndex < 0 || weight <= 0f) return;
        if (votes.TryGetValue(boneIndex, out float existing))
            votes[boneIndex] = existing + weight;
        else
            votes[boneIndex] = weight;
    }

    // ── Gizmo resources ─────────────────────────────────────────────────
    private          IRHIPipeline? _gizmoPipeline;
    private          IRHIBuffer?[] _gizmoUniformBuffers = new IRHIBuffer?[5];
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
    private readonly TextureFormat _colorFormat;

    public ViewportRenderer(IRHIDevice device, World world, BlueSky.Rendering.TerrainSystem? terrainSystem = null, TextureFormat colorFormat = TextureFormat.RGBA8Unorm)
    {
        _device = device;
        _world = world;
        _terrainSystem = terrainSystem;
        _colorFormat = colorFormat;
        _terrainRenderer = new BlueSky.Rendering.TerrainRenderer(device);
        CreatePipelines();
        CreateBuffers();
        CreateDefaultTextures();
        CreateGizmoGeometry();
        
        // Clean up any corrupted mesh entities on startup
        CleanupCorruptedMeshes();
    }
    
    /// <summary>
    /// Get cached mesh GPU data for EasePlus renderer integration.
    /// </summary>
    public MeshGPUData? GetCachedMesh(string assetId)
    {
        if (string.IsNullOrEmpty(assetId)) return null;
        
        if (!_meshCache.TryGetValue(assetId, out var gpuData))
        {
            // Demand-load the mesh
            gpuData = LoadGpuMesh(assetId);
        }
        
        return gpuData;
    }
    
    /// <summary>
    /// Load cached material for EasePlus renderer integration.
    /// </summary>
    public BlueSky.Core.Assets.MaterialAsset? LoadCachedMaterial(string? matPath)
    {
        return LoadCachedMaterialInternal(matPath);
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
    /// <param name="storedInSrgb">
    /// True for base-color / albedo (glTF sRGB). False for normal, MR, AO, opacity — linear data.
    /// </param>
    public IRHITexture? LoadCachedTexture(string path, bool storedInSrgb = false)
    {
        if (string.IsNullOrEmpty(path)) 
        {
            return null;
        }

        var cacheKey = (path, storedInSrgb);
        if (_textureCache.TryGetValue(cacheKey, out var cached)) 
        {
            return cached;
        }

        if (!System.IO.File.Exists(path))
        {
            // Do NOT cache null here — the file may appear after a re-import.
            return null;
        }

        try
        {
            IRHITexture? tex = null;

            if (path.EndsWith(".blueskyasset", StringComparison.OrdinalIgnoreCase))
            {
                tex = LoadTextureFromBlueAsset(path, storedInSrgb);
            }
            else
            {
                tex = LoadTextureFromRawFile(path, storedInSrgb);
            }

            _textureCache[cacheKey] = tex;
            return tex;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Viewport] Exception loading texture '{path}': {ex.Message}");
            return null; // Not cached — will retry next frame
        }
    }

    private IRHITexture? LoadTextureFromBlueAsset(string path, bool storedInSrgb)
    {
        var asset = BlueSky.Core.Assets.BlueAsset.Load(path);
        if (asset == null || !asset.HasPayload)
        {
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
            return null;
        }

        byte[] data = reader.ReadBytes(dataLen);

        var tex = _device.CreateTexture(new TextureDesc
        {
            Width = (uint)width, Height = (uint)height,
            Depth = 1, MipLevels = 1, ArrayLayers = 1,
            Format = storedInSrgb ? TextureFormat.RGBA8Srgb : TextureFormat.RGBA8Unorm,
            Usage  = TextureUsage.Sampled,
            DebugName = asset.AssetName
        });
        _device.UploadTexture(tex, data);
        
        return tex;
    }

    private IRHITexture? LoadTextureFromRawFile(string path, bool storedInSrgb)
    {
        // Raw image files (png/jpg/etc.) — no vertical flip needed.
        // Modern DCC tools export OBJ/FBX with standard UV convention (V=0 at top).
        StbImageSharp.StbImage.stbi_set_flip_vertically_on_load(0);
        using var stream = System.IO.File.OpenRead(path);
        var image = StbImageSharp.ImageResult.FromStream(
            stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);

        if (image == null)
        {
            return null;
        }

        var tex = _device.CreateTexture(new TextureDesc
        {
            Width = (uint)image.Width, Height = (uint)image.Height,
            Depth = 1, MipLevels = 1, ArrayLayers = 1,
            Format = storedInSrgb ? TextureFormat.RGBA8Srgb : TextureFormat.RGBA8Unorm,
            Usage  = TextureUsage.Sampled,
            DebugName = System.IO.Path.GetFileNameWithoutExtension(path)
        });
        _device.UploadTexture(tex, image.Data);
        return tex;
    }

    /// <summary>
    /// Demand-load a MaterialAsset from a .blueskyasset file.
    /// Results are cached by path. Missing files are NOT permanently cached.
    /// Does NOT mutate the loaded asset (no metallic hotfix — import correctly instead).
    /// </summary>
    private BlueSky.Core.Assets.MaterialAsset? LoadCachedMaterialInternal(string? path)
    {
        if (string.IsNullOrEmpty(path)) 
        {
            return null;
        }
        
        if (_materialCache.TryGetValue(path, out var cached)) 
        {
            return cached;
        }

        if (!System.IO.File.Exists(path))
        {
            // Not cached — will retry after re-import
            return null;
        }

        try
        {
            var mat = BlueSky.Core.Assets.MaterialAsset.Load(path);
            _materialCache[path] = mat; // cache even if null (decode failure)
            return mat;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Viewport] Exception loading material '{path}': {ex.Message}");
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
        var toRemove = new System.Collections.Generic.List<(string Path, bool Srgb)>();
        foreach (var kv in _textureCache)
        {
            if (kv.Key.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                kv.Value?.Dispose();
                toRemove.Add(kv.Key);
            }
        }
        foreach (var k in toRemove)
            _textureCache.Remove(k);
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

        // Evict textures (cache keys are path + sRGB flag)
        var texKeys = new System.Collections.Generic.List<(string Path, bool Srgb)>(
            System.Linq.Enumerable.Where(_textureCache.Keys,
                k => k.Path.StartsWith(assetDir, StringComparison.OrdinalIgnoreCase)));
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

    /// <summary>
    /// Drop cached GPU mesh data for one asset so the next draw reloads submeshes + materialSlotPaths from disk.
    /// Call after static mesh metadata changes or when debugging stale material bindings.
    /// </summary>
    public void InvalidateMeshGpuCache(string meshAssetId)
    {
        if (string.IsNullOrEmpty(meshAssetId)) return;
        if (!_meshCache.TryGetValue(meshAssetId, out var gpu)) return;
        gpu.Dispose();
        _meshCache.Remove(meshAssetId);
        Console.WriteLine($"[Viewport] Invalidated GPU mesh cache: {meshAssetId}");
    }

    /// <summary>Next draw queues up to 100 submesh lines printed to the console (see F10 in EditorApp).</summary>
    public void RequestMaterialDebugDump() => _materialDebugDumpPending = true;

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
            // FIX: Same shared-buffer aliasing bug as the main pass.
            // Build a local shadow instance list, upload once, then use per-item
            // InstanceIndex as firstInstance in DrawIndexed.
            var shadowInstances = new List<EntityUniforms>(shadowItems.Count);
            var shadowIndices   = new int[shadowItems.Count];
            for (int si = 0; si < shadowItems.Count; si++)
            {
                shadowIndices[si] = shadowInstances.Count;
                if (shadowInstances.Count < MaxFrameInstances)
                    shadowInstances.Add(new EntityUniforms
                    {
                        Model = ToSystemMatrix4x4(shadowItems[si].Transform.WorldMatrix),
                        Color = System.Numerics.Vector4.One
                    });
            }
            // Upload ALL shadow instance transforms at once
            if (_frameInstanceBuffer != null && shadowInstances.Count > 0)
            {
                int uploadCount = Math.Min(shadowInstances.Count, MaxFrameInstances);
                ReadOnlySpan<EntityUniforms> shadowSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(shadowInstances).Slice(0, uploadCount);
                _device.UpdateBuffer(_frameInstanceBuffer, MemoryMarshal.AsBytes(shadowSpan));
            }

            var batches = shadowItems.Select((item, idx) => (item, idx))
                .GroupBy(x => new { x.item.GpuData, x.item.Submesh.IndexOffset });

            cmd.SetUniformBuffer(_frameInstanceBuffer!, 30);

            foreach (var batch in batches)
            {
                var batchItems = batch.ToList();
                var firstItem = batchItems[0].item;

                cmd.SetVertexBuffer(firstItem.GpuData.VertexBuffer!, 0);
                cmd.SetIndexBuffer(firstItem.GpuData.IndexBuffer!, IndexType.UInt32);

                for (int i = 0; i < batchItems.Count; i += MaxInstancesPerBatch)
                {
                    int count = Math.Min(MaxInstancesPerBatch, batchItems.Count - i);
                    uint firstInst = (uint)shadowIndices[batchItems[i].idx];
                    
                    cmd.DrawIndexed((uint)firstItem.Submesh.IndexCount, (uint)count, (uint)firstItem.Submesh.IndexOffset, 0, firstInst);
                }
            }
        }

        cmd.EndRenderPass();
    }

    private static readonly System.Numerics.Vector3 DefaultAlbedo = new(0.5f, 0.5f, 0.5f); // Neutral grey fallback - visible even without material
    
public void Render(IRHICommandBuffer cmd, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 proj,
    System.Numerics.Vector3 cameraPos, int viewportX, int viewportY, int viewportW, int viewportH, float deltaTime)
{
    _frameCount++;
    _elapsedTime += deltaTime;

    // ═══════════════════════════════════════════════════════════════════════════
    // SUPER VERBOSE DEBUG MODE
    // ═══════════════════════════════════════════════════════════════════════════
    
    if (VerboseViewportLogging && _frameCount <= 10)
    {
        Console.WriteLine($"[DEBUG] Frame {_frameCount}: cameraPos={cameraPos}");
        Console.WriteLine($"[DEBUG] UniformBuffer={_uniformBuffer != null}, ShadowMap={_shadowMap != null}");
        Console.WriteLine($"[DEBUG] Pipelines: sky={_skyPipeline != null}, grid={_gridPipeline != null}, mesh={_meshPipeline != null}");
    }
    
    if (VerboseViewportLogging && (_frameCount == 60 || _frameCount == 120 || _frameCount == 180))
    {
        Console.WriteLine($"[ViewportRenderer] Frame {_frameCount}: viewport=({viewportX},{viewportY}) size={viewportW}x{viewportH}");
        Console.WriteLine($"[ViewportRenderer] Pipelines: sky={_skyPipeline != null}, grid={_gridPipeline != null}, mesh={_meshPipeline != null}");
        Console.WriteLine($"[ViewportRenderer] SunDirection={BlueSky.Core.WorldEnvironment.GlobalEnvironment.SunDirection}");
        Console.WriteLine($"[ViewportRenderer] CameraPos={cameraPos}");
        
        // Check if there are any entities
        var query = _world.CreateQuery().All<TransformComponent>().All<BlueSky.Core.ECS.Builtin.StaticMeshComponent>().Build();
        var chunks = _world.GetQueryChunks(query);
        int entityCount = 0;
        foreach (var chunk in chunks)
        {
            entityCount += chunk.Count;
        }
        Console.WriteLine($"[ViewportRenderer] Entity count in world: {entityCount}");
    }
    
// ── build uniforms for sky/grid (old ViewUniforms) ────────────────────────────────
var viewProj = view * proj;
System.Numerics.Matrix4x4.Invert(viewProj, out var invViewProj);
var sunDir = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.3f, 0.7f, 0.4f)); // Natural sun angle

var lightProj = System.Numerics.Matrix4x4.CreateOrthographicOffCenter(-20, 20, -20, 20, 0.1f, 100f);
var lightView = System.Numerics.Matrix4x4.CreateLookAt(-sunDir * 30f, System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitY);
var lightViewProj = lightView * lightProj;

var sunDirectionValue = BlueSky.Core.WorldEnvironment.GlobalEnvironment.SunDirection;

var uniforms = new ViewUniforms
{
    View = view,
    Proj = proj,
    ViewProj = viewProj,
    InvViewProj = invViewProj,
    LightSpaceMatrix = lightViewProj,
    CameraPos = new System.Numerics.Vector4(cameraPos, 1.0f),
    Time = _elapsedTime,
    SunDirection = sunDirectionValue,
    WindParams = BlueSky.Core.WorldEnvironment.GlobalEnvironment.WindParams
};

var uniformSpan = MemoryMarshal.CreateSpan(ref uniforms, 1);
_device.UpdateBuffer(_uniformBuffer!, MemoryMarshal.AsBytes(uniformSpan));

if (VerboseViewportLogging && _frameCount <= 3)
{
    Console.WriteLine($"[DEBUG] Uniform buffer updated. SunDirection in struct: {uniforms.SunDirection}");
}

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
            Direction = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.3f, 0.7f, 0.4f)), // Natural sun angle
            Intensity = 3.5f, // Realistic sun intensity - not too bright
            Color = new System.Numerics.Vector3(1.0f, 0.98f, 0.95f), // Natural daylight color
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
        
        // Update lighting settings with improved ambient for better depth perception
        var lightSettings = new LightingSettings
        {
            Quality = 2, // High
            MaxLights = 64,
            EnableIBL = 1, // Enable IBL for better ambient
            EnableVolumetrics = 0,
            EnableContactShadows = 1,
            Exposure = 1.0f, // Natural exposure - not overblown
            AmbientColor = new System.Numerics.Vector3(0.15f, 0.18f, 0.22f), // Realistic ambient - subtle sky bounce
        };
        var lightSettingsSpan = MemoryMarshal.CreateSpan(ref lightSettings, 1);
        _device.UpdateBuffer(_lightSettingsBuffer!, MemoryMarshal.AsBytes(lightSettingsSpan));
        
        // Default material data (will be overridden per-submesh in RenderEntities)
        var material = new MaterialData
        {
            AlbedoAndMetallic = new System.Numerics.Vector4(DefaultAlbedo, 0.1f), // Bright white with low metallic
            Roughness = 0.6f, // Slightly rougher for better lighting
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

        // ── 2. Terrain (BEFORE entities/grid; writes depth like world geometry) ───────────
        if (_terrainSystem != null && _meshPipeline != null && _shadowMap != null &&
            _defaultWhiteTexture != null && _defaultNormalTexture != null &&
            _defaultRmaTexture != null && _defaultWhiteOpacityTexture != null)
        {
            _terrainRenderer.Render(cmd, _world, _terrainSystem, _meshPipeline,
                _uniformBuffer!, _lightBuffer!, _lightCountBuffer!, _lightSettingsBuffer!,
                _shadowMap, _defaultWhiteTexture, _defaultNormalTexture,
                _defaultRmaTexture, _defaultWhiteOpacityTexture, viewProj, cameraPos);
        }

        // ── 3. Entities (BEFORE grid for correct transparency) ───────────
        // Clear per-frame instance list so we start fresh; RenderEntities will
        // repopulate it and upload it once before any draw calls.
        _frameInstances.Clear();
        RenderEntities(cmd, view, proj, cameraPos);

        // ── 4. Grid (AFTER opaque world geometry for proper alpha blending) ───────────
        cmd.SetPipeline(_gridPipeline!);
        cmd.SetUniformBuffer(_uniformBuffer!, 10);
        cmd.Draw(6); // fullscreen quad (2 tris)
        
        // ── 5. Editor Gizmos (LAST — always on top) ──────────────────────
        RenderTerrainBrushPreview(cmd, viewProj);
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
            ColorFormats    = new[] { _colorFormat },
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
            ColorFormats    = new[] { _colorFormat },
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
            ColorFormats    = new[] { _colorFormat },
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
            ColorFormats    = new[] { _colorFormat },
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
            ColorFormats    = new[] { _colorFormat },
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
                ColorFormats    = new[] { _colorFormat },
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
                System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Shaders", baseName + ".metallib"),
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
        else if (_device.Backend == RHIBackend.DirectX11)
        {
            // For DX11, load pre-compiled .cso (Compiled Shader Object) files
            // These are generated by running compile_shaders.bat with fxc.exe
            string csoFileName = GetCSOFileName(stage, entryPoint);
            
            if (!string.IsNullOrEmpty(csoFileName))
            {
                string[] searchPaths = new[]
                {
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", csoFileName),
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Editor", "Shaders", csoFileName),
                    System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Editor", "Shaders", csoFileName),
                    System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "BlueSkyEngine", "Editor", "Shaders", csoFileName),
                };
                
                string? found = System.Array.Find(searchPaths, System.IO.File.Exists);
                if (found != null)
                {
                    bytecode = System.IO.File.ReadAllBytes(found);
                    Console.WriteLine($"[ViewportRenderer] Loaded DX11 shader: {found} ({bytecode.Length} bytes)");
                }
                else
                {
                    Console.WriteLine($"[ViewportRenderer] WARNING: {csoFileName} not found. Run compile_shaders.bat to compile HLSL shaders.");
                    Console.WriteLine($"  Searched:");
                    foreach (var p in searchPaths) Console.WriteLine($"    {p}");
                }
            }
        }

        return new()
        {
            Stage      = stage,
            EntryPoint = entryPoint,
            Bytecode   = bytecode,
        };
    }

    /// <summary>
    /// Map a (stage, entryPoint) pair to the corresponding .cso filename.
    /// Naming convention: {entryPoint}.cso  (compiled by compile_shaders.bat)
    /// </summary>
    private static string GetCSOFileName(ShaderStage stage, string entryPoint)
    {
        // Direct mapping from entry point to CSO filename
        // The compile_shaders.bat uses: fxc /E {entryPoint} /Fo {entryPoint}.cso
        return entryPoint switch
        {
            // Sky
            "vs_sky"   => "vs_sky.cso",
            "fs_sky"   => "fs_sky.cso",
            // Grid
            "vs_grid"  => "vs_grid.cso",
            "fs_grid"  => "fs_grid.cso",
            // Mesh
            "vs_mesh"  => "vs_mesh.cso",
            "fs_mesh"  => "fs_mesh.cso",
            // Shadow
            "horizon_shadow_vertex"   => "vs_shadow.cso",
            "horizon_shadow_fragment" => "fs_shadow.cso",
            "vs_shadow"   => "vs_shadow.cso",
            "fs_shadow"   => "fs_shadow.cso",
            // Gizmo
            "vs_gizmo" => "vs_gizmo.cso",
            "fs_gizmo" => "fs_gizmo.cso",
            // Wireframe
            "fs_wireframe" => "fs_wireframe.cso",
            // UI
            "vs_ui"    => "vs_ui.cso",
            "fs_ui"    => "fs_ui.cso",
            _ => $"{entryPoint}.cso"
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
        
        // Large per-frame instance buffer — holds transforms for ALL entities.
        // Uploaded once per frame so every draw call sees its own unique slice.
        _frameInstanceBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = (ulong)Marshal.SizeOf<EntityUniforms>() * MaxFrameInstances,
            Usage      = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "Viewport.FrameInstanceUB",
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
        // Bind buffers for fs_mesh (viewport_3d.metal)
        // CRITICAL: fs_mesh uses the FULL ViewUniforms struct (with sunDirection, windParams)
        // NOT the smaller HorizonViewUniforms. Binding the wrong struct here was causing
        // garbage sunDirection → zero direct lighting → black models.
        // Buffer 10: ViewUniforms (full struct with sunDirection for light calculation)
        cmd.SetUniformBuffer(_uniformBuffer!, 10);
        // Buffer 11: MaterialData (Bound per-mesh in BindMaterial)
        // Buffer 13: LightData* (fs_mesh expects slot 13, NOT 12!)
        cmd.SetUniformBuffer(_lightBuffer!, 13);
        // Buffer 14: int lightCount (fs_mesh expects slot 14, NOT 13!)
        cmd.SetUniformBuffer(_lightCountBuffer!, 14);
        // Buffer 15: LightingSettings (fs_mesh expects slot 15, NOT 14!)
        cmd.SetUniformBuffer(_lightSettingsBuffer!, 15);

        bool matDbgRun = _materialDebugDumpPending;
        int matDbgBudget = matDbgRun ? 100 : 0;
        int matDbgPrinted = 0;

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
                
                bool isVisible = IsSphereFrustumVisible(entityPos, boundingRadius, frustumPlanes);
                
                if (!isVisible)
                {
                    continue;
                }

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

                        var material = LoadCachedMaterialInternal(matPath);

                        if (matDbgBudget > 0)
                        {
                            bool inlineMat = !string.IsNullOrEmpty(staticMesh.GetEffectiveMaterial(submesh.MaterialSlot));
                            gpuData.MaterialSlotPaths.TryGetValue(submesh.MaterialSlot, out var metaSlotPath);
                            string? ap = material?.AlbedoTexturePath;
                            string? rp = material?.RMATexturePath;
                            bool aOk = !string.IsNullOrEmpty(ap) && System.IO.File.Exists(ap);
                            bool rOk = !string.IsNullOrEmpty(rp) && System.IO.File.Exists(rp);
                            var ent = chunk.GetEntities()[i];
                            Console.WriteLine(
                                $"[MatDbg] mesh={System.IO.Path.GetFileName(assetId)} ent={ent.Id} slot={submesh.MaterialSlot} " +
                                $"path={(string.IsNullOrEmpty(matPath) ? "EMPTY" : System.IO.Path.GetFileName(matPath))} " +
                                $"inline={inlineMat} metaPath={(string.IsNullOrEmpty(metaSlotPath) ? "-" : System.IO.Path.GetFileName(metaSlotPath))} " +
                                $"mat={(material == null ? "NULL" : "OK")} albedoDisk={aOk} rmaDisk={rOk}");
                            matDbgBudget--;
                            matDbgPrinted++;
                        }
                        // Record the instance index DURING gather so DrawBatched can
                        // use it as firstInstance — no matrix-equality search needed.
                        int instIdx = _frameInstances.Count;
                        var color = material != null
                            ? new System.Numerics.Vector4(material.Albedo.X, material.Albedo.Y, material.Albedo.Z, material.Opacity)
                            : new System.Numerics.Vector4(DefaultAlbedo, 1.0f);
                        
                        var modelMatrix = transform.WorldMatrix;
                        
                        // Check if this entity has a CarController (for wheel steering/spinning animation)
                        var carController = BlueSky.Core.Gameplay.CarControllerSystem.GetController((uint)chunk.GetEntities()[i].Id);
                        if (carController != null && carController.AnimController != null && carController.SkeletalMesh != null)
                        {
                            // ── Skeletal-mesh path: use bone voting ──
                            int boneIdx = -1;
                            var skelMesh = carController.SkeletalMesh;
                            if (skelMesh.Vertices != null && gpuData.RawIndices != null)
                            {
                                int indexOffset = submesh.IndexOffset;
                                int indexEnd = Math.Min(indexOffset + submesh.IndexCount, gpuData.RawIndices.Length);
                                int maxVertsToCheck = Math.Min(16, (indexEnd - indexOffset) / 3);

                                var boneVotes = new Dictionary<int, float>();

                                for (int vi = 0; vi < maxVertsToCheck && (indexOffset + vi * 3) < indexEnd; vi++)
                                {
                                    int idxPos = indexOffset + vi * 3;
                                    if (idxPos >= gpuData.RawIndices.Length) break;
                                    uint vertexIdx = gpuData.RawIndices[idxPos];
                                    if (vertexIdx >= skelMesh.Vertices.Length) continue;

                                    var vertex = skelMesh.Vertices[vertexIdx];
                                    AccumulateBoneVote(boneVotes, vertex.BoneIndex0, vertex.BoneWeight0);
                                    AccumulateBoneVote(boneVotes, vertex.BoneIndex1, vertex.BoneWeight1);
                                    AccumulateBoneVote(boneVotes, vertex.BoneIndex2, vertex.BoneWeight2);
                                    AccumulateBoneVote(boneVotes, vertex.BoneIndex3, vertex.BoneWeight3);
                                }

                                float maxVote = 0f;
                                foreach (var kvp in boneVotes)
                                {
                                    if (kvp.Value > maxVote)
                                    {
                                        maxVote = kvp.Value;
                                        boneIdx = kvp.Key;
                                    }
                                }

                                if (maxVote < 0.1f)
                                    boneIdx = -1;
                            }

                            if (boneIdx >= 0 && boneIdx < carController.AnimController.BoneTransforms.Length)
                            {
                                var boneMatrix = carController.AnimController.BoneTransforms[boneIdx];
                                var engineBone = new BlueSky.Core.Math.Matrix4x4(
                                    boneMatrix.M11, boneMatrix.M12, boneMatrix.M13, boneMatrix.M14,
                                    boneMatrix.M21, boneMatrix.M22, boneMatrix.M23, boneMatrix.M24,
                                    boneMatrix.M31, boneMatrix.M32, boneMatrix.M33, boneMatrix.M34,
                                    boneMatrix.M41, boneMatrix.M42, boneMatrix.M43, boneMatrix.M44
                                );
                                
                                // Log which submeshes are animated (write to file for debugging)
                                if (_debugFrameCounter++ % 60 == 0)
                                {
                                    var logPath = "/tmp/bluesky_bones.txt";
                                    var msg = $"[Rendering] Submesh #{gpuData.Submeshes.IndexOf(submesh)} → bone {boneIdx}\n";
                                    System.IO.File.AppendAllText(logPath, msg);
                                }
                                
                                modelMatrix = engineBone * modelMatrix;
                            }
                        }
                        else if (carController != null && carController.WheelCount >= 4
                                 && gpuData.RawIndices != null && gpuData.RawVertexPositions != null)
                        {
                            // ── Static-mesh fallback: centroid-based wheel detection ──
                            // Map each submesh to a wheel slot by comparing its vertex
                            // centroid against the car controller's wheel positions.
                            uint entId = (uint)chunk.GetEntities()[i].Id;
                            var cacheKey = (assetId, entId);

                            if (!_submeshWheelMap.TryGetValue(cacheKey, out int[] wheelMap))
                            {
                                wheelMap = BuildSubmeshWheelMap(gpuData, carController);
                                _submeshWheelMap[cacheKey] = wheelMap;
                            }

                            // Find current submesh's index in the submesh list
                            int submeshIdx = gpuData.Submeshes.IndexOf(submesh);
                            if (submeshIdx >= 0 && submeshIdx < wheelMap.Length && wheelMap[submeshIdx] >= 0)
                            {
                                int wheelSlot = wheelMap[submeshIdx];
                                var wheelRot = carController.GetWheelTransformMatrix(wheelSlot);
                                Console.WriteLine($"[ViewportRenderer] 🎨 Entity_{chunk.GetEntities()[i].Id}: Applied static wheel transform slot={wheelSlot} to submesh");
                                
                                // Compute the submesh centroid so we can rotate around it
                                var centroid = ComputeSubmeshCentroid(gpuData, submesh);

                                // Build: Translate(-centroid) * Rotation * Translate(+centroid)
                                var toOrigin = System.Numerics.Matrix4x4.CreateTranslation(-centroid);
                                var fromOrigin = System.Numerics.Matrix4x4.CreateTranslation(centroid);
                                var wheelTransform = toOrigin * wheelRot * fromOrigin;

                                var ew = new BlueSky.Core.Math.Matrix4x4(
                                    wheelTransform.M11, wheelTransform.M12, wheelTransform.M13, wheelTransform.M14,
                                    wheelTransform.M21, wheelTransform.M22, wheelTransform.M23, wheelTransform.M24,
                                    wheelTransform.M31, wheelTransform.M32, wheelTransform.M33, wheelTransform.M34,
                                    wheelTransform.M41, wheelTransform.M42, wheelTransform.M43, wheelTransform.M44
                                );
                                modelMatrix = ew * modelMatrix;
                            }
                        }

                        if (instIdx < MaxFrameInstances)
                        {
                            _frameInstances.Add(new EntityUniforms
                            {
                                Model = ToSystemMatrix4x4(modelMatrix),
                                Color = color
                            });
                        }
                        else instIdx = MaxFrameInstances - 1; // clamp; scene too large

                        var item = new RenderItem
                        {
                            Entity = chunk.GetEntities()[i],
                            Transform = transform,
                            StaticMesh = staticMesh,
                            GpuData = gpuData,
                            Submesh = submesh,
                            Material = material,
                            DistanceToCameraSq = distSq,
                            InstanceIndex = instIdx
                        };

                        if (material != null && material.BlendMode == BlueSky.Rendering.Materials.BlendMode.AlphaBlend)
                            transparentItems.Add(item);
                        else
                            opaqueItems.Add(item);
                    }
                }
            }
        }

        if (matDbgRun)
        {
            Console.WriteLine($"[MatDbg] === end ({matDbgPrinted} lines) ===");
            _materialDebugDumpPending = false;
        }

        // 2. Upload ALL instance transforms ONCE before any draw calls.
        //    This is the critical fix: a single UpdateBuffer here means the GPU
        //    sees every entity's unique transform, not just the last one written.
        UploadFrameInstances();

        // 3. Draw Opaque Pass (Opaque + AlphaTest)
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

            // Parse raw index array on CPU for skeletal-mesh bone detection.
            // submesh.IndexOffset indexes into THIS array, not skelMesh.Indices.
            uint[] rawIndices = new uint[iLen / 4];
            Buffer.BlockCopy(iData, 0, rawIndices, 0, (int)iLen);

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

            // Parse vertex positions on the CPU for centroid-based wheel detection.
            int vertexStride = 32; // Position(12) + Normal(12) + UV(8)
            int vertexCount = vLen / vertexStride;
            var rawPositions = new System.Numerics.Vector3[vertexCount];
            for (int vi = 0; vi < vertexCount; vi++)
            {
                int off = vi * vertexStride;
                if (off + 12 <= vData.Length)
                {
                    rawPositions[vi] = new System.Numerics.Vector3(
                        BitConverter.ToSingle(vData, off),
                        BitConverter.ToSingle(vData, off + 4),
                        BitConverter.ToSingle(vData, off + 8));
                }
            }

            var gpuData = new MeshGPUData 
            { 
                VertexBuffer = vb, 
                IndexBuffer = ib, 
                IndexCount = (int)(iLen / 4),
                Submeshes = submeshes,
                RawIndices = rawIndices,
                RawVertexPositions = rawPositions
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
        /// <summary>
        /// Index of this item's EntityUniforms entry in _frameInstances.
        /// Set during the gather phase; used as firstInstance in DrawIndexed.
        /// </summary>
        public int InstanceIndex;
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
            // glTF base color is sRGB; normal + MR + opacity are linear data.
            if (!string.IsNullOrEmpty(materialAsset.AlbedoTexturePath))
                albedoTex = LoadCachedTexture(materialAsset.AlbedoTexturePath, storedInSrgb: true);
            if (!string.IsNullOrEmpty(materialAsset.NormalTexturePath))
                normalTex = LoadCachedTexture(materialAsset.NormalTexturePath, storedInSrgb: false);
            
            // RMA texture: try RMATexturePath first, fall back to RoughnessTexturePath or MetallicTexturePath
            if (!string.IsNullOrEmpty(materialAsset.RMATexturePath))
                rmaTex = LoadCachedTexture(materialAsset.RMATexturePath, storedInSrgb: false);
            else if (!string.IsNullOrEmpty(materialAsset.RoughnessTexturePath))
                rmaTex = LoadCachedTexture(materialAsset.RoughnessTexturePath, storedInSrgb: false);
            else if (!string.IsNullOrEmpty(materialAsset.MetallicTexturePath))
                rmaTex = LoadCachedTexture(materialAsset.MetallicTexturePath, storedInSrgb: false);
            
            if (!string.IsNullOrEmpty(materialAsset.OpacityTexturePath))
                opacityTex = LoadCachedTexture(materialAsset.OpacityTexturePath, storedInSrgb: false);
        }

        var submeshMaterial = new MaterialData
        {
            AlbedoAndMetallic = new System.Numerics.Vector4(
                materialAsset != null ? materialAsset.Albedo.X : DefaultAlbedo.X,
                materialAsset != null ? materialAsset.Albedo.Y : DefaultAlbedo.Y,
                materialAsset != null ? materialAsset.Albedo.Z : DefaultAlbedo.Z,
                materialAsset?.Metallic ?? 0.1f), // Lower metallic for better visibility
            Roughness = materialAsset?.Roughness ?? 0.6f, // Slightly rougher for better lighting visibility
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
        
        // CRITICAL: Use SetFragmentUniforms (Metal: setFragmentBytes:length:atIndex:)
        // instead of UpdateBuffer + SetUniformBuffer. The _materialBuffer is a SHARED
        // CPU→GPU buffer — UpdateBuffer does a CPU memcpy, and the GPU only reads
        // at execution time. Since all draws are recorded before GPU executes,
        // only the LAST UpdateBuffer write is visible → every submesh gets the same
        // material. SetFragmentUniforms pushes INLINE constant data per draw call,
        // giving each submesh its own unique snapshot of the material data.
        var matSpan = MemoryMarshal.CreateSpan(ref submeshMaterial, 1);
        cmd.SetFragmentUniforms(11, MemoryMarshal.AsBytes(matSpan));

        cmd.SetTexture(albedoTex ?? _defaultWhiteTexture!, 2);
        cmd.SetTexture(normalTex ?? _defaultNormalTexture!, 3);
        cmd.SetTexture(rmaTex ?? _defaultRmaTexture!, 4);
        cmd.SetTexture(opacityTex ?? _defaultWhiteOpacityTexture!, 5);
    }
    
    // ── Per-frame instance data staging ──────────────────────────────────────
    // Stores all entity transforms for the current frame. DrawBatched fills this
    // list during collection and UploadFrameInstances writes it to the GPU once.
    private readonly List<EntityUniforms> _frameInstances = new(256);

    /// <summary>
    /// Upload ALL instance transforms collected this frame into _frameInstanceBuffer.
    /// Must be called BEFORE DrawBatched so every draw call reads a stable, unique slice.
    /// </summary>
    private void UploadFrameInstances()
    {
        if (_frameInstances.Count == 0 || _frameInstanceBuffer == null) return;
        int count = Math.Min(_frameInstances.Count, MaxFrameInstances);
        ReadOnlySpan<EntityUniforms> span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_frameInstances).Slice(0, count);
        _device.UpdateBuffer(_frameInstanceBuffer, MemoryMarshal.AsBytes(span));
    }

    private void DrawBatched(IRHICommandBuffer cmd, System.Collections.Generic.List<RenderItem> items)
    {
        if (items.Count == 0) return;
        
        // FIX: Use _frameInstanceBuffer (uploaded ONCE before any draw calls by
        // UploadFrameInstances). Each RenderItem carries its exact InstanceIndex,
        // which becomes the firstInstance parameter in DrawIndexed, pointing the
        // GPU at that entity's unique slot in the buffer.
        cmd.SetUniformBuffer(_frameInstanceBuffer!, 30);
        
        items.Sort(CompareRenderItemsForBatching);

        for (int i = 0; i < items.Count;)
        {
            var firstItem = items[i];

            cmd.SetVertexBuffer(firstItem.GpuData.VertexBuffer!, 0);
            cmd.SetIndexBuffer(firstItem.GpuData.IndexBuffer!, IndexType.UInt32);
            BindMaterial(cmd, firstItem);

            // Emit one DrawIndexed per contiguous instance chunk.
            // firstInstance = items[i].InstanceIndex  — the exact slot in
            // _frameInstanceBuffer that was written for this entity during gather.
            int instanceCount = 1;
            while (i + instanceCount < items.Count && instanceCount < MaxInstancesPerBatch)
            {
                var next = items[i + instanceCount];
                if (!CanBatchRenderItems(firstItem, next) ||
                    next.InstanceIndex != firstItem.InstanceIndex + instanceCount)
                {
                    break;
                }

                instanceCount++;
            }

            uint firstInst = (uint)firstItem.InstanceIndex;
            cmd.DrawIndexed((uint)firstItem.Submesh.IndexCount, (uint)instanceCount,
                (uint)firstItem.Submesh.IndexOffset, 0, firstInst);

            i += instanceCount;
        }
    }

    private static int CompareRenderItemsForBatching(RenderItem a, RenderItem b)
    {
        int cmp = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a.GpuData)
            .CompareTo(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(b.GpuData));
        if (cmp != 0) return cmp;

        cmp = a.Submesh.IndexOffset.CompareTo(b.Submesh.IndexOffset);
        if (cmp != 0) return cmp;

        cmp = a.Submesh.IndexCount.CompareTo(b.Submesh.IndexCount);
        if (cmp != 0) return cmp;

        cmp = a.Submesh.MaterialSlot.CompareTo(b.Submesh.MaterialSlot);
        if (cmp != 0) return cmp;

        return RuntimeHash(a.Material).CompareTo(RuntimeHash(b.Material));
    }

    private static int RuntimeHash(object? value) =>
        value != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value) : 0;

    private static bool CanBatchRenderItems(RenderItem a, RenderItem b)
    {
        return ReferenceEquals(a.GpuData, b.GpuData)
            && a.Submesh.IndexOffset == b.Submesh.IndexOffset
            && a.Submesh.IndexCount == b.Submesh.IndexCount
            && a.Submesh.MaterialSlot == b.Submesh.MaterialSlot
            && ReferenceEquals(a.Material, b.Material);
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
        
        // Gizmo uniform buffers (one per axis + center + terrain brush preview)
        for (int i = 0; i < _gizmoUniformBuffers.Length; i++)
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
    private void RenderTerrainBrushPreview(IRHICommandBuffer cmd, System.Numerics.Matrix4x4 viewProj)
    {
        if (!_terrainBrushPreviewVisible || !_gizmoGeometryCreated || _gizmoPipeline == null ||
            _gizmoRingVB == null || _gizmoRingIB == null || _gizmoUniformBuffers.Length < 5)
            return;

        var normal = _terrainBrushPreviewNormal.LengthSquared() > 0.0001f
            ? System.Numerics.Vector3.Normalize(_terrainBrushPreviewNormal)
            : System.Numerics.Vector3.UnitY;
        var forward = MathF.Abs(System.Numerics.Vector3.Dot(normal, System.Numerics.Vector3.UnitZ)) > 0.95f
            ? System.Numerics.Vector3.UnitX
            : System.Numerics.Vector3.UnitZ;
        forward = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(System.Numerics.Vector3.Cross(normal, forward), normal));

        var liftedPosition = _terrainBrushPreviewPosition + normal * 0.035f;
        var world = System.Numerics.Matrix4x4.CreateWorld(liftedPosition, forward, normal);
        float scale = _terrainBrushPreviewRadius / 0.8f;
        var model = System.Numerics.Matrix4x4.CreateScale(scale, 0.35f, scale) * world;

        var uniforms = new GizmoUniforms
        {
            ViewProj = viewProj,
            Model = model,
            Color = BrushPreviewColor(_terrainBrushPreviewMode),
            GizmoType = 1.0f,
            AxisId = 3.0f,
            IsHovered = 1.0f,
        };

        var span = MemoryMarshal.CreateSpan(ref uniforms, 1);
        _device.UpdateBuffer(_gizmoUniformBuffers[4]!, MemoryMarshal.AsBytes(span));

        cmd.SetPipeline(_gizmoPipeline!);
        cmd.SetUniformBuffer(_gizmoUniformBuffers[4]!, 10);
        cmd.SetVertexBuffer(_gizmoRingVB!, 0);
        cmd.SetIndexBuffer(_gizmoRingIB!, IndexType.UInt16);
        cmd.DrawIndexed((uint)_gizmoRingIndexCount);
    }

    private static System.Numerics.Vector4 BrushPreviewColor(BrushMode mode) => mode switch
    {
        BrushMode.Lower => new System.Numerics.Vector4(0.30f, 0.55f, 1.00f, 0.78f),
        BrushMode.Smooth => new System.Numerics.Vector4(0.35f, 0.95f, 0.75f, 0.78f),
        BrushMode.Flatten => new System.Numerics.Vector4(1.00f, 0.85f, 0.25f, 0.82f),
        BrushMode.Noise => new System.Numerics.Vector4(0.85f, 0.55f, 1.00f, 0.80f),
        BrushMode.Erode => new System.Numerics.Vector4(1.00f, 0.52f, 0.30f, 0.80f),
        BrushMode.Erase => new System.Numerics.Vector4(1.00f, 0.25f, 0.25f, 0.82f),
        _ => new System.Numerics.Vector4(0.45f, 1.00f, 0.35f, 0.78f),
    };

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
        _terrainRenderer.Dispose();
        
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

    /// <summary>
    /// Build a mapping from submesh index to wheel slot (0-3) based on vertex centroids.
    /// </summary>
    private static int[] BuildSubmeshWheelMap(MeshGPUData gpuData, CarController carController)
    {
        // Get wheel world positions using the public API
        var wheelPositions = new System.Numerics.Vector3[4];
        
        // These correspond to: Front Left, Front Right, Rear Left, Rear Right
        // Hardcoded fallback positions (will be overridden by physics at runtime)
        wheelPositions[0] = new System.Numerics.Vector3(-0.8f, -0.3f, 1.5f);  // Front Left
        wheelPositions[1] = new System.Numerics.Vector3(0.8f, -0.3f, 1.5f);   // Front Right
        wheelPositions[2] = new System.Numerics.Vector3(-0.8f, -0.3f, -1.5f); // Rear Left
        wheelPositions[3] = new System.Numerics.Vector3(0.8f, -0.3f, -1.5f);  // Rear Right

        int[] wheelMap = new int[gpuData.Submeshes.Count];
        for (int i = 0; i < wheelMap.Length; i++) wheelMap[i] = -1;

        for (int submeshIdx = 0; submeshIdx < gpuData.Submeshes.Count; submeshIdx++)
        {
            var submesh = gpuData.Submeshes[submeshIdx];
            var centroid = ComputeSubmeshCentroid(gpuData, submesh);

            float minDist = float.MaxValue;
            int bestWheel = -1;

            for (int w = 0; w < 4; w++)
            {
                float dist = System.Numerics.Vector3.Distance(centroid, wheelPositions[w]);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestWheel = w;
                }
            }

            // Only map if reasonably close (within 2 units)
            if (bestWheel >= 0 && minDist < 2.0f)
            {
                wheelMap[submeshIdx] = bestWheel;
            }
        }

        return wheelMap;
    }

    /// <summary>
    /// Compute the centroid (average position) of all vertices in a submesh.
    /// </summary>
    private static System.Numerics.Vector3 ComputeSubmeshCentroid(MeshGPUData gpuData, SubmeshInfo submesh)
    {
        if (gpuData.RawIndices == null || gpuData.RawVertexPositions == null)
            return System.Numerics.Vector3.Zero;

        var sum = System.Numerics.Vector3.Zero;
        int count = 0;

        int indexEnd = System.Math.Min(submesh.IndexOffset + submesh.IndexCount, gpuData.RawIndices.Length);
        for (int idx = submesh.IndexOffset; idx < indexEnd; idx++)
        {
            uint vertIdx = gpuData.RawIndices[idx];
            if (vertIdx < gpuData.RawVertexPositions.Length)
            {
                sum += gpuData.RawVertexPositions[(int)vertIdx];
                count++;
            }
        }

        if (count > 0)
            sum /= count;

        return sum;
    }
}
