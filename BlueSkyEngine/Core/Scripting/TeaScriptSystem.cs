using System;
using System.Collections.Generic;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Assets;
using TeaScript.Bridge;

namespace BlueSky.Core.Scripting;

/// <summary>
/// ECS System that manages TeaScript execution for all entities with TeaScriptComponent.
/// </summary>
public class TeaScriptSystem : SystemBase
{
    private readonly Dictionary<uint, TeaScriptEngine> _runtimeInstances = new();
    private float _deltaTime = 0.016f;
    
    public TeaScriptSystem(World world)
    {
        Initialize(world);
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
            var engine = new TeaScriptEngine();
            
            // Register basic engine functions
            RegisterEngineFunctions(engine, entity);
            
            // Load the actual script file
            if (!string.IsNullOrEmpty(script.ScriptAssetId))
            {
                Console.WriteLine($"[TeaScript] Loading script: {script.ScriptAssetId}");
                engine.LoadScript(script.ScriptAssetId);
            }
            else
            {
                Console.WriteLine($"[TeaScript] No script file specified for entity {entity.Id}");
                script.IsEnabled = false;
                return;
            }
            
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
        
        // Time
        engine.RegisterFunction("getDeltaTime", (args) =>
        {
            return (double)_deltaTime;
        });
        
        // Transform - Get Position
        engine.RegisterFunction("getPositionX", (args) =>
        {
            if (World.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                return (double)transform.Position.X;
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getPositionY", (args) =>
        {
            if (World.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                return (double)transform.Position.Y;
            }
            return 0.0;
        });
        
        engine.RegisterFunction("getPositionZ", (args) =>
        {
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
            return false;
        });
        
        engine.RegisterFunction("getMouseButton", (args) =>
        {
            return false;
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
            }
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
        
        // ═══════════════════════════════════════════════════════════════
        //  PHYSICS API
        // ═══════════════════════════════════════════════════════════════
        
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
                BlueSky.Physics.PhysicsTeaScriptBridge.AddForce(entity, impulse);
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
            }
            return null;
        });
        
        engine.RegisterFunction("setGravity", (args) =>
        {
            if (args.Count >= 1 && World.HasComponent<RigidbodyComponent>(entity))
            {
                ref var rb = ref World.GetComponent<RigidbodyComponent>(entity);
                rb.UseGravity = Convert.ToBoolean(args[0]);
            }
            return null;
        });
        
        engine.RegisterFunction("setKinematic", (args) =>
        {
            if (args.Count >= 1 && World.HasComponent<RigidbodyComponent>(entity))
            {
                ref var rb = ref World.GetComponent<RigidbodyComponent>(entity);
                rb.IsKinematic = Convert.ToBoolean(args[0]);
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
    
    /// <summary>
    /// Cleanup all script instances.
    /// </summary>
    public void Cleanup()
    {
        _runtimeInstances.Clear();
    }
}
