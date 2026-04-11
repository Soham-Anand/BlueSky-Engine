using System;
using System.Text.Json.Serialization;

namespace BlueSky.Core.Assets
{
    public enum AssetType
    {
        Unknown,
        Mesh, // deprecated
        StaticMesh,
        SkeletalMesh,
        Texture,
        Material,
        Shader,
        Scene,
        Prefab,
        Audio,
        Script
    }

    public class AssetMeta
    {
        [JsonPropertyName("AssetId")]
        public Guid AssetId { get; set; } = Guid.NewGuid();

        [JsonPropertyName("Type")]
        public AssetType Type { get; set; } = AssetType.Unknown;

        [JsonPropertyName("SourcePath")]
        public string SourcePath { get; set; } = string.Empty;
        
        [JsonPropertyName("ImportDate")]
        public DateTime ImportDate { get; set; } = DateTime.UtcNow;
    }
}
