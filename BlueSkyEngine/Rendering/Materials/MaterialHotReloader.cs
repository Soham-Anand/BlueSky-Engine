using System;
using System.IO;
using BlueSky.Core.Diagnostics;
using BlueSky.Rendering.Textures;

namespace BlueSky.Rendering.Materials;

/// <summary>
/// Monitors material and texture files on disk and automatically reloads them.
/// </summary>
public class MaterialHotReloader : IDisposable
{
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public MaterialHotReloader(string watchPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(watchPath);
            if (!Directory.Exists(fullPath))
            {
                ErrorHandler.LogWarning($"Hot Reloader path does not exist: {fullPath}", "MaterialHotReloader");
                return;
            }
                
            _watcher = new FileSystemWatcher(fullPath);
            _watcher.IncludeSubdirectories = true;
            _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime;
            _watcher.Filter = "*.*";
            
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            
            _watcher.EnableRaisingEvents = true;
            
            ErrorHandler.LogInfo($"Material Hot Reloader started on {fullPath}", "MaterialHotReloader");
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError("Failed to initialize Material Hot Reloader", ex, "MaterialHotReloader");
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            string ext = Path.GetExtension(e.FullPath).ToLower();
            
            if (ext == ".blueskyasset" || ext == ".dds" || ext == ".ktx2" || ext == ".png" || ext == ".jpg")
            {
                ErrorHandler.LogInfo($"Hot reloading asset: {e.FullPath}", "MaterialHotReloader");
                
                // Unload texture from cache so the next access reloads it from disk
                if (ext != ".blueskyasset")
                {
                    TextureStreaming.Instance.Unload(e.FullPath);
                }
                
                // Note: For .blueskyasset, UltraRenderer/MaterialBatching will reload it when queried next
                // or we could explicitly notify the renderer.
            }
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError($"Error hot reloading {e.FullPath}", ex, "MaterialHotReloader");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileChanged;
            _watcher.Created -= OnFileChanged;
            _watcher.Dispose();
        }
        
        _disposed = true;
    }
}
