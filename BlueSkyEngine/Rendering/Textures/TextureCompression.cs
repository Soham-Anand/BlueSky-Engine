using System;
using System.Runtime.InteropServices;

namespace BlueSky.Rendering.Textures;

/// <summary>
/// Texture compression system for reducing memory bandwidth on old hardware.
/// Supports BC7 (Windows/DX) and ASTC (mobile) compression formats.
/// </summary>
public class TextureCompression
{
    /// <summary>
    /// Compress RGBA8 texture to BC7 format.
    /// BC7 provides 8:1 compression ratio with good quality.
    /// </summary>
    public static byte[] CompressBC7(byte[] rgbaData, int width, int height)
    {
        // BC7 compresses 4x4 pixel blocks (16 pixels) into 16 bytes
        // Compression ratio: 64 bytes (RGBA8 4x4) -> 16 bytes (BC7) = 4:1
        int blockWidth = 4;
        int blockHeight = 4;
        
        int blocksX = (width + blockWidth - 1) / blockWidth;
        int blocksY = (height + blockHeight - 1) / blockHeight;
        
        byte[] compressed = new byte[blocksX * blocksY * 16];
        
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                int blockIndex = by * blocksX + bx;
                int blockOffset = blockIndex * 16;
                
                // Extract 4x4 block
                var block = ExtractBlock(rgbaData, width, height, bx * 4, by * 4);
                
                // Compress block to BC7
                var compressedBlock = CompressBC7Block(block);
                
                // Store compressed block
                Buffer.BlockCopy(compressedBlock, 0, compressed, blockOffset, 16);
            }
        }
        
        return compressed;
    }
    
    /// <summary>
    /// Compress RGBA8 texture to ASTC 4x4 format.
    /// ASTC provides flexible block sizes with good quality.
    /// </summary>
    public static byte[] CompressASTC4x4(byte[] rgbaData, int width, int height)
    {
        // ASTC 4x4 compresses 4x4 pixel blocks (16 pixels) into 16 bytes
        // Compression ratio: 64 bytes (RGBA8 4x4) -> 16 bytes (ASTC 4x4) = 4:1
        int blockWidth = 4;
        int blockHeight = 4;
        
        int blocksX = (width + blockWidth - 1) / blockWidth;
        int blocksY = (height + blockHeight - 1) / blockHeight;
        
        byte[] compressed = new byte[blocksX * blocksY * 16];
        
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                int blockIndex = by * blocksX + bx;
                int blockOffset = blockIndex * 16;
                
                // Extract 4x4 block
                var block = ExtractBlock(rgbaData, width, height, bx * 4, by * 4);
                
                // Compress block to ASTC
                var compressedBlock = CompressASTCBlock(block);
                
                // Store compressed block
                Buffer.BlockCopy(compressedBlock, 0, compressed, blockOffset, 16);
            }
        }
        
        return compressed;
    }
    
    /// <summary>
    /// Compress RGBA8 texture to ASTC 6x6 format (higher quality, lower compression).
    /// </summary>
    public static byte[] CompressASTC6x6(byte[] rgbaData, int width, int height)
    {
        // ASTC 6x6 compresses 6x6 pixel blocks (36 pixels) into 16 bytes
        // Compression ratio: 144 bytes (RGBA8 6x6) -> 16 bytes (ASTC 6x6) = 9:1
        int blockWidth = 6;
        int blockHeight = 6;
        
        int blocksX = (width + blockWidth - 1) / blockWidth;
        int blocksY = (height + blockHeight - 1) / blockHeight;
        
        byte[] compressed = new byte[blocksX * blocksY * 16];
        
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                int blockIndex = by * blocksX + bx;
                int blockOffset = blockIndex * 16;
                
                // Extract 6x6 block
                var block = ExtractBlock(rgbaData, width, height, bx * 6, by * 6, 6, 6);
                
                // Compress block to ASTC 6x6
                var compressedBlock = CompressASTCBlock(block);
                
                // Store compressed block
                Buffer.BlockCopy(compressedBlock, 0, compressed, blockOffset, 16);
            }
        }
        
        return compressed;
    }
    
    /// <summary>
    /// Extract a pixel block from RGBA8 data.
    /// </summary>
    private static byte[] ExtractBlock(byte[] data, int width, int height, int startX, int startY, int blockW = 4, int blockH = 4)
    {
        byte[] block = new byte[blockW * blockH * 4];
        
        for (int y = 0; y < blockH; y++)
        {
            for (int x = 0; x < blockW; x++)
            {
                int srcX = startX + x;
                int srcY = startY + y;
                
                // Clamp to image bounds
                srcX = Math.Clamp(srcX, 0, width - 1);
                srcY = Math.Clamp(srcY, 0, height - 1);
                
                int srcOffset = (srcY * width + srcX) * 4;
                int dstOffset = (y * blockW + x) * 4;
                
                if (srcOffset + 4 <= data.Length)
                {
                    Buffer.BlockCopy(data, srcOffset, block, dstOffset, 4);
                }
            }
        }
        
        return block;
    }
    
    /// <summary>
    /// Simplified BC7 block compression (placeholder - real BC7 is complex).
    /// For production, use a library like bc7enc or astcenc.
    /// </summary>
    private static byte[] CompressBC7Block(byte[] block)
    {
        // Simplified: just store average color for now
        // Real BC7 requires complex encoding with 2 color endpoints and weights
        
        byte[] compressed = new byte[16];
        
        // Calculate average color
        float r = 0, g = 0, b = 0, a = 0;
        int pixelCount = 16;
        
        for (int i = 0; i < block.Length; i += 4)
        {
            r += block[i];
            g += block[i + 1];
            b += block[i + 2];
            a += block[i + 3];
        }
        
        r /= pixelCount;
        g /= pixelCount;
        b /= pixelCount;
        a /= pixelCount;
        
        // Store average color in first 4 bytes
        compressed[0] = (byte)r;
        compressed[1] = (byte)g;
        compressed[2] = (byte)b;
        compressed[3] = (byte)a;
        
        // Rest of the block would be encoded with BC7 mode
        // For now, fill with zeros
        for (int i = 4; i < 16; i++)
            compressed[i] = 0;
        
        return compressed;
    }
    
    /// <summary>
    /// Simplified ASTC block compression (placeholder).
    /// For production, use astcenc library.
    /// </summary>
    private static byte[] CompressASTCBlock(byte[] block)
    {
        // Similar to BC7, real ASTC requires complex encoding
        // This is a placeholder that stores the average color
        
        byte[] compressed = new byte[16];
        
        // Calculate average color
        float r = 0, g = 0, b = 0, a = 0;
        int pixelCount = block.Length / 4;
        
        for (int i = 0; i < block.Length; i += 4)
        {
            r += block[i];
            g += block[i + 1];
            b += block[i + 2];
            a += block[i + 3];
        }
        
        r /= pixelCount;
        g /= pixelCount;
        b /= pixelCount;
        a /= pixelCount;
        
        // Store average color
        compressed[0] = (byte)r;
        compressed[1] = (byte)g;
        compressed[2] = (byte)b;
        compressed[3] = (byte)a;
        
        // Rest would be encoded with ASTC
        for (int i = 4; i < 16; i++)
            compressed[i] = 0;
        
        return compressed;
    }
    
    /// <summary>
    /// Get recommended compression format based on platform and quality settings.
    /// </summary>
    public static CompressionFormat GetRecommendedFormat(Platform platform, QualityLevel quality)
    {
        return (platform, quality) switch
        {
            (Platform.Windows, QualityLevel.Low) => CompressionFormat.BC1,
            (Platform.Windows, QualityLevel.Medium) => CompressionFormat.BC3,
            (Platform.Windows, QualityLevel.High) => CompressionFormat.BC7,
            (Platform.MacOS, QualityLevel.Low) => CompressionFormat.ASTC4x4,
            (Platform.MacOS, QualityLevel.Medium) => CompressionFormat.ASTC4x4,
            (Platform.MacOS, QualityLevel.High) => CompressionFormat.ASTC6x6,
            (Platform.Mobile, QualityLevel.Low) => CompressionFormat.ASTC4x4,
            (Platform.Mobile, QualityLevel.Medium) => CompressionFormat.ASTC4x4,
            (Platform.Mobile, QualityLevel.High) => CompressionFormat.ASTC6x6,
            _ => CompressionFormat.BC3
        };
    }
}

public enum CompressionFormat
{
    None,
    BC1,      // DXT1 - 4:1 compression, no alpha
    BC3,      // DXT5 - 4:1 compression, with alpha
    BC7,      // DX11 - 4:1 compression, high quality
    ASTC4x4,  // 4:1 compression, mobile
    ASTC6x6,  // 9:1 compression, higher quality
    ETC2      // Mobile alternative
}

public enum Platform
{
    Windows,
    MacOS,
    Linux,
    Mobile
}

public enum QualityLevel
{
    Low,
    Medium,
    High,
    Ultra
}
