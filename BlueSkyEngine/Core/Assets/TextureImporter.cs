using StbImageSharp;

namespace BlueSky.Core.Assets;

/// <summary>
/// Texture import handler - loads images and converts to engine-ready format.
/// </summary>
public class TextureImportHandler : IAssetImportHandler
{
    public string[] SupportedExtensions => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".psd", ".gif" };
    public AssetType AssetType => AssetType.Texture;

    public ImportResult Import(string sourceFile, BlueAsset asset, ImportOptions? options)
    {
        try
        {
            // Load image using StbImageSharp
            StbImage.stbi_set_flip_vertically_on_load(0); // Don't flip - engine handles orientation
            
            using var stream = File.OpenRead(sourceFile);
            var result = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            if (result == null)
            {
                return new ImportResult
                {
                    Success = false,
                    Error = "Failed to load image"
                };
            }

            var width = result.Width;
            var height = result.Height;
            var pixelData = result.Data;

            // Check for alpha channel
            var hasAlpha = HasAlphaChannel(pixelData);

            // Pack texture data into payload (width, height, channels, data)
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            writer.Write(width);
            writer.Write(height);
            writer.Write(4); // RGBA channels
            writer.Write(pixelData.Length);
            writer.Write(pixelData);

            // Update asset metadata
            asset.Metadata["width"] = width.ToString();
            asset.Metadata["height"] = height.ToString();
            asset.Metadata["format"] = "RGBA8";
            asset.Metadata["channels"] = "4";
            asset.Metadata["hasAlpha"] = hasAlpha.ToString();
            asset.Metadata["sizeBytes"] = pixelData.Length.ToString();

            Console.WriteLine($"[TextureImporter] ✓ Imported: {width}x{height} RGBA ({pixelData.Length} bytes)");

            return new ImportResult
            {
                Success = true,
                PayloadData = ms.ToArray()
            };
        }
        catch (Exception ex)
        {
            return new ImportResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static bool HasAlphaChannel(byte[] pixelData)
    {
        // Check if any pixel has alpha < 255
        for (int i = 3; i < pixelData.Length; i += 4)
        {
            if (pixelData[i] < 255)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Check if a .blueskyasset texture file contains any non-opaque alpha pixels.
    /// Used by MeshImportHandler to decide blend mode at import time.
    /// </summary>
    public static bool HasAlphaInAsset(string assetPath)
    {
        try
        {
            if (!File.Exists(assetPath)) return false;
            
            var asset = BlueAsset.Load(assetPath);
            if (asset == null || !asset.HasPayload) return false;
            
            using var ms = new MemoryStream(asset.PayloadData);
            using var reader = new BinaryReader(ms);
            
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            int components = reader.ReadInt32();
            int dataLen = reader.ReadInt32();
            
            if (width <= 0 || height <= 0 || dataLen <= 0) return false;
            
            byte[] data = reader.ReadBytes(dataLen);
            return HasAlphaChannel(data);
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Check if a raw image file (PNG/TGA/etc.) contains any non-opaque alpha pixels.
    /// </summary>
    public static bool HasAlphaInFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            
            StbImageSharp.StbImage.stbi_set_flip_vertically_on_load(0);
            using var stream = File.OpenRead(filePath);
            var result = StbImageSharp.ImageResult.FromStream(
                stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            
            if (result == null) return false;
            return HasAlphaChannel(result.Data);
        }
        catch
        {
            return false;
        }
    }
}
