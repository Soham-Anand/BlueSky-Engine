using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Animation;

/// <summary>
/// Skeletal mesh with bone hierarchy and skinning data.
/// Supports up to 4 bone influences per vertex for optimal performance.
/// </summary>
public class SkeletalMesh
{
    public string Name { get; set; } = string.Empty;
    public SkeletalVertex[] Vertices { get; set; } = Array.Empty<SkeletalVertex>();
    public uint[] Indices { get; set; } = Array.Empty<uint>();
    public Bone[] Bones { get; set; } = Array.Empty<Bone>();
    public Dictionary<string, int> BoneNameToIndex { get; set; } = new();
    
    /// <summary>
    /// Root bone index (usually 0, but can be different for sub-meshes)
    /// </summary>
    public int RootBoneIndex { get; set; } = 0;
    
    /// <summary>
    /// Bounding box for culling (in bind pose)
    /// </summary>
    public BoundingBox Bounds { get; set; }
    
    /// <summary>
    /// Submesh data for multi-mesh imports (e.g., car body + 4 wheels)
    /// Used for debugging and bone detection per submesh
    /// </summary>
    public List<GLTF.SubmeshData>? SubmeshData { get; set; }
    
    /// <summary>
    /// Material data extracted from GLTF/FBX (colors, textures, PBR properties)
    /// Array indexed by material index from the import
    /// </summary>
    public GLTF.MaterialData[]? Materials { get; set; }
}

/// <summary>
/// Vertex with skeletal skinning data.
/// Uses 4 bone influences for smooth deformation.
/// </summary>
public struct SkeletalVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 TexCoord;
    public Vector3 Tangent;
    
    // Skinning data (4 influences max)
    public int BoneIndex0;
    public int BoneIndex1;
    public int BoneIndex2;
    public int BoneIndex3;
    
    public float BoneWeight0;
    public float BoneWeight1;
    public float BoneWeight2;
    public float BoneWeight3;
    
    /// <summary>
    /// Normalize bone weights to sum to 1.0
    /// </summary>
    public void NormalizeWeights()
    {
        float sum = BoneWeight0 + BoneWeight1 + BoneWeight2 + BoneWeight3;
        if (sum > 0.0001f)
        {
            float invSum = 1.0f / sum;
            BoneWeight0 *= invSum;
            BoneWeight1 *= invSum;
            BoneWeight2 *= invSum;
            BoneWeight3 *= invSum;
        }
    }
}

/// <summary>
/// Bone in the skeleton hierarchy.
/// Stores bind pose and parent relationship.
/// </summary>
public class Bone
{
    public string Name { get; set; } = string.Empty;
    public int ParentIndex { get; set; } = -1; // -1 = root bone
    
    /// <summary>
    /// Local transform relative to parent bone
    /// </summary>
    public Matrix4x4 LocalBindPose { get; set; } = Matrix4x4.Identity;
    
    /// <summary>
    /// Inverse bind pose matrix (transforms from mesh space to bone space)
    /// </summary>
    public Matrix4x4 InverseBindPose { get; set; } = Matrix4x4.Identity;
    
    /// <summary>
    /// Children bone indices for hierarchy traversal
    /// </summary>
    public List<int> Children { get; set; } = new();
}

/// <summary>
/// Simple bounding box for culling
/// </summary>
public struct BoundingBox
{
    public Vector3 Min;
    public Vector3 Max;
    
    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Extents => (Max - Min) * 0.5f;
    
    public static BoundingBox FromVertices(IEnumerable<Vector3> positions)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        
        foreach (var pos in positions)
        {
            min = Vector3.Min(min, pos);
            max = Vector3.Max(max, pos);
        }
        
        return new BoundingBox { Min = min, Max = max };
    }
}
