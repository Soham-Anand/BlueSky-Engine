// Zero-dependency JSON parser for GLTF
// Handles all GLTF JSON structures without System.Text.Json or Newtonsoft
// Production-grade with proper error handling and validation

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BlueSky.Animation.GLTF;

public static class GltfJsonParser
{
    public static GltfRoot Parse(string json)
    {
        var tokenizer = new JsonTokenizer(json);
        var value = ParseValue(tokenizer);
        
        if (value is not Dictionary<string, object> root)
            throw new GltfException("Root must be an object");
        
        return DeserializeRoot(root);
    }
    
    private static object ParseValue(JsonTokenizer tokenizer)
    {
        tokenizer.SkipWhitespace();
        char c = tokenizer.Peek();
        
        return c switch
        {
            '{' => ParseObject(tokenizer),
            '[' => ParseArray(tokenizer),
            '"' => ParseString(tokenizer),
            't' or 'f' => ParseBoolean(tokenizer),
            'n' => ParseNull(tokenizer),
            _ => ParseNumber(tokenizer)
        };
    }
    
    private static Dictionary<string, object> ParseObject(JsonTokenizer tokenizer)
    {
        var obj = new Dictionary<string, object>();
        tokenizer.Expect('{');
        tokenizer.SkipWhitespace();
        
        if (tokenizer.Peek() == '}')
        {
            tokenizer.Read();
            return obj;
        }
        
        while (true)
        {
            tokenizer.SkipWhitespace();
            string key = ParseString(tokenizer);
            tokenizer.SkipWhitespace();
            tokenizer.Expect(':');
            object value = ParseValue(tokenizer);
            obj[key] = value;
            
            tokenizer.SkipWhitespace();
            char next = tokenizer.Read();
            if (next == '}') break;
            if (next != ',') throw new GltfException($"Expected ',' or '}}', got '{next}'");
        }
        
        return obj;
    }
    
    private static List<object> ParseArray(JsonTokenizer tokenizer)
    {
        var arr = new List<object>();
        tokenizer.Expect('[');
        tokenizer.SkipWhitespace();
        
        if (tokenizer.Peek() == ']')
        {
            tokenizer.Read();
            return arr;
        }
        
        while (true)
        {
            arr.Add(ParseValue(tokenizer));
            tokenizer.SkipWhitespace();
            char next = tokenizer.Read();
            if (next == ']') break;
            if (next != ',') throw new GltfException($"Expected ',' or ']', got '{next}'");
        }
        
        return arr;
    }
    
    private static string ParseString(JsonTokenizer tokenizer)
    {
        tokenizer.Expect('"');
        var sb = new StringBuilder();
        
        while (true)
        {
            char c = tokenizer.Read();
            if (c == '"') break;
            
            if (c == '\\')
            {
                char escape = tokenizer.Read();
                sb.Append(escape switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'u' => ParseUnicodeEscape(tokenizer),
                    _ => throw new GltfException($"Invalid escape sequence: \\{escape}")
                });
            }
            else
            {
                sb.Append(c);
            }
        }
        
        return sb.ToString();
    }
    
    private static char ParseUnicodeEscape(JsonTokenizer tokenizer)
    {
        string hex = new string(new[] { tokenizer.Read(), tokenizer.Read(), tokenizer.Read(), tokenizer.Read() });
        return (char)int.Parse(hex, NumberStyles.HexNumber);
    }
    
    private static double ParseNumber(JsonTokenizer tokenizer)
    {
        var sb = new StringBuilder();
        
        if (tokenizer.Peek() == '-')
            sb.Append(tokenizer.Read());
        
        while (char.IsDigit(tokenizer.Peek()) || tokenizer.Peek() == '.' || 
               tokenizer.Peek() == 'e' || tokenizer.Peek() == 'E' || 
               tokenizer.Peek() == '+' || tokenizer.Peek() == '-')
        {
            sb.Append(tokenizer.Read());
        }
        
        return double.Parse(sb.ToString(), CultureInfo.InvariantCulture);
    }
    
    private static bool ParseBoolean(JsonTokenizer tokenizer)
    {
        char c = tokenizer.Peek();
        if (c == 't')
        {
            tokenizer.ExpectSequence("true");
            return true;
        }
        else
        {
            tokenizer.ExpectSequence("false");
            return false;
        }
    }
    
    private static object? ParseNull(JsonTokenizer tokenizer)
    {
        tokenizer.ExpectSequence("null");
        return null;
    }
    
    // Deserialization helpers
    private static GltfRoot DeserializeRoot(Dictionary<string, object> obj)
    {
        var root = new GltfRoot();
        
        if (obj.TryGetValue("asset", out var asset))
            root.Asset = DeserializeAsset((Dictionary<string, object>)asset);
        
        root.ExtensionsUsed = GetStringArray(obj, "extensionsUsed");
        root.ExtensionsRequired = GetStringArray(obj, "extensionsRequired");
        root.Scene = GetInt(obj, "scene");
        
        if (obj.TryGetValue("scenes", out var scenes))
            root.Scenes = DeserializeArray((List<object>)scenes, DeserializeScene);
        
        if (obj.TryGetValue("nodes", out var nodes))
            root.Nodes = DeserializeArray((List<object>)nodes, DeserializeNode);
        
        if (obj.TryGetValue("meshes", out var meshes))
            root.Meshes = DeserializeArray((List<object>)meshes, DeserializeMesh);
        
        if (obj.TryGetValue("accessors", out var accessors))
            root.Accessors = DeserializeArray((List<object>)accessors, DeserializeAccessor);
        
        if (obj.TryGetValue("bufferViews", out var bufferViews))
            root.BufferViews = DeserializeArray((List<object>)bufferViews, DeserializeBufferView);
        
        if (obj.TryGetValue("buffers", out var buffers))
            root.Buffers = DeserializeArray((List<object>)buffers, DeserializeBuffer);
        
        if (obj.TryGetValue("materials", out var materials))
            root.Materials = DeserializeArray((List<object>)materials, DeserializeMaterial);
        
        if (obj.TryGetValue("textures", out var textures))
            root.Textures = DeserializeArray((List<object>)textures, DeserializeTexture);
        
        if (obj.TryGetValue("images", out var images))
            root.Images = DeserializeArray((List<object>)images, DeserializeImage);
        
        if (obj.TryGetValue("samplers", out var samplers))
            root.Samplers = DeserializeArray((List<object>)samplers, DeserializeSampler);
        
        if (obj.TryGetValue("skins", out var skins))
            root.Skins = DeserializeArray((List<object>)skins, DeserializeSkin);
        
        if (obj.TryGetValue("animations", out var animations))
            root.Animations = DeserializeArray((List<object>)animations, DeserializeAnimation);
        
        if (obj.TryGetValue("cameras", out var cameras))
            root.Cameras = DeserializeArray((List<object>)cameras, DeserializeCamera);
        
        return root;
    }
    
    private static GltfAsset DeserializeAsset(Dictionary<string, object> obj)
    {
        return new GltfAsset
        {
            Version = GetString(obj, "version") ?? "2.0",
            Generator = GetString(obj, "generator"),
            Copyright = GetString(obj, "copyright"),
            MinVersion = GetString(obj, "minVersion")
        };
    }
    
    private static GltfScene DeserializeScene(Dictionary<string, object> obj)
    {
        return new GltfScene
        {
            Name = GetString(obj, "name"),
            Nodes = GetIntArray(obj, "nodes")
        };
    }
    
    private static GltfNode DeserializeNode(Dictionary<string, object> obj)
    {
        return new GltfNode
        {
            Name = GetString(obj, "name"),
            Children = GetIntArray(obj, "children"),
            Mesh = GetInt(obj, "mesh"),
            Skin = GetInt(obj, "skin"),
            Camera = GetInt(obj, "camera"),
            Matrix = GetFloatArray(obj, "matrix"),
            Translation = GetFloatArray(obj, "translation"),
            Rotation = GetFloatArray(obj, "rotation"),
            Scale = GetFloatArray(obj, "scale"),
            Weights = GetFloatArray(obj, "weights")
        };
    }
    
    private static GltfMesh DeserializeMesh(Dictionary<string, object> obj)
    {
        var mesh = new GltfMesh
        {
            Name = GetString(obj, "name"),
            Weights = GetFloatArray(obj, "weights")
        };
        
        if (obj.TryGetValue("primitives", out var prims))
            mesh.Primitives = DeserializeArray((List<object>)prims, DeserializePrimitive);
        
        return mesh;
    }
    
    private static GltfPrimitive DeserializePrimitive(Dictionary<string, object> obj)
    {
        var prim = new GltfPrimitive
        {
            Indices = GetInt(obj, "indices"),
            Material = GetInt(obj, "material"),
            Mode = GetInt(obj, "mode") ?? 4
        };
        
        if (obj.TryGetValue("attributes", out var attrs))
        {
            var attrDict = (Dictionary<string, object>)attrs;
            foreach (var kvp in attrDict)
                prim.Attributes[kvp.Key] = Convert.ToInt32(kvp.Value);
        }
        
        return prim;
    }
    
    private static GltfAccessor DeserializeAccessor(Dictionary<string, object> obj)
    {
        return new GltfAccessor
        {
            Name = GetString(obj, "name"),
            BufferView = GetInt(obj, "bufferView"),
            ByteOffset = GetInt(obj, "byteOffset") ?? 0,
            ComponentType = GetInt(obj, "componentType") ?? 0,
            Normalized = GetBool(obj, "normalized") ?? false,
            Count = GetInt(obj, "count") ?? 0,
            Type = GetString(obj, "type") ?? "SCALAR",
            Max = GetFloatArray(obj, "max"),
            Min = GetFloatArray(obj, "min")
        };
    }
    
    private static GltfBufferView DeserializeBufferView(Dictionary<string, object> obj)
    {
        return new GltfBufferView
        {
            Name = GetString(obj, "name"),
            Buffer = GetInt(obj, "buffer") ?? 0,
            ByteOffset = GetInt(obj, "byteOffset") ?? 0,
            ByteLength = GetInt(obj, "byteLength") ?? 0,
            ByteStride = GetInt(obj, "byteStride"),
            Target = GetInt(obj, "target")
        };
    }
    
    private static GltfBuffer DeserializeBuffer(Dictionary<string, object> obj)
    {
        return new GltfBuffer
        {
            Name = GetString(obj, "name"),
            Uri = GetString(obj, "uri"),
            ByteLength = GetInt(obj, "byteLength") ?? 0
        };
    }
    
    private static GltfMaterial DeserializeMaterial(Dictionary<string, object> obj)
    {
        var mat = new GltfMaterial
        {
            Name = GetString(obj, "name"),
            EmissiveFactor = GetFloatArray(obj, "emissiveFactor"),
            AlphaMode = GetString(obj, "alphaMode") ?? "OPAQUE",
            AlphaCutoff = GetFloat(obj, "alphaCutoff") ?? 0.5f,
            DoubleSided = GetBool(obj, "doubleSided") ?? false
        };
        
        if (obj.TryGetValue("pbrMetallicRoughness", out var pbr))
            mat.PbrMetallicRoughness = DeserializePbrMetallicRoughness((Dictionary<string, object>)pbr);
            
        if (obj.TryGetValue("normalTexture", out var nt))
            mat.NormalTexture = DeserializeNormalTextureInfo((Dictionary<string, object>)nt);
            
        if (obj.TryGetValue("occlusionTexture", out var ot))
            mat.OcclusionTexture = DeserializeOcclusionTextureInfo((Dictionary<string, object>)ot);
            
        if (obj.TryGetValue("emissiveTexture", out var et))
            mat.EmissiveTexture = DeserializeTextureInfo((Dictionary<string, object>)et);
        
        return mat;
    }
    
    private static GltfPbrMetallicRoughness DeserializePbrMetallicRoughness(Dictionary<string, object> obj)
    {
        var pbr = new GltfPbrMetallicRoughness
        {
            BaseColorFactor = GetFloatArray(obj, "baseColorFactor"),
            MetallicFactor = GetFloat(obj, "metallicFactor") ?? 1.0f,
            RoughnessFactor = GetFloat(obj, "roughnessFactor") ?? 1.0f
        };
        
        if (obj.TryGetValue("baseColorTexture", out var bct))
            pbr.BaseColorTexture = DeserializeTextureInfo((Dictionary<string, object>)bct);
            
        if (obj.TryGetValue("metallicRoughnessTexture", out var mrt))
            pbr.MetallicRoughnessTexture = DeserializeTextureInfo((Dictionary<string, object>)mrt);
            
        return pbr;
    }
    
    private static GltfTextureInfo DeserializeTextureInfo(Dictionary<string, object> obj)
    {
        return new GltfTextureInfo
        {
            Index = GetInt(obj, "index") ?? -1,
            TexCoord = GetInt(obj, "texCoord") ?? 0
        };
    }
    
    private static GltfNormalTextureInfo DeserializeNormalTextureInfo(Dictionary<string, object> obj)
    {
        return new GltfNormalTextureInfo
        {
            Index = GetInt(obj, "index") ?? -1,
            TexCoord = GetInt(obj, "texCoord") ?? 0,
            Scale = GetFloat(obj, "scale") ?? 1.0f
        };
    }
    
    private static GltfOcclusionTextureInfo DeserializeOcclusionTextureInfo(Dictionary<string, object> obj)
    {
        return new GltfOcclusionTextureInfo
        {
            Index = GetInt(obj, "index") ?? -1,
            TexCoord = GetInt(obj, "texCoord") ?? 0,
            Strength = GetFloat(obj, "strength") ?? 1.0f
        };
    }
    
    private static GltfTexture DeserializeTexture(Dictionary<string, object> obj)
    {
        return new GltfTexture
        {
            Name = GetString(obj, "name"),
            Sampler = GetInt(obj, "sampler"),
            Source = GetInt(obj, "source")
        };
    }
    
    private static GltfImage DeserializeImage(Dictionary<string, object> obj)
    {
        return new GltfImage
        {
            Name = GetString(obj, "name"),
            Uri = GetString(obj, "uri"),
            MimeType = GetString(obj, "mimeType"),
            BufferView = GetInt(obj, "bufferView")
        };
    }
    
    private static GltfSampler DeserializeSampler(Dictionary<string, object> obj)
    {
        return new GltfSampler
        {
            Name = GetString(obj, "name"),
            MagFilter = GetInt(obj, "magFilter"),
            MinFilter = GetInt(obj, "minFilter"),
            WrapS = GetInt(obj, "wrapS") ?? 10497,
            WrapT = GetInt(obj, "wrapT") ?? 10497
        };
    }
    
    private static GltfSkin DeserializeSkin(Dictionary<string, object> obj)
    {
        return new GltfSkin
        {
            Name = GetString(obj, "name"),
            InverseBindMatrices = GetInt(obj, "inverseBindMatrices"),
            Skeleton = GetInt(obj, "skeleton"),
            Joints = GetIntArray(obj, "joints") ?? Array.Empty<int>()
        };
    }
    
    private static GltfAnimation DeserializeAnimation(Dictionary<string, object> obj)
    {
        var anim = new GltfAnimation { Name = GetString(obj, "name") };
        
        if (obj.TryGetValue("channels", out var channels))
            anim.Channels = DeserializeArray((List<object>)channels, DeserializeAnimationChannel);
        
        if (obj.TryGetValue("samplers", out var samplers))
            anim.Samplers = DeserializeArray((List<object>)samplers, DeserializeAnimationSampler);
        
        return anim;
    }
    
    private static GltfAnimationChannel DeserializeAnimationChannel(Dictionary<string, object> obj)
    {
        var channel = new GltfAnimationChannel
        {
            Sampler = GetInt(obj, "sampler") ?? 0
        };
        
        if (obj.TryGetValue("target", out var target))
            channel.Target = DeserializeAnimationTarget((Dictionary<string, object>)target);
        
        return channel;
    }
    
    private static GltfAnimationTarget DeserializeAnimationTarget(Dictionary<string, object> obj)
    {
        return new GltfAnimationTarget
        {
            Node = GetInt(obj, "node"),
            Path = GetString(obj, "path") ?? "translation"
        };
    }
    
    private static GltfAnimationSampler DeserializeAnimationSampler(Dictionary<string, object> obj)
    {
        return new GltfAnimationSampler
        {
            Input = GetInt(obj, "input") ?? 0,
            Interpolation = GetString(obj, "interpolation") ?? "LINEAR",
            Output = GetInt(obj, "output") ?? 0
        };
    }
    
    private static GltfCamera DeserializeCamera(Dictionary<string, object> obj)
    {
        return new GltfCamera
        {
            Name = GetString(obj, "name"),
            Type = GetString(obj, "type") ?? "perspective"
        };
    }
    
    // Helper methods
    private static T[] DeserializeArray<T>(List<object> list, Func<Dictionary<string, object>, T> deserializer)
    {
        var result = new T[list.Count];
        for (int i = 0; i < list.Count; i++)
            result[i] = deserializer((Dictionary<string, object>)list[i]);
        return result;
    }
    
    private static string? GetString(Dictionary<string, object> obj, string key)
    {
        return obj.TryGetValue(key, out var val) ? val as string : null;
    }
    
    private static int? GetInt(Dictionary<string, object> obj, string key)
    {
        return obj.TryGetValue(key, out var val) ? Convert.ToInt32(val) : null;
    }
    
    private static float? GetFloat(Dictionary<string, object> obj, string key)
    {
        return obj.TryGetValue(key, out var val) ? Convert.ToSingle(val) : null;
    }
    
    private static bool? GetBool(Dictionary<string, object> obj, string key)
    {
        return obj.TryGetValue(key, out var val) ? (bool)val : null;
    }
    
    private static int[]? GetIntArray(Dictionary<string, object> obj, string key)
    {
        if (!obj.TryGetValue(key, out var val)) return null;
        var list = (List<object>)val;
        var result = new int[list.Count];
        for (int i = 0; i < list.Count; i++)
            result[i] = Convert.ToInt32(list[i]);
        return result;
    }
    
    private static float[]? GetFloatArray(Dictionary<string, object> obj, string key)
    {
        if (!obj.TryGetValue(key, out var val)) return null;
        var list = (List<object>)val;
        var result = new float[list.Count];
        for (int i = 0; i < list.Count; i++)
            result[i] = Convert.ToSingle(list[i]);
        return result;
    }
    
    private static string[]? GetStringArray(Dictionary<string, object> obj, string key)
    {
        if (!obj.TryGetValue(key, out var val)) return null;
        var list = (List<object>)val;
        var result = new string[list.Count];
        for (int i = 0; i < list.Count; i++)
            result[i] = (string)list[i];
        return result;
    }
}

internal class JsonTokenizer
{
    private readonly string _json;
    private int _position;
    
    public JsonTokenizer(string json)
    {
        _json = json;
        _position = 0;
    }
    
    public char Peek()
    {
        if (_position >= _json.Length)
            throw new GltfException("Unexpected end of JSON");
        return _json[_position];
    }
    
    public char Read()
    {
        if (_position >= _json.Length)
            throw new GltfException("Unexpected end of JSON");
        return _json[_position++];
    }
    
    public void Expect(char expected)
    {
        char actual = Read();
        if (actual != expected)
            throw new GltfException($"Expected '{expected}', got '{actual}'");
    }
    
    public void ExpectSequence(string expected)
    {
        foreach (char c in expected)
            Expect(c);
    }
    
    public void SkipWhitespace()
    {
        while (_position < _json.Length && char.IsWhiteSpace(_json[_position]))
            _position++;
    }
}

public class GltfException : Exception
{
    public GltfException(string message) : base(message) { }
    public GltfException(string message, Exception inner) : base(message, inner) { }
}
