using System;
using BlueSky.Ramanujan;
using System.Collections.Generic;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;

namespace BlueSky.Meridian;

/// <summary>
/// SkyWorld spatial chunking and cell residency streamer.
/// Partitions entities into discrete 2D (X, Z) cells of configurable world size (default 128m),
/// updates entity cell assignments when transforms move across cell boundaries, and streams
/// cell snapshots in and out of the SkyCore World based on distance to active streaming anchors.
/// </summary>
public sealed class WorldStreamer : SystemBase
{
    private readonly Dictionary<WorldCellCoord, List<EntityData>> _dormantCellSnapshots = new();
    private readonly HashSet<WorldCellCoord> _loadedCells = new();
    private readonly List<Vector3> _anchors = new();
    private QueryDescription _cellTrackedQuery;
    private QueryDescription _untaggedTransformQuery;

    /// <summary>Width/depth of each spatial world cell in meters.</summary>
    public float CellSize { get; set; } = 128.0f;

    /// <summary>Chebyshev cell radius around anchors where full simulation (Physics + Scripts) runs.</summary>
    public int ActiveSimulationRadius { get; set; } = 2;

    /// <summary>Chebyshev cell radius around anchors where LOD proxies remain visible.</summary>
    public int VisibleLodRadius { get; set; } = 4;

    /// <summary>Chebyshev cell radius beyond which non-persistent entities are snapshot-saved and evicted from SkyCore.</summary>
    public int UnloadRadius { get; set; } = 6;

    /// <summary>Number of cells currently resident in the active SkyCore World.</summary>
    public int LoadedCellCount => _loadedCells.Count;

    /// <summary>Number of cells currentlyhibernated in serialized snapshot storage.</summary>
    public int DormantCellCount => _dormantCellSnapshots.Count;

    public WorldStreamer(float cellSize = 128.0f)
    {
        CellSize = MathF.Max(8.0f, cellSize);
    }

    protected override void OnInitialize()
    {
        if (World != null)
        {
            _cellTrackedQuery = World.CreateQuery()
                .All<TransformComponent>()
                .All<WorldCellComponent>()
                .Build();

            _untaggedTransformQuery = World.CreateQuery()
                .All<TransformComponent>()
                .None<WorldCellComponent>()
                .None<EditorOnlyTagComponent>()
                .Build();
        }
    }

    /// <summary>
    /// Sets a single streaming anchor position (e.g., player or editor camera).
    /// </summary>
    public void SetStreamingAnchor(Vector3 worldPosition)
    {
        _anchors.Clear();
        _anchors.Add(worldPosition);
    }

    /// <summary>
    /// Sets multiple streaming anchor positions (e.g., all connected players on a multiplayer host).
    /// </summary>
    public void SetStreamingAnchors(ReadOnlySpan<Vector3> worldPositions)
    {
        _anchors.Clear();
        for (int i = 0; i < worldPositions.Length; i++)
            _anchors.Add(worldPositions[i]);
    }

    /// <summary>
    /// Assigns <see cref="WorldCellComponent"/> tags to any transform entities that do not yet have one.
    /// Entities with <see cref="NetworkManagerComponent"/>, <see cref="CameraComponent"/>, or <see cref="LightComponent"/>
    /// default to <c>IsPersistent = true</c>.
    /// </summary>
    public void PartitionUntaggedEntities()
    {
        if (World == null)
            return;

        var cmd = World.CreateCommandBuffer();
        foreach (var chunk in World.EnumerateQueryChunks(_untaggedTransformQuery))
        {
            int transformIdx = chunk.GetComponentIndex(typeof(TransformComponent));
            bool isPersistentArchetype =
                chunk.Archetype.HasComponent(typeof(NetworkManagerComponent)) ||
                chunk.Archetype.HasComponent(typeof(CameraComponent)) ||
                chunk.Archetype.HasComponent(typeof(LightComponent)) ||
                chunk.Archetype.HasComponent(typeof(TerrainComponent));

            var entities = chunk.GetEntities();
            var transforms = chunk.GetComponentSpan<TransformComponent>(transformIdx);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var pos = transforms[i].Position;
                var coord = WorldCellCoord.FromWorldPosition(pos.X, pos.Z, CellSize);
                cmd.AddComponent(entity, new WorldCellComponent(coord, isPersistentArchetype));
                _loadedCells.Add(coord);
            }
        }
        cmd.Playback(World);
    }

    public override void Update(float dt)
    {
        if (World == null)
            return;

        PartitionUntaggedEntities();

        if (_anchors.Count == 0)
            return;

        // Compute anchor cell coordinates
        Span<WorldCellCoord> anchorCells = _anchors.Count <= 16
            ? stackalloc WorldCellCoord[_anchors.Count]
            : new WorldCellCoord[_anchors.Count];

        for (int i = 0; i < _anchors.Count; i++)
        {
            anchorCells[i] = WorldCellCoord.FromWorldPosition(_anchors[i].X, _anchors[i].Z, CellSize);
        }

        // 1. Stream in any dormant cells that are now within VisibleLodRadius of any anchor
        if (_dormantCellSnapshots.Count > 0)
        {
            var cellsToWake = new List<WorldCellCoord>();
            foreach (var dormantCoord in _dormantCellSnapshots.Keys)
            {
                int minDist = GetMinCellDistance(dormantCoord, anchorCells);
                if (minDist <= VisibleLodRadius)
                    cellsToWake.Add(dormantCoord);
            }

            foreach (var coord in cellsToWake)
            {
                WakeCell(coord);
            }
        }

        // 2. Update cell coordinates and residency tiers for live entities; hibernate entities beyond UnloadRadius
        var entitiesToEvict = new List<(Entity entity, WorldCellCoord coord)>();
        _loadedCells.Clear();

        foreach (var chunk in World.EnumerateQueryChunks(_cellTrackedQuery))
        {
            int transformIdx = chunk.GetComponentIndex(typeof(TransformComponent));
            int cellIdx = chunk.GetComponentIndex(typeof(WorldCellComponent));

            var entities = chunk.GetEntities();
            var transforms = chunk.GetComponentSpan<TransformComponent>(transformIdx);
            var cells = chunk.GetComponentSpan<WorldCellComponent>(cellIdx);

            for (int i = 0; i < entities.Length; i++)
            {
                ref var cellComp = ref cells[i];
                var pos = transforms[i].Position;
                var currentCoord = WorldCellCoord.FromWorldPosition(pos.X, pos.Z, CellSize);
                cellComp.Cell = currentCoord;

                if (cellComp.IsPersistent)
                {
                    cellComp.Tier = WorldResidencyTier.ActiveSimulation;
                    _loadedCells.Add(currentCoord);
                    continue;
                }

                int dist = GetMinCellDistance(currentCoord, anchorCells);
                if (dist <= ActiveSimulationRadius)
                {
                    cellComp.Tier = WorldResidencyTier.ActiveSimulation;
                    _loadedCells.Add(currentCoord);
                }
                else if (dist <= VisibleLodRadius)
                {
                    cellComp.Tier = WorldResidencyTier.VisibleLodProxy;
                    _loadedCells.Add(currentCoord);
                }
                else if (dist > UnloadRadius)
                {
                    cellComp.Tier = WorldResidencyTier.Unloaded;
                    entitiesToEvict.Add((entities[i], currentCoord));
                }
                else
                {
                    _loadedCells.Add(currentCoord);
                }
            }
        }

        // 3. Snapshot and unload entities that crossed outside UnloadRadius
        foreach (var (entity, coord) in entitiesToEvict)
        {
            HibernateEntity(entity, coord);
        }

        LastSystemVersion = World.Version;
    }

    private void HibernateEntity(Entity entity, WorldCellCoord coord)
    {
        if (World == null || !World.IsEntityValid(entity))
            return;

        var snapshotScene = SceneConverter.WorldToSceneDataForEntity(World, entity, $"Cell_{coord.X}_{coord.Z}");
        if (snapshotScene.Entities.Count > 0)
        {
            if (!_dormantCellSnapshots.TryGetValue(coord, out var list))
            {
                list = new List<EntityData>();
                _dormantCellSnapshots[coord] = list;
            }
            list.Add(snapshotScene.Entities[0]);
        }

        World.DestroyEntity(entity);
    }

    private void WakeCell(WorldCellCoord coord)
    {
        if (World == null || !_dormantCellSnapshots.Remove(coord, out var entityList))
            return;

        var cellScene = new SceneData
        {
            Name = $"StreamedCell_{coord.X}_{coord.Z}",
            WorldCellSize = CellSize,
            Entities = entityList
        };

        SceneConverter.SceneDataToWorld(cellScene, World, clearWorld: false);
        _loadedCells.Add(coord);
    }

    private static int GetMinCellDistance(WorldCellCoord cell, ReadOnlySpan<WorldCellCoord> anchors)
    {
        int min = int.MaxValue;
        for (int i = 0; i < anchors.Length; i++)
        {
            int d = cell.ChebyshevDistanceTo(anchors[i]);
            if (d < min)
                min = d;
        }
        return min;
    }
}
