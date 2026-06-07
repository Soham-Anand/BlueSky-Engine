using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Platform;
using BlueSky.Platform.Input;
using BlueSky.Rendering;
using BlueSky.Animation;
using BVec3 = BlueSky.Core.Math.Vector3;

namespace BlueSky.Core.Gameplay;

public class CarControllerSystem
{
    private static Dictionary<uint, CarController> s_allControllers = new();

    public static CarController? GetController(uint entityId)
    {
        s_allControllers.TryGetValue(entityId, out var controller);
        return controller;
    }

    private World? _world;
    private IInputContext? _input;
    private Viewport? _viewport;
    private PlayerController? _playerController;
    private WheelVisualSystem _wheelVisualSystem;

    private Dictionary<uint, CarController> _runtimeControllers = new();

    /// <summary>Loaded skeletal meshes keyed by entity ID</summary>
    private Dictionary<uint, SkeletalMesh> _loadedMeshes = new();

    /// <summary>Animation controllers keyed by entity ID</summary>
    private Dictionary<uint, AnimationController> _animControllers = new();

    public void Initialize(World world, IInputContext input, Viewport viewport)
    {
        _world = world;
        _input = input;
        _viewport = viewport;
        _playerController = PlayerController.Instance;
        _wheelVisualSystem = new WheelVisualSystem();
        _playerController.Initialize(input, viewport);

        Console.WriteLine("[CarControllerSystem] ✅ Initialized - Cars will auto-possess when added");
    }

    public void Update(float deltaTime)
    {
        if (_world == null || _input == null) return;

        _playerController?.Update(deltaTime);

        InitializeCarControllers();

        foreach (var controller in _runtimeControllers.Values)
        {
            controller.Update(deltaTime);
        }

        UpdateWheelVisuals();
    }

    private void UpdateWheelVisuals()
    {
        if (_world == null) return;

        foreach (var kvp in _runtimeControllers)
        {
            Entity carEntity = new Entity((int)kvp.Key, 1);
            if (!_world.IsEntityValid(carEntity)) continue;

            CarController controller = kvp.Value;
            if (controller._wheelStates != null)
            {
                _wheelVisualSystem.Update(_world, carEntity, controller._wheelStates);
            }
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
                    Console.WriteLine($"[CarControllerSystem] 🔧 Initializing runtime controller for Entity_{entity.Id}...");

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
                        WheelRadius = carComp.WheelRadius
                    };

                    // ── Load skeletal mesh if the entity has a SkeletalMeshComponent ──
                    SkeletalMesh? skeletalMesh = null;
                    AnimationController? animController = null;

                    if (_world.TryGetComponent<SkeletalMeshComponent>(entity, out var skelComp) && !string.IsNullOrEmpty(skelComp.MeshAssetPath))
                    {
                        Console.WriteLine($"[CarControllerSystem] 🦴 Entity_{entity.Id} has SkeletalMeshComponent: {skelComp.MeshAssetPath}");
                        skeletalMesh = LoadAndValidateSkeletalMesh(entity, skelComp.MeshAssetPath);

                        if (skeletalMesh != null)
                        {
                            // Create an animation controller for bone transform driving
                            animController = new AnimationController(skeletalMesh);
                            _animControllers[(uint)entity.Id] = animController;

                            // Mark the component as loaded
                            skelComp.IsLoaded = true;
                            _world.AddComponent(entity, skelComp);
                            
                            Console.WriteLine($"[CarControllerSystem] ✅ AnimationController created for Entity_{entity.Id} with {skeletalMesh.Bones.Length} bones");
                        }
                        else
                        {
                            Console.WriteLine($"[CarControllerSystem] ❌ Failed to load skeletal mesh for Entity_{entity.Id}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[CarControllerSystem] ⚠️ Entity_{entity.Id} has NO SkeletalMeshComponent!");
                        Console.WriteLine($"[CarControllerSystem] 💡 WHEEL ANIMATION DISABLED - Add a SkeletalMeshComponent to enable bone-driven wheel animation!");
                        Console.WriteLine($"[CarControllerSystem] 💡 To fix: Right-click entity → Add Component → Skeletal Mesh → Select your car's .glb file");
                    }

                    controller.Initialize(entity, _world, skeletalMesh, animController);
                    
                    // Write comprehensive diagnostic to file
                    string diagFile = "/tmp/bluesky_car_init.txt";
                    var diag = new System.Text.StringBuilder();
                    diag.AppendLine($"\n═══ CAR INITIALIZATION DIAGNOSTIC ═══");
                    diag.AppendLine($"Entity ID: {entity.Id}");
                    diag.AppendLine($"Has SkeletalMeshComponent: {_world.HasComponent<SkeletalMeshComponent>(entity)}");
                    diag.AppendLine($"SkeletalMesh loaded: {skeletalMesh != null}");
                    diag.AppendLine($"AnimationController created: {animController != null}");
                    
                    if (skeletalMesh != null)
                    {
                        diag.AppendLine($"Bone count: {skeletalMesh.Bones.Length}");
                        diag.AppendLine($"Vertices: {skeletalMesh.Vertices?.Length ?? 0}");
                        diag.AppendLine($"Materials: {skeletalMesh.Materials?.Length ?? 0}");
                    }
                    
                    if (animController != null)
                    {
                        diag.AppendLine($"✅ ANIMATION CONTROLLER ACTIVE - Wheels SHOULD animate!");
                    }
                    else
                    {
                        diag.AppendLine($"❌ NO ANIMATION CONTROLLER - Wheels will NOT animate!");
                        diag.AppendLine($"Fix: Add SkeletalMeshComponent to entity pointing to .glb file");
                    }
                    
                    System.IO.File.AppendAllText(diagFile, diag.ToString());
                    Console.WriteLine($"[CarControllerSystem] 🎯 Entity_{entity.Id}: SkeletalMesh={skeletalMesh != null}, AnimController={animController != null}");
                    Console.WriteLine($"[CarControllerSystem] 📄 Full diagnostic written to {diagFile}");
                    _runtimeControllers[(uint)entity.Id] = controller;
                    s_allControllers[(uint)entity.Id] = controller;

                    carComp.IsInitialized = true;
                    carComp.EntityId = (uint)entity.Id;
                    _world.AddComponent(entity, carComp);

                    Console.WriteLine($"[CarControllerSystem] ✅ Runtime controller initialized for Entity_{entity.Id}");
                    Console.WriteLine($"[CarControllerSystem] 🎮 Total cars available: {_runtimeControllers.Count}");

                    Console.WriteLine($"[CarControllerSystem] 📢 Auto-advertising car for possession...");
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
        Console.WriteLine($"[CarControllerSystem] 🦴 Loading skeletal mesh: {assetPath}");
        if (!string.Equals(importPath, assetPath, StringComparison.Ordinal))
        {
            Console.WriteLine($"[CarControllerSystem] 🦴 Resolved imported asset to source file: {importPath}");
        }

        try
        {
            var (isSkeletal, meshObj) = SkeletalMeshImporter.ImportMesh(importPath);

            if (!isSkeletal || meshObj is not SkeletalMesh skeletalMesh)
            {
                Console.WriteLine($"[CarControllerSystem] ❌ Entity_{entityId}: '{importPath}' is NOT a skeletal mesh! " +
                    "Car controller requires a skeletal mesh with wheel bones.");
                return null;
            }

            // Validate required bones
            Console.WriteLine($"[CarControllerSystem] 🦴 Skeletal mesh has {skeletalMesh.Bones.Length} bones. Validating required vehicle bones...");
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
                    Console.WriteLine($"[CarControllerSystem]   ✅ '{requiredBone}' found{aliasText} (index {boneIdx})");
                }
                else
                {
                    Console.WriteLine($"[CarControllerSystem]   ❌ '{requiredBone}' MISSING!");
                    LogSimilarBoneNames(skeletalMesh, requiredBone);
                    allBonesPresent = false;
                }
            }

            if (!allBonesPresent)
            {
                Console.WriteLine($"[CarControllerSystem] ⚠️ Entity_{entityId}: Skeletal mesh is MISSING required bones! " +
                    "Car will use fallback wheel positions. Required bones: " +
                    string.Join(", ", CarController.DefaultBoneNames));
                // Still return the mesh - CarController will fall back to hardcoded positions for missing bones
            }
            else
            {
                Console.WriteLine($"[CarControllerSystem] ✅ All required vehicle bones present in '{assetPath}'");
            }

            _loadedMeshes[entityId] = skeletalMesh;
            return skeletalMesh;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CarControllerSystem] ❌ Failed to load skeletal mesh '{assetPath}': {ex.Message}");
            return null;
        }
    }

    private static void DumpSkeletalMeshBones(SkeletalMesh mesh)
    {
        if (mesh.Bones == null || mesh.Bones.Length == 0)
        {
            Console.WriteLine("[CarControllerSystem] 🦴 Imported bone list: <none>");
            return;
        }

        Console.WriteLine("[CarControllerSystem] 🦴 Imported bone list:");
        for (int i = 0; i < mesh.Bones.Length; i++)
        {
            var bone = mesh.Bones[i];
            var local = bone.LocalBindPose;
            var inverse = bone.InverseBindPose;
            string children = bone.Children.Count > 0 ? string.Join(",", bone.Children) : "-";

            Console.WriteLine(
                $"[CarControllerSystem]   [{i:00}] '{bone.Name}' parent={bone.ParentIndex} children={children} " +
                $"localT=({local.M41:F3}, {local.M42:F3}, {local.M43:F3}) " +
                $"inverseT=({inverse.M41:F3}, {inverse.M42:F3}, {inverse.M43:F3})");
        }

        var duplicateNames = mesh.Bones
            .GroupBy(b => b.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicateNames.Length > 0)
        {
            Console.WriteLine($"[CarControllerSystem] ⚠️ Duplicate bone names detected: {string.Join(", ", duplicateNames)}");
        }
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

        if (candidates.Length > 0)
        {
            Console.WriteLine($"[CarControllerSystem]      closest imported name(s): {string.Join(", ", candidates.Select(n => $"'{n}'"))}");
        }
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
            Console.WriteLine($"[CarControllerSystem] ⚠️ Could not read skeletal asset header: {assetPath}");
            return assetPath;
        }

        if (asset.Type != BlueSky.Core.Assets.AssetType.SkeletalMesh)
        {
            Console.WriteLine($"[CarControllerSystem] ⚠️ Asset '{assetPath}' is {asset.Type}, not SkeletalMesh.");
        }

        if (!string.IsNullOrEmpty(asset.SourceFile) && File.Exists(asset.SourceFile))
            return asset.SourceFile;

        Console.WriteLine($"[CarControllerSystem] ⚠️ Skeletal .blueskyasset has no available source file; runtime importer only supports source mesh formats right now.");
        return assetPath;
    }

    public void AddCarController(Entity entity)
    {
        if (_world == null)
        {
            Console.WriteLine("[CarControllerSystem] ❌ ERROR: Cannot add car controller - World is null!");
            return;
        }

        bool hasTransform = _world.TryGetComponent<TransformComponent>(entity, out _);
        bool hasRigidbody = _world.TryGetComponent<RigidbodyComponent>(entity, out _);
        bool hasCollider = _world.TryGetComponent<ColliderComponent>(entity, out _);
        bool hasSkeletalMesh = _world.TryGetComponent<SkeletalMeshComponent>(entity, out var skeletalMesh);
        bool hasStaticMesh = _world.TryGetComponent<StaticMeshComponent>(entity, out var staticMesh);

        if (!hasTransform)
        {
            Console.WriteLine($"[CarControllerSystem] ⚠️ WARNING: Entity_{entity.Id} has no Transform component!");
        }
        if (!hasRigidbody)
        {
            Console.WriteLine($"[CarControllerSystem] ⚠️ WARNING: Entity_{entity.Id} has no Rigidbody component! Add one for physics.");
        }
        if (!hasCollider)
        {
            Console.WriteLine($"[CarControllerSystem] ⚠️ WARNING: Entity_{entity.Id} has no Collider component! Add one for physics.");
        }
        if (!hasSkeletalMesh)
        {
            if (hasStaticMesh)
            {
                Console.WriteLine($"[CarControllerSystem] ⚠️ WARNING: Entity_{entity.Id} has StaticMeshComponent ('{staticMesh.MeshAssetId}') but no SkeletalMeshComponent. " +
                    "Wheel bones cannot be driven from a static mesh; car will use hardcoded fallback positions.");
            }
            else
            {
                Console.WriteLine($"[CarControllerSystem] ⚠️ WARNING: Entity_{entity.Id} has no SkeletalMesh component! " +
                    "Add one with a mesh containing these bones: " +
                    string.Join(", ", CarController.DefaultBoneNames) +
                    ". Car will use hardcoded fallback positions. " +
                    "Or use setWheelBone() in TeaScript to configure custom bone names.");
            }
        }
        else if (string.IsNullOrEmpty(skeletalMesh.MeshAssetPath))
        {
            Console.WriteLine($"[CarControllerSystem] ⚠️ WARNING: Entity_{entity.Id} has SkeletalMeshComponent but MeshAssetPath is empty. " +
                "Car will use hardcoded fallback positions until a skeletal mesh asset is assigned.");
        }

        var carComponent = CarControllerComponent.CreateDefault();
        _world.AddComponent(entity, carComponent);

        Console.WriteLine($"[CarControllerSystem] ✅ Car controller component added to Entity_{entity.Id}");
        Console.WriteLine($"[CarControllerSystem] 💡 The car will auto-initialize and auto-possess on next frame!");
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

        // Destroy fallback wheel entities
        if (_world != null)
        {
            foreach (var kvp in _runtimeControllers)
            {
                Entity entity = new Entity((int)kvp.Key, 1);
                _wheelVisualSystem.RemoveWheelEntities(entity, _world);
            }
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
        _animControllers.Clear();
        _wheelVisualSystem.Cleanup();

        // Clear bone overrides
        CarController.ClearAllBoneOverrides();

        Console.WriteLine("[CarControllerSystem] 🧹 Full cleanup completed");
    }
}
