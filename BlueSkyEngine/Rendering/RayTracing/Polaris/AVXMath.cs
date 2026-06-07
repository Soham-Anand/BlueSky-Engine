// BlueSkyEngine - Project Polaris: AVX1 SIMD Math
//
// SANDY BRIDGE COMPATIBLE (i5-2410M)
// ====================================
// ✅ AVX1 (256-bit, 8 floats)
// ✅ SSE4.2
// ❌ AVX2 (Haswell+)
// ❌ FMA3 (Haswell+) → every a*b+c = TWO instructions
//
// All operations process 8 floats simultaneously.
// This is the mathematical foundation of Project Polaris.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace BlueSky.Rendering.RayTracing.Polaris;

/// <summary>
/// AVX1-compatible SIMD math for 8-wide ray tracing.
/// Every method processes 8 values simultaneously using 256-bit registers.
/// </summary>
public static class AVXMath
{
    /// <summary>Hardware support flags (checked once at startup)</summary>
    public static readonly bool HasAVX = Avx.IsSupported;
    public static readonly bool HasSSE = Sse.IsSupported;
    
    // ═══════════════════════════════════════════════════════════════
    // BROADCAST / LOAD
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>Broadcast scalar to all 8 lanes</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Broadcast(float value)
        => Vector256.Create(value);
    
    /// <summary>Load 8 floats from pointer (must be 32-byte aligned for best perf)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe Vector256<float> Load(float* ptr)
        => Avx.LoadVector256(ptr);
    
    /// <summary>Store 8 floats to pointer</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Store(float* ptr, Vector256<float> v)
        => Avx.Store(ptr, v);
    
    // ═══════════════════════════════════════════════════════════════
    // VECTOR3 OPERATIONS (8-wide, SoA layout)
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>
    /// 8-wide dot product: dot(a, b) for 8 pairs of 3D vectors.
    /// No FMA on Sandy Bridge: a*b + c = Add(Multiply(a,b), c)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Dot3(
        Vector256<float> ax, Vector256<float> ay, Vector256<float> az,
        Vector256<float> bx, Vector256<float> by, Vector256<float> bz)
    {
        var result = Avx.Multiply(ax, bx);                         // ax*bx
        result = Avx.Add(result, Avx.Multiply(ay, by));            // + ay*by
        result = Avx.Add(result, Avx.Multiply(az, bz));            // + az*bz
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
        // cross(a,b) = (ay*bz - az*by, az*bx - ax*bz, ax*by - ay*bx)
        rx = Avx.Subtract(Avx.Multiply(ay, bz), Avx.Multiply(az, by));
        ry = Avx.Subtract(Avx.Multiply(az, bx), Avx.Multiply(ax, bz));
        rz = Avx.Subtract(Avx.Multiply(ax, by), Avx.Multiply(ay, bx));
    }
    
    // ═══════════════════════════════════════════════════════════════
    // ARITHMETIC
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>
    /// 8-wide multiply-add: a*b + c (two instructions, no FMA on Sandy Bridge)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> MulAdd(Vector256<float> a, Vector256<float> b, Vector256<float> c)
        => Avx.Add(Avx.Multiply(a, b), c);
    
    /// <summary>
    /// 8-wide multiply-subtract: a*b - c
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> MulSub(Vector256<float> a, Vector256<float> b, Vector256<float> c)
        => Avx.Subtract(Avx.Multiply(a, b), c);
    
    /// <summary>
    /// 8-wide reciprocal with Newton-Raphson refinement.
    /// VRCPPS gives ~12 bits; one NR iteration → ~23 bits (full float precision).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Reciprocal(Vector256<float> x)
    {
        var rcp = Avx.Reciprocal(x);                               // ~12 bit estimate
        var two = Broadcast(2.0f);
        // NR: rcp = rcp * (2 - x * rcp)
        rcp = Avx.Multiply(rcp, Avx.Subtract(two, Avx.Multiply(x, rcp)));
        return rcp;
    }
    
    /// <summary>8-wide absolute value (clear sign bit)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Abs(Vector256<float> x)
    {
        var signMask = Broadcast(-0.0f); // 0x80000000 in each lane
        return Avx.AndNot(signMask, x);
    }
    
    // ═══════════════════════════════════════════════════════════════
    // MIN / MAX
    // ═══════════════════════════════════════════════════════════════
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Min(Vector256<float> a, Vector256<float> b)
        => Avx.Min(a, b);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Max(Vector256<float> a, Vector256<float> b)
        => Avx.Max(a, b);
    
    // ═══════════════════════════════════════════════════════════════
    // COMPARISONS (return NaN-mask: all-bits-set for true, zero for false)
    // ═══════════════════════════════════════════════════════════════
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> CmpLE(Vector256<float> a, Vector256<float> b)
        => Avx.Compare(a, b, FloatComparisonMode.OrderedLessThanOrEqualSignaling);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> CmpGE(Vector256<float> a, Vector256<float> b)
        => Avx.Compare(a, b, FloatComparisonMode.OrderedGreaterThanOrEqualSignaling);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> CmpLT(Vector256<float> a, Vector256<float> b)
        => Avx.Compare(a, b, FloatComparisonMode.OrderedLessThanSignaling);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> CmpGT(Vector256<float> a, Vector256<float> b)
        => Avx.Compare(a, b, FloatComparisonMode.OrderedGreaterThanSignaling);
    
    // ═══════════════════════════════════════════════════════════════
    // MASK OPERATIONS
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>Extract 8-bit integer mask from comparison result (bit i = lane i)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MoveMask(Vector256<float> mask)
        => Avx.MoveMask(mask);
    
    /// <summary>Conditional select: returns mask ? trueVal : falseVal per-lane</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Select(Vector256<float> mask, Vector256<float> trueVal, Vector256<float> falseVal)
        => Avx.BlendVariable(falseVal, trueVal, mask);
    
    /// <summary>Bitwise AND of two masks</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> And(Vector256<float> a, Vector256<float> b)
        => Avx.And(a, b);
    
    /// <summary>Bitwise OR of two masks</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Or(Vector256<float> a, Vector256<float> b)
        => Avx.Or(a, b);
    
    /// <summary>Bitwise AND-NOT: (~a) & b</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> AndNot(Vector256<float> a, Vector256<float> b)
        => Avx.AndNot(a, b);
    
    // ═══════════════════════════════════════════════════════════════
    // CONSTANTS
    // ═══════════════════════════════════════════════════════════════
    
    public static readonly Vector256<float> Zero = Vector256<float>.Zero;
    public static readonly Vector256<float> One = Broadcast(1.0f);
    public static readonly Vector256<float> Epsilon = Broadcast(1e-8f);
    public static readonly Vector256<float> MaxFloat = Broadcast(float.MaxValue);
    public static readonly Vector256<float> AllBitsSet = Broadcast(BitConverter.Int32BitsToSingle(unchecked((int)0xFFFFFFFF)));
}
