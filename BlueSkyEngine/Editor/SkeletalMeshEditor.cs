// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
// SKELETAL MESH EDITOR - PRODUCTION-GRADE SKELETAL MESH INSPECTOR WITH PHYSICS ASSET
// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
//
// TABS:
// - Bones:      Bone hierarchy tree with selection
// - Physics:    Per-bone physics body editor with Cook! button → exports .bsphy
// - Info:       Mesh statistics and asset info
//
// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using BlueSky.Motif;
using BlueSky.Core.Assets;
using BlueSky.Editor.UI;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Rendering;
using BlueSky.Rendering.RHI;
using Viewport = BlueSky.Rendering.Viewport;
using AssetType = BlueSky.Core.Assets.AssetType;

namespace BlueSky.Editor;

/// <summary>
/// Skeletal Mesh Editor — full editor for skeletal meshes with Bones, Physics, and Info tabs.
/// Physics tab supports per-bone shape editing and Cook! → .bsphy export.
/// </summary>
public class SkeletalMeshEditor
{
    // ── Core State ────────────────────────────────────────────────────────
    private BlueAsset? _currentAsset;
    private string _assetPath = "";
    private bool _isDirty = false;

    // ── Preview ───────────────────────────────────────────────────────────
    private Core.ECS.Entity _previewEntity;
    private Core.ECS.World? _lastWorld;
    private bool _hasSpawnedPreview = false;
    private ViewportRenderer? _previewRenderer;
    private IRHITexture? _previewTexture;
    private IRHIDevice? _rhi;
    private uint _previewWidth = 512;
    private uint _previewHeight = 512;
    public Vector4 PreviewRect { get; private set; }

    // ── Skeletal Mesh Data ────────────────────────────────────────────────
    private SkeletalMesh? _skeletalMesh;
    private string _meshName = "";
    private int _vertexCount = 0;
    private int _triangleCount = 0;
    private int _boneCount = 0;
    private Vector3 _boundsMin = Vector3.Zero;
    private Vector3 _boundsMax = Vector3.Zero;

    // ── Physics Asset ─────────────────────────────────────────────────────
    private PhysicsAsset _physicsAsset = new();
    private string _physicsAssetPath = "";
    private bool _isPhysicsDirty = false;
    private int _selectedBoneIndex = -1;
    private string _cookStatus = "";

    // ── UI State ──────────────────────────────────────────────────────────
    private int _selectedTab = 0; // 0=Bones, 1=Physics, 2=Info
    private float _previewRotation = 0f;
    private bool _autoRotate = true;
    private float _tabTransition = 0f;
    private int _previousTab = 0;
    private readonly Dictionary<int, float> _hoverAnim = new();

    public bool IsOpen { get; set; } = false;

    // ════════════════════════════════════════════════════════════════════════
    // OPEN / CLOSE
    // ════════════════════════════════════════════════════════════════════════

    public void Open(string assetPath, Core.ECS.World world, Viewport viewport, IRHIDevice rhi)
    {
        try
        {
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
            {
                Console.WriteLine($"[SkeletalMeshEditor] ERROR: Asset file not found: {assetPath}");
                return;
            }

            var asset = BlueAsset.Load(assetPath);
            if (asset == null)
            {
                Console.WriteLine($"[SkeletalMeshEditor] ERROR: Failed to load asset: {assetPath}");
                return;
            }

            if (asset.Type != AssetType.SkeletalMesh)
            {
                Console.WriteLine($"[SkeletalMeshEditor] ERROR: Asset is not a skeletal mesh: {asset.Type}");
                return;
            }

            _currentAsset = asset;
            _assetPath = assetPath;
            _isDirty = false;
            _isPhysicsDirty = false;
            _rhi = rhi;

            if (_lastWorld != world)
            {
                _hasSpawnedPreview = false;
                _previewEntity = default;
                _lastWorld = world;
            }
            IsOpen = true;

            LoadAssetData();
            LoadPhysicsAsset();

            if (_previewRenderer == null)
                InitializePreviewRenderer(world, viewport);

            SpawnPreviewMesh(world, viewport);
            PositionCamera(viewport);

            Console.WriteLine($"[SkeletalMeshEditor] Opened: {_meshName} ({_boneCount} bones, {_vertexCount} verts)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkeletalMeshEditor] EXCEPTION: {ex.Message}");
        }
    }

    public void Close(Core.ECS.World world)
    {
        if (_hasSpawnedPreview && _previewEntity.Id != 0)
        {
            try { world.DestroyEntity(_previewEntity); }
            catch { }
        }
        _previewTexture?.Dispose();
        _previewTexture = null;
        _previewRenderer = null;
        _hasSpawnedPreview = false;
        _previewEntity = default;
        IsOpen = false;
    }

    // ════════════════════════════════════════════════════════════════════════
    // PREVIEW RENDERING
    // ════════════════════════════════════════════════════════════════════════

    private void InitializePreviewRenderer(Core.ECS.World world, Viewport viewport)
    {
        if (_rhi == null) return;
        try
        {
            _previewTexture = _rhi.CreateTexture(new TextureDesc
            {
                Width = _previewWidth, Height = _previewHeight,
                Depth = 1, MipLevels = 1, ArrayLayers = 1,
                Format = BlueSky.Rendering.RHI.TextureFormat.RGBA8Unorm,
                Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
                DebugName = "SkeletalMeshPreview"
            });
            _previewRenderer = new ViewportRenderer(_rhi, world);
            Console.WriteLine($"[SkeletalMeshEditor] Preview renderer initialized");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkeletalMeshEditor] Preview renderer failed: {ex.Message}");
        }
    }

    private void PositionCamera(Viewport viewport)
    {
        try
        {
            Vector3 center = (_boundsMin + _boundsMax) * 0.5f;
            Vector3 size = _boundsMax - _boundsMin;
            float diagonal = MathF.Sqrt(size.X * size.X + size.Y * size.Y + size.Z * size.Z);
            if (diagonal < 0.001f) diagonal = 2.0f;

            float fovRad = 60f * MathF.PI / 180f;
            float distance = (diagonal * 0.5f) / MathF.Tan(fovRad * 0.5f);
            distance = Math.Max(distance * 1.2f, 3.0f);

            var cameraPos = new Core.Math.Vector3(
                center.X + diagonal * 0.4f,
                center.Y + diagonal * 0.35f,
                center.Z + distance);

            ref var cam = ref viewport.GetCameraTransform();
            cam.SetPosition(cameraPos);
            cam.LookAt(new Core.Math.Vector3(center.X, center.Y, center.Z), Core.Math.Vector3.Up);
        }
        catch { }
    }

    private void SpawnPreviewMesh(Core.ECS.World world, Viewport viewport)
    {
        if (_currentAsset == null) return;

        if (_hasSpawnedPreview && world.HasComponent<Core.ECS.Builtin.SkeletalMeshComponent>(_previewEntity))
        {
            ref var meshComp = ref world.GetComponent<Core.ECS.Builtin.SkeletalMeshComponent>(_previewEntity);
            meshComp.MeshAssetPath = _assetPath;
            if (world.HasComponent<Core.ECS.Builtin.TransformComponent>(_previewEntity))
            {
                ref var t = ref world.GetComponent<Core.ECS.Builtin.TransformComponent>(_previewEntity);
                t.Position = Core.Math.Vector3.Zero;
                t.Rotation = Core.Math.Quaternion.Identity;
                t.Scale = Core.Math.Vector3.One;
            }
            Program.GetMainViewport()?.InvalidateMeshGpuCache(_assetPath);
            return;
        }

        try
        {
            _previewEntity = world.CreateEntity();
            world.AddComponent(_previewEntity, new Core.ECS.Builtin.TransformComponent
            {
                Position = Core.Math.Vector3.Zero,
                Rotation = Core.Math.Quaternion.Identity,
                Scale = Core.Math.Vector3.One
            });
            var skelComp = new Core.ECS.Builtin.SkeletalMeshComponent(_assetPath);
            world.AddComponent(_previewEntity, skelComp);
            _hasSpawnedPreview = true;
            Program.GetMainViewport()?.InvalidateMeshGpuCache(_assetPath);
            Console.WriteLine($"[SkeletalMeshEditor] Spawned preview: {_meshName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkeletalMeshEditor] Failed to spawn preview: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // DATA LOADING
    // ════════════════════════════════════════════════════════════════════════

    private void LoadAssetData()
    {
        if (_currentAsset == null) return;
        _meshName = _currentAsset.AssetName;

        if (_currentAsset.Metadata.TryGetValue("vertexCount", out var v))
            int.TryParse(v, out _vertexCount);
        if (_currentAsset.Metadata.TryGetValue("triangleCount", out var t))
            int.TryParse(t, out _triangleCount);

        if (_currentAsset.Metadata.TryGetValue("boneCount", out var b))
            int.TryParse(b, out _boneCount);

        if (_currentAsset.Metadata.TryGetValue("boundsMin", out var bMin))
        {
            var p = bMin.Split(',');
            if (p.Length == 3)
            {
                float.TryParse(p[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _boundsMin.X);
                float.TryParse(p[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _boundsMin.Y);
                float.TryParse(p[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _boundsMin.Z);
            }
        }
        if (_currentAsset.Metadata.TryGetValue("boundsMax", out var bMax))
        {
            var p = bMax.Split(',');
            if (p.Length == 3)
            {
                float.TryParse(p[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _boundsMax.X);
                float.TryParse(p[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _boundsMax.Y);
                float.TryParse(p[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _boundsMax.Z);
            }
        }

        // Load the skeletal mesh data for bone list
        try
        {
            string payloadPath = _assetPath;
            if (File.Exists(payloadPath))
            {
                using var fs = File.OpenRead(payloadPath);
                using var br = new BinaryReader(fs);
                // Skip BSAS header: magic(4) + version(4) + jsonLen(4) + json + payloadLen(4)
                if (br.ReadByte() == 'B' && br.ReadByte() == 'S' && br.ReadByte() == 'A' && br.ReadByte() == 'S')
                {
                    br.ReadInt32(); // version
                    int jsonLen = br.ReadInt32();
                    fs.Seek(jsonLen, SeekOrigin.Current);
                    int payloadLen = br.ReadInt32();
                    if (payloadLen > 0)
                    {
                        // Payload contains bone names in metadata, but we need the actual SkeletalMesh object.
                        // The SkeletalMesh is loaded at runtime by ViewportRenderer. For the editor,
                        // we'll read bone names from the metadata keys or use the importer's cache.
                        // For now, try to deserialize the payload as a SkeletalMeshAsset.
                    }
                }
            }
        }
        catch { }

        // Try to get bone info from the runtime cache if available
        TryLoadBoneData();
    }

    private void TryLoadBoneData()
    {
        if (_currentAsset == null) return;

        // SourceFile is a top-level BlueAsset property, NOT in Metadata
        string? sourceFile = _currentAsset.SourceFile;

        if (!string.IsNullOrEmpty(sourceFile) && File.Exists(sourceFile))
        {
            Console.WriteLine($"[SkeletalMeshEditor] Source-file skeletal import removed — rigged import now comes from StrataPack skeletal v2. File: {sourceFile}");
        }
        else
        {
            Console.WriteLine($"[SkeletalMeshEditor] SourceFile missing or not found: '{sourceFile}'");
        }

        // Fallback: raw source files are no longer imported — use Ease + .stratapack.
        // (GLTFImportHandler path removed with the parsers.)

        // Last resort: try AnimationAsset.Load (different binary format)
        try
        {
            var skelAsset = Motif.AnimationAsset.Load(_assetPath);
            if (skelAsset?.Mesh != null && skelAsset.Mesh.Bones.Length > 0)
            {
                _skeletalMesh = skelAsset.Mesh;
                _boneCount = skelAsset.Mesh.Bones.Length;
                _vertexCount = skelAsset.Mesh.Vertices.Length;
                _triangleCount = skelAsset.Mesh.Indices.Length / 3;
                _boundsMin = skelAsset.Mesh.Bounds.Min;
                _boundsMax = skelAsset.Mesh.Bounds.Max;
                Console.WriteLine($"[SkeletalMeshEditor] Loaded bone data: {_boneCount} bones from AnimationAsset");
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkeletalMeshEditor] AnimationAsset.Load failed: {ex.Message}");
        }

        Console.WriteLine($"[SkeletalMeshEditor] Could not load bone data for: {_assetPath}");
    }

    private void LoadPhysicsAsset()
    {
        _physicsAssetPath = Path.ChangeExtension(_assetPath, ".bsphy");

        // Check for .bsphy next to the asset
        if (File.Exists(_physicsAssetPath))
        {
            _physicsAsset = PhysicsAsset.Load(_physicsAssetPath) ?? new PhysicsAsset();
            Console.WriteLine($"[SkeletalMeshEditor] Loaded physics asset: {_physicsAsset.Bodies.Count} bodies");
        }
        else
        {
            _physicsAsset = new PhysicsAsset { Name = _meshName + "_Physics" };
            Console.WriteLine($"[SkeletalMeshEditor] No existing .bsphy found, created new");
        }

        // Sync body list with current bones if empty
        if (_physicsAsset.Bodies.Count == 0 && _skeletalMesh != null)
        {
            SyncBodiesWithSkeleton();
        }
    }

    private void SyncBodiesWithSkeleton()
    {
        if (_skeletalMesh == null) return;

        // Keep existing bodies matched by name, add new ones for unmatched bones
        var existing = new Dictionary<string, PhysicsBody>();
        foreach (var b in _physicsAsset.Bodies)
            existing[b.BoneName] = b;

        _physicsAsset.Bodies.Clear();
        for (int i = 0; i < _skeletalMesh.Bones.Length; i++)
        {
            var bone = _skeletalMesh.Bones[i];
            if (existing.TryGetValue(bone.Name, out var existingBody))
            {
                existingBody.BoneIndex = i;
                _physicsAsset.Bodies.Add(existingBody);
            }
            else
            {
                _physicsAsset.Bodies.Add(new PhysicsBody
                {
                    BoneName = bone.Name,
                    BoneIndex = i,
                    bEnabled = true,
                    Mass = 1.0f,
                    Shapes = new List<PhysicsShape>
                    {
                        new PhysicsShape
                        {
                            ShapeType = PhysicsShapeType.Box,
                            Size = Vector3.One * 0.5f,
                            Radius = 0.25f,
                            Height = 0.5f
                        }
                    }
                });
            }
        }
        _isPhysicsDirty = true;
        Console.WriteLine($"[SkeletalMeshEditor] Synced {_physicsAsset.Bodies.Count} bodies with skeleton");
        AutoDetectBoneShapes();
    }

    // ════════════════════════════════════════════════════════════════════════
    // AUTO-DETECT SHAPES
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Analyzes per-bone vertex distributions and auto-selects the best shape type.
    /// Replaces each body's shapes with a single auto-detected shape.
    /// </summary>
    private void AutoDetectBoneShapes()
    {
        if (_skeletalMesh == null) return;

        var bones = _skeletalMesh.Bones;
        var boneWorlds = new System.Numerics.Matrix4x4[bones.Length];
        ComputeBoneWorldTransforms(bones, boneWorlds);

        foreach (var body in _physicsAsset.Bodies)
        {
            if (body.BoneIndex < 0 || body.BoneIndex >= bones.Length)
                continue;
            if (!body.bEnabled) continue;

            var verts = GetBoneVertices(body.BoneIndex);
            if (verts.Count < 3)
            {
                if (body.Shapes.Count == 0)
                    body.Shapes.Add(new PhysicsShape { ShapeType = PhysicsShapeType.Box, Size = new Vector3(0.1f) });
                continue;
            }

            // Compute AABB
            Vector3 bmin = new(float.MaxValue), bmax = new(float.MinValue), center = Vector3.Zero;
            foreach (var v in verts)
            {
                bmin = Vector3.Min(bmin, v);
                bmax = Vector3.Max(bmax, v);
                center += v;
            }
            center /= verts.Count;
            Vector3 extents = (bmax - bmin) * 0.5f;

            // Sort extents descending: e0 >= e1 >= e2 (half-extents)
            float[] e = { extents.X, extents.Y, extents.Z };
            Array.Sort(e);
            float e0 = e[2], e1 = e[1], e2 = e[0];

            float ratioLong = e0 / Math.Max(e2, 0.001f);
            float ratioMid  = e1 / Math.Max(e2, 0.001f);

            PhysicsShapeType chosenType;
            if (ratioLong < 1.5f)
                chosenType = PhysicsShapeType.Sphere;
            else if (ratioLong > 4.0f)
                chosenType = PhysicsShapeType.Capsule;
            else if (ratioMid < 2.0f)
                chosenType = PhysicsShapeType.Box;
            else
                chosenType = PhysicsShapeType.ConvexHull;

            // Bone position in mesh space — center becomes relative offset
            var boneWorld = boneWorlds[body.BoneIndex];
            var bonePos = new Vector3(boneWorld.Translation.X, boneWorld.Translation.Y, boneWorld.Translation.Z);
            Vector3 relativeCenter = center - bonePos;

            // Compute shape parameters
            float radius, height;
            Vector3 size;
            List<Vector3>? hullVerts = null;
            List<int>? hullIdx = null;
            switch (chosenType)
            {
                case PhysicsShapeType.Sphere:
                    float maxDist = 0;
                    foreach (var v in verts)
                        maxDist = Math.Max(maxDist, Vector3.Distance(v, center));
                    radius = Math.Max(maxDist, 0.01f);
                    height = radius * 2f;
                    size = bmax - bmin;
                    break;

                case PhysicsShapeType.Capsule:
                    float capR = Math.Max(e1, 0.01f);
                    radius = capR;
                    height = Math.Max(2f * (e0 - capR), 0.01f);
                    size = bmax - bmin;
                    break;

                case PhysicsShapeType.ConvexHull:
                    var (computedVerts, computedIdx) = ComputeConvexHull(verts);
                    for (int hv = 0; hv < computedVerts.Count; hv++)
                        computedVerts[hv] = computedVerts[hv] - bonePos;
                    hullVerts = computedVerts;
                    hullIdx = computedIdx;
                    float hullDist = 0;
                    foreach (var v in verts)
                        hullDist = Math.Max(hullDist, Vector3.Distance(v, center));
                    radius = Math.Max(hullDist, 0.01f);
                    height = 0;
                    size = bmax - bmin;
                    break;

                default: // Box
                    radius = 0;
                    height = 0;
                    size = new Vector3(
                        Math.Max(bmax.X - bmin.X, 0.01f),
                        Math.Max(bmax.Y - bmin.Y, 0.01f),
                        Math.Max(bmax.Z - bmin.Z, 0.01f));
                    break;
            }

            var newShape = new PhysicsShape
            {
                ShapeType = chosenType,
                Center = relativeCenter,
                Size = size,
                Radius = radius,
                Height = height
            };
            if (hullVerts != null) newShape.HullVertices = hullVerts;
            if (hullIdx != null) newShape.HullIndices = hullIdx;

            body.Shapes.Clear();
            body.Shapes.Add(newShape);
        }

        _isPhysicsDirty = true;
        _cookStatus = $"Detected: {_physicsAsset.Bodies.Count} bodies";
        Console.WriteLine($"[SkeletalMeshEditor] Auto-detected shapes for {_physicsAsset.Bodies.Count} bodies");
    }

    // ════════════════════════════════════════════════════════════════════════
    // COOK!
    // ════════════════════════════════════════════════════════════════════════

    private void CookPhysicsAsset()
    {
        if (_skeletalMesh == null)
        {
            _cookStatus = "No bone data loaded";
            return;
        }

        // NOTE: NO AutoDetectBoneShapes() call — preserves shape types AND measurements

        var bones = _skeletalMesh.Bones;
        var boneWorlds = new System.Numerics.Matrix4x4[bones.Length];
        ComputeBoneWorldTransforms(bones, boneWorlds);

        Console.WriteLine($"[SkeletalMeshEditor] Cooking physics asset for {bones.Length} bones...");

        foreach (var body in _physicsAsset.Bodies)
        {
            if (body.BoneIndex < 0 || body.BoneIndex >= bones.Length)
                continue;
            if (!body.bEnabled) continue;
            if (body.Shapes.Count == 0) continue;

            var boneVerts = GetBoneVertices(body.BoneIndex);
            if (boneVerts.Count == 0) continue;

            // Compute AABB center from bone vertices
            Vector3 bmin = new(float.MaxValue), bmax = new(float.MinValue);
            foreach (var v in boneVerts)
            {
                bmin = Vector3.Min(bmin, v);
                bmax = Vector3.Max(bmax, v);
            }
            Vector3 bcenter = (bmin + bmax) * 0.5f;

            // Bone position in mesh space — fix center to be relative to bone
            var boneWorld = boneWorlds[body.BoneIndex];
            var bonePos = new Vector3(boneWorld.Translation.X, boneWorld.Translation.Y, boneWorld.Translation.Z);
            Vector3 relativeCenter = bcenter - bonePos;

            // Cook each shape — updates Center (bone-relative), Mass, and Hull for ConvexHull
            // Size / Radius / Height are NEVER overwritten — those are your manual values
            foreach (var shape in body.Shapes)
            {
                shape.Center = relativeCenter;

                // ConvexHull hull data must be generated from mesh — no manual editing UI exists
                if (shape.ShapeType == PhysicsShapeType.ConvexHull)
                {
                    var (hullVerts, hullIdx) = ComputeConvexHull(boneVerts);
                    for (int hv = 0; hv < hullVerts.Count; hv++)
                        hullVerts[hv] = hullVerts[hv] - bonePos;
                    shape.HullVertices = hullVerts;
                    shape.HullIndices = hullIdx;
                }
            }

            // Re-estimate mass from current shape dimensions
            if (body.Shapes.Count > 0)
            {
                var s = body.Shapes[0];
                float volume = s.ShapeType switch
                {
                    PhysicsShapeType.Box => s.Size.X * s.Size.Y * s.Size.Z,
                    PhysicsShapeType.Sphere => (4f / 3f) * MathF.PI * s.Radius * s.Radius * s.Radius,
                    PhysicsShapeType.Capsule => MathF.PI * s.Radius * s.Radius * s.Height,
                    _ => s.Size.X * s.Size.Y * s.Size.Z
                };
                body.Mass = Math.Max(volume * 100f, 0.1f);
            }
        }

        _cookStatus = $"Cooked: {_physicsAsset.Bodies.Count} bodies";
        _isPhysicsDirty = true;
        Console.WriteLine($"[SkeletalMeshEditor] ✓ {_cookStatus}");
    }

    private List<Vector3> GetBoneVertices(int boneIndex, float minWeight = 0.01f)
    {
        var verts = new List<Vector3>();
        if (_skeletalMesh == null) return verts;

        foreach (var v in _skeletalMesh.Vertices)
        {
            bool include = false;
            if (v.BoneIndex0 == boneIndex && v.BoneWeight0 > minWeight) include = true;
            else if (v.BoneIndex1 == boneIndex && v.BoneWeight1 > minWeight) include = true;
            else if (v.BoneIndex2 == boneIndex && v.BoneWeight2 > minWeight) include = true;
            else if (v.BoneIndex3 == boneIndex && v.BoneWeight3 > minWeight) include = true;

            if (include)
                verts.Add(v.Position);
        }
        return verts;
    }

    private int GetBoneVertexCount(int boneIndex, float minWeight = 0.01f)
    {
        if (_skeletalMesh == null) return 0;
        int count = 0;
        foreach (var v in _skeletalMesh.Vertices)
        {
            if (v.BoneIndex0 == boneIndex && v.BoneWeight0 > minWeight) count++;
            else if (v.BoneIndex1 == boneIndex && v.BoneWeight1 > minWeight) count++;
            else if (v.BoneIndex2 == boneIndex && v.BoneWeight2 > minWeight) count++;
            else if (v.BoneIndex3 == boneIndex && v.BoneWeight3 > minWeight) count++;
        }
        return count;
    }

    // ════════════════════════════════════════════════════════════════════════
    // SAVE
    // ════════════════════════════════════════════════════════════════════════

    private void SavePhysicsAsset()
    {
        if (string.IsNullOrEmpty(_physicsAssetPath)) return;
        try
        {
            string dir = Path.GetDirectoryName(_physicsAssetPath) ?? "";
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (_physicsAsset.Save(_physicsAssetPath))
            {
                _isPhysicsDirty = false;
                Console.WriteLine($"[SkeletalMeshEditor] ✓ Saved .bsphy: {Path.GetFileName(_physicsAssetPath)}");
            }
            else
            {
                Console.WriteLine($"[SkeletalMeshEditor] ✗ Failed to save .bsphy");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkeletalMeshEditor] Save exception: {ex.Message}");
        }
    }

    private void SaveAll()
    {
        SavePhysicsAsset();
    }

    // ════════════════════════════════════════════════════════════════════════
    // MAIN RENDER
    // ════════════════════════════════════════════════════════════════════════

    public void Render(EditorUI ui, float x, float y, float width, float height)
    {
        if (!IsOpen || _currentAsset == null) return;

        // ── Title Bar ──
        float titleH = 50;
        ui.RoundedGradientPanel(x, y, width, titleH,
            new Vector4(0.18f, 0.22f, 0.38f, 0.95f),
            new Vector4(0.14f, 0.18f, 0.32f, 0.95f), 10f);
        ui.Panel(x + 10, y + titleH - 2, width - 20, 2, new Vector4(0.4f, 0.6f, 1.0f, 0.6f));

        ui.SetCursor(x + 20, y + 16);
        ui.Text($"🦴 Skeletal Mesh Editor", Vector4.One);
        ui.SetCursor(x + 20, y + 32);
        ui.Text(_meshName, new Vector4(0.7f, 0.85f, 1.0f, 0.9f));

        // Close button
        float closeX = x + width - 50;
        if (ui.ButtonEx(closeX, y + 10, 35, 30, "✕",
            ui.IsHovering(closeX, y + 10, 35, 30)
                ? new Vector4(0.95f, 0.35f, 0.35f, 1f)
                : new Vector4(0.7f, 0.25f, 0.25f, 0.8f),
            new Vector4(1f, 0.45f, 0.45f, 1f), new Vector4(0.6f, 0.20f, 0.20f, 0.9f),
            Vector4.Zero, Vector4.One))
        {
            IsOpen = false;
            return;
        }

        float contentY = y + titleH + 10;
        float contentH = height - titleH - 20;

        // ── Tab Bar ──
        float tabBarH = 45;
        RenderTabBar(ui, x + 15, contentY, width - 30, tabBarH);
        contentY += tabBarH + 15;
        contentH -= tabBarH + 15;

        // ── Split Layout ──
        float leftW = width * 0.48f;
        float rightW = width * 0.48f;
        float gutter = width * 0.04f;

        RenderLeftPanel(ui, x + 15, contentY, leftW, contentH - 60);
        RenderPreviewPanel(ui, x + leftW + gutter + 15, contentY, rightW, contentH - 60);

        // ── Action Bar ──
        RenderActionBar(ui, x + 15, y + height - 55, width - 30, 45);
    }

    // ════════════════════════════════════════════════════════════════════════
    // TAB BAR
    // ════════════════════════════════════════════════════════════════════════

    private void RenderTabBar(EditorUI ui, float x, float y, float width, float height)
    {
        string[] tabs = { "🦴 Bones", "🔧 Physics", "ℹ️ Info" };
        float tabW = width / tabs.Length;

        ui.RoundedGradientPanel(x, y, width, height,
            new Vector4(0.12f, 0.13f, 0.16f, 0.9f),
            new Vector4(0.10f, 0.11f, 0.14f, 0.9f), 8f);

        for (int i = 0; i < tabs.Length; i++)
        {
            float tx = x + i * tabW + 2;
            float tw = tabW - 4;
            bool isSelected = _selectedTab == i;
            bool isHovered = ui.IsHovering(tx, y, tw, height);

            if (!_hoverAnim.ContainsKey(i + 100)) _hoverAnim[i + 100] = 0f;
            float target = (isHovered || isSelected) ? 1f : 0f;
            _hoverAnim[i + 100] += (target - _hoverAnim[i + 100]) * 0.15f;
            float ha = _hoverAnim[i + 100];

            Vector4 bg = isSelected
                ? new Vector4(0.22f + ha * 0.03f, 0.35f + ha * 0.05f, 0.55f + ha * 0.05f, 0.95f)
                : new Vector4(0.14f + ha * 0.02f, 0.15f + ha * 0.02f, 0.18f + ha * 0.02f, 0.85f);

            ui.RoundedPanel(tx, y + 3, tw, height - 6, bg, 6f);

            Vector4 textColor = isSelected ? Vector4.One : new Vector4(0.7f, 0.7f, 0.7f, 0.8f + ha * 0.2f);
            ui.SetCursor(tx + 10, y + (height - 14) / 2);
            ui.Text(tabs[i], textColor);

            if (isSelected)
                ui.Panel(tx + 5, y + height - 5, tw - 10, 3, new Vector4(0.4f, 0.65f, 1.0f, 1f));

            if (isHovered && ui.IsMouseDown)
            {
                _previousTab = _selectedTab;
                _selectedTab = i;
                _tabTransition = 0f;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // LEFT PANEL
    // ════════════════════════════════════════════════════════════════════════

    private void RenderLeftPanel(EditorUI ui, float x, float y, float width, float height)
    {
        ui.RoundedPanel(x, y, width, height, new Vector4(0.14f, 0.15f, 0.18f, 0.95f), 8f);

        switch (_selectedTab)
        {
            case 0: RenderBonesTab(ui, x + 10, y + 10, width - 20, height - 20); break;
            case 1: RenderPhysicsTab(ui, x + 10, y + 10, width - 20, height - 20); break;
            case 2: RenderInfoTab(ui, x + 10, y + 10, width - 20, height - 20); break;
        }
    }

    // ── Bones Tab ──

    private void RenderBonesTab(EditorUI ui, float x, float y, float width, float height)
    {
        ui.SetCursor(x, y);
        ui.Text("Bone Hierarchy", Vector4.One);
        y += 30;

        if (_skeletalMesh == null || _skeletalMesh.Bones.Length == 0)
        {
            ui.RoundedPanel(x, y, width, 80, new Vector4(0.3f, 0.2f, 0.2f, 0.3f), 6f);
            ui.SetCursor(x + 15, y + 15);
            ui.Text("No bone data loaded", new Vector4(0.7f, 0.5f, 0.5f, 1f));
            ui.SetCursor(x + 15, y + 40);
            ui.Text("Open a skeletal mesh asset to view bones", new Vector4(0.5f, 0.5f, 0.5f, 1f));
            return;
        }

        int boneCount = _skeletalMesh.Bones.Length;
        float rowH = 26;
        float scrollH = height - 40;
        float contentH = boneCount * rowH + 20;

        float scrollOff = ui.BeginScrollArea("SkelEditor_Bones", x, y, width, scrollH, contentH);

        for (int i = 0; i < boneCount; i++)
        {
            var bone = _skeletalMesh.Bones[i];
            float drawY = y + i * rowH - scrollOff;
            if (drawY + rowH < y || drawY > y + scrollH) continue;

            bool isSelected = _selectedBoneIndex == i;
            bool isHovered = ui.IsHovering(x, drawY, width, rowH);

            Vector4 bg = isSelected
                ? new Vector4(0.25f, 0.40f, 0.60f, 0.9f)
                : isHovered
                    ? new Vector4(0.18f, 0.20f, 0.24f, 0.9f)
                    : new Vector4(0.14f, 0.15f, 0.18f, 0.5f);

            ui.RoundedPanel(x, drawY, width, rowH - 2, bg, 4f);

            // Indent by depth
            int depth = 0;
            int parentIdx = bone.ParentIndex;
            while (parentIdx >= 0 && parentIdx < boneCount && depth < 8)
            {
                depth++;
                parentIdx = _skeletalMesh.Bones[parentIdx].ParentIndex;
            }
            float indentX = x + 8 + depth * 14;

            // Tree connector
            if (depth > 0)
            {
                ui.Panel(indentX - 10, drawY + rowH / 2 - 1, 8, 1, new Vector4(0.4f, 0.4f, 0.4f, 0.5f));
            }

            // Leaf icon
            bool isLeaf = bone.Children == null || bone.Children.Count == 0;
            string icon = isLeaf ? "◇" : "▸";
            ui.SetCursor(indentX, drawY + 6);
            ui.Text($"{icon} {bone.Name}", isSelected ? Vector4.One : new Vector4(0.8f, 0.8f, 0.8f, 1f));

            if (isHovered && ui.IsMouseDown)
                _selectedBoneIndex = i;
        }

        ui.EndScrollArea("SkelEditor_Bones");
    }

    // ── Physics Tab ──

    private void RenderPhysicsTab(EditorUI ui, float x, float y, float width, float height)
    {
        // Header with Detect and Cook buttons
        ui.SetCursor(x, y);
        ui.Text("Physics Bodies", Vector4.One);

        // Detect button — runs AutoDetectBoneShapes (overrides types + measurements)
        float detectW = 90;
        float cookW = 110;
        float btnGap = 6;
        float cookX = x + width - cookW;
        float detectX = cookX - detectW - btnGap;

        Vector4 detectBg = ui.IsHovering(detectX, y - 4, detectW, 28)
            ? new Vector4(0.3f, 0.55f, 0.75f, 1f)
            : new Vector4(0.25f, 0.45f, 0.65f, 0.95f);

        if (ui.ButtonEx(detectX, y - 4, detectW, 28, "🔍 Detect",
            detectBg, new Vector4(0.35f, 0.6f, 0.8f, 1f), new Vector4(0.2f, 0.4f, 0.6f, 0.95f),
            Vector4.Zero, Vector4.One))
        {
            AutoDetectBoneShapes();
        }

        // Cook! button — only refines measurements, preserves shape types
        Vector4 cookBg = ui.IsHovering(cookX, y - 4, cookW, 28)
            ? new Vector4(0.9f, 0.55f, 0.15f, 1f)
            : new Vector4(0.85f, 0.50f, 0.10f, 0.95f);

        if (ui.ButtonEx(cookX, y - 4, cookW, 28, "⚡ Cook!",
            cookBg, new Vector4(1f, 0.65f, 0.2f, 1f), new Vector4(0.8f, 0.45f, 0.08f, 0.95f),
            Vector4.Zero, Vector4.One))
        {
            CookPhysicsAsset();
        }

        y += 32;

        // Status bar
        if (!string.IsNullOrEmpty(_cookStatus))
        {
            ui.RoundedPanel(x, y, width, 22, new Vector4(0.2f, 0.35f, 0.2f, 0.4f), 4f);
            ui.SetCursor(x + 10, y + 4);
            ui.Text(_cookStatus, new Vector4(0.5f, 0.9f, 0.5f, 1f));
            y += 28;
        }

        // Dirty indicator
        if (_isPhysicsDirty)
        {
            ui.RoundedPanel(x, y, width, 22, new Vector4(0.4f, 0.3f, 0.1f, 0.4f), 4f);
            ui.SetCursor(x + 10, y + 4);
            ui.Text("Unsaved changes", new Vector4(1f, 0.8f, 0.3f, 1f));
            y += 28;
        }

        // Bone body list
        if (_physicsAsset.Bodies.Count == 0)
        {
            ui.RoundedPanel(x, y, width, 80, new Vector4(0.3f, 0.2f, 0.2f, 0.3f), 6f);
            ui.SetCursor(x + 15, y + 15);
            ui.Text("No physics bodies", new Vector4(0.7f, 0.5f, 0.5f, 1f));
            ui.SetCursor(x + 15, y + 40);
            ui.Text("Click Cook! to generate from skeleton", new Vector4(0.5f, 0.5f, 0.5f, 1f));
            return;
        }

        float scrollH = height - (y - (y - 32)) - 30;
        float bodyRowH = 30;
        float expandedH = 200;
        float contentH = 0;
        for (int i = 0; i < _physicsAsset.Bodies.Count; i++)
            contentH += _selectedBoneIndex == i ? expandedH : bodyRowH;

        float scrollOff = ui.BeginScrollArea("SkelEditor_Physics", x, y, width, scrollH, contentH);

        float curY = y;
        for (int i = 0; i < _physicsAsset.Bodies.Count; i++)
        {
            var body = _physicsAsset.Bodies[i];
            bool isExpanded = _selectedBoneIndex == i;
            float rowH = isExpanded ? expandedH : bodyRowH;
            float drawY = curY - scrollOff;

            if (drawY + rowH < y || drawY > y + scrollH)
            {
                curY += rowH;
                continue;
            }

            // Body row background
            Vector4 bg = body.bEnabled
                ? new Vector4(0.16f, 0.18f, 0.22f, 0.9f)
                : new Vector4(0.14f, 0.14f, 0.16f, 0.7f);

            if (isExpanded)
                bg = new Vector4(0.20f, 0.25f, 0.35f, 0.95f);

            ui.RoundedPanel(x, drawY, width, rowH - 2, bg, 6f);

            // Enable checkbox
            string checkMark = body.bEnabled ? "☑" : "☐";
            if (ui.ButtonEx(x + 5, drawY + 3, 22, 22, checkMark,
                body.bEnabled ? new Vector4(0.3f, 0.6f, 0.3f, 0.8f) : new Vector4(0.3f, 0.3f, 0.3f, 0.5f),
                new Vector4(0.4f, 0.7f, 0.4f, 1f), new Vector4(0.25f, 0.5f, 0.25f, 0.8f),
                Vector4.Zero, Vector4.One))
            {
                body.bEnabled = !body.bEnabled;
                _isPhysicsDirty = true;
            }

            // Bone name
            ui.SetCursor(x + 32, drawY + 7);
            string boneName = body.BoneName.Length > 22 ? body.BoneName[..20] + ".." : body.BoneName;
            ui.Text(boneName, body.bEnabled ? Vector4.One : new Vector4(0.5f, 0.5f, 0.5f, 0.8f));

            // Shape type selector
            float selX = x + width - 120;
            string[] shapeTypes = { "Box", "Sphere", "Capsule", "Convex" };
            string[] shapeLabels = { "B", "S", "A", "C" };
            for (int s = 0; s < shapeTypes.Length; s++)
            {
                float sx = selX + s * 28;
                bool isActive = body.Shapes.Count > 0 &&
                    body.Shapes[0].ShapeType.ToString() == shapeTypes[s] ||
                    (shapeTypes[s] == "Convex" && body.Shapes.Count > 0 && body.Shapes[0].ShapeType == PhysicsShapeType.ConvexHull);

                // Simple check: match by index
                bool typeMatch = false;
                if (body.Shapes.Count > 0)
                {
                    typeMatch = shapeTypes[s] switch
                    {
                        "Box" => body.Shapes[0].ShapeType == PhysicsShapeType.Box,
                        "Sphere" => body.Shapes[0].ShapeType == PhysicsShapeType.Sphere,
                        "Capsule" => body.Shapes[0].ShapeType == PhysicsShapeType.Capsule,
                        "Convex" => body.Shapes[0].ShapeType == PhysicsShapeType.ConvexHull,
                        _ => false
                    };
                }

                Vector4 btnBg = typeMatch
                    ? new Vector4(0.3f, 0.55f, 0.8f, 0.9f)
                    : new Vector4(0.18f, 0.19f, 0.22f, 0.8f);

                uint shapeBtnId = (uint)(5000 + i * 10 + s);
                if (ui.ButtonEx(sx, drawY + 4, 26, 20, shapeLabels[s],
                    btnBg, new Vector4(0.35f, 0.6f, 0.85f, 1f), new Vector4(0.25f, 0.5f, 0.75f, 0.9f),
                    Vector4.Zero, new Vector4(0.9f, 0.9f, 0.9f, 1f), shapeBtnId))
                {
                    if (body.Shapes.Count == 0)
                        body.Shapes.Add(new PhysicsShape());

                    body.Shapes[0].ShapeType = shapeTypes[s] switch
                    {
                        "Box" => PhysicsShapeType.Box,
                        "Sphere" => PhysicsShapeType.Sphere,
                        "Capsule" => PhysicsShapeType.Capsule,
                        "Convex" => PhysicsShapeType.ConvexHull,
                        _ => PhysicsShapeType.Box
                    };
                    _isPhysicsDirty = true;
                }
            }

            // Remove button
            if (ui.ButtonEx(x + width - 25, drawY + 4, 20, 20, "×",
                new Vector4(0.6f, 0.25f, 0.25f, 0.7f),
                new Vector4(0.8f, 0.3f, 0.3f, 1f), new Vector4(0.5f, 0.2f, 0.2f, 0.8f),
                Vector4.Zero, Vector4.One, (uint)(6000 + i)))
            {
                _physicsAsset.Bodies.RemoveAt(i);
                _isPhysicsDirty = true;
                if (_selectedBoneIndex >= _physicsAsset.Bodies.Count)
                    _selectedBoneIndex = _physicsAsset.Bodies.Count - 1;
                break;
            }

            // Click to expand
            if (ui.IsHovering(x + 32, drawY, width - 160, bodyRowH) && ui.IsMouseDown)
            {
                _selectedBoneIndex = isExpanded ? -1 : i;
            }

            // Expanded shape properties
            if (isExpanded && body.Shapes.Count > 0)
            {
                var shape = body.Shapes[0];
                float propY = drawY + bodyRowH;
                float labelCol = x + 20;
                float valCol = x + 100;
                float sliderW = width - 130;

                // Center (read-only display)
                ui.SetCursor(labelCol, propY + 2);
                ui.Text("Center", new Vector4(0.6f, 0.7f, 0.8f, 1f));
                ui.SetCursor(valCol, propY + 2);
                ui.Text($"({shape.Center.X:F2}, {shape.Center.Y:F2}, {shape.Center.Z:F2})", new Vector4(0.8f, 0.8f, 0.8f, 1f));
                propY += 22;

                // Vertex count per bone (mesh info)
                int vertCount = GetBoneVertexCount(body.BoneIndex);
                ui.SetCursor(labelCol, propY + 2);
                ui.Text("Verts (>1%)", new Vector4(0.6f, 0.7f, 0.8f, 1f));
                ui.SetCursor(valCol, propY + 2);
                Vector4 vertCol = vertCount >= 3
                    ? new Vector4(0.5f, 0.9f, 0.6f, 1f)
                    : new Vector4(1f, 0.5f, 0.5f, 1f);
                ui.Text($"{vertCount}", vertCol);
                propY += 20;

                // Type-specific properties
                if (shape.ShapeType == PhysicsShapeType.Box)
                {
                    float sx = shape.Size.X, sy = shape.Size.Y, sz = shape.Size.Z;
                    RenderShapeSlider(ui, "Size X", ref sx, 0.01f, 10f, labelCol, valCol, sliderW, ref propY, ref _isPhysicsDirty);
                    RenderShapeSlider(ui, "Size Y", ref sy, 0.01f, 10f, labelCol, valCol, sliderW, ref propY, ref _isPhysicsDirty);
                    RenderShapeSlider(ui, "Size Z", ref sz, 0.01f, 10f, labelCol, valCol, sliderW, ref propY, ref _isPhysicsDirty);
                    shape.Size = new Vector3(sx, sy, sz);
                }
                else if (shape.ShapeType == PhysicsShapeType.Sphere)
                {
                    float rad = shape.Radius;
                    RenderShapeSlider(ui, "Radius", ref rad, 0.01f, 5f, labelCol, valCol, sliderW, ref propY, ref _isPhysicsDirty);
                    shape.Radius = rad;
                }
                else if (shape.ShapeType == PhysicsShapeType.Capsule)
                {
                    float rad = shape.Radius;
                    float hgt = shape.Height;
                    RenderShapeSlider(ui, "Radius", ref rad, 0.01f, 5f, labelCol, valCol, sliderW, ref propY, ref _isPhysicsDirty);
                    RenderShapeSlider(ui, "Height", ref hgt, 0.01f, 10f, labelCol, valCol, sliderW, ref propY, ref _isPhysicsDirty);
                    shape.Radius = rad;
                    shape.Height = hgt;
                }
                else if (shape.ShapeType == PhysicsShapeType.ConvexHull)
                {
                    float rad = shape.Radius;
                    RenderShapeSlider(ui, "Radius", ref rad, 0.01f, 5f, labelCol, valCol, sliderW, ref propY, ref _isPhysicsDirty);
                    shape.Radius = rad;
                }

                // Mass, Friction, Restitution
                float mass = body.Mass;
                float friction = shape.Friction;
                float bounce = shape.Restitution;
                RenderShapeSlider(ui, "Mass", ref mass, 0.1f, 10000f, labelCol, valCol, sliderW, ref propY, ref _isPhysicsDirty);
                RenderShapeSlider(ui, "Friction", ref friction, 0f, 1f, labelCol, valCol, sliderW, ref propY, ref _isPhysicsDirty);
                RenderShapeSlider(ui, "Bounce", ref bounce, 0f, 1f, labelCol, valCol, sliderW, ref propY, ref _isPhysicsDirty);
                body.Mass = mass;
                shape.Friction = friction;
                shape.Restitution = bounce;

                // Trigger toggle
                ui.SetCursor(labelCol, propY + 2);
                ui.Text("Trigger", new Vector4(0.6f, 0.7f, 0.8f, 1f));
                if (ui.ButtonEx(valCol, propY, 40, 20, shape.IsTrigger ? "ON" : "OFF",
                    shape.IsTrigger ? new Vector4(0.4f, 0.7f, 0.4f, 0.8f) : new Vector4(0.3f, 0.3f, 0.3f, 0.5f),
                    new Vector4(0.45f, 0.75f, 0.45f, 1f), new Vector4(0.35f, 0.65f, 0.35f, 0.8f),
                    Vector4.Zero, Vector4.One))
                {
                    shape.IsTrigger = !shape.IsTrigger;
                    _isPhysicsDirty = true;
                }
                propY += 24;

                // Add Shape button
                if (ui.ButtonEx(labelCol, propY, width - 40, 22, "+ Add Shape",
                    new Vector4(0.25f, 0.40f, 0.55f, 0.8f),
                    new Vector4(0.30f, 0.45f, 0.60f, 1f),
                    new Vector4(0.20f, 0.35f, 0.50f, 0.8f),
                    Vector4.Zero, new Vector4(0.9f, 0.9f, 0.9f, 1f)))
                {
                    body.Shapes.Add(new PhysicsShape { ShapeType = PhysicsShapeType.Box, Size = Vector3.One * 0.3f });
                    _isPhysicsDirty = true;
                }
            }

            curY += rowH;
        }

        ui.EndScrollArea("SkelEditor_Physics");
    }

    private void RenderShapeSlider(EditorUI ui, string label, ref float value, float min, float max,
        float labelCol, float valCol, float sliderW, ref float y, ref bool dirty)
    {
        ui.SetCursor(labelCol, y + 2);
        ui.Text(label, new Vector4(0.6f, 0.7f, 0.8f, 1f));
        ui.SetCursor(valCol, y);
        float v = value;
        if (ui.Slider(ref v, min, max, sliderW, 14))
        {
            value = v;
            dirty = true;
        }
        y += 20;
    }

    // ── Info Tab ──

    private void RenderInfoTab(EditorUI ui, float x, float y, float width, float height)
    {
        ui.SetCursor(x, y);
        ui.Text("Mesh Information", Vector4.One);
        y += 35;

        float cardW = width;
        float cardH = 90;

        // Stats card
        ui.RoundedPanel(x, y, cardW, cardH, new Vector4(0.16f, 0.20f, 0.26f, 0.8f), 6f);
        ui.SetCursor(x + 15, y + 10);
        ui.Text("Geometry", new Vector4(0.7f, 0.8f, 0.9f, 1f));
        ui.SetCursor(x + 15, y + 32);
        ui.Text($"Vertices: {_vertexCount:N0}", new Vector4(0.85f, 0.85f, 0.85f, 1f));
        ui.SetCursor(x + 15, y + 52);
        ui.Text($"Triangles: {_triangleCount:N0}", new Vector4(0.85f, 0.85f, 0.85f, 1f));
        ui.SetCursor(x + 15, y + 72);
        ui.Text($"Bones: {_boneCount}", new Vector4(0.85f, 0.85f, 0.85f, 1f));
        y += cardH + 10;

        // Bounds card
        Vector3 size = _boundsMax - _boundsMin;
        ui.RoundedPanel(x, y, cardW, 80, new Vector4(0.20f, 0.16f, 0.26f, 0.8f), 6f);
        ui.SetCursor(x + 15, y + 10);
        ui.Text("Bounding Box", new Vector4(0.8f, 0.7f, 0.9f, 1f));
        ui.SetCursor(x + 15, y + 32);
        ui.Text($"Size: {size.X:F2} x {size.Y:F2} x {size.Z:F2}", new Vector4(0.85f, 0.85f, 0.85f, 1f));
        ui.SetCursor(x + 15, y + 52);
        var center = (_boundsMin + _boundsMax) * 0.5f;
        ui.Text($"Center: {center.X:F2}, {center.Y:F2}, {center.Z:F2}", new Vector4(0.85f, 0.85f, 0.85f, 1f));
        y += 90;

        // Physics info
        ui.RoundedPanel(x, y, cardW, 70, new Vector4(0.16f, 0.26f, 0.20f, 0.8f), 6f);
        ui.SetCursor(x + 15, y + 10);
        ui.Text("Physics Asset", new Vector4(0.7f, 0.9f, 0.7f, 1f));
        ui.SetCursor(x + 15, y + 32);
        ui.Text($"Bodies: {_physicsAsset.Bodies.Count}", new Vector4(0.85f, 0.85f, 0.85f, 1f));
        ui.SetCursor(x + 15, y + 52);
        string status = _isPhysicsDirty ? "Modified (unsaved)" : (File.Exists(_physicsAssetPath) ? "Saved" : "Not created");
        ui.Text($"Status: {status}", _isPhysicsDirty ? new Vector4(1f, 0.8f, 0.3f, 1f) : new Vector4(0.85f, 0.85f, 0.85f, 1f));
    }

    // ════════════════════════════════════════════════════════════════════════
    // PREVIEW PANEL
    // ════════════════════════════════════════════════════════════════════════

    private void RenderPreviewPanel(EditorUI ui, float x, float y, float width, float height)
    {
        // Header
        float headerH = 35;
        ui.RoundedGradientPanel(x, y, width, headerH,
            new Vector4(0.18f, 0.22f, 0.30f, 0.9f),
            new Vector4(0.14f, 0.18f, 0.26f, 0.9f), 6f);
        ui.SetCursor(x + 15, y + 10);
        ui.Text("3D Preview", new Vector4(1f, 1f, 1f, 1f));

        // Auto-rotate toggle
        Vector4 rotBg = _autoRotate
            ? new Vector4(0.4f, 0.8f, 0.6f, 0.9f)
            : new Vector4(0.2f, 0.22f, 0.26f, 0.8f);
        if (ui.ButtonEx(x + width - 50, y + 5, 38, 26, "Rot",
            rotBg,
            new Vector4(rotBg.X + 0.05f, rotBg.Y + 0.05f, rotBg.Z + 0.05f, 1f),
            new Vector4(rotBg.X - 0.05f, rotBg.Y - 0.05f, rotBg.Z - 0.05f, 0.9f),
            Vector4.Zero, Vector4.One))
        {
            _autoRotate = !_autoRotate;
        }

        // Viewport area
        float vpY = y + headerH + 5;
        float vpH = height - headerH - 100;
        float vpW = width - 10;
        float vpX = x + 5;

        PreviewRect = new Vector4(vpX, vpY, vpW, vpH);

        // Stats below
        float statsY = vpY + vpH + 12;
        float cardW = (width - 30) / 3;

        ui.RoundedPanel(x + 5, statsY, cardW, 45, new Vector4(0.16f, 0.20f, 0.26f, 0.9f), 4f);
        ui.SetCursor(x + 12, statsY + 8);
        ui.Text("Vertices", new Vector4(0.6f, 0.7f, 0.8f, 1f));
        ui.SetCursor(x + 12, statsY + 25);
        ui.Text($"{_vertexCount:N0}", new Vector4(0.9f, 0.9f, 0.9f, 1f));

        ui.RoundedPanel(x + cardW + 10, statsY, cardW, 45, new Vector4(0.20f, 0.16f, 0.26f, 0.9f), 4f);
        ui.SetCursor(x + cardW + 17, statsY + 8);
        ui.Text("Triangles", new Vector4(0.7f, 0.6f, 0.8f, 1f));
        ui.SetCursor(x + cardW + 17, statsY + 25);
        ui.Text($"{_triangleCount:N0}", new Vector4(0.9f, 0.9f, 0.9f, 1f));

        ui.RoundedPanel(x + cardW * 2 + 15, statsY, cardW, 45, new Vector4(0.16f, 0.26f, 0.20f, 0.9f), 4f);
        ui.SetCursor(x + cardW * 2 + 22, statsY + 8);
        ui.Text("Bones", new Vector4(0.6f, 0.8f, 0.7f, 1f));
        ui.SetCursor(x + cardW * 2 + 22, statsY + 25);
        ui.Text($"{_boneCount}", new Vector4(0.9f, 0.9f, 0.9f, 1f));

        // Camera hint
        ui.SetCursor(x + 10, y + height - 30);
        ui.Text("Right-click drag: rotate  |  Scroll: zoom", new Vector4(0.4f, 0.4f, 0.4f, 0.8f));
    }

    // ════════════════════════════════════════════════════════════════════════
    // ACTION BAR
    // ════════════════════════════════════════════════════════════════════════

    private void RenderActionBar(EditorUI ui, float x, float y, float width, float height)
    {
        ui.RoundedPanel(x, y, width, height, new Vector4(0.12f, 0.13f, 0.16f, 0.95f), 6f);

        bool hasChanges = _isDirty || _isPhysicsDirty;
        float btnW = 120;

        // Save button
        Vector4 saveBg = hasChanges
            ? new Vector4(0.2f, 0.55f, 0.3f, 0.95f)
            : new Vector4(0.2f, 0.3f, 0.25f, 0.6f);

        if (ui.ButtonEx(x + 10, y + 8, btnW, 30, "Save All",
            saveBg,
            new Vector4(0.25f, 0.65f, 0.35f, 1f),
            new Vector4(0.18f, 0.50f, 0.28f, 0.95f),
            Vector4.Zero, Vector4.One))
        {
            SaveAll();
        }

        // Save .bsphy only
        if (ui.ButtonEx(x + 140, y + 8, 130, 30, "Save .bsphy",
            new Vector4(0.3f, 0.35f, 0.5f, 0.9f),
            new Vector4(0.35f, 0.40f, 0.55f, 1f),
            new Vector4(0.25f, 0.30f, 0.45f, 0.9f),
            Vector4.Zero, Vector4.One))
        {
            SavePhysicsAsset();
        }

        // Status
        float statusX = x + width - 250;
        ui.SetCursor(statusX, y + 15);
        if (hasChanges)
            ui.Text("* Unsaved changes", new Vector4(1f, 0.7f, 0.3f, 1f));
        else
            ui.Text("All saved", new Vector4(0.5f, 0.7f, 0.5f, 0.8f));

        // Filename
        ui.SetCursor(statusX, y + 30);
        ui.Text(Path.GetFileName(_assetPath), new Vector4(0.4f, 0.4f, 0.4f, 0.7f));
    }

    // ════════════════════════════════════════════════════════════════════════
    // UPDATE (called from main loop)
    // ════════════════════════════════════════════════════════════════════════

    public void Update(float deltaTime)
    {
        if (!IsOpen) return;

        if (_autoRotate)
        {
            _previewRotation += deltaTime * 0.3f;
            if (_previewRotation > MathF.PI * 2)
                _previewRotation -= MathF.PI * 2;
        }

        if (_tabTransition < 1f)
        {
            _tabTransition += deltaTime * 4f;
            if (_tabTransition > 1f) _tabTransition = 1f;
        }

        if (_hasSpawnedPreview && _lastWorld != null &&
            _lastWorld.HasComponent<Core.ECS.Builtin.TransformComponent>(_previewEntity))
        {
            ref var t = ref _lastWorld.GetComponent<Core.ECS.Builtin.TransformComponent>(_previewEntity);
            if (_autoRotate)
            {
                float yaw = _previewRotation * (180f / MathF.PI);
                t.Rotation = Core.Math.Quaternion.Euler(0, yaw, 0);
            }
        }

        // Update physics debug shapes on the viewport renderer when Physics tab is active
        UpdatePhysicsDebugShapes();
    }

    private void UpdatePhysicsDebugShapes()
    {
        var debugShapes = new List<ViewportRenderer.DebugShape>();
        var viewportRenderer = Program.GetMainViewport();
        if (viewportRenderer == null || _selectedTab != 2 || _skeletalMesh == null || _physicsAsset == null)
        {
            if (viewportRenderer != null)
                viewportRenderer._physicsDebugShapes = debugShapes;
            return;
        }

        var bones = _skeletalMesh.Bones;
        if (bones.Length == 0)
        {
            viewportRenderer._physicsDebugShapes = debugShapes;
            return;
        }

        var boneWorlds = new System.Numerics.Matrix4x4[bones.Length];
        ComputeBoneWorldTransforms(bones, boneWorlds);

        System.Numerics.Matrix4x4 entityWorld = System.Numerics.Matrix4x4.Identity;
        if (_hasSpawnedPreview && _lastWorld != null &&
            _lastWorld.HasComponent<Core.ECS.Builtin.TransformComponent>(_previewEntity))
        {
            ref var t = ref _lastWorld.GetComponent<Core.ECS.Builtin.TransformComponent>(_previewEntity);
            var pos = new System.Numerics.Vector3(t.Position.X, t.Position.Y, t.Position.Z);
            var rot = new System.Numerics.Quaternion(t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W);
            var scl = new System.Numerics.Vector3(t.Scale.X, t.Scale.Y, t.Scale.Z);
            entityWorld = System.Numerics.Matrix4x4.CreateScale(scl)
                        * System.Numerics.Matrix4x4.CreateFromQuaternion(rot)
                        * System.Numerics.Matrix4x4.CreateTranslation(pos);
        }

        foreach (var body in _physicsAsset.Bodies)
        {
            if (!body.bEnabled) continue;
            int boneIdx = body.BoneIndex;
            if (boneIdx < 0 || boneIdx >= bones.Length) continue;

            var boneWorld = boneWorlds[boneIdx];

            foreach (var shape in body.Shapes)
            {
                var center = new System.Numerics.Vector3(shape.Center.X, shape.Center.Y, shape.Center.Z);
                var sRot = new System.Numerics.Quaternion(shape.Rotation.X, shape.Rotation.Y, shape.Rotation.Z, shape.Rotation.W);
                var size = new System.Numerics.Vector3(shape.Size.X, shape.Size.Y, shape.Size.Z);

                System.Numerics.Matrix4x4 shapeLocal;
                switch (shape.ShapeType)
                {
                    case PhysicsShapeType.Sphere:
                        shapeLocal = System.Numerics.Matrix4x4.CreateScale(shape.Radius)
                                   * System.Numerics.Matrix4x4.CreateFromQuaternion(sRot)
                                   * System.Numerics.Matrix4x4.CreateTranslation(center);
                        break;
                    case PhysicsShapeType.Capsule:
                        shapeLocal = System.Numerics.Matrix4x4.CreateScale(
                                         new System.Numerics.Vector3(shape.Radius, shape.Height * 0.5f, shape.Radius))
                                   * System.Numerics.Matrix4x4.CreateFromQuaternion(sRot)
                                   * System.Numerics.Matrix4x4.CreateTranslation(center);
                        break;
                    case PhysicsShapeType.ConvexHull:
                        shapeLocal = System.Numerics.Matrix4x4.CreateFromQuaternion(sRot)
                                   * System.Numerics.Matrix4x4.CreateTranslation(center);
                        break;
                    default: // Box
                        shapeLocal = System.Numerics.Matrix4x4.CreateScale(size)
                                   * System.Numerics.Matrix4x4.CreateFromQuaternion(sRot)
                                   * System.Numerics.Matrix4x4.CreateTranslation(center);
                        break;
                }

                var transform = shapeLocal * boneWorld * entityWorld;

                var debugType = shape.ShapeType switch
                {
                    PhysicsShapeType.Sphere    => ViewportRenderer.DebugShapeType.Sphere,
                    PhysicsShapeType.Capsule   => ViewportRenderer.DebugShapeType.Capsule,
                    PhysicsShapeType.ConvexHull => ViewportRenderer.DebugShapeType.ConvexHull,
                    _                          => ViewportRenderer.DebugShapeType.Box,
                };

                var color = body.BoneIndex == _selectedBoneIndex
                    ? new System.Numerics.Vector4(0.2f, 0.9f, 0.4f, 0.45f)
                    : new System.Numerics.Vector4(0.45f, 0.3f, 0.85f, 0.3f);

                debugShapes.Add(new ViewportRenderer.DebugShape
                {
                    Transform = transform,
                    ShapeType = debugType,
                    Size = size,
                    Radius = shape.Radius,
                    Height = shape.Height,
                    Color = color,
                    HullVertices = shape.HullVertices.Count > 0
                        ? shape.HullVertices.Select(v => new System.Numerics.Vector3(v.X, v.Y, v.Z)).ToArray()
                        : null,
                    HullIndices = shape.HullIndices.Count > 0 ? shape.HullIndices.ToArray() : null,
                });
            }
        }

        viewportRenderer._physicsDebugShapes = debugShapes;
    }

    private static void ComputeBoneWorldTransforms(Bone[] bones, System.Numerics.Matrix4x4[] outWorlds)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            var local = new System.Numerics.Matrix4x4(
                bones[i].LocalBindPose.M11, bones[i].LocalBindPose.M12, bones[i].LocalBindPose.M13, bones[i].LocalBindPose.M14,
                bones[i].LocalBindPose.M21, bones[i].LocalBindPose.M22, bones[i].LocalBindPose.M23, bones[i].LocalBindPose.M24,
                bones[i].LocalBindPose.M31, bones[i].LocalBindPose.M32, bones[i].LocalBindPose.M33, bones[i].LocalBindPose.M34,
                bones[i].LocalBindPose.M41, bones[i].LocalBindPose.M42, bones[i].LocalBindPose.M43, bones[i].LocalBindPose.M44);
            int parent = bones[i].ParentIndex;
            if (parent >= 0 && parent < bones.Length)
                outWorlds[i] = local * outWorlds[parent];
            else
                outWorlds[i] = local;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3D Convex Hull (QuickHull)
    // ════════════════════════════════════════════════════════════════════════

    private static (List<Vector3> vertices, List<int> indices) ComputeConvexHull(List<Vector3> inputPoints)
    {
        if (inputPoints.Count < 4)
        {
            var verts = new List<Vector3>(inputPoints);
            var idx = new List<int>();
            for (int i = 0; i < verts.Count; i++) idx.Add(i);
            return (verts, idx);
        }

        var pts = Deduplicate(inputPoints);
        if (pts.Count < 4)
        {
            var idx = new List<int>();
            for (int i = 0; i < pts.Count; i++) idx.Add(i);
            return (pts, idx);
        }

        var hull = new QuickHullImpl();
        hull.Build(pts);
        return (hull.GetVertices(), hull.GetIndices());
    }

    private static List<Vector3> Deduplicate(List<Vector3> points)
    {
        var result = new List<Vector3>();
        var seen = new HashSet<(int, int, int)>();
        foreach (var p in points)
        {
            var key = ((int)(p.X * 1000), (int)(p.Y * 1000), (int)(p.Z * 1000));
            if (seen.Add(key)) result.Add(p);
        }
        return result;
    }

    private class QuickHullImpl
    {
        private List<Vector3> _points = new();
        private List<Face> _faces = new();

        private class Face
        {
            public int I0, I1, I2;
            public Vector3 Normal;
            public float Offset;
            public List<int> OutsidePoints = new();
        }

        private const float Eps = 1e-6f;

        public void Build(List<Vector3> points)
        {
            _points = points;
            _faces.Clear();

            FindInitialTetrahedron();
            AssignPointsToFaces();

            int iter = 0;
            while (iter++ < 10000)
            {
                Face? worstFace = null;
                float maxDist = Eps;
                foreach (var f in _faces)
                {
                    if (f.OutsidePoints.Count == 0) continue;
                    float d = DistFromPlane(_points[f.OutsidePoints[0]], f);
                    int worstIdx = f.OutsidePoints[0];
                    foreach (int pi in f.OutsidePoints)
                    {
                        float dd = DistFromPlane(_points[pi], f);
                        if (dd > d) { d = dd; worstIdx = pi; }
                    }
                    if (d > maxDist) { maxDist = d; worstFace = f; worstFace!.OutsidePoints = new List<int> { worstIdx }; }
                }

                if (worstFace == null) break;

                int apex = worstFace.OutsidePoints[0];
                var visibleFaces = new List<Face>();
                foreach (var f in _faces)
                    if (f.OutsidePoints.Count > 0 || DistFromPlane(_points[apex], f) > Eps)
                        if (IsVisible(f, apex))
                            visibleFaces.Add(f);

                var horizon = FindHorizonEdges(visibleFaces);

                foreach (var f in visibleFaces) _faces.Remove(f);

                var newFaces = new List<Face>();
                foreach (var (a, b) in horizon)
                {
                    var nf = new Face { I0 = a, I1 = b, I2 = apex };
                    ComputePlane(nf);
                    newFaces.Add(nf);
                }

                var oldOutside = new HashSet<int>();
                foreach (var f in visibleFaces)
                    foreach (int pi in f.OutsidePoints)
                        if (pi != apex) oldOutside.Add(pi);

                foreach (var nf in newFaces)
                {
                    foreach (int pi in oldOutside)
                    {
                        if (DistFromPlane(_points[pi], nf) > Eps)
                            nf.OutsidePoints.Add(pi);
                    }
                    _faces.Add(nf);
                }
            }
        }

        public List<Vector3> GetVertices()
        {
            var used = new HashSet<int>();
            foreach (var f in _faces) { used.Add(f.I0); used.Add(f.I1); used.Add(f.I2); }
            var map = new Dictionary<int, int>();
            var verts = new List<Vector3>();
            foreach (int i in used.OrderBy(x => x))
            {
                map[i] = verts.Count;
                verts.Add(_points[i]);
            }
            return verts;
        }

        public List<int> GetIndices()
        {
            var used = new HashSet<int>();
            foreach (var f in _faces) { used.Add(f.I0); used.Add(f.I1); used.Add(f.I2); }
            var map = new Dictionary<int, int>();
            var sorted = used.OrderBy(x => x).ToList();
            for (int i = 0; i < sorted.Count; i++) map[sorted[i]] = i;
            var indices = new List<int>();
            foreach (var f in _faces)
            {
                indices.Add(map[f.I0]);
                indices.Add(map[f.I1]);
                indices.Add(map[f.I2]);
            }
            return indices;
        }

        private void FindInitialTetrahedron()
        {
            int minI = 0, maxI = 0;
            for (int i = 1; i < _points.Count; i++)
            {
                if (_points[i].X < _points[minI].X) minI = i;
                if (_points[i].X > _points[maxI].X) maxI = i;
            }

            float maxD = 0; int a = minI, b = maxI;
            for (int i = 0; i < _points.Count; i++)
            {
                float d = (_points[i] - _points[minI]).LengthSquared();
                if (d > maxD) { maxD = d; a = i; }
            }
            maxD = 0;
            for (int i = 0; i < _points.Count; i++)
            {
                float d = (_points[i] - _points[a]).LengthSquared();
                if (d > maxD) { maxD = d; b = i; }
            }

            float maxCross = 0; int c = 0;
            var ab = _points[b] - _points[a];
            for (int i = 0; i < _points.Count; i++)
            {
                if (i == a || i == b) continue;
                var cross = Vector3.Cross(ab, _points[i] - _points[a]);
                float d = cross.LengthSquared();
                if (d > maxCross) { maxCross = d; c = i; }
            }

            float maxVol = 0; int d2 = 0;
            var ac = _points[c] - _points[a];
            var n = Vector3.Normalize(Vector3.Cross(ab, ac));
            for (int i = 0; i < _points.Count; i++)
            {
                if (i == a || i == b || i == c) continue;
                float v = MathF.Abs(Vector3.Dot(_points[i] - _points[a], n));
                if (v > maxVol) { maxVol = v; d2 = i; }
            }

            bool flip = Vector3.Dot(Vector3.Cross(_points[b] - _points[a], _points[c] - _points[a]),
                                      _points[d2] - _points[a]) > 0;

            if (flip)
                _faces.Add(new Face { I0 = a, I1 = c, I2 = b });
            else
                _faces.Add(new Face { I0 = a, I1 = b, I2 = c });

            if (flip)
            {
                _faces.Add(new Face { I0 = a, I1 = b, I2 = d2 });
                _faces.Add(new Face { I0 = b, I1 = c, I2 = d2 });
                _faces.Add(new Face { I0 = c, I1 = a, I2 = d2 });
            }
            else
            {
                _faces.Add(new Face { I0 = a, I1 = c, I2 = d2 });
                _faces.Add(new Face { I0 = c, I1 = b, I2 = d2 });
                _faces.Add(new Face { I0 = b, I1 = a, I2 = d2 });
            }

            foreach (var f in _faces) ComputePlane(f);
        }

        private void AssignPointsToFaces()
        {
            foreach (var f in _faces)
                f.OutsidePoints.Clear();
            for (int i = 0; i < _points.Count; i++)
                foreach (var f in _faces)
                    if (DistFromPlane(_points[i], f) > Eps)
                        f.OutsidePoints.Add(i);
        }

        private void ComputePlane(Face f)
        {
            var v0 = _points[f.I0]; var v1 = _points[f.I1]; var v2 = _points[f.I2];
            f.Normal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
            f.Offset = -Vector3.Dot(f.Normal, v0);
        }

        private static float DistFromPlane(Vector3 p, Face f)
        {
            return Vector3.Dot(f.Normal, p) + f.Offset;
        }

        private bool IsVisible(Face f, int pointIdx)
        {
            return DistFromPlane(_points[pointIdx], f) > Eps;
        }

        private List<(int a, int b)> FindHorizonEdges(List<Face> visibleFaces)
        {
            var edgeCount = new Dictionary<(int, int), int>();
            foreach (var f in visibleFaces)
            {
                IncEdge(edgeCount, f.I0, f.I1);
                IncEdge(edgeCount, f.I1, f.I2);
                IncEdge(edgeCount, f.I2, f.I0);
            }

            var horizon = new List<(int, int)>();
            foreach (var kvp in edgeCount)
            {
                if (kvp.Value == 1)
                {
                    var (a, b) = kvp.Key;
                    var reverse = (b, a);
                    if (!edgeCount.ContainsKey(reverse) || edgeCount[reverse] == 0)
                        horizon.Add((a, b));
                }
            }
            return horizon;
        }

        private static void IncEdge(Dictionary<(int, int), int> dict, int a, int b)
        {
            var key = (a, b);
            dict.TryGetValue(key, out int count);
            dict[key] = count + 1;
        }
    }
}
