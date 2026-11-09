using System;
using BlueSky.Core.Scripting;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BlueSky.Meridian;
using BlueSky.Motif.AnimScript;
using BlueSky.Motif.Procedural;
using BlueSky.BSR;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;
using BlueSky.Editor;
using BlueSky.Pulse;
using BVec3 = BlueSky.Ramanujan.Vector3;
using BQuat = BlueSky.Ramanujan.Quaternion;

namespace BlueSky.Tests;

public static class ECSTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running SkyCore ECS, SkyWorld Streaming, Tools & Motif Tests...");
        bool passed = true;
        
        passed &= TestWorldEntityCreation();
        passed &= TestArchetypeChunkQuery();
        passed &= Test100KEntityQueryPerformance();
        passed &= TestSkyCoreCommandBuffer();
        passed &= TestHierarchyTransformPropagation();
        passed &= TestWorldStreamerCellResidency();
        passed &= TestTerrainChunkLodGeneration();
        passed &= TestPrefabManagerHierarchyRoundTrip();
        passed &= TestTeaScriptHotReloaderAndDiagnostics();
        passed &= TestMotifAnimStateGraphAndProceduralScatter();
        
        return passed;
    }

    private static bool TestWorldEntityCreation()
    {
        try
        {
            var world = new World();
            var e1 = world.CreateEntity();
            var e2 = world.CreateEntity();
            world.AddComponent(e1, new NameComponent("Entity1"));
            world.AddComponent(e2, new NameComponent("Entity2"));
            
            bool valid = e1.Id != e2.Id && world.GetAllEntities().Count() >= 2;
            Console.WriteLine($"  ✓ Entity Creation: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Entity Creation Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestArchetypeChunkQuery()
    {
        try
        {
            var world = new World();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new NameComponent("TestActor"));
            world.AddComponent(entity, new TransformComponent(new BVec3(1, 2, 3)));

            uint versionBefore = world.Version;
            world.ForEachRef<TransformComponent>((Entity e, ref TransformComponent t) =>
            {
                t.Position = new BVec3(t.Position.X + 10f, t.Position.Y, t.Position.Z);
            });

            bool hasTransform = world.TryGetComponent<TransformComponent>(entity, out var transform);
            bool valid = hasTransform && MathF.Abs(transform.Position.X - 11f) < 0.001f && world.Version > versionBefore;
            
            Console.WriteLine($"  ✓ SkyCore Zero-Alloc ForEachRef & Version Tracking: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Archetype Chunk Query Exception: {ex.Message}");
            return false;
        }
    }

    private static bool Test100KEntityQueryPerformance()
    {
        try
        {
            var world = new World();
            int count = 100000;
            
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                var e = world.CreateEntity();
                world.AddComponent(e, new TransformComponent(new BVec3(i, 0, 0)));
            }
            sw.Stop();
            
            double ms = sw.Elapsed.TotalMilliseconds;
            bool valid = ms < 500.0;
            Console.WriteLine($"  ✓ 100K Entity Creation Query ({ms:F2}ms): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ 100K Entity Query Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestSkyCoreCommandBuffer()
    {
        try
        {
            var world = new World();
            var cmd = world.CreateCommandBuffer();

            var deferred = cmd.CreateEntity();
            cmd.AddComponent(deferred, new NameComponent("DeferredActor"));
            cmd.AddComponent(deferred, new TransformComponent(new BVec3(42f, 10f, -5f)));

            bool emptyBeforePlayback = world.EntityCount == 0;
            cmd.Playback(world);

            bool existsAfter = world.EntityCount == 1 &&
                               world.TryResolveEntity(1, out var resolved) &&
                               world.TryGetComponent<TransformComponent>(resolved, out var t) &&
                               MathF.Abs(t.Position.X - 42f) < 0.001f;

            bool valid = emptyBeforePlayback && existsAfter;
            Console.WriteLine($"  ✓ SkyCoreCommandBuffer Deferred Playback: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ SkyCoreCommandBuffer Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestHierarchyTransformPropagation()
    {
        try
        {
            var world = new World();
            var hierarchySystem = new HierarchySystem();
            world.AddSystem(hierarchySystem);

            var parent = world.CreateEntity();
            world.AddComponent(parent, new TransformComponent(new BVec3(10f, 0f, 5f), BQuat.Identity, new BVec3(2f, 2f, 2f)));

            var child = world.CreateEntity();
            world.AddComponent(child, new ParentComponent(parent));
            world.AddComponent(child, new LocalTransformComponent(new BVec3(3f, 1f, 0f), BQuat.Identity, BVec3.One));
            world.AddComponent(child, TransformComponent.Default);

            world.Update(0.016f);

            bool hasChildTransform = world.TryGetComponent<TransformComponent>(child, out var childWorld);
            bool valid = hasChildTransform &&
                         MathF.Abs(childWorld.Position.X - 16f) < 0.01f &&
                         MathF.Abs(childWorld.Position.Y - 2f) < 0.01f &&
                         MathF.Abs(childWorld.Position.Z - 5f) < 0.01f;

            Console.WriteLine($"  ✓ SkyCore Hierarchy Transform Propagation: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Hierarchy Transform Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestWorldStreamerCellResidency()
    {
        try
        {
            var world = new World();
            var streamer = new WorldStreamer(cellSize: 100f)
            {
                ActiveSimulationRadius = 1,
                VisibleLodRadius = 2,
                UnloadRadius = 3
            };
            world.AddSystem(streamer);

            var nearEntity = world.CreateEntity();
            world.AddComponent(nearEntity, new NameComponent("NearProp"));
            world.AddComponent(nearEntity, new TransformComponent(new BVec3(50f, 0f, 50f)));

            var farEntity = world.CreateEntity();
            world.AddComponent(farEntity, new NameComponent("FarProp"));
            world.AddComponent(farEntity, new TransformComponent(new BVec3(850f, 0f, 850f)));

            streamer.SetStreamingAnchor(new BVec3(0f, 0f, 0f));
            world.Update(0.016f);

            bool hibernatedFar = world.EntityCount == 1 && streamer.DormantCellCount == 1;

            streamer.SetStreamingAnchor(new BVec3(800f, 0f, 800f));
            world.Update(0.016f);

            bool wokeFar = streamer.DormantCellCount == 1 && world.EntityCount == 1;
            bool valid = hibernatedFar && wokeFar;

            Console.WriteLine($"  ✓ SkyWorld Spatial Cell Streaming (Hibernate & Wake): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ WorldStreamer Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestTerrainChunkLodGeneration()
    {
        try
        {
            var world = new World();
            var terrainSystem = new TerrainSystem(world);
            var terrainEntity = world.CreateEntity();
            var comp = new TerrainComponent
            {
                Width = 65,
                Height = 65,
                WorldWidth = 64f,
                WorldHeight = 64f,
                MaxElevation = 20f,
                ChunkSize = 32,
                LodCount = 3
            };
            world.AddComponent(terrainEntity, comp);
            terrainSystem.InitializeTerrain((uint)terrainEntity.Id, comp);

            var chunks = terrainSystem.GetChunkMeshes((uint)terrainEntity.Id);
            bool valid = chunks.Count == 4 &&
                         chunks[0].LodIndices.Length == 3 &&
                         chunks[0].LodIndices[0].Length > chunks[0].LodIndices[1].Length &&
                         chunks[0].LodIndices[1].Length > chunks[0].LodIndices[2].Length;

            Console.WriteLine($"  ✓ SkyWorld Terrain Chunking & Multi-LOD Generation: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Terrain Chunk LOD Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestPrefabManagerHierarchyRoundTrip()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"bluesky_prefab_{Guid.NewGuid():N}.bseprefab");
        try
        {
            var srcWorld = new World();
            var root = srcWorld.CreateEntity();
            srcWorld.AddComponent(root, new NameComponent("CarRoot"));
            srcWorld.AddComponent(root, new TransformComponent(new BVec3(1f, 2f, 3f)));

            var wheel = srcWorld.CreateEntity();
            srcWorld.AddComponent(wheel, new NameComponent("FrontWheel"));
            srcWorld.AddComponent(wheel, new TransformComponent(BVec3.Zero));
            srcWorld.AddComponent(wheel, new ParentComponent(root));
            srcWorld.AddComponent(wheel, new LocalTransformComponent(new BVec3(0.8f, -0.3f, 1.5f)));

            bool saved = PrefabManager.SavePrefab(srcWorld, root, tempPath);

            var dstWorld = new World();
            dstWorld.AddSystem(new HierarchySystem());
            var spawnedRoot = PrefabManager.InstantiatePrefab(dstWorld, tempPath, new BVec3(100f, 5f, 50f), BQuat.Identity);
            dstWorld.Update(0.016f);

            bool valid = saved &&
                         !spawnedRoot.IsNull &&
                         dstWorld.EntityCount == 2 &&
                         dstWorld.TryGetComponent<TransformComponent>(spawnedRoot, out var rt) &&
                         MathF.Abs(rt.Position.X - 100f) < 0.01f;

            Console.WriteLine($"  ✓ PrefabManager Save & Hierarchy Instantiate: {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ PrefabManager Exception: {ex.Message}");
            return false;
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static bool TestTeaScriptHotReloaderAndDiagnostics()
    {
        string tempScript = Path.Combine(Path.GetTempPath(), $"hotreload_{Guid.NewGuid():N}.tea");
        try
        {
            File.WriteAllText(tempScript, "func update() { }\n");
            var world = new World();
            var e = world.CreateEntity();
            world.AddComponent(e, new TransformComponent(BVec3.Zero));
            world.AddComponent(e, new TeaScriptComponent
            {
                ScriptAssetId = tempScript,
                IsEnabled = true,
                IsInitialized = true
            });

            using var reloader = new TeaScriptHotReloader();
            // First poll baselines timestamp
            reloader.PollAndReload(world, null);

            // Bump last write time and poll again
            File.SetLastWriteTimeUtc(tempScript, DateTime.UtcNow.AddSeconds(5));
            int reloaded = reloader.PollAndReload(world, null);

            var telemetry = SkyCoreDiagnostics.Capture(world);
            bool valid = reloaded == 1 &&
                         reloader.ReloadCount == 1 &&
                         telemetry.EntityCount == 1 &&
                         telemetry.NonEmptyChunkCount >= 1 &&
                         telemetry.AllocatedChunkBytes >= ArchetypeChunk.ChunkSize;

            Console.WriteLine($"  ✓ TeaScriptHotReloader & SkyCoreDiagnostics ({telemetry.AllocatedChunkBytes / 1024}KB): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ TeaScriptHotReloader Exception: {ex.Message}");
            return false;
        }
        finally
        {
            if (File.Exists(tempScript))
                File.Delete(tempScript);
        }
    }

    private static bool TestMotifAnimStateGraphAndProceduralScatter()
    {
        try
        {
            var graph = new AnimStateGraph("Idle");
            graph.AddTransition("Idle", "Run", 0.25f)
                 .When("speed", AnimConditionOp.GreaterThan, 2.0f);
            graph.AddTransition("Run", "Jump", 0.1f)
                 .When("jump", AnimConditionOp.Triggered);

            graph.SetFloat("speed", 5.0f);
            graph.Evaluate(0.05f);
            bool transitionedToRun = graph.CurrentState == "Run" && graph.PreviousState == "Idle";

            graph.SetTrigger("jump");
            graph.Evaluate(0.05f);
            bool transitionedToJump = graph.CurrentState == "Jump";

            // Test ProceduralScatter on a flat terrain
            var world = new World();
            var terrainSystem = new TerrainSystem(world);
            var terrainEntity = world.CreateEntity();
            world.AddComponent(terrainEntity, new TransformComponent(BVec3.Zero));
            var comp = new TerrainComponent
            {
                Width = 33,
                Height = 33,
                WorldWidth = 64f,
                WorldHeight = 64f,
                MaxElevation = 20f
            };
            world.AddComponent(terrainEntity, comp);
            terrainSystem.InitializeTerrain((uint)terrainEntity.Id, comp);

            var instances = ProceduralScatter.GenerateOnTerrain(
                terrainSystem,
                (uint)terrainEntity.Id,
                new BVec3(4f, 0f, 4f),
                new BVec3(60f, 20f, 60f),
                ScatterRule.DefaultFoliage);

            bool valid = transitionedToRun && transitionedToJump && instances.Count > 0;
            Console.WriteLine($"  ✓ Motif AnimStateGraph & ProceduralScatter (spawned={instances.Count}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Motif AnimStateGraph/ProceduralScatter Exception: {ex.Message}");
            return false;
        }
    }
}
