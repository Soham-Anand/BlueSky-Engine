using System;
using BlueSky.Ramanujan;
using System.Collections.Generic;
using System.IO;
using BlueSky.Meridian;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;

namespace BlueSky.Editor;

/// <summary>
/// Developer prefab workflow manager for creating, caching, and instantiating
/// reusable entity templates (<c>.bseprefab</c> / <c>.bsprefab</c>) into a <see cref="World"/>.
/// Supports both single-entity prefabs and multi-entity parent-child hierarchies.
/// </summary>
public static class PrefabManager
{
    private static readonly Dictionary<string, SceneData> s_prefabCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Saves a root entity and any direct children attached via <see cref="ParentComponent"/>
    /// to a prefab file on disk.
    /// </summary>
    public static bool SavePrefab(World world, Entity rootEntity, string filePath)
    {
        if (world == null || !world.IsEntityValid(rootEntity) || string.IsNullOrWhiteSpace(filePath))
            return false;

        string name = Path.GetFileNameWithoutExtension(filePath);
        var prefabData = SceneConverter.WorldToSceneDataForEntity(world, rootEntity, name);

        // Also include direct children that reference rootEntity as their ParentComponent
        var childQuery = world.CreateQuery().All<ParentComponent>().Build();
        foreach (var chunk in world.EnumerateQueryChunks(childQuery))
        {
            int parentIdx = chunk.GetComponentIndex(typeof(ParentComponent));
            var parents = chunk.GetComponentSpan<ParentComponent>(parentIdx);
            var entities = chunk.GetEntities();

            for (int i = 0; i < entities.Length; i++)
            {
                if (parents[i].Parent == rootEntity)
                {
                    var childScene = SceneConverter.WorldToSceneDataForEntity(world, entities[i], $"Child_{entities[i].Id}");
                    if (childScene.Entities.Count > 0)
                        prefabData.Entities.Add(childScene.Entities[0]);
                }
            }
        }

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        SceneSerializer.SaveScene(prefabData, filePath);
        s_prefabCache[Path.GetFullPath(filePath)] = prefabData;
        return File.Exists(filePath);
    }

    /// <summary>
    /// Loads a prefab from disk (or returns the cached template).
    /// </summary>
    public static SceneData? LoadPrefab(string filePath, bool bypassCache = false)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        string fullPath = Path.GetFullPath(filePath);
        if (!bypassCache && s_prefabCache.TryGetValue(fullPath, out var cached))
            return cached;

        var loaded = SceneSerializer.LoadScene(fullPath);
        if (loaded != null)
            s_prefabCache[fullPath] = loaded;

        return loaded;
    }

    /// <summary>
    /// Instantiates a prefab from disk into <paramref name="world"/> at the specified world position and rotation.
    /// Returns the spawned root <see cref="Entity"/> (or <see cref="Entity.Null"/> on failure).
    /// </summary>
    public static Entity InstantiatePrefab(
        World world,
        string filePath,
        Vector3 position,
        Quaternion rotation)
    {
        var prefab = LoadPrefab(filePath);
        if (prefab == null)
            return Entity.Null;

        return InstantiatePrefab(world, prefab, position, rotation);
    }

    /// <summary>
    /// Instantiates an in-memory <see cref="SceneData"/> prefab into <paramref name="world"/>.
    /// </summary>
    public static Entity InstantiatePrefab(
        World world,
        SceneData prefabData,
        Vector3 position,
        Quaternion rotation)
    {
        if (world == null || prefabData == null || prefabData.Entities.Count == 0)
            return Entity.Null;

        var beforeIds = new HashSet<int>();
        foreach (var e in world.GetAllEntities())
            beforeIds.Add(e.Id);

        SceneConverter.SceneDataToWorld(prefabData, world, clearWorld: false);

        Entity rootEntity = Entity.Null;
        foreach (var e in world.GetAllEntities())
        {
            if (!beforeIds.Contains(e.Id))
            {
                if (rootEntity.IsNull || !world.HasComponent<ParentComponent>(e))
                {
                    rootEntity = e;
                    break;
                }
            }
        }

        if (!rootEntity.IsNull && world.HasComponent<TransformComponent>(rootEntity))
        {
            ref var t = ref world.GetComponent<TransformComponent>(rootEntity);
            t.Position = position;
            t.Rotation = rotation;
        }

        return rootEntity;
    }

    public static void ClearCache() => s_prefabCache.Clear();
}
