using System;
using System.Collections.Generic;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Assets;
using BlueSky.Runtime.UI;
using BlueSky.Core.Gameplay;
using BlueSky.Physics;
using TeaScript.Bridge;

namespace BlueSky.Core.Scripting;

/// <summary>
/// ECS System that manages TeaScript execution for all entities with TeaScriptComponent.
/// </summary>
public class TeaScriptSystem : SystemBase
{
    private readonly Dictionary<uint, TeaScriptEngine> _runtimeInstances = new();
    private float _deltaTime = 0.016f;
    private Func<string, bool>? _keyProvider;
    private Func<int, bool>? _mouseButtonProvider;

    private static TeaScriptSystem? _instance;
    public static TeaScriptSystem? Instance => _instance;

    public TeaScriptSystem(World world)
    {
        _instance = this;
        Initialize(world);
    }

    public static void CallFunctionOnAllScripts(string functionName, params object?[] args)
    {
        if (_instance == null) return;
        
        foreach (var engine in _instance._runtimeInstances.Values)
        {
            try
            {
                engine.CallFunction(functionName, args);
            }
            catch (Exception)
            {
                // Ignore scripts that do not have this function defined
            }
        }
    }

    public void SetInputProviders(Func<string, bool>? keyProvider, Func<int, bool>? mouseButtonProvider = null)
    {
        _keyProvider = keyProvider;
        _mouseButtonProvider = mouseButtonProvider;
    }
    
    /// <summary>
    /// Update all TeaScript components.
    /// </summary>
    public override void Update(float deltaTime)
    {
        _deltaTime = deltaTime;
        
        if (World == null) return;
        
        // Query for entities with both TeaScriptComponent and TransformComponent
        var query = World.CreateQuery()
            .All<TeaScriptComponent>()
            .All<TransformComponent>()
            .Build();
        
        var chunks = World.GetQueryChunks(query);
        
        int scriptCount = 0;
        foreach (var chunk in chunks)
        {
            int scriptIndex = chunk.GetComponentIndex(typeof(TeaScriptComponent));
            int transformIndex = chunk.GetComponentIndex(typeof(TransformComponent));
            var entities = chunk.GetEntities();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                ref var script = ref chunk.GetComponent<TeaScriptComponent>(i, scriptIndex);
                ref var transform = ref chunk.GetComponent<TransformComponent>(i, transformIndex);
                
                if (!script.IsEnabled) continue;
                
                scriptCount++;
                
                // Initialize script if needed
                if (!script.IsInitialized && !string.IsNullOrEmpty(script.ScriptAssetId))
                {
                    InitializeScript(ref script, entity);
                }
                
                // Call update()
                if (script.IsInitialized && script.RuntimeInstance != 0)
                {
                    if (_runtimeInstances.TryGetValue(script.RuntimeInstance, out var engine))
                    {
                        try
                        {
                            engine.CallUpdate();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[TeaScript] Error in update(): {ex.Message}");
                        }
                    }
                }
            }
        }
        
        // Debug: Log script count on first frame
        if (scriptCount > 0 && _deltaTime < 0.1f)
        {
            Console.WriteLine($"[TeaScript] Updating {scriptCount} script(s)");
        }
    }
    
    /// <summary>
    /// Initialize a script instance.
    /// </summary>
    private void InitializeScript(ref TeaScriptComponent script, Entity entity)
    {
        try
        {
            var scriptPath = ResolveScriptPath(script.ScriptAssetId);
            if (string.IsNullOrEmpty(scriptPath))
            {
                Console.WriteLine($"[TeaScript] No script file specified for entity {entity.Id}");
                script.IsEnabled = false;
                return;
            }

            if (!System.IO.File.Exists(scriptPath))
            {
                Console.WriteLine($"[TeaScript] Script not found for entity {entity.Id}: {script.ScriptAssetId}");
                script.IsEnabled = false;
                return;
            }

            var engine = new TeaScriptEngine();
            
            // Register basic engine functions
            RegisterEngineFunctions(engine, entity);
            
            // Load the actual script file
            Console.WriteLine($"[TeaScript] Loading script: {scriptPath}");
            engine.LoadScript(scriptPath);
            
            // Store instance
            uint instanceId = (uint)_runtimeInstances.Count + 1;
            _runtimeInstances[instanceId] = engine;
            script.RuntimeInstance = instanceId;
            
            // Call start()
            engine.CallStart();
            
            script.IsInitialized = true;
            Console.WriteLine($"[TeaScript] Initialized script for entity {entity.Id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TeaScript] Failed to initialize script: {ex.Message}");
            Console.WriteLine($"[TeaScript] Stack trace: {ex.StackTrace}");
            script.IsEnabled = false;
        }
    }
    
    /// <summary>
    /// Register all engine functions that scripts can call.
    /// </summary>
    private void RegisterEngineFunctions(TeaScriptEngine engine, Entity entity)
    {
        if (World == null) return;
        
        // Logging
        engine.RegisterFunction("log", (args) =>
        {
            string message = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            Console.WriteLine($"[TeaScript:{entity.Id}] {message}");
            return null;
        });

        // Runtime UI - frame-local HUD drawing. These are rendered by the
        // runtime UI overlay during play mode, above the viewport.
        engine.RegisterFunction("uiText", (args) =>
        {
            if (args.Count >= 3)
            {
                RuntimeUI.Label(
                    args[0]?.ToString() ?? "",
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2]),
                    ParseRuntimeUIAnchor(args, 3),
                    ParseRuntimeUIColor(args, 4, RuntimeUI.TextPrimary));
            }
            return null;
        });

        engine.RegisterFunction("uiPanel", (args) =>
        {
            if (args.Count >= 4)
            {
                RuntimeUI.Panel(
                    Convert.ToSingle(args[0]),
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2]),
                    Convert.ToSingle(args[3]),
                    ParseRuntimeUIAnchor(args, 4),
                    ParseRuntimeUIColor(args, 5, RuntimeUI.PanelColor));
            }
            return null;
        });

        engine.RegisterFunction("uiProgressBar", (args) =>
        {
            if (args.Count >= 5)
            {
                RuntimeUI.ProgressBar(
                    Convert.ToSingle(args[0]),
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2]),
                    Convert.ToSingle(args[3]),
                    Convert.ToSingle(args[4]),
                    ParseRuntimeUIAnchor(args, 5));
            }
            return null;
        });
        
        // Time
        engine.RegisterFunction("getDeltaTime", (args) =>
        {
            return (double)_deltaTime;
        });
        
        // Transform - Get Position
        engine.RegisterFunction("getPositionX", (args) =>
        {
            if (World.HasComponent<RigidbodyComponent>(entity))
            {
                return (double)BlueSky.Physics.PhysicsTeaScriptBridge.GetPosition(entity).X;
            }

            if (World.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                return (double)transform.Position.X;
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getPositionY", (args) =>
        {
            if (World.HasComponent<RigidbodyComponent>(entity))
            {
                return (double)BlueSky.Physics.PhysicsTeaScriptBridge.GetPosition(entity).Y;
            }

            if (World.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                return (double)transform.Position.Y;
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getPositionZ", (args) =>
        {
            if (World.HasComponent<RigidbodyComponent>(entity))
            {
                return (double)BlueSky.Physics.PhysicsTeaScriptBridge.GetPosition(entity).Z;
            }

            if (World.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                return (double)transform.Position.Z;
            }
            return 0.0;
        });
        
        // Transform - Set Position
        engine.RegisterFunction("setPositionX", (args) =>
        {
            if (args.Count >= 1 && World.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref World.GetComponent<TransformComponent>(entity);
                var pos = transform.Position;
                transform.Position = new BlueSky.Core.Math.Vector3(Convert.ToSingle(args[0]), pos.Y, pos.Z);
                SyncPhysicsPosition(entity, transform.Position);
            }
            return null;
        });
        
        engine.RegisterFunction("setPositionY", (args) =>
        {
            if (args.Count >= 1 && World.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref World.GetComponent<TransformComponent>(entity);
                var pos = transform.Position;
                transform.Position = new BlueSky.Core.Math.Vector3(pos.X, Convert.ToSingle(args[0]), pos.Z);
                SyncPhysicsPosition(entity, transform.Position);
            }
            return null;
        });
        
        engine.RegisterFunction("setPositionZ", (args) =>
        {
            if (args.Count >= 1 && World.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref World.GetComponent<TransformComponent>(entity);
                var pos = transform.Position;
                transform.Position = new BlueSky.Core.Math.Vector3(pos.X, pos.Y, Convert.ToSingle(args[0]));
                SyncPhysicsPosition(entity, transform.Position);
            }
            return null;
        });
        
        // Transform - Move
        engine.RegisterFunction("move", (args) =>
        {
            if (args.Count >= 3 && World.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref World.GetComponent<TransformComponent>(entity);
                float x = Convert.ToSingle(args[0]);
                float y = Convert.ToSingle(args[1]);
                float z = Convert.ToSingle(args[2]);
                var pos = transform.Position;
                transform.Position = new BlueSky.Core.Math.Vector3(pos.X + x, pos.Y + y, pos.Z + z);
                SyncPhysicsPosition(entity, transform.Position);
            }
            return null;
        });
        
        // Entity
        engine.RegisterFunction("destroy", (args) =>
        {
            Console.WriteLine($"[TeaScript] Entity {entity.Id} requested destruction");
            return null;
        });
        
        // Input (placeholder)
        engine.RegisterFunction("getKey", (args) =>
        {
            string key = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            return _keyProvider?.Invoke(key) ?? false;
        });
        
        engine.RegisterFunction("getMouseButton", (args) =>
        {
            int button = args.Count > 0 ? Convert.ToInt32(args[0]) : 0;
            return _mouseButtonProvider?.Invoke(button) ?? false;
        });
        
        // Transform - Set Position (all at once)
        engine.RegisterFunction("setPosition", (args) =>
        {
            if (args.Count >= 3 && World.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref World.GetComponent<TransformComponent>(entity);
                float x = Convert.ToSingle(args[0]);
                float y = Convert.ToSingle(args[1]);
                float z = Convert.ToSingle(args[2]);
                transform.Position = new BlueSky.Core.Math.Vector3(x, y, z);
                SyncPhysicsPosition(entity, transform.Position);
            }
            return null;
        });

        // Compatibility alias used by the bundled player.tea example.
        // Two args move in X/Y for simple 2D tests; three args move in 3D.
        engine.RegisterFunction("movePlayer", (args) =>
        {
            if (World == null || args.Count < 2 || !World.HasComponent<TransformComponent>(entity))
                return null;

            ref var transform = ref World.GetComponent<TransformComponent>(entity);
            var pos = transform.Position;
            float x = Convert.ToSingle(args[0]);
            float y = Convert.ToSingle(args[1]);
            float z = args.Count >= 3 ? Convert.ToSingle(args[2]) : pos.Z;
            transform.Position = new BlueSky.Core.Math.Vector3(x, y, z);
            SyncPhysicsPosition(entity, transform.Position);
            return null;
        });
        
        // Math functions
        engine.RegisterFunction("sin", (args) =>
        {
            if (args.Count >= 1)
            {
                double value = Convert.ToDouble(args[0]);
                return System.Math.Sin(value);
            }
            return 0.0;
        });
        
        engine.RegisterFunction("cos", (args) =>
        {
            if (args.Count >= 1)
            {
                double value = Convert.ToDouble(args[0]);
                return System.Math.Cos(value);
            }
            return 0.0;
        });
        
        engine.RegisterFunction("sqrt", (args) =>
        {
            if (args.Count >= 1)
            {
                double value = Convert.ToDouble(args[0]);
                return System.Math.Sqrt(value);
            }
            return 0.0;
        });
        
        engine.RegisterFunction("abs", (args) =>
        {
            if (args.Count >= 1)
            {
                double value = Convert.ToDouble(args[0]);
                return System.Math.Abs(value);
            }
            return 0.0;
        });
        
        engine.RegisterFunction("min", (args) =>
        {
            if (args.Count >= 2)
            {
                double a = Convert.ToDouble(args[0]);
                double b = Convert.ToDouble(args[1]);
                return System.Math.Min(a, b);
            }
            return 0.0;
        });
        
        engine.RegisterFunction("max", (args) =>
        {
            if (args.Count >= 2)
            {
                double a = Convert.ToDouble(args[0]);
                double b = Convert.ToDouble(args[1]);
                return System.Math.Max(a, b);
            }
            return 0.0;
        });
        
        // ══════════════════════════════════════════════════════════════
        //  VEHICLE PHYSICS API (uses static CarControllerSystem lookup)
        // ════════════════════════════════════════════════════════════

        Func<int, CarController?> getController = (entityId) =>
            CarControllerSystem.GetController((uint)entityId);

        Func<int, int, WheelState?> getWheel = (entityId, wheelIndex) =>
        {
            var ctrl = getController(entityId);
            if (ctrl == null || ctrl._wheelStates == null) return null;
            if (wheelIndex < 0 || wheelIndex >= ctrl._wheelStates.Length) return null;
            return ctrl._wheelStates[wheelIndex];
        };

        engine.RegisterFunction("getWheelGrounded", (args) =>
        {
            if (args.Count >= 1)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                var w = getWheel(entity.Id, wheelIndex);
                return w?.IsGrounded ?? false;
            }
            return false;
        });

        engine.RegisterFunction("getWheelSuspension", (args) =>
        {
            if (args.Count >= 1)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                var w = getWheel(entity.Id, wheelIndex);
                return (double)(w?.SuspensionCompression ?? 0.0);
            }
            return 0.0;
        });

        engine.RegisterFunction("getWheelSlip", (args) =>
        {
            if (args.Count >= 2)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                bool isLongitudinal = Convert.ToBoolean(args[1]);
                var w = getWheel(entity.Id, wheelIndex);
                if (w == null) return 0.0;
                return (double)(isLongitudinal ? w.SlipRatio : w.SlipAngle);
            }
            return 0.0;
        });

        engine.RegisterFunction("getWheelSteerAngle", (args) =>
        {
            if (args.Count >= 1)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                var w = getWheel(entity.Id, wheelIndex);
                return (double)(w?.SteerAngle ?? 0.0);
            }
            return 0.0;
        });

        engine.RegisterFunction("getWheelAngularVelocity", (args) =>
        {
            if (args.Count >= 1)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                var w = getWheel(entity.Id, wheelIndex);
                return (double)(w?.AngularVelocity ?? 0.0);
            }
            return 0.0;
        });

        engine.RegisterFunction("getWheelContactNormalX", (args) =>
        {
            if (args.Count >= 1)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                var w = getWheel(entity.Id, wheelIndex);
                return (double)(w?.ContactNormal.X ?? 0.0);
            }
            return 0.0;
        });

        engine.RegisterFunction("getWheelContactNormalY", (args) =>
        {
            if (args.Count >= 1)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                var w = getWheel(entity.Id, wheelIndex);
                return (double)(w?.ContactNormal.Y ?? 0.0);
            }
            return 0.0;
        });

        engine.RegisterFunction("getWheelContactNormalZ", (args) =>
        {
            if (args.Count >= 1)
            {
                int wheelIndex = Convert.ToInt32(args[0]);
                var w = getWheel(entity.Id, wheelIndex);
                return (double)(w?.ContactNormal.Z ?? 0.0);
            }
            return 0.0;
        });

        // ══════════════════════════════════════════════════════════════
        //  BONE MAPPING API (for skeletal mesh vehicle configuration)
        // ══════════════════════════════════════════════════════════════

        // setWheelBone(slot, boneName)
        // slot: 0=RightFront, 1=LeftFront, 2=LeftRear, 3=RightRear, 4=MainBody
        engine.RegisterFunction("setWheelBone", (args) =>
        {
            if (args.Count >= 2)
            {
                int slot = Convert.ToInt32(args[0]);
                string boneName = args[1]?.ToString() ?? "";
                uint eid = (uint)entity.Id;
                CarController.SetBoneOverride(eid, slot, boneName);
                Console.WriteLine($"[TeaScript:{entity.Id}] setWheelBone({slot}, \"{boneName}\")");
            }
            return null;
        });

        // setBodyBone(boneName) — shorthand for setWheelBone(4, boneName)
        engine.RegisterFunction("setBodyBone", (args) =>
        {
            if (args.Count >= 1)
            {
                string boneName = args[0]?.ToString() ?? "";
                uint eid = (uint)entity.Id;
                CarController.SetBodyBoneOverride(eid, boneName);
                Console.WriteLine($"[TeaScript:{entity.Id}] setBodyBone(\"{boneName}\")");
            }
            return null;
        });

        // refreshBones() — re-resolve bone mapping after setting overrides
        // Must be called after setWheelBone/setBodyBone and after the car controller is initialized
        engine.RegisterFunction("refreshBones", (args) =>
        {
            var ctrl = getController(entity.Id);
            if (ctrl != null)
            {
                ctrl.RefreshBoneMapping();
                Console.WriteLine($"[TeaScript:{entity.Id}] Bone mapping refreshed");
            }
            else
            {
                Console.WriteLine($"[TeaScript:{entity.Id}] refreshBones: car controller not yet initialized");
            }
            return null;
        });

        // setWheelPosition(slot, x, y, z) — override wheel local position
        // slot: 0=FrontLeft, 1=FrontRight, 2=RearLeft, 3=RearRight
        engine.RegisterFunction("setWheelPosition", (args) =>
        {
            if (args.Count >= 4)
            {
                var ctrl = getController(entity.Id);
                if (ctrl != null)
                {
                    int slot = Convert.ToInt32(args[0]);
                    float x = Convert.ToSingle(args[1]);
                    float y = Convert.ToSingle(args[2]);
                    float z = Convert.ToSingle(args[3]);
                    ctrl.SetWheelLocalPosition(slot, x, y, z);
                }
            }
            return null;
        });

        // setDriveWheels(fl, fr, rl, rr) — configure which wheels receive motor torque
        engine.RegisterFunction("setDriveWheels", (args) =>
        {
            if (args.Count >= 4)
            {
                var ctrl = getController(entity.Id);
                if (ctrl != null)
                {
                    ctrl.SetDriveWheels(
                        Convert.ToBoolean(args[0]),
                        Convert.ToBoolean(args[1]),
                        Convert.ToBoolean(args[2]),
                        Convert.ToBoolean(args[3]));
                }
            }
            return null;
        });

        // setSteerWheels(fl, fr, rl, rr) — configure which wheels steer
        engine.RegisterFunction("setSteerWheels", (args) =>
        {
            if (args.Count >= 4)
            {
                var ctrl = getController(entity.Id);
                if (ctrl != null)
                {
                    ctrl.SetSteerWheels(
                        Convert.ToBoolean(args[0]),
                        Convert.ToBoolean(args[1]),
                        Convert.ToBoolean(args[2]),
                        Convert.ToBoolean(args[3]));
                }
            }
            return null;
        });

        // ═════════════════════════════════════════════════════════════
        //  PHYSICS API
        // ══════════════════════════════════════════════════════════════
        
        // Rigidbody - Velocity
        engine.RegisterFunction("getVelocityX", (args) =>
        {
            if (World.HasComponent<RigidbodyComponent>(entity))
            {
                var velocity = BlueSky.Physics.PhysicsTeaScriptBridge.GetVelocity(entity);
                return (double)velocity.X;
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getVelocityY", (args) =>
        {
            if (World.HasComponent<RigidbodyComponent>(entity))
            {
                var velocity = BlueSky.Physics.PhysicsTeaScriptBridge.GetVelocity(entity);
                return (double)velocity.Y;
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getVelocityZ", (args) =>
        {
            if (World.HasComponent<RigidbodyComponent>(entity))
            {
                var velocity = BlueSky.Physics.PhysicsTeaScriptBridge.GetVelocity(entity);
                return (double)velocity.Z;
            }
            return 0.0;
        });
        
        engine.RegisterFunction("setVelocity", (args) =>
        {
            if (args.Count >= 3 && World.HasComponent<RigidbodyComponent>(entity))
            {
                float x = Convert.ToSingle(args[0]);
                float y = Convert.ToSingle(args[1]);
                float z = Convert.ToSingle(args[2]);
                var velocity = new System.Numerics.Vector3(x, y, z);
                BlueSky.Physics.PhysicsTeaScriptBridge.SetVelocity(entity, velocity);
            }
            return null;
        });
        
        // Rigidbody - Force
        engine.RegisterFunction("addForce", (args) =>
        {
            if (args.Count >= 3 && World.HasComponent<RigidbodyComponent>(entity))
            {
                float x = Convert.ToSingle(args[0]);
                float y = Convert.ToSingle(args[1]);
                float z = Convert.ToSingle(args[2]);
                var force = new System.Numerics.Vector3(x, y, z);
                BlueSky.Physics.PhysicsTeaScriptBridge.AddForce(entity, force);
            }
            return null;
        });
        
        engine.RegisterFunction("addImpulse", (args) =>
        {
            if (args.Count >= 3 && World.HasComponent<RigidbodyComponent>(entity))
            {
                float x = Convert.ToSingle(args[0]);
                float y = Convert.ToSingle(args[1]);
                float z = Convert.ToSingle(args[2]);
                var impulse = new System.Numerics.Vector3(x, y, z);
                BlueSky.Physics.PhysicsTeaScriptBridge.AddImpulse(entity, impulse);
            }
            return null;
        });
        
        // Rigidbody - Properties
        engine.RegisterFunction("getMass", (args) =>
        {
            if (World.TryGetComponent<RigidbodyComponent>(entity, out var rb))
            {
                return (double)rb.Mass;
            }
            return 1.0;
        });
        
        engine.RegisterFunction("setMass", (args) =>
        {
            if (args.Count >= 1 && World.HasComponent<RigidbodyComponent>(entity))
            {
                ref var rb = ref World.GetComponent<RigidbodyComponent>(entity);
                rb.Mass = Convert.ToSingle(args[0]);
                BlueSky.Physics.PhysicsTeaScriptBridge.SetMass(entity, rb.Mass);
            }
            return null;
        });
        
        engine.RegisterFunction("setGravity", (args) =>
        {
            if (args.Count >= 1 && World.HasComponent<RigidbodyComponent>(entity))
            {
                ref var rb = ref World.GetComponent<RigidbodyComponent>(entity);
                rb.UseGravity = Convert.ToBoolean(args[0]);
                BlueSky.Physics.PhysicsTeaScriptBridge.SetUseGravity(entity, rb.UseGravity);
            }
            return null;
        });
        
        engine.RegisterFunction("setKinematic", (args) =>
        {
            if (args.Count >= 1 && World.HasComponent<RigidbodyComponent>(entity))
            {
                ref var rb = ref World.GetComponent<RigidbodyComponent>(entity);
                rb.IsKinematic = Convert.ToBoolean(args[0]);
                BlueSky.Physics.PhysicsTeaScriptBridge.SetKinematic(entity, rb.IsKinematic);
            }
            return null;
        });
        
        // Rotation
        engine.RegisterFunction("rotate", (args) =>
        {
            if (args.Count >= 3 && World.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref World.GetComponent<TransformComponent>(entity);
                float x = Convert.ToSingle(args[0]);
                float y = Convert.ToSingle(args[1]);
                float z = Convert.ToSingle(args[2]);
                
                // Simple euler angle rotation (degrees)
                transform.Rotation = BlueSky.Core.Math.Quaternion.Euler(x, y, z);
                BlueSky.Physics.PhysicsTeaScriptBridge.SetRotation(entity, new System.Numerics.Quaternion(
                    transform.Rotation.X,
                    transform.Rotation.Y,
                    transform.Rotation.Z,
                    transform.Rotation.W));
            }
            return null;
        });
        
        // Raycasting (placeholder)
        engine.RegisterFunction("raycast", (args) =>
        {
            if (args.Count >= 6)
            {
                // raycast(originX, originY, originZ, dirX, dirY, dirZ, maxDistance)
                Console.WriteLine($"[TeaScript] raycast called but not yet implemented");
                return false;
            }
            return false;
        });
    }

    private static void SyncPhysicsPosition(Entity entity, BlueSky.Core.Math.Vector3 position)
    {
        BlueSky.Physics.PhysicsTeaScriptBridge.SetPosition(
            entity,
            new System.Numerics.Vector3(position.X, position.Y, position.Z));
    }

    private static string ResolveScriptPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        path = path.Trim();
        if (System.IO.File.Exists(path))
            return path;

        if (!System.IO.Path.IsPathRooted(path))
        {
            string cwdPath = System.IO.Path.GetFullPath(path, Environment.CurrentDirectory);
            if (System.IO.File.Exists(cwdPath))
                return cwdPath;
        }

        return path;
    }

    private static RuntimeUIAnchor ParseRuntimeUIAnchor(List<object?> args, int index)
    {
        if (args.Count <= index || args[index] == null)
            return RuntimeUIAnchor.TopLeft;

        string value = args[index]!.ToString() ?? "";
        return Enum.TryParse(value, ignoreCase: true, out RuntimeUIAnchor anchor)
            ? anchor
            : RuntimeUIAnchor.TopLeft;
    }

    private static System.Numerics.Vector4 ParseRuntimeUIColor(List<object?> args, int index, System.Numerics.Vector4 fallback)
    {
        if (args.Count < index + 3)
            return fallback;

        float r = Convert.ToSingle(args[index]);
        float g = Convert.ToSingle(args[index + 1]);
        float b = Convert.ToSingle(args[index + 2]);
        float a = args.Count > index + 3 ? Convert.ToSingle(args[index + 3]) : fallback.W;
        return new System.Numerics.Vector4(r, g, b, a);
    }
    
    /// <summary>
    /// Cleanup all script instances.
    /// </summary>
    public void Cleanup()
    {
        _runtimeInstances.Clear();
    }

    public void ResetRuntimeInstances()
    {
        _runtimeInstances.Clear();
    }
}
