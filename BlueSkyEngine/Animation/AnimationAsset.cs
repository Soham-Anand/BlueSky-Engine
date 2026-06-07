using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueSky.Animation;

/// <summary>
/// Animation asset serializer for .blueskyasset format.
/// Supports both skeletal meshes and animation clips.
/// </summary>
public static class AnimationAsset
{
    private const int MAGIC_NUMBER = 0x42534153; // "BSAS" = BlueSky Animation System
    private const int VERSION = 1;
    
    /// <summary>
    /// Save animation asset to .blueskyasset file
    /// </summary>
    public static void Save(string path, SkeletalMeshAsset asset)
    {
        try
        {
            var json = JsonSerializer.Serialize(asset, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            
            using var writer = new BinaryWriter(File.Create(path));
            
            // Write header
            writer.Write(MAGIC_NUMBER);
            writer.Write(VERSION);
            writer.Write((int)AssetType.SkeletalMesh);
            
            // Write JSON data
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
            writer.Write(jsonBytes.Length);
            writer.Write(jsonBytes);
            
            Console.WriteLine($"[NotBSAnimation] Saved asset: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotBSAnimation] Failed to save asset: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Load animation asset from .blueskyasset file
    /// </summary>
    public static SkeletalMeshAsset? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[NotBSAnimation] File not found: {path}");
                return null;
            }
            
            using var reader = new BinaryReader(File.OpenRead(path));
            
            // Read and validate header
            int magic = reader.ReadInt32();
            if (magic != MAGIC_NUMBER)
            {
                Console.WriteLine($"[NotBSAnimation] Invalid file format (magic: 0x{magic:X})");
                return null;
            }
            
            int version = reader.ReadInt32();
            if (version != VERSION)
            {
                Console.WriteLine($"[NotBSAnimation] Unsupported version: {version}");
                return null;
            }
            
            var assetType = (AssetType)reader.ReadInt32();
            if (assetType != AssetType.SkeletalMesh)
            {
                Console.WriteLine($"[NotBSAnimation] Wrong asset type: {assetType}");
                return null;
            }
            
            // Read JSON data
            int jsonLength = reader.ReadInt32();
            var jsonBytes = reader.ReadBytes(jsonLength);
            var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
            
            var asset = JsonSerializer.Deserialize<SkeletalMeshAsset>(json);
            
            Console.WriteLine($"[NotBSAnimation] Loaded asset: {path}");
            return asset;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotBSAnimation] Failed to load asset: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Save animation clip to .blueskyasset file
    /// </summary>
    public static void SaveClip(string path, AnimationClip clip)
    {
        try
        {
            var json = JsonSerializer.Serialize(clip, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            
            using var writer = new BinaryWriter(File.Create(path));
            
            // Write header
            writer.Write(MAGIC_NUMBER);
            writer.Write(VERSION);
            writer.Write((int)AssetType.AnimationClip);
            
            // Write JSON data
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
            writer.Write(jsonBytes.Length);
            writer.Write(jsonBytes);
            
            Console.WriteLine($"[NotBSAnimation] Saved animation clip: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotBSAnimation] Failed to save clip: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Load animation clip from .blueskyasset file
    /// </summary>
    public static AnimationClip? LoadClip(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[NotBSAnimation] File not found: {path}");
                return null;
            }
            
            using var reader = new BinaryReader(File.OpenRead(path));
            
            // Read and validate header
            int magic = reader.ReadInt32();
            if (magic != MAGIC_NUMBER)
            {
                Console.WriteLine($"[NotBSAnimation] Invalid file format");
                return null;
            }
            
            int version = reader.ReadInt32();
            var assetType = (AssetType)reader.ReadInt32();
            
            if (assetType != AssetType.AnimationClip)
            {
                Console.WriteLine($"[NotBSAnimation] Wrong asset type: {assetType}");
                return null;
            }
            
            // Read JSON data
            int jsonLength = reader.ReadInt32();
            var jsonBytes = reader.ReadBytes(jsonLength);
            var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
            
            var clip = JsonSerializer.Deserialize<AnimationClip>(json);
            
            Console.WriteLine($"[NotBSAnimation] Loaded animation clip: {path}");
            return clip;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotBSAnimation] Failed to load clip: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Detect asset type from file
    /// </summary>
    public static AssetType? DetectAssetType(string path)
    {
        try
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            
            int magic = reader.ReadInt32();
            if (magic != MAGIC_NUMBER) return null;
            
            reader.ReadInt32(); // version
            return (AssetType)reader.ReadInt32();
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Asset type identifier for .blueskyasset files
/// </summary>
public enum AssetType
{
    Unknown = 0,
    StaticMesh = 1,
    SkeletalMesh = 2,
    AnimationClip = 3,
    Material = 4,
    Texture = 5,
    Sound = 6,
    Scene = 7
}
