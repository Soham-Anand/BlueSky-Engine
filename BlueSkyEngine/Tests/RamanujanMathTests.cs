using System;
using BlueSky.Ramanujan;

namespace BlueSky.Tests;

/// <summary>
/// Unit &amp; SIMD verification suite for the Ramanujan Hardware-Adaptive Math Subsystem.
/// Verifies fast polynomial approximations, Ramanujan's ellipse circumference formula,
/// matrix inversion, and SIMD batch kernels across both older x86-64 and modern ARM64/AVX2 CPUs.
/// </summary>
public static class RamanujanMathTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine($"\n[TEST SUITE] Running Ramanujan Math Subsystem Tests (Backend: {RamanujanSimd.ActiveBackendName})...");
        bool passed = true;

        passed &= TestFastTrigonometry();
        passed &= TestFastAtan2AndExp();
        passed &= TestRamanujanEllipseCircumference();
        passed &= TestMatrixInvertAndMultiply();
        passed &= TestSimdBatchOperations();

        return passed;
    }

    private static bool TestFastTrigonometry()
    {
        float maxSinErr = 0f;
        float maxCosErr = 0f;

        for (int i = -720; i <= 720; i += 3)
        {
            float rad = i * RamanujanMath.Deg2Rad;
            float s = RamanujanMath.FastSin(rad);
            float c = RamanujanMath.FastCos(rad);
            maxSinErr = MathF.Max(maxSinErr, MathF.Abs(s - MathF.Sin(rad)));
            maxCosErr = MathF.Max(maxCosErr, MathF.Abs(c - MathF.Cos(rad)));
        }

        bool valid = maxSinErr < 2e-4f && maxCosErr < 2e-4f;
        Console.WriteLine($"  ✓ Ramanujan FastSin/FastCos Minimax Accuracy (maxErr={MathF.Max(maxSinErr, maxCosErr):E2}): {(valid ? "PASSED" : "FAILED")}");
        return valid;
    }

    private static bool TestFastAtan2AndExp()
    {
        float maxAtanErr = 0f;
        for (int y = -10; y <= 10; y++)
        {
            for (int x = -10; x <= 10; x++)
            {
                if (x == 0 && y == 0) continue;
                float approx = RamanujanMath.FastAtan2(y, x);
                float exact = MathF.Atan2(y, x);
                maxAtanErr = MathF.Max(maxAtanErr, MathF.Abs(approx - exact));
            }
        }

        float exp0 = RamanujanMath.FastExp(0f);
        float exp1 = RamanujanMath.FastExp(1f);
        float expNeg2 = RamanujanMath.FastExp(-2f);
        bool expValid = MathF.Abs(exp0 - 1f) < 1e-4f &&
                        MathF.Abs(exp1 - MathF.E) < 5e-3f &&
                        MathF.Abs(expNeg2 - MathF.Exp(-2f)) < 2e-3f;

        bool valid = maxAtanErr < 1e-3f && expValid;
        Console.WriteLine($"  ✓ Ramanujan FastAtan2 (maxErr={maxAtanErr:E2}) & FastExp: {(valid ? "PASSED" : "FAILED")}");
        return valid;
    }

    private static bool TestRamanujanEllipseCircumference()
    {
        // Circle of radius R=5: circumference = 2 * PI * 5 = 10 * PI
        float circleC = RamanujanMath.EllipseCircumference(5f, 5f);
        float expectedCircle = 10f * RamanujanMath.PI;

        // Classic ellipse a=10, b=5: known perimeter ≈ 48.4422411
        float ellipseC = RamanujanMath.EllipseCircumference(10f, 5f);
        bool valid = MathF.Abs(circleC - expectedCircle) < 1e-4f &&
                     MathF.Abs(ellipseC - 48.44224f) < 1e-3f;
        Console.WriteLine($"  ✓ Ramanujan 2nd Ellipse Circumference Formula (C(10,5)={ellipseC:F5}): {(valid ? "PASSED" : "FAILED")}");
        return valid;
    }

    private static bool TestMatrixInvertAndMultiply()
    {
        var trs = Matrix4x4.CreateTRS(
            new Vector3(12.5f, -4.0f, 33.0f),
            Quaternion.Euler(25f, -60f, 15f),
            new Vector3(2f, 3f, 0.5f));

        bool inverted = Matrix4x4.Invert(in trs, out var inv);
        var prod = trs * inv;
        var pt = new Vector3(3f, -7f, 11f);
        var worldPt = trs.TransformPoint(pt);
        var roundTripPt = inv.TransformPoint(worldPt);

        float err = Vector3.Distance(pt, roundTripPt);
        bool valid = inverted && err < 1e-3f && MathF.Abs(prod.M11 - 1f) < 1e-3f && MathF.Abs(prod.M44 - 1f) < 1e-3f;
        Console.WriteLine($"  ✓ Ramanujan SIMD Matrix4x4 Multiply & Invert (roundTripErr={err:E2}): {(valid ? "PASSED" : "FAILED")}");
        return valid;
    }

    private static bool TestSimdBatchOperations()
    {
        const int count = 64;
        Span<Vector3> src = stackalloc Vector3[count];
        Span<Vector3> dst = stackalloc Vector3[count];
        Span<Vector3> lerped = stackalloc Vector3[count];

        for (int i = 0; i < count; i++)
            src[i] = new Vector3(i * 1.5f, -i * 0.5f, i * 2.0f);

        var mat = Matrix4x4.CreateTranslation(new Vector3(10f, 20f, 30f));
        RamanujanSimd.TransformPointsBatch(in mat, src, dst);

        bool batchTransformOk = Vector3.Distance(dst[10], src[10] + new Vector3(10f, 20f, 30f)) < 1e-4f;

        RamanujanSimd.LerpVectorsBatch(src, dst, 0.5f, lerped);
        bool batchLerpOk = Vector3.Distance(lerped[10], src[10] + new Vector3(5f, 10f, 15f)) < 1e-4f;

        RamanujanSimd.ComputeBoundsSIMD(src, out var min, out var max);
        bool boundsOk = MathF.Abs(min.Y - (-63 * 0.5f)) < 1e-4f && MathF.Abs(max.Z - (63 * 2.0f)) < 1e-4f;

        var vp = Matrix4x4.CreateLookAt(new Vector3(0, 0, 10), Vector3.Zero, Vector3.Up) *
                 Matrix4x4.CreatePerspective(60f * RamanujanMath.Deg2Rad, 1.77f, 0.1f, 100f);
        var frustum = Frustum.FromViewProjection(vp);
        Span<AABB> boxes = stackalloc AABB[2]
        {
            AABB.FromCenterExtents(Vector3.Zero, Vector3.One),          // Inside view frustum
            AABB.FromCenterExtents(new Vector3(0, 0, 500f), Vector3.One) // Behind camera / outside
        };
        Span<byte> vis = stackalloc byte[2];
        int visCount = RamanujanSimd.CullAABBsBatch(in frustum, boxes, vis);
        bool cullOk = visCount == 1 && vis[0] == 1 && vis[1] == 0;

        bool valid = batchTransformOk && batchLerpOk && boundsOk && cullOk;
        Console.WriteLine($"  ✓ RamanujanSimd Batch Kernels (Transform, Lerp, Bounds, FrustumCull): {(valid ? "PASSED" : "FAILED")}");
        return valid;
    }
}
