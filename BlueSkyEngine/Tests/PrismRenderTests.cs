using System;
using BlueSky.Ramanujan;
using System.Numerics;
using BlueSky.Editor;

namespace BlueSky.Tests;

/// <summary>
/// Diagnostic verification suite for the "Prism" Hardware-Adaptive Rendering &amp; Visual Pipeline:
///   1. Camera-following texel-snapped LightSpaceMatrix stability (zero sub-texel shadow swimming).
///   2. World-space AABB frustum culling accuracy for shadow and camera passes.
///   3. SIMD local mesh AABB computation with planar geometry padding.
/// </summary>
public static class PrismRenderTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Prism Rendering & Visual Pipeline Tests...");
        bool passed = true;

        passed &= TestTexelSnappedShadowMatrix();
        passed &= TestCameraFollowingShadowsFarFromOrigin();
        passed &= TestWorldSpaceAABBFrustumCulling();

        return passed;
    }

    private static bool TestTexelSnappedShadowMatrix()
    {
        var sunDir = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.3f, 0.7f, 0.4f));
        // Sub-texel camera jitter (< 0.002m when 1 texel = 64m / 2048 = 0.03125m)
        var camA = new System.Numerics.Vector3(10.001f, 2.0f, 10.001f);
        var camB = new System.Numerics.Vector3(10.002f, 2.0f, 10.002f);

        var matA = ViewportRenderer.ComputeLightSpaceMatrix(sunDir, camA, 2048, 32f);
        var matB = ViewportRenderer.ComputeLightSpaceMatrix(sunDir, camB, 2048, 32f);

        bool subTexelIdentical = matA.Equals(matB);
        Console.WriteLine($"  ✓ Prism Texel-Snapped Shadow Matrix Stability (Zero Sub-Texel Shimmer): {(subTexelIdentical ? "PASSED" : "FAILED")}");
        return subTexelIdentical;
    }

    private static bool TestCameraFollowingShadowsFarFromOrigin()
    {
        var sunDir = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.3f, 0.7f, 0.4f));
        // Player drives 500 meters away from world origin (where old fixed-origin shadow map lost shadows)
        var farCam = new System.Numerics.Vector3(500f, 2f, -350f);
        var lightMat = ViewportRenderer.ComputeLightSpaceMatrix(sunDir, farCam, 2048, 32f);

        // Project a point right next to the player into light clip space
        var playerWorldPoint = new System.Numerics.Vector4(500f, 0.5f, -350f, 1f);
        var clip = System.Numerics.Vector4.Transform(playerWorldPoint, lightMat);
        var ndc = new System.Numerics.Vector3(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);

        bool insideShadowMap = MathF.Abs(ndc.X) < 0.25f && MathF.Abs(ndc.Y) < 0.25f && ndc.Z >= 0f && ndc.Z <= 1f;
        Console.WriteLine($"  ✓ Prism Camera-Following Shadow Frustum @ (500, -350) (NDC={ndc.X:F3},{ndc.Y:F3},{ndc.Z:F3}): {(insideShadowMap ? "PASSED" : "FAILED")}");
        return insideShadowMap;
    }

    private static bool TestWorldSpaceAABBFrustumCulling()
    {
        var view = System.Numerics.Matrix4x4.CreateLookAt(
            new System.Numerics.Vector3(0, 2, 10),
            new System.Numerics.Vector3(0, 0, 0),
            System.Numerics.Vector3.UnitY);
        var proj = System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView(
            60f * RamanujanMath.Deg2Rad, 16f / 9f, 0.1f, 100f);
        var vp = view * proj;

        Span<System.Numerics.Vector4> planes = stackalloc System.Numerics.Vector4[6];
        planes[0] = NormalizePlane(new(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41));
        planes[1] = NormalizePlane(new(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41));
        planes[2] = NormalizePlane(new(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42));
        planes[3] = NormalizePlane(new(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42));
        planes[4] = NormalizePlane(new(vp.M13, vp.M23, vp.M33, vp.M43));
        planes[5] = NormalizePlane(new(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43));

        var localBox = new AABB(new BlueSky.Ramanujan.Vector3(-1f), new BlueSky.Ramanujan.Vector3(1f));
        var visibleWorldBox = localBox.Transform(BlueSky.Ramanujan.Matrix4x4.CreateTranslation(new BlueSky.Ramanujan.Vector3(0, 0, 0)));
        var behindCameraBox = localBox.Transform(BlueSky.Ramanujan.Matrix4x4.CreateTranslation(new BlueSky.Ramanujan.Vector3(0, 0, 50)));

        bool visOk = ViewportRenderer.IsAABBFrustumVisible(in visibleWorldBox, planes);
        bool cullOk = !ViewportRenderer.IsAABBFrustumVisible(in behindCameraBox, planes);
        bool valid = visOk && cullOk;

        Console.WriteLine($"  ✓ Prism World-Space AABB Frustum Culling (vis={visOk}, culled={cullOk}): {(valid ? "PASSED" : "FAILED")}");
        return valid;
    }

    private static System.Numerics.Vector4 NormalizePlane(System.Numerics.Vector4 p)
    {
        float len = MathF.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
        return len > 1e-5f ? p / len : p;
    }
}
