using System;
using BlueSky.Ramanujan;
using System.Collections.Generic;
using BlueSky.BSR;

namespace BlueSky.Motif.Procedural;

/// <summary>
/// Rules for procedural foliage/prop placement across a terrain or world cell.
/// </summary>
public struct ScatterRule
{
    public float Spacing;
    public float MinHeight;
    public float MaxHeight;
    /// <summary>Maximum allowed terrain slope angle in degrees (0 = flat only, 90 = any slope).</summary>
    public float MaxSlopeDegrees;
    public float MinScale;
    public float MaxScale;
    public uint Seed;

    public static ScatterRule DefaultFoliage => new()
    {
        Spacing = 8.0f,
        MinHeight = 0.0f,
        MaxHeight = 100.0f,
        MaxSlopeDegrees = 35.0f,
        MinScale = 0.85f,
        MaxScale = 1.25f,
        Seed = 1337u
    };
}

/// <summary>
/// Generated procedural instance transform (position, rotation aligned to surface/yaw, uniform scale).
/// </summary>
public readonly struct ScatterInstance
{
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;
    public readonly Vector3 Scale;

    public ScatterInstance(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }
}

/// <summary>
/// Deterministic Motif procedural scatter generator for populating terrain chunks with rocks, trees, and props.
/// </summary>
public static class ProceduralScatter
{
    /// <summary>
    /// Generates jittered grid scatter instances within <paramref name="boundsMin"/> .. <paramref name="boundsMax"/>
    /// filtered by elevation and surface normal slope on <paramref name="terrainSystem"/>.
    /// </summary>
    public static List<ScatterInstance> GenerateOnTerrain(
        TerrainSystem terrainSystem,
        uint terrainEntityId,
        Vector3 boundsMin,
        Vector3 boundsMax,
        in ScatterRule rule)
    {
        var results = new List<ScatterInstance>();
        if (terrainSystem == null)
            return results;

        float spacing = MathF.Max(0.5f, rule.Spacing);
        float minCosSlope = MathF.Cos(System.Math.Clamp(rule.MaxSlopeDegrees, 0f, 90f) * (MathF.PI / 180f));

        int cellIndex = 0;
        for (float z = boundsMin.Z; z < boundsMax.Z; z += spacing)
        {
            for (float x = boundsMin.X; x < boundsMax.X; x += spacing)
            {
                uint h = Hash((uint)cellIndex++, rule.Seed);
                float jx = ((h & 0xFFFFu) / 65535.0f - 0.5f) * spacing * 0.8f;
                float jz = (((h >> 16) & 0xFFFFu) / 65535.0f - 0.5f) * spacing * 0.8f;

                float sampleX = System.Math.Clamp(x + jx, boundsMin.X, boundsMax.X);
                float sampleZ = System.Math.Clamp(z + jz, boundsMin.Z, boundsMax.Z);

                if (!terrainSystem.TrySampleWorldHeight(
                        terrainEntityId,
                        new System.Numerics.Vector3(sampleX, 0f, sampleZ),
                        out float worldY,
                        out var worldNormal))
                {
                    continue;
                }

                if (worldY < rule.MinHeight || worldY > rule.MaxHeight)
                    continue;

                // Slope filter: worldNormal.Y is cos(slopeAngle)
                if (worldNormal.Y < minCosSlope)
                    continue;

                uint h2 = Hash((uint)(cellIndex * 7919), rule.Seed ^ 0x9E3779B9u);
                float tScale = (h2 & 0xFFFFu) / 65535.0f;
                float uniformScale = rule.MinScale + (rule.MaxScale - rule.MinScale) * tScale;
                float yawRadians = (((h2 >> 16) & 0xFFFFu) / 65535.0f) * MathF.PI * 2.0f;

                var pos = new Vector3(sampleX, worldY, sampleZ);
                var rot = new Quaternion(Vector3.Up, yawRadians);
                var scale = new Vector3(uniformScale, uniformScale, uniformScale);

                results.Add(new ScatterInstance(pos, rot, scale));
            }
        }

        return results;
    }

    private static uint Hash(uint x, uint seed)
    {
        uint v = x ^ seed;
        v ^= v >> 16;
        v *= 0x7FEB352Du;
        v ^= v >> 15;
        v *= 0x846CA68Bu;
        v ^= v >> 16;
        return v;
    }
}
