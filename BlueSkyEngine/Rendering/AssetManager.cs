using System.Numerics;
using BlueSky.Core.Assets;
namespace BlueSky.Rendering;

/// <summary>
/// High-level asset manager that handles loading and caching of all game assets.
/// Supports both legacy loading (direct from source files) and new .blueasset pipeline.
/// </summary>
public class AssetManager : IDisposable
{
    private readonly IRenderer _renderer;
    private readonly AssetRegistry _registry;
    private readonly TextureLoader _textureLoader;
    private readonly AssetLoader _assetLoader;
    private readonly Dictionary<string, int> _meshCache = new();
    private readonly Dictionary<string, int> _textureCache = new();
    
    private int _whiteTexture;
    private int _normalTexture;
    private bool _disposed;

    public AssetManager(IRenderer renderer)
    {
        _renderer = renderer;
        _registry = new AssetRegistry();
        _textureLoader = new TextureLoader(renderer);
        _assetLoader = new AssetLoader(renderer);
        
        // Create default textures
        _whiteTexture = _textureLoader.CreateWhiteTexture();
        _normalTexture = _textureLoader.CreateNormalTexture();
        
        Console.WriteLine("[AssetManager] Initialized (supports .blueasset pipeline)");
    }

    public int WhiteTexture => _whiteTexture;
    public int NormalTexture => _normalTexture;

    /// <summary>
    /// Load a 3D model from file. Supports both .blueasset and source files (FBX, OBJ, GLTF, GLB).
    /// Returns the first mesh ID for convenience.
    /// </summary>
    public int LoadModel(string path, out ModelData? modelData)
    {
        // Check if this is a .blueasset file
        if (path.EndsWith(".blueasset"))
        {
            return LoadModelFromAsset(path, out modelData);
        }

        // Check if this is GLTF/GLB - use new importer
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".gltf" || ext == ".glb")
        {
            return LoadModelFromGLTF(path, out modelData);
        }

        // Legacy loading from source file (FBX, OBJ)
        modelData = MeshLoader.LoadModel(path);
        if (modelData == null || modelData.Meshes.Count == 0)
        {
            Console.WriteLine($"[AssetManager] Failed to load model: {path}");
            return 0;
        }

        // Upload all meshes to GPU
        int firstMeshId = 0;
        foreach (var mesh in modelData.Meshes)
        {
            var meshId = UploadMesh(mesh);
            if (firstMeshId == 0) firstMeshId = meshId;
            
            var meshKey = $"{path}:{mesh.Name}";
            _meshCache[meshKey] = meshId;
        }

        // Load all textures
        foreach (var material in modelData.Materials)
        {
            if (!string.IsNullOrEmpty(material.AlbedoTexture))
            {
                LoadTexture(material.AlbedoTexture, true);
            }
            if (!string.IsNullOrEmpty(material.NormalTexture))
            {
                LoadTexture(material.NormalTexture, false);
            }
            if (!string.IsNullOrEmpty(material.MetallicRoughnessTexture))
            {
                LoadTexture(material.MetallicRoughnessTexture, false);
            }
        }

        Console.WriteLine($"[AssetManager] Loaded model '{path}' with {modelData.Meshes.Count} meshes");
        return firstMeshId;
    }
    
    /// <summary>
    /// Load a model from GLTF/GLB file with full material and texture support.
    /// </summary>
    private int LoadModelFromGLTF(string path, out ModelData? modelData)
    {
        if (_meshCache.TryGetValue(path, out var cachedId))
        {
            modelData = null;
            return cachedId;
        }

        try
        {
            var importer = Animation.GLTF.GltfImporter.FromFile(path);
            var root = importer.Root;
            
            modelData = new ModelData();
            
            // Extract all meshes
            if (root.Meshes != null)
            {
                for (int i = 0; i < root.Meshes.Length; i++)
                {
                    var gltfMeshData = importer.ExtractMesh(i);
                    
                    foreach (var prim in gltfMeshData.Primitives)
                    {
                        if (prim.Positions == null) continue;
                        
                        var mesh = ConvertGltfPrimitiveToMeshData(gltfMeshData.Name, prim);
                        modelData.Meshes.Add(mesh);
                    }
                }
            }
            
            // Extract all materials
            if (root.Materials != null)
            {
                for (int i = 0; i < root.Materials.Length; i++)
                {
                    var matData = Animation.GLTF.GltfToEngineBridge.ExtractMaterial(importer, i);
                    var material = ConvertGltfMaterialToMaterialData(matData);
                    modelData.Materials.Add(material);
                }
            }
            
            // Extract and load all textures
            var textures = Animation.GLTF.GltfToEngineBridge.ExtractAllTextures(path);
            foreach (var kvp in textures)
            {
                string texName = kvp.Key;
                byte[] texData = kvp.Value;
                
                // Save to temp file and load (or implement direct byte[] loading)
                string tempPath = Path.Combine(Path.GetTempPath(), $"{texName}.png");
                File.WriteAllBytes(tempPath, texData);
                LoadTexture(tempPath, true);
            }
            
            // Upload meshes to GPU
            int firstMeshId = 0;
            foreach (var mesh in modelData.Meshes)
            {
                var meshId = UploadMesh(mesh);
                if (firstMeshId == 0) firstMeshId = meshId;
                
                var meshKey = $"{path}:{mesh.Name}";
                _meshCache[meshKey] = meshId;
            }
            
            Console.WriteLine($"[AssetManager] Loaded GLTF model '{path}' with {modelData.Meshes.Count} meshes, {modelData.Materials.Count} materials");
            return firstMeshId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetManager] Failed to load GLTF model: {ex.Message}");
            modelData = null;
            return 0;
        }
    }
    
    private MeshData ConvertGltfPrimitiveToMeshData(string name, Animation.GLTF.GltfPrimitiveData prim)
    {
        int vertexCount = prim.Positions!.Length;
        var vertices = new VertexData[vertexCount];
        
        for (int i = 0; i < vertexCount; i++)
        {
            // COORDINATE SYSTEM FIX: glTF right-handed → engine left-handed
            // Negate X to un-mirror geometry (fixes reversed text, steering wheel side, etc.)
            vertices[i].Position = new Vector3(
                -prim.Positions[i].X,
                prim.Positions[i].Y,
                prim.Positions[i].Z
            );
            
            if (prim.Normals != null && i < prim.Normals.Length)
            {
                vertices[i].Normal = new Vector3(
                    -prim.Normals[i].X,
                    prim.Normals[i].Y,
                    prim.Normals[i].Z
                );
            }
            
            if (prim.TexCoords0 != null && i < prim.TexCoords0.Length)
            {
                vertices[i].TexCoords = new Vector2(
                    prim.TexCoords0[i].X,
                    prim.TexCoords0[i].Y
                );
            }
            
            if (prim.Tangents != null && i < prim.Tangents.Length)
            {
                vertices[i].Tangent = new Vector3(
                    -prim.Tangents[i].X,
                    prim.Tangents[i].Y,
                    prim.Tangents[i].Z
                );
            }
        }
        
        uint[] indices = prim.Indices ?? GenerateSequentialIndices(vertexCount);
        
        // WINDING ORDER FIX: X-negate flips triangle facing — swap idx 1 & 2 per triangle
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            (indices[t + 1], indices[t + 2]) = (indices[t + 2], indices[t + 1]);
        }
        
        return new MeshData
        {
            Name = name,
            Vertices = vertices,
            Indices = indices
        };
    }
    
    private MaterialData ConvertGltfMaterialToMaterialData(Animation.GLTF.MaterialData gltfMat)
    {
        return new MaterialData
        {
            Name = gltfMat.Name,
            Albedo = new Vector3(
                gltfMat.BaseColor.X,
                gltfMat.BaseColor.Y,
                gltfMat.BaseColor.Z
            ),
            Metallic = gltfMat.MetallicFactor,
            Roughness = gltfMat.RoughnessFactor
        };
    }
    
    private uint[] GenerateSequentialIndices(int count)
    {
        var indices = new uint[count];
        for (int i = 0; i < count; i++)
            indices[i] = (uint)i;
        return indices;
    }

    /// <summary>
    /// Load a model from .blueasset file (imported asset).
    /// </summary>
    private int LoadModelFromAsset(string assetPath, out ModelData? modelData)
    {
        modelData = null;

        if (_meshCache.TryGetValue(assetPath, out var cachedId))
        {
            return cachedId;
        }

        var meshIds = _assetLoader.LoadMeshAsset(assetPath);
        if (meshIds.Count == 0)
        {
            return 0;
        }

        // Cache the first mesh
        var firstMeshId = meshIds[0];
        _meshCache[assetPath] = (int)firstMeshId;

        return (int)firstMeshId;
    }

    /// <summary>
    /// Load a single mesh from file.
    /// </summary>
    public int LoadMesh(string path)
    {
        if (_meshCache.TryGetValue(path, out var cachedId))
        {
            return cachedId;
        }

        var meshData = MeshLoader.LoadMesh(path);
        if (meshData == null)
        {
            Console.WriteLine($"[AssetManager] Failed to load mesh: {path}");
            return 0;
        }

        var meshId = UploadMesh(meshData);
        _meshCache[path] = meshId;
        return meshId;
    }

    /// <summary>
    /// Load a texture from file. Supports both .blueasset and source files (PNG, JPG, etc.).
    /// </summary>
    public int LoadTexture(string path, bool srgb = true)
    {
        if (_textureCache.TryGetValue(path, out var cachedId))
        {
            return cachedId;
        }

        int textureId;

        // Check if this is a .blueasset file
        if (path.EndsWith(".blueasset"))
        {
            textureId = _assetLoader.LoadTextureAsset(path, srgb);
        }
        else
        {
            // Legacy loading from source file
            textureId = _textureLoader.LoadTexture(path, srgb);
        }

        if (textureId != 0)
        {
            _textureCache[path] = textureId;
        }
        
        return textureId;
    }

    /// <summary>
    /// Get a cached mesh ID by path.
    /// </summary>
    public int? GetMesh(string path)
    {
        return _meshCache.TryGetValue(path, out var id) ? id : null;
    }

    /// <summary>
    /// Get a cached texture ID by path.
    /// </summary>
    public int? GetTexture(string path)
    {
        return _textureCache.TryGetValue(path, out var id) ? id : null;
    }

    private int UploadMesh(MeshData meshData)
    {
        // Convert VertexData to flat float array
        var vertexCount = meshData.Vertices.Length;
        var floatsPerVertex = 14; // pos(3) + normal(3) + uv(2) + tangent(3) + bitangent(3)
        var vertices = new float[vertexCount * floatsPerVertex];

        for (int i = 0; i < vertexCount; i++)
        {
            var v = meshData.Vertices[i];
            var offset = i * floatsPerVertex;
            
            vertices[offset + 0] = v.Position.X;
            vertices[offset + 1] = v.Position.Y;
            vertices[offset + 2] = v.Position.Z;
            
            vertices[offset + 3] = v.Normal.X;
            vertices[offset + 4] = v.Normal.Y;
            vertices[offset + 5] = v.Normal.Z;
            
            vertices[offset + 6] = v.TexCoords.X;
            vertices[offset + 7] = v.TexCoords.Y;

            vertices[offset + 8] = v.Tangent.X;
            vertices[offset + 9] = v.Tangent.Y;
            vertices[offset + 10] = v.Tangent.Z;

            vertices[offset + 11] = v.Bitangent.X;
            vertices[offset + 12] = v.Bitangent.Y;
            vertices[offset + 13] = v.Bitangent.Z;
        }

        return _renderer.CreateMesh(vertices, meshData.Indices);
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Cleanup textures
        foreach (var textureId in _textureCache.Values)
        {
            _renderer.DeleteResource(ResourceType.Texture, textureId);
        }
        _renderer.DeleteResource(ResourceType.Texture, _whiteTexture);
        _renderer.DeleteResource(ResourceType.Texture, _normalTexture);

        // Cleanup meshes
        foreach (var meshId in _meshCache.Values)
        {
            _renderer.DeleteMesh(meshId);
        }

        _registry.Clear();
        _disposed = true;
        
        Console.WriteLine("[AssetManager] Disposed");
    }
}
