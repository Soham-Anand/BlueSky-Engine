// BlueSkyEngine - Texture Streaming System
// Async texture loading with mip streaming and VRAM budget management

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlueSky.Core.Diagnostics;
using NotBSRenderer;

namespace BlueSky.Rendering.Textures;

/// <summary>
/// Texture streaming manager - loads textures asynchronously with mip streaming.
/// Manages VRAM budget and automatically evicts textures when over budget.
/// Thread-safe singleton.
/// </summary>
public sealed class TextureStreaming : IDisposable
{
    private static readonly Lazy<TextureStreaming> _instance = new(() => new TextureStreaming());
    public static TextureStreaming Instance => _instance.Value;
    
    private readonly TextureCache _cache;
    private readonly TextureLoadQueue _loadQueue;
    private readonly TextureBudget _budget;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task[] _workerTasks;
    private bool _disposed;
    private IRHITexture? _fallbackTexture;
    private readonly object _fallbackLock = new();
    
    // Configuration
    public int WorkerThreadCount { get; set; } = 4;
    public long VRAMBudgetBytes { get; set; } = 2L * 1024 * 1024 * 1024; // 2 GB default
    
    // Statistics
    public int LoadedTextureCount => _cache.Count;
    public long CurrentVRAMUsage => _cache.TotalMemoryUsage;
    public int PendingLoadCount => _loadQueue.Count;
    
    private TextureStreaming()
    {
        _cache = new TextureCache();
        _loadQueue = new TextureLoadQueue();
        _budget = new TextureBudget(VRAMBudgetBytes);
        
        // Start worker threads
        _workerTasks = new Task[WorkerThreadCount];
        for (int i = 0; i < WorkerThreadCount; i++)
        {
            int workerId = i;
            _workerTasks[i] = Task.Run(() => WorkerLoop(workerId), _cts.Token);
        }
        
        ErrorHandler.LogInfo($"TextureStreaming initialized with {WorkerThreadCount} workers", "TextureStreaming");
    }
    
    /// <summary>
    /// Load texture asynchronously with priority and mip streaming.
    /// Returns immediately with a handle that can be polled for completion.
    /// </summary>
    public TextureHandle LoadAsync(string path, IRHIDevice device, 
                                   TexturePriority priority = TexturePriority.Medium,
                                   int minMipLevel = 0)
    {
        // Check cache first
        if (_cache.TryGet(path, out var cached))
        {
            cached.LastAccessTime = DateTime.UtcNow;
            cached.Priority = (TexturePriority)Math.Max((int)cached.Priority, (int)priority);
            return new TextureHandle(cached.Texture, true);
        }
        
        // Queue for loading
        var request = new TextureLoadRequest
        {
            Path = path,
            Device = device,
            Priority = priority,
            MinMipLevel = minMipLevel,
            CompletionSource = new TaskCompletionSource<IRHITexture>()
        };
        
        _loadQueue.Enqueue(request);
        
        return new TextureHandle(request.CompletionSource.Task);
    }
    
    /// <summary>
    /// Load texture synchronously (blocks until loaded).
    /// </summary>
    public IRHITexture? LoadSync(string path, IRHIDevice device, 
                                 TexturePriority priority = TexturePriority.High)
    {
        var handle = LoadAsync(path, device, priority);
        return handle.WaitForCompletion();
    }
    
    /// <summary>
    /// Unload texture from cache (decrements ref count).
    /// </summary>
    public void Unload(string path)
    {
        _cache.Release(path);
    }
    
    /// <summary>
    /// Force eviction of low-priority textures to free VRAM.
    /// </summary>
    public void EvictLowPriorityTextures(long targetBytes)
    {
        _cache.EvictLowPriority(targetBytes);
    }
    
    /// <summary>
    /// Update streaming system (call once per frame).
    /// </summary>
    public void Update(float deltaTime)
    {
        // Check VRAM budget
        if (CurrentVRAMUsage > VRAMBudgetBytes)
        {
            long excess = CurrentVRAMUsage - VRAMBudgetBytes;
            ErrorHandler.LogWarning($"VRAM over budget by {excess / (1024 * 1024)} MB, evicting textures", "TextureStreaming");
            EvictLowPriorityTextures(excess);
        }
        
        // Update budget stats
        _budget.Update(CurrentVRAMUsage);
    }
    
    /// <summary>
    /// Get streaming statistics.
    /// </summary>
    public StreamingStats GetStats()
    {
        return new StreamingStats
        {
            LoadedTextureCount = LoadedTextureCount,
            CurrentVRAMUsage = CurrentVRAMUsage,
            VRAMBudget = VRAMBudgetBytes,
            PendingLoadCount = PendingLoadCount,
            CacheHitRate = _cache.HitRate
        };
    }
    
    private void WorkerLoop(int workerId)
    {
        ErrorHandler.LogInfo($"Texture worker {workerId} started", "TextureStreaming");
        
        while (!_cts.Token.IsCancellationRequested)
        {
            if (_loadQueue.TryDequeue(out var request))
            {
                try
                {
                    var texture = LoadTextureInternal(request);
                    
                    if (texture != null)
                    {
                        // Add to cache
                        _cache.Add(request.Path, texture, request.Priority);
                        request.CompletionSource.SetResult(texture);
                    }
                    else
                    {
                        request.CompletionSource.SetException(new FileNotFoundException($"Failed to load texture: {request.Path}"));
                    }
                }
                catch (Exception ex)
                {
                    ErrorHandler.LogError($"Worker {workerId} failed to load {request.Path}", ex, "TextureStreaming");
                    request.CompletionSource.SetException(ex);
                }
            }
            else
            {
                // No work, sleep briefly
                Thread.Sleep(10);
            }
        }
        
        ErrorHandler.LogInfo($"Texture worker {workerId} stopped", "TextureStreaming");
    }
    
    private IRHITexture? LoadTextureInternal(TextureLoadRequest request)
    {
        ErrorHandler.LogInfo($"Loading texture: {request.Path} (priority: {request.Priority})", "TextureStreaming");
        
        string ext = Path.GetExtension(request.Path).ToLower();
        
        // Load based on file extension
        IRHITexture? texture = ext switch
        {
            ".dds" => LoadDDS(request),
            ".ktx2" => LoadKTX2(request),
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" => LoadSTB(request),
            _ => null
        };
        
        if (texture == null)
        {
            ErrorHandler.LogError($"Failed to load texture: {request.Path}, using fallback.", null, "TextureStreaming");
            texture = GetOrCreateFallbackTexture(request.Device);
        }
        
        return texture;
    }
    
    private IRHITexture GetOrCreateFallbackTexture(IRHIDevice device)
    {
        lock (_fallbackLock)
        {
            if (_fallbackTexture != null) return _fallbackTexture;
            
            var desc = new TextureDesc
            {
                Width = 8,
                Height = 8,
                Depth = 1,
                MipLevels = 1,
                Format = NotBSRenderer.TextureFormat.RGBA8Unorm,
                Usage = NotBSRenderer.TextureUsage.Sampled | NotBSRenderer.TextureUsage.TransferDst
            };
            
            _fallbackTexture = device.CreateTexture(desc);
            
            byte[] data = new byte[8 * 8 * 4];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    // Checkerboard pattern (black and grey, similar to Unreal)
                    bool isLight = ((x / 4) + (y / 4)) % 2 == 0;
                    byte color = isLight ? (byte)128 : (byte)0; // 128 = grey, 0 = black
                    
                    int index = (y * 8 + x) * 4;
                    data[index + 0] = color;     // R
                    data[index + 1] = color;     // G
                    data[index + 2] = color;     // B
                    data[index + 3] = 255;       // A
                }
            }
            
            device.UploadTexture(_fallbackTexture, data, 0);
            return _fallbackTexture;
        }
    }
    
    private IRHITexture? LoadDDS(TextureLoadRequest request)
    {
        var dds = DDSLoader.Load(request.Path);
        if (dds == null) return null;
        
        return dds.UploadToGPU(request.Device);
    }
    
    private IRHITexture? LoadKTX2(TextureLoadRequest request)
    {
        var ktx2 = KTX2Loader.Load(request.Path);
        if (ktx2 == null) return null;
        
        return ktx2.UploadToGPU(request.Device);
    }
    
    private IRHITexture? LoadSTB(TextureLoadRequest request)
    {
        // TODO: Use stb_image for PNG/JPG/etc
        ErrorHandler.LogWarning($"STB image loader not yet implemented: {request.Path}", "TextureStreaming");
        return null;
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        _cts.Cancel();
        Task.WaitAll(_workerTasks, TimeSpan.FromSeconds(5));
        _cache.Clear();
        _cts.Dispose();
        
        lock (_fallbackLock)
        {
            _fallbackTexture?.Dispose();
            _fallbackTexture = null;
        }
        
        _disposed = true;
        
        ErrorHandler.LogInfo("TextureStreaming disposed", "TextureStreaming");
    }
}

/// <summary>
/// Texture load priority.
/// </summary>
public enum TexturePriority
{
    Low = 0,      // Background assets, far LODs
    Medium = 1,   // Normal assets
    High = 2,     // Near camera, important assets
    Critical = 3  // UI, player character
}

/// <summary>
/// Texture load request.
/// </summary>
internal class TextureLoadRequest
{
    public string Path = "";
    public IRHIDevice Device = null!;
    public TexturePriority Priority;
    public int MinMipLevel;
    public TaskCompletionSource<IRHITexture> CompletionSource = null!;
}

/// <summary>
/// Texture handle - represents an async texture load operation.
/// </summary>
public readonly struct TextureHandle
{
    private readonly IRHITexture? _immediateTexture;
    private readonly Task<IRHITexture>? _asyncTask;
    private readonly bool _isImmediate;
    
    internal TextureHandle(IRHITexture texture, bool immediate)
    {
        _immediateTexture = texture;
        _asyncTask = null;
        _isImmediate = immediate;
    }
    
    internal TextureHandle(Task<IRHITexture> task)
    {
        _immediateTexture = null;
        _asyncTask = task;
        _isImmediate = false;
    }
    
    public bool IsReady => _isImmediate || (_asyncTask?.IsCompleted ?? false);
    public bool IsValid => _immediateTexture != null || _asyncTask != null;
    
    public IRHITexture? GetTexture()
    {
        if (_isImmediate) return _immediateTexture;
        if (_asyncTask?.IsCompleted == true) return _asyncTask.Result;
        return null;
    }
    
    public IRHITexture? WaitForCompletion(int timeoutMs = -1)
    {
        if (_isImmediate) return _immediateTexture;
        if (_asyncTask == null) return null;
        
        if (timeoutMs > 0)
            _asyncTask.Wait(timeoutMs);
        else
            _asyncTask.Wait();
        
        return _asyncTask.IsCompleted ? _asyncTask.Result : null;
    }
}

/// <summary>
/// Streaming statistics.
/// </summary>
public struct StreamingStats
{
    public int LoadedTextureCount;
    public long CurrentVRAMUsage;
    public long VRAMBudget;
    public int PendingLoadCount;
    public float CacheHitRate;
    
    public float VRAMUsagePercent => VRAMBudget > 0 ? (float)CurrentVRAMUsage / VRAMBudget * 100f : 0f;
}
