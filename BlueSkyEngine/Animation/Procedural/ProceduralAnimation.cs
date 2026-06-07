using System;
using System.Collections.Generic;
using System.Numerics;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;

namespace BlueSky.Animation.Procedural;

public struct ProceduralOffset
{
    public Vector3 PositionDelta;
    public Vector3 RotationDelta; // Euler angles (pitch, yaw, roll)
    public float FOVDelta;
}

public interface IProceduralOffsetProvider
{
    void Update(float deltaTime);
    ProceduralOffset GetOffset();
    bool IsActive { get; }
}

/// <summary>
/// System for managing and combining procedural animation offsets.
/// Runs after the main AnimationSystem.
/// </summary>
public class ProceduralAnimationSystem : SystemBase
{
    private readonly List<IProceduralOffsetProvider> _providers = new();

    public void RegisterProvider(IProceduralOffsetProvider provider)
    {
        if (!_providers.Contains(provider))
        {
            _providers.Add(provider);
        }
    }

    public void UnregisterProvider(IProceduralOffsetProvider provider)
    {
        _providers.Remove(provider);
    }

    public override void Update(float dt)
    {
        if (World == null) return;

        ProceduralOffset totalOffset = default;

        // Update providers and accumulate offsets
        foreach (var provider in _providers)
        {
            if (provider.IsActive)
            {
                provider.Update(dt);
                var offset = provider.GetOffset();
                
                totalOffset.PositionDelta += offset.PositionDelta;
                totalOffset.RotationDelta += offset.RotationDelta;
                totalOffset.FOVDelta += offset.FOVDelta;
            }
        }

        // Apply to active camera (simplified - normally you'd target a specific entity)
        World.ForEach<CameraComponent, TransformComponent>((entity, camera, transform) =>
        {
            // Apply FOV delta (assuming camera has a FOV property we can modify or add)
            camera.FovOffset = totalOffset.FOVDelta; 
            
            // Note: In a full engine, we'd apply these offsets non-destructively
            // e.g., to a "CameraOffsetComponent" so it doesn't overwrite the base transform.
            // For now, we apply them directly to the transform's procedural offset properties if they exist.
            
            // To be robust, we introduce a new concept or just modify the TransformComponent temporarily
            // Let's assume TransformComponent can handle local procedural offsets.
            // This is a placeholder for where the actual integration happens.
            
            World.AddComponent(entity, camera);
        });
    }
}

// Note: Extending CameraComponent for the FovOffset
public partial struct CameraComponent
{
    public float FovOffset; // Requires manual integration into the renderer's projection matrix computation
}
