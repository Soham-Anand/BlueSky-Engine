using System;
using System.Numerics;

namespace BlueSky.Audio;

public enum DistanceAttenuationCurve
{
    Linear,
    Inverse,
    Logarithmic
}

/// <summary>
/// Enhanced 3D audio processor for spatial audio features like Doppler, advanced panning, and occlusion.
/// </summary>
public class SpatialAudio
{
    private Vector3 _listenerPosition;
    private Vector3 _listenerForward;
    private Vector3 _listenerUp;
    private Vector3 _listenerRight;
    private Vector3 _listenerVelocity;

    public void UpdateListener(Vector3 position, Vector3 forward, Vector3 up, Vector3 velocity)
    {
        _listenerPosition = position;
        _listenerForward = Vector3.Normalize(forward);
        _listenerUp = Vector3.Normalize(up);
        _listenerRight = Vector3.Cross(_listenerUp, _listenerForward);
        _listenerVelocity = velocity;
    }

    /// <summary>
    /// Processes spatial audio for a source, updating its calculated volume and pitch
    /// </summary>
    public void ProcessSource(AudioSource source, float deltaTime)
    {
        if (!source.Is3D) return;

        Vector3 sourceToListener = _listenerPosition - source.Position;
        float distance = sourceToListener.Length();

        // 1. Distance Attenuation
        float attenuation = CalculateAttenuation(distance, source.MinDistance, source.MaxDistance, DistanceAttenuationCurve.Linear); // Assuming linear for now, could add property to source
        
        // 2. Occlusion (Simulated with a raycast here, normally you'd query the physics engine)
        float occlusionMultiplier = CalculateOcclusion(source.Position, _listenerPosition);
        
        source.CalculatedVolume = source.Volume * attenuation * occlusionMultiplier;

        // 3. Stereo Panning (simplified)
        // Angle between listener right vector and direction to source
        if (distance > 0.01f)
        {
            Vector3 dirToSource = -sourceToListener / distance;
            float pan = Vector3.Dot(_listenerRight, dirToSource);
            source.Pan = pan; // Requires adding Pan property to AudioSource
        }
        else
        {
            source.Pan = 0.0f;
        }

        // 4. Doppler Effect
        // Approximating relative velocity if source velocity isn't tracked. 
        // For a full implementation, AudioSource needs a Velocity property.
        float speedOfSound = 343.0f; 
        Vector3 sourceVelocity = Vector3.Zero; // Assume static for now
        
        Vector3 relativeVelocity = _listenerVelocity - sourceVelocity;
        if (distance > 0.01f)
        {
            Vector3 dirToSource = -sourceToListener / distance;
            float approachSpeed = Vector3.Dot(relativeVelocity, dirToSource);
            
            // f' = f * (c + vr) / (c + vs)
            float dopplerFactor = (speedOfSound + approachSpeed) / speedOfSound;
            source.CalculatedPitch = source.Pitch * dopplerFactor; // Requires adding CalculatedPitch property
        }
        else
        {
            source.CalculatedPitch = source.Pitch;
        }
    }

    private float CalculateAttenuation(float distance, float minDistance, float maxDistance, DistanceAttenuationCurve curve)
    {
        if (distance <= minDistance) return 1.0f;
        if (distance >= maxDistance) return 0.0f;

        float normalizedDistance = (distance - minDistance) / (maxDistance - minDistance);

        return curve switch
        {
            DistanceAttenuationCurve.Linear => 1.0f - normalizedDistance,
            DistanceAttenuationCurve.Inverse => 1.0f / (1.0f + normalizedDistance * 9.0f), // simple inverse curve
            DistanceAttenuationCurve.Logarithmic => MathF.Max(0, 1.0f - MathF.Log10(1.0f + normalizedDistance * 9.0f)),
            _ => 1.0f - normalizedDistance
        };
    }

    private float CalculateOcclusion(Vector3 sourcePos, Vector3 listenerPos)
    {
        // TODO: Integrate with Jolt Physics for actual raycast
        // If raycast hits something that isn't the player, return < 1.0f
        return 1.0f;
    }
}
