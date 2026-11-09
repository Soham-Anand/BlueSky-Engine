using System;
using BlueSky.Meridian;
using BlueSky.BSR;
using BlueSky.SkyCore;

namespace BlueSky.Pulse;

/// <summary>
/// Snapshot of SkyCore ECS, SkyWorld streaming, and chunked terrain telemetry.
/// </summary>
public readonly struct SkyCoreTelemetrySnapshot
{
    public int EntityCount { get; init; }
    public int NonEmptyChunkCount { get; init; }
    public int TotalChunkCapacity { get; init; }
    public long AllocatedChunkBytes { get; init; }
    public uint WorldVersion { get; init; }
    public int LoadedWorldCells { get; init; }
    public int DormantWorldCells { get; init; }
    public int VisibleTerrainChunks { get; init; }
    public int CulledTerrainChunks { get; init; }
    public int RenderedTerrainTriangles { get; init; }

    public float ChunkOccupancyRatio =>
        TotalChunkCapacity > 0 ? (float)EntityCount / TotalChunkCapacity : 0f;

    public override string ToString() =>
        $"SkyCore: {EntityCount} entities in {NonEmptyChunkCount} chunks ({AllocatedChunkBytes / 1024} KB, {ChunkOccupancyRatio * 100f:F0}% full) | " +
        $"SkyWorld: {LoadedWorldCells} active cells, {DormantWorldCells} dormant | " +
        $"Terrain: {VisibleTerrainChunks} vis / {CulledTerrainChunks} culled chunks ({RenderedTerrainTriangles} tris)";
}

/// <summary>
/// Collects live diagnostics from SkyCore ECS, SkyWorld cell streaming, and TerrainRenderer.
/// </summary>
public static class SkyCoreDiagnostics
{
    public static SkyCoreTelemetrySnapshot Capture(
        World? world,
        WorldStreamer? streamer = null,
        TerrainRenderer? terrainRenderer = null)
    {
        if (world == null)
            return default;

        var allQuery = world.CreateQuery().Build();
        int chunkCount = 0;
        int totalCapacity = 0;

        foreach (var chunk in world.EnumerateQueryChunks(allQuery))
        {
            chunkCount++;
            totalCapacity += chunk.Capacity;
        }

        return new SkyCoreTelemetrySnapshot
        {
            EntityCount = world.EntityCount,
            NonEmptyChunkCount = chunkCount,
            TotalChunkCapacity = totalCapacity,
            AllocatedChunkBytes = (long)chunkCount * ArchetypeChunk.ChunkSize,
            WorldVersion = world.Version,
            LoadedWorldCells = streamer?.LoadedCellCount ?? 0,
            DormantWorldCells = streamer?.DormantCellCount ?? 0,
            VisibleTerrainChunks = terrainRenderer?.LastVisibleChunks ?? 0,
            CulledTerrainChunks = terrainRenderer?.LastCulledChunks ?? 0,
            RenderedTerrainTriangles = terrainRenderer?.LastRenderedTriangles ?? 0
        };
    }
}
