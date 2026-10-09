using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using BlueSky.Rendering.Strata;

namespace BlueSky.Motif;

/// <summary>Loads rigged meshes from the engine's StrataPack interchange format.</summary>
public static class SkeletalMeshImporter
{
    public static (bool isSkeletal, object mesh) ImportMesh(string path, bool generateCollisions = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return (false, null!);

        var pack = StrataPack.Decode(File.ReadAllBytes(path));
        int skeletalMeshIndex = pack.Header.Meshes.FindIndex(mesh => mesh.SkeletonIndex >= 0);
        if (skeletalMeshIndex < 0)
            return (false, null!);

        var sourceMesh = pack.Header.Meshes[skeletalMeshIndex];
        int skeletonIndex = sourceMesh.SkeletonIndex;
        var sourceSkeleton = pack.Header.Skeletons[skeletonIndex];
        var bones = CreateBones(sourceSkeleton);
        var boneNameToIndex = new Dictionary<string, int>(bones.Length, StringComparer.Ordinal);
        for (int i = 0; i < bones.Length; i++)
            boneNameToIndex.Add(bones[i].Name, i);

        int vertexCount = 0;
        int indexCount = 0;
        foreach (var mesh in pack.Header.Meshes)
        {
            if (mesh.SkeletonIndex != skeletonIndex) continue;
            vertexCount = checked(vertexCount + mesh.VertexCount);
            indexCount = checked(indexCount + mesh.IndexCount);
        }

        var vertices = new SkeletalVertex[vertexCount];
        var indices = new uint[indexCount];
        int vertexBase = 0;
        int indexBase = 0;
        foreach (var mesh in pack.Header.Meshes)
        {
            if (mesh.SkeletonIndex != skeletonIndex) continue;
            ReadMesh(pack.Payload, mesh, bones.Length, vertices, vertexBase, indices, indexBase);
            vertexBase += mesh.VertexCount;
            indexBase += mesh.IndexCount;
        }

        var skeletalMesh = new SkeletalMesh
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Vertices = vertices,
            Indices = indices,
            Bones = bones,
            BoneNameToIndex = boneNameToIndex,
            RootBoneIndex = FindRootBone(bones),
            Bounds = vertices.Length == 0
                ? default
                : BoundingBox.FromVertices(GetPositions(vertices))
        };

        if (generateCollisions && vertices.Length > 0)
        {
            skeletalMesh.Collision = new CollisionData
            {
                Type = CollisionType.Box,
                Bounds = skeletalMesh.Bounds
            };
        }

        Console.WriteLine($"[Motif] Imported rigged StrataPack '{Path.GetFileName(path)}': {vertices.Length} vertices, {indices.Length / 3} triangles, {bones.Length} bones.");
        return (true, skeletalMesh);
    }

    private static Bone[] CreateBones(StrataPack.PackSkeleton skeleton)
    {
        var bones = new Bone[skeleton.Bones.Count];
        for (int i = 0; i < bones.Length; i++)
        {
            var source = skeleton.Bones[i];
            bones[i] = new Bone
            {
                Name = source.Name,
                ParentIndex = source.Parent,
                LocalBindPose = ToMatrix(source.RestMatrix),
                InverseBindPose = Matrix4x4.Identity
            };
        }

        for (int i = 0; i < bones.Length; i++)
        {
            int parent = bones[i].ParentIndex;
            if (parent >= 0) bones[parent].Children.Add(i);
        }

        var worldPose = new Matrix4x4[bones.Length];
        var visitState = new byte[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            BuildWorldPose(i, bones, worldPose, visitState);

        for (int i = 0; i < bones.Length; i++)
        {
            if (!Matrix4x4.Invert(worldPose[i], out var inverseBindPose))
                throw new InvalidDataException($"Skeleton bone '{bones[i].Name}' has a singular bind-pose matrix.");
            bones[i].InverseBindPose = inverseBindPose;
        }

        return bones;
    }

    private static Matrix4x4 BuildWorldPose(int index, Bone[] bones, Matrix4x4[] worldPose, byte[] visitState)
    {
        if (visitState[index] == 2) return worldPose[index];
        if (visitState[index] == 1)
            throw new InvalidDataException($"Skeleton hierarchy contains a cycle at bone '{bones[index].Name}'.");

        visitState[index] = 1;
        int parent = bones[index].ParentIndex;
        worldPose[index] = parent < 0
            ? bones[index].LocalBindPose
            : bones[index].LocalBindPose * BuildWorldPose(parent, bones, worldPose, visitState);
        visitState[index] = 2;
        return worldPose[index];
    }

    private static Matrix4x4 ToMatrix(float[] values)
    {
        if (values == null || values.Length != 16)
            throw new InvalidDataException("A StrataPack bone rest matrix must contain 16 floats.");

        return new Matrix4x4(
            values[0], values[1], values[2], values[3],
            values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11],
            values[12], values[13], values[14], values[15]);
    }

    private static void ReadMesh(byte[] payload, StrataPack.PackMesh mesh, int boneCount,
        SkeletalVertex[] vertices, int vertexBase, uint[] indices, int indexBase)
    {
        int vertexOffset = checked((int)mesh.VertexOffset);
        int skinOffset = checked((int)mesh.SkinOffset);
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            int packedOffset = checked(vertexOffset + i * 32);
            int skinRecordOffset = checked(skinOffset + i * StrataPack.SkinStride);
            var vertex = new SkeletalVertex
            {
                Position = ReadVector3(payload, packedOffset),
                Normal = NormalizeOr(ReadVector3(payload, packedOffset + 12), Vector3.UnitY),
                TexCoord = new Vector2(
                    BitConverter.ToSingle(payload, packedOffset + 24),
                    BitConverter.ToSingle(payload, packedOffset + 28)),
                Tangent = Vector3.UnitX,
                BoneIndex0 = ReadBoneIndex(payload, skinRecordOffset, boneCount),
                BoneIndex1 = ReadBoneIndex(payload, skinRecordOffset + 4, boneCount),
                BoneIndex2 = ReadBoneIndex(payload, skinRecordOffset + 8, boneCount),
                BoneIndex3 = ReadBoneIndex(payload, skinRecordOffset + 12, boneCount),
                BoneWeight0 = BitConverter.ToSingle(payload, skinRecordOffset + 16),
                BoneWeight1 = BitConverter.ToSingle(payload, skinRecordOffset + 20),
                BoneWeight2 = BitConverter.ToSingle(payload, skinRecordOffset + 24),
                BoneWeight3 = BitConverter.ToSingle(payload, skinRecordOffset + 28)
            };
            vertex.NormalizeWeights();
            vertices[vertexBase + i] = vertex;
        }

        int sourceIndexOffset = checked((int)mesh.IndexOffset);
        for (int i = 0; i < mesh.IndexCount; i++)
        {
            uint localIndex = BitConverter.ToUInt32(payload, checked(sourceIndexOffset + i * sizeof(uint)));
            if (localIndex >= (uint)mesh.VertexCount)
                throw new InvalidDataException($"StrataPack mesh '{mesh.Name}' index {localIndex} exceeds its vertex count.");
            indices[indexBase + i] = checked((uint)vertexBase + localIndex);
        }
    }

    private static int ReadBoneIndex(byte[] payload, int offset, int boneCount)
    {
        uint index = BitConverter.ToUInt32(payload, offset);
        if (index >= (uint)boneCount)
            throw new InvalidDataException($"StrataPack skin index {index} exceeds its skeleton bone count {boneCount}.");
        return (int)index;
    }

    private static Vector3 ReadVector3(byte[] payload, int offset) => new(
        BitConverter.ToSingle(payload, offset),
        BitConverter.ToSingle(payload, offset + 4),
        BitConverter.ToSingle(payload, offset + 8));

    private static IEnumerable<Vector3> GetPositions(SkeletalVertex[] vertices)
    {
        foreach (var vertex in vertices) yield return vertex.Position;
    }

    private static Vector3 NormalizeOr(Vector3 value, Vector3 fallback) =>
        value.LengthSquared() > 1e-8f ? Vector3.Normalize(value) : fallback;

    private static int FindRootBone(Bone[] bones)
    {
        for (int i = 0; i < bones.Length; i++)
            if (bones[i].ParentIndex < 0) return i;
        return -1;
    }
}

/// <summary>Serializable skeletal mesh and its clips.</summary>
public class SkeletalMeshAsset
{
    public string Name { get; set; } = string.Empty;
    public SkeletalMesh Mesh { get; set; } = null!;
    public List<AnimationClip> Animations { get; set; } = new();
    public CollisionData? Collision { get; set; }
    public AssetMetadata Metadata { get; set; } = new();
}

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

public class AssetMetadata
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "SkeletalMesh";
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Tags { get; set; } = new();
}
