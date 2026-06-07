using System;
using System.Collections.Generic;

namespace NotBSRenderer;

/// <summary>
/// Manages bindless resource handles for modern rendering APIs
/// Provides automatic fallback to slot-based binding for DX11 Feature Level 10.x/11.0
/// Inspired by Frostbite's resource binding system
/// </summary>
public class BindlessResourceManager : IDisposable
{
    private readonly IRHIDevice _device;
    private readonly bool _bindlessSupported;
    
    // Resource tracking
    private readonly Dictionary<IRHITexture, BindlessResourceHandle> _textureHandles = new();
    private readonly Dictionary<IRHIBuffer, BindlessResourceHandle> _bufferHandles = new();
    private readonly Dictionary<uint, object> _handleToResource = new();
    
    // Free list for handle reuse
    private readonly Queue<uint> _freeTextureIndices = new();
    private readonly Queue<uint> _freeBufferIndices = new();
    
    private uint _nextTextureIndex = 0;
    private uint _nextBufferIndex = 0;
    private uint _generation = 0;
    
    // Slot-based fallback tracking (for DX11 without bindless)
    private readonly Dictionary<IRHITexture, uint> _textureSlots = new();
    private readonly Dictionary<IRHIBuffer, uint> _bufferSlots = new();
    private uint _nextTextureSlot = 0;
    private uint _nextBufferSlot = 0;
    
    public bool IsBindlessSupported => _bindlessSupported;
    public int RegisteredTextureCount => _textureHandles.Count;
    public int RegisteredBufferCount => _bufferHandles.Count;
    
    public BindlessResourceManager(IRHIDevice device)
    {
        _device = device;
        _bindlessSupported = device.Capabilities.HasFlag(RHICapabilities.BindlessResources);
        
        Console.WriteLine($"[BindlessResourceManager] Initialized. Bindless support: {_bindlessSupported}");
    }
    
    /// <summary>
    /// Register a texture for bindless access
    /// Returns a handle that can be used in shaders
    /// </summary>
    public BindlessResourceHandle RegisterTexture(IRHITexture texture)
    {
        if (texture == null)
            throw new ArgumentNullException(nameof(texture));
        
        // Check if already registered
        if (_textureHandles.TryGetValue(texture, out var existingHandle))
            return existingHandle;
        
        if (_bindlessSupported)
        {
            // Bindless path
            var handle = _device.RegisterBindlessTexture(texture);
            _textureHandles[texture] = handle;
            _handleToResource[handle.Index] = texture;
            return handle;
        }
        else
        {
            // Fallback: assign a slot
            uint slot = _nextTextureSlot++;
            _textureSlots[texture] = slot;
            
            var handle = new BindlessResourceHandle
            {
                Index = slot,
                Generation = _generation
            };
            _textureHandles[texture] = handle;
            return handle;
        }
    }
    
    /// <summary>
    /// Register a buffer for bindless access
    /// </summary>
    public BindlessResourceHandle RegisterBuffer(IRHIBuffer buffer)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));
        
        // Check if already registered
        if (_bufferHandles.TryGetValue(buffer, out var existingHandle))
            return existingHandle;
        
        if (_bindlessSupported)
        {
            // Bindless path
            var handle = _device.RegisterBindlessBuffer(buffer);
            _bufferHandles[buffer] = handle;
            _handleToResource[handle.Index] = buffer;
            return handle;
        }
        else
        {
            // Fallback: assign a slot
            uint slot = _nextBufferSlot++;
            _bufferSlots[buffer] = slot;
            
            var handle = new BindlessResourceHandle
            {
                Index = slot,
                Generation = _generation
            };
            _bufferHandles[buffer] = handle;
            return handle;
        }
    }
    
    /// <summary>
    /// Unregister a texture
    /// </summary>
    public void UnregisterTexture(IRHITexture texture)
    {
        if (texture == null || !_textureHandles.TryGetValue(texture, out var handle))
            return;
        
        if (_bindlessSupported)
        {
            _device.UnregisterBindlessResource(handle);
            _handleToResource.Remove(handle.Index);
        }
        else
        {
            _textureSlots.Remove(texture);
        }
        
        _textureHandles.Remove(texture);
        _freeTextureIndices.Enqueue(handle.Index);
    }
    
    /// <summary>
    /// Unregister a buffer
    /// </summary>
    public void UnregisterBuffer(IRHIBuffer buffer)
    {
        if (buffer == null || !_bufferHandles.TryGetValue(buffer, out var handle))
            return;
        
        if (_bindlessSupported)
        {
            _device.UnregisterBindlessResource(handle);
            _handleToResource.Remove(handle.Index);
        }
        else
        {
            _bufferSlots.Remove(buffer);
        }
        
        _bufferHandles.Remove(buffer);
        _freeBufferIndices.Enqueue(handle.Index);
    }
    
    /// <summary>
    /// Get the handle for a registered texture
    /// </summary>
    public BindlessResourceHandle GetTextureHandle(IRHITexture texture)
    {
        if (_textureHandles.TryGetValue(texture, out var handle))
            return handle;
        
        return BindlessResourceHandle.Invalid;
    }
    
    /// <summary>
    /// Get the handle for a registered buffer
    /// </summary>
    public BindlessResourceHandle GetBufferHandle(IRHIBuffer buffer)
    {
        if (_bufferHandles.TryGetValue(buffer, out var handle))
            return handle;
        
        return BindlessResourceHandle.Invalid;
    }
    
    /// <summary>
    /// Bind resources using the appropriate method (bindless or slot-based)
    /// </summary>
    public void BindTexture(IRHICommandBuffer cmd, IRHITexture texture, uint binding, uint set = 0)
    {
        if (_bindlessSupported)
        {
            // Bindless: resources are already accessible via handles
            // No need to bind explicitly
        }
        else
        {
            // Slot-based: bind to specific slot
            cmd.SetTexture(texture, binding, set);
        }
    }
    
    /// <summary>
    /// Bind a buffer using the appropriate method
    /// </summary>
    public void BindBuffer(IRHICommandBuffer cmd, IRHIBuffer buffer, uint binding, uint set = 0)
    {
        if (_bindlessSupported)
        {
            // Bindless: resources are already accessible via handles
        }
        else
        {
            // Slot-based: bind to specific slot
            cmd.SetStorageBuffer(buffer, binding, set);
        }
    }
    
    /// <summary>
    /// Create a descriptor table for bindless rendering
    /// </summary>
    public void SetDescriptorTable(IRHICommandBuffer cmd, uint set, ReadOnlySpan<BindlessResourceHandle> handles)
    {
        if (_bindlessSupported)
        {
            cmd.SetBindlessResourceTable(set, handles);
        }
        else
        {
            // Fallback: bind resources to slots sequentially
            for (int i = 0; i < handles.Length; i++)
            {
                var handle = handles[i];
                if (_handleToResource.TryGetValue(handle.Index, out var resource))
                {
                    if (resource is IRHITexture texture)
                    {
                        cmd.SetTexture(texture, (uint)i, set);
                    }
                    else if (resource is IRHIBuffer buffer)
                    {
                        cmd.SetStorageBuffer(buffer, (uint)i, set);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Get statistics about resource usage
    /// </summary>
    public ResourceStats GetStats()
    {
        return new ResourceStats
        {
            RegisteredTextures = _textureHandles.Count,
            RegisteredBuffers = _bufferHandles.Count,
            FreeTextureSlots = _freeTextureIndices.Count,
            FreeBufferSlots = _freeBufferIndices.Count,
            IsBindless = _bindlessSupported
        };
    }
    
    public void Dispose()
    {
        // Unregister all resources
        if (_bindlessSupported)
        {
            foreach (var handle in _textureHandles.Values)
            {
                _device.UnregisterBindlessResource(handle);
            }
            
            foreach (var handle in _bufferHandles.Values)
            {
                _device.UnregisterBindlessResource(handle);
            }
        }
        
        _textureHandles.Clear();
        _bufferHandles.Clear();
        _handleToResource.Clear();
        _textureSlots.Clear();
        _bufferSlots.Clear();
        _freeTextureIndices.Clear();
        _freeBufferIndices.Clear();
    }
}

public struct ResourceStats
{
    public int RegisteredTextures;
    public int RegisteredBuffers;
    public int FreeTextureSlots;
    public int FreeBufferSlots;
    public bool IsBindless;
    
    public override string ToString()
    {
        return $"Textures: {RegisteredTextures}, Buffers: {RegisteredBuffers}, " +
               $"Mode: {(IsBindless ? "Bindless" : "Slot-based")}";
    }
}
