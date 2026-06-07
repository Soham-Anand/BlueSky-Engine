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
        
        // CRITICAL FIX: Collect ALL meshes and ALL primitives, not just [0]
        var allVertices = new List<SkeletalVertex>();
        var allIndices = new List<uint>();
        var submeshInfo = new List<SubmeshData>();
        
        // Loop through ALL meshes in the glTF file (body + wheels)
        for (int meshIdx = 0; meshIdx < root.Meshes.Length; meshIdx++)
        {
            var gltfMeshData = importer.ExtractMesh(meshIdx);
            
            // Loop through ALL primitives in this mesh
            foreach (var prim in gltfMeshData.Primitives)
            {
                if (prim.Positions == null) 
                    continue; // Skip invalid primitives
                
                int vertexCount = prim.Positions.Length;
                uint baseVertex = (uint)allVertices.Count;
                uint baseIndex = (uint)allIndices.Count;
                
                // Add vertices from this primitive
                for (int v = 0; v < vertexCount; v++)
                {
                    var vertex = new SkeletalVertex();
                    vertex.Position = prim.Positions[v];
                    
                    if (prim.Normals != null && v < prim.Normals.Length)
                        vertex.Normal = prim.Normals[v];
                    
                    if (prim.TexCoords0 != null && v < prim.TexCoords0.Length)
                        vertex.TexCoord = new Vector2(prim.TexCoords0[v].X, prim.TexCoords0[v].Y);
                    
                    if (prim.Tangents != null && v < prim.Tangents.Length)
                        vertex.Tangent = new Vector3(prim.Tangents[v].X, prim.Tangents[v].Y, prim.Tangents[v].Z);
                    
                    if (prim.Joints != null && v < prim.Joints.Length)
                    {
                        vertex.BoneIndex0 = prim.Joints[v][0];
                        vertex.BoneIndex1 = prim.Joints[v][1];
                        vertex.BoneIndex2 = prim.Joints[v][2];
                        vertex.BoneIndex3 = prim.Joints[v][3];
                    }
                    
                    if (prim.Weights != null && v < prim.Weights.Length)
                    {
                        vertex.BoneWeight0 = prim.Weights[v].X;
                        vertex.BoneWeight1 = prim.Weights[v].Y;
                        vertex.BoneWeight2 = prim.Weights[v].Z;
                        vertex.BoneWeight3 = prim.Weights[v].W;
                    }
                    
                    allVertices.Add(vertex);
                }
                
                // Add indices from this primitive (offset by baseVertex)
                var primIndices = prim.Indices ?? GenerateSequentialIndices(vertexCount);
                foreach (var idx in primIndices)
                {
                    allIndices.Add(baseVertex + idx);
                }
                
                // Track submesh boundaries
                submeshInfo.Add(new SubmeshData
                {
                    MeshName = gltfMeshData.Name,
                    IndexOffset = (int)baseIndex,
                    IndexCount = primIndices.Length,
                    MaterialIndex = prim.MaterialIndex ?? -1
                });
            }
        }
        
        // Assign combined data to SkeletalMesh
        mesh.Vertices = allVertices.ToArray();
        mesh.Indices = allIndices.ToArray();
        mesh.SubmeshData = submeshInfo; // Store for debugging
        
        // CRITICAL FIX: Extract and store materials from GLTF so colors render properly
        if (root.Materials != null && root.Materials.Length > 0)
        {
            mesh.Materials = new MaterialData[root.Materials.Length];
            for (int i = 0; i < root.Materials.Length; i++)
            {
                mesh.Materials[i] = ExtractMaterial(importer, i);
            }
            Console.WriteLine($"[GLTF Bridge] ✅ Extracted {mesh.Materials.Length} materials with colors");
        }
        
        if (root.Skins != null && root.Skins.Length > 0)
        {
            var skin = root.Skins[0];
            mesh.Bones = new Bone[skin.Joints.Length];
            mesh.RootBoneIndex = 0;
            
            Matrix4x4[]? inverseBindMatrices = null;
            if (skin.InverseBindMatrices.HasValue)
                inverseBindMatrices = importer.ExtractMatrix4Array(skin.InverseBindMatrices.Value);

            var jointNodeToBoneIndex = new Dictionary<int, int>();
            for (int i = 0; i < skin.Joints.Length; i++)
            {
                jointNodeToBoneIndex[skin.Joints[i]] = i;
            }

            var nodeParent = BuildNodeParentMap(root);
            
            for (int i = 0; i < skin.Joints.Length; i++)
            {
                int nodeIndex = skin.Joints[i];
                var node = root.Nodes != null && nodeIndex >= 0 && nodeIndex < root.Nodes.Length
                    ? root.Nodes[nodeIndex]
                    : null;
                string boneName = node?.Name ?? $"Bone_{i}";

                int parentIndex = -1;
                if (nodeParent.TryGetValue(nodeIndex, out int parentNodeIndex) &&
                    jointNodeToBoneIndex.TryGetValue(parentNodeIndex, out int parentBoneIndex))
                {
                    parentIndex = parentBoneIndex;
                }
                
                mesh.Bones[i] = new Bone
                {
                    Name = boneName,
                    ParentIndex = parentIndex,
                    LocalBindPose = node != null ? GetNodeLocalTransform(node) : Matrix4x4.Identity,
                    InverseBindPose = inverseBindMatrices?[i] ?? Matrix4x4.Identity
                };
                
                mesh.BoneNameToIndex[boneName] = i;
            }

            for (int i = 0; i < mesh.Bones.Length; i++)
            {
                int parentIndex = mesh.Bones[i].ParentIndex;
                if (parentIndex >= 0 && parentIndex < mesh.Bones.Length)
                    mesh.Bones[parentIndex].Children.Add(i);
            }

            if (skin.Skeleton.HasValue && jointNodeToBoneIndex.TryGetValue(skin.Skeleton.Value, out int rootBoneIndex))
            {
                mesh.RootBoneIndex = rootBoneIndex;
            }
            else
            {
                for (int i = 0; i < mesh.Bones.Length; i++)
                {
                    if (mesh.Bones[i].ParentIndex < 0)
                    {
                        mesh.RootBoneIndex = i;
                        break;
                    }
                }
            }
        }
        
        return mesh;
    }

    private static Dictionary<int, int> BuildNodeParentMap(GltfRoot root)
    {
        var parentMap = new Dictionary<int, int>();
        if (root.Nodes == null) return parentMap;

        for (int parentIndex = 0; parentIndex < root.Nodes.Length; parentIndex++)
        {
            var children = root.Nodes[parentIndex].Children;
            if (children == null) continue;

            foreach (int childIndex in children)
            {
                parentMap[childIndex] = parentIndex;
            }
        }

        return parentMap;
    }

    private static Matrix4x4 GetNodeLocalTransform(GltfNode node)
    {
        if (node.Matrix != null && node.Matrix.Length == 16)
        {
            return new Matrix4x4(
                node.Matrix[0], node.Matrix[1], node.Matrix[2], node.Matrix[3],
                node.Matrix[4], node.Matrix[5], node.Matrix[6], node.Matrix[7],
                node.Matrix[8], node.Matrix[9], node.Matrix[10], node.Matrix[11],
                node.Matrix[12], node.Matrix[13], node.Matrix[14], node.Matrix[15]
            );
        }

        var translation = (node.Translation != null && node.Translation.Length >= 3)
            ? new Vector3(node.Translation[0], node.Translation[1], node.Translation[2])
            : Vector3.Zero;

        var rotation = (node.Rotation != null && node.Rotation.Length >= 4)
            ? new Quaternion(node.Rotation[0], node.Rotation[1], node.Rotation[2], node.Rotation[3])
            : Quaternion.Identity;

        var scale = (node.Scale != null && node.Scale.Length >= 3)
            ? new Vector3(node.Scale[0], node.Scale[1], node.Scale[2])
            : Vector3.One;

        return Matrix4x4.CreateScale(scale) *
               Matrix4x4.CreateFromQuaternion(rotation) *
               Matrix4x4.CreateTranslation(translation);
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
            DoubleSided = gltfMat.DoubleSided,
            // CRITICAL: Initialize to -1 (no texture). Struct default is 0, which
            // is a valid GLTF texture index and would silently bind the wrong texture
            // to materials that have no textures assigned.
            BaseColorTextureIndex = -1,
            MetallicRoughnessTextureIndex = -1,
            NormalTextureIndex = -1,
            OcclusionTextureIndex = -1,
            EmissiveTextureIndex = -1
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

public struct SubmeshData
{
    public string MeshName;
    public int IndexOffset;
    public int IndexCount;
    public int MaterialIndex;
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
