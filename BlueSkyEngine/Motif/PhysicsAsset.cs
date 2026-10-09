using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlueSky.Motif;

/// <summary>
/// Physics asset for skeletal meshes — per-bone collision shapes.
/// Saved as standalone .bsphy JSON files.
/// </summary>
public class PhysicsAsset
{
    public string Name { get; set; } = "";
    public List<PhysicsBody> Bodies { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public static PhysicsAsset? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            string json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<PhysicsAssetData>(json, JsonOpts);
            return data?.ToRuntime();
        }
        catch { return null; }
    }

    public bool Save(string path)
    {
        try
        {
            var data = PhysicsAssetData.FromRuntime(this);
            string json = JsonSerializer.Serialize(data, JsonOpts);
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, json);
            return true;
        }
        catch { return false; }
    }
}

public class PhysicsBody
{
    public string BoneName { get; set; } = "";
    public int BoneIndex { get; set; } = -1;
    public bool bEnabled { get; set; } = true;
    public float Mass { get; set; } = 1.0f;
    public float LinearDamping { get; set; } = 0.0f;
    public float AngularDamping { get; set; } = 0.05f;
    public List<PhysicsShape> Shapes { get; set; } = new();
}

public class PhysicsShape
{
    public PhysicsShapeType ShapeType { get; set; } = PhysicsShapeType.Box;
    public Vector3 Center { get; set; } = Vector3.Zero;
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public Vector3 Size { get; set; } = Vector3.One;
    public float Radius { get; set; } = 0.5f;
    public float Height { get; set; } = 1.0f;
    public bool IsTrigger { get; set; } = false;
    public float Friction { get; set; } = 0.5f;
    public float Restitution { get; set; } = 0.0f;
    public List<Vector3> HullVertices { get; set; } = new();
    public List<int> HullIndices { get; set; } = new();
}

public enum PhysicsShapeType
{
    Box,
    Sphere,
    Capsule,
    ConvexHull
}

// ── JSON serialization data classes (float arrays for System.Text.Json compat) ──

internal class PhysicsAssetData
{
    public string Name { get; set; } = "";
    public List<PhysicsBodyData> Bodies { get; set; } = new();

    public static PhysicsAssetData FromRuntime(PhysicsAsset asset)
    {
        var d = new PhysicsAssetData { Name = asset.Name };
        foreach (var b in asset.Bodies)
        {
            var bd = new PhysicsBodyData
            {
                BoneName = b.BoneName,
                BoneIndex = b.BoneIndex,
                bEnabled = b.bEnabled,
                Mass = b.Mass,
                LinearDamping = b.LinearDamping,
                AngularDamping = b.AngularDamping
            };
            foreach (var s in b.Shapes)
            {
                var shapeData = new PhysicsShapeData
                {
                    ShapeType = s.ShapeType.ToString(),
                    Center = Vec3ToArray(s.Center),
                    Rotation = QuatToArray(s.Rotation),
                    Size = Vec3ToArray(s.Size),
                    Radius = s.Radius,
                    Height = s.Height,
                    IsTrigger = s.IsTrigger,
                    Friction = s.Friction,
                    Restitution = s.Restitution
                };
                if (s.HullVertices.Count > 0)
                {
                    var hv = new float[s.HullVertices.Count * 3];
                    for (int i = 0; i < s.HullVertices.Count; i++)
                    {
                        hv[i * 3] = s.HullVertices[i].X;
                        hv[i * 3 + 1] = s.HullVertices[i].Y;
                        hv[i * 3 + 2] = s.HullVertices[i].Z;
                    }
                    shapeData.HullVertices = hv;
                    shapeData.HullIndices = s.HullIndices.ToArray();
                }
                bd.Shapes.Add(shapeData);
            }
            d.Bodies.Add(bd);
        }
        return d;
    }

    public PhysicsAsset ToRuntime()
    {
        var asset = new PhysicsAsset { Name = Name };
        foreach (var bd in Bodies)
        {
            var body = new PhysicsBody
            {
                BoneName = bd.BoneName,
                BoneIndex = bd.BoneIndex,
                bEnabled = bd.bEnabled,
                Mass = bd.Mass,
                LinearDamping = bd.LinearDamping,
                AngularDamping = bd.AngularDamping
            };
            foreach (var sd in bd.Shapes)
            {
                var shape = new PhysicsShape
                {
                    ShapeType = Enum.TryParse<PhysicsShapeType>(sd.ShapeType, true, out var t) ? t : PhysicsShapeType.Box,
                    Center = ArrayToVec3(sd.Center),
                    Rotation = ArrayToQuat(sd.Rotation),
                    Size = ArrayToVec3(sd.Size) == Vector3.Zero ? Vector3.One : ArrayToVec3(sd.Size),
                    Radius = sd.Radius <= 0 ? 0.5f : sd.Radius,
                    Height = sd.Height <= 0 ? 1.0f : sd.Height,
                    IsTrigger = sd.IsTrigger,
                    Friction = sd.Friction,
                    Restitution = sd.Restitution
                };
                if (sd.HullVertices != null && sd.HullVertices.Length >= 9)
                {
                    for (int i = 0; i < sd.HullVertices.Length - 2; i += 3)
                        shape.HullVertices.Add(new Vector3(sd.HullVertices[i], sd.HullVertices[i + 1], sd.HullVertices[i + 2]));
                    if (sd.HullIndices != null)
                        shape.HullIndices.AddRange(sd.HullIndices);
                }
                body.Shapes.Add(shape);
            }
            asset.Bodies.Add(body);
        }
        return asset;
    }

    private static float[] Vec3ToArray(Vector3 v) => new[] { v.X, v.Y, v.Z };
    private static float[] QuatToArray(Quaternion q) => new[] { q.X, q.Y, q.Z, q.W };
    private static Vector3 ArrayToVec3(float[]? a) =>
        (a != null && a.Length >= 3) ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;
    private static Quaternion ArrayToQuat(float[]? a) =>
        (a != null && a.Length >= 4) ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.Identity;
}

internal class PhysicsBodyData
{
    public string BoneName { get; set; } = "";
    public int BoneIndex { get; set; } = -1;
    public bool bEnabled { get; set; } = true;
    public float Mass { get; set; } = 1.0f;
    public float LinearDamping { get; set; } = 0.0f;
    public float AngularDamping { get; set; } = 0.05f;
    public List<PhysicsShapeData> Shapes { get; set; } = new();
}

internal class PhysicsShapeData
{
    public string ShapeType { get; set; } = "Box";
    public float[] Center { get; set; } = new float[] { 0, 0, 0 };
    public float[] Rotation { get; set; } = new float[] { 0, 0, 0, 1 };
    public float[] Size { get; set; } = new float[] { 1, 1, 1 };
    public float Radius { get; set; } = 0.5f;
    public float Height { get; set; } = 1.0f;
    public bool IsTrigger { get; set; } = false;
    public float Friction { get; set; } = 0.5f;
    public float Restitution { get; set; } = 0.0f;
    public float[]? HullVertices { get; set; }
    public int[]? HullIndices { get; set; }
}
