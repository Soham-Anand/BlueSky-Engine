using System;
using System.Numerics;
using BlueSky.Platform;
using BlueSky.Platform.Input;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Motif;
using BVec3 = BlueSky.Core.Math.Vector3;
using BQuat = BlueSky.Core.Math.Quaternion;

namespace BlueSky.Core.Gameplay;

public struct CarInput
{
    public float Throttle;
    public float Steer;
    public float Brake;
    public float Handbrake;
}

public class CarController : IPossessable
{
    public float MotorForce { get; set; } = 8000f;
    public float BrakeForce { get; set; } = 12000f;
    public float MaxSteerAngle { get; set; } = 30f;
    public float DownForce { get; set; } = 100f;
    public Vector3 CenterOfMassOffset { get; set; } = new(0, -0.5f, 0);
    public float SuspensionRestLength { get; set; } = 0.24f;
    public float SuspensionStiffness { get; set; } = 22000f;
    public float SuspensionDamping { get; set; } = 2800f;
    public float WheelRadius { get; set; } = 0.30f;

    private float _motorInput;
    private float _steerInput;
    private bool _brakeInput;
    private bool _handbrakeInput;

    private const float InputSmoothSpeed = 5.0f;
    private int _wheelTraceFrame;

    private VehiclePhysics? _vehiclePhysics;
    public WheelState[]? _wheelStates;

    private bool _usePerWheelPhysics;

    /// <summary>
    /// Enables the experimental internal wheel-body solver. It is deliberately
    /// opt-in: the stable default is the dynamic chassis body path used by the
    /// original vehicle implementation.
    /// Set this before Initialize when a vehicle has been validated for the
    /// wheel-body solver.
    /// </summary>
    public bool UsePerWheelPhysics
    {
        get => _usePerWheelPhysics;
        set => _usePerWheelPhysics = value;
    }

    private Entity _entity;
    public Entity StoredEntity => _entity;
    private World? _world;
    private PhysicsComponent? _physics;
    private TransformComponent? _transform;

    private bool _isPossessed;
    private PlayerController? _controller;

    // Chase camera
    private ChaseCameraController _chaseCamera = new();
    private Vector3 _cachedCamPos;
    private Vector3 _cachedCamTarget;
    private bool _chaseCamDirty = true;
    private bool _hasLastClientPosition;
    private Vector3 _lastClientPosition;
    private Vector3 _smoothedClientVelocity;

    // Network state (host-authoritative: client receives these from host for HUD)
    private bool _hasNetworkState = false;
    public bool HasNetworkState => _hasNetworkState;
    /// <summary>True when this controller has live data: PhysicsComponent (host) or network state (client). Scene cars without physics return false.</summary>
    public bool HasActiveData => _hasNetworkState || _physics.HasValue;
    private float _networkSpeed = 0f;
    private float _networkSpeedMPH = 0f;
    private int _networkGear = 1;
    private float _networkRPM = 800f;

    // Transmission (Phase 4)
    private const int GearCount = 6;
    private static readonly float[] GearRatios = { 3.5f, 2.2f, 1.6f, 1.2f, 0.95f, 0.78f };
    private const float DifferentialRatio = 3.42f;
    private const float RedlineRPM = 7000f;
    private const float IdleRPM = 800f;
    private int _currentGear = 1;
    private float _currentRPM;

    // ── Skeletal mesh bone-driven wheel system ───────────────────────────
    /// <summary>
    /// Default bone names for the vehicle skeletal mesh.
    /// These can be overridden per-entity via TeaScript (setWheelBone / setBodyBone).
    /// Based on the Blender armature naming: FL_mesh, FR_mesh, RL_mesh, RR_mesh, Main.
    /// </summary>
    public static readonly string[] DefaultBoneNames =
    {
        "FR_mesh",            // Index 0 - Front Right wheel
        "FL_mesh",            // Index 1 - Front Left wheel
        "RL_mesh",            // Index 2 - Rear Left wheel
        "RR_mesh",            // Index 3 - Rear Right wheel
        "Main"                // Index 4 - root body bone
    };

    public const int BoneSlot_RightFront = 0;
    public const int BoneSlot_LeftFront  = 1;
    public const int BoneSlot_LeftRear   = 2;
    public const int BoneSlot_RightRear  = 3;
    public const int BoneSlot_MainBody   = 4;
    public const int TotalBoneSlots      = 5;

    /// <summary>Current bone names (may be overridden by TeaScript)</summary>
    private string[] _boneNames = (string[])DefaultBoneNames.Clone();

    /// <summary>Resolved bone indices from the SkeletalMesh (order matches _boneNames)</summary>
    private int[] _boneIndices = Array.Empty<int>();

    /// <summary>Bind-pose local positions extracted from the bone data</summary>
    private BVec3[] _boneWheelPositions = Array.Empty<BVec3>();


    /// <summary>Reference to the loaded skeletal mesh (for runtime bone re-resolution)</summary>
    private SkeletalMesh? _skeletalMesh;
    public SkeletalMesh? SkeletalMesh => _skeletalMesh;

    // ── Bone transform overrides (set by TeaScript, read by renderer) ─────
    /// <summary>
    /// Per-bone 4×4 transform overrides written by TeaScript via setBoneTransform / setWheelTransform.
    /// The renderer applies these directly instead of using heuristic bone detection.
    /// Key = bone index, Value = local-space transform matrix.
    /// </summary>
    public Dictionary<int, System.Numerics.Matrix4x4> BoneTransformOverrides { get; } = new();

    /// <summary>
    /// Wheel slot → bone index mapping populated by refreshBones().
    /// Allows the renderer to map a wheel slot to its bone without heuristic guessing.
    /// </summary>
    public int[] WheelSlotToBoneIndex { get; private set; } = Array.Empty<int>();

    // ── Data-only driving state (set by TeaScript, consumed by physics) ───
    /// <summary>Throttle input 0..1 — TeaScript writes, VehiclePhysics reads.</summary>
    public float ThrottleInput { get; set; }
    /// <summary>Brake input 0..1 — TeaScript writes, VehiclePhysics reads.</summary>
    public float BrakeInput { get; set; }
    /// <summary>Steering input -1..1 — TeaScript writes, VehiclePhysics reads.</summary>
    public float SteerInput { get; set; }

    // ── Static bone name override registry (set from TeaScript before init) ──
    private static readonly Dictionary<uint, string[]> s_boneOverrides = new();

    /// <summary>
    /// Set a bone name override for a specific entity (called from TeaScript).
    /// The override will be applied when the car controller initializes.
    /// </summary>
    public static void SetBoneOverride(uint entityId, int slot, string boneName)
    {
        if (slot < 0 || slot >= TotalBoneSlots) return;

        if (!s_boneOverrides.TryGetValue(entityId, out var overrides))
        {
            overrides = (string[])DefaultBoneNames.Clone();
            s_boneOverrides[entityId] = overrides;
        }
        overrides[slot] = boneName ?? DefaultBoneNames[slot];
    }

    /// <summary>
    /// Set the body bone name override for a specific entity (called from TeaScript).
    /// </summary>
    public static void SetBodyBoneOverride(uint entityId, string boneName)
    {
        SetBoneOverride(entityId, BoneSlot_MainBody, boneName);
    }

    /// <summary>
    /// Check if bone overrides exist for an entity and return them.
    /// </summary>
    public static string[]? GetBoneOverrides(uint entityId)
    {
        s_boneOverrides.TryGetValue(entityId, out var overrides);
        return overrides;
    }

    /// <summary>
    /// Clear bone overrides for an entity (cleanup).
    /// </summary>
    public static void ClearBoneOverrides(uint entityId)
    {
        s_boneOverrides.Remove(entityId);
    }

    /// <summary>
    /// Clear all bone overrides (called during play mode stop).
    /// </summary>
    public static void ClearAllBoneOverrides()
    {
        s_boneOverrides.Clear();
    }

    public static bool TryResolveBoneName(SkeletalMesh mesh, string requestedName, out int boneIdx, out string resolvedName)
    {
        if (mesh.BoneNameToIndex.TryGetValue(requestedName, out boneIdx))
        {
            resolvedName = requestedName;
            return true;
        }

        foreach (var alias in GetBoneAliases(requestedName))
        {
            if (mesh.BoneNameToIndex.TryGetValue(alias, out boneIdx))
            {
                resolvedName = alias;
                return true;
            }
        }

        string requestedKey = NormalizeBoneName(requestedName);
        foreach (var kvp in mesh.BoneNameToIndex)
        {
            string candidateKey = NormalizeBoneName(kvp.Key);
            if (candidateKey == requestedKey || LooksLikeSameVehicleSlot(requestedKey, candidateKey))
            {
                boneIdx = kvp.Value;
                resolvedName = kvp.Key;
                return true;
            }
        }

        boneIdx = -1;
        resolvedName = requestedName;
        return false;
    }

    private static IEnumerable<string> GetBoneAliases(string requestedName)
    {
        return NormalizeBoneName(requestedName) switch
        {
            "frmesh" => new[] { "FR", "RF", "FrontRight", "RightFront", "Wheel_FR", "FR_Wheel", "front_right_wheel" },
            "flmesh" => new[] { "FL", "LF", "FrontLeft", "LeftFront", "Wheel_FL", "FL_Wheel", "front_left_wheel" },
            "rlmesh" => new[] { "RL", "LR", "RearLeft", "LeftRear", "Wheel_RL", "RL_Wheel", "rear_left_wheel" },
            "rrmesh" => new[] { "RR", "RearRight", "RightRear", "Wheel_RR", "RR_Wheel", "rear_right_wheel" },
            "main" => new[] { "Root", "root", "Body", "Chassis", "MainBody", "Armature" },
            _ => Array.Empty<string>()
        };
    }

    private static bool LooksLikeSameVehicleSlot(string requestedKey, string candidateKey)
    {
        return requestedKey switch
        {
            "frmesh" => ContainsAll(candidateKey, "front", "right") || candidateKey == "fr" || candidateKey == "rf",
            "flmesh" => ContainsAll(candidateKey, "front", "left") || candidateKey == "fl" || candidateKey == "lf",
            "rlmesh" => ContainsAll(candidateKey, "rear", "left") || candidateKey == "rl" || candidateKey == "lr",
            "rrmesh" => ContainsAll(candidateKey, "rear", "right") || candidateKey == "rr",
            "main" => candidateKey == "root" ||
                      candidateKey.Contains("body", StringComparison.OrdinalIgnoreCase) ||
                      candidateKey.Contains("chassis", StringComparison.OrdinalIgnoreCase) ||
                      candidateKey.Contains("main", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool ContainsAll(string value, params string[] parts)
    {
        foreach (var part in parts)
        {
            if (!value.Contains(part, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static string NormalizeBoneName(string name)
    {
        return new string((name ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    public bool CanBePossessed => true;
    public string DisplayName => "Sports Car";
    public bool IsPossessed => _isPossessed;

    public int CurrentGear => _hasNetworkState ? _networkGear : _currentGear;
    public float CurrentRPM => _hasNetworkState ? _networkRPM : _currentRPM;

    public void AdvertisePossession(string playerId = "Player1")
    {
        if (_controller != null && _isPossessed)
        {
            return;
        }

        PlayerController.Instance.RegisterPossessionRequest(this, playerId);
    }

    /// <summary>
    /// Initialize the car controller with an optional SkeletalMesh for bone-driven wheels.
    /// If no mesh is provided, falls back to hardcoded positions.
    /// </summary>
    public void Initialize(Entity entity, World world, SkeletalMesh? skeletalMesh = null)
    {
        _entity = entity;
        _world = world;
        _chaseCamera = new ChaseCameraController();

        bool hasPhysics = _world.TryGetComponent<PhysicsComponent>(entity, out var phys);
        bool hasTransform = _world.TryGetComponent<TransformComponent>(entity, out var tf);

        if (hasPhysics)
        {
            _physics = phys;
        }

        if (hasTransform)
        {
            _transform = tf;
        }

        // Apply bone name overrides from TeaScript if any exist
        var overrides = GetBoneOverrides((uint)entity.Id);
        if (overrides != null)
        {
            _boneNames = overrides;
        }

        // Store skeletal mesh reference and resolve bone indices
        _skeletalMesh = skeletalMesh;
        if (skeletalMesh != null)
        {
            ResolveBoneIndices(skeletalMesh);
        }

        InitializeWheelStates();

        try
        {
            System.IO.File.WriteAllText("/tmp/bluesky_wheel_trace.txt",
                $"=== wheel trace entity={entity.Id} initialized={DateTime.UtcNow:O} ===\n");
        }
        catch { }

        float vehicleMass = hasPhysics ? phys.Mass : 1500f;
        BVec3 comOffset = new BVec3(0, -0.5f, 0);

        var physicsWorld = BlueSky.Airborne.PhysicsTeaScriptBridge.PhysicsWorld;
        if (physicsWorld != null && _wheelStates != null)
        {
            _vehiclePhysics = new VehiclePhysics(
                physicsWorld,
                _wheelStates,
                vehicleMass,
                MotorForce,
                BrakeForce,
                MaxSteerAngle);
        }

    }

    /// <summary>
    /// Resolve bone indices from the skeletal mesh and extract bind-pose wheel positions.
    /// Uses _boneNames which may have been overridden by TeaScript.
    /// </summary>
    private void ResolveBoneIndices(SkeletalMesh mesh)
    {
        _boneIndices = new int[TotalBoneSlots];
        for (int i = 0; i < TotalBoneSlots; i++)
            _boneIndices[i] = -1;
        _boneWheelPositions = new BVec3[TotalBoneSlots];

        var logPath = "/tmp/bluesky_bones.txt";
        var log = new System.Text.StringBuilder();
        log.AppendLine($"\n=== BONE RESOLUTION for Entity_{_entity.Id} ===");
        log.AppendLine($"Available bones in skeletal mesh ({mesh.Bones.Length} total):");
        foreach (var kvp in mesh.BoneNameToIndex)
        {
            int pIdx = (kvp.Value >= 0 && kvp.Value < mesh.Bones.Length) ? mesh.Bones[kvp.Value].ParentIndex : -1;
            log.AppendLine($"  - '{kvp.Key}' → index {kvp.Value} (ParentIndex={pIdx})");
        }

        for (int i = 0; i < TotalBoneSlots; i++)
        {
            string boneName = _boneNames[i];
            if (TryResolveBoneName(mesh, boneName, out int boneIdx, out string resolvedName))
            {
                _boneIndices[i] = boneIdx;

                // Extract bind-pose position from the bone's LocalBindPose matrix
                var bindPose = mesh.Bones[boneIdx].LocalBindPose;
                _boneWheelPositions[i] = new BVec3(
                    bindPose.M41,  // Translation X
                    bindPose.M42,  // Translation Y
                    bindPose.M43   // Translation Z
                );

                string aliasText = string.Equals(boneName, resolvedName, StringComparison.Ordinal)
                    ? ""
                    : $" (resolved from '{boneName}')";

                log.AppendLine($"✅ Slot {i} '{resolvedName}'{aliasText} → bone index {boneIdx}, " +
                    $"bind pos ({_boneWheelPositions[i].X:F3}, {_boneWheelPositions[i].Y:F3}, {_boneWheelPositions[i].Z:F3})");
            }
            else
            {
                _boneIndices[i] = -1;
                log.AppendLine($"❌ Slot {i} bone '{boneName}' NOT FOUND in skeletal mesh!");
            }
        }
        
        System.IO.File.WriteAllText(logPath, log.ToString());
    }

    /// <summary>
    /// Re-resolve bone indices after bone names have been changed at runtime.
    /// Called from TeaScript after setWheelBone/setBodyBone.
    /// </summary>
    public void RefreshBoneMapping()
    {
        if (_skeletalMesh == null)
        {
            return;
        }
        ResolveBoneIndices(_skeletalMesh);
        InitializeWheelStates();

        // Populate WheelSlotToBoneIndex from resolved _boneIndices
        // Bone slot order: FR(0), FL(1), RL(2), RR(3), Main(4)
        // Wheel state order: FL(0), FR(1), RL(2), RR(3)
        int boneSlotForWheelState0 = BoneSlot_LeftFront;   // FL
        int boneSlotForWheelState1 = BoneSlot_RightFront;  // FR
        int boneSlotForWheelState2 = BoneSlot_LeftRear;    // RL
        int boneSlotForWheelState3 = BoneSlot_RightRear;   // RR
        WheelSlotToBoneIndex = new int[4];
        if (_boneIndices.Length > boneSlotForWheelState0) WheelSlotToBoneIndex[0] = _boneIndices[boneSlotForWheelState0];
        if (_boneIndices.Length > boneSlotForWheelState1) WheelSlotToBoneIndex[1] = _boneIndices[boneSlotForWheelState1];
        if (_boneIndices.Length > boneSlotForWheelState2) WheelSlotToBoneIndex[2] = _boneIndices[boneSlotForWheelState2];
        if (_boneIndices.Length > boneSlotForWheelState3) WheelSlotToBoneIndex[3] = _boneIndices[boneSlotForWheelState3];
    }

    /// <summary>
    /// Set a 4×4 local-space transform override for a specific bone index.
    /// Called from TeaScript via setBoneTransform or setWheelTransform.
    /// </summary>
    public void SetBoneTransformOverride(int boneIndex, System.Numerics.Matrix4x4 matrix)
    {
        BoneTransformOverrides[boneIndex] = matrix;
    }

    /// <summary>
    /// Convenience: set a wheel slot's bone transform from spin + steer angles (radians).
    /// Computes the rotation matrix and stores it as a bone transform override.
    /// </summary>
    public void SetWheelSpinAndSteer(int wheelSlot, float spinAngle, float steerAngle)
    {
        if (wheelSlot < 0 || wheelSlot >= 4 || WheelSlotToBoneIndex == null || wheelSlot >= WheelSlotToBoneIndex.Length)
            return;

        int boneIndex = WheelSlotToBoneIndex[wheelSlot];
        if (boneIndex < 0) return;

        var spin = System.Numerics.Quaternion.CreateFromAxisAngle(
            System.Numerics.Vector3.UnitX, spinAngle);
        var steer = System.Numerics.Quaternion.CreateFromAxisAngle(
            System.Numerics.Vector3.UnitY, steerAngle);
        // Apply spin in the wheel's local frame, then steer the whole wheel
        // around the upright axis. Explicit multiplication keeps this path
        // consistent with GetWheelTransformMatrix and the per-frame updater.
        var rotation = steer * spin;
        BoneTransformOverrides[boneIndex] = System.Numerics.Matrix4x4.CreateFromQuaternion(rotation);
    }

    /// <summary>Clear all bone transform overrides (e.g. on play mode stop).</summary>
    public void ClearBoneTransformOverrides()
    {
        BoneTransformOverrides.Clear();
    }

    /// <summary>
    /// Override a specific wheel's local position (called from TeaScript).
    /// Slot: 0=FrontLeft, 1=FrontRight, 2=RearLeft, 3=RearRight
    /// </summary>
    public void SetWheelLocalPosition(int slot, float x, float y, float z)
    {
        if (_wheelStates == null || slot < 0 || slot >= _wheelStates.Length) return;
        
        // CRITICAL: WheelConfig is a struct, so we must copy-modify-assign
        var config = _wheelStates[slot].Config;
        config.LocalPosition = new BVec3(x, y, z);
        _wheelStates[slot].Config = config;
        
        _wheelStates[slot].WorldPosition = new BVec3(x, y, z);
    }

    /// <summary>
    /// Set which wheels are drive wheels (called from TeaScript).
    /// Pass 4 booleans: frontLeft, frontRight, rearLeft, rearRight
    /// </summary>
    public void SetDriveWheels(bool fl, bool fr, bool rl, bool rr)
    {
        if (_wheelStates == null) return;
        
        // Fix struct copy issue for all wheels
        var cfg0 = _wheelStates[0].Config; cfg0.IsDriveWheel = fl; _wheelStates[0].Config = cfg0;
        var cfg1 = _wheelStates[1].Config; cfg1.IsDriveWheel = fr; _wheelStates[1].Config = cfg1;
        var cfg2 = _wheelStates[2].Config; cfg2.IsDriveWheel = rl; _wheelStates[2].Config = cfg2;
        var cfg3 = _wheelStates[3].Config; cfg3.IsDriveWheel = rr; _wheelStates[3].Config = cfg3;
        
    }

    /// <summary>
    /// Set which wheels steer (called from TeaScript).
    /// </summary>
    public void SetSteerWheels(bool fl, bool fr, bool rl, bool rr)
    {
        if (_wheelStates == null) return;
        
        // Fix struct copy issue for all wheels
        var cfg0 = _wheelStates[0].Config; cfg0.IsSteerWheel = fl; _wheelStates[0].Config = cfg0;
        var cfg1 = _wheelStates[1].Config; cfg1.IsSteerWheel = fr; _wheelStates[1].Config = cfg1;
        var cfg2 = _wheelStates[2].Config; cfg2.IsSteerWheel = rl; _wheelStates[2].Config = cfg2;
        var cfg3 = _wheelStates[3].Config; cfg3.IsSteerWheel = rr; _wheelStates[3].Config = cfg3;
        
    }

    private void InitializeWheelStates()
    {
        if (_wheelStates == null)
        {
            _wheelStates = new WheelState[4];
        }

        // Use bone positions from the skeletal mesh if available, otherwise fall back to defaults
        BVec3[] wheelPositions = new BVec3[4];

        if (_boneWheelPositions.Length >= 4 && _boneIndices[0] >= 0)
        {
            // Bone order: 0=RightFront, 1=LeftFront, 2=LeftRear, 3=RightRear
            wheelPositions[0] = _boneWheelPositions[BoneSlot_LeftFront];   // Front Left
            wheelPositions[1] = _boneWheelPositions[BoneSlot_RightFront];  // Front Right
            wheelPositions[2] = _boneWheelPositions[BoneSlot_LeftRear];    // Rear Left
            wheelPositions[3] = _boneWheelPositions[BoneSlot_RightRear];   // Rear Right

            wheelPositions = NormalizeSkeletalWheelPositionsForPhysics(wheelPositions);
        }
        else
        {
            // Fallback: hardcoded positions (no skeletal mesh or missing bones)
            wheelPositions[0] = new BVec3(-0.8f, -0.3f,  1.5f); // Front Left
            wheelPositions[1] = new BVec3( 0.8f, -0.3f,  1.5f); // Front Right
            wheelPositions[2] = new BVec3(-0.8f, -0.3f, -1.5f); // Rear Left
            wheelPositions[3] = new BVec3( 0.8f, -0.3f, -1.5f); // Rear Right

        }

        for (int i = 0; i < 4; i++)
        {
            if (_wheelStates[i] == null)
            {
                _wheelStates[i] = new WheelState();
            }

            _wheelStates[i].Config = new WheelConfig
            {
                LocalPosition = wheelPositions[i],
                SuspensionRestLength = SuspensionRestLength,
                SuspensionStiffness = SuspensionStiffness,
                SuspensionDamping = SuspensionDamping,
                WheelRadius = WheelRadius,
                IsDriveWheel = i >= 2,          // Rear wheels are driven
                IsSteerWheel = i < 2,            // Front wheels steer
                MaxSteerAngle = 30.0f,
                TractionMultiplier = 1.0f
            };
            _wheelStates[i].WorldPosition = wheelPositions[i];
        }
    }

    private static BVec3[] NormalizeSkeletalWheelPositionsForPhysics(BVec3[] source)
    {
        // Preserve exact 3D model wheel positions so physics raycasts and tire forces
        // match visual mesh geometry 1:1 without artificial scale distortion.
        return source;
    }

    public void OnPossessed(PlayerController controller)
    {
        _isPossessed = true;
        _controller = controller;
        _chaseCamera.Reset();

        BlueSky.Core.Scripting.TeaScriptSystem.CallFunctionOnAllScripts("onCarPossessed", DisplayName);
    }

    public void OnUnpossessed()
    {
        _isPossessed = false;
        _controller = null;

        _motorInput = 0;
        _steerInput = 0;
        _brakeInput = false;
        _handbrakeInput = false;


        BlueSky.Core.Scripting.TeaScriptSystem.CallFunctionOnAllScripts("onCarUnpossessed");
    }

    public void Update(float deltaTime)
    {
        if (_vehiclePhysics != null && _wheelStates != null)
        {
            if (!_isPossessed)
            {
                // If not possessed, update wheel spin based on vehicle speed
                var velocity = GetVelocity();
                var forward = GetForwardVector();
                float speed = Vector3.Dot(velocity, forward);
                
                foreach (var wheel in _wheelStates)
                {
                    if (wheel.Config.WheelRadius > 0)
                    {
                        wheel.AngularVelocity = speed / wheel.Config.WheelRadius;
                        wheel.SpinAngle += wheel.AngularVelocity * deltaTime;
                    }
                    wheel.SteerAngle = 0.0f; // No steering input when unpossessed
                }
            }

            // Update wheel bone transforms for rendering (BoneTransformOverrides path)
            UpdateWheelBoneTransforms();

            if (++_wheelTraceFrame % 30 == 0)
            {
                try
                {
                    var trace = new System.Text.StringBuilder();
                    trace.Append($"frame={_wheelTraceFrame} possessed={_isPossessed} dt={deltaTime:F5} ");
                    for (int i = 0; i < _wheelStates.Length; i++)
                    {
                        var w = _wheelStates[i];
                        trace.Append($"w{i}[spin={w.SpinAngle:F4},ang={w.AngularVelocity:F4},ground={w.IsGrounded},drive={w.Config.IsDriveWheel},steer={w.SteerAngle:F2}] ");
                    }
                    trace.Append("bones=");
                    for (int i = 0; i < System.Math.Min(5, _boneIndices.Length); i++) trace.Append($"{_boneIndices[i]},");
                    trace.AppendLine();
                    System.IO.File.AppendAllText("/tmp/bluesky_wheel_trace.txt", trace.ToString());
                }
                catch { }
            }
        }

        // Update chase camera exactly once with real deltaTime
        if (_isPossessed)
        {
            _chaseCamDirty = true;
            UpdateChaseCamera(deltaTime);

            float speedMPH = GetSpeedMPH();
            BlueSky.Core.Scripting.TeaScriptSystem.CallFunctionOnAllScripts("updateSpeed", (double)MathF.Round(speedMPH));
            BlueSky.Core.Scripting.TeaScriptSystem.CallFunctionOnAllScripts("updateRPM", (double)MathF.Round(_currentRPM));
            BlueSky.Core.Scripting.TeaScriptSystem.CallFunctionOnAllScripts("updateGear", _currentGear);
        }
    }

    /// <summary>
    /// Updates wheel bone transforms in BoneTransformOverrides dictionary.
    /// This is used by the renderer's NEW PATH (TeaScript bone overrides).
    /// Called every Update frame to keep wheel visuals synchronized with physics.
    /// </summary>
    private void UpdateWheelBoneTransforms()
    {
        if (_wheelStates == null || WheelSlotToBoneIndex == null ||
            WheelSlotToBoneIndex.Length < 4)
            return;

        int mainBodyBoneIdx = (_boneIndices != null && _boneIndices.Length > BoneSlot_MainBody) ? _boneIndices[BoneSlot_MainBody] : -1;

        for (int wheelSlot = 0; wheelSlot < 4; wheelSlot++)
        {
            int boneIndex = WheelSlotToBoneIndex[wheelSlot];
            if (boneIndex < 0) continue;

            // Main chassis / body / root bone MUST NEVER receive a wheel rotation override!
            if (boneIndex == mainBodyBoneIdx) continue;
            if (_skeletalMesh != null && boneIndex >= 0 && boneIndex < _skeletalMesh.Bones.Length)
            {
                string bName = _skeletalMesh.Bones[boneIndex].Name.ToLowerInvariant();
                if (bName == "armature" || bName == "root" || bName == "body" || bName == "chassis" || bName == "main")
                    continue;
            }

            WheelState wheel = _wheelStates[wheelSlot];
            float steerRadians = wheel.SteerAngle * (MathF.PI / 180f);
            Quaternion spin = Quaternion.CreateFromAxisAngle(Vector3.UnitX, wheel.SpinAngle);
            Quaternion steer = Quaternion.CreateFromAxisAngle(Vector3.UnitY, steerRadians);
            Quaternion rotation = Quaternion.Normalize(steer * spin);

            BoneTransformOverrides[boneIndex] = Matrix4x4.CreateFromQuaternion(rotation);
        }
    }

    public void FixedUpdate(float fixedDeltaTime)
    {
        ApplyCarPhysics(fixedDeltaTime);
    }

    public void ProcessInput(IInputContext input, float deltaTime)
    {
        if (!_isPossessed || input == null) return;

        float newMotorInput = 0;
        float newSteerInput = 0;

        if (input.IsKeyDown(KeyCode.W) || input.IsKeyDown(KeyCode.Up))
            newMotorInput = 1.0f;
        else if (input.IsKeyDown(KeyCode.S) || input.IsKeyDown(KeyCode.Down))
            newMotorInput = -1.0f;

        if (input.IsKeyDown(KeyCode.A) || input.IsKeyDown(KeyCode.Left))
            newSteerInput = 1.0f;   // A = steer left (positive)
        else if (input.IsKeyDown(KeyCode.D) || input.IsKeyDown(KeyCode.Right))
            newSteerInput = -1.0f;  // D = steer right (negative)

        _brakeInput = input.IsKeyDown(KeyCode.S) || input.IsKeyDown(KeyCode.Down);
        _handbrakeInput = input.IsKeyDown(KeyCode.Space);

        _motorInput = Lerp(_motorInput, newMotorInput, InputSmoothSpeed * deltaTime);
        _steerInput = Lerp(_steerInput, newSteerInput, InputSmoothSpeed * deltaTime);
    }

    /// <summary>
    /// Apply pre-read input values (from network replication) directly.
    /// Used by host to apply client input, bypassing keyboard reading.
    /// </summary>
    public void ApplyNetInput(CarInput input, float deltaTime)
    {
        float newMotorInput = 0f;
        float newSteerInput = 0f;

        if (input.Throttle > 0f)
            newMotorInput = input.Throttle;
        else if (input.Brake > 0f)
            newMotorInput = -input.Brake;

        newSteerInput = -input.Steer; // Match ProcessInput convention (A=positive steer)
        _brakeInput = input.Brake > 0f;
        _handbrakeInput = input.Handbrake > 0f;

        _motorInput = Lerp(_motorInput, newMotorInput, InputSmoothSpeed * deltaTime);
        _steerInput = Lerp(_steerInput, newSteerInput, InputSmoothSpeed * deltaTime);
    }

    private float _physicsAccumulator = 0f;
    private const float FixedPhysicsDt = 1.0f / 60.0f;

    private void ApplyCarPhysics(float deltaTime)
    {
        if (_world == null || _vehiclePhysics == null) return;

        // 1. Collect merged input
        float effectiveThrottleInput = _motorInput;
        float effectiveBrakeInput = _brakeInput ? 1.0f : 0.0f;
        float effectiveSteerInput = _steerInput;

        if (ThrottleInput != 0f || BrakeInput != 0f || SteerInput != 0f)
        {
            effectiveThrottleInput = ThrottleInput;
            effectiveBrakeInput = BrakeInput;
            effectiveSteerInput = SteerInput;
        }

        var inputState = new VehicleInput
        {
            Throttle = System.Math.Clamp(effectiveThrottleInput, 0f, 1f),
            Brake = System.Math.Clamp(effectiveBrakeInput, 0f, 1f),
            Steer = System.Math.Clamp(effectiveSteerInput, -1f, 1f),
            Handbrake = _handbrakeInput ? 1.0f : 0.0f
        };

        // 2. Fixed Timestep Accumulator Loop (60 Hz Scheduler)
        _physicsAccumulator += MathF.Min(deltaTime, 0.1f);
        while (_physicsAccumulator >= FixedPhysicsDt)
        {
            // Execute ONE simulation step of VehiclePhysics
            // VehiclePhysics calculates forces/torques and calls AddForceAtPosition on Jolt body
            _vehiclePhysics.Step(FixedPhysicsDt, inputState, _entity);

            _physicsAccumulator -= FixedPhysicsDt;
        }

        // 3. Read Jolt Authoritative Chassis Transform (Jolt is single source of truth!)
        var physicsPos = BlueSky.Airborne.PhysicsTeaScriptBridge.GetPosition(_entity);
        var physicsRot = BlueSky.Airborne.PhysicsTeaScriptBridge.GetRotation(_entity);

        if (_transform.HasValue)
        {
            var t = _transform.Value;
            t.Position = physicsPos.ToBlue();
            t.Rotation = physicsRot.ToBlue();
            _transform = t;

            if (_world != null && _world.IsEntityValid(_entity) &&
                _world.HasComponent<TransformComponent>(_entity))
            {
                ref var ecsTransform = ref _world.GetComponent<TransformComponent>(_entity);
                ecsTransform.Position = physicsPos.ToBlue();
                ecsTransform.Rotation = physicsRot.ToBlue();
            }
        }

        // Sync transmission RPM for UI / HUD
        _currentRPM = _vehiclePhysics.EngineRPM;
        _currentGear = _vehiclePhysics.CurrentGear;

        // 4. Synchronize Visual Wheel Transforms & Skeletal Bone Overrides
        UpdateWheelPositions();
    }

    // TeaScript writes BoneTransformOverrides; the renderer reads them directly.

    /// <summary>
    /// Extracts the yaw-only (Y-axis) rotation from a quaternion, discarding
    /// roll (X) and pitch (Z). This prevents Jolt contact impulses from
    /// visually tilting the car chassis.
    /// </summary>
    private static System.Numerics.Quaternion ExtractYawOnlyRotation(System.Numerics.Quaternion q)
    {
        // Transform forward vector (0, 0, 1) by q to get the world-space facing direction
        var forward = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, q);

        // Project onto XZ plane to isolate pure planar yaw
        var planar = new System.Numerics.Vector3(forward.X, 0f, forward.Z);
        if (planar.LengthSquared() < 0.0001f)
            return System.Numerics.Quaternion.Identity;

        planar = System.Numerics.Vector3.Normalize(planar);
        float yawAngle = MathF.Atan2(planar.X, planar.Z);
        return System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, yawAngle);
    }

    private void UpdateTransmission(float deltaTime)
    {
        if (_wheelStates == null) return;
        float speed = GetSpeed();
        float wheelAngularVelocity = 0f;
        for (int i = 0; i < _wheelStates.Length; i++)
        {
            if (_wheelStates[i].Config.IsDriveWheel)
            {
                wheelAngularVelocity = MathF.Max(wheelAngularVelocity, MathF.Abs(_wheelStates[i].AngularVelocity));
            }
        }

        float rpm = wheelAngularVelocity * GearRatios[_currentGear - 1] * DifferentialRatio * (60f / MathF.Tau);
        rpm = MathF.Max(rpm, IdleRPM);

        // Auto-shift up at redline
        if (rpm >= RedlineRPM && _currentGear < GearCount)
        {
            _currentGear++;
            rpm = rpm * GearRatios[_currentGear - 1] / GearRatios[_currentGear - 2];
        }

        // Auto-shift down when RPM drops too low
        float motorInput = ThrottleInput != 0f ? ThrottleInput : _motorInput;
        if (rpm < IdleRPM * 1.5f && _currentGear > 1 && motorInput > 0)
        {
            _currentGear--;
            rpm = rpm * GearRatios[_currentGear - 1] / GearRatios[_currentGear];
        }

        // Coasting: if no throttle and low speed, downshift
        if (motorInput < 0.1f && rpm < IdleRPM * 1.2f && _currentGear > 1)
        {
            _currentGear--;
        }

        _currentRPM = rpm;
    }

    private float CalculateTorqueCurve(float rpm)
    {
        // Simple torque curve: peak torque around 4000 RPM
        float normalizedRPM = rpm / RedlineRPM;
        float torque = 1.0f - MathF.Pow(normalizedRPM - 0.57f, 2) * 3.0f;
        return MathF.Max(0.3f, MathF.Min(1.0f, torque));
    }

    private void UpdateWheelPositions()
    {
        if (_vehiclePhysics == null || _wheelStates == null) return;

        // Per-wheel: wheel positions are managed by the per-wheel solver (contact-based)
        if (_usePerWheelPhysics) return;

        var carPos = BlueSky.Airborne.PhysicsTeaScriptBridge.GetPosition(_entity);
        var carRot = BlueSky.Airborne.PhysicsTeaScriptBridge.GetRotation(_entity);

        BVec3 pos = carPos.ToBlue();
        BQuat rot = carRot.ToBlue();

        foreach (var wheel in _wheelStates)
        {
            wheel.WorldPosition = pos + rot * wheel.Config.LocalPosition;
        }
    }

    // Camera is now handled via ChaseCameraController (Phase 3)
    private void UpdateChaseCamera(float deltaTime)
    {
        if (!_chaseCamDirty) return;
        _chaseCamDirty = false;

        var velocity = BlueSky.Airborne.PhysicsTeaScriptBridge.GetVelocity(_entity);

        Vector3 carPos;
        Quaternion carRot;

        // Follow the same interpolated ECS transform that the renderer uses.
        // Reading the backend body here would put the camera one fixed tick
        // ahead of the visible chassis whenever render FPS exceeds 60.
        if (_world != null && _world.TryGetComponent<TransformComponent>(_entity, out var liveTf))
        {
            carPos = new Vector3(liveTf.Position.X, liveTf.Position.Y, liveTf.Position.Z);
            carRot = new Quaternion(liveTf.Rotation.X, liveTf.Rotation.Y, liveTf.Rotation.Z, liveTf.Rotation.W);

            // Network clients do not own a physics body. Estimate their
            // camera velocity from the interpolated transform instead of
            // using a permanently zero backend velocity.
            var physicsWorld = BlueSky.Airborne.PhysicsTeaScriptBridge.PhysicsWorld;
            if (physicsWorld == null || !physicsWorld.HasBody(_entity))
            {
                if (_hasLastClientPosition)
                {
                    Vector3 rawVelocity = (carPos - _lastClientPosition) / MathF.Max(deltaTime, 0.001f);
                    _smoothedClientVelocity = Vector3.Lerp(_smoothedClientVelocity, rawVelocity, 0.1f);
                    velocity = _smoothedClientVelocity;
                }
                _lastClientPosition = carPos;
                _hasLastClientPosition = true;
            }
        }
        else
        {
            var physicsPos = BlueSky.Airborne.PhysicsTeaScriptBridge.GetPosition(_entity);
            var physicsRot = BlueSky.Airborne.PhysicsTeaScriptBridge.GetRotation(_entity);
            carPos = new Vector3(physicsPos.X, physicsPos.Y, physicsPos.Z);
            carRot = physicsRot;
        }

        _chaseCamera.Update(deltaTime, carPos, carRot, velocity,
            out _cachedCamPos, out _cachedCamTarget);
    }

    public Vector3 GetCameraPosition()
    {
        return _cachedCamPos;
    }

    public Vector3 GetCameraTarget()
    {
        return _cachedCamTarget;
    }

    private Vector3 GetForwardVector()
    {
        var physicsRot = BlueSky.Airborne.PhysicsTeaScriptBridge.GetRotation(_entity);
        var rotationMatrix = Matrix4x4.CreateFromQuaternion(physicsRot);
        return Vector3.Transform(Vector3.UnitZ, rotationMatrix);
    }

    private Vector3 GetRightVector()
    {
        var physicsRot = BlueSky.Airborne.PhysicsTeaScriptBridge.GetRotation(_entity);
        var rotationMatrix = Matrix4x4.CreateFromQuaternion(physicsRot);
        return Vector3.Transform(Vector3.UnitX, rotationMatrix);
    }

    private Vector3 GetVelocity()
    {
        return BlueSky.Airborne.PhysicsTeaScriptBridge.GetVelocity(_entity);
    }

    private void ApplyForce(Vector3 force)
    {
        if (force.LengthSquared() > 0.01f && _world != null)
        {
            BlueSky.Airborne.PhysicsTeaScriptBridge.AddImpulse(_entity, force);
        }
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * MathF.Min(t, 1.0f);
    }

    public float GetSpeed()
    {
        if (_hasNetworkState) return _networkSpeed;
        return GetVelocity().Length();
    }

    public float GetSpeedMPH()
    {
        if (_hasNetworkState) return _networkSpeedMPH;
        return GetSpeed() * 2.237f;
    }

    /// <summary>
    /// Apply network-replicated car state from host (for client-side HUD).
    /// </summary>
    public void ApplyNetworkState(float speed, float speedMPH, int gear, float rpm)
    {
        _hasNetworkState = true;
        _networkSpeed = speed;
        _networkSpeedMPH = speedMPH;
        _networkGear = gear;
        _networkRPM = rpm;
    }

    /// <summary>
    /// Number of wheels (always 4).
    /// </summary>
    public int WheelCount => _wheelStates?.Length ?? 0;

    /// <summary>
    /// Get a rotation matrix for a wheel slot (0=FL, 1=FR, 2=RL, 3=RR)
    /// encoding spin (X-axis) and steer (Y-axis) from the current WheelState.
    /// Usable by the renderer to animate static-mesh submeshes without a skeletal mesh.
    /// </summary>
    public Matrix4x4 GetWheelTransformMatrix(int wheelIndex)
    {
        if (_wheelStates == null || wheelIndex < 0 || wheelIndex >= _wheelStates.Length)
            return Matrix4x4.Identity;

        WheelState wheel = _wheelStates[wheelIndex];

        Quaternion spin = Quaternion.CreateFromAxisAngle(Vector3.UnitX, wheel.SpinAngle);
        Quaternion steer = Quaternion.CreateFromAxisAngle(Vector3.UnitY,
            wheel.SteerAngle * (MathF.PI / 180f));

        return Matrix4x4.CreateFromQuaternion(steer * spin);
    }

    /// <summary>Debug: expose raw SpinAngle for renderer diagnostics.</summary>
    public float GetDebugSpinAngle(int wheelIndex)
    {
        if (_wheelStates == null || wheelIndex < 0 || wheelIndex >= _wheelStates.Length)
            return -999f;
        return _wheelStates[wheelIndex].SpinAngle;
    }

    /// <summary>
    /// Get the local-space wheel center position for a wheel slot.
    /// Used by the renderer to identify which submeshes belong to which wheel.
    /// </summary>
    public Vector3 GetWheelLocalPosition(int wheelIndex)
    {
        if (_wheelStates == null || wheelIndex < 0 || wheelIndex >= _wheelStates.Length)
            return Vector3.Zero;

        var lp = _wheelStates[wheelIndex].Config.LocalPosition;
        return new Vector3(lp.X, lp.Y, lp.Z);
    }

    /// <summary>
    /// Resolves a pack-skeleton bone NAME to the configured wheel slot.
    /// Used for stratapack-imported meshes, which carry a skeleton sidecar
    /// but no FBX SkeletalMesh object. Matches _boneNames slots 0-3
    /// (wheels only — the Main/body slot never matches).
    /// </summary>
    public bool TryGetWheelSlotForBoneName(string? boneName, out int wheelSlot)
    {
        wheelSlot = -1;
        if (string.IsNullOrWhiteSpace(boneName) || _boneNames == null)
            return false;
        for (int slot = 0; slot < 4 && slot < _boneNames.Length; slot++)
        {
            if (string.Equals(_boneNames[slot], boneName, StringComparison.OrdinalIgnoreCase))
            {
                wheelSlot = slot;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Resolves an imported skeletal bone index to the configured wheel slot.
    /// The viewport uses this to apply a rigid fallback rotation to wheel
    /// submeshes when GPU skinning is unavailable.
    /// </summary>
    public bool TryGetWheelSlotForBoneIndex(int boneIndex, out int wheelSlot)
    {
        wheelSlot = -1;
        if (_boneIndices == null || boneIndex < 0) return false;

        // The root/body/chassis bone is NEVER a wheel slot!
        int mainBodyBoneIdx = (_boneIndices.Length > BoneSlot_MainBody) ? _boneIndices[BoneSlot_MainBody] : -1;
        if (boneIndex == mainBodyBoneIdx)
        {
            return false;
        }

        if (_skeletalMesh != null && boneIndex >= 0 && boneIndex < _skeletalMesh.Bones.Length)
        {
            string boneName = _skeletalMesh.Bones[boneIndex].Name.ToLowerInvariant();
            if (boneName == "armature" || boneName == "root" || boneName == "body" || boneName == "chassis" || boneName == "main")
            {
                return false;
            }
        }

        // Bone slots are FR, FL, RL, RR while wheel state slots are FL, FR, RL, RR.
        int[] stateForBone = { 1, 0, 2, 3 };
        for (int boneSlot = 0; boneSlot < 4 && boneSlot < _boneIndices.Length; boneSlot++)
        {
            if (_boneIndices[boneSlot] >= 0 && _boneIndices[boneSlot] == boneIndex)
            {
                wheelSlot = stateForBone[boneSlot];
                return true;
            }
        }
        return false;
    }
    
    /// <summary>
    /// Get wheel state for TeaScript API - provides access to slip, grounding, suspension, etc.
    /// </summary>
    public WheelState? GetWheelState(int wheelIndex)
    {
        if (_wheelStates == null || wheelIndex < 0 || wheelIndex >= _wheelStates.Length)
            return null;
        return _wheelStates[wheelIndex];
    }
}
