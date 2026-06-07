// BlueSkyEngine - KTX2 Texture Loader
// Loads Khronos Texture v2 (.ktx2) files with ASTC/ETC2 compression

using System;
using System.IO;
using System.Text;
using BlueSky.Core.Diagnostics;
using NotBSRenderer;

namespace BlueSky.Rendering.Textures;

/// <summary>
/// KTX2 texture loader - supports ASTC, ETC2, BC, and uncompressed formats.
/// KTX2 is the modern standard for GPU textures (successor to KTX1).
/// </summary>
public static class KTX2Loader
{
    private static readonly byte[] KTX2_IDENTIFIER = { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };
    
    /// <summary>
    /// Load KTX2 texture from file.
    /// </summary>
    public static KTX2Texture? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                ErrorHandler.LogError($"KTX2 file not found: {path}", null, "KTX2Loader");
                return null;
            }
            
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            
            // Read identifier
            byte[] identifier = reader.ReadBytes(12);
            if (!identifier.SequenceEqual(KTX2_IDENTIFIER))
            {
                ErrorHandler.LogError($"Invalid KTX2 file (bad identifier): {path}", null, "KTX2Loader");
                return null;
            }
            
            // Read header
            var header = ReadHeader(reader);
            
            // Determine format
            var format = DetermineFormat(header);
            if (format == TextureFormat.RGBA8Unorm && header.VkFormat != 0)
            {
                ErrorHandler.LogWarning($"Unsupported KTX2 format in {path}, using RGBA8 fallback", "KTX2Loader");
            }
            
            // Read level index
            var levelIndex = ReadLevelIndex(reader, header);
            
            // Read mip levels
            var mipLevels = new KTX2MipLevel[header.LevelCount];
            
            for (int mip = 0; mip < header.LevelCount; mip++)
            {
                var level = levelIndex[mip];
                stream.Seek((long)level.ByteOffset, SeekOrigin.Begin);
                
                byte[] data = reader.ReadBytes((int)level.ByteLength);
                
                uint mipWidth = Math.Max(1, header.PixelWidth >> mip);
                uint mipHeight = Math.Max(1, header.PixelHeight >> mip);
                
                mipLevels[mip] = new KTX2MipLevel
                {
                    Width = mipWidth,
                    Height = mipHeight,
                    Data = data
                };
            }
            
            var texture = new KTX2Texture
            {
                Width = header.PixelWidth,
                Height = header.PixelHeight,
                Format = format,
                MipLevels = mipLevels
            };
            
            ErrorHandler.LogInfo($"Loaded KTX2: {path} ({header.PixelWidth}x{header.PixelHeight}, {format}, {header.LevelCount} mips)", "KTX2Loader");
            return texture;
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError($"Failed to load KTX2: {path}", ex, "KTX2Loader");
            return null;
        }
    }
    
    private static KTX2Header ReadHeader(BinaryReader reader)
    {
        return new KTX2Header
        {
            VkFormat = reader.ReadUInt32(),
            TypeSize = reader.ReadUInt32(),
            PixelWidth = reader.ReadUInt32(),
            PixelHeight = reader.ReadUInt32(),
            PixelDepth = reader.ReadUInt32(),
            LayerCount = reader.ReadUInt32(),
            FaceCount = reader.ReadUInt32(),
            LevelCount = reader.ReadUInt32(),
            SupercompressionScheme = reader.ReadUInt32()
        };
    }
    
    private static KTX2LevelIndex[] ReadLevelIndex(BinaryReader reader, KTX2Header header)
    {
        var levels = new KTX2LevelIndex[header.LevelCount];
        
        for (int i = 0; i < header.LevelCount; i++)
        {
            levels[i] = new KTX2LevelIndex
            {
                ByteOffset = reader.ReadUInt64(),
                ByteLength = reader.ReadUInt64(),
                UncompressedByteLength = reader.ReadUInt64()
            };
        }
        
        return levels;
    }
    
    private static TextureFormat DetermineFormat(KTX2Header header)
    {
        // Vulkan format enum to TextureFormat mapping
        return header.VkFormat switch
        {
            // ASTC formats
            157 => TextureFormat.ASTC4x4Unorm,
            158 => TextureFormat.ASTC4x4Srgb,
            159 => TextureFormat.ASTC5x5Unorm,
            160 => TextureFormat.ASTC5x5Srgb,
            161 => TextureFormat.ASTC6x6Unorm,
            162 => TextureFormat.ASTC6x6Srgb,
            163 => TextureFormat.ASTC8x8Unorm,
            164 => TextureFormat.ASTC8x8Srgb,
            165 => TextureFormat.ASTC10x10Unorm,
            166 => TextureFormat.ASTC10x10Srgb,
            167 => TextureFormat.ASTC12x12Unorm,
            168 => TextureFormat.ASTC12x12Srgb,
            
            // BC formats
            131 => TextureFormat.BC1Unorm,
            132 => TextureFormat.BC1Srgb,
            135 => TextureFormat.BC2Unorm,
            136 => TextureFormat.BC2Srgb,
            137 => TextureFormat.BC3Unorm,
            138 => TextureFormat.BC3Srgb,
            139 => TextureFormat.BC4Unorm,
            141 => TextureFormat.BC5Unorm,
            143 => TextureFormat.BC6HUFloat,
            145 => TextureFormat.BC7Unorm,
            146 => TextureFormat.BC7Srgb,
            
            // Uncompressed
            37 => TextureFormat.RGBA8Unorm,
            43 => TextureFormat.RGBA8Srgb,
            44 => TextureFormat.BGRA8Unorm,
            50 => TextureFormat.BGRA8Srgb,
            
            _ => TextureFormat.RGBA8Unorm // Fallback
        };
    }
}

/// <summary>
/// KTX2 texture data.
/// </summary>
public class KTX2Texture
{
    public uint Width;
    public uint Height;
    public TextureFormat Format;
    public KTX2MipLevel[] MipLevels = Array.Empty<KTX2MipLevel>();
    
    /// <summary>
    /// Upload to GPU.
    /// </summary>
    public IRHITexture? UploadToGPU(IRHIDevice device)
    {
        try
        {
            var desc = new TextureDesc
            {
                Width = Width,
                Height = Height,
                Depth = 1,
                Format = Format.ToRHIFormat(),
                Usage = NotBSRenderer.TextureUsage.Sampled | NotBSRenderer.TextureUsage.TransferDst,
                MipLevels = (uint)MipLevels.Length,
                ArrayLayers = 1
            };
            
            var texture = device.CreateTexture(desc);
            
            // Upload each mip level
            for (int mip = 0; mip < MipLevels.Length; mip++)
            {
                device.UploadTexture(texture, MipLevels[mip].Data, (uint)mip);
            }
            
            return texture;
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError("Failed to upload KTX2 texture to GPU", ex, "KTX2Loader");
            return null;
        }
    }
}

/// <summary>
/// KTX2 mip level.
/// </summary>
public struct KTX2MipLevel
{
    public uint Width;
    public uint Height;
    public byte[] Data;
}

/// <summary>
/// KTX2 header structure.
/// </summary>
internal struct KTX2Header
{
    public uint VkFormat;
    public uint TypeSize;
    public uint PixelWidth;
    public uint PixelHeight;
    public uint PixelDepth;
    public uint LayerCount;
    public uint FaceCount;
    public uint LevelCount;
    public uint SupercompressionScheme;
}

/// <summary>
/// KTX2 level index entry.
/// </summary>
internal struct KTX2LevelIndex
{
    public ulong ByteOffset;
    public ulong ByteLength;
    public ulong UncompressedByteLength;
}
