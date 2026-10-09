using System.Collections.Concurrent;
using System.Numerics;
using BlueSky.Core.Assets;

namespace BlueSky.Editor.Services;

/// <summary>
/// Content browser + drag/drop + context menu state.
/// Directory enumeration and asset-header reads run outside the UI thread.
/// </summary>
public sealed class AssetService : IDisposable
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly object _browserLock = new();
    private readonly ConcurrentDictionary<string, BlueAsset> _headerCache = new(PathComparer);
    private readonly ConcurrentDictionary<string, byte> _headerLoads = new(PathComparer);
    private readonly ConcurrentDictionary<string, byte> _checkedHeaders = new(PathComparer);

    private string _indexedBrowserDirectory = "";
    private ContentBrowserListing? _browserListing;
    private Task<ContentBrowserListing>? _browserScanTask;
    private CancellationTokenSource? _browserScanCancellation;
    private FileSystemWatcher? _browserWatcher;
    private long _browserGeneration;
    private long _headerEpoch;
    private bool _browserDirty = true;
    private DateTime _scanNotBeforeUtc;
    private bool _disposed;

    public int SelectedSourceIndex { get; set; } = 0;
    public int SelectedAssetIndex { get; set; } = -1;

    public string CurrentBrowserDir { get; set; } = "";

    public string? DraggedAssetPath { get; set; }
    public Vector2 DragStartMouse { get; set; }
    public bool IsDraggingAsset { get; set; }
    public uint DoubleClickTarget { get; set; }
    public double LastClickTime { get; set; }

    public bool ShowContextMenu { get; set; }
    public float ContextMenuX { get; set; }
    public float ContextMenuY { get; set; }
    public string ContextMenuPath { get; set; } = "";

    /// <summary>
    /// Returns the latest cached listing immediately. A null result means the
    /// first scan is still running; the scan itself never blocks the caller.
    /// </summary>
    public ContentBrowserListing? GetContentBrowserListing(string directory)
    {
        if (_disposed || string.IsNullOrWhiteSpace(directory))
            return null;

        string fullPath;
        try { fullPath = Path.GetFullPath(directory); }
        catch { return null; }

        lock (_browserLock)
        {
            if (_disposed)
                return null;

            if (!PathComparer.Equals(fullPath, _indexedBrowserDirectory))
                SelectBrowserDirectory(fullPath);

            CompleteBrowserScanIfReady();

            if (_browserScanTask == null && _browserDirty && DateTime.UtcNow >= _scanNotBeforeUtc)
                StartBrowserScan();

            return _browserListing;
        }
    }

    /// <summary>Marks the current folder stale and starts a fresh scan next frame.</summary>
    public void RefreshContentBrowserListing()
    {
        lock (_browserLock)
        {
            if (_disposed)
                return;

            InvalidateHeaders();
            _browserDirty = true;
            _scanNotBeforeUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Returns a cached asset header, scheduling a background read on a cache
    /// miss. Repeated UI frames never reopen the same .blueskyasset file.
    /// </summary>
    public BlueAsset? GetAssetHeader(string path)
    {
        if (_disposed || string.IsNullOrWhiteSpace(path))
            return null;

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch { return null; }

        if (_headerCache.TryGetValue(fullPath, out var header))
            return header;

        if (_checkedHeaders.ContainsKey(fullPath) || !_headerLoads.TryAdd(fullPath, 0))
            return null;

        long epoch = Interlocked.Read(ref _headerEpoch);
        _ = Task.Run(() =>
        {
            BlueAsset? loaded = null;
            try { loaded = BlueAsset.LoadHeader(fullPath); }
            catch { /* Invalid or concurrently removed assets are ignored. */ }
            finally
            {
                if (epoch == Interlocked.Read(ref _headerEpoch))
                {
                    if (loaded != null)
                        _headerCache[fullPath] = loaded;

                    _checkedHeaders[fullPath] = 0;
                }
                _headerLoads.TryRemove(fullPath, out _);
            }
        });

        return null;
    }

    private void SelectBrowserDirectory(string fullPath)
    {
        _browserGeneration++;
        CancelBrowserScan();
        _browserScanCancellation = null;
        _browserScanTask = null;
        _browserListing = null;
        _indexedBrowserDirectory = fullPath;
        _browserDirty = true;
        _scanNotBeforeUtc = DateTime.UtcNow;
        InvalidateHeaders();
        DisposeWatcher();
    }

    private void StartBrowserScan()
    {
        string directory = _indexedBrowserDirectory;
        long generation = _browserGeneration;
        var cancellation = new CancellationTokenSource();
        _browserScanCancellation = cancellation;
        _browserDirty = false;
        _browserScanTask = Task.Run(() => ScanBrowserDirectory(directory, generation, cancellation.Token), cancellation.Token);
        _ = _browserScanTask.ContinueWith(
            _ => cancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void CompleteBrowserScanIfReady()
    {
        if (_browserScanTask is not { IsCompleted: true } completed)
            return;

        _browserScanTask = null;
        _browserScanCancellation = null;

        if (completed.Status == TaskStatus.RanToCompletion && completed.Result.Generation == _browserGeneration)
        {
            _browserListing = completed.Result;
            if (_browserListing.Error == null)
                StartWatcher(_indexedBrowserDirectory);
        }
        else if (completed.IsFaulted && !_disposed)
        {
            string error = completed.Exception?.GetBaseException().Message ?? "Could not read this folder.";
            _browserListing = new ContentBrowserListing(_indexedBrowserDirectory, Array.Empty<string>(), Array.Empty<string>(), error, _browserGeneration);
        }

        if (_browserDirty)
            _scanNotBeforeUtc = DateTime.UtcNow.AddMilliseconds(150);
    }

    private static ContentBrowserListing ScanBrowserDirectory(string directory, long generation, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] directories = Directory.GetDirectories(directory);
            cancellationToken.ThrowIfCancellationRequested();
            string[] files = Directory.GetFiles(directory);
            cancellationToken.ThrowIfCancellationRequested();
            return new ContentBrowserListing(directory, directories, files, null, generation);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ContentBrowserListing(directory, Array.Empty<string>(), Array.Empty<string>(), ex.Message, generation);
        }
    }

    private void StartWatcher(string directory)
    {
        DisposeWatcher();
        try
        {
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Changed += OnBrowserChanged;
            watcher.Created += OnBrowserChanged;
            watcher.Deleted += OnBrowserChanged;
            watcher.Renamed += OnBrowserRenamed;
            watcher.Error += OnBrowserWatcherError;
            _browserWatcher = watcher;
            watcher.EnableRaisingEvents = true;
        }
        catch
        {
            DisposeWatcher();
        }
    }

    private void OnBrowserChanged(object sender, FileSystemEventArgs e)
    {
        if (!ReferenceEquals(sender, _browserWatcher))
            return;

        lock (_browserLock)
        {
            if (_disposed || !ReferenceEquals(sender, _browserWatcher))
                return;

            InvalidateHeader(e.FullPath);
            MarkBrowserDirty();
        }
    }

    private void OnBrowserRenamed(object sender, RenamedEventArgs e)
    {
        if (!ReferenceEquals(sender, _browserWatcher))
            return;

        lock (_browserLock)
        {
            if (_disposed || !ReferenceEquals(sender, _browserWatcher))
                return;

            InvalidateHeader(e.OldFullPath);
            InvalidateHeader(e.FullPath);
            MarkBrowserDirty();
        }
    }

    private void OnBrowserWatcherError(object sender, ErrorEventArgs e)
    {
        lock (_browserLock)
        {
            if (_disposed || !ReferenceEquals(sender, _browserWatcher))
                return;

            InvalidateHeaders();
            MarkBrowserDirty();
        }
    }

    private void MarkBrowserDirty()
    {
        _browserDirty = true;
        _scanNotBeforeUtc = DateTime.UtcNow.AddMilliseconds(150);
    }

    private void InvalidateHeader(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            _headerCache.TryRemove(fullPath, out _);
            _checkedHeaders.TryRemove(fullPath, out _);
            Interlocked.Increment(ref _headerEpoch);
        }
        catch { }
    }

    private void InvalidateHeaders()
    {
        _headerCache.Clear();
        _checkedHeaders.Clear();
        Interlocked.Increment(ref _headerEpoch);
    }

    private void DisposeWatcher()
    {
        if (_browserWatcher == null)
            return;

        _browserWatcher.EnableRaisingEvents = false;
        _browserWatcher.Dispose();
        _browserWatcher = null;
    }

    private void CancelBrowserScan()
    {
        try { _browserScanCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        lock (_browserLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            CancelBrowserScan();
            _browserScanCancellation = null;
            _browserScanTask = null;
            DisposeWatcher();
        }
    }
}

/// <summary>A completed directory listing; asset metadata is requested lazily.</summary>
public sealed class ContentBrowserListing
{
    internal ContentBrowserListing(string directoryPath, string[] directories, string[] files, string? error, long generation)
    {
        DirectoryPath = directoryPath;
        Directories = directories;
        Files = files;
        Error = error;
        Generation = generation;
    }

    public string DirectoryPath { get; }
    public string[] Directories { get; }
    public string[] Files { get; }
    public string? Error { get; }
    internal long Generation { get; }
}
