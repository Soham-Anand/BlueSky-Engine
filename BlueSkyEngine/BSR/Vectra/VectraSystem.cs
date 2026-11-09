using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace BlueSky.BSR.Vectra
{
    /// <summary>
    /// Owns the pool plus a small emitter registry (cap 64). One Update per
    /// frame: emitters spawn, pool integrates. Sim time is measured every frame
    /// for the benchmark gate; emitters beyond the cap are rejected loudly.
    /// </summary>
    public sealed class VectraSystem
    {
        public const int MaxEmitters = 64;

        public VectraPool Pool { get; } = new VectraPool();
        public float LastSimMs { get; private set; }

        private readonly List<VectraEmitter> _emitters = new(MaxEmitters);
        private readonly Stopwatch _sw = new();

        public int EmitterCount => _emitters.Count;

        public bool AddEmitter(VectraEmitter emitter)
        {
            if (emitter == null || _emitters.Count >= MaxEmitters)
                return false;
            _emitters.Add(emitter);
            return true;
        }

        public bool RemoveEmitter(VectraEmitter emitter) => _emitters.Remove(emitter);

        public void Clear()
        {
            _emitters.Clear();
            Pool.Clear();
        }

        public void Update(float dt)
        {
            _sw.Restart();
            for (int i = 0; i < _emitters.Count; i++)
            {
                var e = _emitters[i];
                e.Update(Pool, dt);
            }
            Pool.Update(dt);
            _sw.Stop();
            LastSimMs = (float)_sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>Determinism oracle: reset + replay N fixed steps, checksum.</summary>
        public uint ReplayChecksum(uint seed, int steps, float dt)
        {
            Pool.Reseed(seed);
            Pool.Clear();
            for (int i = 0; i < _emitters.Count; i++)
                _emitters[i].ResetSimulation();
            for (int s = 0; s < steps; s++)
                Update(dt);
            return Pool.ComputeChecksum();
        }
    }
}
