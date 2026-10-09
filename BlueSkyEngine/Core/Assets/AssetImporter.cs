// ═══════════════════════════════════════════════════════════════════════════
// MESH IMPORT - GEOMETRY ONLY (no material system)
// ═══════════════════════════════════════════════════════════════════════════
//
// OVERVIEW:
// Meshes carry geometry only: [vertices][indices][submesh_info(offset,count)].
// Every surface shades with the single global clay in the viewport renderer.
// Texture extraction from source files is preserved for Strata (future).
//
// ═══════════════════════════════════════════════════════════════════════════

using BlueSky.Core.Diagnostics;
using BlueSky.Motif;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Tasks;

namespace BlueSky.Core.Assets;

/// <summary>
/// Asset import pipeline - converts source files (FBX, PNG, etc.) to .blueasset files.
/// Like Unreal's asset import system!
/// </summary>
public class AssetImporter
{
    private readonly string _projectPath;
    private readonly string _assetsDirectory;
    private readonly string _contentDirectory;
    private readonly Dictionary<string, IAssetImportHandler> _importers = new();

    public AssetImporter(string projectPath)
    {
        _projectPath = projectPath;
        var project = BlueProject.Load(projectPath);
        
        if (project == null)
        {
            throw new Exception($"Failed to load project: {projectPath}");
        }

        _assetsDirectory = project.GetAssetsDirectory(projectPath);
        _contentDirectory = project.GetContentDirectory(projectPath);

        // Register default importers
        RegisterImporter(new MeshImportHandler());
        RegisterImporter(new FBXImportHandler());
        RegisterImporter(new GLTFImportHandler());
        RegisterImporter(new StratapackImportHandler());
        RegisterImporter(new ScriptImportHandler());

        ErrorHandler.LogInfo($"AssetImporter initialized for project: {project.ProjectName}", "AssetImporter");
    }

    /// <summary>
    /// Register a custom import handler.
    /// </summary>
    public void RegisterImporter(IAssetImportHandler handler)
    {
        foreach (var extension in handler.SupportedExtensions)
        {
            _importers[extension.ToLowerInvariant()] = handler;
        }
    }

    /// <summary>
    /// Import a source file and create a .blueasset.
    /// </summary>
    public BlueAsset? Import(string sourceFilePath, ImportOptions? options = null)
    {
        try
        {
            if (!File.Exists(sourceFilePath))
            {
                ErrorHandler.LogError($"Source file not found: {sourceFilePath}", context: "AssetImporter");
                return null;
            }

            var extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            
            if (!_importers.TryGetValue(extension, out var importer))
            {
                ErrorHandler.LogWarning($"No importer registered for extension: {extension}", "AssetImporter");
                return null;
            }

            ErrorHandler.LogInfo($"Importing: {Path.GetFileName(sourceFilePath)}", "AssetImporter");

            // Create asset
            var asset = new BlueAsset
            {
                AssetName = Path.GetFileNameWithoutExtension(sourceFilePath),
                Type = importer.AssetType,
                SourceFile = sourceFilePath,
                SourceFileHash = BlueAsset.ComputeFileHash(sourceFilePath),
                ImportDate = DateTime.UtcNow,
                ImportSettings = options?.Settings ?? new()
            };
            // Determine target directory and create it before import
            var assetFileName = $"{asset.AssetName}.blueskyasset";
            string assetPath;
            
            if (extension == ".obj" || extension == ".glb" || extension == ".gltf" || extension == ".fbx" || extension == ".stratapack")
            {
                // Create subfolder for mesh formats and their textures
                string subDir = Path.Combine(_assetsDirectory, asset.AssetName);
                Directory.CreateDirectory(subDir);
                assetPath = Path.Combine(subDir, assetFileName);
                
                options ??= new ImportOptions();
                options.Settings["TargetDirectory"] = subDir;
            }
            else
            {
                assetPath = Path.Combine(_assetsDirectory, assetFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
            }

            // Import data
            var importResult = importer.Import(sourceFilePath, asset, options);
            
            if (!importResult.Success)
            {
                ErrorHandler.LogError($"Import failed: {importResult.Error}", context: "AssetImporter");
                return null;
            }

            // Save asset file
            asset.PayloadData = importResult.PayloadData ?? Array.Empty<byte>();
            asset.DataFile = importResult.DataFilePath ?? "";
            asset.ThumbnailFile = importResult.ThumbnailPath ?? "";
            
            if (!asset.Save(assetPath))
            {
                ErrorHandler.LogError($"Failed to save asset: {assetPath}", context: "AssetImporter");
                return null;
            }

            ErrorHandler.LogInfo($"✓ Imported: {asset.AssetName} → {Path.GetFileName(assetPath)}", "AssetImporter");
            return asset;
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError($"Import exception: {ex.Message}", ex, "AssetImporter");
            return null;
        }
    }

    /// <summary>
    /// Batch import multiple files.
    /// </summary>
    public List<BlueAsset> ImportBatch(IEnumerable<string> sourceFiles, ImportOptions? options = null)
    {
        var assets = new List<BlueAsset>();
        
        foreach (var file in sourceFiles)
        {
            var asset = Import(file, options);
            if (asset != null)
            {
                assets.Add(asset);
            }
        }
        
        ErrorHandler.LogInfo($"Batch import complete: {assets.Count}/{sourceFiles.Count()} succeeded", "AssetImporter");
        return assets;
    }

    /// <summary>
    /// Reimport an asset (source file changed).
    /// </summary>
    public bool Reimport(string assetPath)
    {
        var asset = BlueAsset.Load(assetPath);
        if (asset == null)
        {
            return false;
        }

        if (!asset.NeedsReimport())
        {
            ErrorHandler.LogInfo($"Asset up-to-date: {asset.AssetName}", "AssetImporter");
            return true;
        }

        ErrorHandler.LogInfo($"Reimporting: {asset.AssetName}", "AssetImporter");
        
        var newAsset = Import(asset.SourceFile);
        return newAsset != null;
    }

    /// <summary>
    /// Scan for assets that need reimport.
    /// </summary>
    public List<string> FindAssetsNeedingReimport()
    {
        var assetsNeedingReimport = new List<string>();
        
        var assetFiles = Directory.GetFiles(_assetsDirectory, "*.blueskyasset", SearchOption.AllDirectories);
        
        foreach (var assetFile in assetFiles)
        {
            var asset = BlueAsset.Load(assetFile);
            if (asset != null && asset.NeedsReimport())
            {
                assetsNeedingReimport.Add(assetFile);
            }
        }
        
        return assetsNeedingReimport;
    }

    /// <summary>
    /// Auto-reimport all changed assets.
    /// </summary>
    public int ReimportAll()
    {
        var assetsToReimport = FindAssetsNeedingReimport();
        
        if (assetsToReimport.Count == 0)
        {
            ErrorHandler.LogInfo("All assets up-to-date", "AssetImporter");
            return 0;
        }

        ErrorHandler.LogInfo($"Reimporting {assetsToReimport.Count} changed assets...", "AssetImporter");
        
        int successCount = 0;
        foreach (var assetPath in assetsToReimport)
        {
            if (Reimport(assetPath))
            {
                successCount++;
            }
        }
        
        ErrorHandler.LogInfo($"Reimport complete: {successCount}/{assetsToReimport.Count} succeeded", "AssetImporter");
        return successCount;
    }

    private string GetAssetSubdirectory(AssetType type)
    {
        return type switch
        {
            AssetType.Mesh => "Meshes",
            AssetType.StaticMesh => "Meshes",
            AssetType.SkeletalMesh => "Meshes",
            AssetType.Texture => "Textures",
            AssetType.Scene => "Scenes",
            AssetType.Terrain => "Terrains",
            AssetType.Script => "Scripts",
            AssetType.Audio => "Audio",
            _ => "Other"
        };
    }
}

/// <summary>
/// Interface for asset import handlers.
/// </summary>
public interface IAssetImportHandler
{
    string[] SupportedExtensions { get; }
    AssetType AssetType { get; }
    ImportResult Import(string sourceFile, BlueAsset asset, ImportOptions? options);
}

public class ImportOptions
{
    public Dictionary<string, object> Settings { get; set; } = new();
    public bool GenerateThumbnail { get; set; } = true;
    public bool GenerateLODs { get; set; } = false;
    public bool CompressData { get; set; } = true;
}

public class ImportResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public byte[]? PayloadData { get; set; }
    public string? DataFilePath { get; set; } // DEPRECATED
    public string? ThumbnailPath { get; set; }
}

/// <summary>
/// Mesh import handler (.obj).
/// Direct source-mesh import was removed: Blender Ease + .stratapack is the
/// only mesh door. This handler stays registered so the dispatcher reports a
/// loud rejection instead of "no importer".
/// </summary>
public class MeshImportHandler : IAssetImportHandler
{
    public string[] SupportedExtensions => new[] { ".obj" };
    public AssetType AssetType => AssetType.StaticMesh;

    public ImportResult Import(string sourceFile, BlueAsset asset, ImportOptions? options)
    {
        return new ImportResult
        {
            Success = false,
            Error = $"Direct .obj import is no longer supported — use BlueSky Engine Ease in Blender and import the .stratapack instead. (File: {Path.GetFileName(sourceFile)})"
        };
    }
}


/// <summary>
/// Script import handler (.bluescript, .cs, .tea files).
/// </summary>
public class ScriptImportHandler : IAssetImportHandler
{
    public string[] SupportedExtensions => new[] { ".bluescript", ".cs", ".tea" };
    public AssetType AssetType => AssetType.Script;

    public ImportResult Import(string sourceFile, BlueAsset asset, ImportOptions? options)
    {
        try
        {
            return new ImportResult
            {
                Success = true,
                DataFilePath = sourceFile
            };
        }
        catch (Exception ex)
        {
            return new ImportResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
}

/// <summary>
/// FBX import handler (.fbx).
/// Direct source-mesh import was removed: Blender Ease + .stratapack is the
/// only mesh door. This handler stays registered so the dispatcher reports a
/// loud rejection instead of "no importer".
/// </summary>
public class FBXImportHandler : IAssetImportHandler
{
    public string[] SupportedExtensions => new[] { ".fbx" };
    public AssetType AssetType => AssetType.StaticMesh;

    public ImportResult Import(string sourceFile, BlueAsset asset, ImportOptions? options)
    {
        return new ImportResult
        {
            Success = false,
            Error = $"Direct .fbx import is no longer supported — use BlueSky Engine Ease in Blender and import the .stratapack instead. (File: {Path.GetFileName(sourceFile)})"
        };
    }
}

/// <summary>
/// Expanded vertex with position, normal, and UV for deduplication.
/// Used during FBX import to expand per-polygon-vertex attributes into unique vertices.
/// </summary>
internal readonly struct ExpandedVertex : IEquatable<ExpandedVertex>
{
    public readonly System.Numerics.Vector3 Position;
    public readonly System.Numerics.Vector3 Normal;
    public readonly System.Numerics.Vector2 UV;

    public ExpandedVertex(System.Numerics.Vector3 position, System.Numerics.Vector3 normal, System.Numerics.Vector2 uv)
    {
        Position = position;
        Normal = normal;
        UV = uv;
    }

    public bool Equals(ExpandedVertex other)
    {
        return Position == other.Position && Normal == other.Normal && UV == other.UV;
    }

    public override bool Equals(object? obj) => obj is ExpandedVertex v && Equals(v);

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Position.X, Position.Y, Position.Z,
            Normal.X, Normal.Y,
            UV.X, UV.Y);
    }
}

/// <summary>
/// GLTF/GLB import handler - imports ALL meshes and textures from GLTF 2.0 files.
/// Handles multi-mesh GLB files (geometry only; surface colors come from the global clay).
/// Properly applies node transforms from scene graph to position mesh parts correctly.
/// 
/// CRITICAL FIX: Node Transform Application
/// ─────────────────────────────────────────
/// GLTF files store mesh geometry in local space and use a scene graph (nodes) to position
/// meshes in world space. Each node has a transform (TRS or Matrix) that must be applied to
/// vertices during import, otherwise mesh parts appear "exploded" (disconnected/floating).
/// 
/// Scene Graph Structure:
///   Scene → Root Nodes → Child Nodes → Mesh References
///   Each Node: Translation, Rotation (quaternion), Scale, or Matrix (4x4)
///   World Transform = Parent Transform × Local Transform (hierarchical)
/// 
/// Implementation:
///   1. ComputeNodeTransforms() recursively traverses scene graph from root nodes
///   2. Computes world transform for each node (parent × local accumulation)
///   3. During mesh extraction, applies world transform to vertex positions/normals
///   4. Result: mesh parts correctly positioned relative to each other
/// 
/// Matrix Math:
///   Local Transform = Scale × Rotation × Translation (GLTF spec order)
///   World Transform = Parent × Local (row-major multiplication)
///   Vertex Position = Transform(localPos, worldTransform) × scaleFactor
///   Vertex Normal = Normalize(TransformNormal(localNormal, worldTransform))
/// </summary>
public class StratapackImportHandler : IAssetImportHandler
{
    public string[] SupportedExtensions => new[] { ".stratapack" };
    public AssetType AssetType => AssetType.StaticMesh;

    public ImportResult Import(string sourceFile, BlueAsset asset, ImportOptions? options)
    {
        const string Ctx = "StratapackImportHandler";
        try
        {
            BlueSky.Rendering.Strata.StrataPack.PackFile pack;
            try
            {
                pack = BlueSky.Rendering.Strata.StrataPack.Decode(File.ReadAllBytes(sourceFile));
            }
            catch (InvalidOperationException ex)
            {
                return new ImportResult { Success = false, Error = ex.Message };
            }

            if (pack.Header.Meshes.Count == 0)
            {
                return new ImportResult
                {
                    Success = false,
                    Error = $"Pack '{Path.GetFileName(sourceFile)}' contains no meshes — nothing assigned."
                };
            }

            byte[] payload = pack.Payload;

            static byte[] Slice(byte[] src, ulong off, ulong size)
            {
                var dst = new byte[size];
                Buffer.BlockCopy(src, (int)off, dst, 0, (int)size);
                return dst;
            }

            string targetDir = options?.Settings != null
                && options.Settings.TryGetValue("TargetDirectory", out var td) && td is string tdStr
                ? tdStr : Path.Combine("Assets", asset.AssetName);
            var (strataDir, texturesDir) =
                BlueSky.Core.Assets.StrataImportWriter.EnsureMaterialsLayout(targetDir);

            byte[]? Blob(ulong off, ulong size)
            {
                if (size == 0) return null;
                var dst = new byte[size];
                Buffer.BlockCopy(payload, (int)off, dst, 0, (int)size);
                return dst;
            }

            // One mesh file per pack mesh; Materials/ is shared across all of them.
            // The dispatcher's asset carries the FIRST mesh; the rest save directly.
            byte[]? primaryPayload = null;
            int meshIndex = 0;
            foreach (var mesh in pack.Header.Meshes)
            {
                bool isPrimary = meshIndex == 0;
                BlueAsset meshAsset = asset;
                string meshTag = asset.AssetName;
                if (!isPrimary)
                {
                    meshTag = $"{asset.AssetName}_{mesh.Name}";
                    meshAsset = new BlueAsset
                    {
                        AssetName = meshTag,
                        Type = AssetType.StaticMesh,
                        SourceFile = sourceFile,
                        ImportDate = DateTime.UtcNow,
                    };
                }

                byte[] vData = Slice(payload, mesh.VertexOffset, mesh.VertexSize);
                byte[] iData = Slice(payload, mesh.IndexOffset, mesh.IndexSize);

                // Bounds from Packed32 positions (stride 32, xyz = first 12 bytes).
                var bmin = new System.Numerics.Vector3(float.MaxValue);
                var bmax = new System.Numerics.Vector3(float.MinValue);
                for (int v = 0; v < mesh.VertexCount; v++)
                {
                    int o = v * 32;
                    var p = new System.Numerics.Vector3(
                        BitConverter.ToSingle(vData, o),
                        BitConverter.ToSingle(vData, o + 4),
                        BitConverter.ToSingle(vData, o + 8));
                    bmin = System.Numerics.Vector3.Min(bmin, p);
                    bmax = System.Numerics.Vector3.Max(bmax, p);
                }

                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);
                writer.Write(vData.Length);
                writer.Write(vData);
                writer.Write((uint)iData.Length);
                writer.Write(iData);
                writer.Write(mesh.Submeshes.Count);
                foreach (var sub in mesh.Submeshes)
                {
                    writer.Write(sub.Offset);
                    writer.Write(sub.Count);
                    writer.Write(sub.Slot);
                }

                // Surfaces → .stratamat. Texture roles resolved per slot from the
                // pack's texture/aomap/bent tables (albedo + normal + RMA assembly).
                var usedSlots = new HashSet<int>();
                foreach (var sub in mesh.Submeshes) usedSlots.Add(sub.Slot);
                if (usedSlots.Count == 0)
                {
                    if (isPrimary)
                    {
                        return new ImportResult
                        {
                            Success = false,
                            Error = $"Mesh '{mesh.Name}' has NO surface slots — nothing assigned."
                        };
                    }
                    ErrorHandler.LogError(
                        $"Mesh '{mesh.Name}' has NO surface slots — skipped.", null, Ctx);
                    meshIndex++;
                    continue;
                }

                foreach (int slot in usedSlots)
                {
                    var surf = pack.Header.Surfaces.Find(s => s.Slot == slot);
                    var alb = surf != null && surf.AlbedoLinear.Length == 3
                        ? new System.Numerics.Vector3(surf.AlbedoLinear[0], surf.AlbedoLinear[1], surf.AlbedoLinear[2])
                        : new System.Numerics.Vector3(0.5f, 0.5f, 0.5f);
                    var emi = surf != null && surf.EmissiveLinear.Length == 3
                        ? new System.Numerics.Vector3(surf.EmissiveLinear[0], surf.EmissiveLinear[1], surf.EmissiveLinear[2])
                        : System.Numerics.Vector3.Zero;
                    var coatTint = surf != null && surf.ClearcoatTintLinear != null && surf.ClearcoatTintLinear.Length == 3
                        ? new System.Numerics.Vector3(surf.ClearcoatTintLinear[0], surf.ClearcoatTintLinear[1], surf.ClearcoatTintLinear[2])
                        : System.Numerics.Vector3.One;

                    byte[]? albedoBytes = null; int albedoW = 0, albedoH = 0;
                    byte[]? normalBytes = null; int normalW = 0, normalH = 0;
                    byte[]? rmaBytes = null; int rmaW = 0, rmaH = 0;
                    foreach (var tex in pack.Header.Textures)
                    {
                        if (tex.Slot != slot) continue;
                        var blob = Blob(tex.Offset, tex.Size);
                        if (blob == null) continue;
                        string role = (tex.Role ?? "").ToLowerInvariant();
                        // Persist every pack texture for user override.
                        BlueSky.Core.Assets.StrataImportWriter.WriteTextureBlob(
                            texturesDir, $"slot{slot}_{role}_{tex.Width}x{tex.Height}",
                            tex.Width, tex.Height, blob);
                        if (role.Contains("albedo") || role.Contains("basecolor") || role.Contains("diffuse"))
                        { albedoBytes = blob; albedoW = tex.Width; albedoH = tex.Height; }
                        else if (role.Contains("normal"))
                        { normalBytes = blob; normalW = tex.Width; normalH = tex.Height; }
                        else if (role.Contains("rma") || role.Contains("rough") || role.Contains("metal") || role.Contains("orm"))
                        { rmaBytes = blob; rmaW = tex.Width; rmaH = tex.Height; }
                    }

                    // Bent-normal map: separate BentMaps table. ONLY the "world"
                    // encoding feeds the lobe (the shader reads world-space RGB
                    // and the importer never guesses normal spaces) — anything
                    // else persists for override but stays unassembled, loudly.
                    byte[]? bentBytes = null; int bentW = 0, bentH = 0;
                    foreach (var bent in pack.Header.BentMaps)
                    {
                        if (bent.Slot != slot) continue;
                        var blob = Blob(bent.Offset, bent.Size);
                        if (blob == null) continue;
                        BlueSky.Core.Assets.StrataImportWriter.WriteTextureBlob(
                            texturesDir, $"slot{slot}_bent_{bent.Width}x{bent.Height}",
                            bent.Width, bent.Height, blob);
                        if (string.Equals(bent.Encoding, "world", StringComparison.OrdinalIgnoreCase))
                        { bentBytes = blob; bentW = bent.Width; bentH = bent.Height; }
                        else
                            ErrorHandler.LogInfo(
                                $"[StratapackImportHandler] Slot {slot} bent map encoding '{bent.Encoding}' != world — persisted, not assembled.", Ctx);
                    }

                    var mat = BlueSky.Rendering.Strata.StrataImporter.Assemble(
                        $"{meshTag}_slot{slot}",
                        alb,
                        surf?.Metallic ?? 0f,
                        surf?.Roughness ?? 0.6f,
                        1f,
                        emi,
                        surf != null && surf.EmissiveIntensity > 0f ? surf.EmissiveIntensity : 0f,
                        albedoSrgb: albedoBytes, albedoW: albedoW, albedoH: albedoH,
                        normalMap: normalBytes, normalW: normalW, normalH: normalH,
                        rmaMap: rmaBytes, rmaW: rmaW, rmaH: rmaH,
                        clearcoat: surf?.Clearcoat ?? 0f,
                        clearcoatTintLinear: coatTint,
                        alpha: surf?.Alpha ?? 1f,
                        sheen: surf?.Sheen ?? 0f,
                        anisotropy: surf?.Anisotropy ?? 0f,
                        bentMap: bentBytes, bentW: bentW, bentH: bentH);

                    string stratamatPath =
                        BlueSky.Core.Assets.StrataImportWriter.WriteStrataMaterial(strataDir, mat);
                    BlueSky.Core.Assets.StrataImportWriter.LinkSlot(meshAsset.Metadata, slot, stratamatPath);
                    ErrorHandler.LogInfo(
                        $"[StratapackImportHandler] ✓ Slot {slot} → '{Path.GetFileName(stratamatPath)}' " +
                        $"(mask=0x{(uint)mat.Features:X})", Ctx);
                }

                // Vertex AO (Phase A): NO sidecar files. The .stratapack stays
                // the database; the asset carries pack-relative references and
                // readers slice straight from the pack via DecodeHeader.
                if (mesh.AoSize > 0 && mesh.AoCount > 0)
                {
                    meshAsset.Metadata["sourcePack"] = sourceFile;
                    meshAsset.Metadata["aoOffset"] = mesh.AoOffset.ToString();
                    meshAsset.Metadata["aoSize"] = mesh.AoSize.ToString();
                    meshAsset.Metadata["aoCount"] = mesh.AoCount.ToString();
                }

                // Skeleton + skinning (skeletal): same rule, no sidecars.
                // Bones live in the pack header; the skin stream is sliced
                // from the pack payload at read time.
                if (mesh.SkeletonIndex >= 0 && mesh.SkinCount > 0 &&
                    mesh.SkeletonIndex < pack.Header.Skeletons.Count)
                {
                    var skel = pack.Header.Skeletons[mesh.SkeletonIndex];
                    meshAsset.Metadata["sourcePack"] = sourceFile;
                    meshAsset.Metadata["skinOffset"] = mesh.SkinOffset.ToString();
                    meshAsset.Metadata["skinSize"] = mesh.SkinSize.ToString();
                    meshAsset.Metadata["skinCount"] = mesh.SkinCount.ToString();
                    meshAsset.Metadata["skeletonIndex"] = mesh.SkeletonIndex.ToString();
                    meshAsset.Metadata["skeletonName"] = skel.Name;
                    meshAsset.Metadata["skeletonBones"] = skel.Bones.Count.ToString();
                    ErrorHandler.LogInfo(
                        $"[StratapackImportHandler] ✓ Skeleton '{skel.Name}' ({skel.Bones.Count} bones, {mesh.SkinCount} skinned verts, in-pack)", Ctx);
                }

                meshAsset.Metadata["vertexCount"] = (vData.Length / 32).ToString();
                meshAsset.Metadata["triangleCount"] = (iData.Length / 12).ToString();
                meshAsset.Metadata["submeshCount"] = mesh.Submeshes.Count.ToString();
                meshAsset.Metadata["meshCount"] = "1";
                // Every mesh cites its pack: Phase-B probes aggregate across all
                // packs referenced by loaded meshes. No files, just the path.
                meshAsset.Metadata["sourcePack"] = sourceFile;
                meshAsset.Metadata["format"] = "Packed32";
                meshAsset.Metadata["boundsMin"] = $"{bmin.X},{bmin.Y},{bmin.Z}";
                meshAsset.Metadata["boundsMax"] = $"{bmax.X},{bmax.Y},{bmax.Z}";
                meshAsset.Type = AssetType.StaticMesh;

                ErrorHandler.LogInfo(
                    $"✓ StrataPack imported: {mesh.Name} ({mesh.VertexCount} verts, " +
                    $"{mesh.IndexCount / 3} tris, {mesh.Submeshes.Count} submesh(es))", Ctx);

                if (isPrimary)
                {
                    primaryPayload = ms.ToArray();
                }
                else
                {
                    string extraPath = Path.Combine(targetDir, $"{meshTag}.blueskyasset");
                    var extra = new BlueAsset
                    {
                        AssetName = meshTag,
                        Type = AssetType.StaticMesh,
                        SourceFile = sourceFile,
                        ImportDate = DateTime.UtcNow,
                        Metadata = meshAsset.Metadata,
                        PayloadData = ms.ToArray(),
                    };
                    if (!extra.Save(extraPath))
                    {
                        ErrorHandler.LogError($"Mesh '{mesh.Name}': failed to save '{extraPath}'.", null, Ctx);
                    }
                }
                meshIndex++;
            }

            if (primaryPayload == null)
            {
                return new ImportResult
                {
                    Success = false,
                    Error = $"Pack '{Path.GetFileName(sourceFile)}' produced no importable mesh."
                };
            }

            return new ImportResult { Success = true, PayloadData = primaryPayload };
        }
        catch (Exception ex)
        {
            return new ImportResult { Success = false, Error = ex.Message };
        }
    }
}
/// <summary>
/// GLTF/GLB import handler (.gltf/.glb).
/// Direct source-mesh import was removed: Blender Ease + .stratapack is the
/// only mesh door. This handler stays registered so the dispatcher reports a
/// loud rejection instead of "no importer".
/// </summary>
public class GLTFImportHandler : IAssetImportHandler
{
    public string[] SupportedExtensions => new[] { ".gltf", ".glb" };
    public AssetType AssetType => AssetType.StaticMesh;

    public ImportResult Import(string sourceFile, BlueAsset asset, ImportOptions? options)
    {
        return new ImportResult
        {
            Success = false,
            Error = $"Direct {Path.GetExtension(sourceFile).ToLowerInvariant()} import is no longer supported — use BlueSky Engine Ease in Blender and import the .stratapack instead. (File: {Path.GetFileName(sourceFile)})"
        };
    }
}
