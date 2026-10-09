namespace BlueSky.Rendering.RHI;

public interface IRHITexture : IDisposable
{
    uint Width { get; }
    uint Height { get; }
    TextureFormat Format { get; }
    TextureUsage Usage { get; }
}
