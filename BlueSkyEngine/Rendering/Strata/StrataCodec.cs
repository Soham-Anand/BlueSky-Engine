using System;
using System.Text;
using System.Text.Json;

namespace BlueSky.Rendering.Strata;

/// <summary>
/// .stratamat binary codec.
///
/// Layout:
///   magic (8 bytes, "STRATAMT")
///   format_version (uint32 LE, currently 1)
///   header_size (uint64 LE)
///   payload_size (uint64 LE)
///   JSON header (header_size bytes, UTF-8)
///   binary payload (payload_size bytes)
///
/// The JSON describes what the payload means (slots, colorspaces, offsets).
/// Decode fails LOUDLY on any structural problem — never silent fallback.
/// </summary>
public static class StrataCodec
{
    public const uint CurrentVersion = 1;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("STRATAMT\0");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        IncludeFields = true,
    };

    // Internal header DTO: keeps texture refs + params, never the payload.
    private sealed class StrataHeader
    {
        public string Name { get; set; } = "";
        public float[] AlbedoLinear { get; set; } = Array.Empty<float>();
        public float Metallic { get; set; }
        public float Roughness { get; set; }
        public float AO { get; set; }
        public float[]? ClearcoatTintLinear { get; set; }
        public float[] EmissiveLinear { get; set; } = Array.Empty<float>();
        public float EmissiveIntensity { get; set; }
        public uint Features { get; set; }
        // Nullable so pre-alpha files (key missing → null) decode to opaque.
        public float? Alpha { get; set; }
        public StrataTextureRef[] Textures { get; set; } = Array.Empty<StrataTextureRef>();
    }

    public static byte[] Encode(StrataMaterial material)
    {
        var header = new StrataHeader
        {
            Name = material.Name,
            AlbedoLinear = new[] { material.AlbedoLinear.X, material.AlbedoLinear.Y, material.AlbedoLinear.Z },
            Metallic = material.Metallic,
            Roughness = material.Roughness,
            AO = material.AO,
            ClearcoatTintLinear = new[]
            {
                material.ClearcoatTintLinear.X,
                material.ClearcoatTintLinear.Y,
                material.ClearcoatTintLinear.Z,
            },
            EmissiveLinear = new[] { material.EmissiveLinear.X, material.EmissiveLinear.Y, material.EmissiveLinear.Z },
            EmissiveIntensity = material.EmissiveIntensity,
            Features = (uint)material.Features,
            Alpha = material.Alpha,
            Textures = material.Textures.ToArray(),
        };

        byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions);
        byte[] payload = material.Payload ?? Array.Empty<byte>();

        // Validate offsets before writing: every texture must fit the payload.
        foreach (var tex in header.Textures)
        {
            if (tex.PayloadOffset + tex.PayloadSize > (ulong)payload.Length)
                throw new InvalidOperationException(
                    $"Strata encode failed: texture '{tex.Slot}' range " +
                    $"[{tex.PayloadOffset}, {tex.PayloadOffset + tex.PayloadSize}) exceeds payload ({payload.Length} bytes).");
        }

        byte[] out_ = new byte[8 + 4 + 8 + 8 + headerBytes.Length + payload.Length];
        int o = 0;
        Buffer.BlockCopy(Magic, 0, out_, o, 8); o += 8;
        BitConverter.TryWriteBytes(new Span<byte>(out_, o, 4), CurrentVersion); o += 4;
        BitConverter.TryWriteBytes(new Span<byte>(out_, o, 8), (ulong)headerBytes.Length); o += 8;
        BitConverter.TryWriteBytes(new Span<byte>(out_, o, 8), (ulong)payload.Length); o += 8;
        Buffer.BlockCopy(headerBytes, 0, out_, o, headerBytes.Length); o += headerBytes.Length;
        Buffer.BlockCopy(payload, 0, out_, o, payload.Length);
        return out_;
    }

    public static StrataMaterial Decode(byte[] data)
    {
        if (data == null || data.Length < 28)
            throw new InvalidOperationException(
                $"Strata decode failed: {data?.Length ?? 0} bytes, need at least 28 for the container header.");

        for (int i = 0; i < 8; i++)
        {
            if (data[i] != Magic[i])
                throw new InvalidOperationException(
                    "Strata decode failed: bad magic. " +
                    "This is not a .stratamat file. Legacy material assets are unsupported by design.");
        }

        uint version = BitConverter.ToUInt32(data, 8);
        if (version != CurrentVersion)
            throw new InvalidOperationException(
                $"Strata decode failed: format version {version}, engine supports {CurrentVersion}. Re-export the asset.");

        ulong headerSize = BitConverter.ToUInt64(data, 12);
        ulong payloadSize = BitConverter.ToUInt64(data, 20);

        if (28UL + headerSize + payloadSize != (ulong)data.Length)
            throw new InvalidOperationException(
                $"Strata decode failed: declared sizes (header {headerSize} + payload {payloadSize}) " +
                $"do not match file length ({data.Length} bytes). File is truncated or corrupt.");

        StrataHeader? header;
        try
        {
            header = JsonSerializer.Deserialize<StrataHeader>(
                new ReadOnlySpan<byte>(data, 28, (int)headerSize), JsonOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Strata decode failed: header JSON is invalid: {ex.Message}");
        }
        if (header == null)
            throw new InvalidOperationException("Strata decode failed: header deserialized to null.");

        byte[] payload = new byte[payloadSize];
        Buffer.BlockCopy(data, 28 + (int)headerSize, payload, 0, (int)payloadSize);

        foreach (var tex in header.Textures)
        {
            if (tex.PayloadOffset + tex.PayloadSize > payloadSize)
                throw new InvalidOperationException(
                    $"Strata decode failed: texture '{tex.Slot}' range exceeds payload. File is corrupt.");
        }

        return new StrataMaterial
        {
            Name = header.Name,
            AlbedoLinear = Vec(header.AlbedoLinear, "AlbedoLinear"),
            Metallic = header.Metallic,
            Roughness = header.Roughness,
            AO = header.AO,
            ClearcoatTintLinear = header.ClearcoatTintLinear == null
                ? System.Numerics.Vector3.One
                : Vec(header.ClearcoatTintLinear, "ClearcoatTintLinear"),
            EmissiveLinear = Vec(header.EmissiveLinear, "EmissiveLinear"),
            EmissiveIntensity = header.EmissiveIntensity,
            Features = (StrataFeature)header.Features,
            Alpha = header.Alpha ?? 1f,
            Textures = new System.Collections.Generic.List<StrataTextureRef>(header.Textures),
            Payload = payload,
        };
    }

    private static System.Numerics.Vector3 Vec(float[] v, string field)
    {
        if (v == null || v.Length != 3)
            throw new InvalidOperationException(
                $"Strata decode failed: '{field}' must have exactly 3 components.");
        return new System.Numerics.Vector3(v[0], v[1], v[2]);
    }
}
