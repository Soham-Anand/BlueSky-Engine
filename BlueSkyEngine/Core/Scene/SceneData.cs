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
[JsonDerivedType(typeof(StaticMeshComponentData), "StaticMesh")]
[JsonDerivedType(typeof(RigidbodyComponentData), "Rigidbody")]
[JsonDerivedType(typeof(ColliderComponentData), "Collider")]
[JsonDerivedType(typeof(TeaScriptComponentData), "TeaScript")]
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

public class StaticMeshComponentData : ComponentData
{
    public override string Type => "StaticMesh";
    public string MeshAssetId { get; set; } = "";
    public List<string> MaterialSlots { get; set; } = new();
}

public class RigidbodyComponentData : ComponentData
{
    public override string Type => "Rigidbody";
    public float Mass { get; set; } = 1.0f;
    public float Drag { get; set; } = 0.05f;
    public float AngularDrag { get; set; } = 0.05f;
    public bool UseGravity { get; set; } = true;
    public bool IsKinematic { get; set; } = false;
    public bool FreezePositionX { get; set; }
    public bool FreezePositionY { get; set; }
    public bool FreezePositionZ { get; set; }
    public bool FreezeRotationX { get; set; }
    public bool FreezeRotationY { get; set; }
    public bool FreezeRotationZ { get; set; }
}

public class ColliderComponentData : ComponentData
{
    public override string Type => "Collider";
    public string ColliderType { get; set; } = "Box";
    public Vector3 Size { get; set; } = Vector3.One;
    public float Radius { get; set; } = 0.5f;
    public float Height { get; set; } = 2.0f;
    public bool IsTrigger { get; set; } = false;
    public float Friction { get; set; } = 0.5f;
    public float Restitution { get; set; } = 0.3f;
}

public class TeaScriptComponentData : ComponentData
{
    public override string Type => "TeaScript";
    public string ScriptAssetId { get; set; } = "";
}
