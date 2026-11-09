using System;
using BlueSky.Ramanujan;

namespace BlueSky.BSR.Vectra
{
    /// <summary>
    /// Fixed-capacity Structure-of-Arrays particle pool. Zero allocations after
    /// construction: all channels preallocated, dead particles swap-removed.
    /// Deterministic: every random draw comes from the seeded mulberry32 stream,
    /// so identical seed + identical call sequence = identical pool state.
    /// Sway is intentionally NOT simulated here; the vertex shader offsets by
    /// seed + time for free (smoother than any CPU timestep could do).
    /// </summary>
    public sealed class VectraPool
    {
        public const int MaxParticles = 8192;

        private readonly float[] _px = new float[MaxParticles];
        private readonly float[] _py = new float[MaxParticles];
        private readonly float[] _pz = new float[MaxParticles];
        private readonly float[] _vx = new float[MaxParticles];
        private readonly float[] _vy = new float[MaxParticles];
        private readonly float[] _vz = new float[MaxParticles];
        private readonly float[] _age = new float[MaxParticles];
        private readonly float[] _life = new float[MaxParticles];
        private readonly float[] _size0 = new float[MaxParticles];
        private readonly float[] _grow = new float[MaxParticles];
        private readonly float[] _r = new float[MaxParticles];
        private readonly float[] _g = new float[MaxParticles];
        private readonly float[] _b = new float[MaxParticles];
        private readonly float[] _a0 = new float[MaxParticles];
        private readonly float[] _seed = new float[MaxParticles];
        private readonly float[] _drag = new float[MaxParticles];
        private readonly float[] _grav = new float[MaxParticles];
        private readonly float[] _turbulence = new float[MaxParticles];
        private readonly float[] _add = new float[MaxParticles];

        private int _count;
        private uint _rng;
        private float _time;

        public int LiveCount => _count;
        public int Capacity => MaxParticles;

        public VectraPool(uint seed = 1337)
        {
            _rng = seed == 0 ? 1337 : seed;
        }

        public void Reseed(uint seed)
        {
            _rng = seed == 0 ? 1337 : seed;
            _time = 0f;
        }

        /// <summary>Mulberry32. Sole randomness source (determinism contract).</summary>
        public float NextFloat()
        {
            _rng += 0x6D2B79F5;
            uint t = _rng;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + ((t ^ (t >> 7)) * (t | 61));
            return ((t ^ (t >> 14)) >> 0) / 4294967296f;
        }

        public float NextRange(float min, float max) => min + (max - min) * NextFloat();

        public bool Spawn(
            float px, float py, float pz,
            float vx, float vy, float vz,
            float life, float size, float grow,
            float r, float g, float b, float a,
            float drag, float grav, float turbulence = 0.22f, float additive = 0f)
        {
            if (_count >= MaxParticles || life <= 0f || size <= 0f)
                return false;
            int i = _count++;
            _px[i] = px; _py[i] = py; _pz[i] = pz;
            _vx[i] = vx; _vy[i] = vy; _vz[i] = vz;
            _age[i] = 0f;
            _life[i] = life;
            _size0[i] = size;
            _grow[i] = grow;
            _r[i] = r; _g[i] = g; _b[i] = b; _a0[i] = a;
            _seed[i] = NextFloat();
            _drag[i] = drag;
            _grav[i] = grav;
            _turbulence[i] = Math.Max(0f, turbulence);
            _add[i] = additive >= 0.5f ? 1f : 0f;
            return true;
        }

        public void Update(float dt)
        {
            if (dt <= 0f || _count == 0)
                return;
            _time += dt;
            for (int i = _count - 1; i >= 0; i--)
            {
                float age = _age[i] + dt;
                if (age >= _life[i])
                {
                    RemoveAt(i);
                    continue;
                }
                _age[i] = age;
                // Exponential damping is stable across frame rates and large
                // timesteps; the previous linear factor changed character at
                // low frame rates and could clamp abruptly to zero.
                float damp = MathF.Exp(-Math.Max(0f, _drag[i]) * dt);
                float phase = _seed[i] * 6.2831853f + age * (1.2f + _seed[i] * 1.7f);
                float turbulence = _turbulence[i];
                // High-frequency jitter (per-particle, incoherent).
                _vx[i] += MathF.Sin(phase) * turbulence * 0.35f * dt;
                _vz[i] += MathF.Cos(phase * 0.83f + 1.7f) * turbulence * 0.35f * dt;
                // Coherent curl: a divergence-free swirl field so nearby puffs
                // orbit the same vortex instead of drifting apart. This is what
                // makes a column read as smoke rather than steam.
                if (turbulence > 0f)
                {
                    System.Numerics.Vector2 turb = CurlAccel(_px[i], _pz[i], _time, _seed[i], turbulence);
                    _vx[i] += turb.X * dt;
                    _vz[i] += turb.Y * dt;
                }
                _vx[i] *= damp;
                _vz[i] *= damp;
                _vy[i] = _vy[i] * damp + _grav[i] * dt;
                _px[i] += _vx[i] * dt;
                _py[i] += _vy[i] * dt;
                _pz[i] += _vz[i] * dt;
            }
        }

        // ── Curl noise (coherent smoke vortices) ──────────────────────────
        // Potential field ψ(x,z); horizontal flow = curl(ψ) = (∂ψ/∂z, -∂ψ/∂x),
        // which is divergence-free: puffs orbit vortex cores and never pile up.
        // Pure function of (position, time, seed) — no RNG draws, so the
        // determinism contract holds.

        private const float TurbFrequency = 0.22f;
        private const float TurbEpsilon = 0.7f;
        private const int TurbFieldSeed = 1337;

        private static System.Numerics.Vector2 CurlAccel(
            float x, float z, float time, float seed, float strength)
        {
            // Small per-particle domain offset keeps neighbours in the same
            // cell (coherence) while avoiding perfectly identical trajectories.
            float ox = seed * 0.6f;
            float oz = seed * 0.45f;
            float sx = x * TurbFrequency + ox;
            float sz = z * TurbFrequency + oz;
            float t = time * 0.25f;

            float p0 = Potential(sx, sz, t);
            float pX = Potential(sx + TurbEpsilon, sz, t);
            float pZ = Potential(sx, sz + TurbEpsilon, t);
            float dpdz = (pZ - p0) / TurbEpsilon;
            float dpdx = (pX - p0) / TurbEpsilon;
            return new System.Numerics.Vector2(dpdz, -dpdx) * strength;
        }

        private static float Potential(float x, float z, float t)
        {
            // Two octaves: broad vortices + finer curls inside them.
            float p = ValueNoise2(x + t * 0.35f, z - t * 0.22f, TurbFieldSeed);
            p += 0.5f * ValueNoise2(x * 2.1f - t * 0.5f, z * 2.1f + 11.3f, TurbFieldSeed + 77);
            return p;
        }

        private static float ValueNoise2(float x, float y, int seed)
        {
            int xi = (int)MathF.Floor(x);
            int yi = (int)MathF.Floor(y);
            float xf = x - xi;
            float yf = y - yi;
            float u = xf * xf * (3f - 2f * xf);
            float v = yf * yf * (3f - 2f * yf);
            float a = Hash2(xi, yi, seed);
            float b = Hash2(xi + 1, yi, seed);
            float c = Hash2(xi, yi + 1, seed);
            float d = Hash2(xi + 1, yi + 1, seed);
            float top = a + (b - a) * u;
            float bot = c + (d - c) * u;
            return top + (bot - top) * v;
        }

        private static float Hash2(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        private void RemoveAt(int i)
        {
            int last = --_count;
            if (i == last)
                return;
            _px[i] = _px[last]; _py[i] = _py[last]; _pz[i] = _pz[last];
            _vx[i] = _vx[last]; _vy[i] = _vy[last]; _vz[i] = _vz[last];
            _age[i] = _age[last]; _life[i] = _life[last];
            _size0[i] = _size0[last]; _grow[i] = _grow[last];
            _r[i] = _r[last]; _g[i] = _g[last]; _b[i] = _b[last]; _a0[i] = _a0[last];
            _seed[i] = _seed[last]; _drag[i] = _drag[last]; _grav[i] = _grav[last];
            _turbulence[i] = _turbulence[last]; _add[i] = _add[last];
        }

        public void Clear() => _count = 0;

        public float Age(int i) => _age[i];
        public float Life(int i) => _life[i];
        public float SizeAt(int i) => _size0[i] + _grow[i] * _age[i];

        public void Read(int i, out float px, out float py, out float pz,
            out float size, out float r, out float g, out float b, out float a, out float seed)
        {
            Read(i, out px, out py, out pz, out size, out r, out g, out b, out a, out seed, out _);
        }

        public void Read(int i, out float px, out float py, out float pz,
            out float size, out float r, out float g, out float b, out float a, out float seed, out float additive)
        {
            px = _px[i]; py = _py[i]; pz = _pz[i];
            size = _size0[i] + _grow[i] * _age[i];
            float t = _age[i] / _life[i];
            r = _r[i]; g = _g[i]; b = _b[i];
            // Smooth emergence and dissolution avoid visible particle popping.
            float fadeIn = Smooth01(Math.Clamp(t / 0.18f, 0f, 1f));
            float fadeOut = 1f - Smooth01(Math.Clamp((t - 0.48f) / 0.52f, 0f, 1f));
            a = _a0[i] * fadeIn * fadeOut;
            seed = _seed[i];
            additive = _add[i];
        }

        private static float Smooth01(float t) => t * t * (3f - 2f * t);

        /// <summary>FNV-1a over live channel bits. Determinism oracle for tests.</summary>
        public uint ComputeChecksum()
        {
            unchecked
            {
                uint h = 2166136261;
                for (int i = 0; i < _count; i++)
                {
                    h = Mix(h, _px[i]); h = Mix(h, _py[i]); h = Mix(h, _pz[i]);
                    h = Mix(h, _vx[i]); h = Mix(h, _vy[i]); h = Mix(h, _vz[i]);
                    h = Mix(h, _age[i]); h = Mix(h, _life[i]); h = Mix(h, _size0[i]);
                    h = Mix(h, _grow[i]); h = Mix(h, _r[i]); h = Mix(h, _g[i]);
                    h = Mix(h, _b[i]); h = Mix(h, _a0[i]); h = Mix(h, _seed[i]);
                    h = Mix(h, _drag[i]); h = Mix(h, _grav[i]); h = Mix(h, _turbulence[i]);
                    h = Mix(h, _add[i]);
                }
                h = (h ^ (uint)_count) * 16777619;
                return h;
            }
        }

        private static uint Mix(uint hash, float value) =>
            unchecked((hash ^ (uint)BitConverter.SingleToInt32Bits(value)) * 16777619);
    }
}
