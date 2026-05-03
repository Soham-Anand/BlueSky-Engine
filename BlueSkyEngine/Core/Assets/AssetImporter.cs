// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
// MATERIAL SLOT SYSTEM - COMPLETE WORKFLOW
// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
// 
// OVERVIEW:
// Multi-material mesh support (Blender-style) - each submesh can have its own material assigned via slot index (0-7).
// 
// WORKFLOW:
// 1. FBX IMPORT (AssetImporter.cs):
//    - Extracts submeshes from FBX (each with material name from DCC tool)
//    - Creates binary mesh data: [vertices][indices][submesh_info(offset,count,slot)]
//    - Auto-generates default materials for each slot with distinct neutral colors
//    - Saves material paths in asset metadata: materialSlot0, materialSlot1, etc.
// 
// 2. ENTITY SPAWN (Program.cs - SpawnDraggedAsset):
//    - Reads mesh asset metadata
//    - Auto-assigns materials from metadata to StaticMeshComponent slots
//    - Each slot (0-7) gets its material path from metadata
// 
// 3. VIEWPORT RENDERING (ViewportRenderer.cs - RenderEntities):
//    - Loads mesh with submesh info from .blueskyasset
//    - For each submesh: resolves material from slot index → loads MaterialAsset → binds textures → renders
//    - Per-submesh material properties (albedo, metallic, roughness, textures) applied to GPU
// 
// 4. INSPECTOR UI (Program.cs - BuildWorkspaceUI):
//    - Shows all material slots with drag-drop assignment
//    - "Auto-Color Slots" button generates vibrant colored materials for easy visualization
//    - Edit/Clear buttons per slot
// 
// KEY FEATURES:
// - Up to 8 material slots per mesh (hardware-friendly limit)
// - Automatic material slot detection from FBX material assignments
// - Drag-drop material assignment in inspector
// - Auto-color utility for quick multi-material visualization
// - Material caching for performance (LRU eviction)
// - Texture loading from file paths or .blueskyasset format
// - PBR material support (albedo, metallic, roughness, normal, RMA textures)
// 
// USAGE:
// 1. Import FBX with multiple materials → materials auto-created with distinct colors
// 2. Drag mesh into viewport → materials auto-assigned from metadata
// 3. Select entity → Inspector shows all material slots
// 4. Drag .blueskyasset materials onto slots OR click "Auto-Color Slots" for quick visualization
// 5. Viewport renders each submesh with its assigned material in real-time
// 
// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

using BlueSky.Core.Diagnostics;
using BlueSky.Animation.FBX;
using BlueSky.Animation;
using System.Collections.Concurrent;
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
        RegisterImporter(new TextureImportHandler());
        RegisterImporter(new MaterialImportHandler());
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
            
            if (extension == ".obj" || extension == ".glb" || extension == ".gltf" || extension == ".fbx")
            {
                // Create subfolder for mesh formats and their materials/textures
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
            AssetType.Material => "Materials",
            AssetType.Scene => "Scenes",
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
/// Mesh import handler (OBJ, and other formats via plugins)
/// </summary>
public class MeshImportHandler : IAssetImportHandler
{
    public string[] SupportedExtensions => new[] { ".obj" };
    public AssetType AssetType => AssetType.StaticMesh;

    /// <summary>
    /// Case-insensitive file search in a directory. Returns full path or null.
    /// </summary>
    private static string? FindFileInsensitive(string directory, string fileName)
    {
        if (!Directory.Exists(directory)) return null;
        try
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
        }
        catch { }
        return null;
    }

    public ImportResult Import(string sourceFile, BlueAsset asset, ImportOptions? options)
    {
        try
        {
            // Use OBJ parser
            var objMesh = OBJParser.Parse(sourceFile);
            if (objMesh == null)
            {
                return new ImportResult
                {
                    Success = false,
                    Error = "Failed to parse OBJ file"
                };
            }

            // Convert to engine-ready data
            var (vertexData, indexData, vertexCount, indexCount, submeshes) = OBJParser.ConvertToEngineData(objMesh);

            // Pack binary data into Payload
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(vertexData.Length);
            writer.Write(vertexData);
            writer.Write(indexData.Length);
            writer.Write(indexData);

            writer.Write(submeshes.Count); // submesh count
            
            var materialSlots = new List<string>();
            string targetDir = options?.Settings != null && options.Settings.TryGetValue("TargetDirectory", out var td) && td is string tdStr 
                ? tdStr 
                : Path.Combine("Assets", asset.AssetName);

            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            string materialsDir = Path.Combine(targetDir, "Materials");
            string texturesDir = Path.Combine(targetDir, "Textures");
            if (!Directory.Exists(materialsDir)) Directory.CreateDirectory(materialsDir);
            if (!Directory.Exists(texturesDir)) Directory.CreateDirectory(texturesDir);

            // Parse MTL if available
            var materials = new Dictionary<string, MTLMaterial>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(objMesh.MaterialLibrary))
            {
                string sourceDir = Path.GetDirectoryName(sourceFile) ?? "";
                string mtlPath = Path.Combine(sourceDir, objMesh.MaterialLibrary);
                ErrorHandler.LogInfo($"[MeshImportHandler] Looking for MTL at {mtlPath}", "AssetImporter");
                if (File.Exists(mtlPath))
                {
                    materials = MTLParser.Parse(mtlPath);
                    ErrorHandler.LogInfo($"[MeshImportHandler] Parsed {materials.Count} materials from MTL", "AssetImporter");
                }
                else
                {
                    ErrorHandler.LogWarning($"[MeshImportHandler] MTL file not found at {mtlPath}", "AssetImporter");
                }
            }

            ErrorHandler.LogInfo($"[MeshImportHandler] ═══════════════════════════════════════════════════════════", "AssetImporter");
            ErrorHandler.LogInfo($"[MeshImportHandler] MATERIAL SLOT ASSIGNMENT (Total submeshes: {submeshes.Count})", "AssetImporter");
            ErrorHandler.LogInfo($"[MeshImportHandler] ═══════════════════════════════════════════════════════════", "AssetImporter");
            
            // ── PARALLEL TEXTURE PRE-IMPORT ────────────────────────────────────────────────
            var textureMap = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var uniqueTextureSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string meshSourceDir = Path.GetDirectoryName(sourceFile) ?? "";

            foreach (var mtl in materials.Values)
            {
                if (!string.IsNullOrEmpty(mtl.map_Kd)) uniqueTextureSources.Add(mtl.map_Kd);
                if (!string.IsNullOrEmpty(mtl.map_Ka)) uniqueTextureSources.Add(mtl.map_Ka);
                if (!string.IsNullOrEmpty(mtl.map_Bump)) uniqueTextureSources.Add(mtl.map_Bump);
                if (!string.IsNullOrEmpty(mtl.map_Ns)) uniqueTextureSources.Add(mtl.map_Ns);
                if (!string.IsNullOrEmpty(mtl.map_d)) uniqueTextureSources.Add(mtl.map_d);
                if (!string.IsNullOrEmpty(mtl.map_Ke)) uniqueTextureSources.Add(mtl.map_Ke);
            }

            if (uniqueTextureSources.Count > 0)
            {
                ErrorHandler.LogInfo($"[MeshImportHandler] Pre-importing {uniqueTextureSources.Count} unique textures in parallel...", "AssetImporter");
                
                Parallel.ForEach(uniqueTextureSources, texRelPath =>
                {
                    string texSource = Path.Combine(meshSourceDir, texRelPath);
                    if (!File.Exists(texSource))
                    {
                        string? found = FindFileInsensitive(meshSourceDir, texRelPath);
                        if (found != null) texSource = found;
                    }

                    if (File.Exists(texSource))
                    {
                        var texImporter = new TextureImportHandler();
                        string texFileName = Path.GetFileNameWithoutExtension(texSource);
                        var texBlueAsset = new BlueAsset { AssetName = texFileName, Type = AssetType.Texture, SourceFile = texSource, ImportDate = DateTime.UtcNow };
                        var texRes = texImporter.Import(texSource, texBlueAsset, null);
                        
                        if (texRes.Success)
                        {
                            texBlueAsset.PayloadData = texRes.PayloadData;
                            string texDest = Path.Combine(texturesDir, $"{texFileName}.blueskyasset");
                            if (texBlueAsset.Save(texDest))
                            {
                                textureMap[texRelPath] = texDest;
                            }
                        }
                    }
                });
            }

            int slotIndex = 0;
            foreach (var submesh in submeshes)
            {
                writer.Write(submesh.IndexOffset);
                writer.Write(submesh.IndexCount);
                writer.Write(slotIndex);

                string matName = submesh.MaterialName;
                if (string.IsNullOrEmpty(matName)) matName = "default";
                
                ErrorHandler.LogInfo($"[MeshImportHandler] Slot {slotIndex}: Material '{matName}' (IndexOffset={submesh.IndexOffset}, IndexCount={submesh.IndexCount})", "AssetImporter");
                
                string matFileName = $"{matName}.blueskyasset";
                string matPath = Path.Combine(materialsDir, matFileName);

                if (materials.TryGetValue(matName, out var mtl))
                {
                    var matAsset = new MaterialAsset { MaterialName = matName, MaterialId = Guid.NewGuid() };
                    matAsset.Albedo = new Vector3Data(mtl.Kd.X, mtl.Kd.Y, mtl.Kd.Z);
                    
                    float ns = System.Math.Max(mtl.Ns, 0f);
                    float roughness = ns > 0f
                        ? System.Math.Clamp(System.MathF.Sqrt(2.0f / (ns + 2.0f)), 0.02f, 1.0f)
                        : 1.0f;
                    matAsset.Roughness = roughness;
                    
                    float kdLuma = (mtl.Kd.X + mtl.Kd.Y + mtl.Kd.Z) / 3.0f;
                    bool likelyMetal = kdLuma < 0.02f && string.IsNullOrEmpty(mtl.map_Kd);
                    matAsset.Metallic = likelyMetal ? 0.95f : 0.0f;
                    matAsset.Opacity = mtl.d;
                    
                    // Wire emissive from MTL Ke
                    if (mtl.Ke.X > 0.01f || mtl.Ke.Y > 0.01f || mtl.Ke.Z > 0.01f)
                    {
                        matAsset.Emission = new Vector3Data(mtl.Ke.X, mtl.Ke.Y, mtl.Ke.Z);
                        matAsset.EmissionIntensity = 1.0f;
                    }

                    if (!string.IsNullOrEmpty(mtl.map_Kd) && textureMap.TryGetValue(mtl.map_Kd, out var albedoPath))
                        matAsset.AlbedoTexturePath = albedoPath;
                    
                    if (!string.IsNullOrEmpty(mtl.map_Bump) && textureMap.TryGetValue(mtl.map_Bump, out var normalPath))
                        matAsset.NormalTexturePath = normalPath;
                    
                    if (!string.IsNullOrEmpty(mtl.map_Ns) && textureMap.TryGetValue(mtl.map_Ns, out var roughPath))
                        matAsset.RoughnessTexturePath = roughPath;
                    
                    // ── Opacity / Blend Mode Detection ──────────────────────────────
                    // Common pattern in OBJ: map_d == map_Kd (alpha embedded in diffuse)
                    // We need to detect actual alpha pixels to avoid false transparency.
                    if (!string.IsNullOrEmpty(mtl.map_d))
                    {
                        bool isSameAsAlbedo = string.Equals(mtl.map_d, mtl.map_Kd, StringComparison.OrdinalIgnoreCase);
                        
                        if (isSameAsAlbedo)
                        {
                            // map_d == map_Kd: Check if albedo texture actually has alpha pixels
                            bool textureHasAlpha = false;
                            if (textureMap.TryGetValue(mtl.map_Kd, out var albPath))
                            {
                                textureHasAlpha = TextureImportHandler.HasAlphaInAsset(albPath);
                            }
                            
                            if (textureHasAlpha)
                            {
                                // Alpha is in the albedo texture itself — no separate opacity texture needed
                                matAsset.BlendMode = BlueSky.Rendering.Materials.BlendMode.AlphaBlend;
                                ErrorHandler.LogInfo($"[MeshImportHandler] Material '{matName}': map_d==map_Kd with real alpha → AlphaBlend (albedo alpha)", "AssetImporter");
                            }
                            else
                            {
                                // Texture has no alpha — stay opaque despite map_d being set
                                matAsset.BlendMode = BlueSky.Rendering.Materials.BlendMode.Opaque;
                                ErrorHandler.LogInfo($"[MeshImportHandler] Material '{matName}': map_d==map_Kd but NO alpha pixels → Opaque", "AssetImporter");
                            }
                        }
                        else if (textureMap.TryGetValue(mtl.map_d, out var opacityPath))
                        {
                            // map_d is a different file — use it as separate opacity texture
                            matAsset.OpacityTexturePath = opacityPath;
                            matAsset.BlendMode = BlueSky.Rendering.Materials.BlendMode.AlphaBlend;
                            ErrorHandler.LogInfo($"[MeshImportHandler] Material '{matName}': separate map_d → AlphaBlend (opacity texture)", "AssetImporter");
                        }
                    }
                    else if (matAsset.Opacity < 0.9f)
                    {
                        matAsset.BlendMode = BlueSky.Rendering.Materials.BlendMode.AlphaBlend;
                    }
                    else
                    {
                        matAsset.BlendMode = BlueSky.Rendering.Materials.BlendMode.Opaque;
                    }

                    matAsset.Save(matPath);
                }
                else
                {
                    var matAsset = new MaterialAsset { MaterialName = matName, MaterialId = Guid.NewGuid() };
                    matAsset.Save(matPath);
                }

                asset.Metadata[$"materialSlot{slotIndex}"] = matPath;
                materialSlots.Add(matName);
                slotIndex++;
            }

            ErrorHandler.LogInfo($"[MeshImportHandler] ═══════════════════════════════════════════════════════════", "AssetImporter");
            ErrorHandler.LogInfo($"[MeshImportHandler] MATERIAL SLOT ASSIGNMENT COMPLETE", "AssetImporter");
            ErrorHandler.LogInfo($"[MeshImportHandler] ═══════════════════════════════════════════════════════════", "AssetImporter");

            // Update asset metadata
            asset.Metadata["materialSlots"] = string.Join(",", materialSlots);
            asset.Metadata["submeshCount"] = submeshes.Count.ToString();
            asset.Metadata["vertexCount"] = vertexCount.ToString();
            asset.Metadata["triangleCount"] = (indexCount / 3).ToString();
            asset.Metadata["meshCount"] = submeshes.Count.ToString();
            asset.Metadata["format"] = "Packed32"; // Position + Normal + UV

            // Store bounds
            asset.Metadata["boundsMin"] = $"{objMesh.Bounds.Min.X},{objMesh.Bounds.Min.Y},{objMesh.Bounds.Min.Z}";
            asset.Metadata["boundsMax"] = $"{objMesh.Bounds.Max.X},{objMesh.Bounds.Max.Y},{objMesh.Bounds.Max.Z}";

            return new ImportResult
            {
                Success = true,
                PayloadData = ms.ToArray()
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
/// Material import handler.
/// </summary>
public class MaterialImportHandler : IAssetImportHandler
{
    public string[] SupportedExtensions => new[] { ".mat", ".mtl" };
    public AssetType AssetType => AssetType.Material;

    public ImportResult Import(string sourceFile, BlueAsset asset, ImportOptions? options)
    {
        try
        {
            asset.Metadata["shader"] = "Standard";
            
            return new ImportResult
            {
                Success = true,
                DataFilePath = sourceFile + ".data"
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
/// FBX import handler - imports static meshes from FBX files.
/// Supports multi-mesh FBX files (all sub-meshes combined into submeshes).
/// Properly handles per-polygon-vertex normals and UVs via vertex expansion.
/// </summary>
public class FBXImportHandler : IAssetImportHandler
{
    public string[] SupportedExtensions => new[] { ".fbx" };
    public AssetType AssetType => AssetType.StaticMesh;

    public ImportResult Import(string sourceFile, BlueAsset asset, ImportOptions? options)
    {
        try
        {
            ErrorHandler.LogInfo($"Starting FBX import: {Path.GetFileName(sourceFile)}", "FBXImportHandler");

            // Read user scale from import options (default 1.0 = no extra scaling)
            float userScale = 1.0f;
            if (options?.Settings != null && options.Settings.TryGetValue("scale", out var scaleObj))
            {
                if (scaleObj is float f) userScale = f;
                else if (scaleObj is double d) userScale = (float)d;
            }
            // Actual scale is computed after parsing, using FBX UnitScaleFactor
            float scale = userScale;

            return ImportStaticMesh(sourceFile, asset, scale, options);
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError($"FBX import failed: {ex.Message}", ex, "FBXImportHandler");
            return new ImportResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private ImportResult ImportStaticMesh(string sourceFile, BlueAsset asset, float userScale, ImportOptions? options)
    {
        try
        {
            float scale = userScale;
            // Parse all meshes from the FBX
            var importer = new FbxImporterV2();
            var allMeshes = importer.ImportAll(sourceFile);

            if (allMeshes.Count == 0)
            {
                return new ImportResult
                {
                    Success = false,
                    Error = "No geometry found in FBX file"
                };
            }

            ErrorHandler.LogInfo($"[FBXImportHandler] Found {allMeshes.Count} sub-mesh(es) in FBX", "FBXImportHandler");

            // Use UnitScaleFactor from FBX GlobalSettings for correct scaling
            // FBX UnitScaleFactor gives cm-per-unit. Convert to meters: multiply by (UnitScaleFactor * 0.01)
            if (allMeshes.Count > 0)
            {
                float unitScale = allMeshes[0].GlobalSettings.UnitScaleFactor;
                // unitScale = 1.0 means 1 unit = 1 cm, so multiply by 0.01 for meters
                // unitScale = 100.0 means 1 unit = 1 m, so multiply by 1.0 for meters
                float autoScale = unitScale * 0.01f;
                scale = autoScale * userScale;
                ErrorHandler.LogInfo($"[FBXImportHandler] UnitScaleFactor={unitScale}, autoScale={autoScale:F4}, userScale={userScale:F2}, finalScale={scale:F4}", "FBXImportHandler");
            }

            // Expand and combine all sub-meshes into a single vertex/index buffer
            // with submesh info for each sub-mesh
            var finalVertices = new List<ExpandedVertex>();
            var finalIndices = new List<uint>();
            var submeshInfos = new List<(int indexOffset, int indexCount, int materialSlot)>();
            var dedupMap = new Dictionary<ExpandedVertex, uint>();

            var boundsMin = new System.Numerics.Vector3(float.MaxValue);
            var boundsMax = new System.Numerics.Vector3(float.MinValue);

            int meshSlot = 0;
            foreach (var mesh in allMeshes)
            {
                int submeshIndexStart = finalIndices.Count;
                bool normalsArePerPolygonVertex = mesh.Normals.Length == mesh.Indices.Length;
                bool uvsArePerPolygonVertex = mesh.UVs.Length == mesh.Indices.Length;

                for (int t = 0; t < mesh.Indices.Length; t++)
                {
                    uint origIdx = mesh.Indices[t];

                    // Bounds check on vertex index
                    if (origIdx >= mesh.Vertices.Length)
                        continue;

                    // Get position (apply scale)
                    var pos = mesh.Vertices[origIdx] * scale;

                    // Get normal: per-polygon-vertex or per-vertex
                    System.Numerics.Vector3 normal;
                    if (normalsArePerPolygonVertex && t < mesh.Normals.Length)
                        normal = mesh.Normals[t];
                    else if (!normalsArePerPolygonVertex && origIdx < mesh.Normals.Length)
                        normal = mesh.Normals[origIdx];
                    else
                        normal = System.Numerics.Vector3.UnitY;

                    // Get UV: per-polygon-vertex or per-vertex
                    System.Numerics.Vector2 uv;
                    if (uvsArePerPolygonVertex && t < mesh.UVs.Length)
                        uv = mesh.UVs[t];
                    else if (!uvsArePerPolygonVertex && origIdx < mesh.UVs.Length)
                        uv = mesh.UVs[origIdx];
                    else
                        uv = System.Numerics.Vector2.Zero;

                    var expanded = new ExpandedVertex(pos, normal, uv);

                    // Deduplicate: reuse existing vertex if position+normal+uv match
                    if (!dedupMap.TryGetValue(expanded, out uint newIdx))
                    {
                        newIdx = (uint)finalVertices.Count;
                        finalVertices.Add(expanded);
                        dedupMap[expanded] = newIdx;

                        // Update bounds
                        boundsMin = System.Numerics.Vector3.Min(boundsMin, pos);
                        boundsMax = System.Numerics.Vector3.Max(boundsMax, pos);
                    }

                    finalIndices.Add(newIdx);
                }

                int submeshIndexCount = finalIndices.Count - submeshIndexStart;
                if (submeshIndexCount > 0)
                {
                    submeshInfos.Add((submeshIndexStart, submeshIndexCount, meshSlot));
                    meshSlot++;
                }
            }

            if (finalVertices.Count == 0)
            {
                return new ImportResult
                {
                    Success = false,
                    Error = "No valid vertices after processing FBX meshes"
                };
            }

            // Write the payload in ViewportRenderer expected format:
            // [int32 vertexDataLen][byte[] vertexData][uint32 indexDataLen][byte[] indexData]
            // [int32 submeshCount][per submesh: int32 offset, int32 count, int32 slot]
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // Vertex data: 32 bytes per vertex (Position(12) + Normal(12) + UV(8))
            int vertexByteCount = finalVertices.Count * 32;
            writer.Write(vertexByteCount);
            for (int i = 0; i < finalVertices.Count; i++)
            {
                var v = finalVertices[i];
                writer.Write(v.Position.X);
                writer.Write(v.Position.Y);
                writer.Write(v.Position.Z);
                writer.Write(v.Normal.X);
                writer.Write(v.Normal.Y);
                writer.Write(v.Normal.Z);
                writer.Write(v.UV.X);
                writer.Write(v.UV.Y);
            }

            // Index data: 4 bytes per index (uint32)
            uint indexByteCount = (uint)(finalIndices.Count * 4);
            writer.Write(indexByteCount);
            foreach (var idx in finalIndices)
            {
                writer.Write(idx);
            }

            // Submesh data
            writer.Write(submeshInfos.Count);
            foreach (var (offset, count, slot) in submeshInfos)
            {
                writer.Write(offset);
                writer.Write(count);
                writer.Write(slot);
            }

            // Metadata
            int totalTriangles = finalIndices.Count / 3;
            asset.Metadata["vertexCount"] = finalVertices.Count.ToString();
            asset.Metadata["triangleCount"] = totalTriangles.ToString();
            asset.Metadata["submeshCount"] = submeshInfos.Count.ToString();
            asset.Metadata["meshCount"] = allMeshes.Count.ToString();
            asset.Metadata["materialSlotCount"] = submeshInfos.Count.ToString();
            
            string targetDir = options?.Settings != null && options.Settings.TryGetValue("TargetDirectory", out var td) && td is string tdStr 
                ? tdStr 
                : Path.Combine("Assets", asset.AssetName);

            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
            string materialsDir = Path.Combine(targetDir, "Materials");
            if (!Directory.Exists(materialsDir)) Directory.CreateDirectory(materialsDir);

            var slotNames = new List<string>();
            for (int i = 0; i < submeshInfos.Count; i++)
            {
                // Use material name from FBX if available
                string matName = (i < allMeshes.Count && !string.IsNullOrEmpty(allMeshes[i].MaterialName))
                    ? allMeshes[i].MaterialName
                    : $"Material_{i}";
                // Sanitize for filename
                matName = string.Join("_", matName.Split(Path.GetInvalidFileNameChars()));
                slotNames.Add(matName);
                
                string matFileName = $"{matName}.blueskyasset";
                string matPath = Path.Combine(materialsDir, matFileName);
                
                // Create a default material for each slot if it doesn't exist
                // Use distinct colors for easy visualization (production-ready neutral palette)
                if (!File.Exists(matPath))
                {
                    // Neutral color palette for auto-generated materials (subtle but distinct)
                    var neutralPalette = new[]
                    {
                        (0.85f, 0.85f, 0.85f), // Slot 0: Light Grey (body)
                        (0.15f, 0.15f, 0.15f), // Slot 1: Dark Grey (trim)
                        (0.95f, 0.95f, 0.95f), // Slot 2: White (glass/chrome)
                        (0.65f, 0.65f, 0.65f), // Slot 3: Medium Grey
                        (0.45f, 0.45f, 0.45f), // Slot 4: Charcoal
                        (0.75f, 0.75f, 0.75f), // Slot 5: Silver
                        (0.55f, 0.55f, 0.55f), // Slot 6: Steel
                        (0.35f, 0.35f, 0.35f)  // Slot 7: Graphite
                    };
                    
                    var (r, g, b) = neutralPalette[i % neutralPalette.Length];
                    var matAsset = new MaterialAsset 
                    { 
                        MaterialName = matName, 
                        MaterialId = Guid.NewGuid(),
                        Albedo = new Vector3Data(r, g, b),
                        Metallic = 0.1f,
                        Roughness = 0.6f,
                        AO = 1.0f
                    };
                    matAsset.Save(matPath);
                }
                
                asset.Metadata[$"materialSlot{i}"] = matPath;
            }
            asset.Metadata["materialSlots"] = string.Join(",", slotNames);
            
            asset.Metadata["format"] = "Packed32";
            asset.Metadata["boundsMin"] = $"{boundsMin.X},{boundsMin.Y},{boundsMin.Z}";
            asset.Metadata["boundsMax"] = $"{boundsMax.X},{boundsMax.Y},{boundsMax.Z}";
            asset.Type = AssetType.StaticMesh;

            ErrorHandler.LogInfo(
                $"✓ FBX imported: {finalVertices.Count} verts, {totalTriangles} tris, {submeshInfos.Count} submesh(es) from {allMeshes.Count} mesh(es)",
                "FBXImportHandler");

            return new ImportResult
            {
                Success = true,
                PayloadData = ms.ToArray()
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
/// GLTF/GLB import handler - imports ALL meshes, materials, and textures from GLTF 2.0 files.
/// Handles multi-mesh GLB files (like Aston Martin Valhalla with 308 meshes + 47 materials).
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
public class GLTFImportHandler : IAssetImportHandler
{
    public string[] SupportedExtensions => new[] { ".gltf", ".glb" };
    public AssetType AssetType => AssetType.StaticMesh;

    /// <summary>
    /// Recursively compute world transforms for all nodes in the scene graph.
    /// GLTF scene graph: each node has local transform (TRS or matrix) and children.
    /// World transform = parent world transform × local transform.
    /// This is CRITICAL for multi-mesh models where each mesh part has its own position/rotation.
    /// </summary>
    private static void ComputeNodeTransforms(
        BlueSky.Animation.GLTF.GltfRoot root, 
        int nodeIdx, 
        System.Numerics.Matrix4x4 parentTransform, 
        Dictionary<int, System.Numerics.Matrix4x4> outTransforms)
    {
        if (root.Nodes == null || nodeIdx >= root.Nodes.Length) return;
        
        var node = root.Nodes[nodeIdx];
        
        // Compute local transform from TRS or matrix
        System.Numerics.Matrix4x4 localTransform;
        
        if (node.Matrix != null && node.Matrix.Length == 16)
        {
            // Matrix property (column-major)
            localTransform = new System.Numerics.Matrix4x4(
                node.Matrix[0], node.Matrix[1], node.Matrix[2], node.Matrix[3],
                node.Matrix[4], node.Matrix[5], node.Matrix[6], node.Matrix[7],
                node.Matrix[8], node.Matrix[9], node.Matrix[10], node.Matrix[11],
                node.Matrix[12], node.Matrix[13], node.Matrix[14], node.Matrix[15]
            );
        }
        else
        {
            // TRS properties (Translation, Rotation, Scale)
            var translation = (node.Translation != null && node.Translation.Length == 3)
                ? new System.Numerics.Vector3(node.Translation[0], node.Translation[1], node.Translation[2])
                : System.Numerics.Vector3.Zero;
            
            var rotation = (node.Rotation != null && node.Rotation.Length == 4)
                ? new System.Numerics.Quaternion(node.Rotation[0], node.Rotation[1], node.Rotation[2], node.Rotation[3])
                : System.Numerics.Quaternion.Identity;
            
            var scale = (node.Scale != null && node.Scale.Length == 3)
                ? new System.Numerics.Vector3(node.Scale[0], node.Scale[1], node.Scale[2])
                : System.Numerics.Vector3.One;
            
            // Build TRS matrix: T × R × S (GLTF spec: apply scale, then rotation, then translation)
            // System.Numerics uses row-major, so we compose right-to-left
            localTransform = System.Numerics.Matrix4x4.CreateScale(scale) *
                             System.Numerics.Matrix4x4.CreateFromQuaternion(rotation) *
                             System.Numerics.Matrix4x4.CreateTranslation(translation);
        }
        
        // Compute world transform: local × parent (GLTF column-major order)
        // System.Numerics is row-major, so this becomes: parent * local in code
        var worldTransform = localTransform * parentTransform;
        outTransforms[nodeIdx] = worldTransform;
        
        // Recurse to children
        if (node.Children != null)
        {
            foreach (var childIdx in node.Children)
            {
                ComputeNodeTransforms(root, childIdx, worldTransform, outTransforms);
            }
        }
    }
    
    /// <summary>
    /// Flip texture horizontally (mirror left-right) to fix GLTF coordinate system mismatch.
    /// Fixes mirrored text/logos like "ASTON MARTIN" appearing as "NITRAM NOTSA".
    /// Operates in-place on RGBA8 pixel data.
    /// </summary>
    private static void FlipTextureHorizontally(byte[] pixels, int width, int height)
    {
        int stride = width * 4; // 4 bytes per pixel (RGBA)
        byte[] rowBuffer = new byte[stride];
        
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * stride;
            
            // Reverse pixels in this row
            for (int x = 0; x < width / 2; x++)
            {
                int leftIdx = rowStart + x * 4;
                int rightIdx = rowStart + (width - 1 - x) * 4;
                
                // Swap RGBA pixels
                for (int c = 0; c < 4; c++)
                {
                    byte temp = pixels[leftIdx + c];
                    pixels[leftIdx + c] = pixels[rightIdx + c];
                    pixels[rightIdx + c] = temp;
                }
            }
        }
    }

    public ImportResult Import(string sourceFile, BlueAsset asset, ImportOptions? options)
    {
        try
        {
            ErrorHandler.LogInfo($"[GLTFImportHandler] Starting GLTF import: {Path.GetFileName(sourceFile)}", "GLTFImportHandler");

            var importer = BlueSky.Animation.GLTF.GltfImporter.FromFile(sourceFile);
            var root = importer.Root;

            if (root.Meshes == null || root.Meshes.Length == 0)
            {
                return new ImportResult { Success = false, Error = "No meshes found in GLTF file" };
            }

            // Setup directories
            string targetDir = options?.Settings != null && options.Settings.TryGetValue("TargetDirectory", out var td) && td is string tdStr 
                ? tdStr 
                : Path.Combine("Assets", asset.AssetName);
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
            string materialsDir = Path.Combine(targetDir, "Materials");
            string texturesDir = Path.Combine(targetDir, "Textures");
            if (!Directory.Exists(materialsDir)) Directory.CreateDirectory(materialsDir);
            if (!Directory.Exists(texturesDir)) Directory.CreateDirectory(texturesDir);

            // ── STEP 1: Extract and save all textures ──────────────────────────────────────
            var texturePathMap = new Dictionary<int, string>(); // GLTF texture index → .blueskyasset path
            if (root.Textures != null && root.Textures.Length > 0)
            {
                ErrorHandler.LogInfo($"[GLTFImportHandler] ═══════════════════════════════════════════════════════════", "GLTFImportHandler");
                ErrorHandler.LogInfo($"[GLTFImportHandler] EXTRACTING {root.Textures.Length} EMBEDDED TEXTURES", "GLTFImportHandler");
                ErrorHandler.LogInfo($"[GLTFImportHandler] ═══════════════════════════════════════════════════════════", "GLTFImportHandler");
                
                for (int i = 0; i < root.Textures.Length; i++)
                {
                    try
                    {
                        var texData = importer.ExtractTexture(i);
                        if (texData != null && texData.Length > 0)
                        {
                            // Decode image bytes using StbImageSharp
                            StbImageSharp.StbImage.stbi_set_flip_vertically_on_load(0);
                            using var stream = new MemoryStream(texData);
                            var imageResult = StbImageSharp.ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
                            
                            if (imageResult != null)
                            {
                                // Use GLTF image name if available, otherwise texture name, otherwise index
                                string texName = $"Texture_{i}";
                                
                                // Try to get name from GLTF texture
                                if (root.Textures[i].Name != null && !string.IsNullOrWhiteSpace(root.Textures[i].Name))
                                {
                                    texName = root.Textures[i].Name;
                                }
                                // Try to get name from GLTF image
                                else if (root.Textures[i].Source.HasValue && 
                                         root.Images != null && 
                                         root.Textures[i].Source.Value < root.Images.Length &&
                                         root.Images[root.Textures[i].Source.Value].Name != null)
                                {
                                    texName = root.Images[root.Textures[i].Source.Value].Name;
                                }
                                
                                // Sanitize filename
                                texName = string.Join("_", texName.Split(Path.GetInvalidFileNameChars()));
                                
                                var texAsset = new BlueAsset { AssetName = texName, Type = AssetType.Texture, ImportDate = DateTime.UtcNow };
                                
                                // Pack texture data into payload (width, height, channels, data)
                                using var texMs = new MemoryStream();
                                using var texWriter = new BinaryWriter(texMs);
                                texWriter.Write(imageResult.Width);
                                texWriter.Write(imageResult.Height);
                                texWriter.Write(4); // RGBA channels
                                texWriter.Write(imageResult.Data.Length);
                                texWriter.Write(imageResult.Data);
                                
                                texAsset.PayloadData = texMs.ToArray();
                                texAsset.Metadata["width"] = imageResult.Width.ToString();
                                texAsset.Metadata["height"] = imageResult.Height.ToString();
                                texAsset.Metadata["format"] = "RGBA8";
                                texAsset.Metadata["channels"] = "4";
                                
                                string texPath = Path.Combine(texturesDir, $"{texName}.blueskyasset");
                                if (texAsset.Save(texPath))
                                {
                                    texturePathMap[i] = texPath;
                                    ErrorHandler.LogInfo($"[GLTFImportHandler] ✓ Texture {i}: '{texName}' ({imageResult.Width}x{imageResult.Height} RGBA)", "GLTFImportHandler");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorHandler.LogWarning($"[GLTFImportHandler] Failed to extract texture {i}: {ex.Message}", "GLTFImportHandler");
                    }
                }
                ErrorHandler.LogInfo($"[GLTFImportHandler] ═══════════════════════════════════════════════════════════", "GLTFImportHandler");
                ErrorHandler.LogInfo($"[GLTFImportHandler] ✓ EXTRACTED {texturePathMap.Count}/{root.Textures.Length} TEXTURES", "GLTFImportHandler");
                ErrorHandler.LogInfo($"[GLTFImportHandler] ═══════════════════════════════════════════════════════════", "GLTFImportHandler");
            }

            // ── STEP 2: Convert and save all materials ─────────────────────────────────────
            var materialPathMap = new Dictionary<int, string>(); // GLTF material index → .blueskyasset path
            if (root.Materials != null && root.Materials.Length > 0)
            {
                ErrorHandler.LogInfo($"[GLTFImportHandler] ═══════════════════════════════════════════════════════════", "GLTFImportHandler");
                ErrorHandler.LogInfo($"[GLTFImportHandler] CONVERTING {root.Materials.Length} MATERIALS WITH FULL PBR WORKFLOW", "GLTFImportHandler");
                ErrorHandler.LogInfo($"[GLTFImportHandler] ═══════════════════════════════════════════════════════════", "GLTFImportHandler");
                
                for (int i = 0; i < root.Materials.Length; i++)
                {
                    try
                    {
                        var gltfMat = root.Materials[i];
                        string matName = string.IsNullOrEmpty(gltfMat.Name) ? $"Material_{i}" : gltfMat.Name;
                        matName = string.Join("_", matName.Split(Path.GetInvalidFileNameChars()));

                        ErrorHandler.LogInfo($"[GLTFImportHandler] ─── Material {i}: '{matName}' ───", "GLTFImportHandler");

                        var matAsset = new MaterialAsset 
                        { 
                            MaterialName = matName, 
                            MaterialId = Guid.NewGuid(),
                            Albedo = new Vector3Data(1.0f, 1.0f, 1.0f), // Default white (textures will override)
                            Metallic = 0.0f,
                            Roughness = 0.5f,
                            Opacity = 1.0f
                        };

                        // PBR Metallic-Roughness workflow
                        if (gltfMat.PbrMetallicRoughness != null)
                        {
                            var pbr = gltfMat.PbrMetallicRoughness;
                            
                            // Base color factor
                            if (pbr.BaseColorFactor != null && pbr.BaseColorFactor.Length >= 3)
                            {
                                matAsset.Albedo = new Vector3Data(pbr.BaseColorFactor[0], pbr.BaseColorFactor[1], pbr.BaseColorFactor[2]);
                                if (pbr.BaseColorFactor.Length >= 4)
                                    matAsset.Opacity = pbr.BaseColorFactor[3];
                                ErrorHandler.LogInfo($"[GLTFImportHandler]   BaseColor: ({pbr.BaseColorFactor[0]:F3}, {pbr.BaseColorFactor[1]:F3}, {pbr.BaseColorFactor[2]:F3})", "GLTFImportHandler");
                            }
                            
                            matAsset.Metallic = pbr.MetallicFactor;
                            matAsset.Roughness = pbr.RoughnessFactor;
                            ErrorHandler.LogInfo($"[GLTFImportHandler]   Metallic: {pbr.MetallicFactor:F2}, Roughness: {pbr.RoughnessFactor:F2}", "GLTFImportHandler");

                            // ★ CRITICAL FIX: Albedo/BaseColor texture
                            if (pbr.BaseColorTexture != null && pbr.BaseColorTexture.Index >= 0)
                            {
                                if (texturePathMap.TryGetValue(pbr.BaseColorTexture.Index, out var albedoPath))
                                {
                                    matAsset.AlbedoTexturePath = albedoPath;
                                    ErrorHandler.LogInfo($"[GLTFImportHandler]   ✓ Albedo Texture: {Path.GetFileName(albedoPath)}", "GLTFImportHandler");
                                }
                                else
                                {
                                    ErrorHandler.LogWarning($"[GLTFImportHandler]   ✗ Albedo texture index {pbr.BaseColorTexture.Index} not found in texture map!", "GLTFImportHandler");
                                }
                            }
                            else
                            {
                                ErrorHandler.LogInfo($"[GLTFImportHandler]   No albedo texture (using base color factor)", "GLTFImportHandler");
                            }

                            // ★ CRITICAL FIX: Metallic-Roughness texture (packed: R=unused, G=roughness, B=metallic)
                            if (pbr.MetallicRoughnessTexture != null && pbr.MetallicRoughnessTexture.Index >= 0)
                            {
                                if (texturePathMap.TryGetValue(pbr.MetallicRoughnessTexture.Index, out var mrPath))
                                {
                                    // ★ FIX: Write to RMATexturePath (what ViewportRenderer reads)
                                    // AND RoughnessTexturePath (for other consumers)
                                    matAsset.RMATexturePath = mrPath;
                                    matAsset.RoughnessTexturePath = mrPath;
                                    ErrorHandler.LogInfo($"[GLTFImportHandler]   ✓ Metallic-Roughness Texture → RMA+Roughness: {Path.GetFileName(mrPath)}", "GLTFImportHandler");
                                }
                                else
                                {
                                    ErrorHandler.LogWarning($"[GLTFImportHandler]   ✗ MR texture index {pbr.MetallicRoughnessTexture.Index} not found!", "GLTFImportHandler");
                                }
                            }
                        }

                        // Normal map
                        if (gltfMat.NormalTexture != null && gltfMat.NormalTexture.Index >= 0)
                        {
                            if (texturePathMap.TryGetValue(gltfMat.NormalTexture.Index, out var normalPath))
                            {
                                matAsset.NormalTexturePath = normalPath;
                                ErrorHandler.LogInfo($"[GLTFImportHandler]   ✓ Normal Texture: {Path.GetFileName(normalPath)}", "GLTFImportHandler");
                            }
                        }

                        // Occlusion map (AO) - MaterialAsset doesn't have AOTexturePath, skip for now
                        // TODO: Add AOTexturePath to MaterialAsset class
                        if (gltfMat.OcclusionTexture != null && gltfMat.OcclusionTexture.Index >= 0)
                        {
                            if (texturePathMap.TryGetValue(gltfMat.OcclusionTexture.Index, out var aoPath))
                            {
                                // Store in RMA texture path as a workaround (R=roughness, G=metallic, B=AO)
                                if (string.IsNullOrEmpty(matAsset.RMATexturePath))
                                {
                                    matAsset.RMATexturePath = aoPath;
                                    ErrorHandler.LogInfo($"[GLTFImportHandler]   ✓ AO Texture (as RMA): {Path.GetFileName(aoPath)}", "GLTFImportHandler");
                                }
                            }
                        }

                        // Emissive
                        if (gltfMat.EmissiveFactor != null && gltfMat.EmissiveFactor.Length >= 3 && 
                            (gltfMat.EmissiveFactor[0] > 0 || gltfMat.EmissiveFactor[1] > 0 || gltfMat.EmissiveFactor[2] > 0))
                        {
                            matAsset.Emission = new Vector3Data(gltfMat.EmissiveFactor[0], gltfMat.EmissiveFactor[1], gltfMat.EmissiveFactor[2]);
                            matAsset.EmissionIntensity = 1.0f;
                            ErrorHandler.LogInfo($"[GLTFImportHandler]   Emissive: ({gltfMat.EmissiveFactor[0]:F2}, {gltfMat.EmissiveFactor[1]:F2}, {gltfMat.EmissiveFactor[2]:F2})", "GLTFImportHandler");
                        }

                        // Emissive texture - MaterialAsset doesn't have EmissiveTexturePath, skip for now
                        // TODO: Add EmissiveTexturePath to MaterialAsset class
                        if (gltfMat.EmissiveTexture != null && gltfMat.EmissiveTexture.Index >= 0)
                        {
                            if (texturePathMap.TryGetValue(gltfMat.EmissiveTexture.Index, out var emissivePath))
                            {
                                ErrorHandler.LogInfo($"[GLTFImportHandler]   ⚠ Emissive Texture found but MaterialAsset doesn't support it yet: {Path.GetFileName(emissivePath)}", "GLTFImportHandler");
                            }
                        }

                        // Alpha mode
                        if (!string.IsNullOrEmpty(gltfMat.AlphaMode))
                        {
                            matAsset.BlendMode = gltfMat.AlphaMode switch
                            {
                                "BLEND" => BlueSky.Rendering.Materials.BlendMode.AlphaBlend,
                                "MASK" => BlueSky.Rendering.Materials.BlendMode.AlphaBlend,
                                _ => BlueSky.Rendering.Materials.BlendMode.Opaque
                            };
                            ErrorHandler.LogInfo($"[GLTFImportHandler]   Alpha Mode: {gltfMat.AlphaMode} → {matAsset.BlendMode}", "GLTFImportHandler");
                        }

                        // Double-sided
                        if (gltfMat.DoubleSided)
                        {
                            ErrorHandler.LogInfo($"[GLTFImportHandler]   Double-sided: true", "GLTFImportHandler");
                        }

                        string matPath = Path.Combine(materialsDir, $"{matName}.blueskyasset");
                        matAsset.Save(matPath);
                        materialPathMap[i] = matPath;
                        
                        ErrorHandler.LogInfo($"[GLTFImportHandler]   ✓ Saved: {Path.GetFileName(matPath)}", "GLTFImportHandler");
                    }
                    catch (Exception ex)
                    {
                        ErrorHandler.LogError($"[GLTFImportHandler] Failed to convert material {i}: {ex.Message}", ex, "GLTFImportHandler");
                        
                        // Create fallback material
                        var fallbackMat = new MaterialAsset 
                        { 
                            MaterialName = $"Material_{i}", 
                            MaterialId = Guid.NewGuid(),
                            Albedo = new Vector3Data(1.0f, 0.0f, 1.0f) // Magenta = missing material
                        };
                        string fallbackPath = Path.Combine(materialsDir, $"Material_{i}.blueskyasset");
                        fallbackMat.Save(fallbackPath);
                        materialPathMap[i] = fallbackPath;
                    }
                }
                ErrorHandler.LogInfo($"[GLTFImportHandler] ═══════════════════════════════════════════════════════════", "GLTFImportHandler");
                ErrorHandler.LogInfo($"[GLTFImportHandler] ✓ CONVERTED {materialPathMap.Count}/{root.Materials.Length} MATERIALS", "GLTFImportHandler");
                ErrorHandler.LogInfo($"[GLTFImportHandler] ═══════════════════════════════════════════════════════════", "GLTFImportHandler");
            }

            // ── STEP 3: Build scene graph and compute node transforms ──────────────────────
            var nodeTransforms = new Dictionary<int, System.Numerics.Matrix4x4>();
            var nodeToMesh = new Dictionary<int, int>(); // node index → mesh index
            
            if (root.Nodes != null)
            {
                // Build node hierarchy and compute world transforms
                for (int i = 0; i < root.Nodes.Length; i++)
                {
                    var node = root.Nodes[i];
                    if (node.Mesh.HasValue)
                    {
                        nodeToMesh[i] = node.Mesh.Value;
                    }
                }
                
                // Compute world transforms for all nodes (traverse from scene roots)
                if (root.Scenes != null && root.Scene.HasValue && root.Scene.Value < root.Scenes.Length)
                {
                    var scene = root.Scenes[root.Scene.Value];
                    if (scene.Nodes != null)
                    {
                        foreach (var rootNodeIdx in scene.Nodes)
                        {
                            ComputeNodeTransforms(root, rootNodeIdx, System.Numerics.Matrix4x4.Identity, nodeTransforms);
                        }
                    }
                }
                else if (root.Scenes != null && root.Scenes.Length > 0)
                {
                    // Fallback: use first scene
                    var scene = root.Scenes[0];
                    if (scene.Nodes != null)
                    {
                        foreach (var rootNodeIdx in scene.Nodes)
                        {
                            ComputeNodeTransforms(root, rootNodeIdx, System.Numerics.Matrix4x4.Identity, nodeTransforms);
                        }
                    }
                }
            }

            // ── STEP 4: Extract ALL meshes and combine into submeshes ──────────────────────
            var finalVertices = new List<ExpandedVertex>();
            var finalIndices = new List<uint>();
            var submeshInfos = new List<(int indexOffset, int indexCount, int materialSlot)>();

            // GLTF spec uses meters as the default unit
            // No scale factor applied - use native GLTF units
            int totalPrimitives = 0;
            
            // Process meshes via scene graph nodes (to apply transforms)
            foreach (var kvp in nodeToMesh)
            {
                int nodeIdx = kvp.Key;
                int meshIdx = kvp.Value;
                
                if (meshIdx >= root.Meshes.Length) continue;
                
                // Get world transform for this node
                System.Numerics.Matrix4x4 worldTransform = nodeTransforms.TryGetValue(nodeIdx, out var transform) 
                    ? transform 
                    : System.Numerics.Matrix4x4.Identity;
                
                var gltfMesh = importer.ExtractMesh(meshIdx);
                foreach (var prim in gltfMesh.Primitives)
                {
                    if (prim.Positions == null) continue;

                    int submeshIndexStart = finalIndices.Count;
                    int submeshVertexStart = finalVertices.Count;

                    // Add all vertices for this primitive (with world transform)
                    for (int i = 0; i < prim.Positions.Length; i++)
                    {
                        // Apply world transform to position
                        var localPos = prim.Positions[i];
                        var worldPos = System.Numerics.Vector3.Transform(localPos, worldTransform);
                        
                        // COORDINATE SYSTEM FIX: glTF is right-handed, engine is left-handed.
                        // Negate X to convert handedness. This fixes:
                        //   - Mirrored text ("ASTON MARTIN" → "NITRAM NOTSA")
                        //   - Steering wheel on wrong side
                        //   - All geometry appearing as a mirror image
                        worldPos.X = -worldPos.X;
                        
                        // Apply node transform to normal (rotation only, no translation/scale)
                        var localNormal = (prim.Normals != null && i < prim.Normals.Length) ? prim.Normals[i] : System.Numerics.Vector3.UnitY;
                        var worldNormal = System.Numerics.Vector3.TransformNormal(localNormal, worldTransform);
                        worldNormal = System.Numerics.Vector3.Normalize(worldNormal);
                        worldNormal.X = -worldNormal.X; // Match position X-negate
                        
                        var uv = (prim.TexCoords0 != null && i < prim.TexCoords0.Length) ? prim.TexCoords0[i] : System.Numerics.Vector2.Zero;

                        finalVertices.Add(new ExpandedVertex(worldPos, worldNormal, uv));
                    }

                    // Add indices (offset by submeshVertexStart to reference global vertex buffer)
                    if (prim.Indices != null && prim.Indices.Length > 0)
                    {
                        foreach (var idx in prim.Indices)
                        {
                            finalIndices.Add((uint)(submeshVertexStart + idx));
                        }
                    }
                    else
                    {
                        // No indices - generate sequential
                        for (int i = 0; i < prim.Positions.Length; i++)
                        {
                            finalIndices.Add((uint)(submeshVertexStart + i));
                        }
                    }

                    int submeshIndexCount = finalIndices.Count - submeshIndexStart;
                    
                    // WINDING ORDER FIX: Negating X flips triangle winding.
                    // Swap indices 1 and 2 of each triangle to restore correct front-face orientation.
                    for (int t = submeshIndexStart; t + 2 < finalIndices.Count; t += 3)
                    {
                        (finalIndices[t + 1], finalIndices[t + 2]) = (finalIndices[t + 2], finalIndices[t + 1]);
                    }
                    
                    if (submeshIndexCount > 0)
                    {
                        int matSlot = prim.Material ?? 0;
                        submeshInfos.Add((submeshIndexStart, submeshIndexCount, matSlot));
                        totalPrimitives++;
                    }
                }
            }

            if (finalVertices.Count == 0)
            {
                return new ImportResult { Success = false, Error = "No valid vertices after processing GLTF meshes" };
            }

            ErrorHandler.LogInfo($"[GLTFImportHandler] ✓ Extracted {totalPrimitives} primitives from {nodeToMesh.Count} nodes", "GLTFImportHandler");
            ErrorHandler.LogInfo($"[GLTFImportHandler] ✓ Total: {finalVertices.Count} vertices, {finalIndices.Count / 3} triangles", "GLTFImportHandler");

            // ── STEP 5: Pack binary data ───────────────────────────────────────────────────
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // Vertex data (32 bytes per vertex)
            int vertexByteCount = finalVertices.Count * 32;
            writer.Write(vertexByteCount);
            foreach (var v in finalVertices)
            {
                writer.Write(v.Position.X); writer.Write(v.Position.Y); writer.Write(v.Position.Z);
                writer.Write(v.Normal.X); writer.Write(v.Normal.Y); writer.Write(v.Normal.Z);
                writer.Write(v.UV.X); writer.Write(v.UV.Y);
            }

            // Index data
            uint indexByteCount = (uint)(finalIndices.Count * 4);
            writer.Write(indexByteCount);
            foreach (var idx in finalIndices) writer.Write(idx);

            // Submesh data
            writer.Write(submeshInfos.Count);
            foreach (var (offset, count, slot) in submeshInfos)
            {
                writer.Write(offset);
                writer.Write(count);
                writer.Write(slot);
            }

            // ── STEP 6: Assign materials to slots ──────────────────────────────────────────
            var usedMaterialSlots = new HashSet<int>();
            foreach (var (_, _, slot) in submeshInfos) usedMaterialSlots.Add(slot);

            var slotNames = new List<string>();
            foreach (var matSlot in usedMaterialSlots.OrderBy(x => x))
            {
                string matName = $"Material_{matSlot}";
                
                if (materialPathMap.TryGetValue(matSlot, out var matPath))
                {
                    // Extract material name from path
                    matName = Path.GetFileNameWithoutExtension(matPath);
                    asset.Metadata[$"materialSlot{matSlot}"] = matPath;
                }
                else
                {
                    // Fallback: create default material
                    var fallbackMat = new MaterialAsset 
                    { 
                        MaterialName = matName, 
                        MaterialId = Guid.NewGuid(),
                        Albedo = new Vector3Data(0.8f, 0.8f, 0.8f)
                    };
                    string fallbackPath = Path.Combine(materialsDir, $"{matName}.blueskyasset");
                    fallbackMat.Save(fallbackPath);
                    asset.Metadata[$"materialSlot{matSlot}"] = fallbackPath;
                }
                
                slotNames.Add(matName);
            }

            // Metadata
            asset.Metadata["materialSlots"] = string.Join(",", slotNames); // CRITICAL: Static Mesh Editor needs this!
            asset.Metadata["vertexCount"] = finalVertices.Count.ToString();
            asset.Metadata["triangleCount"] = (finalIndices.Count / 3).ToString();
            asset.Metadata["submeshCount"] = submeshInfos.Count.ToString();
            asset.Metadata["materialSlotCount"] = (usedMaterialSlots.Count > 0 ? usedMaterialSlots.Max() + 1 : 0).ToString();
            asset.Metadata["format"] = "Packed32";

            ErrorHandler.LogInfo($"[GLTFImportHandler] ✓ GLTF import complete: {submeshInfos.Count} submeshes, {usedMaterialSlots.Count} material slots", "GLTFImportHandler");

            return new ImportResult
            {
                Success = true,
                PayloadData = ms.ToArray()
            };
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError($"GLTF import failed: {ex.Message}", ex, "GLTFImportHandler");
            ErrorHandler.LogError($"Stack trace: {ex.StackTrace}", context: "GLTFImportHandler");
            return new ImportResult { Success = false, Error = ex.Message };
        }
    }
}
