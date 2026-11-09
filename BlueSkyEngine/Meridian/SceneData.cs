using System.Collections.Generic;
using BlueSky.Airborne;
using System.Text.Json.Serialization;

namespace BlueSky.Meridian;

/// <summary>
/// Serializable scene data structure for SkyCore and SkyWorld streaming.
/// </summary>
public class SceneData
{
    public string Name { get; set; } = "Untitled Scene";
    public string Version { get; set; } = "2.0";
    public float WorldCellSize { get; set; } = 128.0f;
    public List<EntityData> Entities { get; set; } = new();
}

public class EntityData
{
    public int Id { get; set; }
    public string Name { get; set; } = "Entity";
    public List<ComponentData> Components { get; set; } = new();
}

[JsonDerivedType(typeof(TransformComponentData), "Transform")]
[JsonDerivedType(typeof(ParentComponentData), "Parent")]
[JsonDerivedType(typeof(WorldCellComponentData), "WorldCell")]
[JsonDerivedType(typeof(StaticMeshComponentData), "StaticMesh")]
[JsonDerivedType(typeof(SkeletalMeshComponentData), "SkeletalMesh")]
[JsonDerivedType(typeof(PhysicsComponentData), "Physics")]
[JsonDerivedType(typeof(TeaScriptComponentData), "TeaScript")]
[JsonDerivedType(typeof(LightComponentData), "Light")]
[JsonDerivedType(typeof(CameraComponentData), "Camera")]
[JsonDerivedType(typeof(TerrainComponentData), "Terrain")]
[JsonDerivedType(typeof(CarControllerComponentData), "CarController")]
[JsonDerivedType(typeof(NetworkManagerComponentData), "NetworkManager")]
[JsonDerivedType(typeof(SpawnPointComponentData), "SpawnPoint")]
[JsonDerivedType(typeof(AudioSourceComponentData), "AudioSource")]
[JsonDerivedType(typeof(AIAgentComponentData), "AIAgent")]
public abstract class ComponentData
{
    public abstract string Type { get; }
}

public class TransformComponentData : ComponentData
{
    public override string Type => "Transform";
    public float[] Position { get; set; } = new float[3];
    public float[] Rotation { get; set; } = new float[] { 0, 0, 0, 1 };
    public float[] Scale { get; set; } = new float[] { 1, 1, 1 };
}

public class ParentComponentData : ComponentData
{
    public override string Type => "Parent";
    public int ParentEntityId { get; set; }
    public float[] LocalPosition { get; set; } = new float[3];
    public float[] LocalRotation { get; set; } = new float[] { 0, 0, 0, 1 };
    public float[] LocalScale { get; set; } = new float[] { 1, 1, 1 };
}

public class WorldCellComponentData : ComponentData
{
    public override string Type => "WorldCell";
    public int CellX { get; set; }
    public int CellZ { get; set; }
    public bool IsPersistent { get; set; }
}

public class AudioSourceComponentData : ComponentData
{
    public override string Type => "AudioSource";
    public string ClipPath { get; set; } = "";
    public float Volume { get; set; } = 1.0f;
    public float Pitch { get; set; } = 1.0f;
    public float MinDistance { get; set; } = 1.0f;
    public float MaxDistance { get; set; } = 100.0f;
    public bool Loop { get; set; }
    public bool Is3D { get; set; } = true;
    public bool PlayOnStart { get; set; }
    public BlueSky.Echo.DistanceAttenuationCurve AttenuationCurve { get; set; } = BlueSky.Echo.DistanceAttenuationCurve.Linear;
}

public class AIAgentComponentData : ComponentData
{
    public override string Type => "AIAgent";
    public string BehaviorProfile { get; set; } = "DefaultAgent";
    public float PerceptionRadius { get; set; } = 25.0f;
    public float DecisionInterval { get; set; } = 0.2f;
    public bool IsEnabled { get; set; } = true;
}

public class LightComponentData : ComponentData
{
    public override string Type => "Light";
    public string LightType { get; set; } = "Directional";
    public float[] Color { get; set; } = new float[] { 1, 1, 1 };
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
}

public class SkeletalMeshComponentData : ComponentData
{
    public override string Type => "SkeletalMesh";
    public string MeshAssetPath { get; set; } = "";
    public bool IsLoaded { get; set; }
}

public class TerrainComponentData : ComponentData
{
    public override string Type => "Terrain";
    public string TerrainAssetPath { get; set; } = "";
    public int Width { get; set; } = 256;
    public int Height { get; set; } = 256;
    public float WorldWidth { get; set; } = 100.0f;
    public float WorldHeight { get; set; } = 100.0f;
    public float MaxElevation { get; set; } = 20.0f;
    public int ChunkSize { get; set; } = 32;
    public int LodCount { get; set; } = 3;
    public int SurfaceMode { get; set; } = 0;
    public bool CollisionEnabled { get; set; } = true;
}

/// <summary>Unified Physics component type. Serialized as "Physics".</summary>
public class PhysicsComponentData : ComponentData
{
    public override string Type => "Physics";

    public float Mass { get; set; } = 1.0f;
    public float Drag { get; set; } = 0.0f;
    public float AngularDrag { get; set; } = 0.05f;
    public bool UseGravity { get; set; } = true;
    public bool IsKinematic { get; set; } = false;
    public bool FreezePositionX { get; set; }
    public bool FreezePositionY { get; set; }
    public bool FreezePositionZ { get; set; }
    public bool FreezeRotationX { get; set; }
    public bool FreezeRotationY { get; set; }
    public bool FreezeRotationZ { get; set; }

    public string ColliderType { get; set; } = "Box";
    public float[] Center { get; set; } = new float[] { 0, 0, 0 };
    public float[] Size { get; set; } = new float[] { 1, 1, 1 };
    public float Radius { get; set; } = 0.5f;
    public float Height { get; set; } = 2.0f;
    public bool IsTrigger { get; set; } = false;
    public float Friction { get; set; } = 0.5f;
    public float Restitution { get; set; } = 0.3f;

    public string? PhysicsAssetPath { get; set; }
}

public class TeaScriptComponentData : ComponentData
{
    public override string Type => "TeaScript";
    public string ScriptAssetId { get; set; } = "";
    public bool AllowRuntimeUI { get; set; }
    public bool BlockRuntimeInput { get; set; }
}

public class NetworkManagerComponentData : ComponentData
{
    public override string Type => "NetworkManager";
    public string PrefabPath { get; set; } = "";
    public int MaxPlayers { get; set; } = 8;
    public float ReplicationInterval { get; set; } = 0.1f;
}

public class SpawnPointComponentData : ComponentData
{
    public override string Type => "SpawnPoint";
    public int Index { get; set; } = 0;
}

public class CarControllerComponentData : ComponentData
{
    public override string Type => "CarController";
    public float MotorForce { get; set; } = 12000f;
    public float BrakeForce { get; set; } = 22000f;
    public float MaxSteerAngle { get; set; } = 30f;
    public float DownForce { get; set; } = 100f;
    public float[] CenterOfMassOffset { get; set; } = new float[] { 0, -0.5f, 0 };

    public float[] CameraOffset { get; set; } = new float[] { 0, 2.5f, -6f };
    public float[] CameraTargetOffset { get; set; } = new float[] { 0, 0.5f, 0 };

    public float[] WheelPositionFL { get; set; } = new float[] { -0.8f, -0.3f, 1.5f };
    public float[] WheelPositionFR { get; set; } = new float[] { 0.8f, -0.3f, 1.5f };
    public float[] WheelPositionRL { get; set; } = new float[] { -0.8f, -0.3f, -1.5f };
    public float[] WheelPositionRR { get; set; } = new float[] { 0.8f, -0.3f, -1.5f };
    public float SuspensionRestLength { get; set; } = 0.24f;
    public float SuspensionStiffness { get; set; } = 22000.0f;
    public float SuspensionDamping { get; set; } = 2800.0f;
    public float WheelRadius { get; set; } = 0.30f;
}
