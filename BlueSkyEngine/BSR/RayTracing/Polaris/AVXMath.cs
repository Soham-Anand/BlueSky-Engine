// BlueSkyEngine - Project Polaris: Ramanujan Cross-Platform 8-Wide SIMD Math
//
// HARDWARE-ADAPTIVE 8-WIDE SIMD (OLD & NEW PCs + APPLE SILICON)
// ==============================================================
// ✅ x86-64 AVX2 + FMA3 (Haswell / Ryzen / Core Ultra)
// ✅ x86-64 AVX1 (Sandy Bridge i5-2410M — 256-bit, no FMA3)
// ✅ ARM64 NEON / Vector256 (Apple Silicon M1/M2/M3/M4 via 2x128-bit Vector256)
// ✅ SSE2 / Scalar fallback
//
// All operations process 8 floats simultaneously using Vector256<float>.

using System;
using BlueSky.Ramanujan;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace BlueSky.BSR.RayTracing.Polaris;

/// <summary>
/// Hardware-adaptive 8-wide SIMD math for ray tracing, powered by Ramanujan.
/// Every method processes 8 values simultaneously using 256-bit vectors.
/// </summary>
public static class AVXMath
{
    /// <summary>Hardware support flags (checked once at startup)</summary>
    public static readonly bool HasAVX = Avx.IsSupported || Vector256.IsHardwareAccelerated;
    public static readonly bool HasSSE = Sse.IsSupported || Vector128.IsHardwareAccelerated;
    
    // ═══════════════════════════════════════════════════════════════
    // BROADCAST / LOAD / STORE
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>Broadcast scalar to all 8 lanes</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Broadcast(float value)
        => Vector256.Create(value);
    
    /// <summary>Load 8 floats from pointer</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe Vector256<float> Load(float* ptr)
        => Avx.IsSupported ? Avx.LoadVector256(ptr) : Vector256.Load(ptr);
    
    /// <summary>Store 8 floats to pointer</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Store(float* ptr, Vector256<float> v)
    {
        if (Avx.IsSupported) Avx.Store(ptr, v);
        else v.Store(ptr);
    }
    
    // ═══════════════════════════════════════════════════════════════
    // VECTOR3 OPERATIONS (8-wide, SoA layout)
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>
    /// 8-wide dot product: dot(a, b) for 8 pairs of 3D vectors.
    /// Uses RamanujanSimd.Fma256 (FMA3 on modern CPUs, MUL+ADD on Sandy Bridge AVX1, dual-NEON on ARM64).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Dot3(
        Vector256<float> ax, Vector256<float> ay, Vector256<float> az,
        Vector256<float> bx, Vector256<float> by, Vector256<float> bz)
    {
        var result = ax * bx;
        result = RamanujanSimd.Fma256(ay, by, result);
        result = RamanujanSimd.Fma256(az, bz, result);
        return result;
    }
    
    /// <summary>
    /// 8-wide cross product: cross(a, b) for 8 pairs of 3D vectors.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Cross3(
        Vector256<float> ax, Vector256<float> ay, Vector256<float> az,
        Vector256<float> bx, Vector256<float> by, Vector256<float> bz,
        out Vector256<float> rx, out Vector256<float> ry, out Vector256<float> rz)
    {
        rx = (ay * bz) - (az * by);
        ry = (az * bx) - (ax * bz);
        rz = (ax * by) - (ay * bx);
    }
    
    // ═══════════════════════════════════════════════════════════════
    // ARITHMETIC
    // ═══════════════════════════════════════════════════════════════
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Add(Vector256<float> a, Vector256<float> b) => a + b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Subtract(Vector256<float> a, Vector256<float> b) => a - b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Multiply(Vector256<float> a, Vector256<float> b) => a * b;

    /// <summary>
    /// 8-wide multiply-add: a*b + c (hardware-adaptive via RamanujanSimd.Fma256)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> MulAdd(Vector256<float> a, Vector256<float> b, Vector256<float> c)
        => RamanujanSimd.Fma256(a, b, c);
    
    /// <summary>
    /// 8-wide multiply-subtract: a*b - c
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> MulSub(Vector256<float> a, Vector256<float> b, Vector256<float> c)
        => (a * b) - c;
    
    /// <summary>
    /// 8-wide reciprocal: uses AVX VRCPPS + 1 Newton-Raphson iteration on x86, or Vector256 division on ARM64.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Reciprocal(Vector256<float> x)
    {
        if (Avx.IsSupported)
        {
            var rcp = Avx.Reciprocal(x);
            var two = Broadcast(2.0f);
            return Avx.Multiply(rcp, Avx.Subtract(two, Avx.Multiply(x, rcp)));
        }
        return One / x;
    }
    
    /// <summary>8-wide absolute value</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Abs(Vector256<float> x)
        => Vector256.Abs(x);
    
    // ═══════════════════════════════════════════════════════════════
    // MIN / MAX
    // ═══════════════════════════════════════════════════════════════
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Min(Vector256<float> a, Vector256<float> b)
        => Avx.IsSupported ? Avx.Min(a, b) : Vector256.Min(a, b);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Max(Vector256<float> a, Vector256<float> b)
        => Avx.IsSupported ? Avx.Max(a, b) : Vector256.Max(a, b);
    
    // ═══════════════════════════════════════════════════════════════
    // COMPARISONS (return NaN-mask: all-bits-set for true, zero for false)
    // ═══════════════════════════════════════════════════════════════
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> CmpLE(Vector256<float> a, Vector256<float> b)
        => Avx.IsSupported ? Avx.Compare(a, b, FloatComparisonMode.OrderedLessThanOrEqualSignaling) : Vector256.LessThanOrEqual(a, b);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> CmpGE(Vector256<float> a, Vector256<float> b)
        => Avx.IsSupported ? Avx.Compare(a, b, FloatComparisonMode.OrderedGreaterThanOrEqualSignaling) : Vector256.GreaterThanOrEqual(a, b);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> CmpLT(Vector256<float> a, Vector256<float> b)
        => Avx.IsSupported ? Avx.Compare(a, b, FloatComparisonMode.OrderedLessThanSignaling) : Vector256.LessThan(a, b);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> CmpGT(Vector256<float> a, Vector256<float> b)
        => Avx.IsSupported ? Avx.Compare(a, b, FloatComparisonMode.OrderedGreaterThanSignaling) : Vector256.GreaterThan(a, b);
    
    // ═══════════════════════════════════════════════════════════════
    // MASK OPERATIONS
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>Extract 8-bit integer mask from comparison result (bit i = lane i)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MoveMask(Vector256<float> mask)
        => Avx.IsSupported ? Avx.MoveMask(mask) : (int)mask.ExtractMostSignificantBits();
    
    /// <summary>Conditional select: returns mask ? trueVal : falseVal per-lane</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Select(Vector256<float> mask, Vector256<float> trueVal, Vector256<float> falseVal)
        => Avx.IsSupported ? Avx.BlendVariable(falseVal, trueVal, mask) : Vector256.ConditionalSelect(mask, trueVal, falseVal);
    
    /// <summary>Bitwise AND of two masks</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> And(Vector256<float> a, Vector256<float> b)
        => Vector256.BitwiseAnd(a, b);
    
    /// <summary>Bitwise OR of two masks</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Or(Vector256<float> a, Vector256<float> b)
        => Vector256.BitwiseOr(a, b);
    
    /// <summary>Bitwise AND-NOT: (~a) &amp; b</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> AndNot(Vector256<float> a, Vector256<float> b)
        => Vector256.AndNot(b, a);
    
    // ═══════════════════════════════════════════════════════════════
    // CONSTANTS
    // ═══════════════════════════════════════════════════════════════
    
    public static readonly Vector256<float> Zero = Vector256<float>.Zero;
    public static readonly Vector256<float> One = Broadcast(1.0f);
    public static readonly Vector256<float> Epsilon = Broadcast(1e-8f);
    public static readonly Vector256<float> MaxFloat = Broadcast(float.MaxValue);
    public static readonly Vector256<float> AllBitsSet = Broadcast(BitConverter.Int32BitsToSingle(unchecked((int)0xFFFFFFFF)));
}
