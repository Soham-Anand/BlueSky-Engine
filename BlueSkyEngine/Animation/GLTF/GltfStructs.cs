// Production-grade GLTF 2.0 data structures
// Spec: https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html
// Zero dependencies, full spec compliance

using System;
using System.Numerics;
using System.Collections.Generic;

namespace BlueSky.Animation.GLTF;

public class GltfAsset
{
    public string Version { get; set; } = "2.0";
    public string? Generator { get; set; }
    public string? Copyright { get; set; }
    public string? MinVersion { get; set; }
}

public class GltfScene
{
    public string? Name { get; set; }
    public int[]? Nodes { get; set; }
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfNode
{
    public string? Name { get; set; }
    public int[]? Children { get; set; }
    public int? Mesh { get; set; }
    public int? Skin { get; set; }
    public int? Camera { get; set; }
    public float[]? Matrix { get; set; } // 16 elements, column-major
    public float[]? Translation { get; set; } // [x, y, z]
    public float[]? Rotation { get; set; } // [x, y, z, w] quaternion
    public float[]? Scale { get; set; } // [x, y, z]
    public float[]? Weights { get; set; } // Morph target weights
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfMesh
{
    public string? Name { get; set; }
    public GltfPrimitive[] Primitives { get; set; } = Array.Empty<GltfPrimitive>();
    public float[]? Weights { get; set; }
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfPrimitive
{
    public Dictionary<string, int> Attributes { get; set; } = new();
    public int? Indices { get; set; }
    public int? Material { get; set; }
    public int Mode { get; set; } = 4; // TRIANGLES
    public GltfMorphTarget[]? Targets { get; set; }
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfMorphTarget
{
    public Dictionary<string, int> Attributes { get; set; } = new();
}

public enum GltfPrimitiveMode
{
    Points = 0,
    Lines = 1,
    LineLoop = 2,
    LineStrip = 3,
    Triangles = 4,
    TriangleStrip = 5,
    TriangleFan = 6
}

public class GltfAccessor
{
    public string? Name { get; set; }
    public int? BufferView { get; set; }
    public int ByteOffset { get; set; } = 0;
    public int ComponentType { get; set; }
    public bool Normalized { get; set; } = false;
    public int Count { get; set; }
    public string Type { get; set; } = "SCALAR";
    public float[]? Max { get; set; }
    public float[]? Min { get; set; }
    public GltfSparse? Sparse { get; set; }
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfSparse
{
    public int Count { get; set; }
    public GltfSparseIndices Indices { get; set; } = new();
    public GltfSparseValues Values { get; set; } = new();
}

public class GltfSparseIndices
{
    public int BufferView { get; set; }
    public int ByteOffset { get; set; } = 0;
    public int ComponentType { get; set; }
}

public class GltfSparseValues
{
    public int BufferView { get; set; }
    public int ByteOffset { get; set; } = 0;
}

public enum GltfComponentType
{
    Byte = 5120,
    UnsignedByte = 5121,
    Short = 5122,
    UnsignedShort = 5123,
    UnsignedInt = 5125,
    Float = 5126
}

public enum GltfAccessorType
{
    Scalar,
    Vec2,
    Vec3,
    Vec4,
    Mat2,
    Mat3,
    Mat4
}

public class GltfBufferView
{
    public string? Name { get; set; }
    public int Buffer { get; set; }
    public int ByteOffset { get; set; } = 0;
    public int ByteLength { get; set; }
    public int? ByteStride { get; set; }
    public int? Target { get; set; }
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public enum GltfBufferViewTarget
{
    ArrayBuffer = 34962,
    ElementArrayBuffer = 34963
}

public class GltfBuffer
{
    public string? Name { get; set; }
    public string? Uri { get; set; }
    public int ByteLength { get; set; }
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfMaterial
{
    public string? Name { get; set; }
    public GltfPbrMetallicRoughness? PbrMetallicRoughness { get; set; }
    public GltfNormalTextureInfo? NormalTexture { get; set; }
    public GltfOcclusionTextureInfo? OcclusionTexture { get; set; }
    public GltfTextureInfo? EmissiveTexture { get; set; }
    public float[]? EmissiveFactor { get; set; } // [r, g, b]
    public string AlphaMode { get; set; } = "OPAQUE";
    public float AlphaCutoff { get; set; } = 0.5f;
    public bool DoubleSided { get; set; } = false;
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfPbrMetallicRoughness
{
    public float[]? BaseColorFactor { get; set; } // [r, g, b, a]
    public GltfTextureInfo? BaseColorTexture { get; set; }
    public float MetallicFactor { get; set; } = 1.0f;
    public float RoughnessFactor { get; set; } = 1.0f;
    public GltfTextureInfo? MetallicRoughnessTexture { get; set; }
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfTextureInfo
{
    public int Index { get; set; }
    public int TexCoord { get; set; } = 0;
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfNormalTextureInfo : GltfTextureInfo
{
    public float Scale { get; set; } = 1.0f;
}

public class GltfOcclusionTextureInfo : GltfTextureInfo
{
    public float Strength { get; set; } = 1.0f;
}

public class GltfTexture
{
    public string? Name { get; set; }
    public int? Sampler { get; set; }
    public int? Source { get; set; }
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfSampler
{
    public string? Name { get; set; }
    public int? MagFilter { get; set; }
    public int? MinFilter { get; set; }
    public int WrapS { get; set; } = 10497; // REPEAT
    public int WrapT { get; set; } = 10497; // REPEAT
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfImage
{
    public string? Name { get; set; }
    public string? Uri { get; set; }
    public string? MimeType { get; set; }
    public int? BufferView { get; set; }
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfSkin
{
    public string? Name { get; set; }
    public int? InverseBindMatrices { get; set; }
    public int? Skeleton { get; set; }
    public int[] Joints { get; set; } = Array.Empty<int>();
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfAnimation
{
    public string? Name { get; set; }
    public GltfAnimationChannel[] Channels { get; set; } = Array.Empty<GltfAnimationChannel>();
    public GltfAnimationSampler[] Samplers { get; set; } = Array.Empty<GltfAnimationSampler>();
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfAnimationChannel
{
    public int Sampler { get; set; }
    public GltfAnimationTarget Target { get; set; } = new();
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfAnimationTarget
{
    public int? Node { get; set; }
    public string Path { get; set; } = "translation";
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfAnimationSampler
{
    public int Input { get; set; }
    public string Interpolation { get; set; } = "LINEAR";
    public int Output { get; set; }
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfCamera
{
    public string? Name { get; set; }
    public string Type { get; set; } = "perspective";
    public GltfCameraPerspective? Perspective { get; set; }
    public GltfCameraOrthographic? Orthographic { get; set; }
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}

public class GltfCameraPerspective
{
    public float? AspectRatio { get; set; }
    public float Yfov { get; set; }
    public float? Zfar { get; set; }
    public float Znear { get; set; }
}

public class GltfCameraOrthographic
{
    public float Xmag { get; set; }
    public float Ymag { get; set; }
    public float Zfar { get; set; }
    public float Znear { get; set; }
}

public class GltfRoot
{
    public string[]? ExtensionsUsed { get; set; }
    public string[]? ExtensionsRequired { get; set; }
    public GltfAsset Asset { get; set; } = new();
    public int? Scene { get; set; }
    public GltfScene[]? Scenes { get; set; }
    public GltfNode[]? Nodes { get; set; }
    public GltfMesh[]? Meshes { get; set; }
    public GltfAccessor[]? Accessors { get; set; }
    public GltfBufferView[]? BufferViews { get; set; }
    public GltfBuffer[]? Buffers { get; set; }
    public GltfMaterial[]? Materials { get; set; }
    public GltfTexture[]? Textures { get; set; }
    public GltfImage[]? Images { get; set; }
    public GltfSampler[]? Samplers { get; set; }
    public GltfSkin[]? Skins { get; set; }
    public GltfAnimation[]? Animations { get; set; }
    public GltfCamera[]? Cameras { get; set; }
    public Dictionary<string, object>? Extensions { get; set; }
    public object? Extras { get; set; }
}
