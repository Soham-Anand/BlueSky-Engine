using System;
using System.Numerics;
using BlueSky.Airborne;

namespace BlueSky.Echo;

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
    private Vector3 _listenerPosition = Vector3.Zero;
    private Vector3 _listenerForward = -Vector3.UnitZ;
    private Vector3 _listenerUp = Vector3.UnitY;
    private Vector3 _listenerRight = Vector3.UnitX;
    private Vector3 _listenerVelocity = Vector3.Zero;

    public void UpdateListener(Vector3 position, Vector3 forward, Vector3 up, Vector3 velocity)
    {
        _listenerPosition = position;
        _listenerForward = NormalizeOr(forward, -Vector3.UnitZ);
        _listenerUp = NormalizeOr(up, Vector3.UnitY);
        // BlueSky uses a right-handed basis with camera forward = -Z and up = +Y,
        // so right = forward x up (+X for the default camera). Using up x forward
        // here yields -X and mirrors every pan.
        _listenerRight = NormalizeOr(Vector3.Cross(_listenerForward, _listenerUp), Vector3.UnitX);
        _listenerUp = NormalizeOr(Vector3.Cross(_listenerRight, _listenerForward), Vector3.UnitY);
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
        float attenuation = CalculateAttenuation(
            distance, source.MinDistance, source.MaxDistance, source.AttenuationCurve);
        
        // 2. Occlusion (Simulated with a raycast here, normally you'd query the physics engine)
        float occlusionMultiplier = CalculateOcclusion(source.Position, _listenerPosition);
        
        source.CalculatedVolume = source.Volume * attenuation * occlusionMultiplier;

        // 3. Stereo Panning (simplified)
        // Angle between listener right vector and direction to source
        if (distance > 0.01f)
        {
            Vector3 dirToSource = -sourceToListener / distance;
            float pan = Vector3.Dot(_listenerRight, dirToSource);
            source.Pan = pan;
        }
        else
        {
            source.Pan = 0.0f;
        }

        // 4. Doppler Effect
        float speedOfSound = 343.0f; 
        Vector3 relativeVelocity = _listenerVelocity - source.Velocity;
        if (distance > 0.01f)
        {
            Vector3 dirToSource = -sourceToListener / distance;
            float approachSpeed = Vector3.Dot(relativeVelocity, dirToSource);
            
            // Clamp Doppler multiplier to [0.25, 4.0] so supersonic separation never produces negative pitch
            float dopplerFactor = Math.Clamp((speedOfSound + approachSpeed) / speedOfSound, 0.25f, 4.0f);
            source.CalculatedPitch = source.Pitch * dopplerFactor;
        }
        else
        {
            source.CalculatedPitch = source.Pitch;
        }
    }

    private float CalculateAttenuation(float distance, float minDistance, float maxDistance, DistanceAttenuationCurve curve)
    {
        minDistance = MathF.Max(0.0f, minDistance);
        maxDistance = MathF.Max(minDistance + 1e-4f, maxDistance);
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

    private static float CalculateOcclusion(Vector3 sourcePos, Vector3 listenerPos)
    {
        Vector3 toListener = listenerPos - sourcePos;
        float distance = toListener.Length();
        if (distance <= 1e-4f)
            return 1.0f;

        return PhysicsTeaScriptBridge.Raycast(sourcePos, toListener / distance, distance, out _)
            ? 0.25f
            : 1.0f;
    }

    private static Vector3 NormalizeOr(Vector3 value, Vector3 fallback) =>
        value.LengthSquared() > 1e-8f ? Vector3.Normalize(value) : fallback;
}
