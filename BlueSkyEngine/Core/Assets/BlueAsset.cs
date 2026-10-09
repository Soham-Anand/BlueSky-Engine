using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueSky.Core.Assets;

/// <summary>
/// BlueSky Asset file (.blueskyasset)
/// This is an imported asset, like .uasset in Unreal.
/// Contains metadata + processed data ready for the engine.
/// </summary>
public class BlueAsset
{
    [JsonPropertyName("assetId")]
    public Guid AssetId { get; set; } = Guid.NewGuid();

    [JsonPropertyName("assetName")]
    public string AssetName { get; set; } = "Unnamed Asset";

    [JsonPropertyName("assetType")]
    public AssetType Type { get; set; } = AssetType.Unknown;

    [JsonPropertyName("sourceFile")]
    public string SourceFile { get; set; } = "";

    [JsonPropertyName("sourceFileHash")]
    public string SourceFileHash { get; set; } = "";

    [JsonPropertyName("importDate")]
    public DateTime ImportDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("importSettings")]
    public Dictionary<string, object> ImportSettings { get; set; } = new();

    [JsonPropertyName("dependencies")]
    public List<Guid> Dependencies { get; set; } = new();

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    [JsonPropertyName("dataFile")]
    public string DataFile { get; set; } = ""; // DEPRECATED: Path to external binary data file

    [JsonPropertyName("thumbnailFile")]
    public string ThumbnailFile { get; set; } = ""; // Path to thumbnail image

    [JsonIgnore]
    public byte[] PayloadData { get; set; } = Array.Empty<byte>();

    [JsonIgnore]
    public bool HasPayload => PayloadData != null && PayloadData.Length > 0;

    /// <summary>
    /// Save asset to single binary .blueskyasset file.
    /// </summary>
    public bool Save(string path)
    {
        try
        {
            LastModified = DateTime.UtcNow;

            if (!path.EndsWith(".blueskyasset"))
            {
                path += ".blueskyasset";
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true, // Easier debugging if we extract the payload
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var jsonString = JsonSerializer.Serialize(this, options);
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(jsonString);

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(fs);

            // Magic Bytes "BSAS"
            writer.Write((byte)'B');
            writer.Write((byte)'S');
            writer.Write((byte)'A');
            writer.Write((byte)'S');
            
            // Format Version (1)
            writer.Write((int)1);

            // JSON Metadata Length
            writer.Write((int)jsonBytes.Length);

            // JSON Metadata
            writer.Write(jsonBytes);

            // Payload Length
            int payloadLen = PayloadData?.Length ?? 0;
            writer.Write(payloadLen);

            // Payload
            if (payloadLen > 0)
            {
                writer.Write(PayloadData!);
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BlueAsset] ✗ Failed to save: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Load asset from proprietary binary or legacy JSON .blueskyasset file.
    /// </summary>
    public static BlueAsset? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            if (fs.Length == 0) return null;

            byte firstByte = reader.ReadByte();
            fs.Position = 0; // Reset position
            
            // '{' implies legacy plain JSON asset format
            if (firstByte == '{')
            {
                if (fs.Length > 100L * 1024 * 1024) return null;
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<BlueAsset>(json);
            }
            
            // Magic Check
            byte[] magic = reader.ReadBytes(4);
            if (magic[0] != 'B' || magic[1] != 'S' || magic[2] != 'A' || magic[3] != 'S')
            {
                Console.WriteLine($"[BlueAsset] ✗ Invalid asset magic header in: {path}");
                return null;
            }

            int formatVersion = reader.ReadInt32(); // Version 1
            if (formatVersion != 1) return null;

            int jsonLen = reader.ReadInt32();
            if (jsonLen <= 0 || jsonLen > 100 * 1024 * 1024 || jsonLen > fs.Length - fs.Position) // Sanity check 100MB string limit
            {
                Console.WriteLine($"[BlueAsset] ✗ Invalid JSON metadata size in: {path}");
                return null;
            }

            byte[] jsonBytes = reader.ReadBytes(jsonLen);
            if (jsonBytes.Length != jsonLen) return null;
            string jsonString = System.Text.Encoding.UTF8.GetString(jsonBytes);
            
            var asset = JsonSerializer.Deserialize<BlueAsset>(jsonString);
            if (asset == null) return null;

            int payloadLen = reader.ReadInt32();
            if (payloadLen < 0 || payloadLen > fs.Length - fs.Position) return null;
            if (payloadLen > 0)
            {
                asset.PayloadData = reader.ReadBytes(payloadLen);
                if (asset.PayloadData.Length != payloadLen) return null;
            }

            return asset;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BlueAsset] ✗ Failed to load: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Loads only the fast-path JSON header of the asset, ignoring the heavy binary payload.
    /// Ideal for Asset Browser list displays to hit 60fps.
    /// </summary>
    public static BlueAsset? LoadHeader(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            if (fs.Length == 0) return null;

            byte firstByte = reader.ReadByte();
            fs.Position = 0;
            
            // '{' implies legacy plain JSON asset format
            if (firstByte == '{')
            {
                if (fs.Length > 10L * 1024 * 1024) return null;
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<BlueAsset>(json);
            }
            
            // Magic Check
            byte[] magic = reader.ReadBytes(4);
            if (magic[0] != 'B' || magic[1] != 'S' || magic[2] != 'A' || magic[3] != 'S')
                return null;

            int formatVersion = reader.ReadInt32(); // Version 1
            if (formatVersion != 1) return null;
            int jsonLen = reader.ReadInt32();
            
            if (jsonLen <= 0 || jsonLen > 10 * 1024 * 1024 || jsonLen > fs.Length - fs.Position)
                return null;

            byte[] jsonBytes = reader.ReadBytes(jsonLen);
            if (jsonBytes.Length != jsonLen) return null;
            string jsonString = System.Text.Encoding.UTF8.GetString(jsonBytes);
            
            return JsonSerializer.Deserialize<BlueAsset>(jsonString);
        }
        catch
        {
            return null; // Silent catch for UI loops
        }
    }

    /// <summary>
    /// Check if source file has changed since import.
    /// </summary>
    public bool NeedsReimport()
    {
        if (!File.Exists(SourceFile))
        {
            return false; // Source file deleted
        }

        var currentHash = ComputeFileHash(SourceFile);
        return currentHash != SourceFileHash;
    }

    /// <summary>
    /// Compute MD5 hash of a file.
    /// </summary>
    public static string ComputeFileHash(string path)
    {
        try
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            using var stream = File.OpenRead(path);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }
}
