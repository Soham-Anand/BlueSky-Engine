// Bridge between GLTF importer and BlueSky Engine mesh/animation systems
// Converts GLTF data to engine-native formats with zero allocations where possible

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BSVec2 = BlueSky.Core.Math.Vector2;
using BSVec3 = BlueSky.Core.Math.Vector3;
using BSVec4 = BlueSky.Core.Math.Vector4;
using BSMat4 = BlueSky.Core.Math.Matrix4x4;
using BSQuat = BlueSky.Core.Math.Quaternion;

namespace BlueSky.Animation.GLTF;

public static class GltfToEngineBridge
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SkeletalMesh ImportSkeletalMesh(string filePath)
    {
        var importer = GltfImporter.FromFile(filePath);
        var root = importer.Root;
        
        if (root.Meshes == null || root.Meshes.Length == 0)
            throw new GltfException("No meshes found in GLTF file");
        
        var mesh = new SkeletalMesh();
        var gltfMeshData = importer.ExtractMesh(0);
        var prim = gltfMeshData.Primitives[0];
        
        if (prim.Positions == null) 
            throw new GltfException("Mesh has no position data");
        
        int vertexCount = prim.Positions.Length;
        var vertices = new SkeletalVertex[vertexCount];
        
        for (int v = 0; v < vertexCount; v++)
        {
            vertices[v].Position = prim.Positions[v];
            
            if (prim.Normals != null && v < prim.Normals.Length)
                vertices[v].Normal = prim.Normals[v];
            
            if (prim.TexCoords0 != null && v < prim.TexCoords0.Length)
                vertices[v].TexCoord = new Vector2(prim.TexCoords0[v].X, prim.TexCoords0[v].Y);
            
            if (prim.Tangents != null && v < prim.Tangents.Length)
                vertices[v].Tangent = new Vector3(prim.Tangents[v].X, prim.Tangents[v].Y, prim.Tangents[v].Z);
            
            if (prim.Joints != null && v < prim.Joints.Length)
            {
                vertices[v].BoneIndex0 = prim.Joints[v][0];
                vertices[v].BoneIndex1 = prim.Joints[v][1];
                vertices[v].BoneIndex2 = prim.Joints[v][2];
                vertices[v].BoneIndex3 = prim.Joints[v][3];
            }
            
            if (prim.Weights != null && v < prim.Weights.Length)
            {
                vertices[v].BoneWeight0 = prim.Weights[v].X;
                vertices[v].BoneWeight1 = prim.Weights[v].Y;
                vertices[v].BoneWeight2 = prim.Weights[v].Z;
                vertices[v].BoneWeight3 = prim.Weights[v].W;
            }
        }
        
        mesh.Vertices = vertices;
        mesh.Indices = prim.Indices ?? GenerateSequentialIndices(vertexCount);
        
        if (root.Skins != null && root.Skins.Length > 0)
        {
            var skin = root.Skins[0];
            mesh.Bones = new Bone[skin.Joints.Length];
            
            Matrix4x4[]? inverseBindMatrices = null;
            if (skin.InverseBindMatrices.HasValue)
                inverseBindMatrices = importer.ExtractMatrix4Array(skin.InverseBindMatrices.Value);
            
            for (int i = 0; i < skin.Joints.Length; i++)
            {
                int nodeIndex = skin.Joints[i];
                string boneName = root.Nodes?[nodeIndex].Name ?? $"Bone_{i}";
                
                mesh.Bones[i] = new Bone
                {
                    Name = boneName,
                    ParentIndex = -1,
                    InverseBindPose = inverseBindMatrices?[i] ?? Matrix4x4.Identity
                };
                
                mesh.BoneNameToIndex[boneName] = i;
            }
        }
        
        return mesh;
    }
    
    // TODO: Implement animation import once AnimationClip structure is finalized
    /*
    public static AnimationClip ImportAnimation(string filePath, int animationIndex = 0)
    {
        // Animation import requires understanding the engine's AnimationClip structure
        throw new NotImplementedException("Animation import not yet implemented");
    }
    
    public static List<AnimationClip> ImportAllAnimations(string filePath)
    {
        throw new NotImplementedException("Animation import not yet implemented");
    }
    */
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] ExtractTexture(string filePath, int imageIndex)
    {
        var importer = GltfImporter.FromFile(filePath);
        return importer.ExtractImage(imageIndex);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Dictionary<string, byte[]> ExtractAllTextures(string filePath)
    {
        var importer = GltfImporter.FromFile(filePath);
        var root = importer.Root;
        var textures = new Dictionary<string, byte[]>();
        
        if (root.Images != null)
        {
            for (int i = 0; i < root.Images.Length; i++)
            {
                var image = root.Images[i];
                string name = image.Name ?? $"Texture_{i}";
                textures[name] = importer.ExtractImage(i);
            }
        }
        
        return textures;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MaterialData ExtractMaterial(GltfImporter importer, int materialIndex)
    {
        var root = importer.Root;
        
        if (root.Materials == null || materialIndex >= root.Materials.Length)
            return MaterialData.Default;
        
        var gltfMat = root.Materials[materialIndex];
        var matData = new MaterialData
        {
            Name = gltfMat.Name ?? $"Material_{materialIndex}",
            AlphaMode = gltfMat.AlphaMode,
            AlphaCutoff = gltfMat.AlphaCutoff,
            DoubleSided = gltfMat.DoubleSided
        };
        
        if (gltfMat.PbrMetallicRoughness != null)
        {
            var pbr = gltfMat.PbrMetallicRoughness;
            
            if (pbr.BaseColorFactor != null && pbr.BaseColorFactor.Length >= 4)
            {
                matData.BaseColor = new Core.Math.Vector4(
                    pbr.BaseColorFactor[0],
                    pbr.BaseColorFactor[1],
                    pbr.BaseColorFactor[2],
                    pbr.BaseColorFactor[3]
                );
            }
            
            matData.MetallicFactor = pbr.MetallicFactor;
            matData.RoughnessFactor = pbr.RoughnessFactor;
            
            if (pbr.BaseColorTexture != null)
                matData.BaseColorTextureIndex = pbr.BaseColorTexture.Index;
            
            if (pbr.MetallicRoughnessTexture != null)
                matData.MetallicRoughnessTextureIndex = pbr.MetallicRoughnessTexture.Index;
        }
        
        if (gltfMat.NormalTexture != null)
        {
            matData.NormalTextureIndex = gltfMat.NormalTexture.Index;
            matData.NormalScale = gltfMat.NormalTexture.Scale;
        }
        
        if (gltfMat.OcclusionTexture != null)
        {
            matData.OcclusionTextureIndex = gltfMat.OcclusionTexture.Index;
            matData.OcclusionStrength = gltfMat.OcclusionTexture.Strength;
        }
        
        if (gltfMat.EmissiveTexture != null)
            matData.EmissiveTextureIndex = gltfMat.EmissiveTexture.Index;
        
        if (gltfMat.EmissiveFactor != null && gltfMat.EmissiveFactor.Length >= 3)
        {
            matData.EmissiveFactor = new Core.Math.Vector3(
                gltfMat.EmissiveFactor[0],
                gltfMat.EmissiveFactor[1],
                gltfMat.EmissiveFactor[2]
            );
        }
        
        return matData;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BSVec2 ToEngineVec2(Vector2 v)
    {
        return new BSVec2(v.X, v.Y);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BSVec3 ToEngineVec3(Vector3 v)
    {
        return new BSVec3(v.X, v.Y, v.Z);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BSVec4 ToEngineVec4(Vector4 v)
    {
        return new BSVec4(v.X, v.Y, v.Z, v.W);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BSMat4 ToEngineMat4(Matrix4x4 m)
    {
        return new BSMat4(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44
        );
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint[] GenerateSequentialIndices(int count)
    {
        var indices = new uint[count];
        for (int i = 0; i < count; i++)
            indices[i] = (uint)i;
        return indices;
    }
}

public struct MaterialData
{
    public string Name;
    public BSVec4 BaseColor;
    public float MetallicFactor;
    public float RoughnessFactor;
    public BSVec3 EmissiveFactor;
    public float NormalScale;
    public float OcclusionStrength;
    public string AlphaMode;
    public float AlphaCutoff;
    public bool DoubleSided;
    
    public int BaseColorTextureIndex;
    public int MetallicRoughnessTextureIndex;
    public int NormalTextureIndex;
    public int OcclusionTextureIndex;
    public int EmissiveTextureIndex;
    
    public static MaterialData Default => new MaterialData
    {
        Name = "Default",
        BaseColor = new BSVec4(1, 1, 1, 1),
        MetallicFactor = 0f,
        RoughnessFactor = 1f,
        EmissiveFactor = new BSVec3(0, 0, 0),
        NormalScale = 1f,
        OcclusionStrength = 1f,
        AlphaMode = "OPAQUE",
        AlphaCutoff = 0.5f,
        DoubleSided = false,
        BaseColorTextureIndex = -1,
        MetallicRoughnessTextureIndex = -1,
        NormalTextureIndex = -1,
        OcclusionTextureIndex = -1,
        EmissiveTextureIndex = -1
    };
}

public struct AnimationTrack
{
    public string BoneName;
    public AnimationKey<BSVec3>[]? PositionKeys;
    public AnimationKey<BSQuat>[]? RotationKeys;
    public AnimationKey<BSVec3>[]? ScaleKeys;
}

public struct AnimationKey<T> where T : unmanaged
{
    public float Time;
    public T Value;
}
