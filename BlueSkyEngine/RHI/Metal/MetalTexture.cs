using static BlueSky.BSR.RHI.Metal.MetalInterop;

namespace BlueSky.BSR.RHI.Metal;

internal class MetalTexture : IRHITexture
{
    private IntPtr _texture;
    private bool _disposed;
    
    public uint Width { get; }
    public uint Height { get; }
    public uint MipLevels { get; }
    public TextureFormat Format { get; }
    public TextureUsage Usage { get; }
    
    internal IntPtr Handle => _texture;
    
    public MetalTexture(MetalDevice device, TextureDesc desc)
    {
        Width = desc.Width;
        Height = desc.Height;
        MipLevels = Math.Max(1u, desc.MipLevels);
        Format = desc.Format;
        Usage = desc.Usage;
        
        // Create texture descriptor
        var descriptorClass = GetClass("MTLTextureDescriptor");
        var allocSel = GetSelector("alloc");
        var initSel = GetSelector("init");
        var descriptor = objc_msgSend(descriptorClass, allocSel);
        descriptor = objc_msgSend(descriptor, initSel);
        
        // Set properties correctly without invalid chaining
        var setWidthSel = GetSelector("setWidth:");
        objc_msgSend_void_ulong(descriptor, setWidthSel, desc.Width);
        
        var setHeightSel = GetSelector("setHeight:");
        objc_msgSend_void_ulong(descriptor, setHeightSel, desc.Height);
        
        var setPixelFormatSel = GetSelector("setPixelFormat:");
        objc_msgSend_void_ulong(descriptor, setPixelFormatSel, ToMTLPixelFormat(desc.Format));
        
        // Map Usage to MTLTextureUsage and StorageMode
        ulong usageFlags = MTLTextureUsageUnknown;
        ulong storageMode = MTLResourceStorageModeShared;
        
        bool isDepthFormat = desc.Format == TextureFormat.Depth32Float || desc.Format == TextureFormat.Depth24Stencil8;
        if (isDepthFormat)
        {
            // Depth textures can also be sampled (soft particles, shadowing).
            // Keep them private; blit encoders can copy between private depth
            // targets without exposing attachment memory to the CPU.
            if (desc.Usage.HasFlag(TextureUsage.DepthStencil))
                usageFlags |= MTLTextureUsageRenderTarget;
            if (desc.Usage.HasFlag(TextureUsage.Sampled))
                usageFlags |= MTLTextureUsageShaderRead;
            storageMode = MTLResourceStorageModePrivate;
        }
        else if (desc.Usage.HasFlag(TextureUsage.RenderTarget))
        {
            usageFlags |= MTLTextureUsageRenderTarget;
            if (desc.Usage.HasFlag(TextureUsage.Sampled)) usageFlags |= MTLTextureUsageShaderRead;
            storageMode = MTLResourceStorageModePrivate;
        }
        else if (desc.Usage.HasFlag(TextureUsage.Sampled))
        {
            usageFlags |= MTLTextureUsageShaderRead;
            storageMode = MTLResourceStorageModeShared; // Need CPU access to upload
        }
        else if (desc.Usage.HasFlag(TextureUsage.Storage))
        {
            usageFlags |= MTLTextureUsageShaderWrite;
            storageMode = MTLResourceStorageModePrivate;
        }
        
        var setUsageSel = GetSelector("setUsage:");
        objc_msgSend_void_ulong(descriptor, setUsageSel, usageFlags);
        
        var setStorageModeSel = GetSelector("setStorageMode:");
        objc_msgSend_void_ulong(descriptor, setStorageModeSel, storageMode);

        // M5: mip chains + TBDR memoryless attachments.
        if (MipLevels > 1)
        {
            if (isDepthFormat)
                throw new ArgumentException("Mipmapped depth textures are not supported on Metal.");
            var setMipLevelsSel = GetSelector("setMipmapLevelCount:");
            objc_msgSend_void_ulong(descriptor, setMipLevelsSel, MipLevels);
        }
        if (desc.Memoryless)
        {
            if (desc.Usage.HasFlag(TextureUsage.Sampled) || desc.Usage.HasFlag(TextureUsage.Storage) ||
                desc.Usage.HasFlag(TextureUsage.TransferSrc) || desc.Usage.HasFlag(TextureUsage.TransferDst))
                throw new ArgumentException("Memoryless textures cannot be Sampled/Storage/Transfer (tile memory only).");
            if (MipLevels > 1)
                throw new ArgumentException("Memoryless textures cannot have mipmaps.");
            // MTLStorageModeMemoryless = 3
            objc_msgSend_void_ulong(descriptor, setStorageModeSel, 3);
        }
        
        // Create texture
        var newTextureSel = GetSelector("newTextureWithDescriptor:");
        _texture = objc_msgSend_ptr(device.Device, newTextureSel, descriptor);
        
        Release(descriptor);
        
        if (_texture == IntPtr.Zero)
            throw new Exception("Failed to create Metal texture");
    }
    
    // Constructor for wrapping existing Metal texture (e.g., from swapchain)
    internal MetalTexture(IntPtr texture, uint width, uint height, TextureFormat format, TextureUsage usage)
    {
        _texture = Retain(texture);
        Width = width;
        Height = height;
        MipLevels = 1;
        Format = format;
        Usage = usage;
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        if (_texture != IntPtr.Zero)
        {
            Release(_texture);
            _texture = IntPtr.Zero;
        }
        
        _disposed = true;
    }
}
