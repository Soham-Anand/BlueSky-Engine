// GLB (binary GLTF) reader with zero dependencies
// Handles chunked binary format, embedded buffers, and validation
// Spec: https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#glb-file-format-specification

using System;
using System.IO;
using System.Text;

namespace BlueSky.Animation.GLTF;

public static class GltfBinaryReader
{
    private const uint GLB_MAGIC = 0x46546C67; // "glTF" in ASCII
    private const uint GLB_VERSION = 2;
    private const uint CHUNK_TYPE_JSON = 0x4E4F534A; // "JSON"
    private const uint CHUNK_TYPE_BIN = 0x004E4942;  // "BIN\0"
    
    public static (GltfRoot root, byte[]? binaryData) ReadGlb(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return ReadGlb(stream);
    }
    
    public static (GltfRoot root, byte[]? binaryData) ReadGlb(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        
        // Read header (12 bytes)
        uint magic = reader.ReadUInt32();
        if (magic != GLB_MAGIC)
            throw new GltfException($"Invalid GLB magic: 0x{magic:X8}, expected 0x{GLB_MAGIC:X8}");
        
        uint version = reader.ReadUInt32();
        if (version != GLB_VERSION)
            throw new GltfException($"Unsupported GLB version: {version}, expected {GLB_VERSION}");
        
        uint length = reader.ReadUInt32();
        
        // Read JSON chunk
        uint jsonChunkLength = reader.ReadUInt32();
        uint jsonChunkType = reader.ReadUInt32();
        
        if (jsonChunkType != CHUNK_TYPE_JSON)
            throw new GltfException($"First chunk must be JSON, got 0x{jsonChunkType:X8}");
        
        byte[] jsonBytes = reader.ReadBytes((int)jsonChunkLength);
        string json = Encoding.UTF8.GetString(jsonBytes);
        
        GltfRoot root = GltfJsonParser.Parse(json);
        
        // Read binary chunk (optional)
        byte[]? binaryData = null;
        if (stream.Position < stream.Length)
        {
            uint binChunkLength = reader.ReadUInt32();
            uint binChunkType = reader.ReadUInt32();
            
            if (binChunkType != CHUNK_TYPE_BIN)
                throw new GltfException($"Second chunk must be BIN, got 0x{binChunkType:X8}");
            
            binaryData = reader.ReadBytes((int)binChunkLength);
        }
        
        return (root, binaryData);
    }
    
    public static void WriteGlb(string filePath, GltfRoot root, byte[]? binaryData = null)
    {
        using var stream = File.Create(filePath);
        WriteGlb(stream, root, binaryData);
    }
    
    public static void WriteGlb(Stream stream, GltfRoot root, byte[]? binaryData = null)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        
        // Serialize JSON
        string json = GltfJsonSerializer.Serialize(root);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        
        // Pad JSON to 4-byte alignment
        int jsonPadding = (4 - (jsonBytes.Length % 4)) % 4;
        uint jsonChunkLength = (uint)(jsonBytes.Length + jsonPadding);
        
        // Calculate total length
        uint totalLength = 12 + 8 + jsonChunkLength; // header + JSON chunk header + JSON data
        if (binaryData != null)
        {
            int binPadding = (4 - (binaryData.Length % 4)) % 4;
            totalLength += 8 + (uint)(binaryData.Length + binPadding);
        }
        
        // Write header
        writer.Write(GLB_MAGIC);
        writer.Write(GLB_VERSION);
        writer.Write(totalLength);
        
        // Write JSON chunk
        writer.Write(jsonChunkLength);
        writer.Write(CHUNK_TYPE_JSON);
        writer.Write(jsonBytes);
        for (int i = 0; i < jsonPadding; i++)
            writer.Write((byte)0x20); // Space padding
        
        // Write binary chunk
        if (binaryData != null)
        {
            int binPadding = (4 - (binaryData.Length % 4)) % 4;
            uint binChunkLength = (uint)(binaryData.Length + binPadding);
            
            writer.Write(binChunkLength);
            writer.Write(CHUNK_TYPE_BIN);
            writer.Write(binaryData);
            for (int i = 0; i < binPadding; i++)
                writer.Write((byte)0x00); // Zero padding
        }
    }
}

public static class GltfJsonSerializer
{
    public static string Serialize(GltfRoot root)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        
        // Asset (required)
        sb.Append("\"asset\":");
        SerializeAsset(sb, root.Asset);
        
        // Scene
        if (root.Scene.HasValue)
        {
            sb.Append(",\"scene\":");
            sb.Append(root.Scene.Value);
        }
        
        // Scenes
        if (root.Scenes != null && root.Scenes.Length > 0)
        {
            sb.Append(",\"scenes\":");
            SerializeArray(sb, root.Scenes, SerializeScene);
        }
        
        // Nodes
        if (root.Nodes != null && root.Nodes.Length > 0)
        {
            sb.Append(",\"nodes\":");
            SerializeArray(sb, root.Nodes, SerializeNode);
        }
        
        // Meshes
        if (root.Meshes != null && root.Meshes.Length > 0)
        {
            sb.Append(",\"meshes\":");
            SerializeArray(sb, root.Meshes, SerializeMesh);
        }
        
        // Accessors
        if (root.Accessors != null && root.Accessors.Length > 0)
        {
            sb.Append(",\"accessors\":");
            SerializeArray(sb, root.Accessors, SerializeAccessor);
        }
        
        // BufferViews
        if (root.BufferViews != null && root.BufferViews.Length > 0)
        {
            sb.Append(",\"bufferViews\":");
            SerializeArray(sb, root.BufferViews, SerializeBufferView);
        }
        
        // Buffers
        if (root.Buffers != null && root.Buffers.Length > 0)
        {
            sb.Append(",\"buffers\":");
            SerializeArray(sb, root.Buffers, SerializeBuffer);
        }
        
        // Materials
        if (root.Materials != null && root.Materials.Length > 0)
        {
            sb.Append(",\"materials\":");
            SerializeArray(sb, root.Materials, SerializeMaterial);
        }
        
        // Textures
        if (root.Textures != null && root.Textures.Length > 0)
        {
            sb.Append(",\"textures\":");
            SerializeArray(sb, root.Textures, SerializeTexture);
        }
        
        // Images
        if (root.Images != null && root.Images.Length > 0)
        {
            sb.Append(",\"images\":");
            SerializeArray(sb, root.Images, SerializeImage);
        }
        
        // Samplers
        if (root.Samplers != null && root.Samplers.Length > 0)
        {
            sb.Append(",\"samplers\":");
            SerializeArray(sb, root.Samplers, SerializeSampler);
        }
        
        sb.Append('}');
        return sb.ToString();
    }
    
    private static void SerializeAsset(StringBuilder sb, GltfAsset asset)
    {
        sb.Append("{\"version\":\"");
        sb.Append(asset.Version);
        sb.Append('"');
        
        if (asset.Generator != null)
        {
            sb.Append(",\"generator\":\"");
            sb.Append(EscapeString(asset.Generator));
            sb.Append('"');
        }
        
        sb.Append('}');
    }
    
    private static void SerializeScene(StringBuilder sb, GltfScene scene)
    {
        sb.Append('{');
        bool first = true;
        
        if (scene.Name != null)
        {
            sb.Append("\"name\":\"");
            sb.Append(EscapeString(scene.Name));
            sb.Append('"');
            first = false;
        }
        
        if (scene.Nodes != null)
        {
            if (!first) sb.Append(',');
            sb.Append("\"nodes\":");
            SerializeIntArray(sb, scene.Nodes);
        }
        
        sb.Append('}');
    }
    
    private static void SerializeNode(StringBuilder sb, GltfNode node)
    {
        sb.Append('{');
        bool first = true;
        
        if (node.Name != null)
        {
            sb.Append("\"name\":\"");
            sb.Append(EscapeString(node.Name));
            sb.Append('"');
            first = false;
        }
        
        if (node.Mesh.HasValue)
        {
            if (!first) sb.Append(',');
            sb.Append("\"mesh\":");
            sb.Append(node.Mesh.Value);
            first = false;
        }
        
        if (node.Translation != null)
        {
            if (!first) sb.Append(',');
            sb.Append("\"translation\":");
            SerializeFloatArray(sb, node.Translation);
            first = false;
        }
        
        if (node.Rotation != null)
        {
            if (!first) sb.Append(',');
            sb.Append("\"rotation\":");
            SerializeFloatArray(sb, node.Rotation);
            first = false;
        }
        
        if (node.Scale != null)
        {
            if (!first) sb.Append(',');
            sb.Append("\"scale\":");
            SerializeFloatArray(sb, node.Scale);
            first = false;
        }
        
        if (node.Children != null)
        {
            if (!first) sb.Append(',');
            sb.Append("\"children\":");
            SerializeIntArray(sb, node.Children);
        }
        
        sb.Append('}');
    }
    
    private static void SerializeMesh(StringBuilder sb, GltfMesh mesh)
    {
        sb.Append('{');
        
        if (mesh.Name != null)
        {
            sb.Append("\"name\":\"");
            sb.Append(EscapeString(mesh.Name));
            sb.Append("\",");
        }
        
        sb.Append("\"primitives\":");
        SerializeArray(sb, mesh.Primitives, SerializePrimitive);
        
        sb.Append('}');
    }
    
    private static void SerializePrimitive(StringBuilder sb, GltfPrimitive prim)
    {
        sb.Append("{\"attributes\":{");
        
        bool first = true;
        foreach (var kvp in prim.Attributes)
        {
            if (!first) sb.Append(',');
            sb.Append('"');
            sb.Append(kvp.Key);
            sb.Append("\":");
            sb.Append(kvp.Value);
            first = false;
        }
        
        sb.Append('}');
        
        if (prim.Indices.HasValue)
        {
            sb.Append(",\"indices\":");
            sb.Append(prim.Indices.Value);
        }
        
        if (prim.Material.HasValue)
        {
            sb.Append(",\"material\":");
            sb.Append(prim.Material.Value);
        }
        
        sb.Append('}');
    }
    
    private static void SerializeAccessor(StringBuilder sb, GltfAccessor accessor)
    {
        sb.Append('{');
        
        if (accessor.BufferView.HasValue)
        {
            sb.Append("\"bufferView\":");
            sb.Append(accessor.BufferView.Value);
            sb.Append(',');
        }
        
        sb.Append("\"componentType\":");
        sb.Append(accessor.ComponentType);
        sb.Append(",\"count\":");
        sb.Append(accessor.Count);
        sb.Append(",\"type\":\"");
        sb.Append(accessor.Type);
        sb.Append('"');
        
        if (accessor.Max != null)
        {
            sb.Append(",\"max\":");
            SerializeFloatArray(sb, accessor.Max);
        }
        
        if (accessor.Min != null)
        {
            sb.Append(",\"min\":");
            SerializeFloatArray(sb, accessor.Min);
        }
        
        sb.Append('}');
    }
    
    private static void SerializeBufferView(StringBuilder sb, GltfBufferView bufferView)
    {
        sb.Append("{\"buffer\":");
        sb.Append(bufferView.Buffer);
        sb.Append(",\"byteLength\":");
        sb.Append(bufferView.ByteLength);
        
        if (bufferView.ByteOffset > 0)
        {
            sb.Append(",\"byteOffset\":");
            sb.Append(bufferView.ByteOffset);
        }
        
        if (bufferView.ByteStride.HasValue)
        {
            sb.Append(",\"byteStride\":");
            sb.Append(bufferView.ByteStride.Value);
        }
        
        sb.Append('}');
    }
    
    private static void SerializeBuffer(StringBuilder sb, GltfBuffer buffer)
    {
        sb.Append("{\"byteLength\":");
        sb.Append(buffer.ByteLength);
        
        if (buffer.Uri != null)
        {
            sb.Append(",\"uri\":\"");
            sb.Append(EscapeString(buffer.Uri));
            sb.Append('"');
        }
        
        sb.Append('}');
    }
    
    private static void SerializeMaterial(StringBuilder sb, GltfMaterial material)
    {
        sb.Append("{\"name\":\"");
        sb.Append(EscapeString(material.Name ?? "Material"));
        sb.Append('"');
        
        if (material.PbrMetallicRoughness != null)
        {
            sb.Append(",\"pbrMetallicRoughness\":{");
            sb.Append("\"metallicFactor\":");
            sb.Append(material.PbrMetallicRoughness.MetallicFactor);
            sb.Append(",\"roughnessFactor\":");
            sb.Append(material.PbrMetallicRoughness.RoughnessFactor);
            sb.Append('}');
        }
        
        sb.Append('}');
    }
    
    private static void SerializeTexture(StringBuilder sb, GltfTexture texture)
    {
        sb.Append('{');
        
        if (texture.Source.HasValue)
        {
            sb.Append("\"source\":");
            sb.Append(texture.Source.Value);
        }
        
        sb.Append('}');
    }
    
    private static void SerializeImage(StringBuilder sb, GltfImage image)
    {
        sb.Append('{');
        
        if (image.Uri != null)
        {
            sb.Append("\"uri\":\"");
            sb.Append(EscapeString(image.Uri));
            sb.Append('"');
        }
        
        sb.Append('}');
    }
    
    private static void SerializeSampler(StringBuilder sb, GltfSampler sampler)
    {
        sb.Append("{\"wrapS\":");
        sb.Append(sampler.WrapS);
        sb.Append(",\"wrapT\":");
        sb.Append(sampler.WrapT);
        sb.Append('}');
    }
    
    private static void SerializeArray<T>(StringBuilder sb, T[] array, Action<StringBuilder, T> serializer)
    {
        sb.Append('[');
        for (int i = 0; i < array.Length; i++)
        {
            if (i > 0) sb.Append(',');
            serializer(sb, array[i]);
        }
        sb.Append(']');
    }
    
    private static void SerializeIntArray(StringBuilder sb, int[] array)
    {
        sb.Append('[');
        for (int i = 0; i < array.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(array[i]);
        }
        sb.Append(']');
    }
    
    private static void SerializeFloatArray(StringBuilder sb, float[] array)
    {
        sb.Append('[');
        for (int i = 0; i < array.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(array[i].ToString("G", System.Globalization.CultureInfo.InvariantCulture));
        }
        sb.Append(']');
    }
    
    private static string EscapeString(string str)
    {
        return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }
}
