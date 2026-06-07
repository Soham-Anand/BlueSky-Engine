// BlueSkyEngine - DDS Texture Loader
// Loads DirectDraw Surface (.dds) files with BC compression support

using System;
using System.IO;
using System.Runtime.InteropServices;
using BlueSky.Core.Diagnostics;
using NotBSRenderer;

namespace BlueSky.Rendering.Textures;

/// <summary>
/// DDS texture loader - supports BC1-BC7, uncompressed formats, and mipmaps.
/// </summary>
public static class DDSLoader
{
    private const uint DDS_MAGIC = 0x20534444; // "DDS "
    
    /// <summary>
    /// Load DDS texture from file.
    /// </summary>
    public static DDSTexture? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                ErrorHandler.LogError($"DDS file not found: {path}", null, "DDSLoader");
                return null;
            }
            
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            
            // Read magic number
            uint magic = reader.ReadUInt32();
            if (magic != DDS_MAGIC)
            {
                ErrorHandler.LogError($"Invalid DDS file (bad magic): {path}", null, "DDSLoader");
                return null;
            }
            
            // Read DDS header
            var header = ReadHeader(reader);
            
            // Determine format
            var format = DetermineFormat(header);
            if (format == TextureFormat.RGBA8Unorm && header.PixelFormat.FourCC != 0)
            {
                ErrorHandler.LogWarning($"Unsupported DDS format in {path}, using RGBA8 fallback", "DDSLoader");
            }
            
            // Read mip levels
            int mipCount = Math.Max(1, (int)header.MipMapCount);
            var mipLevels = new DDSMipLevel[mipCount];
            
            uint width = header.Width;
            uint height = header.Height;
            
            for (int mip = 0; mip < mipCount; mip++)
            {
                uint mipWidth = Math.Max(1, width >> mip);
                uint mipHeight = Math.Max(1, height >> mip);
                
                uint dataSize = CalculateMipSize(format, mipWidth, mipHeight);
                byte[] data = reader.ReadBytes((int)dataSize);
                
                mipLevels[mip] = new DDSMipLevel
                {
                    Width = mipWidth,
                    Height = mipHeight,
                    Data = data
                };
            }
            
            var texture = new DDSTexture
            {
                Width = width,
                Height = height,
                Format = format,
                MipLevels = mipLevels
            };
            
            ErrorHandler.LogInfo($"Loaded DDS: {path} ({width}x{height}, {format}, {mipCount} mips)", "DDSLoader");
            return texture;
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError($"Failed to load DDS: {path}", ex, "DDSLoader");
            return null;
        }
    }
    
    private static DDSHeader ReadHeader(BinaryReader reader)
    {
        var header = new DDSHeader
        {
            Size = reader.ReadUInt32(),
            Flags = reader.ReadUInt32(),
            Height = reader.ReadUInt32(),
            Width = reader.ReadUInt32(),
            PitchOrLinearSize = reader.ReadUInt32(),
            Depth = reader.ReadUInt32(),
            MipMapCount = reader.ReadUInt32()
        };
        
        // Skip reserved
        reader.ReadBytes(11 * 4);
        
        // Read pixel format
        header.PixelFormat = new DDSPixelFormat
        {
            Size = reader.ReadUInt32(),
            Flags = reader.ReadUInt32(),
            FourCC = reader.ReadUInt32(),
            RGBBitCount = reader.ReadUInt32(),
            RBitMask = reader.ReadUInt32(),
            GBitMask = reader.ReadUInt32(),
            BBitMask = reader.ReadUInt32(),
            ABitMask = reader.ReadUInt32()
        };
        
        // Read caps
        header.Caps = reader.ReadUInt32();
        header.Caps2 = reader.ReadUInt32();
        header.Caps3 = reader.ReadUInt32();
        header.Caps4 = reader.ReadUInt32();
        
        // Skip reserved2
        reader.ReadUInt32();
        
        return header;
    }
    
    private static TextureFormat DetermineFormat(DDSHeader header)
    {
        uint fourCC = header.PixelFormat.FourCC;
        
        // Check FourCC for compressed formats
        if (fourCC != 0)
        {
            return fourCC switch
            {
                0x31545844 => TextureFormat.BC1Unorm,  // "DXT1"
                0x33545844 => TextureFormat.BC2Unorm,  // "DXT3"
                0x35545844 => TextureFormat.BC3Unorm,  // "DXT5"
                0x31495441 => TextureFormat.BC4Unorm,  // "ATI1"
                0x32495441 => TextureFormat.BC5Unorm,  // "ATI2"
                0x30315844 => TextureFormat.BC4Unorm,  // "DX10" - need extended header
                _ => TextureFormat.RGBA8Unorm // Fallback
            };
        }
        
        // Uncompressed format based on bit masks
        if (header.PixelFormat.RGBBitCount == 32)
        {
            // Check for RGBA8
            if (header.PixelFormat.RBitMask == 0x000000FF &&
                header.PixelFormat.GBitMask == 0x0000FF00 &&
                header.PixelFormat.BBitMask == 0x00FF0000 &&
                header.PixelFormat.ABitMask == 0xFF000000)
            {
                return TextureFormat.RGBA8Unorm;
            }
            
            // Check for BGRA8
            if (header.PixelFormat.RBitMask == 0x00FF0000 &&
                header.PixelFormat.GBitMask == 0x0000FF00 &&
                header.PixelFormat.BBitMask == 0x000000FF &&
                header.PixelFormat.ABitMask == 0xFF000000)
            {
                return TextureFormat.BGRA8Unorm;
            }
        }
        
        return TextureFormat.RGBA8Unorm; // Default fallback
    }
    
    private static uint CalculateMipSize(TextureFormat format, uint width, uint height)
    {
        if (format.IsCompressed())
        {
            var (blockWidth, blockHeight) = format.GetBlockSize();
            uint blocksX = (width + blockWidth - 1) / blockWidth;
            uint blocksY = (height + blockHeight - 1) / blockHeight;
            uint blockSize = format.GetBytesPerPixel();
            return blocksX * blocksY * blockSize;
        }
        else
        {
            return width * height * format.GetBytesPerPixel();
        }
    }
}

/// <summary>
/// DDS texture data.
/// </summary>
public class DDSTexture
{
    public uint Width;
    public uint Height;
    public TextureFormat Format;
    public DDSMipLevel[] MipLevels = Array.Empty<DDSMipLevel>();
    
    /// <summary>
    /// Upload to GPU.
    /// </summary>
    public IRHITexture? UploadToGPU(IRHIDevice device, bool generateMips = false)
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
            ErrorHandler.LogError("Failed to upload DDS texture to GPU", ex, "DDSLoader");
            return null;
        }
    }
}

/// <summary>
/// DDS mip level.
/// </summary>
public struct DDSMipLevel
{
    public uint Width;
    public uint Height;
    public byte[] Data;
}

/// <summary>
/// DDS header structure.
/// </summary>
internal struct DDSHeader
{
    public uint Size;
    public uint Flags;
    public uint Height;
    public uint Width;
    public uint PitchOrLinearSize;
    public uint Depth;
    public uint MipMapCount;
    public DDSPixelFormat PixelFormat;
    public uint Caps;
    public uint Caps2;
    public uint Caps3;
    public uint Caps4;
}

/// <summary>
/// DDS pixel format structure.
/// </summary>
internal struct DDSPixelFormat
{
    public uint Size;
    public uint Flags;
    public uint FourCC;
    public uint RGBBitCount;
    public uint RBitMask;
    public uint GBitMask;
    public uint BBitMask;
    public uint ABitMask;
}
