using BlueSky.Core.Diagnostics;

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

            // Import data
            var importResult = importer.Import(sourceFilePath, asset, options);
            
            if (!importResult.Success)
            {
                ErrorHandler.LogError($"Import failed: {importResult.Error}", context: "AssetImporter");
                return null;
            }

            // Save asset file
            var assetFileName = $"{asset.AssetName}.blueskyasset";
            var assetPath = Path.Combine(_assetsDirectory, assetFileName);
            
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
            
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
/// Mesh import handler (FBX, OBJ, glTF, etc.)
/// </summary>
public class MeshImportHandler : IAssetImportHandler
{
    public string[] SupportedExtensions => new[] { ".fbx", ".obj", ".gltf", ".glb", ".dae", ".blend" };
    public AssetType AssetType => AssetType.StaticMesh;

    public ImportResult Import(string sourceFile, BlueAsset asset, ImportOptions? options)
    {
        try
        {
            var extension = Path.GetExtension(sourceFile).ToLowerInvariant();

            if (extension == ".obj")
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
                var (vertexData, indexData, vertexCount, indexCount) = OBJParser.ConvertToEngineData(objMesh);

                // Pack binary data into Payload
                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);
                writer.Write(vertexData.Length);
                writer.Write(vertexData);
                writer.Write(indexData.Length);
                writer.Write(indexData);

                // Update asset metadata
                asset.Metadata["vertexCount"] = vertexCount.ToString();
                asset.Metadata["triangleCount"] = (indexCount / 3).ToString();
                asset.Metadata["meshCount"] = "1";
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
            else
            {
                // Placeholder for other formats (FBX, glTF, etc.)
                asset.Metadata["vertexCount"] = "0";
                asset.Metadata["triangleCount"] = "0";
                asset.Metadata["meshCount"] = "0";
                asset.Metadata["format"] = "Unsupported";

                return new ImportResult
                {
                    Success = false,
                    Error = $"Unsupported mesh format: {extension}. Only OBJ is currently supported."
                };
            }
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
                DataFilePath = sourceFile // Scripts don't need conversion
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
