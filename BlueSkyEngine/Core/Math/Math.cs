using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace BlueSky.Core.Math
{
    /// <summary>Common math utility functions for game development.</summary>
    public static class BlueMath
    {
        public const float PI = MathF.PI;
        public const float TAU = MathF.PI * 2f;
        public const float Deg2Rad = MathF.PI / 180f;
        public const float Rad2Deg = 180f / MathF.PI;
        public const float Epsilon = 1e-6f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float InverseLerp(float a, float b, float value) => (System.Math.Abs(b - a) > Epsilon) ? Clamp01((value - a) / (b - a)) : 0f;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp(float value, float min, float max) => MathF.Max(min, MathF.Min(max, value));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp01(float value) => Clamp(value, 0f, 1f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (MathF.Abs(target - current) <= maxDelta) return target;
            return current + MathF.Sign(target - current) * maxDelta;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Remap(float value, float fromMin, float fromMax, float toMin, float toMax)
            => Lerp(toMin, toMax, InverseLerp(fromMin, fromMax, value));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorToInt(float v) => (int)MathF.Floor(v);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CeilToInt(float v) => (int)MathF.Ceiling(v);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RoundToInt(float v) => (int)MathF.Round(v);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastInvSqrt(float x)
        {
            // Quake-style fast inverse sqrt with one Newton-Raphson iteration
            float half = 0.5f * x;
            int i = BitConverter.SingleToInt32Bits(x);
            i = 0x5f3759df - (i >> 1);
            x = BitConverter.Int32BitsToSingle(i);
            x *= (1.5f - half * x * x);
            return x;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Repeat(float t, float length) => Clamp(t - MathF.Floor(t / length) * length, 0f, length);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float PingPong(float t, float length)
        {
            t = Repeat(t, length * 2f);
            return length - MathF.Abs(t - length);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DeltaAngle(float current, float target)
        {
            float delta = Repeat(target - current, 360f);
            if (delta > 180f) delta -= 360f;
            return delta;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int NextPowerOfTwo(int v) { v--; v |= v >> 1; v |= v >> 2; v |= v >> 4; v |= v >> 8; v |= v >> 16; return v + 1; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPowerOfTwo(int v) => v > 0 && (v & (v - 1)) == 0;
    }

    // ────────────────────────────────────────────────────────────────────
    // Vector2
    // ────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Vector2 : IEquatable<Vector2>
    {
        public readonly float X, Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2(float x, float y) => (X, Y) = (x, y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2(float value) => (X, Y) = (value, value);

        public static Vector2 Zero => new(0, 0);
        public static Vector2 One => new(1, 1);
        public static Vector2 Up => new(0, 1);
        public static Vector2 Right => new(1, 0);

        public float LengthSquared { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => X * X + Y * Y; }
        public float Length { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => MathF.Sqrt(LengthSquared); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector2 operator -(Vector2 a) => new(-a.X, -a.Y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector2 operator *(Vector2 a, float s) => new(a.X * s, a.Y * s);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector2 operator *(float s, Vector2 a) => a * s;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector2 operator *(Vector2 a, Vector2 b) => new(a.X * b.X, a.Y * b.Y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector2 operator /(Vector2 a, float s) => new(a.X / s, a.Y / s);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool operator ==(Vector2 a, Vector2 b) => a.Equals(b);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool operator !=(Vector2 a, Vector2 b) => !a.Equals(b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Dot(Vector2 a, Vector2 b) => a.X * b.X + a.Y * b.Y;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Distance(Vector2 a, Vector2 b) => (a - b).Length;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float DistanceSquared(Vector2 a, Vector2 b) => (a - b).LengthSquared;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => new(BlueMath.Lerp(a.X, b.X, t), BlueMath.Lerp(a.Y, b.Y, t));
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector2 Min(Vector2 a, Vector2 b) => new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y));
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector2 Max(Vector2 a, Vector2 b) => new(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector2 Abs(Vector2 a) => new(MathF.Abs(a.X), MathF.Abs(a.Y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2 Normalize() { float l = Length; return l > 0 ? this / l : Zero; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public bool Equals(Vector2 o) => X == o.X && Y == o.Y;
        public override bool Equals(object? obj) => obj is Vector2 o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X:F2}, {Y:F2})";
    }

    // ────────────────────────────────────────────────────────────────────
    // Vector2i (integer)
    // ────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Vector2i : IEquatable<Vector2i>
    {
        public readonly int X, Y;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector2i(int x, int y) => (X, Y) = (x, y);
        public static Vector2i Zero => new(0, 0);
        public static Vector2i One => new(1, 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector2i operator +(Vector2i a, Vector2i b) => new(a.X + b.X, a.Y + b.Y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector2i operator -(Vector2i a, Vector2i b) => new(a.X - b.X, a.Y - b.Y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector2i operator *(Vector2i a, int s) => new(a.X * s, a.Y * s);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector2 ToVector2() => new(X, Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public bool Equals(Vector2i o) => X == o.X && Y == o.Y;
        public override bool Equals(object? obj) => obj is Vector2i o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X}, {Y})";
    }

    // ────────────────────────────────────────────────────────────────────
    // Vector3
    // ────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Vector3 : IEquatable<Vector3>
    {
        public readonly float X, Y, Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector3(float x, float y, float z) => (X, Y, Z) = (x, y, z);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector3(float value) => (X, Y, Z) = (value, value, value);

        public static Vector3 Zero => new(0, 0, 0);
        public static Vector3 One => new(1, 1, 1);
        public static Vector3 Up => new(0, 1, 0);
        public static Vector3 Down => new(0, -1, 0);
        public static Vector3 Right => new(1, 0, 0);
        public static Vector3 Left => new(-1, 0, 0);
        public static Vector3 Forward => new(0, 0, -1);
        public static Vector3 Back => new(0, 0, 1);

        public float LengthSquared { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => X * X + Y * Y + Z * Z; }
        public float Length { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => MathF.Sqrt(LengthSquared); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 operator -(Vector3 a) => new(-a.X, -a.Y, -a.Z);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 operator *(Vector3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 operator *(float s, Vector3 a) => a * s;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 operator *(Vector3 a, Vector3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 operator /(Vector3 a, float s) => new(a.X / s, a.Y / s, a.Z / s);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool operator ==(Vector3 a, Vector3 b) => a.Equals(b);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool operator !=(Vector3 a, Vector3 b) => !a.Equals(b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Cross(Vector3 a, Vector3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Distance(Vector3 a, Vector3 b) => (a - b).Length;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float DistanceSquared(Vector3 a, Vector3 b) => (a - b).LengthSquared;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new(BlueMath.Lerp(a.X, b.X, t), BlueMath.Lerp(a.Y, b.Y, t), BlueMath.Lerp(a.Z, b.Z, t));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDelta)
        {
            var delta = target - current;
            float dist = delta.Length;
            if (dist <= maxDelta || dist < BlueMath.Epsilon) return target;
            return current + delta / dist * maxDelta;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Reflect(Vector3 dir, Vector3 normal) => dir - 2f * Dot(dir, normal) * normal;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 ProjectOnPlane(Vector3 vec, Vector3 normal) => vec - normal * Dot(vec, normal);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Min(Vector3 a, Vector3 b) => new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Min(a.Z, b.Z));
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Max(Vector3 a, Vector3 b) => new(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Max(a.Z, b.Z));
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Abs(Vector3 a) => new(MathF.Abs(a.X), MathF.Abs(a.Y), MathF.Abs(a.Z));
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Clamp(Vector3 v, Vector3 min, Vector3 max) => Min(Max(v, min), max);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Angle(Vector3 from, Vector3 to)
        {
            float d = MathF.Sqrt(from.LengthSquared * to.LengthSquared);
            return d < BlueMath.Epsilon ? 0 : MathF.Acos(BlueMath.Clamp(Dot(from, to) / d, -1f, 1f)) * BlueMath.Rad2Deg;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 Normalize() { float l = Length; return l > 0 ? this / l : Zero; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 NormalizeFast() { float invLen = BlueMath.FastInvSqrt(LengthSquared); return new(X * invLen, Y * invLen, Z * invLen); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public bool Equals(Vector3 o) => X == o.X && Y == o.Y && Z == o.Z;
        public override bool Equals(object? obj) => obj is Vector3 o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
    }

    // ────────────────────────────────────────────────────────────────────
    // Vector3i (integer)
    // ────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Vector3i : IEquatable<Vector3i>
    {
        public readonly int X, Y, Z;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector3i(int x, int y, int z) => (X, Y, Z) = (x, y, z);
        public static Vector3i Zero => new(0, 0, 0);
        public static Vector3i One => new(1, 1, 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3i operator +(Vector3i a, Vector3i b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3i operator -(Vector3i a, Vector3i b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3i operator *(Vector3i a, int s) => new(a.X * s, a.Y * s, a.Z * s);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector3 ToVector3() => new(X, Y, Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public bool Equals(Vector3i o) => X == o.X && Y == o.Y && Z == o.Z;
        public override bool Equals(object? obj) => obj is Vector3i o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    // ────────────────────────────────────────────────────────────────────
    // Vector4
    // ────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Vector4 : IEquatable<Vector4>
    {
        public readonly float X, Y, Z, W;

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector4(float x, float y, float z, float w) => (X, Y, Z, W) = (x, y, z, w);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector4(Vector3 v, float w) => (X, Y, Z, W) = (v.X, v.Y, v.Z, w);

        public static Vector4 Zero => new(0, 0, 0, 0);
        public static Vector4 One => new(1, 1, 1, 1);
        public Vector3 XYZ { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new(X, Y, Z); }
        public float LengthSquared { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => X * X + Y * Y + Z * Z + W * W; }
        public float Length { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => MathF.Sqrt(LengthSquared); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Vector4 operator +(Vector4 a, Vector4 b)
        {
            if (Vector128.IsHardwareAccelerated) { var va = Unsafe.As<Vector4, Vector128<float>>(ref a); var vb = Unsafe.As<Vector4, Vector128<float>>(ref b); var vr = va + vb; return Unsafe.As<Vector128<float>, Vector4>(ref vr); }
            return new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Vector4 operator -(Vector4 a, Vector4 b)
        {
            if (Vector128.IsHardwareAccelerated) { var va = Unsafe.As<Vector4, Vector128<float>>(ref a); var vb = Unsafe.As<Vector4, Vector128<float>>(ref b); var vr = va - vb; return Unsafe.As<Vector128<float>, Vector4>(ref vr); }
            return new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Vector4 operator *(Vector4 a, float s)
        {
            if (Vector128.IsHardwareAccelerated) { var va = Unsafe.As<Vector4, Vector128<float>>(ref a); var vb = Vector128.Create(s); var vr = va * vb; return Unsafe.As<Vector128<float>, Vector4>(ref vr); }
            return new(a.X * s, a.Y * s, a.Z * s, a.W * s);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Vector4 operator /(Vector4 a, float s)
        {
            if (Vector128.IsHardwareAccelerated) { var va = Unsafe.As<Vector4, Vector128<float>>(ref a); var vb = Vector128.Create(s); var vr = va / vb; return Unsafe.As<Vector128<float>, Vector4>(ref vr); }
            return new(a.X / s, a.Y / s, a.Z / s, a.W / s);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static float Dot(Vector4 a, Vector4 b)
        {
            if (Vector128.IsHardwareAccelerated) { var va = Unsafe.As<Vector4, Vector128<float>>(ref a); var vb = Unsafe.As<Vector4, Vector128<float>>(ref b); return Vector128.Dot(va, vb); }
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector4 Lerp(Vector4 a, Vector4 b, float t) => new(BlueMath.Lerp(a.X, b.X, t), BlueMath.Lerp(a.Y, b.Y, t), BlueMath.Lerp(a.Z, b.Z, t), BlueMath.Lerp(a.W, b.W, t));
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector4 Normalize() { float l = Length; return l > 0 ? this / l : Zero; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public bool Equals(Vector4 o) => X == o.X && Y == o.Y && Z == o.Z && W == o.W;
        public override bool Equals(object? obj) => obj is Vector4 o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
        public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2}, {W:F2})";
    }

    // ────────────────────────────────────────────────────────────────────
    // Color
    // ────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Color : IEquatable<Color>
    {
        public readonly float R, G, B, A;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Color(float r, float g, float b, float a = 1f) => (R, G, B, A) = (r, g, b, a);

        public static Color White => new(1, 1, 1, 1);
        public static Color Black => new(0, 0, 0, 1);
        public static Color Red => new(1, 0, 0, 1);
        public static Color Green => new(0, 1, 0, 1);
        public static Color Blue => new(0, 0, 1, 1);
        public static Color Yellow => new(1, 1, 0, 1);
        public static Color Cyan => new(0, 1, 1, 1);
        public static Color Magenta => new(1, 0, 1, 1);
        public static Color Clear => new(0, 0, 0, 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color FromHex(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length >= 6)
            {
                int r = Convert.ToInt32(hex[..2], 16);
                int g = Convert.ToInt32(hex[2..4], 16);
                int b = Convert.ToInt32(hex[4..6], 16);
                int a = hex.Length >= 8 ? Convert.ToInt32(hex[6..8], 16) : 255;
                return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            }
            return White;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color FromHSV(float h, float s, float v, float a = 1f)
        {
            // Clamp h to [0,1) so that (int)(h*6f) % 6 is never negative
            h = h - MathF.Floor(h); // wraps negatives correctly
            float c = v * s, x = c * (1 - MathF.Abs(h * 6f % 2 - 1)), m = v - c;
            int sector = (int)(h * 6f) % 6;
            return sector switch
            {
                0 => new(c + m, x + m, m, a), 1 => new(x + m, c + m, m, a),
                2 => new(m, c + m, x + m, a), 3 => new(m, x + m, c + m, a),
                4 => new(x + m, m, c + m, a), _ => new(c + m, m, x + m, a),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Color Lerp(Color a, Color b, float t) => new(BlueMath.Lerp(a.R, b.R, t), BlueMath.Lerp(a.G, b.G, t), BlueMath.Lerp(a.B, b.B, t), BlueMath.Lerp(a.A, b.A, t));
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector4 ToVector4() => new(R, G, B, A);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public Vector3 ToVector3() => new(R, G, B);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public uint ToRGBA8() => ((uint)(R * 255) << 24) | ((uint)(G * 255) << 16) | ((uint)(B * 255) << 8) | (uint)(A * 255);

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public bool Equals(Color o) => R == o.R && G == o.G && B == o.B && A == o.A;
        public override bool Equals(object? obj) => obj is Color o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(R, G, B, A);
        public override string ToString() => $"Color({R:F2}, {G:F2}, {B:F2}, {A:F2})";
    }

    // ────────────────────────────────────────────────────────────────────
    // Quaternion
    // ────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Quaternion : IEquatable<Quaternion>
    {
        public readonly float X, Y, Z, W;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion(float x, float y, float z, float w) => (X, Y, Z, W) = (x, y, z, w);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion(Vector3 axis, float angle)
        {
            axis = axis.Normalize();
            float h = angle * 0.5f, s = MathF.Sin(h);
            X = axis.X * s; Y = axis.Y * s; Z = axis.Z * s; W = MathF.Cos(h);
        }

        public static Quaternion Identity => new(0, 0, 0, 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Euler(float pitchDeg, float yawDeg, float rollDeg)
        {
            float p = pitchDeg * BlueMath.Deg2Rad * 0.5f, y = yawDeg * BlueMath.Deg2Rad * 0.5f, r = rollDeg * BlueMath.Deg2Rad * 0.5f;
            float sp = MathF.Sin(p), cp = MathF.Cos(p), sy = MathF.Sin(y), cy = MathF.Cos(y), sr = MathF.Sin(r), cr = MathF.Cos(r);
            return new(cy * sp * cr + sy * cp * sr, sy * cp * cr - cy * sp * sr, cy * cp * sr - sy * sp * cr, cy * cp * cr + sy * sp * sr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            if (dot < 0) { b = new(-b.X, -b.Y, -b.Z, -b.W); dot = -dot; }
            if (dot > 0.9995f) return new Quaternion(BlueMath.LerpUnclamped(a.X, b.X, t), BlueMath.LerpUnclamped(a.Y, b.Y, t), BlueMath.LerpUnclamped(a.Z, b.Z, t), BlueMath.LerpUnclamped(a.W, b.W, t)).Normalize();
            float theta = MathF.Acos(dot), sinT = MathF.Sin(theta);
            float wa = MathF.Sin((1 - t) * theta) / sinT, wb = MathF.Sin(t * theta) / sinT;
            return new(wa * a.X + wb * b.X, wa * a.Y + wb * b.Y, wa * a.Z + wb * b.Z, wa * a.W + wb * b.W);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            var f = forward.Normalize();
            var r = Vector3.Cross(up, f).Normalize();
            var u = Vector3.Cross(f, r);
            float trace = r.X + u.Y + f.Z;
            if (trace > 0f)
            {
                float s = MathF.Sqrt(trace + 1f) * 2f;
                return new((u.Z - f.Y) / s, (f.X - r.Z) / s, (r.Y - u.X) / s, s / 4f);
            }
            if (r.X > u.Y && r.X > f.Z) { float s = MathF.Sqrt(1f + r.X - u.Y - f.Z) * 2f; return new(s / 4f, (r.Y + u.X) / s, (f.X + r.Z) / s, (u.Z - f.Y) / s); }
            if (u.Y > f.Z) { float s = MathF.Sqrt(1f + u.Y - r.X - f.Z) * 2f; return new((r.Y + u.X) / s, s / 4f, (u.Z + f.Y) / s, (f.X - r.Z) / s); }
            { float s = MathF.Sqrt(1f + f.Z - r.X - u.Y) * 2f; return new((f.X + r.Z) / s, (u.Z + f.Y) / s, s / 4f, (r.Y - u.X) / s); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public Quaternion Normalize() { float l = MathF.Sqrt(X * X + Y * Y + Z * Z + W * W); return l > 0 ? new(X / l, Y / l, Z / l, W / l) : Identity; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public Quaternion Inverse() => new(-X, -Y, -Z, W);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion operator *(Quaternion a, Quaternion b) => new(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            Vector3 qv = new(q.X, q.Y, q.Z);
            Vector3 uv = Vector3.Cross(qv, v);
            Vector3 uuv = Vector3.Cross(qv, uv);
            return v + (uv * q.W + uuv) * 2f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public bool Equals(Quaternion o) => X == o.X && Y == o.Y && Z == o.Z && W == o.W;
        public override bool Equals(object? obj) => obj is Quaternion o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
        public override string ToString() => $"Quat({X:F3}, {Y:F3}, {Z:F3}, {W:F3})";
    }

    // ────────────────────────────────────────────────────────────────────
    // Matrix4x4 — SIMD-optimized multiplication
    // ────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Matrix4x4 : IEquatable<Matrix4x4>
    {
        public readonly float M11, M12, M13, M14;
        public readonly float M21, M22, M23, M24;
        public readonly float M31, M32, M33, M34;
        public readonly float M41, M42, M43, M44;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Matrix4x4(float m11, float m12, float m13, float m14, float m21, float m22, float m23, float m24, float m31, float m32, float m33, float m34, float m41, float m42, float m43, float m44)
        {
            (M11, M12, M13, M14) = (m11, m12, m13, m14);
            (M21, M22, M23, M24) = (m21, m22, m23, m24);
            (M31, M32, M33, M34) = (m31, m32, m33, m34);
            (M41, M42, M43, M44) = (m41, m42, m43, m44);
        }

        public static Matrix4x4 Identity => new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Matrix4x4 CreateTranslation(Vector3 p) => new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, p.X, p.Y, p.Z, 1);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Matrix4x4 CreateScale(Vector3 s) => new(s.X, 0, 0, 0, 0, s.Y, 0, 0, 0, 0, s.Z, 0, 0, 0, 0, 1);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Matrix4x4 CreateScale(float s) => CreateScale(new Vector3(s));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 CreateRotation(Quaternion r)
        {
            float xx = r.X * r.X, yy = r.Y * r.Y, zz = r.Z * r.Z;
            float xy = r.X * r.Y, zw = r.Z * r.W, zx = r.Z * r.X;
            float yw = r.Y * r.W, yz = r.Y * r.Z, xw = r.X * r.W;
            return new(1 - 2 * (yy + zz), 2 * (xy + zw), 2 * (zx - yw), 0,
                       2 * (xy - zw), 1 - 2 * (zz + xx), 2 * (yz + xw), 0,
                       2 * (zx + yw), 2 * (yz - xw), 1 - 2 * (xx + yy), 0, 0, 0, 0, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 CreatePerspective(float fov, float aspect, float near, float far)
        {
            float yS = 1f / MathF.Tan(fov * 0.5f), xS = yS / aspect;
            return new(xS, 0, 0, 0, 0, yS, 0, 0, 0, 0, far / (far - near), 1, 0, 0, -near * far / (far - near), 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 CreateOrthographic(float w, float h, float near, float far)
            => new(2f / w, 0, 0, 0, 0, 2f / h, 0, 0, 0, 0, 1f / (far - near), 0, 0, 0, -near / (far - near), 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 CreateLookAt(Vector3 eye, Vector3 target, Vector3 up)
        {
            Vector3 z = (eye - target).Normalize(), x = Vector3.Cross(up, z).Normalize(), y = Vector3.Cross(z, x);
            return new(x.X, y.X, z.X, 0, x.Y, y.Y, z.Y, 0, x.Z, y.Z, z.Z, 0,
                       -Vector3.Dot(x, eye), -Vector3.Dot(y, eye), -Vector3.Dot(z, eye), 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 CreateTRS(Vector3 t, Quaternion r, Vector3 s) => CreateScale(s) * CreateRotation(r) * CreateTranslation(t);

        /// <summary>SIMD-optimized 4x4 matrix multiply using Vector128 when available.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b)
        {
            if (Vector128.IsHardwareAccelerated)
            {
                // Load rows of B
                var bRow0 = Vector128.Create(b.M11, b.M12, b.M13, b.M14);
                var bRow1 = Vector128.Create(b.M21, b.M22, b.M23, b.M24);
                var bRow2 = Vector128.Create(b.M31, b.M32, b.M33, b.M34);
                var bRow3 = Vector128.Create(b.M41, b.M42, b.M43, b.M44);

                // Row 0 = a.M1x * bRows
                var r0 = Vector128.Create(a.M11) * bRow0 + Vector128.Create(a.M12) * bRow1 + Vector128.Create(a.M13) * bRow2 + Vector128.Create(a.M14) * bRow3;
                var r1 = Vector128.Create(a.M21) * bRow0 + Vector128.Create(a.M22) * bRow1 + Vector128.Create(a.M23) * bRow2 + Vector128.Create(a.M24) * bRow3;
                var r2 = Vector128.Create(a.M31) * bRow0 + Vector128.Create(a.M32) * bRow1 + Vector128.Create(a.M33) * bRow2 + Vector128.Create(a.M34) * bRow3;
                var r3 = Vector128.Create(a.M41) * bRow0 + Vector128.Create(a.M42) * bRow1 + Vector128.Create(a.M43) * bRow2 + Vector128.Create(a.M44) * bRow3;

                return new(
                    r0[0], r0[1], r0[2], r0[3],
                    r1[0], r1[1], r1[2], r1[3],
                    r2[0], r2[1], r2[2], r2[3],
                    r3[0], r3[1], r3[2], r3[3]);
            }

            // Scalar fallback
            return new(
                a.M11*b.M11+a.M12*b.M21+a.M13*b.M31+a.M14*b.M41, a.M11*b.M12+a.M12*b.M22+a.M13*b.M32+a.M14*b.M42, a.M11*b.M13+a.M12*b.M23+a.M13*b.M33+a.M14*b.M43, a.M11*b.M14+a.M12*b.M24+a.M13*b.M34+a.M14*b.M44,
                a.M21*b.M11+a.M22*b.M21+a.M23*b.M31+a.M24*b.M41, a.M21*b.M12+a.M22*b.M22+a.M23*b.M32+a.M24*b.M42, a.M21*b.M13+a.M22*b.M23+a.M23*b.M33+a.M24*b.M43, a.M21*b.M14+a.M22*b.M24+a.M23*b.M34+a.M24*b.M44,
                a.M31*b.M11+a.M32*b.M21+a.M33*b.M31+a.M34*b.M41, a.M31*b.M12+a.M32*b.M22+a.M33*b.M32+a.M34*b.M42, a.M31*b.M13+a.M32*b.M23+a.M33*b.M33+a.M34*b.M43, a.M31*b.M14+a.M32*b.M24+a.M33*b.M34+a.M34*b.M44,
                a.M41*b.M11+a.M42*b.M21+a.M43*b.M31+a.M44*b.M41, a.M41*b.M12+a.M42*b.M22+a.M43*b.M32+a.M44*b.M42, a.M41*b.M13+a.M42*b.M23+a.M43*b.M33+a.M44*b.M43, a.M41*b.M14+a.M42*b.M24+a.M43*b.M34+a.M44*b.M44);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 operator *(Matrix4x4 m, Vector4 v)
        {
            if (Vector128.IsHardwareAccelerated)
            {
                var vx = Vector128.Create(v.X);
                var vy = Vector128.Create(v.Y);
                var vz = Vector128.Create(v.Z);
                var vw = Vector128.Create(v.W);

                var r0 = Vector128.Create(m.M11, m.M21, m.M31, m.M41);
                var r1 = Vector128.Create(m.M12, m.M22, m.M32, m.M42);
                var r2 = Vector128.Create(m.M13, m.M23, m.M33, m.M43);
                var r3 = Vector128.Create(m.M14, m.M24, m.M34, m.M44);

                var res = vx * r0 + vy * r1 + vz * r2 + vw * r3;
                return new(res[0], res[1], res[2], res[3]);
            }
            return new(
                m.M11 * v.X + m.M12 * v.Y + m.M13 * v.Z + m.M14 * v.W,
                m.M21 * v.X + m.M22 * v.Y + m.M23 * v.Z + m.M24 * v.W,
                m.M31 * v.X + m.M32 * v.Y + m.M33 * v.Z + m.M34 * v.W,
                m.M41 * v.X + m.M42 * v.Y + m.M43 * v.Z + m.M44 * v.W);
        }

        public bool Equals(Matrix4x4 o) =>
            M11==o.M11 && M12==o.M12 && M13==o.M13 && M14==o.M14 &&
            M21==o.M21 && M22==o.M22 && M23==o.M23 && M24==o.M24 &&
            M31==o.M31 && M32==o.M32 && M33==o.M33 && M34==o.M34 &&
            M41==o.M41 && M42==o.M42 && M43==o.M43 && M44==o.M44;
        public override bool Equals(object? obj) => obj is Matrix4x4 o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(
            HashCode.Combine(M11, M12, M13, M14), HashCode.Combine(M21, M22, M23, M24),
            HashCode.Combine(M31, M32, M33, M34), HashCode.Combine(M41, M42, M43, M44));
    }

    // ────────────────────────────────────────────────────────────────────
    // Plane
    // ────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Plane : IEquatable<Plane>
    {
        public readonly float A, B, C, D;

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public Plane(float a, float b, float c, float d) => (A, B, C, D) = (a, b, c, d);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Plane(Vector3 normal, float distance) { var n = normal.Normalize(); (A, B, C, D) = (n.X, n.Y, n.Z, distance); }

        public Vector3 Normal => new(A, B, C);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Plane Normalize(Plane plane)
        {
            float length = MathF.Sqrt(plane.A * plane.A + plane.B * plane.B + plane.C * plane.C);
            if (length < BlueMath.Epsilon) return plane;
            return new Plane(plane.A / length, plane.B / length, plane.C / length, plane.D / length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DotCoordinate(Plane plane, Vector3 point) => plane.A * point.X + plane.B * point.Y + plane.C * point.Z + plane.D;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DotNormal(Plane plane, Vector3 normal) => plane.A * normal.X + plane.B * normal.Y + plane.C * normal.Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public bool Equals(Plane o) => A == o.A && B == o.B && C == o.C && D == o.D;
        public override bool Equals(object? obj) => obj is Plane o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(A, B, C, D);
        public override string ToString() => $"Plane({A:F2}, {B:F2}, {C:F2}, {D:F2})";
    }
}
