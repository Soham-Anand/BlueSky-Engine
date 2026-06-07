// BlueSkyEngine - Texture Load Queue
// Priority queue for async texture loading

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace BlueSky.Rendering.Textures;

/// <summary>
/// Priority queue for texture load requests.
/// Thread-safe.
/// </summary>
internal class TextureLoadQueue
{
    private readonly ConcurrentDictionary<TexturePriority, ConcurrentQueue<TextureLoadRequest>> _queues = new();
    
    public TextureLoadQueue()
    {
        // Initialize queues for each priority level
        _queues[TexturePriority.Critical] = new ConcurrentQueue<TextureLoadRequest>();
        _queues[TexturePriority.High] = new ConcurrentQueue<TextureLoadRequest>();
        _queues[TexturePriority.Medium] = new ConcurrentQueue<TextureLoadRequest>();
        _queues[TexturePriority.Low] = new ConcurrentQueue<TextureLoadRequest>();
    }
    
    public int Count => _queues.Values.Sum(q => q.Count);
    
    /// <summary>
    /// Enqueue a load request with priority.
    /// </summary>
    public void Enqueue(TextureLoadRequest request)
    {
        _queues[request.Priority].Enqueue(request);
    }
    
    /// <summary>
    /// Dequeue highest priority request.
    /// </summary>
    public bool TryDequeue(out TextureLoadRequest? request)
    {
        request = null;
        
        // Try critical first, then high, medium, low
        if (_queues[TexturePriority.Critical].TryDequeue(out request)) return true;
        if (_queues[TexturePriority.High].TryDequeue(out request)) return true;
        if (_queues[TexturePriority.Medium].TryDequeue(out request)) return true;
        if (_queues[TexturePriority.Low].TryDequeue(out request)) return true;
        
        return false;
    }
}
