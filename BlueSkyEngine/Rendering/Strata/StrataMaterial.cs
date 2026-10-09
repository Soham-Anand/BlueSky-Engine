using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Rendering.Strata;

/// <summary>
/// Strata feature mask. Each bit enables one material lobe. The offline
/// specialization step compiles one shader variant per mask value in use —
/// dead lobes are compiled out, never branched at runtime.
/// BentNormal is optional; BakedAO is the universal fallback.
/// </summary>
[Flags]
public enum StrataFeature : uint
{
    None           = 0,
    BakedAO        = 1u << 0,
    BentNormal     = 1u << 1,
    DetailNormal   = 1u << 2,
    WrappedDiffuse = 1u << 3,
    Toksvig        = 1u << 4,
    BumpOffset     = 1u << 5,
    Clearcoat      = 1u << 6,
    Sheen          = 1u << 7,
    Anisotropy     = 1u << 8,
    Iridescence    = 1u << 9,
    ScreenDoor     = 1u << 10,
    AlbedoMap      = 1u << 11,
    Transparent    = 1u << 12,
    Skin           = 1u << 13,
    Hair           = 1u << 14,
}

/// <summary>
/// Colorspace intent of one texture slot. Decided ONCE by the importer;
/// preserved by the codec; never inferred from slot names at runtime.
/// </summary>
public enum StrataColorspace
{
    Linear = 0,
    Srgb = 1,
}

/// <summary>
/// One texture reference inside a .stratamat payload.
/// </summary>
public class StrataTextureRef
{
    public string Slot { get; set; } = "";
    public StrataColorspace Colorspace { get; set; } = StrataColorspace.Linear;
    public int Width { get; set; }
    public int Height { get; set; }
    public string Format { get; set; } = "RGBA8";
    public ulong PayloadOffset { get; set; }
    public ulong PayloadSize { get; set; }
}

/// <summary>
/// Sole material representation of the engine. All colors stored LINEAR.
/// Authoring-time sRGB values must be linearized by the importer —
/// the codec never converts, it only preserves.
/// </summary>
public class StrataMaterial
{
    public string Name { get; set; } = "Unnamed";
    public Vector3 AlbedoLinear { get; set; } = Vector3.One;
    public float Metallic { get; set; }
    public float Roughness { get; set; } = 0.6f;
    public float AO { get; set; } = 1.0f;
    // Artist-authored coat tint, stored in linear space. White preserves the
    // legacy clearcoat response for materials/packs without this value.
    public Vector3 ClearcoatTintLinear { get; set; } = Vector3.One;
    public Vector3 EmissiveLinear { get; set; } = Vector3.Zero;
    public float EmissiveIntensity { get; set; }
    // Draw opacity. 1 = opaque. Persisted in the codec header; old files
    // without the key decode to 1 (see StrataCodec).
    public float Alpha { get; set; } = 1f;
    public StrataFeature Features { get; set; } = StrataFeature.BakedAO;
    public List<StrataTextureRef> Textures { get; set; } = new();

    /// <summary>Raw RGBA8 payload bytes, parallel to Textures[] via offsets.</summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public byte[] ToBytes() => StrataCodec.Encode(this);
    public static StrataMaterial FromBytes(byte[] data) => StrataCodec.Decode(data);

    public void Save(string path) => System.IO.File.WriteAllBytes(path, ToBytes());
    public static StrataMaterial Load(string path) => FromBytes(System.IO.File.ReadAllBytes(path));
}
