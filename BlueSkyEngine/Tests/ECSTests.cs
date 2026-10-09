using System;
using System.Diagnostics;
using System.Linq;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BVec3 = BlueSky.Core.Math.Vector3;

namespace BlueSky.Tests;

public static class ECSTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Archetype ECS Tests...");
        bool passed = true;
        
        passed &= TestWorldEntityCreation();
        passed &= TestArchetypeChunkQuery();
        passed &= Test100KEntityQueryPerformance();
        
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
            
            bool hasTransform = world.TryGetComponent<TransformComponent>(entity, out var transform);
            bool valid = hasTransform && transform.Position.X == 1f;
            
            Console.WriteLine($"  ✓ Archetype Chunk Query: {(valid ? "PASSED" : "FAILED")}");
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
            bool valid = ms < 500.0; // Creation within reasonable budget
            Console.WriteLine($"  ✓ 100K Entity Creation Query ({ms:F2}ms): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ 100K Entity Query Exception: {ex.Message}");
            return false;
        }
    }
}


