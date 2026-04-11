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

            // Save binary data
            var dataPath = sourceFile + ".texdata";
            File.WriteAllBytes(dataPath, pixelData);

            // Update asset metadata
            asset.Metadata["width"] = width.ToString();
            asset.Metadata["height"] = height.ToString();
            asset.Metadata["format"] = "RGBA8";
            asset.Metadata["channels"] = "4";
            asset.Metadata["hasAlpha"] = hasAlpha.ToString();

            return new ImportResult
            {
                Success = true,
                DataFilePath = dataPath
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
}
