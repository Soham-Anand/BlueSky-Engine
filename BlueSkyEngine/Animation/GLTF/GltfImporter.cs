// Production-grade GLTF/GLB importer with full data extraction
// Handles meshes, materials, textures, animations, skins, and scenes
// Zero dependencies, optimized for performance

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace BlueSky.Animation.GLTF;

public class GltfImporter
{
    private GltfRoot _root = null!;
    private byte[]? _binaryData;
    private Dictionary<int, byte[]> _loadedBuffers = new();
    private string _basePath = "";
    
    public GltfRoot Root => _root;
    
    public static GltfImporter FromFile(string filePath)
    {
        var importer = new GltfImporter();
        importer._basePath = Path.GetDirectoryName(filePath) ?? "";
        
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        
        if (ext == ".glb")
        {
            var (root, binaryData) = GltfBinaryReader.ReadGlb(filePath);
            importer._root = root;
            importer._binaryData = binaryData;
        }
        else if (ext == ".gltf")
        {
            string json = File.ReadAllText(filePath);
            importer._root = GltfJsonParser.Parse(json);
        }
        else
        {
            throw new GltfException($"Unsupported file extension: {ext}");
        }
        
        return importer;
    }
    
    public static GltfImporter FromJson(string json)
    {
        var importer = new GltfImporter();
        importer._root = GltfJsonParser.Parse(json);
        return importer;
    }
    
    public static GltfImporter FromGlb(byte[] glbData)
    {
        var importer = new GltfImporter();
        using var stream = new MemoryStream(glbData);
        var (root, binaryData) = GltfBinaryReader.ReadGlb(stream);
        importer._root = root;
        importer._binaryData = binaryData;
        return importer;
    }
    
    // Extract mesh data
    public GltfMeshData ExtractMesh(int meshIndex)
    {
        if (_root.Meshes == null || meshIndex >= _root.Meshes.Length)
            throw new GltfException($"Mesh index {meshIndex} out of range");
        
        var mesh = _root.Meshes[meshIndex];
        var meshData = new GltfMeshData { Name = mesh.Name ?? $"Mesh_{meshIndex}" };
        
        foreach (var primitive in mesh.Primitives)
        {
            var primData = ExtractPrimitive(primitive);
            meshData.Primitives.Add(primData);
        }
        
        return meshData;
    }
    
    private GltfPrimitiveData ExtractPrimitive(GltfPrimitive primitive)
    {
        var primData = new GltfPrimitiveData
        {
            Mode = (GltfPrimitiveMode)primitive.Mode,
            MaterialIndex = primitive.Material
        };
        
        // Extract vertex attributes
        if (primitive.Attributes.TryGetValue("POSITION", out int posAccessor))
            primData.Positions = ExtractVector3Array(posAccessor);
        
        if (primitive.Attributes.TryGetValue("NORMAL", out int normalAccessor))
            primData.Normals = ExtractVector3Array(normalAccessor);
        
        if (primitive.Attributes.TryGetValue("TANGENT", out int tangentAccessor))
            primData.Tangents = ExtractVector4Array(tangentAccessor);
        
        if (primitive.Attributes.TryGetValue("TEXCOORD_0", out int texCoord0Accessor))
            primData.TexCoords0 = ExtractVector2Array(texCoord0Accessor);
        
        if (primitive.Attributes.TryGetValue("TEXCOORD_1", out int texCoord1Accessor))
            primData.TexCoords1 = ExtractVector2Array(texCoord1Accessor);
        
        if (primitive.Attributes.TryGetValue("COLOR_0", out int colorAccessor))
            primData.Colors = ExtractVector4Array(colorAccessor);
        
        if (primitive.Attributes.TryGetValue("JOINTS_0", out int jointsAccessor))
            primData.Joints = ExtractUShort4Array(jointsAccessor);
        
        if (primitive.Attributes.TryGetValue("WEIGHTS_0", out int weightsAccessor))
            primData.Weights = ExtractVector4Array(weightsAccessor);
        
        // Extract indices
        if (primitive.Indices.HasValue)
            primData.Indices = ExtractIndices(primitive.Indices.Value);
        
        return primData;
    }
    
    // Extract typed arrays from accessors
    public Vector3[] ExtractVector3Array(int accessorIndex)
    {
        var accessor = GetAccessor(accessorIndex);
        if (accessor.Type != "VEC3")
            throw new GltfException($"Accessor {accessorIndex} is not VEC3");
        
        var data = GetAccessorData(accessor);
        var result = new Vector3[accessor.Count];
        
        int stride = GetComponentSize(accessor.ComponentType) * 3;
        
        for (int i = 0; i < accessor.Count; i++)
        {
            int offset = i * stride;
            result[i] = new Vector3(
                ReadFloat(data, offset, accessor.ComponentType, accessor.Normalized),
                ReadFloat(data, offset + GetComponentSize(accessor.ComponentType), accessor.ComponentType, accessor.Normalized),
                ReadFloat(data, offset + GetComponentSize(accessor.ComponentType) * 2, accessor.ComponentType, accessor.Normalized)
            );
        }
        
        return result;
    }
    
    public Vector2[] ExtractVector2Array(int accessorIndex)
    {
        var accessor = GetAccessor(accessorIndex);
        if (accessor.Type != "VEC2")
            throw new GltfException($"Accessor {accessorIndex} is not VEC2");
        
        var data = GetAccessorData(accessor);
        var result = new Vector2[accessor.Count];
        
        int stride = GetComponentSize(accessor.ComponentType) * 2;
        
        for (int i = 0; i < accessor.Count; i++)
        {
            int offset = i * stride;
            result[i] = new Vector2(
                ReadFloat(data, offset, accessor.ComponentType, accessor.Normalized),
                ReadFloat(data, offset + GetComponentSize(accessor.ComponentType), accessor.ComponentType, accessor.Normalized)
            );
        }
        
        return result;
    }
    
    public Vector4[] ExtractVector4Array(int accessorIndex)
    {
        var accessor = GetAccessor(accessorIndex);
        if (accessor.Type != "VEC4")
            throw new GltfException($"Accessor {accessorIndex} is not VEC4");
        
        var data = GetAccessorData(accessor);
        var result = new Vector4[accessor.Count];
        
        int stride = GetComponentSize(accessor.ComponentType) * 4;
        
        for (int i = 0; i < accessor.Count; i++)
        {
            int offset = i * stride;
            result[i] = new Vector4(
                ReadFloat(data, offset, accessor.ComponentType, accessor.Normalized),
                ReadFloat(data, offset + GetComponentSize(accessor.ComponentType), accessor.ComponentType, accessor.Normalized),
                ReadFloat(data, offset + GetComponentSize(accessor.ComponentType) * 2, accessor.ComponentType, accessor.Normalized),
                ReadFloat(data, offset + GetComponentSize(accessor.ComponentType) * 3, accessor.ComponentType, accessor.Normalized)
            );
        }
        
        return result;
    }
    
    public ushort[][] ExtractUShort4Array(int accessorIndex)
    {
        var accessor = GetAccessor(accessorIndex);
        if (accessor.Type != "VEC4")
            throw new GltfException($"Accessor {accessorIndex} is not VEC4");
        
        var data = GetAccessorData(accessor);
        var result = new ushort[accessor.Count][];
        
        int stride = GetComponentSize(accessor.ComponentType) * 4;
        
        for (int i = 0; i < accessor.Count; i++)
        {
            int offset = i * stride;
            result[i] = new ushort[4];
            for (int j = 0; j < 4; j++)
            {
                result[i][j] = ReadUShort(data, offset + j * GetComponentSize(accessor.ComponentType), accessor.ComponentType);
            }
        }
        
        return result;
    }
    
    public uint[] ExtractIndices(int accessorIndex)
    {
        var accessor = GetAccessor(accessorIndex);
        if (accessor.Type != "SCALAR")
            throw new GltfException($"Accessor {accessorIndex} is not SCALAR");
        
        var data = GetAccessorData(accessor);
        var result = new uint[accessor.Count];
        
        int componentSize = GetComponentSize(accessor.ComponentType);
        
        for (int i = 0; i < accessor.Count; i++)
        {
            int offset = i * componentSize;
            result[i] = ReadUInt(data, offset, accessor.ComponentType);
        }
        
        return result;
    }
    
    public Matrix4x4[] ExtractMatrix4Array(int accessorIndex)
    {
        var accessor = GetAccessor(accessorIndex);
        if (accessor.Type != "MAT4")
            throw new GltfException($"Accessor {accessorIndex} is not MAT4");
        
        var data = GetAccessorData(accessor);
        var result = new Matrix4x4[accessor.Count];
        
        int stride = GetComponentSize(accessor.ComponentType) * 16;
        
        for (int i = 0; i < accessor.Count; i++)
        {
            int offset = i * stride;
            float[] values = new float[16];
            for (int j = 0; j < 16; j++)
            {
                values[j] = ReadFloat(data, offset + j * GetComponentSize(accessor.ComponentType), accessor.ComponentType, accessor.Normalized);
            }
            
            // GLTF uses column-major matrices
            result[i] = new Matrix4x4(
                values[0], values[1], values[2], values[3],
                values[4], values[5], values[6], values[7],
                values[8], values[9], values[10], values[11],
                values[12], values[13], values[14], values[15]
            );
        }
        
        return result;
    }
    
    public float[] ExtractFloatArray(int accessorIndex)
    {
        var accessor = GetAccessor(accessorIndex);
        if (accessor.Type != "SCALAR")
            throw new GltfException($"Accessor {accessorIndex} is not SCALAR");
        
        var data = GetAccessorData(accessor);
        var result = new float[accessor.Count];
        
        int componentSize = GetComponentSize(accessor.ComponentType);
        
        for (int i = 0; i < accessor.Count; i++)
        {
            int offset = i * componentSize;
            result[i] = ReadFloat(data, offset, accessor.ComponentType, accessor.Normalized);
        }
        
        return result;
    }
    
    // Extract texture data (resolves texture → image → binary data)
    public byte[]? ExtractTexture(int textureIndex)
    {
        if (_root.Textures == null || textureIndex >= _root.Textures.Length)
            return null;
        
        var texture = _root.Textures[textureIndex];
        if (!texture.Source.HasValue)
            return null;
        
        return ExtractImage(texture.Source.Value);
    }
    
    // Extract image data
    public byte[] ExtractImage(int imageIndex)
    {
        if (_root.Images == null || imageIndex >= _root.Images.Length)
            throw new GltfException($"Image index {imageIndex} out of range");
        
        var image = _root.Images[imageIndex];
        
        // Embedded in buffer view
        if (image.BufferView.HasValue)
        {
            var bufferView = GetBufferView(image.BufferView.Value);
            var buffer = GetBuffer(bufferView.Buffer);
            
            byte[] imageData = new byte[bufferView.ByteLength];
            Array.Copy(buffer, bufferView.ByteOffset, imageData, 0, bufferView.ByteLength);
            return imageData;
        }
        
        // External URI
        if (image.Uri != null)
        {
            // Data URI
            if (image.Uri.StartsWith("data:"))
            {
                return DecodeDataUri(image.Uri);
            }
            
            // External file
            string imagePath = Path.Combine(_basePath, image.Uri);
            return File.ReadAllBytes(imagePath);
        }
        
        throw new GltfException($"Image {imageIndex} has no data source");
    }
    
    // Helper methods
    private GltfAccessor GetAccessor(int index)
    {
        if (_root.Accessors == null || index >= _root.Accessors.Length)
            throw new GltfException($"Accessor index {index} out of range");
        return _root.Accessors[index];
    }
    
    private GltfBufferView GetBufferView(int index)
    {
        if (_root.BufferViews == null || index >= _root.BufferViews.Length)
            throw new GltfException($"BufferView index {index} out of range");
        return _root.BufferViews[index];
    }
    
    private byte[] GetBuffer(int index)
    {
        if (_loadedBuffers.TryGetValue(index, out var cached))
            return cached;
        
        if (_root.Buffers == null || index >= _root.Buffers.Length)
            throw new GltfException($"Buffer index {index} out of range");
        
        var buffer = _root.Buffers[index];
        byte[] data;
        
        // GLB embedded buffer
        if (buffer.Uri == null && _binaryData != null)
        {
            data = _binaryData;
        }
        // Data URI
        else if (buffer.Uri != null && buffer.Uri.StartsWith("data:"))
        {
            data = DecodeDataUri(buffer.Uri);
        }
        // External file
        else if (buffer.Uri != null)
        {
            string bufferPath = Path.Combine(_basePath, buffer.Uri);
            data = File.ReadAllBytes(bufferPath);
        }
        else
        {
            throw new GltfException($"Buffer {index} has no data source");
        }
        
        _loadedBuffers[index] = data;
        return data;
    }
    
    private byte[] GetAccessorData(GltfAccessor accessor)
    {
        if (!accessor.BufferView.HasValue)
            throw new GltfException("Accessor has no buffer view");
        
        var bufferView = GetBufferView(accessor.BufferView.Value);
        var buffer = GetBuffer(bufferView.Buffer);
        
        int offset = bufferView.ByteOffset + accessor.ByteOffset;
        int componentSize = GetComponentSize(accessor.ComponentType);
        int elementSize = GetElementSize(accessor.Type) * componentSize;
        int totalSize = accessor.Count * elementSize;
        
        byte[] data = new byte[totalSize];
        
        if (bufferView.ByteStride.HasValue && bufferView.ByteStride.Value > elementSize)
        {
            // Interleaved data
            int stride = bufferView.ByteStride.Value;
            for (int i = 0; i < accessor.Count; i++)
            {
                Array.Copy(buffer, offset + i * stride, data, i * elementSize, elementSize);
            }
        }
        else
        {
            // Packed data
            Array.Copy(buffer, offset, data, 0, totalSize);
        }
        
        return data;
    }
    
    private static int GetComponentSize(int componentType)
    {
        return componentType switch
        {
            5120 => 1, // BYTE
            5121 => 1, // UNSIGNED_BYTE
            5122 => 2, // SHORT
            5123 => 2, // UNSIGNED_SHORT
            5125 => 4, // UNSIGNED_INT
            5126 => 4, // FLOAT
            _ => throw new GltfException($"Unknown component type: {componentType}")
        };
    }
    
    private static int GetElementSize(string type)
    {
        return type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            "MAT2" => 4,
            "MAT3" => 9,
            "MAT4" => 16,
            _ => throw new GltfException($"Unknown accessor type: {type}")
        };
    }
    
    private static float ReadFloat(byte[] data, int offset, int componentType, bool normalized)
    {
        return componentType switch
        {
            5120 => normalized ? (sbyte)data[offset] / 127f : (sbyte)data[offset],
            5121 => normalized ? data[offset] / 255f : data[offset],
            5122 => normalized ? BitConverter.ToInt16(data, offset) / 32767f : BitConverter.ToInt16(data, offset),
            5123 => normalized ? BitConverter.ToUInt16(data, offset) / 65535f : BitConverter.ToUInt16(data, offset),
            5125 => BitConverter.ToUInt32(data, offset),
            5126 => BitConverter.ToSingle(data, offset),
            _ => throw new GltfException($"Cannot read float from component type {componentType}")
        };
    }
    
    private static ushort ReadUShort(byte[] data, int offset, int componentType)
    {
        return componentType switch
        {
            5121 => data[offset],
            5123 => BitConverter.ToUInt16(data, offset),
            _ => throw new GltfException($"Cannot read ushort from component type {componentType}")
        };
    }
    
    private static uint ReadUInt(byte[] data, int offset, int componentType)
    {
        return componentType switch
        {
            5121 => data[offset],
            5123 => BitConverter.ToUInt16(data, offset),
            5125 => BitConverter.ToUInt32(data, offset),
            _ => throw new GltfException($"Cannot read uint from component type {componentType}")
        };
    }
    
    private static byte[] DecodeDataUri(string dataUri)
    {
        // Format: data:[<mediatype>][;base64],<data>
        int commaIndex = dataUri.IndexOf(',');
        if (commaIndex < 0)
            throw new GltfException("Invalid data URI");
        
        string header = dataUri.Substring(0, commaIndex);
        string data = dataUri.Substring(commaIndex + 1);
        
        if (header.Contains("base64"))
        {
            return Convert.FromBase64String(data);
        }
        else
        {
            // URL-encoded data
            return System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data));
        }
    }
}

// Output data structures
public class GltfMeshData
{
    public string Name { get; set; } = "";
    public List<GltfPrimitiveData> Primitives { get; set; } = new();
}

public class GltfPrimitiveData
{
    public GltfPrimitiveMode Mode { get; set; }
    public int? MaterialIndex { get; set; }
    public int? Material => MaterialIndex; // Alias for compatibility
    
    public Vector3[]? Positions { get; set; }
    public Vector3[]? Normals { get; set; }
    public Vector4[]? Tangents { get; set; }
    public Vector2[]? TexCoords0 { get; set; }
    public Vector2[]? TexCoords1 { get; set; }
    public Vector4[]? Colors { get; set; }
    public ushort[][]? Joints { get; set; }
    public Vector4[]? Weights { get; set; }
    
    public uint[]? Indices { get; set; }
}
