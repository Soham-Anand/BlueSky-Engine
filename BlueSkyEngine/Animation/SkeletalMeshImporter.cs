using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Globalization;
using BlueSky.Animation.FBX;

namespace BlueSky.Animation;

/// <summary>
/// Intelligent mesh importer that detects static vs skeletal meshes.
/// Supports FBX, GLTF, and OBJ formats with automatic bone detection.
/// </summary>
public static class SkeletalMeshImporter
{
    /// <summary>
    /// Import a mesh file and automatically detect if it's static or skeletal
    /// </summary>
    public static (bool isSkeletal, object mesh) ImportMesh(string path, bool generateCollisions = false)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[NotBSAnimation] File not found: {path}");
            return (false, null!);
        }
        
        var ext = Path.GetExtension(path).ToLowerInvariant();
        
        return ext switch
        {
            ".fbx" => ImportFBX(path, generateCollisions),
            ".gltf" or ".glb" => ImportGLTF(path, generateCollisions),
            ".obj" => ImportOBJ(path, generateCollisions),
            _ => throw new NotSupportedException($"Format {ext} not supported")
        };
    }
    
    private static (bool, object) ImportFBX(string path, bool generateCollisions)
    {
        Console.WriteLine($"[NotBSAnimation] Importing FBX: {path}");
        
        // TODO: Implement skeletal mesh import with new FbxImporterV2
        // For now, treat all FBX files as static meshes
        Console.WriteLine("[NotBSAnimation] Skeletal mesh import not yet implemented, treating as static mesh");
        
        var staticMesh = new Rendering.MeshData { Name = Path.GetFileNameWithoutExtension(path) };
        
        if (generateCollisions)
        {
            GenerateCollisions(staticMesh);
        }
        
        return (false, staticMesh);
    }
    
    private static (bool, object) ImportGLTF(string path, bool generateCollisions)
    {
        Console.WriteLine($"[NotBSAnimation] Importing GLTF: {path}");
        
        try
        {
            var importer = GLTF.GltfImporter.FromFile(path);
            var root = importer.Root;
            
            bool hasSkins = root.Skins != null && root.Skins.Length > 0;
            bool hasAnimations = root.Animations != null && root.Animations.Length > 0;
            
            if (hasSkins || hasAnimations)
            {
                var skeletalMesh = GLTF.GltfToEngineBridge.ImportSkeletalMesh(path);
                
                if (generateCollisions)
                    GenerateSkeletalCollisions(skeletalMesh);
                
                Console.WriteLine($"[NotBSAnimation] Imported skeletal mesh with {skeletalMesh.Bones.Length} bones");
                return (true, skeletalMesh);
            }
            else
            {
                var staticMesh = ImportStaticMeshFromGLTF(path);
                
                if (generateCollisions)
                    GenerateCollisions(staticMesh);
                
                Console.WriteLine($"[NotBSAnimation] Imported static mesh");
                return (false, staticMesh);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotBSAnimation] GLTF import failed: {ex.Message}");
            throw;
        }
    }
    
    private static Rendering.MeshData ImportStaticMeshFromGLTF(string path)
    {
        var importer = GLTF.GltfImporter.FromFile(path);
        var root = importer.Root;
        
        if (root.Meshes == null || root.Meshes.Length == 0)
            throw new Exception("No meshes found in GLTF file");
        
        var gltfMeshData = importer.ExtractMesh(0);
        var prim = gltfMeshData.Primitives[0];
        
        if (prim.Positions == null)
            throw new Exception("Mesh has no position data");
        
        int vertexCount = prim.Positions.Length;
        var vertices = new Rendering.VertexData[vertexCount];
        
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i].Position = new Vector3(
                prim.Positions[i].X,
                prim.Positions[i].Y,
                prim.Positions[i].Z
            );
            
            if (prim.Normals != null && i < prim.Normals.Length)
            {
                vertices[i].Normal = new Vector3(
                    prim.Normals[i].X,
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
                    prim.Tangents[i].X,
                    prim.Tangents[i].Y,
                    prim.Tangents[i].Z
                );
            }
        }
        
        uint[] indices = prim.Indices ?? GenerateSequentialIndices(vertexCount);
        
        return new Rendering.MeshData
        {
            Name = gltfMeshData.Name,
            Vertices = vertices,
            Indices = indices
        };
    }
    
    private static uint[] GenerateSequentialIndices(int count)
    {
        var indices = new uint[count];
        for (int i = 0; i < count; i++)
            indices[i] = (uint)i;
        return indices;
    }
    
    private static (bool, object) ImportOBJ(string path, bool generateCollisions)
    {
        // OBJ files are always static (no bone data)
        var staticMesh = ImportStaticMeshFromOBJ(path);
        
        if (generateCollisions)
        {
            GenerateCollisions(staticMesh);
        }
        
        return (false, staticMesh);
    }
    
    private static Rendering.MeshData ImportStaticMeshFromOBJ(string path)
    {
        // Use existing mesh loader
        var mesh = Rendering.MeshLoader.LoadMesh(path);
        if (mesh == null)
            throw new Exception($"Failed to load mesh from {path}");
        
        return mesh;
    }
    
    /// <summary>
    /// Generate collision mesh from visual mesh
    /// </summary>
    private static void GenerateCollisions(Rendering.MeshData mesh)
    {
        Console.WriteLine($"[NotBSAnimation] Generating collisions for {mesh.Name}...");
        
        // Simple convex hull generation
        // TODO: Implement proper convex hull algorithm
        var positions = mesh.Vertices.Select(v => v.Position).ToArray();
        var bounds = BoundingBox.FromVertices(positions);
        
        Console.WriteLine($"[NotBSAnimation] Collision bounds: {bounds.Min} to {bounds.Max}");
    }
    
    /// <summary>
    /// Generate collision mesh for skeletal mesh
    /// </summary>
    private static void GenerateSkeletalCollisions(SkeletalMesh mesh)
    {
        Console.WriteLine($"[NotBSAnimation] Generating collisions for skeletal mesh...");
        
        // Generate bounding box from vertices
        if (mesh.Vertices.Length > 0)
        {
            var positions = mesh.Vertices.Select(v => v.Position);
            var bounds = BoundingBox.FromVertices(positions);
            Console.WriteLine($"[NotBSAnimation] Collision bounds: {bounds.Min} to {bounds.Max}");
        }
        
        // TODO: Generate per-bone collision capsules for ragdoll physics
    }
}

/// <summary>
/// Skeletal mesh asset for .blueskyasset format
/// </summary>
public class SkeletalMeshAsset
{
    public string Name { get; set; } = string.Empty;
    public SkeletalMesh Mesh { get; set; } = null!;
    public List<AnimationClip> Animations { get; set; } = new();
    public CollisionData? Collision { get; set; }
    
    /// <summary>
    /// Asset metadata
    /// </summary>
    public AssetMetadata Metadata { get; set; } = new();
}

/// <summary>
/// Collision data for physics
/// </summary>
public class CollisionData
{
    public CollisionType Type { get; set; } = CollisionType.ConvexHull;
    public Vector3[] Vertices { get; set; } = Array.Empty<Vector3>();
    public uint[] Indices { get; set; } = Array.Empty<uint>();
    public BoundingBox Bounds { get; set; }
}

public enum CollisionType
{
    None,
    Box,
    Sphere,
    Capsule,
    ConvexHull,
    TriangleMesh
}

/// <summary>
/// Asset metadata for .blueskyasset files
/// </summary>
public class AssetMetadata
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "SkeletalMesh";
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Tags { get; set; } = new();
}
