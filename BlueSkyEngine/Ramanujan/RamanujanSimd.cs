using System;
using BlueSky.Ramanujan;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace BlueSky.Ramanujan
{
    /// <summary>
    /// Ramanujan Hardware-Adaptive SIMD Batch Kernel Subsystem.
    /// Automatically dispatches optimal vector paths for:
    ///   - Modern x86-64 (AVX2 + FMA3)
    ///   - Legacy x86-64 (Sandy Bridge i5-2410M: AVX1 / SSE4.1 / SSE2 without FMA3)
    ///   - Apple Silicon / ARM64 (AdvSimd / NEON 128-bit &amp; Vector256)
    ///   - Portable Scalar fallback
    /// </summary>
    public static class RamanujanSimd
    {
        public static readonly bool HasAvx2 = Avx2.IsSupported;
        public static readonly bool HasFma = Fma.IsSupported;
        public static readonly bool HasAvx1 = Avx.IsSupported;
        public static readonly bool HasSse41 = Sse41.IsSupported;
        public static readonly bool HasSse2 = Sse2.IsSupported;
        public static readonly bool HasArmNeon = AdvSimd.IsSupported;
        public static readonly bool HasVector256 = Vector256.IsHardwareAccelerated;
        public static readonly bool HasVector128 = Vector128.IsHardwareAccelerated;

        /// <summary>Human-readable description of the active hardware SIMD tier.</summary>
        public static string ActiveBackendName
        {
            get
            {
                if (HasAvx2 && HasFma) return "x86-64 AVX2 + FMA3 (256-bit)";
                if (HasAvx1) return "x86-64 Sandy Bridge AVX1 (256-bit, No-FMA)";
                if (HasSse41) return "x86-64 SSE4.1 (128-bit)";
                if (HasSse2) return "x86-64 SSE2 Baseline (128-bit)";
                if (HasArmNeon) return "ARM64 AdvSimd / NEON (128/256-bit FMA)";
                if (HasVector128) return "Cross-Platform Vector128 SIMD";
                return "Scalar Fallback";
            }
        }

        /// <summary>
        /// 128-bit hardware-adaptive Multiply-Add: (a * b) + c.
        /// Uses single-cycle FMA on Haswell+/ARM64 NEON, and separate MUL+ADD on Sandy Bridge/SSE2.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<float> Fma128(Vector128<float> a, Vector128<float> b, Vector128<float> c)
        {
            if (Fma.IsSupported)
                return Fma.MultiplyAdd(a, b, c);
            if (AdvSimd.IsSupported)
                return AdvSimd.FusedMultiplyAdd(c, a, b);
            return (a * b) + c;
        }

        /// <summary>
        /// 256-bit hardware-adaptive Multiply-Add: (a * b) + c.
        /// Uses Fma.MultiplyAdd on AVX2+FMA, Avx.Add(Avx.Multiply) on Sandy Bridge AVX1, or Vector256 ops on ARM64.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<float> Fma256(Vector256<float> a, Vector256<float> b, Vector256<float> c)
        {
            if (Fma.IsSupported)
                return Fma.MultiplyAdd(a, b, c);
            if (Avx.IsSupported)
                return Avx.Add(Avx.Multiply(a, b), c);
            return (a * b) + c;
        }

        /// <summary>
        /// Transforms a contiguous batch of 3D points by a 4x4 matrix (with translation) using SIMD registers.
        /// Ideal for mesh skinning, terrain chunk vertex deformation, and particle emitters on both old and new PCs.
        /// </summary>
        public static void TransformPointsBatch(in Matrix4x4 matrix, ReadOnlySpan<Vector3> src, Span<Vector3> dst)
        {
            int count = Math.Min(src.Length, dst.Length);
            if (count == 0) return;

            if (Vector128.IsHardwareAccelerated)
            {
                var row0 = Vector128.Create(matrix.M11, matrix.M12, matrix.M13, 0f);
                var row1 = Vector128.Create(matrix.M21, matrix.M22, matrix.M23, 0f);
                var row2 = Vector128.Create(matrix.M31, matrix.M32, matrix.M33, 0f);
                var row3 = Vector128.Create(matrix.M41, matrix.M42, matrix.M43, 0f);

                for (int i = 0; i < count; i++)
                {
                    ref readonly var p = ref src[i];
                    var v = RamanujanSimd.Fma128(Vector128.Create(p.Z), row2,
                            RamanujanSimd.Fma128(Vector128.Create(p.Y), row1,
                            RamanujanSimd.Fma128(Vector128.Create(p.X), row0, row3)));
                    dst[i] = new Vector3(v.GetElement(0), v.GetElement(1), v.GetElement(2));
                }
                return;
            }

            for (int i = 0; i < count; i++)
                dst[i] = matrix.TransformPoint(src[i]);
        }

        /// <summary>
        /// Transforms a contiguous batch of 3D normals by the upper-left 3x3 of a 4x4 matrix using SIMD registers.
        /// </summary>
        public static void TransformNormalsBatch(in Matrix4x4 matrix, ReadOnlySpan<Vector3> src, Span<Vector3> dst)
        {
            int count = Math.Min(src.Length, dst.Length);
            if (count == 0) return;

            if (Vector128.IsHardwareAccelerated)
            {
                var row0 = Vector128.Create(matrix.M11, matrix.M12, matrix.M13, 0f);
                var row1 = Vector128.Create(matrix.M21, matrix.M22, matrix.M23, 0f);
                var row2 = Vector128.Create(matrix.M31, matrix.M32, matrix.M33, 0f);

                for (int i = 0; i < count; i++)
                {
                    ref readonly var n = ref src[i];
                    var v = RamanujanSimd.Fma128(Vector128.Create(n.Z), row2,
                            RamanujanSimd.Fma128(Vector128.Create(n.Y), row1, Vector128.Create(n.X) * row0));
                    dst[i] = new Vector3(v.GetElement(0), v.GetElement(1), v.GetElement(2));
                }
                return;
            }

            for (int i = 0; i < count; i++)
                dst[i] = matrix.TransformNormal(src[i]);
        }

        /// <summary>
        /// Batch linear interpolation of Vector3 spans using 128-bit SIMD lanes (processes 4 floats per instruction).
        /// Used by animation pose blending and network snapshot interpolation.
        /// </summary>
        public static void LerpVectorsBatch(ReadOnlySpan<Vector3> a, ReadOnlySpan<Vector3> b, float t, Span<Vector3> dst)
        {
            int count = Math.Min(Math.Min(a.Length, b.Length), dst.Length);
            if (count == 0) return;
            t = RamanujanMath.Clamp01(t);

            int floatCount = count * 3;
            ReadOnlySpan<float> fa = MemoryMarshal.Cast<Vector3, float>(a[..count]);
            ReadOnlySpan<float> fb = MemoryMarshal.Cast<Vector3, float>(b[..count]);
            Span<float> fd = MemoryMarshal.Cast<Vector3, float>(dst[..count]);

            int idx = 0;
            if (Vector256.IsHardwareAccelerated && floatCount >= 8)
            {
                var vt256 = Vector256.Create(t);
                ref float ra = ref MemoryMarshal.GetReference(fa);
                ref float rb = ref MemoryMarshal.GetReference(fb);
                ref float rd = ref MemoryMarshal.GetReference(fd);

                for (; idx <= floatCount - 8; idx += 8)
                {
                    var va = Unsafe.As<float, Vector256<float>>(ref Unsafe.Add(ref ra, idx));
                    var vb = Unsafe.As<float, Vector256<float>>(ref Unsafe.Add(ref rb, idx));
                    var vr = Fma256(vb - va, vt256, va);
                    Unsafe.As<float, Vector256<float>>(ref Unsafe.Add(ref rd, idx)) = vr;
                }
            }
            if (Vector128.IsHardwareAccelerated && idx <= floatCount - 4)
            {
                var vt128 = Vector128.Create(t);
                ref float ra = ref MemoryMarshal.GetReference(fa);
                ref float rb = ref MemoryMarshal.GetReference(fb);
                ref float rd = ref MemoryMarshal.GetReference(fd);

                for (; idx <= floatCount - 4; idx += 4)
                {
                    var va = Unsafe.As<float, Vector128<float>>(ref Unsafe.Add(ref ra, idx));
                    var vb = Unsafe.As<float, Vector128<float>>(ref Unsafe.Add(ref rb, idx));
                    var vr = Fma128(vb - va, vt128, va);
                    Unsafe.As<float, Vector128<float>>(ref Unsafe.Add(ref rd, idx)) = vr;
                }
            }

            for (; idx < floatCount; idx++)
            {
                fd[idx] = RamanujanMath.Fma(fb[idx] - fa[idx], t, fa[idx]);
            }
        }

        /// <summary>
        /// Computes the tight Axis-Aligned Bounding Box (Min/Max) of a point cloud using SIMD min/max reductions.
        /// </summary>
        public static void ComputeBoundsSIMD(ReadOnlySpan<Vector3> points, out Vector3 min, out Vector3 max)
        {
            if (points.IsEmpty)
            {
                min = Vector3.Zero;
                max = Vector3.Zero;
                return;
            }

            if (Vector128.IsHardwareAccelerated && points.Length >= 2)
            {
                var vMin = Vector128.Create(float.MaxValue);
                var vMax = Vector128.Create(float.MinValue);

                for (int i = 0; i < points.Length; i++)
                {
                    ref readonly var p = ref points[i];
                    var vp = Vector128.Create(p.X, p.Y, p.Z, 0f);
                    vMin = Vector128.Min(vMin, vp);
                    vMax = Vector128.Max(vMax, vp);
                }

                min = new Vector3(vMin.GetElement(0), vMin.GetElement(1), vMin.GetElement(2));
                max = new Vector3(vMax.GetElement(0), vMax.GetElement(1), vMax.GetElement(2));
                return;
            }

            var sMin = new Vector3(float.MaxValue);
            var sMax = new Vector3(float.MinValue);
            for (int i = 0; i < points.Length; i++)
            {
                sMin = Vector3.Min(sMin, points[i]);
                sMax = Vector3.Max(sMax, points[i]);
            }
            min = sMin;
            max = sMax;
        }

        /// <summary>
        /// High-throughput Frustum-AABB culling batch kernel.
        /// Writes 1 to visibilityOut[i] if boxes[i] intersects the frustum, or 0 if culled.
        /// Returns the total number of visible AABBs.
        /// </summary>
        public static int CullAABBsBatch(in Frustum frustum, ReadOnlySpan<AABB> boxes, Span<byte> visibilityOut)
        {
            int count = Math.Min(boxes.Length, visibilityOut.Length);
            if (count == 0) return 0;

            Span<Plane> planes = stackalloc Plane[6];
            planes[0] = frustum.Left;
            planes[1] = frustum.Right;
            planes[2] = frustum.Bottom;
            planes[3] = frustum.Top;
            planes[4] = frustum.Near;
            planes[5] = frustum.Far;

            int visibleCount = 0;
            for (int i = 0; i < count; i++)
            {
                ref readonly var box = ref boxes[i];
                bool inside = true;
                for (int p = 0; p < 6; p++)
                {
                    ref readonly var pl = ref planes[p];
                    float px = pl.A >= 0f ? box.Max.X : box.Min.X;
                    float py = pl.B >= 0f ? box.Max.Y : box.Min.Y;
                    float pz = pl.C >= 0f ? box.Max.Z : box.Min.Z;
                    if (RamanujanMath.Fma(pl.A, px, RamanujanMath.Fma(pl.B, py, RamanujanMath.Fma(pl.C, pz, pl.D))) < 0f)
                    {
                        inside = false;
                        break;
                    }
                }
                visibilityOut[i] = inside ? (byte)1 : (byte)0;
                if (inside) visibleCount++;
            }
            return visibleCount;
        }
    }
}
