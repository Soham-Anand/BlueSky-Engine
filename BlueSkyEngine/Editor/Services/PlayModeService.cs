using System;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Scene;
using BlueSky.Airborne;
using BlueSky.Rendering;

namespace BlueSky.Editor.Services;

/// <summary>
/// Owns play-mode state transitions and physics lifetime.
/// Logic here is engine/editor specific (snapshot + TeaScript + physics world).
/// </summary>
public sealed class PlayModeService
{
    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }

    public SceneSnapshot? Snapshot { get; private set; }
    public IPhysicsWorld? PhysicsWorld { get; private set; }

    public void Start(World world, TerrainSystem? terrainSystem, Action hotReloadScripts, Action resetTeaScriptRuntimeInstances, Action<string> log)
    {
        if (IsPlaying)
            return;

        // The visualizer is opt-in for each play session. Otherwise a stale
        // editor toggle can draw projected labels over the viewport.
        BlueSky.Runtime.PhysicsDebugVisualizer.SetEnabled(false);

        Snapshot = new SceneSnapshot();
        Snapshot.Capture(world);

        resetTeaScriptRuntimeInstances();
        hotReloadScripts();

        if (PhysicsWorld == null)
        {
            var modularWorld = new VehicleWorldPhysics(preferJolt: true);
            PhysicsWorld = modularWorld;
            log(modularWorld.UsingJolt ? "Using Jolt Physics" : "Using Builtin Physics fallback");
        }

        PhysicsTeaScriptBridge.Initialize(PhysicsWorld);

        RegisterTerrains(world, terrainSystem);
        // Populate physics bodies from ECS
        var physicsQuery = world.CreateQuery()
            .All<PhysicsComponent>()
            .All<TransformComponent>()
            .Build();

        var chunks = world.GetQueryChunks(physicsQuery);
        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();
            int physIdx = chunk.GetComponentIndex(typeof(PhysicsComponent));
            int transIdx = chunk.GetComponentIndex(typeof(TransformComponent));

            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                var phys = chunk.GetComponent<PhysicsComponent>(i, physIdx);
                var trans = chunk.GetComponent<TransformComponent>(i, transIdx);

                // NaN guard: if transform has NaN values, use safe defaults
                var pos = new System.Numerics.Vector3(
                    float.IsNaN(trans.Position.X) || float.IsInfinity(trans.Position.X) ? 0f : trans.Position.X,
                    float.IsNaN(trans.Position.Y) || float.IsInfinity(trans.Position.Y) ? 0f : trans.Position.Y,
                    float.IsNaN(trans.Position.Z) || float.IsInfinity(trans.Position.Z) ? 0f : trans.Position.Z);
                var rot = new System.Numerics.Quaternion(
                    float.IsNaN(trans.Rotation.X) || float.IsInfinity(trans.Rotation.X) ? 0f : trans.Rotation.X,
                    float.IsNaN(trans.Rotation.Y) || float.IsInfinity(trans.Rotation.Y) ? 0f : trans.Rotation.Y,
                    float.IsNaN(trans.Rotation.Z) || float.IsInfinity(trans.Rotation.Z) ? 0f : trans.Rotation.Z,
                    float.IsNaN(trans.Rotation.W) || float.IsInfinity(trans.Rotation.W) ? 1f : trans.Rotation.W);

                if (pos != new System.Numerics.Vector3(trans.Position.X, trans.Position.Y, trans.Position.Z))
                    Console.WriteLine($"[PlayMode] WARNING: Entity {entity.Id} had NaN position, reset to origin");

                // Mass guard: ensure mass is never zero (would cause InverseMass = Infinity → NaN)
                if (phys.Mass < 0.001f)
                {
                    phys.Mass = 1.0f;
                    Console.WriteLine($"[PlayMode] WARNING: Entity {entity.Id} had zero mass, reset to 1.0");
                }

                try
                {
                    PhysicsWorld.AddBody(entity, phys, pos, rot);
                }
                catch (Exception ex)
                {
                    log($"Physics body registration failed for entity {entity.Id}: {ex.Message}");
                }
            }
        }

        IsPlaying = true;
        IsPaused = false;
        log("Play mode started - scripts running");
    }

    private void RegisterTerrains(World world, TerrainSystem? terrainSystem)
    {
        if (PhysicsWorld == null || terrainSystem == null)
            return;

        terrainSystem.Update();

        var terrainQuery = world.CreateQuery()
            .All<TerrainComponent>()
            .Build();

        foreach (var chunk in world.GetQueryChunks(terrainQuery))
        {
            var entities = chunk.GetEntities();
            int terrainIdx = chunk.GetComponentIndex(typeof(TerrainComponent));

            for (int i = 0; i < chunk.Count; i++)
            {
                var terrain = chunk.GetComponent<TerrainComponent>(i, terrainIdx);
                if (!terrain.CollisionEnabled)
                    continue;

                var entity = entities[i];
                uint terrainEntityId = (uint)entity.Id;

                // Prefer the real height-field path: the physics engine
                // builds a proper heightfield collider so the car lands
                // on the terrain naturally. Fall back to the height
                // sampler (Builtin physics) if the height field can't
                // be built.
                if (terrainSystem.TryGetPhysicsHeightField(terrainEntityId, out var hf))
                {
                    PhysicsWorld.AddTerrain(entity, in hf);
                }

                PhysicsWorld.AddTerrain(entity, (System.Numerics.Vector3 worldPosition, out float height, out System.Numerics.Vector3 normal) =>
                    terrainSystem.TrySampleWorldHeight(terrainEntityId, worldPosition, out height, out normal));
            }
        }
    }

    public void TogglePause(Action<string> log)
    {
        if (!IsPlaying)
            return;

        IsPaused = !IsPaused;
        log(IsPaused ? "Paused" : "Resumed");
    }

    public void Stop(World world, Action hotReloadScripts, Action<string> log)
    {
        if (!IsPlaying)
            return;

        IsPlaying = false;
        IsPaused = false;
        BlueSky.Runtime.PhysicsDebugVisualizer.SetEnabled(false);

        // ── Clean up car controller system (resets IsInitialized, clears state) ──
        log("Cleaning up car controller system...");
        Program._carControllerSystem?.Cleanup();

        // ── Auto-unpossess any possessed entity (e.g. car) before teardown ──
        var playerCtrl = BlueSky.Core.Gameplay.PlayerController.Instance;
        if (playerCtrl.PossessedEntity != null)
        {
            log("Auto-unpossessing controlled entity...");
            playerCtrl.Unpossess();
        }

        PhysicsTeaScriptBridge.Shutdown();
        PhysicsWorld?.Dispose();
        PhysicsWorld = null;

        if (Snapshot != null)
        {
            Snapshot.Restore(world);
            Snapshot.Clear();
            Snapshot = null;
        }

        log("Stopped - scene restored to editor state");
        hotReloadScripts();
    }
}
