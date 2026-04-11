using System.Collections.Concurrent;

namespace BlueSky.Core.Assets;

/// <summary>
/// Central registry for all loaded assets. Thread-safe and handles caching.
/// </summary>
public class AssetRegistry
{
    private readonly ConcurrentDictionary<string, AssetHandle> _pathToHandle = new();
    private readonly ConcurrentDictionary<AssetHandle, AssetMetadata> _handleToMetadata = new();
    private readonly ConcurrentDictionary<AssetHandle, object> _handleToAsset = new();
    private uint _nextId = 1;

    public AssetHandle Register(string path, AssetType type, object asset)
    {
        // Check if already loaded
        if (_pathToHandle.TryGetValue(path, out var existingHandle))
        {
            return existingHandle;
        }

        var handle = new AssetHandle(_nextId++);
        var metadata = new AssetMetadata
        {
            Handle = handle,
            Path = path,
            Type = type,
            LoadedAt = DateTime.UtcNow
        };

        _pathToHandle[path] = handle;
        _handleToMetadata[handle] = metadata;
        _handleToAsset[handle] = asset;

        return handle;
    }

    public T? Get<T>(AssetHandle handle) where T : class
    {
        if (_handleToAsset.TryGetValue(handle, out var asset))
        {
            return asset as T;
        }
        return null;
    }

    public bool TryGet<T>(AssetHandle handle, out T? asset) where T : class
    {
        asset = Get<T>(handle);
        return asset != null;
    }

    public AssetMetadata? GetMetadata(AssetHandle handle)
    {
        _handleToMetadata.TryGetValue(handle, out var metadata);
        return metadata;
    }

    public bool IsLoaded(string path) => _pathToHandle.ContainsKey(path);

    public AssetHandle? GetHandle(string path)
    {
        if (_pathToHandle.TryGetValue(path, out var handle))
        {
            return handle;
        }
        return null;
    }

    public void Unload(AssetHandle handle)
    {
        if (_handleToMetadata.TryGetValue(handle, out var metadata))
        {
            _pathToHandle.TryRemove(metadata.Path, out _);
            _handleToMetadata.TryRemove(handle, out _);
            
            if (_handleToAsset.TryRemove(handle, out var asset) && asset is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    public void Clear()
    {
        foreach (var handle in _handleToAsset.Keys)
        {
            Unload(handle);
        }
    }
}

public struct AssetMetadata
{
    public AssetHandle Handle;
    public string Path;
    public AssetType Type;
    public DateTime LoadedAt;
}
