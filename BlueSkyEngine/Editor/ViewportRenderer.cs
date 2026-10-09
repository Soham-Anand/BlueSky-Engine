using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using BlueSky.Rendering.RHI;
using BlueSky.Rendering.RHI.DirectX11;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Math;
using BlueSky.Core.Gameplay;
using BlueSky.Motif;

namespace BlueSky.Editor;

/// <summary>
/// GPU-accelerated 3D viewport renderer.  Draws a procedural sky, an
/// infinite XZ-plane grid gizmo, and ECS entities using the RHI pipeline layer.
/// </summary>
public sealed class ViewportRenderer : IDisposable
{

    private static bool VerboseViewportLogging =>
        Environment.GetEnvironmentVariable("BLUESKY_VERBOSE_VIEWPORT") == "1";

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

        // padding to offset 352 (matches Metal/HLSL 16-byte alignment)
        private float _pad1;
        private float _pad2;
        private float _pad3;

        // float4 (NOT float3): HLSL packs float3 tightly while Metal/C# pad to
        // 16 bytes, so float3 sunDirection lands at different offsets per API.
        // xyz = direction toward sun, w unused.
        public System.Numerics.Vector4 SunDirection; // 16 bytes (offset 352)

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

    // ── Entity uniform structure (model matrix + tint) ─────────────────────────
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
    
    // ── Surface data for Astra shader (112 bytes, must match HLSL register(b2)) ───
    // Single global surface: historic orange clay. No material system.
    [StructLayout(LayoutKind.Sequential)]
    private struct AstraSurface
    {
        public System.Numerics.Vector4 BaseColor;           // 16  rgb=base, a=draw alpha
        public float Roughness;                             // 4
        public float Metallic;                              // 4
        public float AO;                                    // 4
        public float EmissiveStrength;                      // 4
        public float SpecularStrength;                      // 4
        public float Shininess;                             // 4
        public float Alpha;                                 // 4
        public uint  Flags;                                 // 4  AstraSurfaceFlags
        public System.Numerics.Vector2 UVScale;             // 8
        public System.Numerics.Vector2 UVOffset;            // 8
        public System.Numerics.Vector4 EmissiveColor;       // 16
        public System.Numerics.Vector4 Custom0;             // 16
        public System.Numerics.Vector4 Custom1;             // 16
        // Total: 16 + 32 + 4 + 16 + 48 = 112 bytes ✓
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

    // ── Physics debug shape overlay types ──────────────────────────────────────────
    public enum DebugShapeType { Box, Sphere, Capsule, ConvexHull }

    public struct DebugShape
    {
        public System.Numerics.Matrix4x4 Transform;
        public DebugShapeType ShapeType;
        public System.Numerics.Vector3 Size;
        public float Radius;
        public float Height;
        public System.Numerics.Vector4 Color;
        public System.Numerics.Vector3[]? HullVertices;
        public int[]? HullIndices;
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
        public System.Numerics.Vector4 AmbientPacked; // xyz=color, w=intensity
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

    // ── Submesh info (geometry ranges; no material system) ────────────────
    public struct SubmeshInfo
    {
        public int IndexOffset;     // Starting index in the index buffer
        public int IndexCount;      // Number of indices for this submesh
    }

    // ── Mesh GPU cache struct ─────────────────────────────────────────────
    public class MeshGPUData : IDisposable
    {
        public IRHIBuffer? VertexBuffer;
        public IRHIBuffer? IndexBuffer;
        public int IndexCount;
        public List<SubmeshInfo> Submeshes = new(); // Geometry ranges
        public List<int> SubmeshSlots = new(); // Parallel surface-slot ints (geometry org)
        public Dictionary<int, string> StrataLinks = new(); // slot → .stratamat path
        public ulong LastUsedFrame; // For LRU cache eviction

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
    private          IRHIPipeline? _wireframePipeline;
    private          IRHIPipeline? _shadowPipeline;
    private          IRHITexture?  _shadowMap;
    private          IRHIBuffer?   _uniformBuffer;
    private          IRHIBuffer?   _entityUniformBuffer;
    private          IRHIBuffer?   _instanceBuffer;
    private          const int     MaxInstancesPerBatch = 64;
    // Per-frame instance buffer: large enough for ALL entities in the scene.
    // Uploaded ONCE per frame before any draw calls so every draw can read its
    // own unique slice via the firstInstance offset — eliminates the shared-
    // buffer aliasing bug where entities would snap to each other's position.
    private          IRHIBuffer?   _frameInstanceBuffer;
    private const int MaxFrameInstances    = 256;  // DX11 HLSL entity array max (256 * 80 = 20480 bytes in cbuffer)
    private          int           _debugFrameCounter   = 0;
    
    // Horizon Lighting buffers
    private IRHIBuffer? _horizonViewUniformBuffer; // Separate buffer for Horizon shader
    private IRHIBuffer? _lightBuffer;
    private IRHIBuffer? _lightCountBuffer;
    private IRHIBuffer? _lightSettingsBuffer;
    private IRHIBuffer? _surfaceBuffer;

    // ── Strata sky-captured IBL: 9 SH coeffs, recaptured when the sun moves ──
    private IRHIBuffer? _shBuffer;
    private System.Numerics.Vector3 _shSunDir = new(float.NaN);
    private float _shPreset = float.NaN;
    private IRHITexture? _gradeLut;
    private IRHIBuffer? _skyParamsBuffer; // b17: SkyPreset float + padding
    // ── Phase-B probes: pack path caches + active probe key ────────────────
    private readonly Dictionary<string, string> _meshPackCache = new();
    private readonly Dictionary<string, System.Collections.Generic.List<BlueSky.Rendering.Strata.StrataPack.PackProbe>> _packProbeCache = new();
    private readonly Dictionary<string, System.Numerics.Vector3[]> _probeCoeffCache = new();
    private string _shProbeKey = "";
    private float _shParamsPreset = float.NaN;
    
    private readonly Dictionary<string, MeshGPUData> _meshCache = new();
    // Asset file reads can be large. Keep them off the render thread; GPU buffer
    // creation remains on the render thread when the completed asset is consumed.
    private readonly ConcurrentDictionary<string, Task<BlueSky.Core.Assets.BlueAsset?>> _pendingMeshAssets = new();

    // ── Strata override (vehicle proof): when set, every mesh renders with ──
    // this material instead of the orange clay. Textures upload lazily once.
    public BlueSky.Rendering.Strata.StrataMaterial? StrataOverride { get; set; }
    private BlueSky.Rendering.Strata.StrataSurfaceUpload.SurfaceParams _strataParams;
    private bool _strataReady;
    private string _strataReadyFor = "";
    private readonly Dictionary<string, IRHITexture> _strataTexCache = new(StringComparer.OrdinalIgnoreCase);

    // ── Wheel animation for static meshes (no skeleton) ─────────────────
    // Maps (meshAssetId, entityId) → submeshIndex→wheelIndex (-1 = not a wheel).
    // Built lazily on first encounter by analysing submesh vertex centroids
    // against the car controller's wheel positions.
    private readonly Dictionary<(string, uint), int[]> _submeshWheelMap = new();
    
    // ── Bone animation for skeletal meshes ──────────────────────────────
    // Maps (meshAssetId, entityId) → submeshIndex→boneIndex (-1 = no bone).
    // Built lazily on first encounter by checking vertex bone weights and falling back to name/hierarchy analysis.
    private readonly Dictionary<(string, uint), int[]> _skeletalSubmeshBoneMap = new();

    // ── Pack skeletons (stratapack import) ──────────────────────────────────
    // assetId → (bone names, per-submesh dominant bone index). Negative cached
    // as empty arrays: one metadata check per static mesh, then dict hits.
    private readonly Dictionary<string, (string[] BoneNames, int[] SubmeshBone)> _packSkeletonCache = new();
    private readonly HashSet<string> _wheelTraceMappings = new();

    
    // ── Texture cache (path + colorspace intent; Strata will own the intent) ──
    // Key includes sRGB flag: same file path must not be shared between color (sRGB) and data (linear) textures.
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
    // (FBX path below; pack-skeleton loader follows it.)

    /// <summary>
    /// Dominant pack-skeleton bone name for one submesh (stratapack imports).
    /// Votes skinning weights over the submesh index range; top-weight bone
    /// wins per vertex, majority wins the submesh. Cached per asset.
    /// </summary>
    private bool TryGetPackSubmeshBoneName(string assetId, MeshGPUData gpuData,
        SubmeshInfo submesh, int submeshIdx, out string boneName)
    {
        boneName = "";
        if (string.IsNullOrEmpty(assetId))
            return false;
        if (!_packSkeletonCache.TryGetValue(assetId, out var entry))
        {
            entry = LoadPackSkeletonMap(assetId, gpuData)
                ?? (Array.Empty<string>(), Array.Empty<int>());
            _packSkeletonCache[assetId] = entry;
        }
        if (entry.BoneNames.Length == 0 || submeshIdx < 0 || submeshIdx >= entry.SubmeshBone.Length)
            return false;
        int bi = entry.SubmeshBone[submeshIdx];
        if (bi < 0 || bi >= entry.BoneNames.Length)
            return false;
        boneName = entry.BoneNames[bi];
        return !string.IsNullOrEmpty(boneName);
    }

    private (string[] BoneNames, int[] SubmeshBone)? LoadPackSkeletonMap(string assetId, MeshGPUData gpuData)
    {
        // No sidecars: skin + skeleton live in the .stratapack. The asset
        // carries pack-relative references; we slice straight from the pack.
        try
        {
            var header = BlueSky.Core.Assets.BlueAsset.LoadHeader(assetId);
            if (header == null || !header.Metadata.TryGetValue("sourcePack", out var packPath) ||
                !header.Metadata.TryGetValue("skinOffset", out var skinOffS) ||
                !header.Metadata.TryGetValue("skinSize", out var skinSizeS) ||
                !header.Metadata.TryGetValue("skinCount", out var skinCountS) ||
                !header.Metadata.TryGetValue("skeletonIndex", out var skelIdxS) ||
                string.IsNullOrWhiteSpace(packPath) || !System.IO.File.Exists(packPath) ||
                !ulong.TryParse(skinOffS, out ulong skinOff) ||
                !ulong.TryParse(skinSizeS, out ulong skinSize) ||
                !int.TryParse(skinCountS, out int skinCount) ||
                !int.TryParse(skelIdxS, out int skelIdx) ||
                gpuData.RawIndices == null || gpuData.Submeshes == null)
                return null;
            var (packHeader, payloadBase) =
                BlueSky.Rendering.Strata.StrataPack.DecodeHeader(packPath);
            if (skelIdx < 0 || skelIdx >= packHeader.Skeletons.Count)
                return null;
            var skel = packHeader.Skeletons[skelIdx];
            if (skel == null || skel.Bones.Count == 0)
                return null;
            int stride = BlueSky.Rendering.Strata.StrataPack.SkinStride;
            if (skinCount <= 0 || skinSize != (ulong)skinCount * (ulong)stride)
                return null;
            byte[] skin = new byte[skinSize];
            using (var fs = new FileStream(packPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Seek(payloadBase + (long)skinOff, SeekOrigin.Begin);
                int got = 0;
                while (got < skin.Length)
                {
                    int n = fs.Read(skin, got, skin.Length - got);
                    if (n <= 0) break;
                    got += n;
                }
                if (got < skin.Length)
                    return null;
            }
            var names = new string[skel.Bones.Count];
            for (int b = 0; b < names.Length; b++) names[b] = skel.Bones[b].Name ?? "";
            var subBone = new int[gpuData.Submeshes.Count];
            for (int s = 0; s < subBone.Length; s++)
            {
                var sub = gpuData.Submeshes[s];
                var votes = new float[names.Length];
                int start = Math.Max(0, sub.IndexOffset);
                int end = Math.Min(gpuData.RawIndices.Length, start + Math.Max(0, sub.IndexCount));
                for (int i = start; i < end; i++)
                {
                    uint vid = gpuData.RawIndices[i];
                    long off = (long)vid * stride;
                    if (off < 0 || off + stride > skin.Length)
                        continue;
                    // Top-weight influence wins this vertex.
                    int best = -1; float bestW = 0f;
                    for (int k = 0; k < 4; k++)
                    {
                        uint bi = BitConverter.ToUInt32(skin, (int)off + k * 4);
                        float w = BitConverter.ToSingle(skin, (int)off + 16 + k * 4);
                        if (bi < (uint)names.Length && w > bestW) { bestW = w; best = (int)bi; }
                    }
                    if (best >= 0) votes[best] += bestW;
                }
                int win = -1; float winV = 0f;
                for (int b = 0; b < votes.Length; b++)
                    if (votes[b] > winV) { winV = votes[b]; win = b; }
                subBone[s] = win;
            }
            return (names, subBone);
        }
        catch
        {
            return null;
        }
    }
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
    internal          List<DebugShape> _physicsDebugShapes = new();
    private          IRHIBuffer?   _hullLineVB;
    private          int           _hullLineVertexCapacity;
    private          IRHIBuffer?   _gizmoCapsuleVB;
    private          IRHIBuffer?   _gizmoCapsuleIB;
    private          int           _gizmoCapsuleIndexCount;
    private          IRHIBuffer?   _gizmoSphereVB;
    private          IRHIBuffer?   _gizmoSphereIB;
    private          int           _gizmoSphereIndexCount;

    private float _elapsedTime;
    private bool  _disposed;
    private readonly TextureFormat _colorFormat;
    private readonly bool _showEditorGizmos;

    public ViewportRenderer(IRHIDevice device, World world, BlueSky.Rendering.TerrainSystem? terrainSystem = null, TextureFormat colorFormat = TextureFormat.RGBA8Unorm, bool showEditorGizmos = true)
    {
        _device = device;
        _showEditorGizmos = showEditorGizmos;
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
    /// Get cached mesh GPU data (shared mesh cache).
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
    /// Detect and remove entities with corrupted mesh data (from old import format).
    /// </summary>
    private void CleanupCorruptedMeshes()
    {
        var query = _world.CreateQuery()
            .All<TransformComponent>()
            .Any(typeof(BlueSky.Core.ECS.Builtin.StaticMeshComponent), typeof(BlueSky.Core.ECS.Builtin.SkeletalMeshComponent))
            .Build();
        var chunks = _world.GetQueryChunks(query);
        var entitiesToRemove = new List<Entity>();
        
        foreach (var chunk in chunks)
        {
            int staticMeshIndex = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.StaticMeshComponent));
            int skeletalMeshIndex = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.SkeletalMeshComponent));
            var entities = chunk.GetEntities();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                string meshId = "";
                if (staticMeshIndex >= 0 && chunk.Archetype.HasComponent(typeof(BlueSky.Core.ECS.Builtin.StaticMeshComponent)))
                {
                    var staticMesh = chunk.GetComponent<BlueSky.Core.ECS.Builtin.StaticMeshComponent>(i, staticMeshIndex);
                    meshId = staticMesh.MeshAssetId;
                }
                else if (skeletalMeshIndex >= 0 && chunk.Archetype.HasComponent(typeof(BlueSky.Core.ECS.Builtin.SkeletalMeshComponent)))
                {
                    var skeletalMesh = chunk.GetComponent<BlueSky.Core.ECS.Builtin.SkeletalMeshComponent>(i, skeletalMeshIndex);
                    meshId = skeletalMesh.MeshAssetPath;
                }
                
                if (string.IsNullOrEmpty(meshId)) continue;
                
                try
                {
                    var asset = BlueSky.Core.Assets.BlueAsset.Load(meshId);
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
    public IRHITexture? LoadCachedTexture(string path, bool storedInSrgb = false, string? baseDirectory = null)
    {
        if (string.IsNullOrEmpty(path)) 
        {
            return null;
        }

        string resolvedPath = ResolveAssetFile(path, baseDirectory) ?? path;
        var cacheKey = (resolvedPath, storedInSrgb);
        if (_textureCache.TryGetValue(cacheKey, out var cached)) 
        {
            return cached;
        }

        if (!System.IO.File.Exists(resolvedPath))
            return null;

        try
        {
            IRHITexture? tex = null;

            if (path.EndsWith(".blueskyasset", StringComparison.OrdinalIgnoreCase))
            {
                tex = LoadTextureFromBlueAsset(resolvedPath, storedInSrgb);
            }
            else
            {
                tex = LoadTextureFromRawFile(resolvedPath, storedInSrgb);
            }

            _textureCache[cacheKey] = tex;
            return tex;
        }
        catch (Exception)
        {
            return null; // Not cached — will retry next frame
        }
    }

    /// <summary>
    /// Resolves imported references after a project or asset folder has moved.
    /// Importers may store absolute paths, project-relative paths, or just a
    /// sidecar filename; all three forms are accepted here.
    /// </summary>
    private static string? ResolveAssetFile(string? path, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string normalized = path.Replace('\\', Path.DirectorySeparatorChar);
        var candidates = new List<string> { normalized };

        if (!Path.IsPathRooted(normalized))
            candidates.Add(Path.Combine(Environment.CurrentDirectory, normalized));

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            string baseDir = baseDirectory!;
            candidates.Add(Path.Combine(baseDir, normalized));
            string fileName = Path.GetFileName(normalized);
            if (!string.IsNullOrEmpty(fileName))
            {
                candidates.Add(Path.Combine(baseDir, fileName));
                candidates.Add(Path.Combine(baseDir, "Textures", fileName));
                candidates.Add(Path.Combine(Directory.GetParent(baseDir)?.FullName ?? baseDir, fileName));
            }
        }

        foreach (string candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch
            {
                // Ignore malformed stale references and continue with the
                // remaining recovery candidates.
            }
        }

        return null;
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
        BlueSky.Rendering.Strata.StrataBenchmark.RecordTextureBytes(data.Length);
        
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
        BlueSky.Rendering.Strata.StrataBenchmark.RecordTextureBytes(image.Data.Length);
        return tex;
    }

    /// <summary>
    /// Builds GPU resources for the active StrataOverride (once per material).
    /// Returns false when no override is set.
    /// </summary>
    private bool EnsureStrataOverride()
    {
        var mat = StrataOverride;
        if (mat == null)
        {
            _strataReady = false;
            _strataReadyFor = "";
            return false;
        }
        if (_strataReady && _strataReadyFor == mat.Name)
            return true;

        _strataParams = BlueSky.Rendering.Strata.StrataSurfaceUpload.BuildParams(mat);
        foreach (var kv in _strataTexCache)
            kv.Value?.Dispose();
        _strataTexCache.Clear();

        foreach (var tex in mat.Textures)
        {
            int size = (int)tex.PayloadSize;
            int off = (int)tex.PayloadOffset;
            if (size <= 0 || off < 0 || off + size > mat.Payload.Length)
            {
                Console.WriteLine($"[Strata] Texture '{tex.Slot}' range invalid — skipped.");
                continue;
            }
            var bytes = new byte[size];
            Buffer.BlockCopy(mat.Payload, off, bytes, 0, size);
            var gpu = _device.CreateTexture(new TextureDesc
            {
                Width = (uint)Math.Max(1, tex.Width),
                Height = (uint)Math.Max(1, tex.Height),
                Depth = 1,
                MipLevels = 1,
                ArrayLayers = 1,
                Format = tex.Colorspace == BlueSky.Rendering.Strata.StrataColorspace.Srgb
                    ? TextureFormat.RGBA8Srgb : TextureFormat.RGBA8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDst,
                DebugName = $"Strata_{mat.Name}_{tex.Slot}"
            });
            _device.UploadTexture(gpu, bytes);
            _strataTexCache[tex.Slot] = gpu;
        }

        _strataReady = true;
        _strataReadyFor = mat.Name;
        Console.WriteLine($"[Strata] Override '{mat.Name}' ready: mask=0x{((uint)mat.Features):X}, textures={_strataTexCache.Count}");
        return true;
    }

    private bool StrataTexture(string slot, out IRHITexture? tex)
        => _strataTexCache.TryGetValue(slot, out tex) && tex != null;

    /// <summary>
    /// Forces the global Strata override to rebuild next frame.
    /// The override cache is name-keyed; same-name mutations need this.
    /// </summary>
    public void InvalidateStrata()
    {
        _strataReady = false;
        _strataReadyFor = "";
    }

    /// <summary>
    /// Resolves a submesh to its .stratamat path via slot links.
    /// Missing link → loud log once per mesh+slot, orange clay fallback.
    /// </summary>
    private string? ResolveSubmeshStrata(string assetId, MeshGPUData gpuData, int submeshIdx)
    {
        int slot = (submeshIdx >= 0 && submeshIdx < gpuData.SubmeshSlots.Count)
            ? gpuData.SubmeshSlots[submeshIdx] : 0;
        if (gpuData.StrataLinks.TryGetValue(slot, out var path) && !string.IsNullOrWhiteSpace(path))
            return path;
        string key = $"{assetId}#{slot}";
        if (_strataMissingLogged.Add(key))
            Console.WriteLine($"[Strata] Mesh '{assetId}' slot {slot} has no .stratamat link — orange clay. Re-import to assign.");
        return null;
    }

    // ── Per-file .stratamat cache (mesh slot assignment) ────────────────────
    private readonly Dictionary<string, (BlueSky.Rendering.Strata.StrataSurfaceUpload.SurfaceParams Params,
        Dictionary<string, IRHITexture> Texs)> _strataFileCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _strataFailLogged = new();
    private readonly HashSet<string> _strataMissingLogged = new();
    private readonly HashSet<string> _strataMaskLogged = new();

    /// <summary>
    /// Loads + uploads one .stratamat file (cached by path). Loud on failure.
    /// </summary>
    private bool EnsureStrataFile(string path)
    {
        if (_strataFileCache.ContainsKey(path))
            return true;
        try
        {
            var mat = BlueSky.Rendering.Strata.StrataMaterial.Load(path);
            var pars = BlueSky.Rendering.Strata.StrataSurfaceUpload.BuildParams(mat);
            var texs = new Dictionary<string, IRHITexture>(StringComparer.OrdinalIgnoreCase);
            foreach (var tex in mat.Textures)
            {
                int size = (int)tex.PayloadSize, off = (int)tex.PayloadOffset;
                if (size <= 0 || off < 0 || off + size > mat.Payload.Length)
                    continue;
                var bytes = new byte[size];
                Buffer.BlockCopy(mat.Payload, off, bytes, 0, size);
                var gpu = _device.CreateTexture(new TextureDesc
                {
                    Width = (uint)Math.Max(1, tex.Width),
                    Height = (uint)Math.Max(1, tex.Height),
                    Depth = 1, MipLevels = 1, ArrayLayers = 1,
                    Format = tex.Colorspace == BlueSky.Rendering.Strata.StrataColorspace.Srgb
                        ? TextureFormat.RGBA8Srgb : TextureFormat.RGBA8Unorm,
                    Usage = TextureUsage.Sampled | TextureUsage.TransferDst,
                    DebugName = $"StrataFile_{mat.Name}_{tex.Slot}"
                });
                _device.UploadTexture(gpu, bytes);
                texs[tex.Slot] = gpu;
            }
            _strataFileCache[path] = (pars, texs);
            return true;
        }
        catch (Exception ex)
        {
            if (_strataFailLogged.Add(path))
                Console.WriteLine($"[Strata] .stratamat unreadable '{path}': {ex.Message} — orange clay fallback.");
            return false;
        }
    }
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
    /// Evict all cached textures and mesh data for a given mesh asset directory.
    /// Call this after re-importing a mesh.
    /// </summary>
    public void InvalidateAssetDirectory(string assetDir)
    {
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

    }

    /// <summary>
    /// Drop cached GPU mesh data for one asset so the next draw reloads submeshes from disk.
    /// </summary>
    public void InvalidateMeshGpuCache(string meshAssetId)
    {
        if (string.IsNullOrEmpty(meshAssetId)) return;
        if (!_meshCache.TryGetValue(meshAssetId, out var gpu)) return;
        gpu.Dispose();
        _meshCache.Remove(meshAssetId);
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

        var query = _world.CreateQuery()
            .All<TransformComponent>()
            .Any(typeof(BlueSky.Core.ECS.Builtin.StaticMeshComponent), typeof(BlueSky.Core.ECS.Builtin.SkeletalMeshComponent))
            .Build();
        var chunks = _world.GetQueryChunks(query);
        
        var shadowItems = new System.Collections.Generic.List<(MeshGPUData GpuData, SubmeshInfo Submesh, BlueSky.Core.Math.Matrix4x4 ModelMatrix)>();

        foreach (var chunk in chunks)
        {
            int transformIndex = chunk.GetComponentIndex(typeof(TransformComponent));
            int staticMeshIndex = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.StaticMeshComponent));
            int skeletalMeshIndex = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.SkeletalMeshComponent));
            for (int i = 0; i < chunk.Count; i++)
            {
                var transform = chunk.GetComponent<TransformComponent>(i, transformIndex);
                
                bool hasSkeletal = skeletalMeshIndex >= 0 && chunk.Archetype.HasComponent(typeof(BlueSky.Core.ECS.Builtin.SkeletalMeshComponent));
                bool hasStatic = staticMeshIndex >= 0 && chunk.Archetype.HasComponent(typeof(BlueSky.Core.ECS.Builtin.StaticMeshComponent));

                string assetId = "";
                if (hasSkeletal)
                {
                    var skeletalMesh = chunk.GetComponent<BlueSky.Core.ECS.Builtin.SkeletalMeshComponent>(i, skeletalMeshIndex);
                    assetId = skeletalMesh.MeshAssetPath;
                }
                else if (hasStatic)
                {
                    var staticMesh = chunk.GetComponent<BlueSky.Core.ECS.Builtin.StaticMeshComponent>(i, staticMeshIndex);
                    assetId = staticMesh.MeshAssetId;
                }

                if (string.IsNullOrEmpty(assetId) || !_meshCache.TryGetValue(assetId, out var gpuData)) continue;
                
                // Ensure submeshes list is initialized
                if (gpuData.Submeshes == null || gpuData.Submeshes.Count == 0)
                {
                    gpuData.Submeshes = new List<SubmeshInfo>
                    {
                        new SubmeshInfo { IndexOffset = 0, IndexCount = gpuData.IndexCount }
                    };
                }

                uint entityId = (uint)chunk.GetEntities()[i].Id;
                var carController = BlueSky.Core.Gameplay.CarControllerSystem.GetController(entityId);

                for (int submeshIdx = 0; submeshIdx < gpuData.Submeshes.Count; submeshIdx++)
                {
                    var submesh = gpuData.Submeshes[submeshIdx];
                    if (submesh.IndexCount == 0) continue;

                    // Transparent surfaces cast no shadow (glass must not blob).
                    // Cache-backed: dict hits after the first frame.
                    string? shadowStrata = ResolveSubmeshStrata(assetId, gpuData, submeshIdx);
                    if (shadowStrata != null && EnsureStrataFile(shadowStrata) &&
                        _strataFileCache.TryGetValue(shadowStrata, out var shadowFile) &&
                        (shadowFile.Params.FeatureMask &
                            (uint)BlueSky.Rendering.Strata.StrataFeature.Transparent) != 0)
                        continue;

                    var modelMatrix = transform.WorldMatrix;

                    if (carController != null)
                    {
                        var cacheKey = (assetId, entityId);
                        modelMatrix = ApplyWheelTransformIfApplicable(gpuData, submesh, submeshIdx, carController, cacheKey, modelMatrix);
                    }

                    shadowItems.Add((gpuData, submesh, modelMatrix));
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
                        Model = ToSystemMatrix4x4(shadowItems[si].ModelMatrix),
                        Color = System.Numerics.Vector4.One
                    });
            }
            // ── FL10.1 / Intel HD 3000 COMPAT FIX ───────────────────────────────
            // D3D11 FL10.x ignores the FirstInstance argument of DrawIndexedInstanced
            // (only honored at FL11.0+). So instead of binding the full instance buffer
            // and using firstInstance = shadowIndices[idx], we rebind a tiny per-draw
            // instance buffer holding ONLY this item's transform, and draw with
            // firstInstance = 0. The shadow VS reads EntityUniforms[SV_InstanceID].
            foreach (var (item, idx) in shadowItems.Select((it, ix) => (it, ix)))
            {
                int slot = shadowIndices[idx];
                if (_instanceBuffer != null && slot < shadowInstances.Count)
                {
                    var one = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(shadowInstances)
                        .Slice(slot, 1);
                    _device.UpdateBuffer(_instanceBuffer, MemoryMarshal.AsBytes(one));
                    cmd.SetUniformBuffer(_instanceBuffer, 12);
                }

                cmd.SetVertexBuffer(item.GpuData.VertexBuffer!, 0);
                cmd.SetIndexBuffer(item.GpuData.IndexBuffer!, IndexType.UInt32);
                BlueSky.Rendering.Strata.StrataBenchmark.RecordShadowDraw();

                cmd.DrawIndexed((uint)item.Submesh.IndexCount, 1, (uint)item.Submesh.IndexOffset, 0, 0);
            }
        }

        cmd.EndRenderPass();
    }

    private static readonly System.Numerics.Vector3 DefaultAlbedo = new(0.95f, 0.5f, 0.2f); // Historic orange clay (pre-material days)
    
    /// <summary>Uploads 9 SH coeffs to the b16 probe buffer.</summary>
    private void UploadShCoeffs(System.Numerics.Vector3[] coeffs)
    {
        if (_shBuffer == null || coeffs == null || coeffs.Length < 9)
            return;
        var shBytes = new byte[9 * 16];
        for (int i = 0; i < 9; i++)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(coeffs[i].X), 0, shBytes, i * 16, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(coeffs[i].Y), 0, shBytes, i * 16 + 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(coeffs[i].Z), 0, shBytes, i * 16 + 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(1f), 0, shBytes, i * 16 + 12, 4);
        }
        _device.UpdateBuffer(_shBuffer, shBytes);
    }

    /// <summary>
    /// Phase-B probe lookup: aggregates probes across every pack referenced
    /// by loaded meshes, selects nearest-in-radius to the camera, projects
    /// its samples (cached per probe). Returns "" + null when no probe
    /// covers the camera (sky fallback). Pack stays the database throughout.
    /// </summary>
    private string TryGetProbeKey(System.Numerics.Vector3 cameraPos,
        out System.Numerics.Vector3[]? coeffs)
    {
        coeffs = null;
        var keys = new System.Collections.Generic.List<string>();
        var flat = new System.Collections.Generic.List<BlueSky.Rendering.Strata.StrataPack.PackProbe>();
        foreach (var assetId in _meshCache.Keys)
        {
            if (!_meshPackCache.TryGetValue(assetId, out var packPath))
            {
                packPath = "";
                try
                {
                    var h = BlueSky.Core.Assets.BlueAsset.LoadHeader(assetId);
                    if (h != null && h.Metadata.TryGetValue("sourcePack", out var sp) &&
                        !string.IsNullOrWhiteSpace(sp))
                        packPath = sp;
                }
                catch { }
                _meshPackCache[assetId] = packPath;
            }
            if (string.IsNullOrEmpty(packPath))
                continue;
            if (!_packProbeCache.TryGetValue(packPath, out var probes))
            {
                probes = new System.Collections.Generic.List<BlueSky.Rendering.Strata.StrataPack.PackProbe>();
                try
                {
                    var decoded = BlueSky.Rendering.Strata.StrataPack.DecodeHeader(packPath);
                    if (decoded.Header.Probes != null)
                        probes.AddRange(decoded.Header.Probes);
                }
                catch { }
                _packProbeCache[packPath] = probes;
            }
            for (int i = 0; i < probes.Count; i++)
            {
                keys.Add(packPath + "#" + probes[i].Name);
                flat.Add(probes[i]);
            }
        }
        if (flat.Count == 0)
            return "";
        int sel = BlueSky.Rendering.Strata.StrataSkyProbe.SelectProbe(flat, cameraPos);
        if (sel < 0)
            return "";
        string key = keys[sel];
        var pr = flat[sel];
        if (!_probeCoeffCache.TryGetValue(key, out var c))
        {
            int n = pr.SampleDirs.Length / 3;
            c = BlueSky.Rendering.Strata.StrataSkyProbe.ProjectSamples(pr.SampleDirs, pr.SampleColors, n);
            _probeCoeffCache[key] = c;
        }
        coeffs = c;
        return key;
    }

public void Render(IRHICommandBuffer cmd, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 proj,
    System.Numerics.Vector3 cameraPos, int viewportX, int viewportY, int viewportW, int viewportH, float deltaTime)
{
    _frameCount++;
    _elapsedTime += deltaTime;

    
    
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
    SunDirection = new System.Numerics.Vector4(sunDirectionValue, 0.0f),
    WindParams = BlueSky.Core.WorldEnvironment.GlobalEnvironment.WindParams
};

var uniformSpan = MemoryMarshal.CreateSpan(ref uniforms, 1);
    _device.UpdateBuffer(_uniformBuffer!, MemoryMarshal.AsBytes(uniformSpan));
    if (_frameCount == 1 || _frameCount % 900 == 0)
        Console.WriteLine($"[Viewport] sun=({sunDirectionValue.X:F3},{sunDirectionValue.Y:F3},{sunDirectionValue.Z:F3}) cam=({cameraPos.X:F1},{cameraPos.Y:F1},{cameraPos.Z:F1})");

        // ── Strata sky capture: recapture SH when sun or preset changes ──────
        // Phase-B probes override the sky when the camera stands inside one:
        // same b16 buffer, renderer agnostic to coefficient origin.
        float skyPreset = Math.Clamp(
            BlueSky.Core.WorldEnvironment.GlobalEnvironment.SkyPreset, 0f, 1f);
        string probeKey = TryGetProbeKey(cameraPos, out var probeCoeffs);
        if (probeCoeffs != null)
        {
            if (_shBuffer != null && probeKey != _shProbeKey)
            {
                UploadShCoeffs(probeCoeffs);
                _shProbeKey = probeKey;
            }
            _shSunDir = sunDirectionValue;
            _shPreset = skyPreset;
        }
        else
        {
            _shProbeKey = "";
            if (_shBuffer != null && (float.IsNaN(_shSunDir.X) || float.IsNaN(_shPreset) ||
                System.Numerics.Vector3.DistanceSquared(sunDirectionValue, _shSunDir) > 1e-8f ||
                Math.Abs(skyPreset - _shPreset) > 1e-6f))
            {
                var coeffs = BlueSky.Rendering.Strata.StrataSkyCapture.CaptureCoefficients(
                    sunDirectionValue, skyPreset);
                UploadShCoeffs(coeffs);
                _shSunDir = sunDirectionValue;
                _shPreset = skyPreset;
            }
        }
        // b17 sky params follow the preset independently of the SH source:
        // fs_sky and the env sampler read it every frame.
        if (_skyParamsBuffer != null && (float.IsNaN(_shParamsPreset) ||
            Math.Abs(skyPreset - _shParamsPreset) > 1e-6f))
        {
            var skyBytes = new byte[16];
            Buffer.BlockCopy(BitConverter.GetBytes(skyPreset), 0, skyBytes, 0, 4);
            _device.UpdateBuffer(_skyParamsBuffer, skyBytes);
            _shParamsPreset = skyPreset;
        }

        // ── Strata grade LUT (generated once, display-referred 16³ strip) ────
        if (_gradeLut == null)
        {
            byte[] lut = BlueSky.Rendering.Strata.StrataLut.Generate();
            _gradeLut = _device.CreateTexture(new TextureDesc
            {
                Width = (uint)BlueSky.Rendering.Strata.StrataLut.StripWidth,
                Height = (uint)BlueSky.Rendering.Strata.StrataLut.StripHeight,
                Depth = 1,
                MipLevels = 1,
                ArrayLayers = 1,
                Format = TextureFormat.RGBA8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDst,
                DebugName = "Strata.GradeLUT"
            });
            _device.UploadTexture(_gradeLut, lut);
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
        lightDataArray.Clear();
        int lightCount = 0;
        foreach (var entity in _world.GetAllEntities())
        {
            if (!_world.HasComponent<LightComponent>(entity)) continue;

            var light = _world.GetComponent<LightComponent>(entity);
            var transform = _world.HasComponent<TransformComponent>(entity)
                ? _world.GetComponent<TransformComponent>(entity)
                : TransformComponent.Default;
            var direction = transform.Forward;
            var directionLength = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
            if ((light.Type is LightComponent.LightType.Directional or LightComponent.LightType.Spot) && directionLength <= 1e-5f)
                continue;
            if (lightCount >= lightDataArray.Length) break;

            float outerAngle = Math.Clamp(light.SpotAngle, 1.0f, 179.0f) * (MathF.PI / 360.0f);
            lightDataArray[lightCount++] = new LightData
            {
                Position = new System.Numerics.Vector3(transform.Position.X, transform.Position.Y, transform.Position.Z),
                Range = light.Type == LightComponent.LightType.Directional ? float.MaxValue : Math.Max(light.Range, 0.001f),
                Direction = new System.Numerics.Vector3(direction.X, direction.Y, direction.Z) / Math.Max(directionLength, 1e-5f),
                Intensity = Math.Max(light.Intensity, 0.0f),
                Color = new System.Numerics.Vector3(light.Color.X, light.Color.Y, light.Color.Z),
                Type = light.Type switch
                {
                    LightComponent.LightType.Directional => 0,
                    LightComponent.LightType.Point => 1,
                    LightComponent.LightType.Spot => 2,
                    _ => 1,
                },
                InnerAngle = outerAngle * 0.8f,
                OuterAngle = outerAngle,
                Attenuation = 0.1f,
                CastShadows = light.CastsShadows ? 1 : 0,
                Volumetric = 0,
            };
        }

        // Keep a useful daylight default for scenes that have no explicit lights.
        if (lightCount == 0)
        {
            lightDataArray[0] = new LightData
            {
                Position = System.Numerics.Vector3.Zero,
                Range = 1000f,
                Direction = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.3f, 0.7f, 0.4f)),
                Intensity = 3.5f,
                Color = new System.Numerics.Vector3(1.0f, 0.98f, 0.95f),
                Type = 0,
                Attenuation = 0.1f,
                CastShadows = 1,
            };
            lightCount = 1;
        }
        
        _device.UpdateBuffer(_lightBuffer!, MemoryMarshal.AsBytes(lightDataArray));
        
        // Update light count
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
            AmbientPacked = new System.Numerics.Vector4(0.15f, 0.18f, 0.22f, 1.0f), // color + intensity
        };
        _debugFrameCounter++;
        var lightSettingsSpan = MemoryMarshal.CreateSpan(ref lightSettings, 1);
        _device.UpdateBuffer(_lightSettingsBuffer!, MemoryMarshal.AsBytes(lightSettingsSpan));
        
        // Default surface data (strata override when active, else orange clay)
        EnsureStrataOverride();
        var surface = new AstraSurface
        {
            BaseColor = _strataReady
                ? new System.Numerics.Vector4(
                    _strataParams.BaseColor.X, _strataParams.BaseColor.Y, _strataParams.BaseColor.Z, 1.0f)
                : new System.Numerics.Vector4(DefaultAlbedo, 1.0f),
            Roughness = _strataReady ? _strataParams.Roughness : 0.6f,
            Metallic = _strataReady ? _strataParams.Metallic : 0.0f,
            AO = _strataReady ? _strataParams.AO : 1.0f,
            EmissiveStrength = 0.0f,
            SpecularStrength = 0.5f,
            Shininess = 32.0f,
            Alpha = _strataReady ? _strataParams.Alpha : 1.0f,
            // Bits 14-15 carry the transient debug-view selector (0 when Lit).
            Flags = (uint)(AstraSurfaceFlags.ReceiveShadow | AstraSurfaceFlags.CastShadow)
                  | ((uint)(Program._debugView & 7) << 14),
            UVScale = new System.Numerics.Vector2(1, 1),
            UVOffset = new System.Numerics.Vector2(0, 0),
            EmissiveColor = new System.Numerics.Vector4(0, 0, 0, 1),
            Custom0 = _strataReady
                ? new System.Numerics.Vector4(1, _strataParams.Wrap, _strataParams.DetailTile, _strataParams.ToksvigK)
                : new System.Numerics.Vector4(1, 0, 0, 0),
            Custom1 = _strataReady
                ? new System.Numerics.Vector4(_strataParams.FeatureMask,
                    _strataParams.ClearcoatTintLinear.X,
                    _strataParams.ClearcoatTintLinear.Y,
                    _strataParams.ClearcoatTintLinear.Z)
                : new System.Numerics.Vector4(0, 1, 1, 1),
        };
        var surfaceSpan = MemoryMarshal.CreateSpan(ref surface, 1);
        _device.UpdateBuffer(_surfaceBuffer!, MemoryMarshal.AsBytes(surfaceSpan));

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
                _defaultRmaTexture, _defaultWhiteOpacityTexture,
                _defaultWhiteTexture, _defaultNormalTexture, _gradeLut, _defaultWhiteTexture,
                Program._debugView, viewProj, cameraPos);
        }
        else
        {
        }

        // ── 3. Entities (BEFORE grid for correct transparency) ───────────
        // Clear per-frame instance list so we start fresh; RenderEntities will
        // repopulate it and upload it once before any draw calls.
        _frameInstances.Clear();
        RenderEntities(cmd, view, proj, cameraPos);

        // ── 4. Grid (AFTER opaque world geometry for proper alpha blending) ───────────
        if (_showEditorGizmos && _gridPipeline != null)
        {
            cmd.SetPipeline(_gridPipeline);
            cmd.SetUniformBuffer(_uniformBuffer!, 10);
            cmd.Draw(6); // fullscreen quad (2 tris)
        }
        
        // ── 5. Editor Gizmos (LAST — always on top) ──────────────────────
        RenderTerrainBrushPreview(cmd, viewProj);
        RenderGizmos(cmd, viewProj, cameraPos);
        RenderPhysicsDebugShapes(cmd, viewProj);
    }

    // ── Pipeline creation ───────────────────────────────────────────────

    private IRHIPipeline? TryCreatePipeline(GraphicsPipelineDesc desc)
    {
        if (desc.VertexShader.Bytecode == null || desc.VertexShader.Bytecode.Length == 0)
        {
            Console.WriteLine($"[DX11] Pipeline '{desc.DebugName}' skipped — vertex shader bytecode empty");
            return null;
        }
        if (desc.FragmentShader.Bytecode == null || desc.FragmentShader.Bytecode.Length == 0)
        {
            Console.WriteLine($"[DX11] Pipeline '{desc.DebugName}' skipped — pixel shader bytecode empty");
            return null;
        }
        return _device.CreateGraphicsPipeline(desc);
    }

    private void CreatePipelines()
    {
        // Sky pipeline — no depth, draws behind everything
        _skyPipeline = TryCreatePipeline(new GraphicsPipelineDesc
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
        _gridPipeline = TryCreatePipeline(new GraphicsPipelineDesc
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
        _meshPipeline = TryCreatePipeline(new GraphicsPipelineDesc
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
        _transparentMeshPipeline = TryCreatePipeline(new GraphicsPipelineDesc
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

        // Wireframe pipeline — super thin outline for 3D depth perception
        _wireframePipeline = TryCreatePipeline(new GraphicsPipelineDesc
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
        _shadowPipeline = TryCreatePipeline(new GraphicsPipelineDesc
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
            _gizmoPipeline = TryCreatePipeline(new GraphicsPipelineDesc
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
        }
        catch (Exception)
        {
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
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Editor", "Shaders", baseName + ".metallib"),
                System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Editor", "Shaders", baseName + ".metallib"),
                System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "BlueSkyEngine", "Editor", "Shaders", baseName + ".metallib"),
            };

            string? found = System.Array.Find(searchPaths, System.IO.File.Exists);
            if (found != null)
            {
                bytecode = System.IO.File.ReadAllBytes(found);
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
                    // Exe-relative paths (self-contained / publish builds)
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shaders", csoFileName),
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Editor", "Shaders", csoFileName),
                    // CWD-relative paths (dev builds / dotnet run)
                    System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Editor", "Shaders", csoFileName),
                    System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "BlueSkyEngine", "Editor", "Shaders", csoFileName),
                    // Parent of exe dir (publish layout puts .cso one level up)
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Editor", "Shaders", csoFileName),
                };

                string? found = System.Array.Find(searchPaths, System.IO.File.Exists);
                if (found != null)
                {
                    bytecode = System.IO.File.ReadAllBytes(found);
                    string magic = bytecode.Length >= 4
                        ? $"0x{bytecode[0]:X2}0x{bytecode[1]:X2}0x{bytecode[2]:X2}0x{bytecode[3]:X2}"
                        : "N/A";
                    Console.WriteLine($"[DX11] .cso loaded: '{csoFileName}' path={found} size={bytecode.Length}b first4=[{magic}]");
                }
                else
                {
                    Console.WriteLine($"[DX11] .cso NOT FOUND: '{csoFileName}' — falling back to D3DCompile");
                }
            }

            // Fallback: if no .cso found, compile HLSL at runtime with D3DCompile
            if (bytecode.Length == 0)
            {
                Console.WriteLine($"[DX11] D3DCompile fallback for '{entryPoint}' stage={stage}");
                bytecode = CompileHLSLAtRuntime(stage, entryPoint);
                if (bytecode.Length > 0)
                    Console.WriteLine($"[DX11] D3DCompile fallback OK: '{entryPoint}' → {bytecode.Length} bytes");
                else
                    Console.WriteLine($"[DX11] D3DCompile fallback FAILED: '{entryPoint}'");
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

    /// <summary>
    /// Compile HLSL at runtime via D3DCompile when no .cso files are available.
    /// </summary>
    private byte[] CompileHLSLAtRuntime(ShaderStage stage, string entryPoint)
    {
        try
        {
            // Determine which HLSL file and shader target
            string hlslFile, target;
            if (entryPoint.Contains("ui"))
            {
                hlslFile = "simple_ui.hlsl";
            }
            else
            {
                hlslFile = "viewport_3d.hlsl";
            }

            if (stage == ShaderStage.Vertex)
                target = "vs_4_1";
            else
                target = "ps_4_1";

            Console.WriteLine($"[DX11] CompileHLSLAtRuntime: entry='{entryPoint}' stage={stage} target={target} hlslFile={hlslFile}");

            // Search for the HLSL source file
            string[] hlslSearchPaths = new[]
            {
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Editor", "Shaders", hlslFile),
                System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Editor", "Shaders", hlslFile),
                System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "BlueSkyEngine", "Editor", "Shaders", hlslFile),
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Editor", "Shaders", hlslFile),
            };

            string? hlslPath = System.Array.Find(hlslSearchPaths, System.IO.File.Exists);
            if (hlslPath == null)
            {
                Console.WriteLine($"[DX11] HLSL NOT FOUND for '{entryPoint}'. Searched:");
                foreach (var p in hlslSearchPaths)
                    Console.WriteLine($"  - {p} (exists={System.IO.File.Exists(p)})");
                return Array.Empty<byte>();
            }

            Console.WriteLine($"[DX11] HLSL found: {hlslPath}");

            string hlslSource = System.IO.File.ReadAllText(hlslPath);
            IntPtr pSrc = Marshal.StringToHGlobalAnsi(hlslSource);
            IntPtr pCode = IntPtr.Zero, pErrors = IntPtr.Zero;

            int hr = D3D11Interop.D3DCompile(
                pSrc,
                (nuint)System.Text.Encoding.UTF8.GetByteCount(hlslSource),
                hlslPath,
                IntPtr.Zero, IntPtr.Zero,
                entryPoint, target,
                0, 0,
                out pCode, out pErrors);

            Marshal.FreeHGlobal(pSrc);

            if (pErrors != IntPtr.Zero)
            {
                string? errMsg = Marshal.PtrToStringAnsi(D3D11Interop.GetBufferPointer(pErrors));
                if (!string.IsNullOrEmpty(errMsg))
                    Console.WriteLine($"[DX11] D3DCompile errors for '{entryPoint}':\n{errMsg}");
                Marshal.Release(pErrors);
            }

            if (hr < 0 || pCode == IntPtr.Zero)
            {
                Console.WriteLine($"[DX11] D3DCompile FAILED for '{entryPoint}' (hr=0x{hr:X8})");
                return Array.Empty<byte>();
            }

            nuint size = D3D11Interop.GetBufferSize(pCode);
            IntPtr pData = D3D11Interop.GetBufferPointer(pCode);
            byte[] result = new byte[size];
            Marshal.Copy(pData, result, 0, (int)size);
            Marshal.Release(pCode);

            // Validate DXBC magic (first 4 bytes should be 'D','X','B','C')
            if (result.Length >= 4 && result[0] == 'D' && result[1] == 'X' && result[2] == 'B' && result[3] == 'C')
                Console.WriteLine($"[DX11] D3DCompile OK: '{entryPoint}' → {result.Length} bytes (valid DXBC)");
            else
                Console.WriteLine($"[DX11] D3DCompile WARNING: '{entryPoint}' → {result.Length} bytes (NOT DXBC! first4=[{result[0]:X2},{result[1]:X2},{result[2]:X2},{result[3]:X2}])");

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DX11] CompileHLSLAtRuntime EXCEPTION for '{entryPoint}': {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<byte>();
        }
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
            Size       = (ulong)Marshal.SizeOf<EntityUniforms>() * MaxFrameInstances,
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
        
        _surfaceBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = (ulong)Marshal.SizeOf<AstraSurface>(),
            Usage      = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "Viewport.SurfaceUB",
        });

        _shBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = 9 * 16, // 9 SH coeffs × float4
            Usage      = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "Viewport.SHProbe",
        });

        _skyParamsBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size       = 16, // SkyPreset float + 12B padding
            Usage      = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName  = "Viewport.SkyParams",
        });
    }


    private void RenderEntities(IRHICommandBuffer cmd, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 proj, System.Numerics.Vector3 cameraPos)
    {
        BlueSky.Rendering.Strata.StrataBenchmark.BeginSection("mesh");
        try
        {
        // Bind buffers for fs_mesh (viewport_3d.metal)
        // CRITICAL: fs_mesh uses the FULL ViewUniforms struct (with sunDirection, windParams)
        // NOT the smaller HorizonViewUniforms. Binding the wrong struct here was causing
        // garbage sunDirection → zero direct lighting → black models.
        // Buffer 10: ViewUniforms (full struct with sunDirection for light calculation)
        cmd.SetUniformBuffer(_uniformBuffer!, 10);
        // Buffer 13: LightData* (fs_mesh expects slot 13)
        cmd.SetUniformBuffer(_lightBuffer!, 13);
        // Buffer 14: int lightCount
        cmd.SetUniformBuffer(_lightCountBuffer!, 14);
        // Buffer 15: LightingSettings
        cmd.SetUniformBuffer(_lightSettingsBuffer!, 15);
        // Buffer 16: Strata sky SH probe (9 float4)
        if (_shBuffer != null)
            cmd.SetUniformBuffer(_shBuffer, 16);
        // Buffer 17: sky params (SkyPreset float)
        if (_skyParamsBuffer != null)
            cmd.SetUniformBuffer(_skyParamsBuffer, 17);

        // Extract frustum planes from ViewProj for CPU culling
        var viewProj = view * proj;
        Span<System.Numerics.Vector4> frustumPlanes = stackalloc System.Numerics.Vector4[6];
        ExtractFrustumPlanes(viewProj, frustumPlanes);

        var query = _world.CreateQuery()
            .All<TransformComponent>()
            .Any(typeof(BlueSky.Core.ECS.Builtin.StaticMeshComponent), typeof(BlueSky.Core.ECS.Builtin.SkeletalMeshComponent))
            .Build();

        // 1. Gather all submeshes to be rendered
        var opaqueItems = new List<RenderItem>();
        var transparentItems = new List<RenderItem>();

        var chunks = _world.GetQueryChunks(query);
        foreach (var chunk in chunks)
        {
            int transformIndex = chunk.GetComponentIndex(typeof(TransformComponent));
            int staticMeshIndex = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.StaticMeshComponent));
            int skeletalMeshIndex = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.SkeletalMeshComponent));

            bool chunkHasStatic = staticMeshIndex >= 0 && chunk.Archetype.HasComponent(typeof(BlueSky.Core.ECS.Builtin.StaticMeshComponent));
            bool chunkHasSkeletal = skeletalMeshIndex >= 0 && chunk.Archetype.HasComponent(typeof(BlueSky.Core.ECS.Builtin.SkeletalMeshComponent));

            for (int i = 0; i < chunk.Count; i++)
            {
                var transform = chunk.GetComponent<TransformComponent>(i, transformIndex);
                
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

                // Resolve asset ID from whichever mesh component is present
                string assetId = "";

                if (chunkHasSkeletal)
                {
                    var skeletalMesh = chunk.GetComponent<BlueSky.Core.ECS.Builtin.SkeletalMeshComponent>(i, skeletalMeshIndex);
                    assetId = skeletalMesh.MeshAssetPath;
                }
                else if (chunkHasStatic)
                {
                    var staticMesh = chunk.GetComponent<BlueSky.Core.ECS.Builtin.StaticMeshComponent>(i, staticMeshIndex);
                    assetId = staticMesh.MeshAssetId;
                }

                if (string.IsNullOrEmpty(assetId))
                {
                    continue;
                }

                if (!_meshCache.TryGetValue(assetId, out var gpuData))
                {
                    // Do not block the viewport frame on disk I/O / JSON decoding.
                    // The asset is uploaded to the GPU on this thread once available.
                    var load = _pendingMeshAssets.GetOrAdd(assetId,
                        id => Task.Run(() => BlueSky.Core.Assets.BlueAsset.Load(id)));
                    if (!load.IsCompleted) continue;
                    _pendingMeshAssets.TryRemove(assetId, out _);
                    BlueSky.Core.Assets.BlueAsset? loadedAsset = null;
                    try { loadedAsset = load.GetAwaiter().GetResult(); }
                    catch { /* LoadGpuMesh handles invalid/missing assets as unavailable. */ }
                    gpuData = LoadGpuMesh(assetId, loadedAsset);
                    if (gpuData == null && chunkHasSkeletal)
                    {
                        if (_frameCount % 120 == 0)
                            Console.WriteLine($"[ViewportRenderer] LoadGpuMesh FAILED for skeletal mesh: {assetId}");
                    }
                }

                if (gpuData != null)
                {
                    if (_frameCount <= 3 && chunkHasSkeletal)
                        Console.WriteLine($"[ViewportRenderer] SKELETAL ENTITY FOUND: asset={assetId} submeshes={gpuData.Submeshes.Count} verts={gpuData.RawVertexPositions?.Length ?? 0}");

                    gpuData.LastUsedFrame = _frameCount;
                    float distSq = System.Numerics.Vector3.DistanceSquared(cameraPos, entityPos);

                    for (int submeshIdx = 0; submeshIdx < gpuData.Submeshes.Count; submeshIdx++)
                    {
                        var submesh = gpuData.Submeshes[submeshIdx];
                        if (submesh.IndexCount == 0) continue;

                        // No material system: every surface shades with the global
                        // historic orange clay. Lighting still varies per pixel.

                        // Record the instance index DURING gather so DrawBatched can
                        // use it as firstInstance — no matrix-equality search needed.
                        int instIdx = _frameInstances.Count;
                        string? strataPath = ResolveSubmeshStrata(assetId, gpuData, submeshIdx);
                        var tint = new System.Numerics.Vector4(DefaultAlbedo, 1.0f);
                        uint itemMask = 0u;
                        if (strataPath != null && EnsureStrataFile(strataPath) &&
                            _strataFileCache.TryGetValue(strataPath, out var tintFile))
                        {
                            tint = new System.Numerics.Vector4(
                                tintFile.Params.BaseColor.X, tintFile.Params.BaseColor.Y,
                                tintFile.Params.BaseColor.Z, 1.0f);
                            itemMask = tintFile.Params.FeatureMask;
                        }
                        else if (_strataReady)
                        {
                            tint = new System.Numerics.Vector4(
                                _strataParams.BaseColor.X, _strataParams.BaseColor.Y, _strataParams.BaseColor.Z, 1.0f);
                            itemMask = _strataParams.FeatureMask;
                        }
                        var color = tint;
                        
                        var modelMatrix = transform.WorldMatrix;
                        
                        // Check if this entity has a CarController (for wheel steering/spinning animation)
                        var carController = BlueSky.Core.Gameplay.CarControllerSystem.GetController((uint)chunk.GetEntities()[i].Id);
                        if (carController != null)
                        {
                            uint entId = (uint)chunk.GetEntities()[i].Id;
                            var cacheKey = (assetId, entId);
                            modelMatrix = ApplyWheelTransformIfApplicable(gpuData, submesh, submeshIdx, carController, cacheKey, modelMatrix);
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
                            GpuData = gpuData,
                            Submesh = submesh,
                            StrataPath = strataPath,
                            DistanceToCameraSq = distSq,
                            InstanceIndex = instIdx
                        };

                        item.Flags = AstraSurfaceFlags.ReceiveShadow | AstraSurfaceFlags.CastShadow;
                        // Transparent-bit materials join the back-to-front blend pass;
                        // everything else stays in the opaque batch.
                        if ((itemMask & (uint)BlueSky.Rendering.Strata.StrataFeature.Transparent) != 0)
                            transparentItems.Add(item);
                        else
                            opaqueItems.Add(item);
                    }
                }
            }
        }

        if (_frameCount == 1 && opaqueItems.Count == 0 && transparentItems.Count == 0)
        {
            Console.WriteLine($"[ViewportRenderer] ⚠️  Frame 1: NO render items! Camera=({cameraPos.X:F1},{cameraPos.Y:F1},{cameraPos.Z:F1}) Chunks={chunks.Count}");
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
        cmd.SetPipeline(_transparentMeshPipeline ?? _meshPipeline!);

        // DrawBatched re-sorts for batching by default; the transparent list
        // must keep its far-to-near order (batching still merges runs).
        DrawBatched(cmd, transparentItems, keepOrder: true);

        // Evict old meshes if cache is too large (VRAM optimization)
        if (_frameCount % 60 == 0 && _meshCache.Count > 64)
        {
            EvictOldMeshes(64);
        }
        }
        finally
        {
            BlueSky.Rendering.Strata.StrataBenchmark.EndSection("mesh");
        }
    }

    private MeshGPUData? LoadGpuMesh(string assetId)
        => LoadGpuMesh(assetId, BlueSky.Core.Assets.BlueAsset.Load(assetId));

    private MeshGPUData? LoadGpuMesh(string assetId, BlueSky.Core.Assets.BlueAsset? asset)
    {
        try
        {
            if (asset == null)
            {
                return null;
            }
            if (asset.PayloadData == null)
            {
                return null;
            }

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
            var submeshSlots = new List<int>();
            try
            {
                int submeshCount = reader.ReadInt32();
                for (int s = 0; s < submeshCount; s++)
                {
                    int indexOffset = reader.ReadInt32();
                    int indexCount = reader.ReadInt32();
                    int slot = reader.ReadInt32(); // surface slot: kept for .stratamat links
                    int totalIndices = (int)(iLen / sizeof(uint));
                    // Never pass malformed ranges through to DrawIndexed. Check by
                    // subtraction so offset + count cannot overflow.
                    if (indexOffset < 0 || indexCount < 0 || indexOffset > totalIndices ||
                        indexCount > totalIndices - indexOffset)
                        continue;
                    submeshes.Add(new SubmeshInfo
                    {
                        IndexOffset = indexOffset,
                        IndexCount = indexCount
                    });
                    submeshSlots.Add(slot);
                }
            }
            catch
            {
                submeshes.Clear();
                submeshSlots.Clear();
                submeshes.Add(new SubmeshInfo { IndexOffset = 0, IndexCount = (int)(iLen / 4) });
                submeshSlots.Add(0);
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
                SubmeshSlots = submeshSlots,
                RawIndices = rawIndices,
                RawVertexPositions = rawPositions
            };

            // Strata slot links live in the mesh header (written at import).
            var linkHeader = BlueSky.Core.Assets.BlueAsset.LoadHeader(assetId);
            if (linkHeader != null)
            {
                for (int s = 0; s < 64; s++)
                {
                    if (linkHeader.Metadata.TryGetValue($"strataSlot{s}", out var link)
                        && !string.IsNullOrWhiteSpace(link))
                        gpuData.StrataLinks[s] = link;
                }
            }
            
            _meshCache[assetId] = gpuData;
            return gpuData;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private struct RenderItem
    {
        public Entity Entity;
        public TransformComponent Transform;
        public MeshGPUData GpuData;
        public SubmeshInfo Submesh;
        public string? StrataPath; // per-submesh .stratamat (null = global/orange path)
        public float DistanceToCameraSq;
        public int InstanceIndex;
        public AstraSurfaceFlags Flags;
    }

    private void BindDefaultSurface(IRHICommandBuffer cmd, RenderItem item)
    {
        // Precedence: per-submesh .stratamat file > global StrataOverride > orange clay.
        // Lighting still varies per pixel in every path.
        var albedo = DefaultAlbedo;
        float rough = 0.6f, metal = 0f, ao = 1f;
        float wrap = 0f, tile = 8f, toksvigK = 0.5f;
        float alpha = 1f;
        var coatTint = System.Numerics.Vector3.One;
        uint mask = 0u;
        IRHITexture? rmaTex = null, detailA = null, detailN = null;
        bool fileBound = false;
        IRHITexture? albedoTex = null;
        IRHITexture? bentTex = null;
        if (item.StrataPath != null && EnsureStrataFile(item.StrataPath) &&
            _strataFileCache.TryGetValue(item.StrataPath, out var file))
        {
            albedo = new System.Numerics.Vector3(
                file.Params.BaseColor.X, file.Params.BaseColor.Y, file.Params.BaseColor.Z);
            rough = file.Params.Roughness;
            metal = file.Params.Metallic;
            ao = file.Params.AO;
            wrap = file.Params.Wrap;
            tile = file.Params.DetailTile;
            toksvigK = file.Params.ToksvigK;
            alpha = file.Params.Alpha;
            coatTint = file.Params.ClearcoatTintLinear;
            mask = file.Params.FeatureMask;
            file.Texs.TryGetValue("rma", out rmaTex);
            file.Texs.TryGetValue("detailAlbedo", out detailA);
            file.Texs.TryGetValue("detailNormal", out detailN);
            file.Texs.TryGetValue("albedo", out albedoTex);
            file.Texs.TryGetValue("bent", out bentTex);
            // Data rules: lobes needing textures stay OFF without blobs.
            if (bentTex == null)
                mask &= ~(uint)BlueSky.Rendering.Strata.StrataFeature.BentNormal;
            fileBound = true;
        }
        if (!fileBound && _strataReady)
        {
            albedo = new System.Numerics.Vector3(
                _strataParams.BaseColor.X, _strataParams.BaseColor.Y, _strataParams.BaseColor.Z);
            rough = _strataParams.Roughness;
            metal = _strataParams.Metallic;
            ao = _strataParams.AO;
            wrap = _strataParams.Wrap;
            tile = _strataParams.DetailTile;
            toksvigK = _strataParams.ToksvigK;
            alpha = _strataParams.Alpha;
            coatTint = _strataParams.ClearcoatTintLinear;
            mask = _strataParams.FeatureMask;
            StrataTexture("rma", out rmaTex);
            StrataTexture("detailAlbedo", out detailA);
            StrataTexture("detailNormal", out detailN);
        }
        // One-line diagnosis aid: which Strata branches + textures feed this draw.
        // Keyed by strata path (or override name) so it prints once, not per frame.
        {
            string maskKey = item.StrataPath ?? (_strataReady ? ("override:" + _strataReadyFor) : "<clay>");
            if (_strataMaskLogged.Add(maskKey))
                Console.WriteLine($"[Strata] Draw '{maskKey}' mask=0x{mask:X} " +
                    $"albedo={albedoTex != null} rma={rmaTex != null} " +
                    $"detailA={detailA != null} detailN={detailN != null} bent={bentTex != null} " +
                    $"rough={rough:F2} ao={ao:F2} tile={tile:F1}");
        }
        var surface = new AstraSurface
        {
            BaseColor = new System.Numerics.Vector4(albedo, 1.0f),
            Roughness = rough,
            Metallic = metal,
            AO = ao,
            EmissiveStrength = 0.0f,
            SpecularStrength = 0.5f,
            Shininess = 32.0f,
            Alpha = alpha,
            Flags = (uint)item.Flags | ((uint)(Program._debugView & 7) << 14),
            UVScale = new System.Numerics.Vector2(1, 1),
            UVOffset = new System.Numerics.Vector2(0, 0),
            EmissiveColor = new System.Numerics.Vector4(0, 0, 0, 1),
            Custom0 = new System.Numerics.Vector4(1, wrap, tile, toksvigK),
            Custom1 = new System.Numerics.Vector4(mask, coatTint.X, coatTint.Y, coatTint.Z),
        };

        // Push inline surface data to GPU at slot b11 (and b2 for legacy compatibility)
        var surfaceSpan = MemoryMarshal.CreateSpan(ref surface, 1);
        cmd.SetFragmentUniforms(11, MemoryMarshal.AsBytes(surfaceSpan));
        cmd.SetFragmentUniforms(2, MemoryMarshal.AsBytes(surfaceSpan));

        cmd.SetTexture(albedoTex ?? _defaultWhiteTexture!, 2);
        cmd.SetTexture(_defaultNormalTexture!, 3);
        cmd.SetTexture(rmaTex ?? _defaultRmaTexture!, 4);
        cmd.SetTexture(_defaultWhiteOpacityTexture!, 5); // t5 unused in Astra; bind default
        cmd.SetTexture(detailA ?? _defaultWhiteTexture!, 6);
        cmd.SetTexture(detailN ?? _defaultNormalTexture!, 7);
        cmd.SetTexture(bentTex ?? _defaultWhiteTexture!, 9); // t9 bent (world RGB); white = unused
        int binds = 7;
        if (_gradeLut != null)
        {
            cmd.SetTexture(_gradeLut, 8);
            binds++;
        }
        BlueSky.Rendering.Strata.StrataBenchmark.RecordTextureBind(binds);
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

    private void DrawBatched(IRHICommandBuffer cmd, System.Collections.Generic.List<RenderItem> items,
        bool keepOrder = false)
    {
        if (items.Count == 0) return;

        // Opaque lists sort for batching; transparent lists arrive far-to-near
        // and must keep that order (batching still merges adjacent runs).
        if (!keepOrder)
            items.Sort(CompareRenderItemsForBatching);
        bool useStableMetalInstances = cmd is BlueSky.Rendering.RHI.Metal.MetalCommandBuffer &&
                                        _frameInstanceBuffer != null;
        if (useStableMetalInstances)
        {
            // Metal command buffers execute after this method returns. Reusing
            // one CPU-visible uniform buffer and rewriting it for every draw
            // makes earlier draws observe the final upload. The frame buffer is
            // uploaded once before drawing and baseInstance selects its stable
            // per-submesh transform.
            cmd.SetUniformBuffer(_frameInstanceBuffer!, 12);
        }

        for (int i = 0; i < items.Count;)
        {
            var firstItem = items[i];

            cmd.SetVertexBuffer(firstItem.GpuData.VertexBuffer!, 0);
            cmd.SetIndexBuffer(firstItem.GpuData.IndexBuffer!, IndexType.UInt32);
            BindDefaultSurface(cmd, firstItem);

            // Emit one DrawIndexed per contiguous instance chunk.
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

            if (useStableMetalInstances)
            {
                cmd.DrawIndexed((uint)firstItem.Submesh.IndexCount, (uint)instanceCount,
                    (uint)firstItem.Submesh.IndexOffset, 0, (uint)firstItem.InstanceIndex);
                BlueSky.Rendering.Strata.StrataBenchmark.RecordMeshDraw((long)firstItem.Submesh.IndexCount * instanceCount / 3);
                i += instanceCount;
                continue;
            }

            // ── FL10.1 / Intel HD 3000 COMPAT FIX ───────────────────────────────
            // D3D11 Feature Level 10.x SILENTLY IGNORES the FirstInstance parameter
            // of DrawIndexedInstanced — it is only honored at FL11.0+. On Intel HD
            // 3000 (FL10.1) a non-zero firstInstance is treated as 0, so every
            // instanced draw would read instance slot 0 and the car would collapse
            // onto a single transform. To stay correct on FL10.1 we rebind a small
            // per-batch instance buffer that holds ONLY this batch's contiguous
            // slice of _frameInstances, and draw with firstInstance = 0. The shader
            // reads EntityUniforms[SV_InstanceID], which now maps 1:1 onto the slice.
            int srcStart = firstItem.InstanceIndex;
            int srcEnd   = Math.Min(srcStart + instanceCount, _frameInstances.Count);
            int sliceLen = srcEnd - srcStart;
            if (sliceLen > 0 && _instanceBuffer != null)
            {
                var slice = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_frameInstances)
                    .Slice(srcStart, sliceLen);
                _device.UpdateBuffer(_instanceBuffer, MemoryMarshal.AsBytes(slice));
                cmd.SetUniformBuffer(_instanceBuffer, 12); // slot 12 → b12 (bound VS+PS)
            }

            cmd.DrawIndexed((uint)firstItem.Submesh.IndexCount, (uint)instanceCount,
                (uint)firstItem.Submesh.IndexOffset, 0, 0);
            BlueSky.Rendering.Strata.StrataBenchmark.RecordMeshDraw((long)firstItem.Submesh.IndexCount * instanceCount / 3);

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

        return string.CompareOrdinal(a.StrataPath, b.StrataPath);
    }

    private static bool CanBatchRenderItems(RenderItem a, RenderItem b)
    {
        return ReferenceEquals(a.GpuData, b.GpuData)
            && a.Submesh.IndexOffset == b.Submesh.IndexOffset
            && a.Submesh.IndexCount == b.Submesh.IndexCount
            && string.Equals(a.StrataPath, b.StrataPath, StringComparison.Ordinal);
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
        
        // ── Sphere mesh (UV sphere, radius=1) for filled translucent rendering ──
        {
            int latSegs = 16, lonSegs = 16;
            var sVerts = new List<Vertex>();
            var sIdx = new List<ushort>();
            float pi = MathF.PI, twoPi = MathF.PI * 2f;

            for (int lat = 0; lat <= latSegs; lat++)
            {
                float phi = lat * pi / latSegs;
                float sinP = MathF.Sin(phi), cosP = MathF.Cos(phi);
                for (int lon = 0; lon <= lonSegs; lon++)
                {
                    float theta = lon * twoPi / lonSegs;
                    float x = sinP * MathF.Cos(theta);
                    float y = cosP;
                    float z = sinP * MathF.Sin(theta);
                    sVerts.Add(new Vertex
                    {
                        Position = new System.Numerics.Vector3(x, y, z),
                        Normal = new System.Numerics.Vector3(x, y, z),
                        UV = System.Numerics.Vector2.Zero
                    });
                }
            }
            for (int lat = 0; lat < latSegs; lat++)
            {
                for (int lon = 0; lon < lonSegs; lon++)
                {
                    ushort a = (ushort)(lat * (lonSegs + 1) + lon);
                    ushort b = (ushort)(a + 1);
                    ushort c = (ushort)(a + (lonSegs + 1));
                    ushort d = (ushort)(c + 1);
                    sIdx.Add(a); sIdx.Add(c); sIdx.Add(b);
                    sIdx.Add(b); sIdx.Add(c); sIdx.Add(d);
                }
            }

            var sVB = System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(sVerts));
            _gizmoSphereVB = _device.CreateBuffer(new BufferDesc
            {
                Size = (ulong)sVB.Length, Usage = BufferUsage.Vertex,
                MemoryType = MemoryType.CpuToGpu, DebugName = "Gizmo.SphereVB"
            });
            _device.UpdateBuffer(_gizmoSphereVB, sVB);

            var sIB = System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(sIdx));
            _gizmoSphereIB = _device.CreateBuffer(new BufferDesc
            {
                Size = (ulong)sIB.Length, Usage = BufferUsage.Index,
                MemoryType = MemoryType.CpuToGpu, DebugName = "Gizmo.SphereIB"
            });
            _device.UpdateBuffer(_gizmoSphereIB, sIB);
            _gizmoSphereIndexCount = sIdx.Count;
        }
        
        // ── Capsule mesh (radius=0.5, Y from -1 to +1, total height=2) ──
        // Bottom hemisphere: center (0,-0.5,0), radius 0.5, φ from 0 (south pole) to π/2 (equator)
        // Cylinder: Y from -0.5 to +0.5, radius 0.5
        // Top hemisphere: center (0,+0.5,0), radius 0.5, φ from π/2 (equator) to 0 (north pole)
        {
            int lonSegs = 16;
            int hemiLatSegs = 8;
            float capR = 0.5f;
            float halfH = 0.5f; // cylinder half-height
            var cVerts = new List<Vertex>();
            var cIdx = new List<ushort>();
            float pi = MathF.PI, twoPi = MathF.PI * 2f;

            // Helper to add a vertex and return its index
            ushort AddVert(System.Numerics.Vector3 pos, System.Numerics.Vector3 nrm)
            {
                ushort idx = (ushort)cVerts.Count;
                cVerts.Add(new Vertex { Position = pos, Normal = nrm, UV = System.Numerics.Vector2.Zero });
                return idx;
            }

            // ── Bottom hemisphere (south pole at Y=-1, equator at Y=-0.5) ──
            // Center at (0, -halfH, 0)
            int hemiBottomStart = cVerts.Count;
            for (int lat = 0; lat <= hemiLatSegs; lat++)
            {
                float phi = lat * (pi * 0.5f) / hemiLatSegs; // 0 to π/2
                float sinP = MathF.Sin(phi), cosP = MathF.Cos(phi);
                for (int lon = 0; lon <= lonSegs; lon++)
                {
                    float theta = lon * twoPi / lonSegs;
                    float nx = sinP * MathF.Cos(theta);
                    float ny = -cosP; // pointing down from center
                    float nz = sinP * MathF.Sin(theta);
                    var pos = new System.Numerics.Vector3(nx * capR, -halfH + ny * capR, nz * capR);
                    AddVert(pos, new System.Numerics.Vector3(nx, ny, nz));
                }
            }
            for (int lat = 0; lat < hemiLatSegs; lat++)
            {
                for (int lon = 0; lon < lonSegs; lon++)
                {
                    ushort a = (ushort)(hemiBottomStart + lat * (lonSegs + 1) + lon);
                    ushort b = (ushort)(a + 1);
                    ushort c = (ushort)(a + (lonSegs + 1));
                    ushort d = (ushort)(c + 1);
                    cIdx.Add(a); cIdx.Add(c); cIdx.Add(b);
                    cIdx.Add(b); cIdx.Add(c); cIdx.Add(d);
                }
            }

            // ── Cylinder (Y from -halfH to +halfH, radius capR) ──
            int cylStart = cVerts.Count;
            // Bottom ring
            for (int lon = 0; lon <= lonSegs; lon++)
            {
                float theta = lon * twoPi / lonSegs;
                float cos = MathF.Cos(theta), sin = MathF.Sin(theta);
                AddVert(new System.Numerics.Vector3(cos * capR, -halfH, sin * capR),
                        new System.Numerics.Vector3(cos, 0, sin));
            }
            // Top ring
            for (int lon = 0; lon <= lonSegs; lon++)
            {
                float theta = lon * twoPi / lonSegs;
                float cos = MathF.Cos(theta), sin = MathF.Sin(theta);
                AddVert(new System.Numerics.Vector3(cos * capR, halfH, sin * capR),
                        new System.Numerics.Vector3(cos, 0, sin));
            }
            for (int lon = 0; lon < lonSegs; lon++)
            {
                ushort bl = (ushort)(cylStart + lon);
                ushort br = (ushort)(bl + 1);
                ushort tl = (ushort)(bl + lonSegs + 1);
                ushort tr = (ushort)(tl + 1);
                cIdx.Add(bl); cIdx.Add(tl); cIdx.Add(br);
                cIdx.Add(br); cIdx.Add(tl); cIdx.Add(tr);
            }

            // ── Top hemisphere (equator at Y=+0.5, north pole at Y=+1) ──
            // Center at (0, +halfH, 0)
            int hemiTopStart = cVerts.Count;
            for (int lat = 0; lat <= hemiLatSegs; lat++)
            {
                float phi = (pi * 0.5f) - lat * (pi * 0.5f) / hemiLatSegs; // π/2 to 0
                float sinP = MathF.Sin(phi), cosP = MathF.Cos(phi);
                for (int lon = 0; lon <= lonSegs; lon++)
                {
                    float theta = lon * twoPi / lonSegs;
                    float nx = sinP * MathF.Cos(theta);
                    float ny = cosP; // pointing up from center
                    float nz = sinP * MathF.Sin(theta);
                    var pos = new System.Numerics.Vector3(nx * capR, halfH + ny * capR, nz * capR);
                    AddVert(pos, new System.Numerics.Vector3(nx, ny, nz));
                }
            }
            for (int lat = 0; lat < hemiLatSegs; lat++)
            {
                for (int lon = 0; lon < lonSegs; lon++)
                {
                    ushort a = (ushort)(hemiTopStart + lat * (lonSegs + 1) + lon);
                    ushort b = (ushort)(a + 1);
                    ushort c = (ushort)(a + (lonSegs + 1));
                    ushort d = (ushort)(c + 1);
                    cIdx.Add(a); cIdx.Add(c); cIdx.Add(b);
                    cIdx.Add(b); cIdx.Add(c); cIdx.Add(d);
                }
            }

            var cVB = System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cVerts));
            _gizmoCapsuleVB = _device.CreateBuffer(new BufferDesc
            {
                Size = (ulong)cVB.Length, Usage = BufferUsage.Vertex,
                MemoryType = MemoryType.CpuToGpu, DebugName = "Gizmo.CapsuleVB"
            });
            _device.UpdateBuffer(_gizmoCapsuleVB, cVB);

            var cIB = System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cIdx));
            _gizmoCapsuleIB = _device.CreateBuffer(new BufferDesc
            {
                Size = (ulong)cIB.Length, Usage = BufferUsage.Index,
                MemoryType = MemoryType.CpuToGpu, DebugName = "Gizmo.CapsuleIB"
            });
            _device.UpdateBuffer(_gizmoCapsuleIB, cIB);
            _gizmoCapsuleIndexCount = cIdx.Count;
        }

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

    private void RenderPhysicsDebugShapes(IRHICommandBuffer cmd, System.Numerics.Matrix4x4 viewProj)
    {
        if (!_gizmoGeometryCreated || _gizmoPipeline == null || _physicsDebugShapes.Count == 0)
            return;

        cmd.SetPipeline(_gizmoPipeline!);
        int ubIdx = 4;

        foreach (var shape in _physicsDebugShapes)
        {
            var model = shape.Transform;

            // ═══════ Pass 1: Translucent fill ═══════
            var fillUniforms = new GizmoUniforms
            {
                ViewProj = viewProj,
                Model = model,
                Color = shape.Color,
                GizmoType = 0,
                AxisId = 0,
                IsHovered = 0f,
            };
            var fillSpan = MemoryMarshal.CreateSpan(ref fillUniforms, 1);
            _device.UpdateBuffer(_gizmoUniformBuffers[ubIdx]!, MemoryMarshal.AsBytes(fillSpan));
            cmd.SetUniformBuffer(_gizmoUniformBuffers[ubIdx]!, 10);

            switch (shape.ShapeType)
            {
                case DebugShapeType.Box:
                    cmd.SetVertexBuffer(_gizmoCubeVB!, 0);
                    cmd.SetIndexBuffer(_gizmoCubeIB!, IndexType.UInt16);
                    cmd.DrawIndexed((uint)_gizmoCubeIndexCount);
                    break;
                case DebugShapeType.Sphere:
                    cmd.SetVertexBuffer(_gizmoSphereVB!, 0);
                    cmd.SetIndexBuffer(_gizmoSphereIB!, IndexType.UInt16);
                    cmd.DrawIndexed((uint)_gizmoSphereIndexCount);
                    break;
                case DebugShapeType.Capsule:
                    cmd.SetVertexBuffer(_gizmoCapsuleVB!, 0);
                    cmd.SetIndexBuffer(_gizmoCapsuleIB!, IndexType.UInt16);
                    cmd.DrawIndexed((uint)_gizmoCapsuleIndexCount);
                    break;
                case DebugShapeType.ConvexHull:
                    DrawFilledConvexHull(cmd, shape, model, ref fillUniforms, ubIdx);
                    break;
            }

            // ═══════ Pass 2: Wireframe edges (alpha=0.85) ═══════
            var wireColor = new System.Numerics.Vector4(shape.Color.X, shape.Color.Y, shape.Color.Z, 0.85f);
            var wireUniforms = new GizmoUniforms
            {
                ViewProj = viewProj,
                Model = model,
                Color = wireColor,
                GizmoType = 0,
                AxisId = 0,
                IsHovered = 0f,
            };
            var wireSpan = MemoryMarshal.CreateSpan(ref wireUniforms, 1);
            _device.UpdateBuffer(_gizmoUniformBuffers[ubIdx]!, MemoryMarshal.AsBytes(wireSpan));
            cmd.SetUniformBuffer(_gizmoUniformBuffers[ubIdx]!, 10);

            switch (shape.ShapeType)
            {
                case DebugShapeType.Box:
                    DrawWireframeBox(cmd, model, ref wireUniforms, ubIdx);
                    break;
                case DebugShapeType.Sphere:
                    DrawWireframeRings(cmd, model, ref wireUniforms, ubIdx);
                    break;
                case DebugShapeType.Capsule:
                    DrawWireframeCapsule(cmd, shape, model, ref wireUniforms, ubIdx);
                    break;
                case DebugShapeType.ConvexHull:
                    DrawConvexHullWireframe(cmd, shape, model, ref wireUniforms, ubIdx);
                    break;
            }
        }
    }

    private void DrawFilledConvexHull(IRHICommandBuffer cmd, DebugShape shape,
        System.Numerics.Matrix4x4 model, ref GizmoUniforms uniforms, int ubIdx)
    {
        if (shape.HullVertices == null || shape.HullIndices == null || shape.HullIndices.Length < 3)
            return;

        var verts = shape.HullVertices;
        var indices = shape.HullIndices;

        var triVerts = new List<float>();
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            if (i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length) continue;

            var p0 = System.Numerics.Vector3.Transform(verts[i0], model);
            var p1 = System.Numerics.Vector3.Transform(verts[i1], model);
            var p2 = System.Numerics.Vector3.Transform(verts[i2], model);

            var edge1 = p1 - p0;
            var edge2 = p2 - p0;
            var n = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(edge1, edge2));

            AddTriVert(p0, n); AddTriVert(p1, n); AddTriVert(p2, n);
        }

        if (triVerts.Count == 0) return;

        int vertCount = triVerts.Count / 8;
        EnsureHullLineBuffer(vertCount);

        var byteSpan = MemoryMarshal.Cast<float, byte>(triVerts.ToArray().AsSpan());
        _device.UpdateBuffer(_hullLineVB!, byteSpan);

        uniforms.Model = System.Numerics.Matrix4x4.Identity;
        var span = MemoryMarshal.CreateSpan(ref uniforms, 1);
        _device.UpdateBuffer(_gizmoUniformBuffers[ubIdx]!, MemoryMarshal.AsBytes(span));
        cmd.SetUniformBuffer(_gizmoUniformBuffers[ubIdx]!, 10);
        cmd.SetVertexBuffer(_hullLineVB!, 0);
        cmd.Draw((uint)vertCount);

        void AddTriVert(System.Numerics.Vector3 p, System.Numerics.Vector3 n)
        {
            triVerts.Add(p.X); triVerts.Add(p.Y); triVerts.Add(p.Z);
            triVerts.Add(n.X); triVerts.Add(n.Y); triVerts.Add(n.Z);
            triVerts.Add(0); triVerts.Add(0);
        }
    }

    private void DrawWireframeBox(IRHICommandBuffer cmd, System.Numerics.Matrix4x4 model,
        ref GizmoUniforms uniforms, int ubIdx)
    {
        float h = 0.5f;
        var corners = new System.Numerics.Vector3[]
        {
            new(-h,-h,-h), new( h,-h,-h), new( h,-h, h), new(-h,-h, h),
            new(-h, h,-h), new( h, h,-h), new( h, h, h), new(-h, h, h),
        };
        var edges = new int[][] {
            new int[]{0,1}, new int[]{1,2}, new int[]{2,3}, new int[]{3,0},
            new int[]{4,5}, new int[]{5,6}, new int[]{6,7}, new int[]{7,4},
            new int[]{0,4}, new int[]{1,5}, new int[]{2,6}, new int[]{3,7}
        };

        var edgeVerts = new List<float>();
        foreach (var e in edges)
            AddEdgeAsQuad(corners[e[0]], corners[e[1]], edgeVerts);

        if (edgeVerts.Count == 0) return;

        int vertCount = edgeVerts.Count / 8;
        EnsureHullLineBuffer(vertCount);

        var byteSpan = MemoryMarshal.Cast<float, byte>(edgeVerts.ToArray().AsSpan());
        _device.UpdateBuffer(_hullLineVB!, byteSpan);

        uniforms.Model = model;
        var span = MemoryMarshal.CreateSpan(ref uniforms, 1);
        _device.UpdateBuffer(_gizmoUniformBuffers[ubIdx]!, MemoryMarshal.AsBytes(span));
        cmd.SetUniformBuffer(_gizmoUniformBuffers[ubIdx]!, 10);
        cmd.SetVertexBuffer(_hullLineVB!, 0);
        cmd.Draw((uint)vertCount);
    }

    private void DrawWireframeRings(IRHICommandBuffer cmd, System.Numerics.Matrix4x4 model,
        ref GizmoUniforms uniforms, int ubIdx)
    {
        var ringRotations = new[]
        {
            System.Numerics.Matrix4x4.Identity,
            System.Numerics.Matrix4x4.CreateRotationX(MathF.PI / 2f),
            System.Numerics.Matrix4x4.CreateRotationY(MathF.PI / 2f),
        };
        foreach (var rot in ringRotations)
        {
            uniforms.Model = rot * model;
            var rSpan = MemoryMarshal.CreateSpan(ref uniforms, 1);
            _device.UpdateBuffer(_gizmoUniformBuffers[ubIdx]!, MemoryMarshal.AsBytes(rSpan));
            cmd.SetVertexBuffer(_gizmoRingVB!, 0);
            cmd.SetIndexBuffer(_gizmoRingIB!, IndexType.UInt16);
            cmd.DrawIndexed((uint)_gizmoRingIndexCount);
        }
    }

    private void DrawWireframeCapsule(IRHICommandBuffer cmd, DebugShape shape,
        System.Numerics.Matrix4x4 model, ref GizmoUniforms uniforms, int ubIdx)
    {
        // 3 orthogonal rings at center (sphere-style)
        DrawWireframeRings(cmd, model, ref uniforms, ubIdx);

        // 2 rings at hemisphere-cylinder junctions (Y=±0.5 in unit capsule)
        float capR = 0.5f;
        float halfH = 0.5f;
        var ringVerts = new List<float>();
        int ringSegs = 48;
        float twoPi = MathF.PI * 2f;

        for (int ring = -1; ring <= 1; ring += 2)
        {
            float y = ring * halfH;
            ringVerts.Clear();
            for (int i = 0; i <= ringSegs; i++)
            {
                float theta = i * twoPi / ringSegs;
                float x = capR * MathF.Cos(theta);
                float z = capR * MathF.Sin(theta);
                // Position
                ringVerts.Add(x); ringVerts.Add(y); ringVerts.Add(z);
                // Normal (radially outward)
                ringVerts.Add(MathF.Cos(theta)); ringVerts.Add(0); ringVerts.Add(MathF.Sin(theta));
                // UV
                ringVerts.Add(0); ringVerts.Add(0);
            }
            int vCount = ringVerts.Count / 8;
            if (vCount < 2) continue;
            EnsureHullLineBuffer(vCount);

            var byteSpan = MemoryMarshal.Cast<float, byte>(ringVerts.ToArray().AsSpan());
            _device.UpdateBuffer(_hullLineVB!, byteSpan);

            uniforms.Model = model;
            var span = MemoryMarshal.CreateSpan(ref uniforms, 1);
            _device.UpdateBuffer(_gizmoUniformBuffers[ubIdx]!, MemoryMarshal.AsBytes(span));
            cmd.SetUniformBuffer(_gizmoUniformBuffers[ubIdx]!, 10);
            cmd.SetVertexBuffer(_hullLineVB!, 0);
            // Draw as line strip for the ring outline
            cmd.Draw((uint)vCount);
        }

        // 4 longitudinal lines along cylinder body
        ringVerts.Clear();
        for (int i = 0; i < 4; i++)
        {
            float theta = i * MathF.PI / 2f;
            float x = capR * MathF.Cos(theta);
            float z = capR * MathF.Sin(theta);
            float nx = MathF.Cos(theta);
            float nz = MathF.Sin(theta);

            // Bottom point
            ringVerts.Add(x); ringVerts.Add(-halfH); ringVerts.Add(z);
            ringVerts.Add(nx); ringVerts.Add(0); ringVerts.Add(nz);
            ringVerts.Add(0); ringVerts.Add(0);
            // Top point
            ringVerts.Add(x); ringVerts.Add(halfH); ringVerts.Add(z);
            ringVerts.Add(nx); ringVerts.Add(0); ringVerts.Add(nz);
            ringVerts.Add(0); ringVerts.Add(0);
        }
        int lineVertCount = ringVerts.Count / 8;
        if (lineVertCount > 0)
        {
            EnsureHullLineBuffer(lineVertCount);
            var byteSpan = MemoryMarshal.Cast<float, byte>(ringVerts.ToArray().AsSpan());
            _device.UpdateBuffer(_hullLineVB!, byteSpan);

            uniforms.Model = model;
            var span = MemoryMarshal.CreateSpan(ref uniforms, 1);
            _device.UpdateBuffer(_gizmoUniformBuffers[ubIdx]!, MemoryMarshal.AsBytes(span));
            cmd.SetUniformBuffer(_gizmoUniformBuffers[ubIdx]!, 10);
            cmd.SetVertexBuffer(_hullLineVB!, 0);
            cmd.Draw((uint)lineVertCount);
        }
    }

    private void EnsureHullLineBuffer(int vertCount)
    {
        if (_hullLineVB == null || _hullLineVertexCapacity < vertCount)
        {
            _hullLineVertexCapacity = Math.Max(vertCount, 256);
            _hullLineVB?.Dispose();
            _hullLineVB = _device.CreateBuffer(new BufferDesc
            {
                Size = (ulong)(_hullLineVertexCapacity * 32),
                Usage = BufferUsage.Vertex,
                DebugName = "HullLineVB"
            });
        }
    }

    private void DrawConvexHullWireframe(IRHICommandBuffer cmd, DebugShape shape,
        System.Numerics.Matrix4x4 model, ref GizmoUniforms uniforms, int ubIdx)
    {
        var verts = shape.HullVertices!;
        var indices = shape.HullIndices!;

        var edgeSet = new HashSet<(int, int)>();
        var edgeVerts = new List<float>();

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int triA = indices[i], triB = indices[i + 1], triC = indices[i + 2];
            AddEdge(triA, triB);
            AddEdge(triB, triC);
            AddEdge(triC, triA);
        }

        if (edgeVerts.Count == 0) return;

        int vertCount = edgeVerts.Count / 8;

        EnsureHullLineBuffer(vertCount);

        var floatArr = edgeVerts.ToArray();
        var byteSpan = MemoryMarshal.Cast<float, byte>(floatArr.AsSpan());
        _device.UpdateBuffer(_hullLineVB!, byteSpan);

        uniforms.Model = System.Numerics.Matrix4x4.Identity;
        var span = MemoryMarshal.CreateSpan(ref uniforms, 1);
        _device.UpdateBuffer(_gizmoUniformBuffers[ubIdx]!, MemoryMarshal.AsBytes(span));
        cmd.SetUniformBuffer(_gizmoUniformBuffers[ubIdx]!, 10);
        cmd.SetVertexBuffer(_hullLineVB!, 0);
        cmd.Draw((uint)vertCount);

        void AddEdge(int a, int b)
        {
            if (a >= verts.Length || b >= verts.Length) return;
            var key = a < b ? (a, b) : (b, a);
            if (!edgeSet.Add(key)) return;

            var p0 = System.Numerics.Vector3.Transform(verts[a], model);
            var p1 = System.Numerics.Vector3.Transform(verts[b], model);
            AddEdgeAsQuad(p0, p1, edgeVerts);
        }
    }

    private static void AddEdgeAsQuad(System.Numerics.Vector3 p0, System.Numerics.Vector3 p1,
        System.Collections.Generic.List<float> outVerts)
    {
        var dir = System.Numerics.Vector3.Normalize(p1 - p0);
        var up = System.MathF.Abs(System.Numerics.Vector3.Dot(dir, System.Numerics.Vector3.UnitY)) > 0.99f
            ? System.Numerics.Vector3.UnitX : System.Numerics.Vector3.UnitY;
        var perp = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(dir, up));
        float t = 0.01f;

        AddLineVert(p0 + perp * t); AddLineVert(p1 + perp * t); AddLineVert(p0 - perp * t);
        AddLineVert(p0 - perp * t); AddLineVert(p1 + perp * t); AddLineVert(p1 - perp * t);

        void AddLineVert(System.Numerics.Vector3 p)
        {
            outVerts.Add(p.X); outVerts.Add(p.Y); outVerts.Add(p.Z);
            outVerts.Add(0); outVerts.Add(1); outVerts.Add(0);
            outVerts.Add(0); outVerts.Add(0);
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
        _surfaceBuffer?.Dispose();
        _shBuffer?.Dispose();
        _skyParamsBuffer?.Dispose();
        _gradeLut?.Dispose();
        _gizmoArrowVB?.Dispose();
        _gizmoArrowIB?.Dispose();
        _gizmoCubeVB?.Dispose();
        _gizmoCubeIB?.Dispose();
        _gizmoRingVB?.Dispose();
        _gizmoRingIB?.Dispose();
        _gizmoCapsuleVB?.Dispose();
        _gizmoCapsuleIB?.Dispose();
        _gizmoSphereVB?.Dispose();
        _gizmoSphereIB?.Dispose();
        
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

        _disposed = true;
    }

    /// <summary>
    private BlueSky.Core.Math.Matrix4x4 ApplyWheelTransformIfApplicable(
        MeshGPUData gpuData, 
        SubmeshInfo submesh, 
        int submeshIdx, 
        BlueSky.Core.Gameplay.CarController carController, 
        (string assetId, uint entityId) cacheKey, 
        BlueSky.Core.Math.Matrix4x4 worldMatrix)
    {
        if (carController == null || carController.WheelCount < 4)
            return worldMatrix;

        // GEOMETRIC CHASSIS GUARANTEE: If a submesh is large (> 1.7m on X or Z axis),
        // or contains > 25% of total mesh geometry, it is the car body/chassis.
        // It MUST NEVER be rotated as a wheel!
        if (gpuData.RawIndices != null && gpuData.RawVertexPositions != null)
        {
            var (min, max) = ComputeSubmeshBounds(gpuData, submesh);
            var size = max - min;
            if (size.X > 1.7f || size.Z > 1.7f || (gpuData.IndexCount > 0 && (float)submesh.IndexCount / gpuData.IndexCount > 0.25f))
            {
                return worldMatrix;
            }
        }

        int wheelSlot = -1;

        // 1. Try bone-based wheel slot mapping first
        if (carController.SkeletalMesh != null)
        {
            if (!_skeletalSubmeshBoneMap.TryGetValue(cacheKey, out var boneMap) || boneMap is null)
            {
                boneMap = BuildSkeletalSubmeshBoneMap(gpuData, carController.SkeletalMesh, carController);
                _skeletalSubmeshBoneMap[cacheKey] = boneMap;
            }
            int boneIdx = (submeshIdx >= 0 && submeshIdx < boneMap.Length) ? boneMap[submeshIdx] : -1;
            if (boneIdx >= 0)
            {
                carController.TryGetWheelSlotForBoneIndex(boneIdx, out wheelSlot);
            }
        }

        // 1b. Pack skeleton (stratapack import): match wheels by BONE NAME.
        // Pack meshes have a skeleton sidecar but no FBX SkeletalMesh, so the
        // index-based path above can't run. Name matching beats the centroid
        // fallback below and never touches body submeshes (slots 0-3 only).
        if (wheelSlot < 0 && TryGetPackSubmeshBoneName(cacheKey.assetId, gpuData, submesh, submeshIdx, out var packBone))
        {
            carController.TryGetWheelSlotForBoneName(packBone, out wheelSlot);
        }

        // 2. Fallback to centroid-based geometry matching if bone mapping didn't yield a wheel slot
        if (wheelSlot < 0 && gpuData.RawIndices != null && gpuData.RawVertexPositions != null)
        {
            if (!_submeshWheelMap.TryGetValue(cacheKey, out var wheelMap) || wheelMap is null)
            {
                wheelMap = BuildSubmeshWheelMap(gpuData, carController);
                _submeshWheelMap[cacheKey] = wheelMap;
            }
            if (submeshIdx >= 0 && submeshIdx < wheelMap.Length)
            {
                wheelSlot = wheelMap[submeshIdx];
            }
        }

        if (_debugFrameCounter % 60 == 0)
        {
            try
            {
                System.IO.File.AppendAllText("/tmp/bluesky_wheel_mapping.txt",
                    $"[Frame {_debugFrameCounter}] Submesh[{submeshIdx}] -> WheelSlot={wheelSlot}\n");
            }
            catch { }
        }

        // Apply wheel rotation matrix ONLY if this submesh is a verified wheel slot (0 to 3)
        if (wheelSlot >= 0 && wheelSlot < 4)
        {
            var wheelRot = carController.GetWheelTransformMatrix(wheelSlot);
            var centroid = ComputeSubmeshCentroid(gpuData, submesh);
            var wheelPos = carController.GetWheelLocalPosition(wheelSlot);

            if (_debugFrameCounter % 60 == 0)
            {
                try
                {
                    System.IO.File.AppendAllText("/tmp/bluesky_centroid_debug.txt",
                        $"[Frame {_debugFrameCounter}] Submesh[{submeshIdx}] WheelSlot={wheelSlot} Centroid=({centroid.X:F3},{centroid.Y:F3},{centroid.Z:F3}) WheelPos=({wheelPos.X:F3},{wheelPos.Y:F3},{wheelPos.Z:F3})\n");
                }
                catch { }
            }

            var wheelTransform = System.Numerics.Matrix4x4.CreateTranslation(-centroid) *
                                 wheelRot *
                                 System.Numerics.Matrix4x4.CreateTranslation(centroid);
            var ew = new BlueSky.Core.Math.Matrix4x4(
                wheelTransform.M11, wheelTransform.M12, wheelTransform.M13, wheelTransform.M14,
                wheelTransform.M21, wheelTransform.M22, wheelTransform.M23, wheelTransform.M24,
                wheelTransform.M31, wheelTransform.M32, wheelTransform.M33, wheelTransform.M34,
                wheelTransform.M41, wheelTransform.M42, wheelTransform.M43, wheelTransform.M44);
            return ew * worldMatrix;
        }

        return worldMatrix;
    }

    /// <summary>
    /// Build a mapping from submesh index to wheel slot (0-3) based on vertex centroids.
    /// </summary>
    private static int[] BuildSubmeshWheelMap(MeshGPUData gpuData, CarController carController)
    {
        var wheelPositions = new System.Numerics.Vector3[4];
        for (int i = 0; i < 4; i++)
            wheelPositions[i] = carController.GetWheelLocalPosition(i);

        int[] wheelMap = new int[gpuData.Submeshes.Count];
        for (int i = 0; i < wheelMap.Length; i++) wheelMap[i] = -1;

        int totalIndices = gpuData.IndexCount;

        for (int submeshIdx = 0; submeshIdx < gpuData.Submeshes.Count; submeshIdx++)
        {
            var submesh = gpuData.Submeshes[submeshIdx];
            
            // Exclude main chassis/body submeshes (submesh containing > 20% of all geometry)
            if (totalIndices > 0 && (float)submesh.IndexCount / totalIndices > 0.20f)
                continue;

            var centroid = ComputeSubmeshCentroid(gpuData, submesh);
            var (min, max) = ComputeSubmeshBounds(gpuData, submesh);
            var size = max - min;

            // Chassis protection by bounding size (wheels are compact: <= 1.6m on X/Y/Z)
            if (size.X > 1.6f || size.Z > 1.6f)
                continue;
            
            // Wheels are compact (<= 1.6m on all axes)
            bool compactWheelPart =
                size.X <= 1.6f &&
                size.Y <= 1.6f &&
                size.Z <= 1.6f;

            // Wheels are offset from centerline (|X| > 0.15m)
            bool offCenter = MathF.Abs(centroid.X) > 0.15f || MathF.Abs(wheelPositions[0].X) < 0.1f;

            if (!compactWheelPart || !offCenter)
                continue;

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

            // If distance to wheel position is within 2.2m
            if (bestWheel >= 0 && minDist < 2.2f)
            {
                wheelMap[submeshIdx] = bestWheel;
            }
        }

        return wheelMap;
    }

    /// <summary>
    /// Build a mapping from submesh index to skeletal bone index using multi-phase heuristics:
    /// 1. Fast-path bone weight voting (checking a subset of vertices).
    /// 2. Deep-path bone weight voting (scanning up to 1000 vertices).
    /// 3. Name similarity matching between submesh name and bone name.
    /// 4. Array structural matching (fallback to 1:1 if sizes align).
    /// </summary>
    private static int[] BuildSkeletalSubmeshBoneMap(MeshGPUData gpuData, SkeletalMesh skelMesh, BlueSky.Core.Gameplay.CarController carController)
    {
        if (gpuData.Submeshes == null) return Array.Empty<int>();
        int[] boneMap = new int[gpuData.Submeshes.Count];
        for (int s = 0; s < boneMap.Length; s++)
        {
            boneMap[s] = -1;
        }

        if (skelMesh.Vertices == null || gpuData.RawIndices == null)
            return boneMap;

        var diag = new System.Text.StringBuilder();

        for (int submeshIdx = 0; submeshIdx < gpuData.Submeshes.Count; submeshIdx++)
        {
            var submesh = gpuData.Submeshes[submeshIdx];
            int indexOffset = submesh.IndexOffset;
            int indexEnd = Math.Min(indexOffset + submesh.IndexCount, gpuData.RawIndices!.Length);

            var boneVotes = new Dictionary<int, float>();

            // Phase 1: Fast voting - check up to 100 vertices (every 3rd index)
            int maxVertsToCheck = Math.Min(100, (indexEnd - indexOffset) / 3);
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
            int bestBone = -1;
            foreach (var kvp in boneVotes)
            {
                if (kvp.Value > maxVote)
                {
                    maxVote = kvp.Value;
                    bestBone = kvp.Key;
                }
            }

            // Phase 2: Deep voting - scan up to 1000 vertices sequentially if fast voting yielded no results
            if (maxVote < 0.1f)
            {
                int scanLimit = Math.Min(1000, indexEnd - indexOffset);
                for (int vi = 0; vi < scanLimit && (indexOffset + vi) < indexEnd; vi++)
                {
                    int idxPos = indexOffset + vi;
                    uint vertexIdx = gpuData.RawIndices[idxPos];
                    if (vertexIdx >= skelMesh.Vertices.Length) continue;

                    var vertex = skelMesh.Vertices[vertexIdx];
                    AccumulateBoneVote(boneVotes, vertex.BoneIndex0, vertex.BoneWeight0);
                    AccumulateBoneVote(boneVotes, vertex.BoneIndex1, vertex.BoneWeight1);
                    AccumulateBoneVote(boneVotes, vertex.BoneIndex2, vertex.BoneWeight2);
                    AccumulateBoneVote(boneVotes, vertex.BoneIndex3, vertex.BoneWeight3);
                }

                foreach (var kvp in boneVotes)
                {
                    if (kvp.Value > maxVote)
                    {
                        maxVote = kvp.Value;
                        bestBone = kvp.Key;
                    }
                }
            }

            string phaseUsed = "none";
            int usedBone = -1;

            if (maxVote >= 0.1f)
            {
                boneMap[submeshIdx] = bestBone;
                usedBone = bestBone;
                phaseUsed = $"weight(vote={maxVote:F3})";
            }
            else
            {
                // Phase 3: Name-based fuzzy matching
                // (Submesh names died with the parser DTOs; bone-name voting
                // in phases 1-2 plus structural fallback carry the mapping.)
                string? submeshName = null;

                if (!string.IsNullOrEmpty(submeshName))
                {
                    string cleanSubmeshName = submeshName.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                    int bestMatchIndex = -1;
                    int bestMatchScore = 0;

                    for (int bi = 0; bi < skelMesh.Bones.Length; bi++)
                    {
                        string cleanBoneName = skelMesh.Bones[bi].Name.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                        if (cleanBoneName == cleanSubmeshName)
                        {
                            bestMatchIndex = bi;
                            break;
                        }
                        else if (cleanBoneName.Contains(cleanSubmeshName) || cleanSubmeshName.Contains(cleanBoneName))
                        {
                            int overlap = Math.Min(cleanBoneName.Length, cleanSubmeshName.Length);
                            if (overlap > bestMatchScore)
                            {
                                bestMatchScore = overlap;
                                bestMatchIndex = bi;
                            }
                        }
                    }

                    if (bestMatchIndex != -1)
                    {
                        boneMap[submeshIdx] = bestMatchIndex;
                        usedBone = bestMatchIndex;
                        phaseUsed = $"name('{submeshName}')";
                    }
                }

                // Phase 4: Structural alignment fallback
                if (usedBone < 0)
                {
                    if (skelMesh.Bones.Length == gpuData.Submeshes.Count)
                    {
                        boneMap[submeshIdx] = submeshIdx;
                        usedBone = submeshIdx;
                        phaseUsed = "structural";
                    }
                    else
                    {
                        boneMap[submeshIdx] = -1;
                        usedBone = -1;
                        phaseUsed = "unmapped";
                    }
                }
            }

            string boneName = usedBone >= 0 && usedBone < skelMesh.Bones.Length
                ? skelMesh.Bones[usedBone].Name
                : "NONE";
            diag.AppendLine($"  submesh[{submeshIdx}] → bone[{usedBone}] ('{boneName}') phase={phaseUsed}");
        }

        try { System.IO.File.WriteAllText("/tmp/bluesky_bone_map.txt", diag.ToString()); } catch { }

        return boneMap;
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

    private static (System.Numerics.Vector3 Min, System.Numerics.Vector3 Max) ComputeSubmeshBounds(MeshGPUData gpuData, SubmeshInfo submesh)
    {
        if (gpuData.RawIndices == null || gpuData.RawVertexPositions == null)
            return (System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero);

        var min = new System.Numerics.Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new System.Numerics.Vector3(float.MinValue, float.MinValue, float.MinValue);
        bool hasVertex = false;

        int indexEnd = System.Math.Min(submesh.IndexOffset + submesh.IndexCount, gpuData.RawIndices.Length);
        for (int idx = submesh.IndexOffset; idx < indexEnd; idx++)
        {
            uint vertIdx = gpuData.RawIndices[idx];
            if (vertIdx >= gpuData.RawVertexPositions.Length)
                continue;

            var p = gpuData.RawVertexPositions[(int)vertIdx];
            min = System.Numerics.Vector3.Min(min, p);
            max = System.Numerics.Vector3.Max(max, p);
            hasVertex = true;
        }

        return hasVertex ? (min, max) : (System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero);
    }
}
