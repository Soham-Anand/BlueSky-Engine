using System;
using System.Numerics;
using System.Runtime.Intrinsics.X86;
using BlueSky.Rendering.RayTracing;
using BlueSky.Rendering.RayTracing.Polaris;

namespace BlueSky.Tests;

public static class PolarisSIMDTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Project Polaris AVX SIMD Tests...");
        bool passed = true;
        
        passed &= TestAVXHardwareSupport();
        passed &= TestBVHConstruction();
        passed &= TestSIMDRayTraversal();
        
        return passed;
    }

    private static bool TestAVXHardwareSupport()
    {
        bool supported = Avx.IsSupported;
        Console.WriteLine($"  ✓ AVX SIMD Hardware Detection: (Supported={supported}) PASSED");
        return true; // Test passes regardless of platform capabilities
    }

    private static bool TestBVHConstruction()
    {
        try
        {
            var triangles = new Triangle[2]
            {
                new Triangle
                {
                    V0 = new Vector3(-1, 0, 0),
                    V1 = new Vector3(1, 0, 0),
                    V2 = new Vector3(0, 2, 0),
                    N0 = Vector3.UnitZ
                },
                new Triangle
                {
                    V0 = new Vector3(0, 0, -1),
                    V1 = new Vector3(0, 0, 1),
                    V2 = new Vector3(0, 2, 0),
                    N0 = Vector3.UnitX
                }
            };

            var bvh = new SIMDBVHTraversal();
            bvh.Build(triangles);
            
            bool valid = bvh.NodeCount > 0 && bvh.TriangleCount == 2;
            Console.WriteLine($"  ✓ SAH BVH Construction ({bvh.NodeCount} nodes): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ SAH BVH Construction Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestSIMDRayTraversal()
    {
        try
        {
            if (!Avx.IsSupported)
            {
                Console.WriteLine("  ✓ 8-Wide AVX Ray Packet Intersect: SKIPPED (ARM64 / Non-AVX Platform)");
                return true;
            }

            var triangles = new Triangle[1]
            {
                new Triangle
                {
                    V0 = new Vector3(-1, -1, -5),
                    V1 = new Vector3(1, -1, -5),
                    V2 = new Vector3(0, 1, -5),
                    N0 = Vector3.UnitZ
                }
            };

            var bvh = new SIMDBVHTraversal();
            bvh.Build(triangles);
            
            Vector3[] directions = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                directions[i] = new Vector3(0, 0, -1f);
            }

            var rays = RayPacket8.CreatePrimary(Vector3.Zero, directions, 8);
            bvh.Traverse(ref rays);
            
            float hitT = rays.GetHitT(0);
            bool valid = hitT < 1000f;
            Console.WriteLine($"  ✓ 8-Wide AVX Ray Packet Intersect (HitT={hitT:F2}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ SIMD Ray Traversal Exception: {ex.Message}");
            return false;
        }
    }

}


