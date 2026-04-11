using System.IO;
using StbImageSharp;

namespace BlueSky.Rendering;

public class TextureLoader
{
    private readonly IRenderer _renderer;

    public TextureLoader(IRenderer renderer)
    {
        _renderer = renderer;
    }

    public int LoadTexture(string path, bool srgb = true)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[TextureLoader] File not found: {path}");
            return 0;
        }

        StbImage.stbi_set_flip_vertically_on_load(1);
        
        using var stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        if (image == null)
        {
            Console.WriteLine($"[TextureLoader] Failed to load: {path}");
            return 0;
        }

        int textureId = _renderer.CreateTexture(image.Width, image.Height, image.Data, srgb);

        Console.WriteLine($"[TextureLoader] Loaded '{path}' ({image.Width}x{image.Height})");
        return textureId;
    }

    public int CreateWhiteTexture()
    {
        byte[] whitePixel = { 255, 255, 255, 255 };
        return _renderer.CreateTexture(1, 1, whitePixel, false);
    }

    public int CreateNormalTexture()
    {
        byte[] normalPixel = { 128, 128, 255, 255 }; // Default normal pointing up
        return _renderer.CreateTexture(1, 1, normalPixel, false);
    }
}
