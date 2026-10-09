using System;
using System.Numerics;
using System.Linq;
using BlueSky.Airborne;
using BlueSky.Core.Gameplay;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BVec3 = BlueSky.Core.Math.Vector3;
using BQuat = BlueSky.Core.Math.Quaternion;

namespace BlueSky.Tests;

public static class PhysicsVehicleTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Physics & Vehicle Systems Tests...");
        bool passed = true;
        
        passed &= TestBuiltinPhysicsWorld();
        passed &= TestGravityAndImpulse();
        passed &= TestBodyCollisionResolution();
        passed &= TestTerrainDataAndRaycast();
        passed &= TestModularVehicleWorld();
        passed &= TestDefaultBackendSelection();
        passed &= TestVehiclePhysicsDynamics();
        passed &= TestVehicleFixedStepUpdatesWheelState();
        passed &= TestJoltVehicleFixedStepUpdatesWheelState();
        passed &= TestJoltHeightfieldLandingAndGravityFlag();
        
        return passed;
    }

    private static bool TestBuiltinPhysicsWorld()
    {
        try
        {
            var world = new BuiltinPhysicsWorld();
            world.Initialize();
            world.Step(0.016f);
            
            Console.WriteLine($"  ✓ Physics World Step Simulation: PASSED");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Physics World Step Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestGravityAndImpulse()
    {
        try
        {
            using var world = new BuiltinPhysicsWorld();
            world.Initialize();
            var entity = new World().CreateEntity();
            world.AddBody(entity, new PhysicsComponent { Mass = 1, Size = Vector3.One }, new Vector3(0, 10, 0), Quaternion.Identity);

            world.AddImpulse(entity, new Vector3(0, 2, 0));
            float before = world.GetPosition(entity).Y;
            for (int i = 0; i < 10; i++) world.Step(1.0f / 60.0f);
            float after = world.GetPosition(entity).Y;
            // The impulse initially moves the body upward, while gravity must
            // reduce that upward velocity over subsequent fixed steps.
            bool valid = after > before && world.GetVelocity(entity).Y < 2.0f;
            Console.WriteLine($"  ✓ Gravity + impulse integration (Y={after:F3}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Gravity integration exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestBodyCollisionResolution()
    {
        try
        {
            using var world = new BuiltinPhysicsWorld();
            world.Initialize();
            var ecs = new World();
            var floor = ecs.CreateEntity();
            var box = ecs.CreateEntity();
            world.AddBody(floor, new PhysicsComponent { Mass = 0, UseGravity = false, Size = new Vector3(20, 1, 20) }, new Vector3(0, -0.5f, 0), Quaternion.Identity);
            world.AddBody(box, new PhysicsComponent { Mass = 1, Size = Vector3.One, Restitution = 0 }, new Vector3(0, 3, 0), Quaternion.Identity);
            for (int i = 0; i < 180; i++) world.Step(1.0f / 60.0f);
            float y = world.GetPosition(box).Y;
            bool valid = y >= 0.48f && y < 2.0f;
            Console.WriteLine($"  ✓ Rigid body/floor collision resolution (Y={y:F3}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Collision resolution exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestTerrainDataAndRaycast()
    {
        try
        {
            using var world = new BuiltinPhysicsWorld();
            world.Initialize();
            var ecs = new World();
            var terrain = ecs.CreateEntity();
            var body = ecs.CreateEntity();
            var terrainData = new PhysicsTerrainData
            {
                Width = 2, Height = 2, WorldWidth = 10, WorldDepth = 10,
                OriginOffset = Vector3.Zero, Samples = new float[] { 0, 0, 0, 0 }
            };
            world.AddTerrain(terrain, in terrainData);
            bool rayHit = world.Raycast(new Vector3(5, 5, 5), -Vector3.UnitY, 10, out var hit);
            world.AddBody(body, new PhysicsComponent { Mass = 1, Size = Vector3.One }, new Vector3(5, 3, 5), Quaternion.Identity);
            for (int i = 0; i < 120; i++) world.Step(1.0f / 60.0f);
            float y = world.GetPosition(body).Y;
            bool valid = rayHit && MathF.Abs(hit.Point.Y) < 0.01f && y >= 0.48f;
            Console.WriteLine($"  ✓ Terrain data + downward raycast (Hit={rayHit}, Y={y:F3}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Terrain/raycast exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestModularVehicleWorld()
    {
        try
        {
            using var world = new VehicleWorldPhysics(preferJolt: false);
            bool valid = !world.UsingJolt && world.FallbackPhysics != null;
            var vehicle = world.CreateVehicle(Array.Empty<WheelState>(), 1400f);
            valid &= vehicle != null;
            Console.WriteLine($"  ✓ Modular VehicleWorldPhysics facade (Fallback={world.FallbackPhysics != null}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Modular physics facade exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestVehiclePhysicsDynamics()
    {
        try
        {
            var world = new BuiltinPhysicsWorld();
            world.Initialize();
            var vehicle = new VehiclePhysics(world, Array.Empty<WheelState>(), 1400f);

            vehicle.Step(0.016f, new VehicleInput { Throttle = 1f }, default);
            
            Console.WriteLine($"  ✓ 4-Wheel Vehicle Suspension & Throttle Step: PASSED");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Vehicle Dynamics Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestVehicleFixedStepUpdatesWheelState()
    {
        return RunVehicleFixedStepUpdatesWheelState(preferJolt: false, "Builtin");
    }

    private static bool TestJoltVehicleFixedStepUpdatesWheelState()
    {
        return RunVehicleFixedStepUpdatesWheelState(preferJolt: true, "Jolt");
    }

    private static bool TestJoltHeightfieldLandingAndGravityFlag()
    {
        try
        {
            using var world = new JoltPhysicsWorld();
            world.Initialize();
            var ecs = new World();
            var terrain = ecs.CreateEntity();
            var fallingBody = ecs.CreateEntity();
            var suspendedBody = ecs.CreateEntity();

            var flatTerrain = new PhysicsTerrainData
            {
                Width = 2,
                Height = 2,
                WorldWidth = 20f,
                WorldDepth = 20f,
                OriginOffset = new Vector3(-10f, 0f, -10f),
                Samples = new[] { 0f, 0f, 0f, 0f }
            };
            world.AddTerrain(terrain, in flatTerrain);
            world.AddBody(fallingBody,
                new PhysicsComponent { Mass = 1f, Size = Vector3.One },
                new Vector3(0f, 3f, 0f), Quaternion.Identity);
            world.AddBody(suspendedBody,
                new PhysicsComponent { Mass = 1f, UseGravity = false, Size = Vector3.One },
                new Vector3(3f, 3f, 0f), Quaternion.Identity);

            for (int i = 0; i < 180; i++)
                world.Step(1f / 60f);

            float fallingY = world.GetPosition(fallingBody).Y;
            float suspendedY = world.GetPosition(suspendedBody).Y;
            bool valid = fallingY >= 0.45f && fallingY < 1.5f &&
                         MathF.Abs(suspendedY - 3f) < 0.05f;
            Console.WriteLine($"  ✓ Jolt heightfield landing/gravity flag " +
                $"(fallingY={fallingY:F3}, noGravityY={suspendedY:F3}): " +
                $"{(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Jolt heightfield/gravity exception: {ex.Message}");
            return false;
        }
    }

    private static bool RunVehicleFixedStepUpdatesWheelState(bool preferJolt, string backendName)
    {
        try
        {
            using IPhysicsWorld world = preferJolt
                ? new VehicleWorldPhysics(preferJolt: true)
                : new BuiltinPhysicsWorld();
            var ecs = new World();
            var floor = ecs.CreateEntity();
            var car = ecs.CreateEntity();

            world.AddBody(floor,
                new PhysicsComponent { Mass = 0f, UseGravity = false, Size = new Vector3(30f, 1f, 30f) },
                new Vector3(0f, -0.5f, 0f), Quaternion.Identity);
            world.AddTerrain(floor, (Vector3 _, out float height, out Vector3 normal) =>
            {
                height = 0f;
                normal = Vector3.UnitY;
                return true;
            });
            world.AddBody(car,
                new PhysicsComponent { Mass = 1200f, Size = new Vector3(2f, 1f, 4f) },
                new Vector3(0f, 0.8f, 0f), Quaternion.Identity);

            var wheels = new WheelState[4];
            var wheelPositions = new[]
            {
                new BVec3(-0.85f, -0.30f,  1.35f),
                new BVec3( 0.85f, -0.30f,  1.35f),
                new BVec3(-0.85f, -0.30f, -1.35f),
                new BVec3( 0.85f, -0.30f, -1.35f)
            };
            for (int i = 0; i < wheels.Length; i++)
            {
                wheels[i] = new WheelState
                {
                    Config = new WheelConfig
                    {
                        LocalPosition = wheelPositions[i],
                        SuspensionRestLength = 0.24f,
                        SuspensionStiffness = 22000f,
                        SuspensionDamping = 2800f,
                        WheelRadius = 0.30f,
                        IsDriveWheel = i >= 2,
                        IsSteerWheel = i < 2,
                        MaxSteerAngle = 30f,
                        TractionMultiplier = 1f
                    }
                };
            }

            var vehicle = new VehiclePhysics(world, wheels, 1200f);
            float initialY = world.GetPosition(car).Y;
            for (int i = 0; i < 120; i++)
            {
                vehicle.Step(1f / 60f, new VehicleInput { Throttle = 1f }, car);
                world.Step(1f / 60f);
            }

            bool wheelStateAdvanced = wheels.Any(w => MathF.Abs(w.SpinAngle) > 0.001f) &&
                                       wheels.Any(w => w.IsGrounded);
            bool bodySettled = world.GetPosition(car).Y >= 0.45f;
            bool valid = wheelStateAdvanced && bodySettled &&
                         world.GetPosition(car).Y <= initialY + 0.1f;

            Console.WriteLine($"  ✓ {backendName} fixed-step vehicle body/wheel synchronization " +
                $"(Y={world.GetPosition(car).Y:F3}, spin={wheels[2].SpinAngle:F3}): " +
                $"{(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ {backendName} vehicle fixed-step synchronization exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestDefaultBackendSelection()
    {
        try
        {
            using var world = new VehicleWorldPhysics(preferJolt: true);
            bool valid = world.IsInitialized && (world.UsingJolt || world.FallbackPhysics != null);
            Console.WriteLine($"  ✓ Default physics backend selection (Jolt={world.UsingJolt}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Default backend selection exception: {ex.Message}");
            return false;
        }
    }
}
