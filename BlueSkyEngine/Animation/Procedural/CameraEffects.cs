using System;
using System.Numerics;

namespace BlueSky.Animation.Procedural;

/// <summary>
/// Provides trauma-based camera shake, dynamic tilt, and FOV kick.
/// </summary>
public class CameraEffects : IProceduralOffsetProvider
{
    public bool IsActive => _trauma > 0.01f || Math.Abs(_currentTilt) > 0.01f || Math.Abs(_currentFOVKick) > 0.1f;

    // Shake
    private float _trauma = 0.0f;
    private float _time = 0.0f;
    
    public float TraumaDecayRate { get; set; } = 2.5f;
    public float ShakeFrequency { get; set; } = 20.0f;
    public Vector3 MaxShakeTranslation { get; set; } = new Vector3(0.5f, 0.5f, 0.5f);
    public Vector3 MaxShakeRotation { get; set; } = new Vector3(0.1f, 0.1f, 0.1f); // Radians

    // Tilt
    private float _targetTilt = 0.0f;
    private float _currentTilt = 0.0f;
    private float _tiltVelocity = 0.0f;
    public float TiltSmoothTime { get; set; } = 0.1f;

    // FOV Kick
    private float _targetFOVKick = 0.0f;
    private float _currentFOVKick = 0.0f;
    private float _fovVelocity = 0.0f;
    public float FOVSmoothTime { get; set; } = 0.15f;

    public void AddTrauma(float amount)
    {
        _trauma = Math.Clamp(_trauma + amount, 0.0f, 1.0f);
    }

    public void SetTilt(float tiltAngleRadians)
    {
        _targetTilt = tiltAngleRadians;
    }

    public void SetFOVKick(float fovKickDegrees)
    {
        _targetFOVKick = fovKickDegrees;
    }

    public void Update(float deltaTime)
    {
        _time += deltaTime;

        // Decay trauma
        if (_trauma > 0)
        {
            _trauma = AnimationCurves.ExponentialDecay(_trauma, TraumaDecayRate, deltaTime);
            if (_trauma < 0.01f) _trauma = 0;
        }

        // Smooth tilt
        _currentTilt = AnimationCurves.SmoothDamp(_currentTilt, _targetTilt, ref _tiltVelocity, TiltSmoothTime, float.PositiveInfinity, deltaTime);

        // Smooth FOV
        _currentFOVKick = AnimationCurves.SmoothDamp(_currentFOVKick, _targetFOVKick, ref _fovVelocity, FOVSmoothTime, float.PositiveInfinity, deltaTime);
    }

    public ProceduralOffset GetOffset()
    {
        ProceduralOffset offset = new ProceduralOffset();

        // Calculate Shake
        if (_trauma > 0)
        {
            float shake = _trauma * _trauma; // Trauma squared

            // Perlin noise for each axis, offset in time to uncorrelate them
            float nx = AnimationCurves.ValueNoise1D(_time, ShakeFrequency);
            float ny = AnimationCurves.ValueNoise1D(_time + 100f, ShakeFrequency);
            float nz = AnimationCurves.ValueNoise1D(_time + 200f, ShakeFrequency);

            offset.PositionDelta.X = MaxShakeTranslation.X * shake * nx;
            offset.PositionDelta.Y = MaxShakeTranslation.Y * shake * ny;
            offset.PositionDelta.Z = MaxShakeTranslation.Z * shake * nz;

            float rx = AnimationCurves.ValueNoise1D(_time + 300f, ShakeFrequency);
            float ry = AnimationCurves.ValueNoise1D(_time + 400f, ShakeFrequency);
            float rz = AnimationCurves.ValueNoise1D(_time + 500f, ShakeFrequency);

            offset.RotationDelta.X = MaxShakeRotation.X * shake * rx; // Pitch
            offset.RotationDelta.Y = MaxShakeRotation.Y * shake * ry; // Yaw
            offset.RotationDelta.Z = MaxShakeRotation.Z * shake * rz; // Roll
        }

        // Apply Tilt
        offset.RotationDelta.Z += _currentTilt; // Roll

        // Apply FOV
        offset.FOVDelta += _currentFOVKick;

        return offset;
    }
}
