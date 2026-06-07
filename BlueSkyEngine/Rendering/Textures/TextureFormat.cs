// BlueSkyEngine - Unified Texture Format System
// Cross-platform texture format definitions with automatic platform mapping

using System;

namespace BlueSky.Rendering.Textures;

/// <summary>
/// Unified texture format enum supporting all major compression formats.
/// Automatically maps to platform-specific formats (Metal, DirectX, Vulkan).
/// </summary>
public enum TextureFormat : uint
{
    // ═══════════════════════════════════════════════════════════════
    //  UNCOMPRESSED FORMATS
    // ═══════════════════════════════════════════════════════════════
    
    // 8-bit per channel
    R8Unorm = 1,
    RG8Unorm = 2,
    RGBA8Unorm = 3,
    RGBA8Srgb = 4,
    BGRA8Unorm = 5,      // Common on Metal/iOS
    BGRA8Srgb = 6,
    
    // 16-bit per channel
    R16Float = 10,
    RG16Float = 11,
    RGBA16Float = 12,
    R16Unorm = 13,
    RG16Unorm = 14,
    RGBA16Unorm = 15,
    
    // 32-bit per channel
    R32Float = 20,
    RG32Float = 21,
    RGBA32Float = 22,
    R32Uint = 23,
    RG32Uint = 24,
    RGBA32Uint = 25,
    
    // Special formats
    RGB10A2Unorm = 30,   // HDR display output
    RG11B10Float = 31,   // HDR without alpha
    RGB9E5Float = 32,    // Shared exponent HDR
    
    // ═══════════════════════════════════════════════════════════════
    //  COMPRESSED FORMATS - BC (DirectX, Desktop)
    // ═══════════════════════════════════════════════════════════════
    
    BC1Unorm = 100,      // DXT1 - RGB, 1-bit alpha (4:1 compression)
    BC1Srgb = 101,
    BC2Unorm = 102,      // DXT3 - RGBA, explicit alpha (4:1)
    BC2Srgb = 103,
    BC3Unorm = 104,      // DXT5 - RGBA, interpolated alpha (4:1)
    BC3Srgb = 105,
    BC4Unorm = 106,      // Single channel (2:1) - grayscale, height maps
    BC4Snorm = 107,
    BC5Unorm = 108,      // Two channel (2:1) - normal maps (RG)
    BC5Snorm = 109,
    BC6HUFloat = 110,    // HDR RGB (6:1)
    BC6HSFloat = 111,
    BC7Unorm = 112,      // Best quality RGBA (4:1)
    BC7Srgb = 113,
    
    // ═══════════════════════════════════════════════════════════════
    //  COMPRESSED FORMATS - ASTC (Metal, Mobile, Vulkan)
    // ═══════════════════════════════════════════════════════════════
    
    ASTC4x4Unorm = 200,  // 8 bpp - highest quality
    ASTC4x4Srgb = 201,
    ASTC5x5Unorm = 202,  // 5.12 bpp
    ASTC5x5Srgb = 203,
    ASTC6x6Unorm = 204,  // 3.56 bpp - balanced
    ASTC6x6Srgb = 205,
    ASTC8x8Unorm = 206,  // 2 bpp - high compression
    ASTC8x8Srgb = 207,
    ASTC10x10Unorm = 208, // 1.28 bpp
    ASTC10x10Srgb = 209,
    ASTC12x12Unorm = 210, // 0.89 bpp - maximum compression
    ASTC12x12Srgb = 211,
    
    // ═══════════════════════════════════════════════════════════════
    //  COMPRESSED FORMATS - ETC2 (Mobile fallback)
    // ═══════════════════════════════════════════════════════════════
    
    ETC2RGB8Unorm = 300,
    ETC2RGB8Srgb = 301,
    ETC2RGBA8Unorm = 302,
    ETC2RGBA8Srgb = 303,
    
    // ═══════════════════════════════════════════════════════════════
    //  DEPTH/STENCIL FORMATS
    // ═══════════════════════════════════════════════════════════════
    
    Depth16Unorm = 400,
    Depth24Plus = 401,
    Depth24PlusStencil8 = 402,
    Depth32Float = 403,
    Depth32FloatStencil8 = 404,
    Stencil8 = 405,
}

/// <summary>
/// Texture format utilities and platform mapping.
/// </summary>
public static class TextureFormatExtensions
{
    /// <summary>
    /// Check if format is compressed.
    /// </summary>
    public static bool IsCompressed(this TextureFormat format)
    {
        return format >= TextureFormat.BC1Unorm && format <= TextureFormat.ETC2RGBA8Srgb;
    }
    
    /// <summary>
    /// Check if format is sRGB.
    /// </summary>
    public static bool IsSrgb(this TextureFormat format)
    {
        return format switch
        {
            TextureFormat.RGBA8Srgb => true,
            TextureFormat.BGRA8Srgb => true,
            TextureFormat.BC1Srgb => true,
            TextureFormat.BC2Srgb => true,
            TextureFormat.BC3Srgb => true,
            TextureFormat.BC7Srgb => true,
            TextureFormat.ASTC4x4Srgb => true,
            TextureFormat.ASTC5x5Srgb => true,
            TextureFormat.ASTC6x6Srgb => true,
            TextureFormat.ASTC8x8Srgb => true,
            TextureFormat.ASTC10x10Srgb => true,
            TextureFormat.ASTC12x12Srgb => true,
            TextureFormat.ETC2RGB8Srgb => true,
            TextureFormat.ETC2RGBA8Srgb => true,
            _ => false
        };
    }
    
    /// <summary>
    /// Check if format is depth/stencil.
    /// </summary>
    public static bool IsDepthStencil(this TextureFormat format)
    {
        return format >= TextureFormat.Depth16Unorm && format <= TextureFormat.Stencil8;
    }
    
    /// <summary>
    /// Get bytes per pixel (uncompressed) or block size (compressed).
    /// </summary>
    public static uint GetBytesPerPixel(this TextureFormat format)
    {
        return format switch
        {
            TextureFormat.R8Unorm => 1,
            TextureFormat.RG8Unorm => 2,
            TextureFormat.RGBA8Unorm => 4,
            TextureFormat.RGBA8Srgb => 4,
            TextureFormat.BGRA8Unorm => 4,
            TextureFormat.BGRA8Srgb => 4,
            TextureFormat.R16Float => 2,
            TextureFormat.RG16Float => 4,
            TextureFormat.RGBA16Float => 8,
            TextureFormat.R32Float => 4,
            TextureFormat.RG32Float => 8,
            TextureFormat.RGBA32Float => 16,
            TextureFormat.RGB10A2Unorm => 4,
            TextureFormat.RG11B10Float => 4,
            
            // Compressed formats return block size (4x4 blocks)
            TextureFormat.BC1Unorm => 8,
            TextureFormat.BC1Srgb => 8,
            TextureFormat.BC2Unorm => 16,
            TextureFormat.BC2Srgb => 16,
            TextureFormat.BC3Unorm => 16,
            TextureFormat.BC3Srgb => 16,
            TextureFormat.BC4Unorm => 8,
            TextureFormat.BC5Unorm => 16,
            TextureFormat.BC6HUFloat => 16,
            TextureFormat.BC7Unorm => 16,
            TextureFormat.BC7Srgb => 16,
            
            // ASTC is variable (4x4 to 12x12 blocks, all 16 bytes)
            TextureFormat.ASTC4x4Unorm => 16,
            TextureFormat.ASTC6x6Unorm => 16,
            TextureFormat.ASTC8x8Unorm => 16,
            
            _ => 4 // Default fallback
        };
    }
    
    /// <summary>
    /// Get block dimensions for compressed formats.
    /// </summary>
    public static (uint width, uint height) GetBlockSize(this TextureFormat format)
    {
        return format switch
        {
            // BC formats are 4x4 blocks
            >= TextureFormat.BC1Unorm and <= TextureFormat.BC7Srgb => (4, 4),
            
            // ASTC variable block sizes
            TextureFormat.ASTC4x4Unorm or TextureFormat.ASTC4x4Srgb => (4, 4),
            TextureFormat.ASTC5x5Unorm or TextureFormat.ASTC5x5Srgb => (5, 5),
            TextureFormat.ASTC6x6Unorm or TextureFormat.ASTC6x6Srgb => (6, 6),
            TextureFormat.ASTC8x8Unorm or TextureFormat.ASTC8x8Srgb => (8, 8),
            TextureFormat.ASTC10x10Unorm or TextureFormat.ASTC10x10Srgb => (10, 10),
            TextureFormat.ASTC12x12Unorm or TextureFormat.ASTC12x12Srgb => (12, 12),
            
            // ETC2 is 4x4 blocks
            >= TextureFormat.ETC2RGB8Unorm and <= TextureFormat.ETC2RGBA8Srgb => (4, 4),
            
            // Uncompressed is 1x1
            _ => (1, 1)
        };
    }
    
    /// <summary>
    /// Get best format for platform and usage.
    /// </summary>
    public static TextureFormat GetBestFormat(NotBSRenderer.RHIBackend backend, TextureUsage usage, bool srgb)
    {
        return backend switch
        {
            NotBSRenderer.RHIBackend.Metal => usage switch
            {
                TextureUsage.Albedo => srgb ? TextureFormat.ASTC6x6Srgb : TextureFormat.ASTC6x6Unorm,
                TextureUsage.Normal => TextureFormat.ASTC6x6Unorm, // Never sRGB for normals
                TextureUsage.RoughnessMetallicAO => TextureFormat.ASTC6x6Unorm,
                TextureUsage.HDR => TextureFormat.BC6HUFloat,
                _ => srgb ? TextureFormat.RGBA8Srgb : TextureFormat.RGBA8Unorm
            },
            
            NotBSRenderer.RHIBackend.DirectX11 or NotBSRenderer.RHIBackend.DirectX12 => usage switch
            {
                TextureUsage.Albedo => srgb ? TextureFormat.BC7Srgb : TextureFormat.BC7Unorm,
                TextureUsage.Normal => TextureFormat.BC5Unorm, // RG for normal maps
                TextureUsage.RoughnessMetallicAO => TextureFormat.BC7Unorm,
                TextureUsage.HDR => TextureFormat.BC6HUFloat,
                _ => srgb ? TextureFormat.RGBA8Srgb : TextureFormat.RGBA8Unorm
            },
            
            NotBSRenderer.RHIBackend.Vulkan => usage switch
            {
                TextureUsage.Albedo => srgb ? TextureFormat.BC7Srgb : TextureFormat.BC7Unorm,
                TextureUsage.Normal => TextureFormat.BC5Unorm,
                TextureUsage.RoughnessMetallicAO => TextureFormat.BC7Unorm,
                TextureUsage.HDR => TextureFormat.BC6HUFloat,
                _ => srgb ? TextureFormat.RGBA8Srgb : TextureFormat.RGBA8Unorm
            },
            
            _ => srgb ? TextureFormat.RGBA8Srgb : TextureFormat.RGBA8Unorm
        };
    }
    
    /// <summary>
    /// Convert to RHI texture format (for backward compatibility).
    /// </summary>
    public static NotBSRenderer.TextureFormat ToRHIFormat(this TextureFormat format)
    {
        return format switch
        {
            TextureFormat.RGBA8Unorm => NotBSRenderer.TextureFormat.RGBA8Unorm,
            TextureFormat.RGBA8Srgb => NotBSRenderer.TextureFormat.RGBA8Srgb,
            TextureFormat.BGRA8Unorm => NotBSRenderer.TextureFormat.BGRA8Unorm,
            TextureFormat.RGBA16Float => NotBSRenderer.TextureFormat.RGBA16Float,
            TextureFormat.RGBA32Float => NotBSRenderer.TextureFormat.RGBA32Float,
            TextureFormat.Depth32Float => NotBSRenderer.TextureFormat.Depth32Float,
            TextureFormat.Depth24PlusStencil8 => NotBSRenderer.TextureFormat.Depth24Stencil8,
            
            // Compressed formats (if RHI supports them)
            TextureFormat.BC1Unorm => NotBSRenderer.TextureFormat.BC1,
            TextureFormat.BC3Unorm => NotBSRenderer.TextureFormat.BC3,
            TextureFormat.BC7Unorm => NotBSRenderer.TextureFormat.BC7,
            
            // Default fallback
            _ => NotBSRenderer.TextureFormat.RGBA8Unorm
        };
    }
}

/// <summary>
/// Texture usage hint for format selection.
/// </summary>
public enum TextureUsage
{
    Albedo,              // Base color (sRGB)
    Normal,              // Normal map (linear, RG or RGB)
    RoughnessMetallicAO, // Packed PBR maps (linear)
    Emissive,            // Emissive (sRGB or HDR)
    HDR,                 // High dynamic range
    UI,                  // UI textures (sRGB, no compression)
    Generic              // Unknown usage
}
