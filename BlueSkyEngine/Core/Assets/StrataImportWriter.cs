using System;
using System.Collections.Generic;
using System.IO;
using BlueSky.Rendering.Strata;

namespace BlueSky.Core.Assets;

/// <summary>
/// Strata-owned import output: the Materials/{StrataFiles,Textures} layout,
/// texture blob writer, .stratamat writer, and slot→file link bookkeeping.
/// Mesh metadata carries only string links (strataSlot{i}); bytes live in files.
/// </summary>
public static class StrataImportWriter
{
    public const string MaterialsDirName = "Materials";
    public const string StrataFilesDirName = "StrataFiles";
    public const string TexturesDirName = "Textures";

    public static (string StrataDir, string TexturesDir) EnsureMaterialsLayout(string meshDir)
    {
        string materials = Path.Combine(meshDir, MaterialsDirName);
        string strata = Path.Combine(materials, StrataFilesDirName);
        string textures = Path.Combine(materials, TexturesDirName);
        Directory.CreateDirectory(strata);
        Directory.CreateDirectory(textures);
        return (strata, textures);
    }

    /// <summary>Writes one RGBA8 texture blob (.blueskyasset). Returns the path.</summary>
    public static string WriteTextureBlob(string texturesDir, string name, int width, int height, byte[] rgba)
    {
        string safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe)) safe = "Texture";
        string path = Path.Combine(texturesDir, $"{safe}.blueskyasset");

        var asset = new BlueAsset { AssetName = safe, Type = AssetType.Texture, ImportDate = DateTime.UtcNow };
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(width);
        w.Write(height);
        w.Write(4);
        w.Write(rgba.Length);
        w.Write(rgba);
        asset.PayloadData = ms.ToArray();
        asset.Metadata["width"] = width.ToString();
        asset.Metadata["height"] = height.ToString();
        asset.Metadata["format"] = "RGBA8";
        asset.Save(path);
        return path;
    }

    /// <summary>Encodes + writes one .stratamat. Returns the path.</summary>
    public static string WriteStrataMaterial(string strataDir, StrataMaterial material)
    {
        string safe = string.Join("_", material.Name.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe)) safe = "slot";
        string path = Path.Combine(strataDir, $"{safe}.stratamat");
        material.Save(path);
        return path;
    }

    public static void LinkSlot(Dictionary<string, string> metadata, int slot, string stratamatPath)
        => metadata[$"strataSlot{slot}"] = stratamatPath;

    public static string? TryGetSlotLink(Dictionary<string, string> metadata, int slot)
        => metadata.TryGetValue($"strataSlot{slot}", out var path) && !string.IsNullOrWhiteSpace(path)
            ? path : null;

    /// <summary>Decodes an image file (png/jpg/bmp/tga) to RGBA8 via Stb.</summary>
    public static (int Width, int Height, byte[] Rgba)? DecodeImageFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            StbImageSharp.StbImage.stbi_set_flip_vertically_on_load(0);
            using var stream = File.OpenRead(path);
            var image = StbImageSharp.ImageResult.FromStream(
                stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            if (image == null || image.Width <= 0 || image.Height <= 0)
                return null;
            return (image.Width, image.Height, image.Data);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a texture filename against a source directory (case-insensitive)
    /// and decodes it. Returns null with no throw when missing/unreadable.
    /// </summary>
    public static (int Width, int Height, byte[] Rgba)? LoadRoleImage(string sourceDir, string fileName)
    {
        try
        {
            string candidate = Path.IsPathRooted(fileName)
                ? fileName
                : Path.Combine(sourceDir, fileName.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(candidate))
            {
                string? insensitive = FindFileInsensitive(sourceDir, Path.GetFileName(fileName));
                if (insensitive == null) return null;
                candidate = insensitive;
            }
            return DecodeImageFile(candidate);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindFileInsensitive(string directory, string fileName)
    {
        try
        {
            if (!Directory.Exists(directory)) return null;
            foreach (var file in Directory.GetFiles(directory))
            {
                if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Decodes in-memory image bytes (FBX embedded content, data URIs).</summary>
    public static (int Width, int Height, byte[] Rgba)? DecodeImageBytes(byte[] data)
    {
        try
        {
            StbImageSharp.StbImage.stbi_set_flip_vertically_on_load(0);
            using var stream = new MemoryStream(data);
            var image = StbImageSharp.ImageResult.FromStream(
                stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            if (image == null || image.Width <= 0 || image.Height <= 0)
                return null;
            return (image.Width, image.Height, image.Data);
        }
        catch
        {
            return null;
        }
    }
}
