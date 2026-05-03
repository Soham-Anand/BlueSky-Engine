using BlueSky.Core.Assets;

namespace BlueSky.Rendering;

/// <summary>
/// Loads processed .blueasset files into GPU memory.
/// Works with the asset import pipeline!
/// </summary>
public class AssetLoader
{
    private readonly IRenderer _renderer;

    public AssetLoader(IRenderer renderer)
    {
        _renderer = renderer;
    }

    /// <summary>
    /// Load mesh from .blueasset file and upload to GPU.
    /// </summary>
    public List<int> LoadMeshAsset(string assetPath)
    {
        var asset = BlueAsset.Load(assetPath);
        if (asset == null || (asset.Type != AssetType.StaticMesh && asset.Type != AssetType.Mesh))
        {
            Console.WriteLine($"[AssetLoader] Failed to load mesh asset: {assetPath}");
            return new List<int>();
        }

        if (!File.Exists(asset.DataFile))
        {
            Console.WriteLine($"[AssetLoader] Mesh data file not found: {asset.DataFile}");
            return new List<int>();
        }

        var meshIds = new List<int>();

        try
        {
            using var ms = new MemoryStream(asset.PayloadData);
            using var reader = new BinaryReader(ms);

            if (asset.Metadata.TryGetValue("format", out var format) && format == "Packed32")
            {
                // New unified binary format (Position, Normal, UV)
                int vertexByteCount = reader.ReadInt32();
                byte[] vertexBytes = reader.ReadBytes(vertexByteCount);
                int vertexCount = vertexByteCount / 32;

                int indexByteCount = reader.ReadInt32();
                byte[] indexBytes = reader.ReadBytes(indexByteCount);
                int indexCount = indexByteCount / 4;

                int submeshCount = reader.ReadInt32();
                for (int i = 0; i < submeshCount; i++)
                {
                    int offset = reader.ReadInt32();
                    int count = reader.ReadInt32();
                    int slot = reader.ReadInt32();

                    // Extract sub-index buffer for this submesh
                    var subIndices = new uint[count];
                    Buffer.BlockCopy(indexBytes, offset * 4, subIndices, 0, count * 4);

                    // Upload as separate mesh to renderer for now
                    // TODO: Move to multi-submesh renderer support
                    float[] floatVertices = new float[vertexCount * 8];
                    Buffer.BlockCopy(vertexBytes, 0, floatVertices, 0, vertexByteCount);

                    var meshId = _renderer.CreateMesh(floatVertices, subIndices);
                    meshIds.Add(meshId);
                }
            }
            else
            {
                // Legacy format support
                int meshCount = reader.ReadInt32();
                for (int i = 0; i < meshCount; i++)
                {
                    string meshName = reader.ReadString();
                    int materialIndex = reader.ReadInt32();
                    int vertexCount = reader.ReadInt32();
                    var vertices = new float[vertexCount * 14];
                    for (int v = 0; v < vertexCount; v++)
                    {
                        for (int f = 0; f < 14; f++) vertices[v * 14 + f] = reader.ReadSingle();
                    }
                    int indexCount = reader.ReadInt32();
                    var indices = new uint[indexCount];
                    for (int idx = 0; idx < indexCount; idx++) indices[idx] = reader.ReadUInt32();

                    var meshId = UploadMeshToGPU(vertices, indices);
                    meshIds.Add(meshId);
                }
            }

            Console.WriteLine($"[AssetLoader] Loaded mesh asset '{asset.AssetName}' with {meshIds.Count} meshes");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetLoader] Error loading mesh: {ex.Message}");
        }

        return meshIds;
    }

    /// <summary>
    /// Load texture from .blueasset file and upload to GPU.
    /// </summary>
    public int LoadTextureAsset(string assetPath, bool srgb = true)
    {
        var asset = BlueAsset.Load(assetPath);
        if (asset == null || asset.Type != AssetType.Texture)
        {
            Console.WriteLine($"[AssetLoader] Failed to load texture asset: {assetPath}");
            return 0;
        }

        if (!File.Exists(asset.DataFile))
        {
            Console.WriteLine($"[AssetLoader] Texture data file not found: {asset.DataFile}");
            return 0;
        }

        try
        {
            using var ms = new MemoryStream(asset.PayloadData);
            using var reader = new BinaryReader(ms);

            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            int components = reader.ReadInt32();
            int dataLength = reader.ReadInt32();
            byte[] data = reader.ReadBytes(dataLength);

            bool isSRGB = srgb;
            if (asset.Metadata.TryGetValue("format", out var fmt) && fmt == "RGBA8") isSRGB = srgb;

            // Upload to GPU
            var textureId = UploadTextureToGPU(width, height, data, isSRGB);

            Console.WriteLine($"[AssetLoader] Loaded texture asset '{asset.AssetName}' ({width}x{height})");
            return textureId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetLoader] Error loading texture: {ex.Message}");
            return 0;
        }
    }

    private int UploadMeshToGPU(float[] vertices, uint[] indices)
    {
        return _renderer.CreateMesh(vertices, indices);
    }

    private int UploadTextureToGPU(int width, int height, byte[] data, bool srgb)
    {
        return _renderer.CreateTexture(width, height, data, srgb);
    }
}
