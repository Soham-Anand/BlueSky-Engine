using System;

namespace BlueSky.Animation.Procedural;

/// <summary>
/// Utility class containing stateless math functions for procedural animations.
/// </summary>
public static class AnimationCurves
{
    /// <summary>
    /// Evaluates a sine wave.
    /// </summary>
    public static float SineWave(float time, float frequency, float amplitude)
    {
        return MathF.Sin(time * frequency * MathF.PI * 2.0f) * amplitude;
    }

    /// <summary>
    /// Exponential decay function, useful for trauma/shake falloff.
    /// </summary>
    public static float ExponentialDecay(float value, float rate, float deltaTime)
    {
        return value * MathF.Exp(-rate * deltaTime);
    }

    /// <summary>
    /// Smooth damp function (spring-damper) for smooth camera transitions.
    /// Based on Game Programming Gems 4.
    /// </summary>
    public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed, float deltaTime)
    {
        smoothTime = MathF.Max(0.0001f, smoothTime);
        float omega = 2.0f / smoothTime;
        
        float x = omega * deltaTime;
        float exp = 1.0f / (1.0f + x + 0.48f * x * x + 0.235f * x * x * x);
        
        float change = current - target;
        float originalTo = target;
        
        float maxChange = maxSpeed * smoothTime;
        change = Math.Clamp(change, -maxChange, maxChange);
        target = current - change;
        
        float temp = (currentVelocity + omega * change) * deltaTime;
        currentVelocity = (currentVelocity - omega * temp) * exp;
        
        float output = target + (change + temp) * exp;
        
        if (originalTo - current > 0.0f == output > originalTo)
        {
            output = originalTo;
            currentVelocity = (output - originalTo) / deltaTime;
        }
        
        return output;
    }

    /// <summary>
    /// Generates a pseudo-random value using a sine wave, useful for cheap 1D noise.
    /// </summary>
    public static float PseudoNoise(float x)
    {
        return MathF.Sin(x * 12.9898f) * 43758.5453f;
    }

    /// <summary>
    /// Returns the fractional part of a number.
    /// </summary>
    public static float Fract(float x)
    {
        return x - MathF.Floor(x);
    }

    /// <summary>
    /// Simple 1D value noise using pseudo-random generation.
    /// </summary>
    public static float ValueNoise1D(float time, float frequency)
    {
        float x = time * frequency;
        float i = MathF.Floor(x);
        float f = Fract(x);

        // Smoothstep interpolation
        float u = f * f * (3.0f - 2.0f * f);

        float n0 = Fract(PseudoNoise(i));
        float n1 = Fract(PseudoNoise(i + 1.0f));

        return Lerp(n0, n1, u) * 2.0f - 1.0f; // Range [-1, 1]
    }

    public static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }
}
