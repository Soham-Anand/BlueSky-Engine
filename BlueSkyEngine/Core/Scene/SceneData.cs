using System.Numerics;
using System.Text.Json.Serialization;

namespace BlueSky.Core.Scene;

/// <summary>
/// Serializable scene data structure.
/// </summary>
public class SceneData
{
    public string Name { get; set; } = "Untitled Scene";
    public string Version { get; set; } = "1.0";
    public List<EntityData> Entities { get; set; } = new();
}

public class EntityData
{
    public int Id { get; set; }
    public string Name { get; set; } = "Entity";
    public List<ComponentData> Components { get; set; } = new();
}

[JsonDerivedType(typeof(TransformComponentData), "Transform")]
[JsonDerivedType(typeof(MeshComponentData), "Mesh")]
[JsonDerivedType(typeof(MaterialComponentData), "Material")]
[JsonDerivedType(typeof(LightComponentData), "Light")]
[JsonDerivedType(typeof(CameraComponentData), "Camera")]
public abstract class ComponentData
{
    public abstract string Type { get; }
}

public class TransformComponentData : ComponentData
{
    public override string Type => "Transform";
    public Vector3 Position { get; set; }
    public Vector4 Rotation { get; set; } // Quaternion as Vector4
    public Vector3 Scale { get; set; } = Vector3.One;
}

public class MeshComponentData : ComponentData
{
    public override string Type => "Mesh";
    public string MeshPath { get; set; } = string.Empty;
    public int MeshIndex { get; set; } = 0;
}

public class MaterialComponentData : ComponentData
{
    public override string Type => "Material";
    public Vector3 Albedo { get; set; } = Vector3.One;
    public float Metallic { get; set; } = 0.0f;
    public float Roughness { get; set; } = 0.5f;
    public string? AlbedoTexture { get; set; }
    public string? NormalTexture { get; set; }
}

public class LightComponentData : ComponentData
{
    public override string Type => "Light";
    public string LightType { get; set; } = "Directional";
    public Vector3 Color { get; set; } = Vector3.One;
    public float Intensity { get; set; } = 1.0f;
}

public class CameraComponentData : ComponentData
{
    public override string Type => "Camera";
    public float Fov { get; set; } = 60.0f;
    public float Near { get; set; } = 0.1f;
    public float Far { get; set; } = 1000.0f;
    public bool IsActive { get; set; } = true;
}
