using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace BlueSky.Rendering.Strata;

/// <summary>
/// .stratapack container codec — the one and only engine import contract.
///
/// Layout:
///   magic "STRATAPK" (8B) | uint32 version (=1) | uint64 header_size
///   | uint64 payload_size | JSON header | binary payload
///
/// Phase-A lighting samples travel as explicit aomap[]/bent[] blob arrays
/// (with declared encodings) or, for vertex AO, as mesh attributes —
/// never disguised, never guessed. All validation fails loudly.
/// </summary>
public static class StrataPack
{
    public const uint CurrentVersion = 1;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("STRATAPK\0");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        IncludeFields = true,
        // Ease (Python) writes camelCase keys; accept both casings.
        PropertyNameCaseInsensitive = true,
    };

    public sealed class PackSubmesh
    {
        public int Offset { get; set; }
        public int Count { get; set; }
        public int Slot { get; set; }
    }

    public sealed class PackMesh
    {
        public string Name { get; set; } = "";
        public int VertexCount { get; set; }
        public int IndexCount { get; set; }
        public List<PackSubmesh> Submeshes { get; set; } = new();
        public ulong VertexOffset { get; set; }
        public ulong VertexSize { get; set; }
        public ulong IndexOffset { get; set; }
        public ulong IndexSize { get; set; }
        // Vertex AO (Phase A): per-vertex floats, NOT a texture blob.
        public ulong AoOffset { get; set; }
        public ulong AoSize { get; set; }
        public int AoCount { get; set; }
        // Skinning (skeletal): per-vertex 32B records (4×UInt32 bone index
        // + 4×Float32 weight, top-4 normalized). Absent (Size 0) = static.
        public ulong SkinOffset { get; set; }
        public ulong SkinSize { get; set; }
        public int SkinCount { get; set; }
        // Index into header Skeletons. -1 = static (no skeleton).
        public int SkeletonIndex { get; set; } = -1;
    }

    /// <summary>
    /// One bone in a pack skeleton. RestMatrix is 16 floats, row-major,
    /// bone-local (parent-relative) rest transform.
    /// </summary>
    public sealed class PackBone
    {
        public string Name { get; set; } = "";
        public int Parent { get; set; } = -1;
        public float[] RestMatrix { get; set; } = Array.Empty<float>();
    }

    public sealed class PackSkeleton
    {
        public string Name { get; set; } = "";
        public List<PackBone> Bones { get; set; } = new();
    }

    /// <summary>
    /// Phase-B light probe: fixed Fibonacci direction samples + analytic
    /// Blender lighting, projected to SH by the engine (ProjectSamples).
    /// C# owns the SH math so probes and sky capture share one estimator.
    /// </summary>
    public sealed class PackProbe
    {
        public string Name { get; set; } = "";
        public float[] Position { get; set; } = Array.Empty<float>();
        public float Radius { get; set; }
        public float[] SampleDirs { get; set; } = Array.Empty<float>();
        public float[] SampleColors { get; set; } = Array.Empty<float>();
    }

    public sealed class PackSurface
    {
        public int Slot { get; set; }
        public float[] AlbedoLinear { get; set; } = Array.Empty<float>();
        public float Metallic { get; set; }
        public float Roughness { get; set; }
        public float[] EmissiveLinear { get; set; } = Array.Empty<float>();
        public float EmissiveIntensity { get; set; }
        // Coat weight from the authoring tool (0 = none). Missing in old
        // packs → 0, so pre-clearcoat packs import unchanged.
        public float Clearcoat { get; set; }
        // Coat color is linear RGB. Missing in old packs → white (legacy clearcoat).
        public float[]? ClearcoatTintLinear { get; set; }
        // Cloth sheen flag from the authoring tool (0 = none). Missing → 0.
        public float Sheen { get; set; }
        // Brushed-metal flag from the authoring tool (0 = none). Missing → 0.
        public float Anisotropy { get; set; }
        // Draw opacity (1 = opaque). Missing in old packs → 1.
        public float Alpha { get; set; } = 1f;
    }

    public sealed class PackTexture
    {
        public int Slot { get; set; }
        public string Role { get; set; } = "";
        public string Colorspace { get; set; } = "linear";
        public int Width { get; set; }
        public int Height { get; set; }
        public ulong Offset { get; set; }
        public ulong Size { get; set; }
        // Mapping provenance (Ease): UV layer + transform, informational.
        // Null when unrecorded; the renderer does not depend on it (yet).
        public string? UvLayer { get; set; }
        public float[]? Transform { get; set; }
    }

    public sealed class PackAoMap
    {
        public int Slot { get; set; }
        public string Role { get; set; } = "ao";
        public string Colorspace { get; set; } = "linear";
        public int Width { get; set; }
        public int Height { get; set; }
        public ulong Offset { get; set; }
        public ulong Size { get; set; }
    }

    public sealed class PackBentMap
    {
        public int Slot { get; set; }
        public string Role { get; set; } = "bent";
        public string Colorspace { get; set; } = "linear";
        public string Encoding { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public ulong Offset { get; set; }
        public ulong Size { get; set; }
    }

    public sealed class PackHeader
    {
        public List<PackMesh> Meshes { get; set; } = new();
        public List<PackSurface> Surfaces { get; set; } = new();
        public List<PackTexture> Textures { get; set; } = new();
        public List<PackAoMap> AoMaps { get; set; } = new();
        public List<PackBentMap> BentMaps { get; set; } = new();
        // Skeletons (skeletal meshes): empty = all static, old packs decode here.
        public List<PackSkeleton> Skeletons { get; set; } = new();
        // Phase-B probes: empty = sky capture everywhere (old packs unaffected).
        public List<PackProbe> Probes { get; set; } = new();
        // SourceMap: per-slot provenance (Blender material → node path →
        // image → source file). Informational; unknown keys tolerated.
        public List<System.Text.Json.JsonElement> Sourcemap { get; set; } = new();
    }

    public sealed class PackFile
    {
        public PackHeader Header { get; set; } = new();
        public byte[] Payload { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Skin record stride: 4×UInt32 bone indices + 4×Float32 weights.
    /// Matches the skeletal shader's int4/float4 skinning inputs.
    /// </summary>
    public const int SkinStride = 32;

    /// <summary>Maximum skeleton size supported by the StrataPack importer.</summary>
    public const int MaxBones = 256;

    private static ulong Add(ulong a, ulong b, string field)
    {
        ulong r = a + b;
        if (r < a)
            throw new InvalidOperationException(
                $"StrataPack decode failed: integer overflow in '{field}'.");
        return r;
    }

    private static bool IsFinite3(float[] v)
    {
        if (v == null || v.Length != 3)
            return false;
        foreach (float f in v)
            if (float.IsNaN(f) || float.IsInfinity(f))
                return false;
        return true;
    }

    /// <summary>
    /// Skeletal validation (validations 13-15): skeleton bones + skin stream.
    /// Static meshes (no skeleton, no skin) skip everything — old packs
    /// decode here untouched.
    /// </summary>
    private static void ValidateSkeleton(PackHeader header, PackMesh mesh, byte[] payload)
    {
        bool hasSkin = mesh.SkinSize > 0 || mesh.SkinCount > 0;
        bool hasSkel = mesh.SkeletonIndex >= 0;
        if (!hasSkin && !hasSkel)
            return;
        if (!hasSkin || !hasSkel)
            throw new InvalidOperationException(
                $"StrataPack decode failed: mesh '{mesh.Name}' has skin without skeleton or vice versa. Both or neither.");
        if (mesh.SkeletonIndex >= header.Skeletons.Count)
            throw new InvalidOperationException(
                $"StrataPack decode failed: mesh '{mesh.Name}' skeleton index {mesh.SkeletonIndex} out of range ({header.Skeletons.Count}).");
        var skel = header.Skeletons[mesh.SkeletonIndex];
        if (skel.Bones.Count == 0 || skel.Bones.Count > MaxBones)
            throw new InvalidOperationException(
                $"StrataPack decode failed: skeleton '{skel.Name}' has {skel.Bones.Count} bones, need 1..{MaxBones}.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int b = 0; b < skel.Bones.Count; b++)
        {
            var bone = skel.Bones[b];
            if (string.IsNullOrWhiteSpace(bone.Name) || !names.Add(bone.Name))
                throw new InvalidOperationException(
                    $"StrataPack decode failed: skeleton '{skel.Name}' bone {b} has empty/duplicate name.");
            if (bone.Parent < -1 || bone.Parent >= skel.Bones.Count || bone.Parent == b)
                throw new InvalidOperationException(
                    $"StrataPack decode failed: skeleton '{skel.Name}' bone '{bone.Name}' has bad parent {bone.Parent}.");
            if (bone.RestMatrix == null || bone.RestMatrix.Length != 16)
                throw new InvalidOperationException(
                    $"StrataPack decode failed: skeleton '{skel.Name}' bone '{bone.Name}' rest matrix must be 16 floats.");
        }
        CheckRange(mesh.SkinOffset, mesh.SkinSize, (ulong)payload.Length, $"mesh '{mesh.Name}' skinning");
        if (mesh.SkinSize != (ulong)mesh.SkinCount * SkinStride)
            throw new InvalidOperationException(
                $"StrataPack decode failed: mesh '{mesh.Name}' skin bytes {mesh.SkinSize} != count {mesh.SkinCount} × {SkinStride}.");
        if (mesh.SkinCount != mesh.VertexCount)
            throw new InvalidOperationException(
                $"StrataPack decode failed: mesh '{mesh.Name}' skin count {mesh.SkinCount} != vertex count {mesh.VertexCount}.");
        for (int v = 0; v < mesh.SkinCount; v++)
        {
            int off = (int)mesh.SkinOffset + v * SkinStride;
            float sum = 0f;
            for (int k = 0; k < 4; k++)
            {
                uint bi = BitConverter.ToUInt32(payload, off + k * 4);
                float w = BitConverter.ToSingle(payload, off + 16 + k * 4);
                if (bi >= (uint)skel.Bones.Count)
                    throw new InvalidOperationException(
                        $"StrataPack decode failed: mesh '{mesh.Name}' vertex {v} bone index {bi} out of range ({skel.Bones.Count}).");
                if (float.IsNaN(w) || float.IsInfinity(w) || w < -1e-6f || w > 1f + 1e-6f)
                    throw new InvalidOperationException(
                        $"StrataPack decode failed: mesh '{mesh.Name}' vertex {v} has bad weight {w}.");
                sum += Math.Max(0f, w);
            }
            if (sum < 0.999f || sum > 1.001f)
                throw new InvalidOperationException(
                    $"StrataPack decode failed: mesh '{mesh.Name}' vertex {v} weights sum to {sum}, need ≈1.");
        }
    }
    private static void CheckRange(ulong offset, ulong size, ulong payloadSize, string field)
    {
        ulong end = Add(offset, size, field);
        if (end > payloadSize)
            throw new InvalidOperationException(
                $"StrataPack decode failed: '{field}' range [{offset}, {end}) exceeds payload ({payloadSize} bytes).");
    }

    public static byte[] Encode(PackFile pack)
    {
        byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(pack.Header, JsonOptions);
        byte[] payload = pack.Payload ?? Array.Empty<byte>();

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

    /// <summary>
    /// Header-only read: magic + version + JSON header, NO payload copy.
    /// Returns the header plus the absolute file offset where the payload
    /// starts, so readers can slice streams (skin, AO) straight from the
    /// .stratapack — the pack stays the database, no sidecar files.
    /// Structural validations (ranges, skeletons) are NOT run here; run
    /// full Decode at import time.
    /// </summary>
    public static (PackHeader Header, long PayloadBase) DecodeHeader(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (fs.Length < 28)
            throw new InvalidOperationException(
                $"StrataPack decode failed: {fs.Length} bytes, need at least 28 for the container header.");
        var head = new byte[28];
        int got = 0;
        while (got < 28)
        {
            int n = fs.Read(head, got, 28 - got);
            if (n <= 0) break;
            got += n;
        }
        if (got < 28)
            throw new InvalidOperationException("StrataPack decode failed: truncated container header.");
        for (int i = 0; i < 8; i++)
        {
            if (head[i] != Magic[i])
                throw new InvalidOperationException(
                    "StrataPack decode failed: bad magic. Not a .stratapack file — foreign formats are unsupported by design.");
        }
        uint version = BitConverter.ToUInt32(head, 8);
        if (version != CurrentVersion)
            throw new InvalidOperationException(
                $"StrataPack decode failed: format version {version}, engine supports {CurrentVersion}. Re-export from Ease.");
        ulong headerSize = BitConverter.ToUInt64(head, 12);
        ulong payloadSize = BitConverter.ToUInt64(head, 20);
        ulong total = Add(Add(28UL, headerSize, "header"), payloadSize, "payload");
        if (total != (ulong)fs.Length)
            throw new InvalidOperationException(
                $"StrataPack decode failed: declared sizes (header {headerSize} + payload {payloadSize}) " +
                $"do not match file length ({fs.Length} bytes). Truncated or corrupt.");
        var hbytes = new byte[headerSize];
        got = 0;
        while (got < (int)headerSize)
        {
            int n = fs.Read(hbytes, got, (int)headerSize - got);
            if (n <= 0) break;
            got += n;
        }
        if (got < (int)headerSize)
            throw new InvalidOperationException("StrataPack decode failed: truncated header JSON.");
        PackHeader? header;
        try
        {
            header = JsonSerializer.Deserialize<PackHeader>(hbytes, JsonOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"StrataPack decode failed: header JSON invalid: {ex.Message}");
        }
        if (header == null)
            throw new InvalidOperationException("StrataPack decode failed: header deserialized to null.");
        return (header, 28L + (long)headerSize);
    }

    public static PackFile Decode(byte[] data)
    {
        // 1. minimal length
        if (data == null || data.Length < 28)
            throw new InvalidOperationException(
                $"StrataPack decode failed: {data?.Length ?? 0} bytes, need at least 28 for the container header.");
        // 2. magic
        for (int i = 0; i < 8; i++)
        {
            if (data[i] != Magic[i])
                throw new InvalidOperationException(
                    "StrataPack decode failed: bad magic. Not a .stratapack file — foreign formats are unsupported by design.");
        }
        // 3. version
        uint version = BitConverter.ToUInt32(data, 8);
        if (version != CurrentVersion)
            throw new InvalidOperationException(
                $"StrataPack decode failed: format version {version}, engine supports {CurrentVersion}. Re-export from Ease.");
        // 4+5. header/payload bounds
        ulong headerSize = BitConverter.ToUInt64(data, 12);
        ulong payloadSize = BitConverter.ToUInt64(data, 20);
        ulong total = Add(Add(28UL, headerSize, "header"), payloadSize, "payload");
        if (total != (ulong)data.Length)
            throw new InvalidOperationException(
                $"StrataPack decode failed: declared sizes (header {headerSize} + payload {payloadSize}) " +
                $"do not match file length ({data.Length} bytes). Truncated or corrupt.");
        // 10. JSON
        PackHeader? header;
        try
        {
            header = JsonSerializer.Deserialize<PackHeader>(
                new ReadOnlySpan<byte>(data, 28, (int)headerSize), JsonOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"StrataPack decode failed: header JSON invalid: {ex.Message}");
        }
        if (header == null)
            throw new InvalidOperationException("StrataPack decode failed: header deserialized to null.");

        // Dead-key guard: pre-canonical Ease wrote "aomap"/"bent" tables that
        // never decoded (different names, not just case). Non-empty ones would
        // silently drop data — fail loudly and direct to re-export instead.
        try
        {
            using var doc = JsonDocument.Parse(
                new ReadOnlyMemory<byte>(data, 28, (int)headerSize));
            foreach (var dead in new[] { "aomap", "bent" })
            {
                if (doc.RootElement.TryGetProperty(dead, out var el) &&
                    el.ValueKind == JsonValueKind.Array && el.GetArrayLength() > 0)
                    throw new InvalidOperationException(
                        $"StrataPack decode failed: legacy non-empty '{dead}' table. Re-export from Ease (canonical key).");
            }
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"StrataPack decode failed: header JSON invalid: {ex.Message}");
        }

        byte[] payload = new byte[payloadSize];
        Buffer.BlockCopy(data, 28 + (int)headerSize, payload, 0, (int)payloadSize);

        foreach (var mesh in header.Meshes)
        {
            // 6. mesh index ranges (overflow-safe)
            CheckRange(mesh.VertexOffset, mesh.VertexSize, payloadSize, $"mesh '{mesh.Name}' vertices");
            CheckRange(mesh.IndexOffset, mesh.IndexSize, payloadSize, $"mesh '{mesh.Name}' indices");
            if (mesh.VertexSize != (ulong)mesh.VertexCount * 32UL)
                throw new InvalidOperationException(
                    $"StrataPack decode failed: mesh '{mesh.Name}' vertex bytes {mesh.VertexSize} != count {mesh.VertexCount} × 32 (Packed32).");
            if (mesh.IndexSize != (ulong)mesh.IndexCount * 4UL)
                throw new InvalidOperationException(
                    $"StrataPack decode failed: mesh '{mesh.Name}' index bytes {mesh.IndexSize} != count {mesh.IndexCount} × 4.");
            if (mesh.AoSize > 0 || mesh.AoCount > 0)
            {
                CheckRange(mesh.AoOffset, mesh.AoSize, payloadSize, $"mesh '{mesh.Name}' vertex AO");
                if (mesh.AoSize != (ulong)mesh.AoCount * 4UL)
                    throw new InvalidOperationException(
                        $"StrataPack decode failed: mesh '{mesh.Name}' AO bytes {mesh.AoSize} != count {mesh.AoCount} × 4 (float32).");
            }
            ValidateSkeleton(header, mesh, payload);
            // 8+9. submesh slots: ranges + per-mesh duplicate check
            foreach (var sub in mesh.Submeshes)
            {
                if (sub.Offset < 0 || sub.Count < 0 || sub.Offset + sub.Count > mesh.IndexCount)
                    throw new InvalidOperationException(
                        $"StrataPack decode failed: mesh '{mesh.Name}' submesh range [{sub.Offset}, {sub.Offset + sub.Count}) exceeds index count {mesh.IndexCount}.");
            }
            var meshSlots = new HashSet<int>();
            foreach (var sub in mesh.Submeshes)
            {
                if (!meshSlots.Add(sub.Slot))
                    throw new InvalidOperationException(
                        $"StrataPack decode failed: mesh '{mesh.Name}' has duplicate slot {sub.Slot}.");
            }
        }
        // 16. Phase-B probes: names, radius, sample integrity.
        {
            var probeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var probe in header.Probes)
            {
                if (string.IsNullOrWhiteSpace(probe.Name) || !probeNames.Add(probe.Name))
                    throw new InvalidOperationException(
                        "StrataPack decode failed: probe has empty/duplicate name.");
                if (probe.Position == null || probe.Position.Length != 3 ||
                    !IsFinite3(probe.Position))
                    throw new InvalidOperationException(
                        $"StrataPack decode failed: probe '{probe.Name}' position must be 3 finite floats.");
                if (float.IsNaN(probe.Radius) || float.IsInfinity(probe.Radius) || probe.Radius <= 0f)
                    throw new InvalidOperationException(
                        $"StrataPack decode failed: probe '{probe.Name}' radius must be finite and > 0.");
                if (probe.SampleDirs == null || probe.SampleColors == null ||
                    probe.SampleDirs.Length != probe.SampleColors.Length ||
                    probe.SampleDirs.Length % 3 != 0 ||
                    probe.SampleDirs.Length / 3 < StrataSkyProbe.MinProbeSamples)
                    throw new InvalidOperationException(
                        $"StrataPack decode failed: probe '{probe.Name}' needs ≥ {StrataSkyProbe.MinProbeSamples} matched dir/color samples.");
                for (int i = 0; i < probe.SampleDirs.Length; i += 3)
                {
                    float dx = probe.SampleDirs[i], dy = probe.SampleDirs[i + 1], dz = probe.SampleDirs[i + 2];
                    if (float.IsNaN(dx) || float.IsNaN(dy) || float.IsNaN(dz) ||
                        float.IsInfinity(dx) || float.IsInfinity(dy) || float.IsInfinity(dz))
                        throw new InvalidOperationException(
                            $"StrataPack decode failed: probe '{probe.Name}' sample {i / 3} direction is not finite.");
                    float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (len < 0.999f || len > 1.001f)
                        throw new InvalidOperationException(
                            $"StrataPack decode failed: probe '{probe.Name}' sample {i / 3} direction not normalized.");
                    float cr = probe.SampleColors[i], cg = probe.SampleColors[i + 1], cb = probe.SampleColors[i + 2];
                    if (float.IsNaN(cr) || float.IsNaN(cg) || float.IsNaN(cb) ||
                        float.IsInfinity(cr) || float.IsInfinity(cg) || float.IsInfinity(cb) ||
                        cr < 0f || cg < 0f || cb < 0f)
                        throw new InvalidOperationException(
                            $"StrataPack decode failed: probe '{probe.Name}' sample {i / 3} color invalid (NaN/Inf/negative).");
                }
            }
        }
        // 7. texture + AO + bent ranges
        foreach (var tex in header.Textures)
            CheckRange(tex.Offset, tex.Size, payloadSize, $"texture slot {tex.Slot} '{tex.Role}'");
        foreach (var ao in header.AoMaps)
            CheckRange(ao.Offset, ao.Size, payloadSize, $"aomap slot {ao.Slot}");
        foreach (var bent in header.BentMaps)
        {
            CheckRange(bent.Offset, bent.Size, payloadSize, $"bent slot {bent.Slot}");
            if (string.IsNullOrWhiteSpace(bent.Encoding))
                throw new InvalidOperationException(
                    $"StrataPack decode failed: bent slot {bent.Slot} declares no encoding. The importer never guesses normal spaces.");
        }

        return new PackFile { Header = header, Payload = payload };
    }
}
