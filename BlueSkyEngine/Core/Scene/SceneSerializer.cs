using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueSky.Core.Scene;

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

    public static void SaveScene(SceneData scene, string path)
    {
        try
        {
            var json = JsonSerializer.Serialize(scene, _options);
            File.WriteAllText(path, json);
            Console.WriteLine($"[SceneSerializer] Saved scene to: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SceneSerializer] Failed to save scene: {ex.Message}");
        }
    }

    public static SceneData? LoadScene(string path)
    {
        try
        {
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
