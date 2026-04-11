using System.Collections.Concurrent;
using BlueSky.Core.Diagnostics;

namespace BlueSky.Core.Assets;

/// <summary>
/// Asset database - tracks all assets in the project and their dependencies.
/// Like Unreal's Asset Registry!
/// </summary>
public class AssetDatabase
{
    private readonly string _projectPath;
    private readonly string _assetsDirectory;
    private readonly ConcurrentDictionary<Guid, AssetEntry> _assetsByGuid = new();
    private readonly ConcurrentDictionary<string, Guid> _assetsByPath = new();
    private readonly FileSystemWatcher? _watcher;
    private bool _isScanning;

    public int AssetCount => _assetsByGuid.Count;
    public IEnumerable<AssetEntry> AllAssets => _assetsByGuid.Values;

    public AssetDatabase(string projectPath, bool watchForChanges = true)
    {
        _projectPath = projectPath;
        
        var project = BlueProject.Load(projectPath);
        if (project == null)
        {
            throw new Exception($"Failed to load project: {projectPath}");
        }

        _assetsDirectory = project.GetAssetsDirectory(projectPath);

        // Initial scan
        Scan();

        // Watch for changes
        if (watchForChanges)
        {
            _watcher = new FileSystemWatcher(_assetsDirectory)
            {
                Filter = "*.blueasset",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnAssetChanged;
            _watcher.Created += OnAssetCreated;
            _watcher.Deleted += OnAssetDeleted;
            _watcher.Renamed += OnAssetRenamed;

            ErrorHandler.LogInfo("Asset database watching for changes", "AssetDatabase");
        }

        ErrorHandler.LogInfo($"Asset database initialized with {AssetCount} assets", "AssetDatabase");
    }

    /// <summary>
    /// Scan the assets directory and build the database.
    /// </summary>
    public void Scan()
    {
        if (_isScanning) return;
        
        _isScanning = true;
        ErrorHandler.LogInfo("Scanning assets directory...", "AssetDatabase");

        try
        {
            var assetFiles = Directory.GetFiles(_assetsDirectory, "*.blueasset", SearchOption.AllDirectories);
            
            int loaded = 0;
            int failed = 0;

            foreach (var assetFile in assetFiles)
            {
                if (LoadAssetEntry(assetFile))
                {
                    loaded++;
                }
                else
                {
                    failed++;
                }
            }

            ErrorHandler.LogInfo($"Scan complete: {loaded} assets loaded, {failed} failed", "AssetDatabase");
        }
        finally
        {
            _isScanning = false;
        }
    }

    /// <summary>
    /// Get asset by GUID.
    /// </summary>
    public AssetEntry? GetAsset(Guid assetId)
    {
        return _assetsByGuid.TryGetValue(assetId, out var entry) ? entry : null;
    }

    /// <summary>
    /// Get asset by path.
    /// </summary>
    public AssetEntry? GetAssetByPath(string path)
    {
        if (_assetsByPath.TryGetValue(path, out var guid))
        {
            return GetAsset(guid);
        }
        return null;
    }

    /// <summary>
    /// Find assets by type.
    /// </summary>
    public List<AssetEntry> FindAssetsByType(AssetType type)
    {
        return _assetsByGuid.Values.Where(a => a.Asset.Type == type).ToList();
    }

    /// <summary>
    /// Find assets by name (fuzzy search).
    /// </summary>
    public List<AssetEntry> FindAssetsByName(string searchTerm)
    {
        searchTerm = searchTerm.ToLowerInvariant();
        return _assetsByGuid.Values
            .Where(a => a.Asset.AssetName.ToLowerInvariant().Contains(searchTerm))
            .ToList();
    }

    /// <summary>
    /// Get all dependencies of an asset (recursive).
    /// </summary>
    public List<AssetEntry> GetDependencies(Guid assetId, bool recursive = true)
    {
        var dependencies = new List<AssetEntry>();
        var visited = new HashSet<Guid>();
        
        GetDependenciesRecursive(assetId, dependencies, visited, recursive);
        
        return dependencies;
    }

    private void GetDependenciesRecursive(Guid assetId, List<AssetEntry> dependencies, HashSet<Guid> visited, bool recursive)
    {
        if (visited.Contains(assetId)) return;
        visited.Add(assetId);

        var asset = GetAsset(assetId);
        if (asset == null) return;

        foreach (var depId in asset.Asset.Dependencies)
        {
            var dep = GetAsset(depId);
            if (dep != null)
            {
                dependencies.Add(dep);
                
                if (recursive)
                {
                    GetDependenciesRecursive(depId, dependencies, visited, true);
                }
            }
        }
    }

    /// <summary>
    /// Get all assets that depend on this asset (reverse dependencies).
    /// </summary>
    public List<AssetEntry> GetReferencers(Guid assetId)
    {
        return _assetsByGuid.Values
            .Where(a => a.Asset.Dependencies.Contains(assetId))
            .ToList();
    }

    /// <summary>
    /// Check if an asset has circular dependencies.
    /// </summary>
    public bool HasCircularDependency(Guid assetId)
    {
        var visited = new HashSet<Guid>();
        var stack = new HashSet<Guid>();
        
        return HasCircularDependencyRecursive(assetId, visited, stack);
    }

    private bool HasCircularDependencyRecursive(Guid assetId, HashSet<Guid> visited, HashSet<Guid> stack)
    {
        if (stack.Contains(assetId)) return true; // Circular!
        if (visited.Contains(assetId)) return false;

        visited.Add(assetId);
        stack.Add(assetId);

        var asset = GetAsset(assetId);
        if (asset != null)
        {
            foreach (var depId in asset.Asset.Dependencies)
            {
                if (HasCircularDependencyRecursive(depId, visited, stack))
                {
                    return true;
                }
            }
        }

        stack.Remove(assetId);
        return false;
    }

    /// <summary>
    /// Get asset statistics.
    /// </summary>
    public AssetDatabaseStats GetStats()
    {
        var stats = new AssetDatabaseStats
        {
            TotalAssets = AssetCount
        };

        foreach (var entry in _assetsByGuid.Values)
        {
            switch (entry.Asset.Type)
            {
                case AssetType.Mesh:
                    stats.MeshCount++;
                    break;
                case AssetType.Texture:
                    stats.TextureCount++;
                    break;
                case AssetType.Material:
                    stats.MaterialCount++;
                    break;
                case AssetType.Scene:
                    stats.SceneCount++;
                    break;
                case AssetType.Script:
                    stats.ScriptCount++;
                    break;
            }
        }

        return stats;
    }

    /// <summary>
    /// Print database statistics.
    /// </summary>
    public void PrintStats()
    {
        var stats = GetStats();
        
        Console.WriteLine("\n=== Asset Database Stats ===");
        Console.WriteLine($"Total Assets: {stats.TotalAssets}");
        Console.WriteLine($"  Meshes: {stats.MeshCount}");
        Console.WriteLine($"  Textures: {stats.TextureCount}");
        Console.WriteLine($"  Materials: {stats.MaterialCount}");
        Console.WriteLine($"  Scenes: {stats.SceneCount}");
        Console.WriteLine($"  Scripts: {stats.ScriptCount}");
    }

    private bool LoadAssetEntry(string assetPath)
    {
        try
        {
            var asset = BlueAsset.Load(assetPath);
            if (asset == null) return false;

            var entry = new AssetEntry
            {
                Asset = asset,
                FilePath = assetPath,
                LastScanned = DateTime.Now
            };

            _assetsByGuid[asset.AssetId] = entry;
            _assetsByPath[assetPath] = asset.AssetId;

            return true;
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError($"Failed to load asset: {assetPath}", ex, "AssetDatabase");
            return false;
        }
    }

    private void OnAssetChanged(object sender, FileSystemEventArgs e)
    {
        Thread.Sleep(100); // Debounce
        LoadAssetEntry(e.FullPath);
        ErrorHandler.LogInfo($"Asset changed: {Path.GetFileName(e.FullPath)}", "AssetDatabase");
    }

    private void OnAssetCreated(object sender, FileSystemEventArgs e)
    {
        Thread.Sleep(100); // Debounce
        LoadAssetEntry(e.FullPath);
        ErrorHandler.LogInfo($"Asset created: {Path.GetFileName(e.FullPath)}", "AssetDatabase");
    }

    private void OnAssetDeleted(object sender, FileSystemEventArgs e)
    {
        if (_assetsByPath.TryRemove(e.FullPath, out var guid))
        {
            _assetsByGuid.TryRemove(guid, out _);
            ErrorHandler.LogInfo($"Asset deleted: {Path.GetFileName(e.FullPath)}", "AssetDatabase");
        }
    }

    private void OnAssetRenamed(object sender, RenamedEventArgs e)
    {
        if (_assetsByPath.TryRemove(e.OldFullPath, out var guid))
        {
            _assetsByPath[e.FullPath] = guid;
            if (_assetsByGuid.TryGetValue(guid, out var entry))
            {
                entry.FilePath = e.FullPath;
            }
            ErrorHandler.LogInfo($"Asset renamed: {Path.GetFileName(e.OldFullPath)} → {Path.GetFileName(e.FullPath)}", "AssetDatabase");
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}

public class AssetEntry
{
    public BlueAsset Asset { get; set; } = new();
    public string FilePath { get; set; } = "";
    public DateTime LastScanned { get; set; }
}

public class AssetDatabaseStats
{
    public int TotalAssets { get; set; }
    public int MeshCount { get; set; }
    public int TextureCount { get; set; }
    public int MaterialCount { get; set; }
    public int SceneCount { get; set; }
    public int ScriptCount { get; set; }
}
