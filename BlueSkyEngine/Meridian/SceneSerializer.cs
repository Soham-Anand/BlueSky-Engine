using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueSky.Meridian;

/// <summary>
/// Handles serialization and deserialization of scenes to/from JSON.
/// </summary>
public static class SceneSerializer
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Writes the scene to disk. Returns false (and logs why) if the file could not be written,
    /// so callers can tell a real save from a failed one.
    /// </summary>
    public static bool SaveScene(SceneData scene, string path)
    {
        string tempPath = path + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(scene, _options);

            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Write to a temp file first so a failed write never destroys an existing scene.
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);

            Console.WriteLine($"[SceneSerializer] Saved scene to: {path}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SceneSerializer] Failed to save scene '{path}': {ex.Message}");
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return false;
        }
    }

    public static SceneData? LoadScene(string path)
    {
        try
        {
            path = BlueSky.Core.Assets.AssetPathResolver.Resolve(path);
            if (!File.Exists(path))
            {
                Console.WriteLine($"[SceneSerializer] Scene file not found: {path}");
                return null;
            }

            var json = File.ReadAllText(path);
            var scene = JsonSerializer.Deserialize<SceneData>(json, _options);
            
            if (scene != null)
            {
                Console.WriteLine($"[SceneSerializer] Loaded scene: {scene.Name} ({scene.Entities.Count} entities)");
            }
            
            return scene;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SceneSerializer] Failed to load scene: {ex.Message}");
            return null;
        }
    }

    public static string SerializeToString(SceneData scene)
    {
        return JsonSerializer.Serialize(scene, _options);
    }

    public static SceneData? DeserializeFromString(string json)
    {
        return JsonSerializer.Deserialize<SceneData>(json, _options);
    }
}
