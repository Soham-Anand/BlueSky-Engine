using System;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Scene;
using BlueSky.Physics;
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

        Snapshot = new SceneSnapshot();
        Snapshot.Capture(world);

        resetTeaScriptRuntimeInstances();
        hotReloadScripts();

        if (PhysicsWorld == null)
        {
            try
            {
                log("Attempting to initialize Jolt Physics...");
                var jolt = new JoltPhysicsWorld();
                jolt.Initialize();
                PhysicsWorld = jolt;
                log("Using Jolt Physics");
            }
            catch (Exception ex)
            {
                log($"Jolt Physics initialization failed: {ex.Message}. Falling back to Builtin Physics.");
                var builtin = new BuiltinPhysicsWorld();
                builtin.Initialize();
                PhysicsWorld = builtin;
                log("Using Builtin Physics");
            }
        }

        PhysicsTeaScriptBridge.Initialize(PhysicsWorld);

        RegisterTerrains(world, terrainSystem);
        // Populate physics bodies from ECS
        var physicsQuery = world.CreateQuery()
            .All<RigidbodyComponent>()
            .All<ColliderComponent>()
            .All<TransformComponent>()
            .Build();

        var chunks = world.GetQueryChunks(physicsQuery);
        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();
            int rbIdx = chunk.GetComponentIndex(typeof(RigidbodyComponent));
            int colIdx = chunk.GetComponentIndex(typeof(ColliderComponent));
            int transIdx = chunk.GetComponentIndex(typeof(TransformComponent));

            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                var rb = chunk.GetComponent<RigidbodyComponent>(i, rbIdx);
                var col = chunk.GetComponent<ColliderComponent>(i, colIdx);
                var trans = chunk.GetComponent<TransformComponent>(i, transIdx);

                var pos = new System.Numerics.Vector3(trans.Position.X, trans.Position.Y, trans.Position.Z);
                var rot = new System.Numerics.Quaternion(trans.Rotation.X, trans.Rotation.Y, trans.Rotation.Z, trans.Rotation.W);

                PhysicsWorld.AddBody(entity, rb, col, pos, rot);
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
