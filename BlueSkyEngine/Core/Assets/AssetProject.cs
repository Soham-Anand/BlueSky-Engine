using System;
using System.Text.Json.Serialization;

namespace BlueSky.Core.Assets
{
    public class AssetProject
    {
        [JsonPropertyName("ProjectName")]
        public string ProjectName { get; set; } = "New Project";

        [JsonPropertyName("EngineVersion")]
        public string EngineVersion { get; set; } = "1.0.0";

        [JsonPropertyName("StartupScene")]
        public string StartupScene { get; set; } = string.Empty;

        [JsonPropertyName("Description")]
        public string Description { get; set; } = string.Empty;
    }
}
