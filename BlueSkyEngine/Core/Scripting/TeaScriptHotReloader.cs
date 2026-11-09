using System;
using System.Collections.Generic;
using System.IO;
using BlueSky.Core.Scripting;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;

namespace BlueSky.Core.Scripting;

/// <summary>
/// Live TeaScript (.tea) hot-reloader for developer iteration.
/// Tracks modification timestamps of active .tea scripts attached to entities in SkyCore
/// and reloads modified scripts on save without restarting Play Mode.
/// </summary>
public sealed class TeaScriptHotReloader : IDisposable
{
    private readonly Dictionary<string, DateTime> _lastWriteTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtyPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public int ReloadCount { get; private set; }
    public string? LastReloadedScript { get; private set; }

    public TeaScriptHotReloader(string? watchDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(watchDirectory) && Directory.Exists(watchDirectory))
        {
            StartWatching(watchDirectory);
        }
    }

    public void StartWatching(string directoryPath)
    {
        StopWatching();
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return;

        try
        {
            _watcher = new FileSystemWatcher(directoryPath, "*.tea")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += (_, e) => OnPathDirty(e.FullPath);
        }
        catch
        {
            // Fallback to timestamp polling in PollAndReload when FileSystemWatcher is restricted
            _watcher = null;
        }
    }

    public void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    /// <summary>
    /// Checks all active <see cref="TeaScriptComponent"/> entities in <paramref name="world"/>
    /// for modified <c>.tea</c> files on disk and hot-reloads any changed scripts.
    /// Returns the number of entities whose scripts were hot-reloaded.
    /// </summary>
    public int PollAndReload(World world, TeaScriptSystem? scriptSystem, Action<string>? log = null)
    {
        if (world == null)
            return 0;

        HashSet<string>? dirtySnapshot = null;
        lock (_lock)
        {
            if (_dirtyPaths.Count > 0)
            {
                dirtySnapshot = new HashSet<string>(_dirtyPaths, StringComparer.OrdinalIgnoreCase);
                _dirtyPaths.Clear();
            }
        }

        var query = world.CreateQuery().All<TeaScriptComponent>().Build();
        int reloadedEntities = 0;

        foreach (var chunk in world.EnumerateQueryChunks(query))
        {
            int scriptIdx = chunk.GetComponentIndex(typeof(TeaScriptComponent));
            var scripts = chunk.GetComponentSpan<TeaScriptComponent>(scriptIdx);
            var entities = chunk.GetEntities();

            for (int i = 0; i < entities.Length; i++)
            {
                ref var script = ref scripts[i];
                string path = script.ScriptAssetId;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                string fullPath = Path.GetFullPath(path);
                DateTime writeTime = File.GetLastWriteTimeUtc(fullPath);

                bool modified = dirtySnapshot != null && dirtySnapshot.Contains(fullPath);
                if (!_lastWriteTimes.TryGetValue(fullPath, out var prevTime))
                {
                    _lastWriteTimes[fullPath] = writeTime;
                }
                else if (writeTime > prevTime)
                {
                    _lastWriteTimes[fullPath] = writeTime;
                    modified = true;
                }

                if (modified)
                {
                    script.IsInitialized = false;
                    ReloadCount++;
                    reloadedEntities++;
                    LastReloadedScript = Path.GetFileName(fullPath);
                    log?.Invoke($"[HotReload] Reloaded '{LastReloadedScript}' on Entity_{entities[i].Id}");
                }
            }
        }

        if (reloadedEntities > 0)
        {
            scriptSystem?.ResetRuntimeInstances();
        }

        return reloadedEntities;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        OnPathDirty(e.FullPath);
    }

    private void OnPathDirty(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return;

        lock (_lock)
        {
            _dirtyPaths.Add(Path.GetFullPath(fullPath));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatching();
    }
}
