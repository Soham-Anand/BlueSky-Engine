using System;
using System.Diagnostics;
using System.Numerics;
using BlueSky.BSR.Vectra;

namespace BlueSky.Tests
{
    public static class VectraTests
    {
        public static bool RunAllTests()
        {
            Console.WriteLine("\n[TEST SUITE] Running Vectra Pool/Sim Tests...");
            bool passed = true;
            passed &= PoolCapIsEnforced();
            passed &= LifetimeKillWorks();
            passed &= DeterminismHolds();
            passed &= EmitterRateIsCorrect();
            passed &= FullPoolSimIsFast();
            passed &= BakedSpriteIsSane();
            passed &= DirectionalLightMapsAreValid();
            passed &= CustomSpriteFallbackWorks();
            passed &= LoadedSpriteFeathered();
            passed &= BackToFrontOrderIsFarFirst();
            passed &= AdditiveFlagSurvives();
            passed &= ScriptedEmitterFlow();
            return passed;
        }

        private static bool AdditiveFlagSurvives()
        {
            try
            {
                var pool = new VectraPool(7);
                pool.Spawn(0, 0, 0, 0, 0, 0, 10f, 1f, 0f, 1, 1, 1, 1, 0, 0, 0f, 1f);
                pool.Spawn(0, 0, 0, 0, 0, 0, 10f, 1f, 0f, 1, 1, 1, 1, 0, 0, 0f, 0f);
                pool.Read(0, out _, out _, out _, out _, out _, out _, out _, out _, out _, out float add0);
                pool.Read(1, out _, out _, out _, out _, out _, out _, out _, out _, out _, out float add1);
                bool valid = pool.LiveCount == 2 && add0 >= 0.5f && add1 < 0.5f;
                Console.WriteLine($"  {(valid ? "✓" : "✗")} Additive flag survives spawn/read: {(valid ? "PASSED" : "FAILED")}");
                return valid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Additive flag threw: {ex.Message}");
                return false;
            }
        }

        private static bool ScriptedEmitterFlow()
        {
            try
            {
                // Mirrors what the TeaScript addEmitter binding does.
                var sys = new VectraSystem();
                var cfg = VectraEmitterConfig.Smoke();
                cfg.Additive = true;
                cfg.RatePerSec = 60f;
                var emitter = new VectraEmitter(cfg, new System.Numerics.Vector3(0f, 0f, 0f));
                bool added = sys.AddEmitter(emitter);
                sys.Update(0.5f);
                int additive = 0, total = sys.Pool.LiveCount;
                for (int i = 0; i < total; i++)
                {
                    sys.Pool.Read(i, out _, out _, out _, out _, out _, out _, out _, out _, out _, out float add);
                    if (add >= 0.5f) additive++;
                }
                bool removed = sys.RemoveEmitter(emitter);
                bool valid = added && total > 0 && additive == total && removed && sys.EmitterCount == 0;
                Console.WriteLine($"  {(valid ? "✓" : "✗")} Scripted emitter flow (spawned={total}, additive={additive}): {(valid ? "PASSED" : "FAILED")}");
                return valid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Scripted emitter flow threw: {ex.Message}");
                return false;
            }
        }

        private static bool BackToFrontOrderIsFarFirst()
        {
            try
            {
                float[] keys = { 9f, 1f, 25f, 4f, 16f };
                int[] order = new int[5];
                VectraRenderer.ComputeBackToFrontOrder(keys, order, 5);
                // Ascending keys carried: expect original indices of 1,4,9,16,25.
                int[] expected = { 1, 3, 0, 4, 2 };
                bool valid = true;
                for (int k = 0; k < 5 && valid; k++)
                    valid = order[k] == expected[k];
                // Filler consumes from the far end: first drawn must be farthest.
                valid &= order[4] == 2;
                Console.WriteLine($"  {(valid ? "✓" : "✗")} Back-to-front order (far first): {(valid ? "PASSED" : "FAILED")}");
                return valid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Back-to-front order threw: {ex.Message}");
                return false;
            }
        }

        private static bool PoolCapIsEnforced()
        {
            try
            {
                var pool = new VectraPool(1);
                int ok = 0;
                for (int i = 0; i < VectraPool.MaxParticles + 100; i++)
                {
                    if (pool.Spawn(0, 0, 0, 0, 0, 0, 10f, 1f, 0f, 1, 1, 1, 1, 0, 0))
                        ok++;
                }
                bool valid = ok == VectraPool.MaxParticles && pool.LiveCount == VectraPool.MaxParticles;
                Console.WriteLine($"  {(valid ? "✓" : "✗")} Pool cap {VectraPool.MaxParticles} (extra rejected): {(valid ? "PASSED" : "FAILED")}");
                return valid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Pool cap threw: {ex.Message}");
                return false;
            }
        }

        private static bool LifetimeKillWorks()
        {
            try
            {
                var pool = new VectraPool(2);
                pool.Spawn(0, 0, 0, 1, 0, 0, 0.1f, 1f, 0f, 1, 1, 1, 1, 0, 0);
                pool.Update(0.05f);
                bool aliveAtHalf = pool.LiveCount == 1;
                pool.Update(0.1f);
                bool deadAfter = pool.LiveCount == 0;
                bool valid = aliveAtHalf && deadAfter;
                Console.WriteLine($"  {(valid ? "✓" : "✗")} Lifetime kill: {(valid ? "PASSED" : "FAILED")}");
                return valid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Lifetime kill threw: {ex.Message}");
                return false;
            }
        }

        private static bool DeterminismHolds()
        {
            try
            {
                uint Run(uint seed)
                {
                    var sys = new VectraSystem();
                    sys.AddEmitter(new VectraEmitter(VectraEmitterConfig.Smoke(), new Vector3(0, 0, 0)));
                    return sys.ReplayChecksum(seed, 120, 1f / 60f);
                }
                uint a = Run(42);
                uint b = Run(42);
                uint c = Run(43);
                bool valid = a == b && a != c && a != 0;
                Console.WriteLine($"  {(valid ? "✓" : "✗")} Determinism (same seed equal, diff seed differs): {(valid ? "PASSED" : "FAILED")}");
                return valid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Determinism threw: {ex.Message}");
                return false;
            }
        }

        private static bool EmitterRateIsCorrect()
        {
            try
            {
                var pool = new VectraPool(3);
                var cfg = VectraEmitterConfig.Smoke();
                cfg.RatePerSec = 60f;
                cfg.LifeMin = cfg.LifeMax = 10f;
                var emitter = new VectraEmitter(cfg, new Vector3(0, 0, 0));
                for (int i = 0; i < 60; i++)
                {
                    emitter.Update(pool, 1f / 60f);
                    pool.Update(1f / 60f);
                }
                bool valid = pool.LiveCount >= 55 && pool.LiveCount <= 65;
                Console.WriteLine($"  {(valid ? "✓" : "✗")} Emitter rate 60/s x 1s = {pool.LiveCount}: {(valid ? "PASSED" : "FAILED")}");
                return valid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Emitter rate threw: {ex.Message}");
                return false;
            }
        }

        private static bool FullPoolSimIsFast()
        {
            try
            {
                var pool = new VectraPool(4);
                for (int i = 0; i < VectraPool.MaxParticles; i++)
                    pool.Spawn(0, 0, 0, 0, 1, 0, 10f, 2f, 1f, 0.3f, 0.3f, 0.3f, 0.4f, 0.5f, 0.5f);
                var sw = Stopwatch.StartNew();
                pool.Update(1f / 60f);
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                bool valid = ms < 8.0;
                Console.WriteLine($"  {(valid ? "✓" : "✗")} Full-pool sim {VectraPool.MaxParticles} in {ms:F2}ms (budget 8ms): {(valid ? "PASSED" : "FAILED")}");
                return valid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Full-pool sim threw: {ex.Message}");
                return false;
            }
        }

        private static bool BakedSpriteIsSane()
        {
            try
            {
                byte[] baked = VectraSprite.Bake();
                int s = VectraSprite.Size;
                bool sizeOk = baked.Length == s * s * 4;
                // All 4 borders (top, bottom, left, right) pinned to zero alpha;
                // quad topology covers all 4 quadrants without bow-tie overlap.
                bool bordersZero = true;
                for (int i = 0; i < s && bordersZero; i++)
                {
                    if (baked[i * 4 + 3] > 2 ||
                        baked[((s - 1) * s + i) * 4 + 3] > 2 ||
                        baked[(i * s) * 4 + 3] > 2 ||
                        baked[(i * s + (s - 1)) * 4 + 3] > 2)
                        bordersZero = false;
                }
                var corners = VectraRenderer.Corners;
                var idx = VectraRenderer.Indices;
                float area0 = MathF.Abs(
                    (corners[idx[1]].X - corners[idx[0]].X) * (corners[idx[2]].Y - corners[idx[0]].Y) -
                    (corners[idx[2]].X - corners[idx[0]].X) * (corners[idx[1]].Y - corners[idx[0]].Y)) * 0.5f;
                float area1 = MathF.Abs(
                    (corners[idx[4]].X - corners[idx[3]].X) * (corners[idx[5]].Y - corners[idx[3]].Y) -
                    (corners[idx[5]].X - corners[idx[3]].X) * (corners[idx[4]].Y - corners[idx[3]].Y)) * 0.5f;
                Vector2 c0 = (corners[idx[0]] + corners[idx[1]] + corners[idx[2]]) / 3f;
                Vector2 c1 = (corners[idx[3]] + corners[idx[4]] + corners[idx[5]]) / 3f;
                // Centroids of the two triangles must be opposite across the origin (not overlapping bow-tie).
                bool quadFullCoverage = MathF.Abs(area0 + area1 - 4f) < 1e-4f && Vector2.Distance(c0 + c1, Vector2.Zero) < 1e-4f;
                long interior = 0;
                for (int i = 3; i < baked.Length; i += 4) interior += baked[i];
                bool valid = sizeOk && bordersZero && quadFullCoverage && interior > 0;
                Console.WriteLine($"  {(valid ? "✓" : "✗")} Baked sprite sane (all 4 borders 0, full quad coverage, interior lives): {(valid ? "PASSED" : "FAILED")}");
                return valid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Baked sprite threw: {ex.Message}");
                return false;
            }
        }

        private static bool DirectionalLightMapsAreValid()
        {
            try
            {
                byte[] baked = VectraSprite.Bake();
                var maps = VectraSprite.BakeDirectionalLightMaps(baked,
                    VectraSprite.Size, VectraSprite.Size, 64);
                bool dimensionsOk = maps.W == 64 && maps.H == 64 &&
                    maps.Positive.Length == maps.W * maps.H * 4 &&
                    maps.Negative.Length == maps.W * maps.H * 4;
                bool opaqueChannels = true;
                for (int i = 3; i < maps.Positive.Length && opaqueChannels; i += 4)
                    opaqueChannels = maps.Positive[i] == 255 && maps.Negative[i] == 255;
                int center = ((maps.H / 2) * maps.W + maps.W / 2) * 4;
                int corner = 0;
                bool centerHasDepth = maps.Negative[center + 1] > maps.Negative[corner + 1];
                bool valid = dimensionsOk && opaqueChannels && centerHasDepth;
                Console.WriteLine($"  {(valid ? "✓" : "✗")} Six-way light maps (packed, bounded, density-responsive): {(valid ? "PASSED" : "FAILED")}");
                return valid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Directional light maps threw: {ex.Message}");
                return false;
            }
        }

        private static bool CustomSpriteFallbackWorks()
        {
            string? garbagePath = null;
            try
            {
                bool missingNull = VectraSprite.TryLoadCustomPng(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vectra-nope-{Guid.NewGuid():N}.png")) == null;
                garbagePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vectra-garbage-{Guid.NewGuid():N}.png");
                System.IO.File.WriteAllBytes(garbagePath, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02 });
                bool garbageNull = VectraSprite.TryLoadCustomPng(garbagePath) == null;
                bool emptyNull = VectraSprite.TryLoadCustomPng("") == null;
                bool valid = missingNull && garbageNull && emptyNull;
                Console.WriteLine($"  {(valid ? "✓" : "✗")} Custom sprite fallback (missing/garbage/empty → null): {(valid ? "PASSED" : "FAILED")}");
                return valid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Custom sprite fallback threw: {ex.Message}");
                return false;
            }
            finally
            {
                try { if (garbagePath != null) System.IO.File.Delete(garbagePath); } catch { }
            }
        }

        private static bool LoadedSpriteFeathered()
        {
            try
            {
                string? found = null;
                foreach (var dir in new[]
                {
                    System.IO.Directory.GetCurrentDirectory(),
                    System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".."),
                })
                {
                    try
                    {
                        string c = System.IO.Path.Combine(dir, "smoke.png");
                        if (System.IO.File.Exists(c)) { found = c; break; }
                    }
                    catch { }
                }
                if (found == null)
                {
                    Console.WriteLine("  - Loaded sprite feathered: SKIPPED (smoke.png not near CWD)");
                    return true;
                }
                var loaded = VectraSprite.TryLoadCustomPng(found);
                if (!loaded.HasValue)
                {
                    Console.WriteLine("  ✗ Loaded sprite feathered: loader rejected smoke.png: FAILED");
                    return false;
                }
                var (rgba, w, h) = loaded.Value;
                bool bordersZero = true;
                for (int x = 0; x < w && bordersZero; x++)
                {
                    if (rgba[x * 4 + 3] > 4 || rgba[((h - 1) * w + x) * 4 + 3] > 4)
                        bordersZero = false;
                }
                long interior = 0;
                for (int i = 3; i < rgba.Length; i += 4) interior += rgba[i];
                bool valid = bordersZero && interior > 0 && w <= VectraSprite.MaxSize && h <= VectraSprite.MaxSize;
                Console.WriteLine($"  {(valid ? "✓" : "✗")} Loaded sprite feathered ({w}x{h}, borders 0): {(valid ? "PASSED" : "FAILED")}");
                return valid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Loaded sprite feathered threw: {ex.Message}");
                return false;
            }
        }
    }
}
