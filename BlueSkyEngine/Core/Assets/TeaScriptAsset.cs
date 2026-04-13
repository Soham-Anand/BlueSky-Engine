using System;
using System.IO;
using System.Text;

namespace BlueSky.Core.Assets;

/// <summary>
/// Represents a TeaScript (.tea) asset.
/// Simplified version for initial integration.
/// </summary>
public class TeaScriptAsset
{
    public string AssetId { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public string SourceCode { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    
    /// <summary>
    /// Create a new TeaScript asset.
    /// </summary>
    public static TeaScriptAsset Create(string name, string sourceCode = "")
    {
        if (string.IsNullOrEmpty(sourceCode))
        {
            sourceCode = GenerateDefaultScript(name);
        }
        
        return new TeaScriptAsset
        {
            AssetId = Guid.NewGuid().ToString(),
            AssetName = name,
            SourceCode = sourceCode,
            LastModified = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Load a TeaScript asset from a .tea file.
    /// </summary>
    public static TeaScriptAsset LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"TeaScript file not found: {filePath}");
        }
        
        string sourceCode = File.ReadAllText(filePath);
        string name = Path.GetFileNameWithoutExtension(filePath);
        
        return new TeaScriptAsset
        {
            AssetId = Guid.NewGuid().ToString(),
            AssetName = name,
            SourceCode = sourceCode,
            LastModified = File.GetLastWriteTimeUtc(filePath)
        };
    }
    
    /// <summary>
    /// Save the script to a file.
    /// </summary>
    public void SaveToFile(string filePath)
    {
        File.WriteAllText(filePath, SourceCode, Encoding.UTF8);
        LastModified = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Load from asset ID (stub for now - would integrate with asset database).
    /// </summary>
    public static TeaScriptAsset LoadFromAsset(string assetId)
    {
        // TODO: Integrate with actual asset database
        // For now, return a simple test script
        return new TeaScriptAsset
        {
            AssetId = assetId,
            AssetName = "TestScript",
            SourceCode = @"
let counter = 0

fn start() {
    log(""Script started!"")
}

fn update() {
    counter = counter + 1
}
",
            LastModified = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Generate a default script template.
    /// </summary>
    private static string GenerateDefaultScript(string name)
    {
        return $@"// {name} - TeaScript
// This script controls entity behavior

// Variables
let speed = 10
let health = 100

// Called once when entity spawns
fn start() {{
    log(""Entity started: {name}"")
}}

// Called every frame
fn update() {{
    // Get delta time
    let dt = getDeltaTime()
    
    // Your logic here
}}

// Custom functions
fn takeDamage(amount) {{
    health = health - amount
    log(""Health: "" + health)
    
    if (health <= 0) {{
        destroy()
    }}
    
    return health
}}
";
    }
}
