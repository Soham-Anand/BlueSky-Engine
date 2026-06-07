using System;
using System.Collections.Generic;
using System.IO;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Physics;
using BSBlueMath = BlueSky.Core.Math.BlueMath;
using BSMatrix4x4 = BlueSky.Core.Math.Matrix4x4;
using BSVector2 = BlueSky.Core.Math.Vector2;
using BSVector3 = BlueSky.Core.Math.Vector3;

namespace BlueSky.Rendering;

/// <summary>
/// Heightfield terrain editing/runtime data. TerrainComponent stores metadata;
/// this system owns the mutable height samples and generated mesh.
/// </summary>
public sealed class TerrainSystem
{
    private const string TerrainMagic = "BSTERRAIN1";
    private const float MinTerrainSize = 1.0f;
    private const float BrushHeightStepRatio = 0.015f;

    private readonly World _world;
    private readonly Dictionary<uint, TerrainData> _terrains = new();

    public TerrainSystem(World world)
    {
        _world = world;
    }

    public void InitializeTerrain(uint entityId, int width, int height)
    {
        var data = new TerrainData(width, height, MathF.Max(width - 1, MinTerrainSize), MathF.Max(height - 1, MinTerrainSize), 20.0f);
        _terrains[entityId] = data;
        RebuildMesh(entityId);
    }

    public void InitializeTerrain(uint entityId, TerrainComponent terrain)
    {
        var data = new TerrainData(terrain);
        _terrains[entityId] = data;
        RebuildMesh(entityId);
    }

    public bool Raycast(uint entityId, BSVector3 origin, BSVector3 direction, out RaycastHit hit)
    {
        hit = default;

        if (!TryGetTerrainData(entityId, out var data) || data.Width < 2 || data.Height < 2)
            return false;

        if (!TryGetEntity(entityId, out var entity))
            return false;

        var model = BSMatrix4x4.Identity;
        if (_world.TryGetComponent<TransformComponent>(entity, out var transform))
            model = transform.WorldMatrix;

        var modelNumerics = ToNumerics(model);
        if (!System.Numerics.Matrix4x4.Invert(modelNumerics, out var inverseModel))
            return false;

        var localOrigin = TransformPoint(origin, inverseModel);
        var localDirection = TransformDirection(direction, inverseModel).Normalize();
        if (localDirection.LengthSquared < BSBlueMath.Epsilon)
            return false;

        if (!IntersectTerrainBounds(localOrigin, localDirection, data, out var tMin, out var tMax))
            return false;

        tMin = MathF.Max(tMin, 0.0f);
        float cellStep = MathF.Max(0.05f, MathF.Min(data.CellSizeX, data.CellSizeZ) * 0.5f);
        float previousT = tMin;
        var previousPoint = localOrigin + localDirection * previousT;
        float previousDelta = previousPoint.Y - SampleHeightAtLocal(data, previousPoint.X, previousPoint.Z);

        if (previousDelta <= 0.0f && IsInsideTerrainXZ(previousPoint, data))
            return BuildHit(previousPoint, modelNumerics, data, out hit);

        for (float t = tMin + cellStep; t <= tMax; t += cellStep)
        {
            var point = localOrigin + localDirection * t;
            if (!IsInsideTerrainXZ(point, data))
            {
                previousT = t;
                previousPoint = point;
                previousDelta = point.Y - SampleHeightAtLocal(data, point.X, point.Z);
                continue;
            }

            float delta = point.Y - SampleHeightAtLocal(data, point.X, point.Z);
            if (previousDelta > 0.0f && delta <= 0.0f)
            {
                float low = previousT;
                float high = t;
                for (int i = 0; i < 8; i++)
                {
                    float mid = (low + high) * 0.5f;
                    var midPoint = localOrigin + localDirection * mid;
                    float midDelta = midPoint.Y - SampleHeightAtLocal(data, midPoint.X, midPoint.Z);
                    if (midDelta > 0.0f)
                        low = mid;
                    else
                        high = mid;
                }

                var hitPoint = localOrigin + localDirection * high;
                return BuildHit(hitPoint, modelNumerics, data, out hit);
            }

            previousT = t;
            previousPoint = point;
            previousDelta = delta;
        }

        return false;
    }

    /// <summary>
    /// Snapshot the height-field data for a terrain so it can be handed
    /// to the physics engine as a real heightfield collider.
    /// </summary>
    public bool TryGetPhysicsHeightField(uint entityId, out PhysicsTerrainData data)
    {
        data = default;
        if (!TryGetTerrainData(entityId, out var t) || t.Width < 2 || t.Height < 2)
            return false;
        if (t.Heights == null || t.Heights.Length != t.Width * t.Height)
            return false;

        System.Numerics.Vector3 originOffset = System.Numerics.Vector3.Zero;
        if (TryGetEntity(entityId, out var entity) &&
            _world.TryGetComponent<TransformComponent>(entity, out var transform))
        {
            originOffset = new System.Numerics.Vector3(transform.Position.X, transform.Position.Y, transform.Position.Z);
        }

        data = new PhysicsTerrainData
        {
            Width        = t.Width,
            Height       = t.Height,
            WorldWidth   = t.WorldWidth,
            WorldDepth   = t.WorldDepth,
            Samples      = (float[])t.Heights.Clone(),
            OriginOffset = originOffset
        };
        return true;
    }

    public bool TrySampleWorldHeight(uint entityId, System.Numerics.Vector3 worldPosition, out float worldHeight, out System.Numerics.Vector3 worldNormal)
    {
        worldHeight = 0.0f;
        worldNormal = System.Numerics.Vector3.UnitY;

        if (!TryGetTerrainData(entityId, out var data) || data.Width < 2 || data.Height < 2)
            return false;

        if (!TryGetEntity(entityId, out var entity))
            return false;

        var model = BSMatrix4x4.Identity;
        if (_world.TryGetComponent<TransformComponent>(entity, out var transform))
            model = transform.WorldMatrix;

        var modelNumerics = ToNumerics(model);
        if (!System.Numerics.Matrix4x4.Invert(modelNumerics, out var inverseModel))
            return false;

        var localPosition = TransformPoint(new BSVector3(worldPosition.X, worldPosition.Y, worldPosition.Z), inverseModel);
        if (!IsInsideTerrainXZ(localPosition, data))
            return false;

        float gridX = data.GridXFromLocalX(localPosition.X);
        float gridZ = data.GridZFromLocalZ(localPosition.Z);
        float localHeight = SampleHeightAtGrid(data, gridX, gridZ);
        var localHit = new BSVector3(localPosition.X, localHeight, localPosition.Z);
        var localNormal = GetNormalAtGrid(data, (int)MathF.Round(gridX), (int)MathF.Round(gridZ));
        var worldHit = TransformPoint(localHit, modelNumerics);
        var normal = TransformDirection(localNormal, modelNumerics).Normalize();

        worldHeight = worldHit.Y;
        worldNormal = new System.Numerics.Vector3(normal.X, normal.Y, normal.Z);
        return true;
    }

    public void ApplyBrush(uint entityId, TerrainBrushStroke stroke)
    {
        if (!TryGetTerrainData(entityId, out var data))
            return;

        float radius = MathF.Max(0.001f, stroke.Radius);
        int radiusCells = Math.Max(1, (int)MathF.Ceiling(radius));
        int centerX = (int)MathF.Round(stroke.LocalX);
        int centerZ = (int)MathF.Round(stroke.LocalZ);
        float[] source = stroke.Mode is BrushMode.Smooth or BrushMode.Erode
            ? (float[])data.Heights.Clone()
            : data.Heights;

        float heightStep = MathF.Max(0.02f, data.MaxElevation * BrushHeightStepRatio);
        bool changed = false;

        for (int z = centerZ - radiusCells; z <= centerZ + radiusCells; z++)
        {
            for (int x = centerX - radiusCells; x <= centerX + radiusCells; x++)
            {
                if (x < 0 || x >= data.Width || z < 0 || z >= data.Height)
                    continue;

                float dx = x - stroke.LocalX;
                float dz = z - stroke.LocalZ;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist > radius)
                    continue;

                float falloff = 1.0f - BSBlueMath.SmoothStep(0.0f, 1.0f, dist / radius);
                float amount = MathF.Max(0.0f, stroke.Strength * falloff);
                if (amount <= 0.0f)
                    continue;

                int index = z * data.Width + x;
                float current = data.Heights[index];
                float next = current;

                switch (stroke.Mode)
                {
                    case BrushMode.Raise:
                        next = current + amount * heightStep;
                        break;
                    case BrushMode.Lower:
                        next = current - amount * heightStep;
                        break;
                    case BrushMode.Smooth:
                    {
                        float average = Average3x3(source, data.Width, data.Height, x, z);
                        next = BSBlueMath.Lerp(current, average, BSBlueMath.Clamp01(amount * 0.45f));
                        break;
                    }
                    case BrushMode.Flatten:
                        next = BSBlueMath.Lerp(current, stroke.TargetHeight, BSBlueMath.Clamp01(amount * 0.55f));
                        break;
                    case BrushMode.Noise:
                    {
                        float noise = FractalValueNoise(x, z);
                        next = current + (noise * 2.0f - 1.0f) * amount * heightStep;
                        break;
                    }
                    case BrushMode.Erode:
                    {
                        float average = Average3x3(source, data.Width, data.Height, x, z);
                        float target = current > average
                            ? average
                            : BSBlueMath.Lerp(current, average, 0.25f);
                        next = BSBlueMath.Lerp(current, target, BSBlueMath.Clamp01(amount * 0.5f));
                        break;
                    }
                    case BrushMode.Erase:
                        next = BSBlueMath.Lerp(current, 0.0f, BSBlueMath.Clamp01(amount * 0.65f));
                        break;
                }

                next = BSBlueMath.Clamp(next, 0.0f, data.MaxElevation);
                if (MathF.Abs(next - current) > 0.0001f)
                {
                    data.Heights[index] = next;
                    changed = true;
                }
            }
        }

        if (changed)
            data.NeedsRebuild = true;
    }

    public void SaveTerrainAsset(uint entityId, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !_terrains.TryGetValue(entityId, out var data))
            return;

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.Write(TerrainMagic);
            writer.Write(data.Width);
            writer.Write(data.Height);
            writer.Write(data.WorldWidth);
            writer.Write(data.WorldDepth);
            writer.Write(data.MaxElevation);
            writer.Write(data.Heights.Length);
            for (int i = 0; i < data.Heights.Length; i++)
                writer.Write(data.Heights[i]);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TerrainSystem] Failed to save terrain asset '{path}': {ex.Message}");
        }
    }

    public void SetHeight(uint entityId, int x, int z, float height)
    {
        if (!TryGetTerrainData(entityId, out var data))
            return;

        if (x < 0 || x >= data.Width || z < 0 || z >= data.Height)
            return;

        data.Heights[z * data.Width + x] = BSBlueMath.Clamp(height, 0.0f, data.MaxElevation);
        data.NeedsRebuild = true;
    }

    public float GetHeight(uint entityId, int x, int z)
    {
        if (!TryGetTerrainData(entityId, out var data))
            return 0.0f;

        if (x < 0 || x >= data.Width || z < 0 || z >= data.Height)
            return 0.0f;

        return data.Heights[z * data.Width + x];
    }

    public void Update()
    {
        SyncTerrainComponents();

        foreach (var kvp in _terrains)
        {
            if (kvp.Value.NeedsRebuild)
            {
                RebuildMesh(kvp.Key);
                kvp.Value.NeedsRebuild = false;
            }
        }
    }

    private void SyncTerrainComponents()
    {
        var query = _world.CreateQuery()
            .All<TerrainComponent>()
            .Build();

        foreach (var chunk in _world.GetQueryChunks(query))
        {
            int terrainIndex = chunk.GetComponentIndex(typeof(TerrainComponent));
            var entities = chunk.GetEntities();

            for (int i = 0; i < chunk.Count; i++)
            {
                ref var terrain = ref chunk.GetComponent<TerrainComponent>(i, terrainIndex);
                uint entityId = (uint)entities[i].Id;

                if (!_terrains.TryGetValue(entityId, out var data))
                {
                    _terrains[entityId] = new TerrainData(terrain);
                    terrain.NeedsRebuild = true;
                    continue;
                }

                bool settingsChanged = data.ApplySettings(terrain);
                if (settingsChanged || terrain.NeedsRebuild)
                    data.NeedsRebuild = true;

                terrain.NeedsRebuild = false;
            }
        }
    }

    private void RebuildMesh(uint entityId)
    {
        if (!_terrains.TryGetValue(entityId, out var data))
            return;

        int width = data.Width;
        int height = data.Height;
        var vertices = new BSVector3[width * height];
        var normals = new BSVector3[width * height];
        var uvs = new BSVector2[width * height];
        var indices = new uint[(width - 1) * (height - 1) * 6];

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = z * width + x;
                vertices[index] = new BSVector3(data.LocalXFromGridX(x), data.Heights[index], data.LocalZFromGridZ(z));
                normals[index] = GetNormalAtGrid(data, x, z);
                uvs[index] = new BSVector2(x / (float)(width - 1), z / (float)(height - 1));
            }
        }

        int write = 0;
        for (int z = 0; z < height - 1; z++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                uint topLeft = (uint)(z * width + x);
                uint topRight = (uint)(z * width + x + 1);
                uint bottomLeft = (uint)((z + 1) * width + x);
                uint bottomRight = (uint)((z + 1) * width + x + 1);

                indices[write++] = topLeft;
                indices[write++] = bottomLeft;
                indices[write++] = topRight;

                indices[write++] = topRight;
                indices[write++] = bottomLeft;
                indices[write++] = bottomRight;
            }
        }

        data.Vertices = vertices;
        data.Normals = normals;
        data.UVs = uvs;
        data.Indices = indices;
    }

    public TerrainMeshData? GetMesh(uint entityId)
    {
        if (!TryGetTerrainData(entityId, out var data))
            return null;

        if (data.Vertices == null || data.Normals == null || data.UVs == null || data.Indices == null)
            return null;

        return new TerrainMeshData
        {
            Vertices = data.Vertices,
            Normals = data.Normals,
            UVs = data.UVs,
            Indices = data.Indices
        };
    }

    public void Clear()
    {
        _terrains.Clear();
    }

    public void LoadTerrainAssetsForWorld()
    {
        var query = _world.CreateQuery()
            .All<TerrainComponent>()
            .Build();

        foreach (var chunk in _world.GetQueryChunks(query))
        {
            int terrainIndex = chunk.GetComponentIndex(typeof(TerrainComponent));
            var entities = chunk.GetEntities();

            for (int i = 0; i < chunk.Count; i++)
            {
                ref var terrain = ref chunk.GetComponent<TerrainComponent>(i, terrainIndex);
                uint entityId = (uint)entities[i].Id;

                if (!string.IsNullOrEmpty(terrain.TerrainAssetPath) &&
                    File.Exists(terrain.TerrainAssetPath) &&
                    TryLoadTerrainAsset(terrain.TerrainAssetPath, out var loaded))
                {
                    _terrains[entityId] = loaded;
                    terrain.Width = loaded.Width;
                    terrain.Height = loaded.Height;
                    terrain.WorldWidth = loaded.WorldWidth;
                    terrain.WorldHeight = loaded.WorldDepth;
                    terrain.MaxElevation = loaded.MaxElevation;
                    terrain.NeedsRebuild = false;
                    RebuildMesh(entityId);
                }
                else
                {
                    InitializeTerrain(entityId, terrain);
                }
            }
        }
    }

    public void SaveAllTerrainAssets()
    {
        var query = _world.CreateQuery()
            .All<TerrainComponent>()
            .Build();

        foreach (var chunk in _world.GetQueryChunks(query))
        {
            int terrainIndex = chunk.GetComponentIndex(typeof(TerrainComponent));
            var entities = chunk.GetEntities();

            for (int i = 0; i < chunk.Count; i++)
            {
                var terrain = chunk.GetComponent<TerrainComponent>(i, terrainIndex);
                if (!string.IsNullOrEmpty(terrain.TerrainAssetPath))
                    SaveTerrainAsset((uint)entities[i].Id, terrain.TerrainAssetPath);
            }
        }
    }

    private bool TryGetTerrainData(uint entityId, out TerrainData data)
    {
        if (_terrains.TryGetValue(entityId, out data!))
            return true;

        if (!TryGetEntity(entityId, out var entity) || !_world.TryGetComponent<TerrainComponent>(entity, out var terrain))
        {
            data = null!;
            return false;
        }

        data = new TerrainData(terrain);
        _terrains[entityId] = data;
        RebuildMesh(entityId);
        return true;
    }

    private bool TryGetEntity(uint entityId, out Entity entity)
    {
        foreach (var candidate in _world.GetAllEntities())
        {
            if ((uint)candidate.Id == entityId)
            {
                entity = candidate;
                return true;
            }
        }

        entity = default;
        return false;
    }

    private static bool TryLoadTerrainAsset(string path, out TerrainData data)
    {
        data = null!;

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (reader.ReadString() != TerrainMagic)
                return false;

            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            float worldWidth = reader.ReadSingle();
            float worldDepth = reader.ReadSingle();
            float maxElevation = reader.ReadSingle();
            int heightCount = reader.ReadInt32();

            data = new TerrainData(width, height, worldWidth, worldDepth, maxElevation);
            if (heightCount != data.Heights.Length)
                return false;

            for (int i = 0; i < data.Heights.Length; i++)
                data.Heights[i] = BSBlueMath.Clamp(reader.ReadSingle(), 0.0f, data.MaxElevation);

            data.NeedsRebuild = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TerrainSystem] Failed to load terrain asset '{path}': {ex.Message}");
            data = null!;
            return false;
        }
    }

    private static bool BuildHit(BSVector3 localPoint, System.Numerics.Matrix4x4 model, TerrainData data, out RaycastHit hit)
    {
        float gridX = data.GridXFromLocalX(localPoint.X);
        float gridZ = data.GridZFromLocalZ(localPoint.Z);
        float localHeight = SampleHeightAtGrid(data, gridX, gridZ);
        var localHit = new BSVector3(localPoint.X, localHeight, localPoint.Z);
        var localNormal = GetNormalAtGrid(data, (int)MathF.Round(gridX), (int)MathF.Round(gridZ));

        hit = new RaycastHit
        {
            LocalX = gridX,
            LocalZ = gridZ,
            Position = TransformPoint(localHit, model),
            Normal = TransformDirection(localNormal, model).Normalize()
        };
        return true;
    }

    private static bool IntersectTerrainBounds(BSVector3 origin, BSVector3 direction, TerrainData data, out float tMin, out float tMax)
    {
        tMin = float.MinValue;
        tMax = float.MaxValue;

        return IntersectSlab(origin.X, direction.X, 0.0f, data.WorldWidth, ref tMin, ref tMax) &&
               IntersectSlab(origin.Y, direction.Y, -1.0f, data.MaxElevation + 1.0f, ref tMin, ref tMax) &&
               IntersectSlab(origin.Z, direction.Z, 0.0f, data.WorldDepth, ref tMin, ref tMax) &&
               tMax >= 0.0f;
    }

    private static bool IntersectSlab(float origin, float direction, float min, float max, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(direction) < BSBlueMath.Epsilon)
            return origin >= min && origin <= max;

        float invD = 1.0f / direction;
        float a = (min - origin) * invD;
        float b = (max - origin) * invD;
        if (a > b)
            (a, b) = (b, a);

        tMin = MathF.Max(tMin, a);
        tMax = MathF.Min(tMax, b);
        return tMin <= tMax;
    }

    private static bool IsInsideTerrainXZ(BSVector3 point, TerrainData data) =>
        point.X >= 0.0f && point.X <= data.WorldWidth &&
        point.Z >= 0.0f && point.Z <= data.WorldDepth;

    private static float SampleHeightAtLocal(TerrainData data, float localX, float localZ) =>
        SampleHeightAtGrid(data, data.GridXFromLocalX(localX), data.GridZFromLocalZ(localZ));

    private static float SampleHeightAtGrid(TerrainData data, float gridX, float gridZ)
    {
        gridX = BSBlueMath.Clamp(gridX, 0.0f, data.Width - 1);
        gridZ = BSBlueMath.Clamp(gridZ, 0.0f, data.Height - 1);

        int x0 = (int)MathF.Floor(gridX);
        int z0 = (int)MathF.Floor(gridZ);
        int x1 = Math.Min(x0 + 1, data.Width - 1);
        int z1 = Math.Min(z0 + 1, data.Height - 1);
        float tx = gridX - x0;
        float tz = gridZ - z0;

        float h00 = data.Heights[z0 * data.Width + x0];
        float h10 = data.Heights[z0 * data.Width + x1];
        float h01 = data.Heights[z1 * data.Width + x0];
        float h11 = data.Heights[z1 * data.Width + x1];

        float hx0 = BSBlueMath.Lerp(h00, h10, tx);
        float hx1 = BSBlueMath.Lerp(h01, h11, tx);
        return BSBlueMath.Lerp(hx0, hx1, tz);
    }

    private static BSVector3 GetNormalAtGrid(TerrainData data, int x, int z)
    {
        float hL = GetHeightClamped(data.Heights, data.Width, data.Height, x - 1, z);
        float hR = GetHeightClamped(data.Heights, data.Width, data.Height, x + 1, z);
        float hD = GetHeightClamped(data.Heights, data.Width, data.Height, x, z - 1);
        float hU = GetHeightClamped(data.Heights, data.Width, data.Height, x, z + 1);

        var tangentX = new BSVector3(data.CellSizeX * 2.0f, hR - hL, 0.0f);
        var tangentZ = new BSVector3(0.0f, hU - hD, data.CellSizeZ * 2.0f);
        return BSVector3.Cross(tangentZ, tangentX).Normalize();
    }

    private static float Average3x3(float[] heights, int width, int height, int centerX, int centerZ)
    {
        float sum = 0.0f;
        int count = 0;

        for (int z = centerZ - 1; z <= centerZ + 1; z++)
        {
            for (int x = centerX - 1; x <= centerX + 1; x++)
            {
                int cx = Math.Clamp(x, 0, width - 1);
                int cz = Math.Clamp(z, 0, height - 1);
                sum += heights[cz * width + cx];
                count++;
            }
        }

        return count > 0 ? sum / count : 0.0f;
    }

    private static float GetHeightClamped(float[] heights, int width, int height, int x, int z)
    {
        x = Math.Clamp(x, 0, width - 1);
        z = Math.Clamp(z, 0, height - 1);
        return heights[z * width + x];
    }

    private static float FractalValueNoise(int x, int z)
    {
        float a = ValueNoise(x, z);
        float b = ValueNoise(x * 2 + 17, z * 2 - 23) * 0.5f;
        float c = ValueNoise(x * 4 - 41, z * 4 + 59) * 0.25f;
        return (a + b + c) / 1.75f;
    }

    private static float ValueNoise(int x, int z)
    {
        uint n = (uint)(x * 374761393 + z * 668265263);
        n = (n ^ (n >> 13)) * 1274126177u;
        n ^= n >> 16;
        return (n & 0x00FFFFFF) / 16777215.0f;
    }

    private static System.Numerics.Matrix4x4 ToNumerics(BSMatrix4x4 m) =>
        new(m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44);

    private static BSVector3 TransformPoint(BSVector3 point, System.Numerics.Matrix4x4 matrix)
    {
        var transformed = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(point.X, point.Y, point.Z), matrix);
        return new BSVector3(transformed.X, transformed.Y, transformed.Z);
    }

    private static BSVector3 TransformDirection(BSVector3 direction, System.Numerics.Matrix4x4 matrix)
    {
        var transformed = System.Numerics.Vector3.TransformNormal(new System.Numerics.Vector3(direction.X, direction.Y, direction.Z), matrix);
        return new BSVector3(transformed.X, transformed.Y, transformed.Z);
    }

    private sealed class TerrainData
    {
        public int Width;
        public int Height;
        public float WorldWidth;
        public float WorldDepth;
        public float MaxElevation;
        public float[] Heights;
        public BSVector3[]? Vertices;
        public BSVector3[]? Normals;
        public BSVector2[]? UVs;
        public uint[]? Indices;
        public bool NeedsRebuild;

        public float CellSizeX => Width > 1 ? WorldWidth / (Width - 1) : WorldWidth;
        public float CellSizeZ => Height > 1 ? WorldDepth / (Height - 1) : WorldDepth;

        public TerrainData(TerrainComponent terrain)
            : this(terrain.Width, terrain.Height, terrain.WorldWidth, terrain.WorldHeight, terrain.MaxElevation)
        {
        }

        public TerrainData(int width, int height, float worldWidth, float worldDepth, float maxElevation)
        {
            Width = Math.Max(2, width);
            Height = Math.Max(2, height);
            WorldWidth = MathF.Max(MinTerrainSize, worldWidth);
            WorldDepth = MathF.Max(MinTerrainSize, worldDepth);
            MaxElevation = MathF.Max(0.01f, maxElevation);
            Heights = new float[Width * Height];
            NeedsRebuild = true;
        }

        public bool ApplySettings(TerrainComponent terrain)
        {
            int newWidth = Math.Max(2, terrain.Width);
            int newHeight = Math.Max(2, terrain.Height);
            float newWorldWidth = MathF.Max(MinTerrainSize, terrain.WorldWidth);
            float newWorldDepth = MathF.Max(MinTerrainSize, terrain.WorldHeight);
            float newMaxElevation = MathF.Max(0.01f, terrain.MaxElevation);
            bool changed = false;

            if (newWidth != Width || newHeight != Height)
            {
                Heights = ResizeHeights(Heights, Width, Height, newWidth, newHeight);
                Width = newWidth;
                Height = newHeight;
                changed = true;
            }

            if (MathF.Abs(newWorldWidth - WorldWidth) > 0.001f)
            {
                WorldWidth = newWorldWidth;
                changed = true;
            }

            if (MathF.Abs(newWorldDepth - WorldDepth) > 0.001f)
            {
                WorldDepth = newWorldDepth;
                changed = true;
            }

            if (MathF.Abs(newMaxElevation - MaxElevation) > 0.001f)
            {
                MaxElevation = newMaxElevation;
                for (int i = 0; i < Heights.Length; i++)
                    Heights[i] = BSBlueMath.Clamp(Heights[i], 0.0f, MaxElevation);
                changed = true;
            }

            return changed;
        }

        public float GridXFromLocalX(float localX) =>
            BSBlueMath.Clamp(localX / WorldWidth * (Width - 1), 0.0f, Width - 1);

        public float GridZFromLocalZ(float localZ) =>
            BSBlueMath.Clamp(localZ / WorldDepth * (Height - 1), 0.0f, Height - 1);

        public float LocalXFromGridX(float x) =>
            Width > 1 ? x / (Width - 1) * WorldWidth : 0.0f;

        public float LocalZFromGridZ(float z) =>
            Height > 1 ? z / (Height - 1) * WorldDepth : 0.0f;

        private static float[] ResizeHeights(float[] source, int oldWidth, int oldHeight, int newWidth, int newHeight)
        {
            var resized = new float[newWidth * newHeight];
            for (int z = 0; z < newHeight; z++)
            {
                float srcZ = newHeight > 1 ? z / (float)(newHeight - 1) * (oldHeight - 1) : 0.0f;
                int z0 = (int)MathF.Floor(srcZ);
                int z1 = Math.Min(z0 + 1, oldHeight - 1);
                float tz = srcZ - z0;

                for (int x = 0; x < newWidth; x++)
                {
                    float srcX = newWidth > 1 ? x / (float)(newWidth - 1) * (oldWidth - 1) : 0.0f;
                    int x0 = (int)MathF.Floor(srcX);
                    int x1 = Math.Min(x0 + 1, oldWidth - 1);
                    float tx = srcX - x0;

                    float h00 = source[z0 * oldWidth + x0];
                    float h10 = source[z0 * oldWidth + x1];
                    float h01 = source[z1 * oldWidth + x0];
                    float h11 = source[z1 * oldWidth + x1];

                    float hx0 = BSBlueMath.Lerp(h00, h10, tx);
                    float hx1 = BSBlueMath.Lerp(h01, h11, tx);
                    resized[z * newWidth + x] = BSBlueMath.Lerp(hx0, hx1, tz);
                }
            }

            return resized;
        }
    }
}

public struct TerrainMeshData
{
    public BSVector3[] Vertices;
    public BSVector3[] Normals;
    public BSVector2[] UVs;
    public uint[] Indices;
}

public struct RaycastHit
{
    public float LocalX;
    public float LocalZ;
    public BSVector3 Position;
    public BSVector3 Normal;
}

public struct TerrainBrushStroke
{
    public float LocalX;
    public float LocalZ;
    public float Radius;
    public float Strength;
    public BrushMode Mode;
    public float TargetHeight;
    public int Layer;
}
