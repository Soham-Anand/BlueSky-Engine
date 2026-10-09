using System;
using System.Collections.Generic;
using System.Numerics;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Airborne;
using BlueSky.Runtime.UI;

namespace BlueSky.Runtime;

/// <summary>
/// Super lightweight physics debug visualizer.
/// Draws a stable, fixed-position physics diagnostics panel using RuntimeUI.
/// Toggle with Ctrl/Cmd+K in the editor.
/// </summary>
public static class PhysicsDebugVisualizer
{
    private static bool _enabled;

    public static bool IsEnabled => _enabled;

    public static void Toggle() => _enabled = !_enabled;

    /// <summary>
    /// Sets the visualizer state explicitly. Play sessions use this to avoid
    /// carrying a debug-overlay toggle from a previous editor session.
    /// </summary>
    public static void SetEnabled(bool enabled) => _enabled = enabled;

    /// <summary>
    /// Call from editor update loop after diagnostics HUD.
    /// Draws physics body positions, collider data, and wheel suspension info.
    /// </summary>
    public static void Draw(
        IPhysicsWorld physicsWorld,
        World? world,
        Matrix4x4 viewProj,
        int viewportW,
        int viewportH,
        float deltaTime)
    {
        if (!_enabled || physicsWorld == null || world == null) return;

        // Panel for text info
        RuntimeUI.Panel(10, 300, 520, 200);

        int y = 310;
        RuntimeUI.Label($"=== PHYSICS DEBUG (Ctrl/Cmd+K) ===", 20, y);
        y += 22;

        int bodyCount = 0;
        foreach (var debugBody in physicsWorld.GetDebugBodies())
        {
            bodyCount++;
            var entity = debugBody.Entity;

            // Get transform position for comparison
            Vector3 transformPos = Vector3.Zero;
            bool hasTransform = false;
            if (world.TryGetComponent<TransformComponent>(entity, out var tf))
            {
                transformPos = new Vector3(tf.Position.X, tf.Position.Y, tf.Position.Z);
                hasTransform = true;
            }

            // Calculate offset between physics and transform
            Vector3 offset = hasTransform ? transformPos - debugBody.Position : Vector3.Zero;
            float offsetLen = offset.Length();

            // Color: green if aligned, red if offset
            var color = offsetLen < 0.01f
                ? new Vector4(0.2f, 1f, 0.3f, 1f)  // green
                : new Vector4(1f, 0.3f, 0.2f, 1f);  // red

            // Text info in panel
            string name = "";
            if (world.TryGetComponent<NameComponent>(entity, out var nc))
                name = $" ({nc.Name})";
            else if (world.TryGetComponent<CarControllerComponent>(entity, out _))
                name = " (Car)";

            RuntimeUI.Label(
                $"#{entity.Id}{name}  Body: ({debugBody.Position.X:F2}, {debugBody.Position.Y:F2}, {debugBody.Position.Z:F2})",
                20, y, RuntimeUIAnchor.TopLeft, color);
            y += 18;

            if (hasTransform)
            {
                string offsetStr = offsetLen > 0.01f
                    ? $"  Transform: ({transformPos.X:F2}, {transformPos.Y:F2}, {transformPos.Z:F2})  OFFSET: {offsetLen:F3}m"
                    : $"  Transform: aligned";
                RuntimeUI.Label(offsetStr, 20, y, RuntimeUIAnchor.TopLeft,
                    offsetLen > 0.01f ? new Vector4(1f, 0.6f, 0.2f, 1f) : new Vector4(0.5f, 0.8f, 0.5f, 1f));
                y += 18;
            }

            // Collider info
            Vector3 colliderWorldCenter = debugBody.Position + debugBody.ColliderCenter;
            RuntimeUI.Label(
                $"  Collider: size=({debugBody.ColliderSize.X:F2}, {debugBody.ColliderSize.Y:F2}, {debugBody.ColliderSize.Z:F2})  center=({debugBody.ColliderCenter.X:F2}, {debugBody.ColliderCenter.Y:F2}, {debugBody.ColliderCenter.Z:F2})  world_center=({colliderWorldCenter.X:F2}, {colliderWorldCenter.Y:F2}, {colliderWorldCenter.Z:F2})",
                20, y, RuntimeUIAnchor.TopLeft, new Vector4(0.7f, 0.7f, 1f, 1f));
            y += 18;

            // Velocity
            float speed = debugBody.Velocity.Length();
            if (speed > 0.01f)
            {
                RuntimeUI.Label(
                    $"  Velocity: ({debugBody.Velocity.X:F2}, {debugBody.Velocity.Y:F2}, {debugBody.Velocity.Z:F2})  speed={speed:F2} m/s",
                    20, y, RuntimeUIAnchor.TopLeft, new Vector4(0.6f, 0.9f, 1f, 1f));
                y += 18;
            }

            // Wheel info for cars
            if (world.HasComponent<CarControllerComponent>(entity))
            {
                var ctrl = BlueSky.Core.Gameplay.CarControllerSystem.GetController((uint)entity.Id);
                if (ctrl != null && ctrl._wheelStates != null)
                {
                    string[] wheelNames = { "FL", "FR", "RL", "RR" };
                    for (int i = 0; i < 4 && i < ctrl._wheelStates.Length; i++)
                    {
                        var ws = ctrl._wheelStates[i];
                        var wheelColor = ws.IsGrounded
                            ? new Vector4(0.2f, 1f, 0.3f, 1f)
                            : new Vector4(1f, 0.3f, 0.2f, 1f);

                        string groundStr = ws.IsGrounded ? "GND" : "AIR";
                        RuntimeUI.Label(
                            $"    {wheelNames[i]}: {groundStr} susp={ws.SuspensionCompression * 100:F0}% contact=({ws.ContactPoint.X:F2},{ws.ContactPoint.Y:F2},{ws.ContactPoint.Z:F2})",
                            20, y, RuntimeUIAnchor.TopLeft, wheelColor);
                        y += 16;
                    }

                    // Draw wheel local positions (what suspension raycasts from)
                    RuntimeUI.Label(
                        $"  Wheel local Y positions: FL={ctrl._wheelStates[0].Config.LocalPosition.Y:F3} FR={ctrl._wheelStates[1].Config.LocalPosition.Y:F3} RL={ctrl._wheelStates[2].Config.LocalPosition.Y:F3} RR={ctrl._wheelStates[3].Config.LocalPosition.Y:F3}",
                        20, y, RuntimeUIAnchor.TopLeft, new Vector4(1f, 1f, 0.5f, 1f));
                    y += 16;
                }
            }

            y += 4; // spacing between entities
        }

        RuntimeUI.Label($"Total physics bodies: {bodyCount}", 20, y, RuntimeUIAnchor.TopLeft, new Vector4(0.8f, 0.8f, 0.8f, 1f));
    }

}
