using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueSky.Core.Assets;

/// <summary>
/// BlueSky Project file (.blueproject)
/// This is the root file for your game project, like .uproject in Unreal.
/// </summary>
public class BlueProject
{
    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = "New BlueSky Project";

    [JsonPropertyName("engineVersion")]
    public string EngineVersion { get; set; } = "1.0.0";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("startupScene")]
    public string StartupScene { get; set; } = "";

    [JsonPropertyName("assetDirectory")]
    public string AssetDirectory { get; set; } = "Assets";

    [JsonPropertyName("contentDirectory")]
    public string ContentDirectory { get; set; } = "Content";

    [JsonPropertyName("settings")]
    public ProjectSettings Settings { get; set; } = new();

    [JsonPropertyName("plugins")]
    public List<string> Plugins { get; set; } = new();

    [JsonPropertyName("createdDate")]
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Save project to .blueproject file.
    /// </summary>
    public bool Save(string path)
    {
        try
        {
            LastModified = DateTime.UtcNow;
            
            if (!path.EndsWith(".BlueSkyProj", StringComparison.OrdinalIgnoreCase))
            {
                path += ".BlueSkyProj";
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(path, json);

            Console.WriteLine($"[BlueProject] ✓ Saved: {Path.GetFileName(path)}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BlueProject] ✗ Failed to save: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Load project from .blueproject file.
    /// </summary>
    public static BlueProject? Load(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*.BlueSkyProj");
                if (files.Length == 0)
                {
                    Console.WriteLine($"[BlueProject] ✗ No .BlueSkyProj file found in directory: {path}");
                    return null;
                }
                path = files[0];
            }

            if (!File.Exists(path))
            {
                Console.WriteLine($"[BlueProject] ✗ File not found: {path}");
                return null;
            }

            var json = File.ReadAllText(path);
            var project = JsonSerializer.Deserialize<BlueProject>(json);

            if (project != null)
            {
                Console.WriteLine($"[BlueProject] ✓ Loaded: {project.ProjectName}");
            }

            return project;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BlueProject] ✗ Failed to load: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Create a new project with default structure.
    /// </summary>
    public static BlueProject CreateNew(string projectName, string directory)
    {
        var project = new BlueProject
        {
            ProjectName = projectName,
            CreatedDate = DateTime.UtcNow,
            LastModified = DateTime.UtcNow
        };

        // Create directory structure
        var projectDir = Path.Combine(directory, projectName);
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets"));
        Directory.CreateDirectory(Path.Combine(projectDir, "Content"));
        Directory.CreateDirectory(Path.Combine(projectDir, "Content", "Meshes"));
        Directory.CreateDirectory(Path.Combine(projectDir, "Content", "Textures"));
        Directory.CreateDirectory(Path.Combine(projectDir, "Content", "Materials"));
        Directory.CreateDirectory(Path.Combine(projectDir, "Content", "Scenes"));
        Directory.CreateDirectory(Path.Combine(projectDir, "Content", "Scripts"));
        Directory.CreateDirectory(Path.Combine(projectDir, "Intermediate"));
        Directory.CreateDirectory(Path.Combine(projectDir, "Saved"));

        // Save project file
        var projectPath = Path.Combine(projectDir, $"{projectName}.BlueSkyProj");
        project.Save(projectPath);

        Console.WriteLine($"[BlueProject] Created new project: {projectName}");
        Console.WriteLine($"[BlueProject] Location: {projectDir}");

        return project;
    }

    /// <summary>
    /// Get the full path to the project directory.
    /// </summary>
    public string GetProjectDirectory(string projectFilePath)
    {
        if (Directory.Exists(projectFilePath))
        {
            return projectFilePath;
        }
        return Path.GetDirectoryName(projectFilePath) ?? "";
    }

    /// <summary>
    /// Get the full path to the Assets directory.
    /// </summary>
    public string GetAssetsDirectory(string projectFilePath)
    {
        return Path.Combine(GetProjectDirectory(projectFilePath), AssetDirectory);
    }

    /// <summary>
    /// Get the full path to the Content directory.
    /// </summary>
    public string GetContentDirectory(string projectFilePath)
    {
        return Path.Combine(GetProjectDirectory(projectFilePath), ContentDirectory);
    }
}

public class ProjectSettings
{
    [JsonPropertyName("targetFramerate")]
    public int TargetFramerate { get; set; } = 60;

    [JsonPropertyName("defaultQuality")]
    public string DefaultQuality { get; set; } = "High";

    [JsonPropertyName("enableVSync")]
    public bool EnableVSync { get; set; } = true;

    [JsonPropertyName("defaultResolution")]
    public Resolution DefaultResolution { get; set; } = new() { Width = 1920, Height = 1080 };

    [JsonPropertyName("enableMultithreading")]
    public bool EnableMultithreading { get; set; } = true;

    [JsonPropertyName("maxWorkerThreads")]
    public int MaxWorkerThreads { get; set; } = -1; // -1 = auto-detect
}

public class Resolution
{
    [JsonPropertyName("width")]
    public int Width { get; set; } = 1920;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 1080;
}
