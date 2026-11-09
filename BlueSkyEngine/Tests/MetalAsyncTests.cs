using System;
using BlueSky.BSR.RHI;

namespace BlueSky.Tests
{
    /// <summary>
    /// M4: proves the async Metal frame machinery headless (no window needed).
    /// Skips gracefully off macOS. Exercises BeginFrame throttle rotation,
    /// SubmitNoWait past the in-flight limit, and DrainPending.
    /// </summary>
    public static class MetalAsyncTests
    {
        public static bool RunAllTests()
        {
            Console.WriteLine("\n[TEST SUITE] Running Metal Async Frame Tests...");
            bool passed = true;
            passed &= AsyncSubmitDrainCycle();
            passed &= MipMemorylessAndGpuClock();
            return passed;
        }

        private static bool MipMemorylessAndGpuClock()
        {
            try
            {
                if (!OperatingSystem.IsMacOS())
                {
                    Console.WriteLine("  SKIP Metal mips/memoryless/GPU clock (not macOS)");
                    return true;
                }
                if (!RHIDiscovery.IsMetalSupported())
                {
                    Console.WriteLine("  - Mips + memoryless + GPU clock: SKIPPED (headless/sandboxed session without MTLDevice)");
                    return true;
                }

                var device = new BlueSky.BSR.RHI.Metal.MetalDevice(
                    BlueSky.Aegis.Detection.IntelligentQueryLayer.Probe());

                // Mip chain: 64×64 ×4 levels, upload L0, generate rest.
                using var mipped = device.CreateTexture(new TextureDesc
                {
                    Width = 64, Height = 64, Depth = 1,
                    MipLevels = 4, ArrayLayers = 1,
                    Format = TextureFormat.RGBA8Unorm,
                    Usage = TextureUsage.Sampled | TextureUsage.TransferDst,
                    DebugName = "MetalAsync.Mipped",
                });
                byte[] l0 = new byte[64 * 64 * 4];
                for (int i = 0; i < l0.Length; i++) l0[i] = (byte)(i & 0xFF);
                device.UploadTexture(mipped, l0, 0);
                device.GenerateMipmaps(mipped);

                // Memoryless depth (tile memory only — no Sampled flag).
                using var memless = device.CreateTexture(new TextureDesc
                {
                    Width = 64, Height = 64, Depth = 1,
                    MipLevels = 1, ArrayLayers = 1,
                    Format = TextureFormat.Depth32Float,
                    Usage = TextureUsage.DepthStencil,
                    Memoryless = true,
                    DebugName = "MetalAsync.Memless",
                });

                // Memoryless + Sampled must fail loudly.
                bool loudFail = false;
                try
                {
                    using var bad = device.CreateTexture(new TextureDesc
                    {
                        Width = 16, Height = 16, Depth = 1,
                        MipLevels = 1, ArrayLayers = 1,
                        Format = TextureFormat.BGRA8Unorm,
                        Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
                        Memoryless = true,
                    });
                }
                catch (ArgumentException) { loudFail = true; }

                // GPU clock: submit, drain, read.
                using var cmd = device.CreateCommandBuffer();
                cmd.BeginRenderPass(mipped, ClearValue.FromColor(0, 0, 0, 1));
                cmd.EndRenderPass();
                device.SubmitNoWait(cmd);
                device.DrainPending();
                bool clockOk = device.LastFrameGpuMs >= 0;

                device.Dispose();
                bool ok = loudFail && clockOk;
                Console.WriteLine($"  {(ok ? "✓" : "✗")} Mips + memoryless + GPU clock (loudFail={loudFail}, gpuMs={device.LastFrameGpuMs:F3}): {(ok ? "PASSED" : "FAILED")}");
                return ok;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Mips + memoryless + GPU clock: FAILED ({ex.Message})");
                return false;
            }
        }
        private static bool AsyncSubmitDrainCycle()
        {
            try
            {
                if (!OperatingSystem.IsMacOS())
                {
                    Console.WriteLine("  SKIP Metal async (not macOS)");
                    return true;
                }
                if (!RHIDiscovery.IsMetalSupported())
                {
                    Console.WriteLine("  - Async submit/drain cycle: SKIPPED (headless/sandboxed session without MTLDevice)");
                    return true;
                }

                var device = new BlueSky.BSR.RHI.Metal.MetalDevice(
                    BlueSky.Aegis.Detection.IntelligentQueryLayer.Probe());

                // Frame index must rotate 1,2,0,1,2 over 5 begins.
                int[] seen = new int[5];
                for (int i = 0; i < 5; i++)
                    seen[i] = device.BeginFrame();
                bool rotationOk = seen[0] == 1 && seen[1] == 2 && seen[2] == 0
                    && seen[3] == 1 && seen[4] == 2;

                // Commit 5 no-wait frames (past the 3-deep limit) on a tiny target.
                using var target = device.CreateTexture(new TextureDesc
                {
                    Width = 64, Height = 64, Depth = 1,
                    MipLevels = 1, ArrayLayers = 1,
                    Format = TextureFormat.BGRA8Unorm,
                    Usage = TextureUsage.RenderTarget,
                    DebugName = "MetalAsync.Smoke",
                });
                for (int i = 0; i < 5; i++)
                {
                    using var cmd = device.CreateCommandBuffer();
                    cmd.BeginRenderPass(target, ClearValue.FromColor(0.1f * i, 0, 0, 1));
                    cmd.EndRenderPass();
                    device.SubmitNoWait(cmd);
                    device.BeginFrame(); // throttles if the GPU falls behind
                }
                device.DrainPending();

                // Upload path still works after async cycling.
                using var buf = device.CreateBuffer(new BufferDesc
                {
                    Size = 64, Usage = BufferUsage.Uniform,
                    MemoryType = MemoryType.CpuToGpu, DebugName = "MetalAsync.UB",
                });
                Span<byte> pattern = stackalloc byte[64];
                for (int i = 0; i < 64; i++) pattern[i] = (byte)i;
                device.UpdateBuffer(buf, pattern);

                device.Dispose();
                bool ok = rotationOk;
                Console.WriteLine($"  {(ok ? "✓" : "✗")} Async submit/drain cycle (rotation={rotationOk}): {(ok ? "PASSED" : "FAILED")}");
                return ok;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Async submit/drain cycle: FAILED ({ex.Message})");
                return false;
            }
        }
    }
}
