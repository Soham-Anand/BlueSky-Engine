using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Platform;
using BlueSky.Platform.Input;
using BlueSky.Rendering;
using BlueSky.Motif;
using BVec3 = BlueSky.Core.Math.Vector3;

namespace BlueSky.Core.Gameplay;

public class CarControllerSystem
{
    private static Dictionary<uint, CarController> s_allControllers = new();
    private static CarController? _possessedController;

    public static CarController? GetController(uint entityId)
    {
        s_allControllers.TryGetValue(entityId, out var controller);
        return controller;
    }

    /// <summary>
    /// The client's possessed car controller (set by GameRuntime when car is possessed).
    /// Bridge functions fall back to this when the requesting entity has no valid data.
    /// </summary>
    public static CarController? PossessedController
    {
        get => _possessedController;
        set => _possessedController = value;
    }

    private World? _world;
    private IInputContext? _input;
    private Viewport? _viewport;
    private PlayerController? _playerController;

    private Dictionary<uint, CarController> _runtimeControllers = new();

    /// <summary>Loaded skeletal meshes keyed by entity ID</summary>
    private Dictionary<uint, SkeletalMesh> _loadedMeshes = new();

    public void Initialize(World world, IInputContext input, Viewport? viewport)
    {
        _world = world;
        _input = input;
        _viewport = viewport;
        _playerController = PlayerController.Instance;
        _playerController.Initialize(input, viewport);
    }

    public void Update(float deltaTime)
    {
        if (_world == null || _input == null) return;

        InitializeCarControllers();

        foreach (var controller in _runtimeControllers.Values)
        {
            controller.Update(deltaTime);
        }
    }

    /// <summary>
    /// Samples possession and player input. This must run before the fixed
    /// physics loop; visual animation is intentionally kept in Update so it
    /// runs after the newest physics state has been produced.
    /// </summary>
    public void ProcessInput(float deltaTime)
    {
        if (_world == null || _input == null) return;

        InitializeCarControllers();
        _playerController?.Update(deltaTime);
    }

    /// <summary>
    /// Ensures newly spawned car entities have controllers before an input
    /// sample or fixed step tries to access them.
    /// </summary>
    public void EnsureInitialized()
    {
        InitializeCarControllers();
    }

    public void FixedUpdate(float fixedDeltaTime)
    {
        if (_world == null) return;

        InitializeCarControllers();

        foreach (var controller in _runtimeControllers.Values)
        {
            controller.FixedUpdate(fixedDeltaTime);
        }
    }

    private void InitializeCarControllers()
    {
        if (_world == null) return;

        var entities = _world.GetAllEntities().ToList();

        foreach (var entity in entities)
        {
            if (_world.TryGetComponent<CarControllerComponent>(entity, out var carComp))
            {
                if (!carComp.IsInitialized && !_runtimeControllers.ContainsKey((uint)entity.Id))
                {

                    var controller = new CarController
                    {
                        MotorForce = carComp.MotorForce,
                        BrakeForce = carComp.BrakeForce,
                        MaxSteerAngle = carComp.MaxSteerAngle,
                        DownForce = carComp.DownForce,
                        CenterOfMassOffset = new System.Numerics.Vector3(carComp.CenterOfMassOffset.X, carComp.CenterOfMassOffset.Y, carComp.CenterOfMassOffset.Z),
                        SuspensionRestLength = carComp.SuspensionRestLength,
                        SuspensionStiffness = carComp.SuspensionStiffness,
                        SuspensionDamping = carComp.SuspensionDamping,
                        WheelRadius = carComp.WheelRadius,
                        // Keep the chassis on the stable rigid-body vehicle path.
                        // The internal wheel-body solver is experimental and
                        // makes the chassis kinematic; wheel visuals remain
                        // independent through CarController's wheel states.
                        UsePerWheelPhysics = false
                    };

                    // ── Load skeletal mesh if the entity has a SkeletalMeshComponent ──
                    SkeletalMesh? skeletalMesh = null;

                    if (_world.TryGetComponent<SkeletalMeshComponent>(entity, out var skelComp) && !string.IsNullOrEmpty(skelComp.MeshAssetPath))
                    {
                        skeletalMesh = LoadAndValidateSkeletalMesh(entity, skelComp.MeshAssetPath);

                        if (skeletalMesh != null)
                        {
                            // Mark the component as loaded
                            skelComp.IsLoaded = true;
                            _world.AddComponent(entity, skelComp);
                        }
                    }

                    controller.Initialize(entity, _world, skeletalMesh);
                    
                    string diagFile = "/tmp/bluesky_car_init.txt";
                    var diag = new System.Text.StringBuilder();
                    diag.AppendLine($"\n═══ CAR INITIALIZATION DIAGNOSTIC ═══");
                    diag.AppendLine($"Entity ID: {entity.Id}");
                    diag.AppendLine($"Has SkeletalMeshComponent: {_world.HasComponent<SkeletalMeshComponent>(entity)}");
                    diag.AppendLine($"SkeletalMesh loaded: {skeletalMesh != null}");
                    
                    if (skeletalMesh != null)
                    {
                        diag.AppendLine($"Bone count: {skeletalMesh.Bones.Length}");
                        diag.AppendLine($"Vertices: {skeletalMesh.Vertices?.Length ?? 0}");
                    }
                    
                    diag.AppendLine($"Bone animation driven by TeaScript (BoneTransformOverrides)");
                    
                    System.IO.File.AppendAllText(diagFile, diag.ToString());
                    _runtimeControllers[(uint)entity.Id] = controller;
                    s_allControllers[(uint)entity.Id] = controller;

                    carComp.IsInitialized = true;
                    carComp.EntityId = (uint)entity.Id;
                    _world.AddComponent(entity, carComp);

                    controller.AdvertisePossession("Player1");
                }
            }
        }
    }

    /// <summary>
    /// Load a skeletal mesh from disk and validate that all required vehicle bones are present.
    /// Returns null if the mesh cannot be loaded or is missing required bones.
    /// </summary>
    private SkeletalMesh? LoadAndValidateSkeletalMesh(Entity entity, string assetPath)
    {
        uint entityId = (uint)entity.Id;

        // Return cached mesh if already loaded
        if (_loadedMeshes.TryGetValue(entityId, out var cached))
            return cached;

        string importPath = ResolveSkeletalImportPath(assetPath);

        try
        {
            var (isSkeletal, meshObj) = SkeletalMeshImporter.ImportMesh(importPath);

            if (!isSkeletal || meshObj is not SkeletalMesh skeletalMesh)
            {
                return null;
            }

            // Validate required bones
            DumpSkeletalMeshBones(skeletalMesh);

            // Check which default bones are present (or if TeaScript overrides exist)
            var boneNamesToCheck = CarController.GetBoneOverrides(entityId) ?? CarController.DefaultBoneNames;
            bool allBonesPresent = true;
            foreach (string requiredBone in boneNamesToCheck)
            {
                if (CarController.TryResolveBoneName(skeletalMesh, requiredBone, out int boneIdx, out string resolvedBone))
                {
                    string aliasText = string.Equals(requiredBone, resolvedBone, StringComparison.Ordinal)
                        ? ""
                        : $" via alias '{resolvedBone}'";
                }
                else
                {
                    LogSimilarBoneNames(skeletalMesh, requiredBone);
                    allBonesPresent = false;
                }
            }

            if (!allBonesPresent)
            {
                // Still return the mesh - CarController will fall back to hardcoded positions for missing bones
            }

            _loadedMeshes[entityId] = skeletalMesh;
            return skeletalMesh;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CarControllerSystem] Skeletal mesh import failed for '{importPath}': {ex.Message}");
            return null;
        }
    }

    private static void DumpSkeletalMeshBones(SkeletalMesh mesh)
    {
        if (mesh.Bones == null || mesh.Bones.Length == 0)
        {
            return;
        }

        for (int i = 0; i < mesh.Bones.Length; i++)
        {
            var bone = mesh.Bones[i];
            var local = bone.LocalBindPose;
            var inverse = bone.InverseBindPose;
            string children = bone.Children.Count > 0 ? string.Join(",", bone.Children) : "-";

        }

        var duplicateNames = mesh.Bones
            .GroupBy(b => b.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

    }

    private static void LogSimilarBoneNames(SkeletalMesh mesh, string requiredBone)
    {
        string requiredKey = NormalizeBoneName(requiredBone);
        var candidates = mesh.Bones
            .Select(b => b.Name)
            .Where(name =>
            {
                string candidateKey = NormalizeBoneName(name);
                return candidateKey.Contains(requiredKey, StringComparison.OrdinalIgnoreCase) ||
                       requiredKey.Contains(candidateKey, StringComparison.OrdinalIgnoreCase) ||
                       LooksLikeSameVehicleSlot(requiredKey, candidateKey);
            })
            .Distinct()
            .Take(4)
            .ToArray();

    }

    private static bool LooksLikeSameVehicleSlot(string requiredKey, string candidateKey)
    {
        return requiredKey switch
        {
            "frmesh" => ContainsAll(candidateKey, "front", "right") || candidateKey.Contains("fr", StringComparison.OrdinalIgnoreCase),
            "flmesh" => ContainsAll(candidateKey, "front", "left") || candidateKey.Contains("fl", StringComparison.OrdinalIgnoreCase),
            "rlmesh" => ContainsAll(candidateKey, "rear", "left") || candidateKey.Contains("rl", StringComparison.OrdinalIgnoreCase),
            "rrmesh" => ContainsAll(candidateKey, "rear", "right") || candidateKey.Contains("rr", StringComparison.OrdinalIgnoreCase),
            "main" => candidateKey.Contains("root", StringComparison.OrdinalIgnoreCase) ||
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

    private static string ResolveSkeletalImportPath(string assetPath)
    {
        if (!string.Equals(Path.GetExtension(assetPath), ".blueskyasset", StringComparison.OrdinalIgnoreCase))
            return assetPath;

        var asset = BlueSky.Core.Assets.BlueAsset.LoadHeader(assetPath);
        if (asset == null)
        {
            return assetPath;
        }

        if (!string.IsNullOrEmpty(asset.SourceFile) && File.Exists(asset.SourceFile))
            return asset.SourceFile;

        return assetPath;
    }

    public void AddCarController(Entity entity)
    {
        if (_world == null)
        {
            return;
        }

        bool hasTransform = _world.TryGetComponent<TransformComponent>(entity, out _);
        bool hasPhysics = _world.TryGetComponent<PhysicsComponent>(entity, out _);
        bool hasSkeletalMesh = _world.TryGetComponent<SkeletalMeshComponent>(entity, out var skeletalMesh);
        bool hasStaticMesh = _world.TryGetComponent<StaticMeshComponent>(entity, out var staticMesh);

        var carComponent = CarControllerComponent.CreateDefault();
        _world.AddComponent(entity, carComponent);

    }

    public CarController? GetPossessedCar()
    {
        return _playerController?.PossessedEntity as CarController;
    }

    /// <summary>
    /// Full cleanup when play mode stops. Clears all runtime state and bone overrides.
    /// </summary>
    public void Cleanup()
    {
        // Unpossess any possessed car first
        var playerCtrl = PlayerController.Instance;
        if (playerCtrl.PossessedEntity != null)
        {
            playerCtrl.Unpossess();
        }

        // Reset IsInitialized on all CarControllerComponents so they re-initialize on next play
        if (_world != null)
        {
            foreach (var entity in _world.GetAllEntities())
            {
                if (_world.TryGetComponent<CarControllerComponent>(entity, out var carComp) && carComp.IsInitialized)
                {
                    carComp.IsInitialized = false;
                    carComp.EntityId = 0;
                    _world.AddComponent(entity, carComp);
                }
            }
        }

        // Clear all runtime state
        _runtimeControllers.Clear();
        s_allControllers.Clear();
        _loadedMeshes.Clear();

        // Clear bone overrides
        CarController.ClearAllBoneOverrides();
    }
}
