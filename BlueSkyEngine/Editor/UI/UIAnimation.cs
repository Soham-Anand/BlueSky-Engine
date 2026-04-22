using System;
using System.Numerics;

namespace BlueSky.Editor.UI;

/// <summary>
/// Easing functions for smooth animations
/// </summary>
public static class Easing
{
    public static float Linear(float t) => t;
    
    public static float EaseInQuad(float t) => t * t;
    public static float EaseOutQuad(float t) => t * (2 - t);
    public static float EaseInOutQuad(float t) => t < 0.5f ? 2 * t * t : -1 + (4 - 2 * t) * t;
    
    public static float EaseInCubic(float t) => t * t * t;
    public static float EaseOutCubic(float t) => (--t) * t * t + 1;
    public static float EaseInOutCubic(float t) => t < 0.5f ? 4 * t * t * t : (t - 1) * (2 * t - 2) * (2 * t - 2) + 1;
    
    public static float EaseInQuart(float t) => t * t * t * t;
    public static float EaseOutQuart(float t) => 1 - (--t) * t * t * t;
    public static float EaseInOutQuart(float t) => t < 0.5f ? 8 * t * t * t * t : 1 - 8 * (--t) * t * t * t;
    
    public static float EaseInExpo(float t) => t == 0 ? 0 : MathF.Pow(2, 10 * (t - 1));
    public static float EaseOutExpo(float t) => t == 1 ? 1 : 1 - MathF.Pow(2, -10 * t);
    
    public static float EaseInBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1;
        return c3 * t * t * t - c1 * t * t;
    }
    
    public static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1;
        return 1 + c3 * MathF.Pow(t - 1, 3) + c1 * MathF.Pow(t - 1, 2);
    }
    
    public static float EaseInOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c2 = c1 * 1.525f;
        
        return t < 0.5f
            ? (MathF.Pow(2 * t, 2) * ((c2 + 1) * 2 * t - c2)) / 2
            : (MathF.Pow(2 * t - 2, 2) * ((c2 + 1) * (t * 2 - 2) + c2) + 2) / 2;
    }
    
    public static float EaseOutElastic(float t)
    {
        const float c4 = (2 * MathF.PI) / 3;
        
        return t == 0 ? 0 : t == 1 ? 1 : MathF.Pow(2, -10 * t) * MathF.Sin((t * 10 - 0.75f) * c4) + 1;
    }
    
    public static float Spring(float t)
    {
        return 1 - MathF.Cos(t * MathF.PI * 2.5f) * MathF.Pow(1 - t, 2);
    }
}

/// <summary>
/// Animated value that smoothly transitions between states
/// </summary>
public class AnimatedFloat
{
    private float _current;
    private float _target;
    private float _velocity;
    private readonly float _smoothTime;
    
    public float Current => _current;
    public float Target => _target;
    
    public AnimatedFloat(float initial = 0f, float smoothTime = 0.15f)
    {
        _current = initial;
        _target = initial;
        _velocity = 0f;
        _smoothTime = smoothTime;
    }
    
    public void SetTarget(float target)
    {
        _target = target;
    }
    
    public void SetImmediate(float value)
    {
        _current = value;
        _target = value;
        _velocity = 0f;
    }
    
    public void Update(float deltaTime)
    {
        _current = SmoothDamp(_current, _target, ref _velocity, _smoothTime, deltaTime);
    }
    
    private static float SmoothDamp(float current, float target, ref float velocity, float smoothTime, float deltaTime)
    {
        smoothTime = MathF.Max(0.0001f, smoothTime);
        float omega = 2f / smoothTime;
        float x = omega * deltaTime;
        float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        float change = current - target;
        float temp = (velocity + omega * change) * deltaTime;
        velocity = (velocity - omega * temp) * exp;
        return target + (change + temp) * exp;
    }
}

/// <summary>
/// Animated color that smoothly transitions
/// </summary>
public class AnimatedColor
{
    private Vector4 _current;
    private Vector4 _target;
    private readonly float _smoothTime;
    
    public Vector4 Current => _current;
    public Vector4 Target => _target;
    
    public AnimatedColor(Vector4 initial, float smoothTime = 0.1f)
    {
        _current = initial;
        _target = initial;
        _smoothTime = smoothTime;
    }
    
    public void SetTarget(Vector4 target)
    {
        _target = target;
    }
    
    public void SetImmediate(Vector4 value)
    {
        _current = value;
        _target = value;
    }
    
    public void Update(float deltaTime)
    {
        _current = Vector4.Lerp(_current, _target, MathF.Min(1f, deltaTime / _smoothTime));
    }
}

/// <summary>
/// Simple tween animation
/// </summary>
public class Tween
{
    private float _elapsed;
    private readonly float _duration;
    private readonly Func<float, float> _easing;
    
    public bool IsComplete => _elapsed >= _duration;
    public float Progress => MathF.Min(1f, _elapsed / _duration);
    public float EasedProgress => _easing(Progress);
    
    public Tween(float duration, Func<float, float>? easing = null)
    {
        _duration = duration;
        _easing = easing ?? Easing.EaseOutQuad;
        _elapsed = 0f;
    }
    
    public void Update(float deltaTime)
    {
        _elapsed += deltaTime;
    }
    
    public void Reset()
    {
        _elapsed = 0f;
    }
}

/// <summary>
/// UI element state tracker for animations
/// </summary>
public class UIElementState
{
    public AnimatedFloat HoverAmount { get; }
    public AnimatedFloat PressAmount { get; }
    public AnimatedFloat FocusAmount { get; }
    public AnimatedColor BackgroundColor { get; }
    public AnimatedFloat Scale { get; }
    public AnimatedFloat Opacity { get; }
    
    public bool IsHovered { get; set; }
    public bool IsPressed { get; set; }
    public bool IsFocused { get; set; }
    
    public UIElementState()
    {
        HoverAmount = new AnimatedFloat(0f, 0.12f);
        PressAmount = new AnimatedFloat(0f, 0.08f);
        FocusAmount = new AnimatedFloat(0f, 0.15f);
        BackgroundColor = new AnimatedColor(Vector4.Zero, 0.1f);
        Scale = new AnimatedFloat(1f, 0.1f);
        Opacity = new AnimatedFloat(1f, 0.15f);
    }
    
    public void Update(float deltaTime)
    {
        HoverAmount.SetTarget(IsHovered ? 1f : 0f);
        PressAmount.SetTarget(IsPressed ? 1f : 0f);
        FocusAmount.SetTarget(IsFocused ? 1f : 0f);
        
        HoverAmount.Update(deltaTime);
        PressAmount.Update(deltaTime);
        FocusAmount.Update(deltaTime);
        BackgroundColor.Update(deltaTime);
        Scale.Update(deltaTime);
        Opacity.Update(deltaTime);
    }
}
