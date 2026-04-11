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
        if (asset == null || asset.Type != AssetType.Mesh)
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
            using var fs = File.OpenRead(asset.DataFile);
            using var reader = new BinaryReader(fs);

            // Read mesh count
            int meshCount = reader.ReadInt32();

            for (int i = 0; i < meshCount; i++)
            {
                // Read mesh data
                string meshName = reader.ReadString();
                int materialIndex = reader.ReadInt32();

                // Read vertices
                int vertexCount = reader.ReadInt32();
                var vertices = new float[vertexCount * 14]; // 14 floats per vertex

                for (int v = 0; v < vertexCount; v++)
                {
                    int offset = v * 14;
                    vertices[offset + 0] = reader.ReadSingle(); // pos.x
                    vertices[offset + 1] = reader.ReadSingle(); // pos.y
                    vertices[offset + 2] = reader.ReadSingle(); // pos.z
                    vertices[offset + 3] = reader.ReadSingle(); // normal.x
                    vertices[offset + 4] = reader.ReadSingle(); // normal.y
                    vertices[offset + 5] = reader.ReadSingle(); // normal.z
                    vertices[offset + 6] = reader.ReadSingle(); // uv.x
                    vertices[offset + 7] = reader.ReadSingle(); // uv.y
                    vertices[offset + 8] = reader.ReadSingle(); // tangent.x
                    vertices[offset + 9] = reader.ReadSingle(); // tangent.y
                    vertices[offset + 10] = reader.ReadSingle(); // tangent.z
                    vertices[offset + 11] = reader.ReadSingle(); // bitangent.x
                    vertices[offset + 12] = reader.ReadSingle(); // bitangent.y
                    vertices[offset + 13] = reader.ReadSingle(); // bitangent.z
                }

                // Read indices
                int indexCount = reader.ReadInt32();
                var indices = new uint[indexCount];
                for (int idx = 0; idx < indexCount; idx++)
                {
                    indices[idx] = reader.ReadUInt32();
                }

                // Upload to GPU
                var meshId = UploadMeshToGPU(vertices, indices);
                meshIds.Add(meshId);
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
            using var fs = File.OpenRead(asset.DataFile);
            using var reader = new BinaryReader(fs);

            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            int components = reader.ReadInt32();
            int dataLength = reader.ReadInt32();
            byte[] data = reader.ReadBytes(dataLength);

            // Upload to GPU
            var textureId = UploadTextureToGPU(width, height, data, srgb);

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
