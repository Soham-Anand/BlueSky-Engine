using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;

namespace BlueSky.Echo;

/// <summary>
/// Bridges SkyCore <see cref="AudioSourceComponent"/> entities into Echo.
///
/// Each entity with an AudioSourceComponent + TransformComponent becomes a
/// voice: the system lazily loads its clip, starts playback when
/// <c>PlayOnStart</c> or <c>IsPlaying</c> is set, follows the entity's world
/// position for 3D attenuation, and stops the voice when the component is
/// removed, the entity is destroyed, or playback finishes.
///
/// Call <see cref="Update"/> before <see cref="Echo.Update"/> each frame so
/// component edits and transforms land on the backend in the same frame.
/// </summary>
public sealed class EchoSystem : IDisposable
{
    private sealed class Voice
    {
        public AudioSource Source = null!;
        public string ClipPath = "";
        public string ClipName = "";
        public bool WasPlayOnStart;
    }

    private readonly Echo _echo;
    private readonly World _world;
    private readonly Dictionary<Entity, Voice> _voices = new();
    private readonly HashSet<Entity> _seen = new();
    private readonly HashSet<string> _missingClipWarnings = new();
    private bool _disposed;

    public EchoSystem(Echo echo, World world)
    {
        _echo = echo ?? throw new ArgumentNullException(nameof(echo));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>Number of currently playing voices owned by this system.</summary>
    public int ActiveVoiceCount => _voices.Count;

    public void Update(float deltaTime)
    {
        if (_disposed)
            return;

        _seen.Clear();

        var query = _world.CreateQuery()
            .All<AudioSourceComponent>()
            .All<TransformComponent>()
            .Build();

        var chunks = _world.GetQueryChunks(query);
        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();
            int audioIndex = chunk.GetComponentIndex(typeof(AudioSourceComponent));
            int transformIndex = chunk.GetComponentIndex(typeof(TransformComponent));

            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                _seen.Add(entity);

                ref var audio = ref chunk.GetComponent<AudioSourceComponent>(i, audioIndex);
                ref var transform = ref chunk.GetComponent<TransformComponent>(i, transformIndex);

                string clipPath = audio.ClipPath;
                if (string.IsNullOrWhiteSpace(clipPath))
                {
                    StopVoice(entity);
                    continue;
                }

                // Reload if the entity switched clips.
                if (_voices.TryGetValue(entity, out var existing) && existing.ClipPath != clipPath)
                    StopVoice(entity);

                bool wantsPlayback = audio.IsPlaying || audio.PlayOnStart;

                if (!_voices.TryGetValue(entity, out var voice))
                {
                    if (!wantsPlayback)
                        continue;

                    voice = StartVoice(entity, ref audio, transform, clipPath);
                    if (voice == null)
                        continue;
                }
                else if (!wantsPlayback)
                {
                    audio.IsPlaying = false;
                    StopVoice(entity);
                    continue;
                }

                // A one-shot that finished: clear the flag and allow a replay.
                if (voice.Source.IsPlaying == false)
                {
                    audio.IsPlaying = false;
                    _echo.StopSound(voice.Source);
                    _voices.Remove(entity);
                    continue;
                }

                // Sync live properties from the component.
                voice.Source.Position = ToNumerics(transform.Position);
                voice.Source.Volume = Math.Clamp(audio.Volume, 0f, 8f);
                voice.Source.Pitch = Math.Clamp(audio.Pitch, 0.01f, 8f);
                voice.Source.MinDistance = Math.Max(0.01f, audio.MinDistance);
                voice.Source.MaxDistance = Math.Max(voice.Source.MinDistance, audio.MaxDistance);
                voice.Source.Loop = audio.Loop;
                voice.Source.Is3D = audio.Is3D;
                voice.Source.AttenuationCurve = audio.AttenuationCurve;
            }
        }

        // Stop voices whose entity/component disappeared this frame.
        if (_voices.Count > 0)
        {
            var stale = new List<Entity>();
            foreach (var entity in _voices.Keys)
            {
                if (!_seen.Contains(entity))
                    stale.Add(entity);
            }
            foreach (var entity in stale)
                StopVoice(entity);
        }
    }

    /// <summary>Stop every voice (used when leaving Play mode).</summary>
    public void StopAll()
    {
        if (_voices.Count == 0)
            return;

        foreach (var entity in new List<Entity>(_voices.Keys))
        {
            // Re-arm PlayOnStart so entering Play mode again replays the sound.
            if (_voices.TryGetValue(entity, out var voice) && voice.WasPlayOnStart &&
                _world.TryGetComponent<AudioSourceComponent>(entity, out var audio))
            {
                audio.PlayOnStart = true;
                audio.IsPlaying = false;
                _world.AddComponent(entity, audio);
            }

            StopVoice(entity);
        }
    }

    private Voice? StartVoice(Entity entity, ref AudioSourceComponent audio, TransformComponent transform, string clipPath)
    {
        // PlayOnStart is a one-shot trigger: consume it before loading. If the
        // clip is missing or fails to start, restore it so fixing the path and
        // replaying works, while logging the missing clip only once.
        bool wasPlayOnStart = audio.PlayOnStart;
        audio.PlayOnStart = false;

        string resolved = BlueSky.Core.Assets.AssetPathResolver.Resolve(clipPath);
        if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
        {
            if (_missingClipWarnings.Add(clipPath))
                Console.WriteLine($"[Echo] Audio clip not found for entity {entity.Id}: {clipPath}");
            audio.PlayOnStart = wasPlayOnStart;
            return null;
        }

        string clipName = Path.GetFileNameWithoutExtension(resolved);
        _echo.LoadClip(clipName, resolved);
        _missingClipWarnings.Remove(clipPath);

        var position = ToNumerics(transform.Position);
        var source = _echo.PlaySound(clipName, position, Math.Clamp(audio.Volume, 0f, 8f), audio.Loop, AudioChannel.SFX, audio.Is3D);
        if (source == null)
        {
            audio.PlayOnStart = wasPlayOnStart;
            return null;
        }

        source.Pitch = Math.Clamp(audio.Pitch, 0.01f, 8f);
        source.MinDistance = Math.Max(0.01f, audio.MinDistance);
        source.MaxDistance = Math.Max(source.MinDistance, audio.MaxDistance);
        source.Is3D = audio.Is3D;
        source.Loop = audio.Loop;
        source.AttenuationCurve = audio.AttenuationCurve;

        var voice = new Voice
        {
            Source = source,
            ClipPath = clipPath,
            ClipName = clipName,
            WasPlayOnStart = wasPlayOnStart
        };

        _voices[entity] = voice;
        audio.IsPlaying = true;
        string kind = IsSuperPath(resolved) ? "Echo Super" : "clip";
        Console.WriteLine($"[Echo] Playing {kind} '{clipName}' on entity {entity.Id} (volume {audio.Volume:0.##}, {(audio.Is3D ? "3D" : "2D")}{(audio.Loop ? ", looping" : "")})");
        return voice;
    }

    private static bool IsSuperPath(string path) =>
        Path.GetExtension(path).Equals(EchoSuperAsset.Extension, StringComparison.OrdinalIgnoreCase);

    private void StopVoice(Entity entity)
    {
        if (!_voices.TryGetValue(entity, out var voice))
            return;

        try { _echo.StopSound(voice.Source); }
        catch { /* best effort */ }

        _voices.Remove(entity);
    }

    private static Vector3 ToNumerics(BlueSky.Ramanujan.Vector3 v) => new(v.X, v.Y, v.Z);

    public void Dispose()
    {
        if (_disposed)
            return;

        StopAll();
        _disposed = true;
    }
}
