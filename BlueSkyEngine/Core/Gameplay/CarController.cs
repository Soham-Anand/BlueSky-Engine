using System;
using System.Numerics;
using BlueSky.Platform;
using BlueSky.Platform.Input;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Animation;
using BVec3 = BlueSky.Core.Math.Vector3;
using BQuat = BlueSky.Core.Math.Quaternion;

namespace BlueSky.Core.Gameplay;

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

    private VehiclePhysics _vehiclePhysics;
    public WheelState[] _wheelStates;

    private Entity _entity;
    private World? _world;
    private RigidbodyComponent? _rigidbody;
    private TransformComponent? _transform;

    private bool _isPossessed;
    private PlayerController? _controller;

    // Chase camera
    private ChaseCameraController _chaseCamera;
    private Vector3 _cachedCamPos;
    private Vector3 _cachedCamTarget;
    private bool _chaseCamDirty = true;
    private int _frameCounter = 0;

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

    /// <summary>Slot indices for the required bones</summary>
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

    /// <summary>Optional AnimationController for driving bone transforms</summary>
    private AnimationController? _animController;
    public AnimationController? AnimController => _animController;

    /// <summary>Reference to the loaded skeletal mesh (for runtime bone re-resolution)</summary>
    private SkeletalMesh? _skeletalMesh;
    public SkeletalMesh? SkeletalMesh => _skeletalMesh;

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

    public int CurrentGear => _currentGear;
    public float CurrentRPM => _currentRPM;

    public void AdvertisePossession(string playerId = "Player1")
    {
        if (_controller != null && _isPossessed)
        {
            Console.WriteLine($"[CarController] Already possessed! Cannot advertise.");
            return;
        }

        Console.WriteLine($"[CarController] Advertising possession for {playerId}...");
        PlayerController.Instance.RegisterPossessionRequest(this, playerId);
    }

    /// <summary>
    /// Initialize the car controller with an optional SkeletalMesh for bone-driven wheels.
    /// If no mesh is provided, falls back to hardcoded positions.
    /// </summary>
    public void Initialize(Entity entity, World world, SkeletalMesh? skeletalMesh = null, AnimationController? animController = null)
    {
        _entity = entity;
        _world = world;
        _chaseCamera = new ChaseCameraController();
        _animController = animController;

        bool hasRigidbody = _world.TryGetComponent<RigidbodyComponent>(entity, out var rb);
        bool hasTransform = _world.TryGetComponent<TransformComponent>(entity, out var tf);

        if (hasRigidbody)
        {
            _rigidbody = rb;
            Console.WriteLine($"[CarController] Rigidbody found on Entity_{entity.Id}");
        }
        else
        {
            Console.WriteLine($"[CarController] No Rigidbody on Entity_{entity.Id} - physics won't work!");
        }

        if (hasTransform)
        {
            _transform = tf;
            Console.WriteLine($"[CarController] Transform found on Entity_{entity.Id} at position {tf.Position}");
        }
        else
        {
            Console.WriteLine($"[CarController] No Transform on Entity_{entity.Id}!");
        }

        // Apply bone name overrides from TeaScript if any exist
        var overrides = GetBoneOverrides((uint)entity.Id);
        if (overrides != null)
        {
            _boneNames = overrides;
            Console.WriteLine($"[CarController] 🦴 Using custom bone name overrides from TeaScript");
        }

        // Store skeletal mesh reference and resolve bone indices
        _skeletalMesh = skeletalMesh;
        if (skeletalMesh != null)
        {
            ResolveBoneIndices(skeletalMesh);
        }

        InitializeWheelStates();

        float vehicleMass = hasRigidbody ? rb.Mass : 1500f;
        BVec3 comOffset = new BVec3(0, -0.5f, 0);

        var physicsWorld = BlueSky.Physics.PhysicsTeaScriptBridge.PhysicsWorld;
        if (physicsWorld != null)
        {
            _vehiclePhysics = new VehiclePhysics(
                physicsWorld,
                _wheelStates,
                vehicleMass,
                comOffset,
                MotorForce,
                BrakeForce,
                MaxSteerAngle);
        }

        Console.WriteLine($"[CarController] Car Entity_{entity.Id} initialized!");
    }

    /// <summary>
    /// Resolve bone indices from the skeletal mesh and extract bind-pose wheel positions.
    /// Uses _boneNames which may have been overridden by TeaScript.
    /// </summary>
    private void ResolveBoneIndices(SkeletalMesh mesh)
    {
        _boneIndices = new int[TotalBoneSlots];
        _boneWheelPositions = new BVec3[TotalBoneSlots];

        var logPath = "/tmp/bluesky_bones.txt";
        var log = new System.Text.StringBuilder();
        log.AppendLine($"\n=== BONE RESOLUTION for Entity_{_entity.Id} ===");
        log.AppendLine($"Available bones in skeletal mesh ({mesh.Bones.Length} total):");
        foreach (var kvp in mesh.BoneNameToIndex)
        {
            log.AppendLine($"  - '{kvp.Key}' → index {kvp.Value}");
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
        Console.WriteLine($"[CarController] 🦴 Bone resolution logged to {logPath}");
    }

    /// <summary>
    /// Re-resolve bone indices after bone names have been changed at runtime.
    /// Called from TeaScript after setWheelBone/setBodyBone.
    /// </summary>
    public void RefreshBoneMapping()
    {
        if (_skeletalMesh == null)
        {
            Console.WriteLine($"[CarController] ⚠️ Cannot refresh bones - no skeletal mesh loaded");
            return;
        }
        ResolveBoneIndices(_skeletalMesh);
        InitializeWheelStates();
        Console.WriteLine($"[CarController] 🦴 Bone mapping refreshed");
    }

    /// <summary>
    /// Override a specific wheel's local position (called from TeaScript).
    /// Slot: 0=FrontLeft, 1=FrontRight, 2=RearLeft, 3=RearRight
    /// </summary>
    public void SetWheelLocalPosition(int slot, float x, float y, float z)
    {
        if (_wheelStates == null || slot < 0 || slot >= _wheelStates.Length) return;
        _wheelStates[slot].Config.LocalPosition = new BVec3(x, y, z);
        _wheelStates[slot].WorldPosition = new BVec3(x, y, z);
        Console.WriteLine($"[CarController] 🔧 Wheel {slot} position set to ({x:F2}, {y:F2}, {z:F2})");
    }

    /// <summary>
    /// Set which wheels are drive wheels (called from TeaScript).
    /// Pass 4 booleans: frontLeft, frontRight, rearLeft, rearRight
    /// </summary>
    public void SetDriveWheels(bool fl, bool fr, bool rl, bool rr)
    {
        if (_wheelStates == null) return;
        _wheelStates[0].Config.IsDriveWheel = fl;
        _wheelStates[1].Config.IsDriveWheel = fr;
        _wheelStates[2].Config.IsDriveWheel = rl;
        _wheelStates[3].Config.IsDriveWheel = rr;
        Console.WriteLine($"[CarController] 🔧 Drive wheels: FL={fl} FR={fr} RL={rl} RR={rr}");
    }

    /// <summary>
    /// Set which wheels steer (called from TeaScript).
    /// </summary>
    public void SetSteerWheels(bool fl, bool fr, bool rl, bool rr)
    {
        if (_wheelStates == null) return;
        _wheelStates[0].Config.IsSteerWheel = fl;
        _wheelStates[1].Config.IsSteerWheel = fr;
        _wheelStates[2].Config.IsSteerWheel = rl;
        _wheelStates[3].Config.IsSteerWheel = rr;
        Console.WriteLine($"[CarController] 🔧 Steer wheels: FL={fl} FR={fr} RL={rl} RR={rr}");
    }

    private void InitializeWheelStates()
    {
        _wheelStates = new WheelState[4];

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
            Console.WriteLine($"[CarController] 🦴 Using skeletal mesh bone positions for wheels");
        }
        else
        {
            // Fallback: hardcoded positions (no skeletal mesh or missing bones)
            wheelPositions[0] = new BVec3(-0.8f, -0.3f,  1.5f); // Front Left
            wheelPositions[1] = new BVec3( 0.8f, -0.3f,  1.5f); // Front Right
            wheelPositions[2] = new BVec3(-0.8f, -0.3f, -1.5f); // Rear Left
            wheelPositions[3] = new BVec3( 0.8f, -0.3f, -1.5f); // Rear Right

            Console.WriteLine($"[CarController] ⚠️ Using fallback wheel positions (no skeletal mesh)");
        }

        for (int i = 0; i < 4; i++)
        {
            _wheelStates[i] = new WheelState
            {
                Config = new WheelConfig
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
                },
                WorldPosition = wheelPositions[i]
            };
        }
    }

    private static BVec3[] NormalizeSkeletalWheelPositionsForPhysics(BVec3[] source)
    {
        if (source.Length < 4) return source;

        float centerX = (source[0].X + source[1].X + source[2].X + source[3].X) * 0.25f;
        float centerZ = (source[0].Z + source[1].Z + source[2].Z + source[3].Z) * 0.25f;

        float frontTrack = MathF.Abs(source[0].X - source[1].X);
        float rearTrack = MathF.Abs(source[2].X - source[3].X);
        float track = (frontTrack + rearTrack) * 0.5f;

        float leftWheelbase = MathF.Abs(source[0].Z - source[2].Z);
        float rightWheelbase = MathF.Abs(source[1].Z - source[3].Z);
        float wheelbase = (leftWheelbase + rightWheelbase) * 0.5f;

        const float targetTrack = 1.75f;
        const float targetWheelbase = 3.10f;
        const float maxStableTrack = 2.60f;
        const float maxStableWheelbase = 4.40f;
        const float minStableTrack = 1.00f;
        const float minStableWheelbase = 2.00f;

        float scaleX = track > 0.001f && (track > maxStableTrack || track < minStableTrack)
            ? targetTrack / track
            : 1.0f;

        float scaleZ = wheelbase > 0.001f && (wheelbase > maxStableWheelbase || wheelbase < minStableWheelbase)
            ? targetWheelbase / wheelbase
            : 1.0f;

        bool needsNormalization =
            MathF.Abs(scaleX - 1.0f) > 0.001f ||
            MathF.Abs(scaleZ - 1.0f) > 0.001f ||
            source.Any(p => p.Y < -0.60f || p.Y > -0.20f);

        if (!needsNormalization)
            return source;

        var normalized = new BVec3[4];
        for (int i = 0; i < 4; i++)
        {
            normalized[i] = new BVec3(
                (source[i].X - centerX) * scaleX,
                System.Math.Min(-0.25f, System.Math.Max(-0.55f, source[i].Y)),
                (source[i].Z - centerZ) * scaleZ);
        }

        float normalizedTrack = (MathF.Abs(normalized[0].X - normalized[1].X) + MathF.Abs(normalized[2].X - normalized[3].X)) * 0.5f;
        float normalizedWheelbase = (MathF.Abs(normalized[0].Z - normalized[2].Z) + MathF.Abs(normalized[1].Z - normalized[3].Z)) * 0.5f;

        Console.WriteLine(
            $"[CarController] 🛞 Normalized skeletal wheel rig for stable physics: " +
            $"track {track:F2}→{normalizedTrack:F2}, wheelbase {wheelbase:F2}→{normalizedWheelbase:F2}, " +
            $"scaleX={scaleX:F3}, scaleZ={scaleZ:F3}");

        for (int i = 0; i < normalized.Length; i++)
        {
            Console.WriteLine(
                $"[CarController]   wheel[{i}] physics pos " +
                $"({source[i].X:F3}, {source[i].Y:F3}, {source[i].Z:F3}) → " +
                $"({normalized[i].X:F3}, {normalized[i].Y:F3}, {normalized[i].Z:F3})");
        }

        return normalized;
    }

    public void OnPossessed(PlayerController controller)
    {
        _isPossessed = true;
        _controller = controller;
        Console.WriteLine("[CarController] Car possessed - WASD to drive, Space for handbrake, E to exit");
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

        Console.WriteLine("[CarController] Car unpossessed");

        BlueSky.Core.Scripting.TeaScriptSystem.CallFunctionOnAllScripts("onCarUnpossessed");
    }

    public void Update(float deltaTime)
    {
        if (!_isPossessed)
        {
            // If not possessed, update wheel spin based on vehicle speed
            if (_vehiclePhysics != null && _wheelStates != null)
            {
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

                UpdateWheelBoneTransforms(deltaTime);
            }
        }
    }

    public void ProcessInput(IInputContext input, float deltaTime)
    {
        if (!_isPossessed || input == null) return;

        // Mark chase camera as needing update this frame
        _chaseCamDirty = true;

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

        ApplyCarPhysics(deltaTime);

        // Update bone transforms if we have an animation controller
        UpdateWheelBoneTransforms(deltaTime);

        // Update chase camera exactly once with real deltaTime
        UpdateChaseCamera(deltaTime);

        float speedMPH = GetSpeedMPH();
        BlueSky.Core.Scripting.TeaScriptSystem.CallFunctionOnAllScripts("updateSpeed", (double)MathF.Round(speedMPH));
        BlueSky.Core.Scripting.TeaScriptSystem.CallFunctionOnAllScripts("updateRPM", (double)MathF.Round(_currentRPM));
        BlueSky.Core.Scripting.TeaScriptSystem.CallFunctionOnAllScripts("updateGear", _currentGear);
    }

    private void ApplyCarPhysics(float deltaTime)
    {
        if (_world == null || _vehiclePhysics == null) return;

        // Update wheel positions from physics body
        UpdateWheelPositions();

        var physicsPos = BlueSky.Physics.PhysicsTeaScriptBridge.GetPosition(_entity);
        var physicsRot = BlueSky.Physics.PhysicsTeaScriptBridge.GetRotation(_entity);

        // Calculate engine RPM from wheel speed
        UpdateTransmission(deltaTime);

        // Calculate throttle with torque curve based on RPM
        float torqueFactor = CalculateTorqueCurve(_currentRPM);
        float effectiveThrottle = _motorInput * torqueFactor;

        _vehiclePhysics.Solve(deltaTime, effectiveThrottle, _brakeInput ? 1.0f : 0.0f, _steerInput,
                             _entity, physicsPos.ToBlue(), physicsRot.ToBlue());

        if (_rigidbody.HasValue && _transform.HasValue)
        {
            physicsPos = BlueSky.Physics.PhysicsTeaScriptBridge.GetPosition(_entity);
            physicsRot = BlueSky.Physics.PhysicsTeaScriptBridge.GetRotation(_entity);
            var t = _transform.Value;
            t.Position = physicsPos.ToBlue();
            t.Rotation = physicsRot.ToBlue();
            _transform = t;
        }
    }

    /// <summary>
    /// Drive the skeletal mesh wheel bone transforms based on current wheel state.
    /// Front wheel bones get steer rotation, all wheel bones get spin rotation.
    /// </summary>
    private void UpdateWheelBoneTransforms(float deltaTime)
    {
        if (_animController == null || _wheelStates == null) 
        {
            return;
        }
        if (_boneIndices.Length < 4) 
        {
            return;
        }

        // Debug: only log once per few seconds to avoid spam
        if (_isPossessed && _frameCounter++ % 60 == 0)
        {
            Console.WriteLine($"[CarController] 🎡 Wheel States:");
            Console.WriteLine($"  FL[0]: angVel={_wheelStates[0].AngularVelocity:F2}, spinAngle={_wheelStates[0].SpinAngle:F2}, steer={_wheelStates[0].SteerAngle:F1}°");
            Console.WriteLine($"  FR[1]: angVel={_wheelStates[1].AngularVelocity:F2}, spinAngle={_wheelStates[1].SpinAngle:F2}, steer={_wheelStates[1].SteerAngle:F1}°");
            Console.WriteLine($"  RL[2]: angVel={_wheelStates[2].AngularVelocity:F2}, spinAngle={_wheelStates[2].SpinAngle:F2}");
            Console.WriteLine($"  RR[3]: angVel={_wheelStates[3].AngularVelocity:F2}, spinAngle={_wheelStates[3].SpinAngle:F2}");
        }

        // Wheel bone order: 0=RightFront, 1=LeftFront, 2=LeftRear, 3=RightRear
        // Wheel state order:  0=FrontLeft,  1=FrontRight, 2=RearLeft,  3=RearRight
        int[] stateForBone = { 1, 0, 2, 3 }; // Maps bone index to wheel state index

        int bonesUpdated = 0;
        for (int boneSlot = 0; boneSlot < 4; boneSlot++)
        {
            int boneIdx = _boneIndices[boneSlot];
            if (boneIdx < 0) 
            {
                Console.WriteLine($"[CarController] ⚠️ Bone slot {boneSlot} has invalid index {boneIdx}");
                continue;
            }

            int stateIdx = stateForBone[boneSlot];
            if (stateIdx >= _wheelStates.Length) continue;

            WheelState wheel = _wheelStates[stateIdx];

            // ALL WHEELS: Roll around X-axis (red, pitch) for acceleration/spinning
            Quaternion spinRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, wheel.SpinAngle);
            
            // FRONT WHEELS ONLY: Steer around Z-axis (blue, roll) for left/right turning
            Quaternion steerRotation = Quaternion.Identity;
            if (boneSlot < 2) // Only front wheels (slots 0 and 1)
            {
                steerRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ,
                    wheel.SteerAngle * (MathF.PI / 180f)); // Steer around blue Z-axis (roll)
            }

            // Apply rotations: Steer first, then spin
            Quaternion localRotation = steerRotation * spinRotation;

            // Build local transform: bind pose translation + gameplay rotation
            BVec3 bindPos = _boneWheelPositions[boneSlot];
            Matrix4x4 localTransform = Matrix4x4.CreateFromQuaternion(
                new System.Numerics.Quaternion(localRotation.X, localRotation.Y, localRotation.Z, localRotation.W))
                * Matrix4x4.CreateTranslation(
                    new Vector3(bindPos.X, bindPos.Y, bindPos.Z));

            _animController.SetBoneLocalTransform(boneIdx, localTransform);
            bonesUpdated++;
        }

        if (_isPossessed && _frameCounter % 60 == 0)
        {
            Console.WriteLine($"[CarController] 🦴 Updated {bonesUpdated}/4 wheel bones");
        }

        // Force recalculation of world bone transforms after updating local ones
        _animController.ComputeWorldTransforms();
    }

    private void UpdateTransmission(float deltaTime)
    {
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
        if (rpm < IdleRPM * 1.5f && _currentGear > 1 && _motorInput > 0)
        {
            _currentGear--;
            rpm = rpm * GearRatios[_currentGear - 1] / GearRatios[_currentGear];
        }

        // Coasting: if no throttle and low speed, downshift
        if (_motorInput < 0.1f && rpm < IdleRPM * 1.2f && _currentGear > 1)
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

        var carPos = BlueSky.Physics.PhysicsTeaScriptBridge.GetPosition(_entity);
        var carRot = BlueSky.Physics.PhysicsTeaScriptBridge.GetRotation(_entity);

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

        var physicsPos = BlueSky.Physics.PhysicsTeaScriptBridge.GetPosition(_entity);
        var physicsRot = BlueSky.Physics.PhysicsTeaScriptBridge.GetRotation(_entity);
        var velocity = BlueSky.Physics.PhysicsTeaScriptBridge.GetVelocity(_entity);

        var carPos = new Vector3(physicsPos.X, physicsPos.Y, physicsPos.Z);

        _chaseCamera.Update(deltaTime, carPos, physicsRot, velocity,
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
        var physicsRot = BlueSky.Physics.PhysicsTeaScriptBridge.GetRotation(_entity);
        var rotationMatrix = Matrix4x4.CreateFromQuaternion(physicsRot);
        return Vector3.Transform(Vector3.UnitZ, rotationMatrix);
    }

    private Vector3 GetRightVector()
    {
        var physicsRot = BlueSky.Physics.PhysicsTeaScriptBridge.GetRotation(_entity);
        var rotationMatrix = Matrix4x4.CreateFromQuaternion(physicsRot);
        return Vector3.Transform(Vector3.UnitX, rotationMatrix);
    }

    private Vector3 GetVelocity()
    {
        if (_world != null)
        {
            return BlueSky.Physics.PhysicsTeaScriptBridge.GetVelocity(_entity);
        }
        return Vector3.Zero;
    }

    private void ApplyForce(Vector3 force)
    {
        if (force.LengthSquared() > 0.01f && _world != null)
        {
            BlueSky.Physics.PhysicsTeaScriptBridge.AddImpulse(_entity, force);
        }
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * MathF.Min(t, 1.0f);
    }

    public float GetSpeed()
    {
        return GetVelocity().Length();
    }

    public float GetSpeedMPH()
    {
        return GetSpeed() * 2.237f;
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
}
