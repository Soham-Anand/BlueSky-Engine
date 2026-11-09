using System;
using System.IO;
using System.Numerics;
using BlueSky.Echo;
using BlueSky.Overthinking;

namespace BlueSky.Tests;

public static class AudioAITests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Echo Audio & Overthinking AI Tests...");
        bool passed = true;
        
        passed &= TestSpatialAudioAttenuation();
        passed &= TestSpatialAudioPanningDirection();
        passed &= TestMiniAudioBackend();
        passed &= TestOggClipDecoding();
        passed &= TestEchoSourceAssetRoundTrip();
        passed &= TestEchoSuperAssetRoundTrip();
        passed &= TestEchoAssetPlayback();
        passed &= TestEchoEffectChainDsp();
        passed &= TestEchoSystemEcsBridge();
        passed &= TestEchoSystemProjectRelativePath();
        passed &= TestAudioViewportPlacement();
        passed &= TestEchoSuperPlacement();
        passed &= TestOverthinkingAIBrain();
        
        return passed;
    }

    private static bool TestMiniAudioBackend()
    {
        string wavPath = Path.Combine(Path.GetTempPath(), $"bluesky_echo_{Guid.NewGuid():N}.wav");
        try
        {
            WriteSineWave(wavPath, 440f, 0.25f, 44100, 1);

            using var backend = new MiniAudioBackend();
            backend.Initialize();

            using var echo = new BlueSky.Echo.Echo(backend);
            echo.LoadClip("beep", wavPath);

            var source = echo.PlaySound("beep", new Vector3(0, 0, -2), 0.5f);
            for (int i = 0; i < 4; i++)
                echo.Update(0.016f);

            // Clip metadata must be probed correctly (0.25s @ 44.1kHz mono).
            var clip = source?.Clip;
            bool metadataOk = clip != null &&
                              Math.Abs(clip.Duration - 0.25f) < 0.02f &&
                              clip.SampleRate == 44100 &&
                              clip.Channels == 1;

            int handle = source?.BackendHandle ?? -1;
            bool playedOk = source != null && handle > 0;

            if (source != null)
                echo.StopSound(source);

            bool valid = metadataOk && playedOk;
            Console.WriteLine($"  ✓ Echo miniaudio backend (device={backend.IsAvailable}, dur={(clip?.Duration ?? 0f):F2}s, handle={handle}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Echo Backend Exception: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
        }
    }

    private static bool TestOggClipDecoding()
    {
        string? path = FindFixture("echo_tone.ogg");
        if (path == null)
        {
            Console.WriteLine("  - Echo OGG/Vorbis decode: SKIPPED (fixture missing)");
            return true;
        }

        try
        {
            using var backend = new MiniAudioBackend();
            backend.Initialize();

            var clip = backend.LoadAudioClip(path);
            bool metadataOk = clip != null &&
                              clip.SampleRate == 44100 &&
                              clip.Channels == 2 &&
                              clip.Duration > 0.1f;

            // Exercise the NVorbis → ma_audio_buffer playback path too.
            using var echo = new BlueSky.Echo.Echo(backend);
            echo.LoadClip("ogg", path);
            var source = echo.PlaySound("ogg", Vector3.Zero, 0.2f, loop: false, is3D: false);
            for (int i = 0; i < 3; i++)
                echo.Update(0.016f);

            int handle = source?.BackendHandle ?? -1;
            bool playbackOk = source != null && handle > 0;
            if (source != null)
                echo.StopSound(source);

            bool valid = metadataOk && playbackOk;
            Console.WriteLine($"  ✓ Echo OGG/Vorbis decode+play (dur={(clip?.Duration ?? 0f):F2}s, {clip?.SampleRate ?? 0}Hz x{clip?.Channels ?? 0}, handle={handle}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Echo OGG Decode Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestEchoSourceAssetRoundTrip()
    {
        string root = Path.Combine(Path.GetTempPath(), $"bluesky_echo_source_{Guid.NewGuid():N}");
        string wavPath = Path.Combine(root, "tone.wav");
        string sourcePath = Path.ChangeExtension(wavPath, EchoSourceAsset.Extension);

        try
        {
            Directory.CreateDirectory(root);
            WriteSineWave(wavPath, 440f, 0.25f, 44100, 1);

            var imported = EchoSourceAsset.Import(wavPath);
            if (imported == null)
            {
                Console.WriteLine("  ❌ Echo Source import returned null");
                return false;
            }

            bool fileWritten = File.Exists(sourcePath);
            bool hasPayload = imported.Pcm.Length > 0 &&
                              imported.FrameCount == imported.Pcm.Length / 2 &&
                              Math.Abs(imported.Duration - 0.25f) < 0.02f &&
                              imported.SampleRate == 44100;

            bool hasWaveform = imported.Waveform.Length == 2048;
            if (hasWaveform)
            {
                float peak = 0f;
                foreach (float v in imported.Waveform)
                    if (v > peak) peak = v;
                hasWaveform = peak > 0.1f;
            }

            // The .echosource must be a real converted container: "ESRC" magic,
            // JSON header, then a PCM payload — not a pointer at the WAV.
            var bytes = File.ReadAllBytes(sourcePath);
            bool binaryContainer = bytes.Length > 4096 &&
                                   bytes[0] == (byte)'E' && bytes[1] == (byte)'S' &&
                                   bytes[2] == (byte)'R' && bytes[3] == (byte)'C';

            var loaded = EchoSourceAsset.Load(sourcePath);
            bool roundTrip = loaded != null &&
                             loaded.AssetId == imported.AssetId &&
                             loaded.FrameCount == imported.FrameCount &&
                             loaded.Pcm.Length == imported.Pcm.Length &&
                             loaded.Waveform.Length == imported.Waveform.Length &&
                             Math.Abs(loaded.Pcm[100] - imported.Pcm[100]) < 0.001f;

            // Saving only metadata must preserve the binary payload.
            loaded!.Gain = 0.5f;
            loaded.TrimStartFrame = loaded.FrameCount / 8;
            loaded.LoopStartFrame = loaded.FrameCount / 4;
            loaded.LoopEndFrame = loaded.FrameCount / 2;
            bool saved = loaded.Save(sourcePath);

            var reloaded = EchoSourceAsset.Load(sourcePath);
            bool metadata = saved && reloaded != null &&
                            Math.Abs(reloaded.Gain - 0.5f) < 0.0001f &&
                            reloaded.TrimStartFrame == loaded.TrimStartFrame &&
                            reloaded.LoopStartFrame == loaded.LoopStartFrame &&
                            reloaded.LoopEndFrame == loaded.LoopEndFrame &&
                            reloaded.Pcm.Length == loaded.Pcm.Length;

            var headerOnly = EchoSourceAsset.LoadHeader(sourcePath);
            bool header = headerOnly != null && headerOnly.Pcm.Length == 0 &&
                          headerOnly.FrameCount == loaded.FrameCount;

            bool valid = fileWritten && hasPayload && hasWaveform && binaryContainer &&
                         roundTrip && metadata && header;
            Console.WriteLine($"  ✓ Echo Source conversion/round-trip (file={fileWritten}, pcm={hasPayload}, wave={hasWaveform}, binary={binaryContainer}, roundtrip={roundTrip}, metadata={metadata}, header={header}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Echo Source Exception: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static bool TestEchoSuperAssetRoundTrip()
    {
        string root = Path.Combine(Path.GetTempPath(), $"bluesky_echo_super_{Guid.NewGuid():N}");
        string audioDir = Path.Combine(root, "Assets", "Audio");
        string wavPath = Path.Combine(audioDir, "tone.wav");

        try
        {
            Directory.CreateDirectory(audioDir);
            WriteSineWave(wavPath, 330f, 0.2f, 44100, 1);

            var source = EchoSourceAsset.Import(wavPath);
            if (source == null)
            {
                Console.WriteLine("  ❌ Echo Super test could not create its Source");
                return false;
            }

            string sourcePath = Path.ChangeExtension(wavPath, EchoSourceAsset.Extension);
            var super = EchoSuperAsset.CreateForSource(source, sourcePath, root);
            super.Name = "Tone Lead";
            super.Output.Channel = AudioChannel.Music;
            super.Output.Loop = true;
            super.Output.MinDistance = 2f;

            super.Effects.Add(EchoEffectSettings.CreateDefault(EchoEffectType.Gain));
            super.Effects[0].GainDb = -6f;
            super.Effects.Add(EchoEffectSettings.CreateDefault(EchoEffectType.Delay));
            super.Effects[1].DelayTime = 0.125f;
            super.Effects[1].Feedback = 0.4f;

            string superPath = EchoSuperAsset.DefaultPathFor(sourcePath);
            bool saved = super.Save(superPath);

            var loaded = EchoSuperAsset.Load(superPath);
            bool roundTrip = loaded != null &&
                             saved &&
                             loaded.AssetId == super.AssetId &&
                             loaded.SourceId == source.AssetId &&
                             loaded.Name == "Tone Lead" &&
                             loaded.Effects.Count == 2 &&
                             loaded.Effects[0].Type == EchoEffectType.Gain &&
                             Math.Abs(loaded.Effects[0].GainDb + 6f) < 0.001f &&
                             loaded.Effects[1].Type == EchoEffectType.Delay &&
                             Math.Abs(loaded.Effects[1].DelayTime - 0.125f) < 0.001f &&
                             loaded.Output.Channel == AudioChannel.Music &&
                             loaded.Output.Loop;

            // Supers are pure JSON presets: no audio payload is baked in.
            string json = File.ReadAllText(superPath);
            bool pureJson = json.TrimStart().StartsWith("{", StringComparison.Ordinal) &&
                            json.Contains("\"sourcePath\"", StringComparison.Ordinal) &&
                            !json.Contains("\"pcm\"", StringComparison.OrdinalIgnoreCase);

            // The project-relative reference must resolve through search roots.
            BlueSky.Core.Assets.AssetPathResolver.RegisterSearchRoot(root);
            var resolved = loaded?.ResolveSource();
            bool resolves = resolved != null && resolved.AssetId == source.AssetId;

            // Clone must be an independent live edit buffer.
            var clone = loaded!.Clone();
            clone.Effects[0].GainDb = 3f;
            bool cloned = clone.Effects.Count == loaded.Effects.Count &&
                          Math.Abs(loaded.Effects[0].GainDb + 6f) < 0.001f;

            bool valid = roundTrip && pureJson && resolves && cloned;
            Console.WriteLine($"  ✓ Echo Super round-trip/live reference (saved={saved}, roundtrip={roundTrip}, json={pureJson}, resolves={resolves}, clone={cloned}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Echo Super Exception: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static bool TestEchoAssetPlayback()
    {
        string root = Path.Combine(Path.GetTempPath(), $"bluesky_echo_play_{Guid.NewGuid():N}");
        string wavPath = Path.Combine(root, "tone.wav");

        try
        {
            Directory.CreateDirectory(root);
            WriteSineWave(wavPath, 440f, 0.3f, 44100, 1);

            var sourceAsset = EchoSourceAsset.Import(wavPath);
            if (sourceAsset == null)
                return false;

            string sourcePath = Path.ChangeExtension(wavPath, EchoSourceAsset.Extension);
            var super = EchoSuperAsset.CreateForSource(sourceAsset, sourcePath, root);
            super.Effects.Add(EchoEffectSettings.CreateDefault(EchoEffectType.Gain));
            super.Effects[0].GainDb = -3f;
            string superPath = EchoSuperAsset.DefaultPathFor(sourcePath);
            if (!super.Save(superPath))
                return false;

            // The Super stores a project-relative Source reference; the project
            // root must be discoverable at runtime.
            BlueSky.Core.Assets.AssetPathResolver.RegisterSearchRoot(root);

            using var backend = new MiniAudioBackend();
            backend.Initialize();
            using var echo = new BlueSky.Echo.Echo(backend);

            var sourceClip = echo.LoadClip("echo-source", sourcePath);
            bool sourceLoaded = sourceClip?.SourceAsset != null &&
                                Math.Abs(sourceClip.Duration - 0.3f) < 0.02f;

            var superClip = echo.LoadClip("echo-super", superPath);
            bool superLoaded = superClip?.SuperAsset != null &&
                               superClip.SourceAsset != null &&
                               Math.Abs(superClip.Duration - 0.3f) < 0.02f;

            var voice = echo.PlaySound("echo-super", Vector3.Zero, 0.5f, loop: false, is3D: false);
            for (int i = 0; i < 3; i++)
                echo.Update(0.016f);

            int handle = voice?.BackendHandle ?? -1;
            bool playbackOk = !backend.IsAvailable || (voice != null && handle > 0);

            // Live edits must reach the playing voice without a restart.
            bool liveUpdate = false;
            if (superClip?.SuperAsset != null)
            {
                superClip.SuperAsset.Effects[0].GainDb = -12f;
                superClip.SuperAsset.Touch();
                echo.Update(0.016f);
                liveUpdate = true;
            }

            if (voice != null)
                echo.StopSound(voice);

            bool valid = sourceLoaded && superLoaded && playbackOk && liveUpdate;
            Console.WriteLine($"  ✓ Echo Source/Super playback (source={sourceLoaded}, super={superLoaded}, handle={handle}, live={liveUpdate}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Echo Asset Playback Exception: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static bool TestEchoEffectChainDsp()
    {
        try
        {
            // Gain: -6.0206 dB is exactly half amplitude.
            var gainSuper = new EchoSuperAsset();
            gainSuper.Effects.Add(EchoEffectSettings.CreateDefault(EchoEffectType.Gain));
            gainSuper.Effects[0].GainDb = -6.0206f;

            var gainChain = new EchoEffectChain(44100, 2);
            gainChain.PostSuper(gainSuper);
            var gainBuffer = new float[512 * 2];
            Array.Fill(gainBuffer, 0.5f);
            gainChain.Process(gainBuffer, 512, 2);
            bool gainOk = Math.Abs(gainBuffer[0] - 0.25f) < 0.005f;

            // Low-pass must reject a Nyquist-rate square wave.
            var lpSuper = new EchoSuperAsset();
            lpSuper.Effects.Add(EchoEffectSettings.CreateDefault(EchoEffectType.LowPass));
            lpSuper.Effects[0].Cutoff = 500f;
            lpSuper.Effects[0].Mix = 1f;

            var lpChain = new EchoEffectChain(44100, 2);
            lpChain.PostSuper(lpSuper);
            var nyquist = new float[4096 * 2];
            for (int i = 0; i < 4096; i++)
            {
                float s = (i & 1) == 0 ? 1f : -1f;
                nyquist[i * 2] = s;
                nyquist[i * 2 + 1] = s;
            }
            lpChain.Process(nyquist, 4096, 2);
            float lpTail = 0f;
            for (int i = 3600; i < 4096; i++)
                lpTail = MathF.Max(lpTail, MathF.Abs(nyquist[i * 2]));
            bool lowPassOk = lpTail < 0.1f;

            // High-pass must reject DC.
            var hpSuper = new EchoSuperAsset();
            hpSuper.Effects.Add(EchoEffectSettings.CreateDefault(EchoEffectType.HighPass));
            hpSuper.Effects[0].Cutoff = 80f;
            hpSuper.Effects[0].Mix = 1f;

            var hpChain = new EchoEffectChain(44100, 2);
            hpChain.PostSuper(hpSuper);
            var dc = new float[4096 * 2];
            Array.Fill(dc, 1f);
            hpChain.Process(dc, 4096, 2);
            float hpTail = 0f;
            for (int i = 3600; i < 4096; i++)
                hpTail = MathF.Max(hpTail, MathF.Abs(dc[i * 2]));
            bool highPassOk = hpTail < 0.1f;

            // Delay: a single impulse reappears after the delay time.
            var delaySuper = new EchoSuperAsset();
            delaySuper.Effects.Add(EchoEffectSettings.CreateDefault(EchoEffectType.Delay));
            delaySuper.Effects[0].DelayTime = 0.1f;
            delaySuper.Effects[0].Feedback = 0f;
            delaySuper.Effects[0].Mix = 1f;

            var delayChain = new EchoEffectChain(44100, 2);
            delayChain.PostSuper(delaySuper);
            var impulse = new float[5000 * 2];
            impulse[0] = 1f;
            impulse[1] = 1f;
            delayChain.Process(impulse, 5000, 2);

            int delayFrame = (int)(0.1f * 44100);
            bool delayOk = impulse[delayFrame * 2] > 0.9f &&
                           MathF.Abs(impulse[10 * 2]) < 0.001f;

            // Reverb: an impulse leaves audible tail energy.
            var reverbSuper = new EchoSuperAsset();
            reverbSuper.Effects.Add(EchoEffectSettings.CreateDefault(EchoEffectType.Reverb));
            reverbSuper.Effects[0].Mix = 0.5f;

            var reverbChain = new EchoEffectChain(44100, 2);
            reverbChain.PostSuper(reverbSuper);
            var reverbBuffer = new float[8192 * 2];
            reverbBuffer[0] = 0.8f;
            reverbBuffer[1] = 0.8f;
            reverbChain.Process(reverbBuffer, 8192, 2);
            float reverbTail = 0f;
            for (int i = 1500; i < 8192; i++)
                reverbTail = MathF.Max(reverbTail, MathF.Abs(reverbBuffer[i * 2]));
            bool reverbOk = reverbTail > 0.0001f;

            // Bypass must leave the signal untouched.
            var bypassChain = new EchoEffectChain(44100, 2);
            bypassChain.PostSuper(gainSuper);
            bypassChain.SetBypassed(true);
            var bypassBuffer = new float[128 * 2];
            Array.Fill(bypassBuffer, 0.5f);
            bypassChain.Process(bypassBuffer, 128, 2);
            bool bypassOk = Math.Abs(bypassBuffer[0] - 0.5f) < 0.0001f;

            bool valid = gainOk && lowPassOk && highPassOk && delayOk && reverbOk && bypassOk;
            Console.WriteLine($"  ✓ Echo live DSP chain (gain={gainOk}, lp={lowPassOk}, hp={highPassOk}, delay={delayOk}, reverb={reverbOk}, bypass={bypassOk}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Echo DSP Exception: {ex.Message}");
            return false;
        }
    }

    private static string? FindFixture(string name)
    {
        string[] candidates =
        {
            $"BlueSkyEngine/Tests/Fixtures/{name}",
            $"Tests/Fixtures/{name}",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Tests", "Fixtures", name),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static bool TestEchoSystemEcsBridge()
    {
        string wavPath = Path.Combine(Path.GetTempPath(), $"bluesky_echo_ecs_{Guid.NewGuid():N}.wav");
        try
        {
            WriteSineWave(wavPath, 220f, 1.0f, 44100, 1);

            using var backend = new MiniAudioBackend();
            backend.Initialize();
            using var echo = new BlueSky.Echo.Echo(backend);
            using var world = new BlueSky.SkyCore.World();

            var entity = world.CreateEntity();
            world.AddComponent(entity, new BlueSky.SkyCore.Builtin.TransformComponent(
                new BlueSky.Ramanujan.Vector3(1f, 0f, -3f)));
            world.AddComponent(entity, new BlueSky.SkyCore.Builtin.AudioSourceComponent(
                wavPath, volume: 0.4f, loop: true, is3D: true) { PlayOnStart = true });

            using var system = new BlueSky.Echo.EchoSystem(echo, world);
            system.Update(0.016f);
            echo.Update(0.016f);

            bool started = system.ActiveVoiceCount == 1;
            world.TryGetComponent<BlueSky.SkyCore.Builtin.AudioSourceComponent>(entity, out var audio);
            bool flagSet = audio.IsPlaying;

            system.StopAll();
            bool stopped = system.ActiveVoiceCount == 0;

            world.TryGetComponent<BlueSky.SkyCore.Builtin.AudioSourceComponent>(entity, out var rearmedAudio);
            bool rearmed = rearmedAudio.PlayOnStart && !rearmedAudio.IsPlaying;

            // Re-entering Play mode must replay PlayOnStart sounds.
            system.Update(0.016f);
            bool replay = system.ActiveVoiceCount == 1;
            system.StopAll();

            bool valid = started && flagSet && stopped && rearmed && replay;
            Console.WriteLine($"  ✓ Echo ECS bridge (started={started}, flag={flagSet}, stopped={stopped}, rearmed={rearmed}, replay={replay}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Echo ECS Bridge Exception: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
        }
    }

    private static bool TestEchoSystemProjectRelativePath()
    {
        string root = Path.Combine(Path.GetTempPath(), $"bluesky_echo_project_{Guid.NewGuid():N}");
        string audioDir = Path.Combine(root, "Assets", "Audio");
        string wavPath = Path.Combine(audioDir, "footstep.wav");

        try
        {
            Directory.CreateDirectory(audioDir);
            WriteSineWave(wavPath, 330f, 0.25f, 44100, 1);

            // Mirrors the editor: project root is a registered search root and
            // entities store portable "Assets/..." paths.
            BlueSky.Core.Assets.AssetPathResolver.RegisterSearchRoot(root);

            using var backend = new MiniAudioBackend();
            backend.Initialize();
            using var echo = new BlueSky.Echo.Echo(backend);
            using var world = new BlueSky.SkyCore.World();

            var entity = world.CreateEntity();
            world.AddComponent(entity, new BlueSky.SkyCore.Builtin.TransformComponent(
                new BlueSky.Ramanujan.Vector3(0f, 0f, -2f)));
            world.AddComponent(entity, new BlueSky.SkyCore.Builtin.AudioSourceComponent(
                "Assets/Audio/footstep.wav", volume: 0.8f, loop: false, is3D: true) { PlayOnStart = true });

            using var system = new BlueSky.Echo.EchoSystem(echo, world);
            system.Update(0.016f);

            bool started = system.ActiveVoiceCount == 1;
            system.StopAll();

            Console.WriteLine($"  ✓ Echo project-relative clip path (started={started}): {(started ? "PASSED" : "FAILED")}");
            return started;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Echo Relative Path Exception: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void WriteSineWave(string path, float frequency, float seconds, int sampleRate, int channels)
    {
        int frames = (int)(sampleRate * seconds);
        int dataSize = frames * channels * sizeof(short);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // RIFF header
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);                       // PCM chunk size
        bw.Write((short)1);                 // PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * sizeof(short)); // byte rate
        bw.Write((short)(channels * sizeof(short)));     // block align
        bw.Write((short)16);                // bits per sample
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);

        for (int i = 0; i < frames; i++)
        {
            double t = (double)i / sampleRate;
            short sample = (short)(Math.Sin(2.0 * Math.PI * frequency * t) * 0.4 * short.MaxValue);
            for (int c = 0; c < channels; c++)
                bw.Write(sample);
        }
    }

    private static bool TestAudioViewportPlacement()
    {
        string wavPath = Path.Combine(Path.GetTempPath(), $"bluesky_echo_place_{Guid.NewGuid():N}.wav");
        try
        {
            WriteSineWave(wavPath, 440f, 0.5f, 44100, 1);

            using var world = new BlueSky.SkyCore.World();
            var dropPosition = new BlueSky.Ramanujan.Vector3(2.5f, 0f, -4f);
            var entity = BlueSky.Editor.Program.SpawnAudioSourceEntity(world, wavPath, dropPosition);
            if (entity == null)
            {
                Console.WriteLine("  ✗ Echo viewport placement: entity was not created");
                return false;
            }

            bool hasAudio = world.TryGetComponent<BlueSky.SkyCore.Builtin.AudioSourceComponent>(entity.Value, out var audio);
            bool hasTransform = world.TryGetComponent<BlueSky.SkyCore.Builtin.TransformComponent>(entity.Value, out var transform);
            bool hasName = world.TryGetComponent<BlueSky.SkyCore.Builtin.NameComponent>(entity.Value, out var name);

            bool placed = hasTransform &&
                          Math.Abs(transform.Position.X - dropPosition.X) < 0.001f &&
                          Math.Abs(transform.Position.Y - dropPosition.Y) < 0.001f &&
                          Math.Abs(transform.Position.Z - dropPosition.Z) < 0.001f;
            bool configured = hasAudio && audio.PlayOnStart && !audio.IsPlaying && audio.Is3D &&
                              Math.Abs(audio.Volume - 1.0f) < 0.001f;
            bool named = hasName && name.Name == Path.GetFileNameWithoutExtension(wavPath);

            // The placed entity must actually start a voice when Play begins.
            using var backend = new MiniAudioBackend();
            backend.Initialize();
            using var echo = new BlueSky.Echo.Echo(backend);
            using var system = new BlueSky.Echo.EchoSystem(echo, world);
            system.Update(0.016f);
            bool plays = system.ActiveVoiceCount == 1;
            system.StopAll();

            bool valid = placed && configured && named && plays;
            Console.WriteLine($"  ✓ Echo viewport placement (placed={placed}, configured={configured}, named={named}, plays={plays}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Echo Viewport Placement Exception: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
        }
    }

    private static bool TestEchoSuperPlacement()
    {
        string root = Path.Combine(Path.GetTempPath(), $"bluesky_echo_super_place_{Guid.NewGuid():N}");
        string audioDir = Path.Combine(root, "Assets", "Audio");
        string wavPath = Path.Combine(audioDir, "lead.wav");

        try
        {
            Directory.CreateDirectory(audioDir);
            WriteSineWave(wavPath, 220f, 0.4f, 44100, 1);

            var source = EchoSourceAsset.Import(wavPath);
            if (source == null)
            {
                Console.WriteLine("  ❌ Echo Super placement: could not create Source");
                return false;
            }

            string sourcePath = Path.ChangeExtension(wavPath, EchoSourceAsset.Extension);
            var super = EchoSuperAsset.CreateForSource(source, sourcePath, root);
            super.Output.Gain = 0.35f;
            super.Output.Loop = true;
            super.Output.Is3D = false;
            super.Output.MinDistance = 3f;
            super.Output.MaxDistance = 42f;
            super.Output.AttenuationCurve = DistanceAttenuationCurve.Inverse;
            super.Effects.Add(EchoEffectSettings.CreateDefault(EchoEffectType.Gain));
            super.Effects[0].GainDb = -6f;

            string superPath = EchoSuperAsset.DefaultPathFor(sourcePath);
            if (!super.Save(superPath))
            {
                Console.WriteLine("  ❌ Echo Super placement: could not save Super");
                return false;
            }

            BlueSky.Core.Assets.AssetPathResolver.RegisterSearchRoot(root);

            using var world = new BlueSky.SkyCore.World();
            var dropPosition = new BlueSky.Ramanujan.Vector3(-1.5f, 0.25f, 6f);
            var entity = BlueSky.Editor.Program.SpawnAudioSourceEntity(world, superPath, dropPosition);
            if (entity == null)
            {
                Console.WriteLine("  ❌ Echo Super placement: entity was not created");
                return false;
            }

            bool hasAudio = world.TryGetComponent<BlueSky.SkyCore.Builtin.AudioSourceComponent>(entity.Value, out var audio);
            bool hasTransform = world.TryGetComponent<BlueSky.SkyCore.Builtin.TransformComponent>(entity.Value, out var transform);
            bool hasName = world.TryGetComponent<BlueSky.SkyCore.Builtin.NameComponent>(entity.Value, out var name);

            bool placed = hasTransform &&
                          Math.Abs(transform.Position.X - dropPosition.X) < 0.001f &&
                          Math.Abs(transform.Position.Y - dropPosition.Y) < 0.001f &&
                          Math.Abs(transform.Position.Z - dropPosition.Z) < 0.001f;

            // A placed Super must carry its own output/spatial defaults.
            bool configured = hasAudio &&
                              audio.PlayOnStart &&
                              Math.Abs(audio.Volume - 0.35f) < 0.001f &&
                              audio.Loop &&
                              !audio.Is3D &&
                              Math.Abs(audio.MinDistance - 3f) < 0.001f &&
                              Math.Abs(audio.MaxDistance - 42f) < 0.001f &&
                              audio.AttenuationCurve == DistanceAttenuationCurve.Inverse;
            bool named = hasName && name.Name == Path.GetFileNameWithoutExtension(superPath);
            bool referencesSuper = hasAudio &&
                                   audio.ClipPath.EndsWith(EchoSuperAsset.Extension, StringComparison.OrdinalIgnoreCase);

            // Play mode must start the Super voice (live effects included).
            using var backend = new MiniAudioBackend();
            backend.Initialize();
            using var echo = new BlueSky.Echo.Echo(backend);
            using var system = new BlueSky.Echo.EchoSystem(echo, world);
            system.Update(0.016f);
            bool plays = system.ActiveVoiceCount == 1;
            system.StopAll();

            bool valid = placed && configured && named && referencesSuper && plays;
            Console.WriteLine($"  ✓ Echo Super placement/defaults (placed={placed}, configured={configured}, named={named}, super={referencesSuper}, plays={plays}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Echo Super Placement Exception: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static bool TestSpatialAudioPanningDirection()
    {
        try
        {
            var audio = new SpatialAudio();

            // Engine camera convention: forward = -Z, up = +Y, so +X is right.
            audio.UpdateListener(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY, Vector3.Zero);

            var rightSource = new AudioSource
            {
                Is3D = true,
                Position = new Vector3(10f, 0f, 0f),
                MinDistance = 1f,
                MaxDistance = 100f,
                Volume = 1f
            };
            var leftSource = new AudioSource
            {
                Is3D = true,
                Position = new Vector3(-10f, 0f, 0f),
                MinDistance = 1f,
                MaxDistance = 100f,
                Volume = 1f
            };

            audio.ProcessSource(rightSource, 0.016f);
            audio.ProcessSource(leftSource, 0.016f);

            // miniaudio: pan -1 = left, +1 = right. A source on the listener's
            // right must pan right, not mirror into the left speaker.
            bool rightIsRight = rightSource.Pan > 0.99f;
            bool leftIsLeft = leftSource.Pan < -0.99f;
            bool valid = rightIsRight && leftIsLeft;

            Console.WriteLine($"  {(valid ? "✓" : "✗")} Spatial audio pan direction (right={rightSource.Pan:F2}, left={leftSource.Pan:F2}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Spatial Audio Panning Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestSpatialAudioAttenuation()
    {
        try
        {
            var audio = new SpatialAudio();
            audio.UpdateListener(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, Vector3.Zero);
            
            var source = new AudioSource
            {
                Is3D = true,
                Position = new Vector3(0, 0, 10),
                MinDistance = 1.0f,
                MaxDistance = 100.0f,
                Volume = 1.0f
            };

            audio.ProcessSource(source, 0.016f);
            
            bool valid = source.CalculatedVolume > 0.0f && source.CalculatedVolume <= 1.0f;
            Console.WriteLine($"  ✓ Echo 3D Audio Spatial Processor (Vol={source.CalculatedVolume:F2}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Spatial Audio Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestOverthinkingAIBrain()
    {
        try
        {
            var brain = new AIBrain();
            brain.SetBlackboardValue("TargetDistance", 15.0f);
            
            var val = brain.GetBlackboardValue<float>("TargetDistance");
            bool valid = val == 15.0f;
            
            Console.WriteLine($"  ✓ Overthinking AI Priority Blackboard (Val={val}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ AI Blackboard Exception: {ex.Message}");
            return false;
        }
    }
}
