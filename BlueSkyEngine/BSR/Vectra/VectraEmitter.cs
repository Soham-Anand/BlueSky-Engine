using System;
using System.Numerics;

namespace BlueSky.BSR.Vectra
{
    /// <summary>
    /// Emitter configuration: everything an artist tunes. Stateless data;
    /// <see cref="VectraEmitter"/> carries the runtime accumulator.
    /// </summary>
    public struct VectraEmitterConfig
    {
        public float RatePerSec;
        public float LifeMin;
        public float LifeMax;
        public float SpeedMin;
        public float SpeedMax;
        public Vector3 Direction;
        public float ConeAngleRad;
        public float Gravity;
        public float Drag;
        public float SizeMin;
        public float SizeMax;
        public float GrowRate;
        public Vector3 ColorA;
        public Vector3 ColorB;
        public float Alpha;
        public bool Additive;
        public float SpawnRadius;
        public float Turbulence;

        public static VectraEmitterConfig Smoke() => new()
        {
            // 22 puffs/sec cuts fragment overdraw by >50% (preserving 175+ FPS on
            // Retina M4) while letting individual cauliflower billows stay crisp
            // instead of averaging into a blurry fog.
            RatePerSec = 22f,
            LifeMin = 3.2f,
            LifeMax = 5.4f,
            SpeedMin = 0.85f,
            SpeedMax = 1.48f,
            Direction = new Vector3(0f, 1f, 0f),
            ConeAngleRad = 0.34f,
            Gravity = 0.26f,
            Drag = 0.46f,
            // Tight nozzle emergence that mushrooms outward as the plume rises.
            SizeMin = 0.92f,
            SizeMax = 1.55f,
            GrowRate = 0.68f,
            ColorA = new Vector3(0.24f, 0.23f, 0.24f),
            ColorB = new Vector3(0.54f, 0.55f, 0.58f),
            Alpha = 0.34f,
            Additive = false,
            SpawnRadius = 0.14f,
            Turbulence = 0.42f,
        };
    }

    /// <summary>
    /// Continuous + burst emitter feeding a <see cref="VectraPool"/>.
    /// All randomness draws from the pool stream (determinism holds end to end).
    /// Position is pushed in by the caller (editor system or TeaScript).
    /// </summary>
    public sealed class VectraEmitter
    {
        public VectraEmitterConfig Config;
        public Vector3 Position;
        public bool Enabled = true;

        private float _accumulator;

        public VectraEmitter(VectraEmitterConfig config, Vector3 position)
        {
            Config = config;
            Position = position;
        }

        public int Update(VectraPool pool, float dt)
        {
            if (!Enabled || dt <= 0f)
                return 0;
            _accumulator += Config.RatePerSec * dt;
            int n = (int)MathF.Floor(_accumulator);
            _accumulator -= n;
            int spawned = 0;
            for (int k = 0; k < n; k++)
            {
                if (SpawnOne(pool))
                    spawned++;
                else
                    break;
            }
            return spawned;
        }

        internal void ResetSimulation() => _accumulator = 0f;

        public int Burst(VectraPool pool, int count)
        {
            int spawned = 0;
            for (int k = 0; k < count; k++)
            {
                if (SpawnOne(pool))
                    spawned++;
                else
                    break;
            }
            return spawned;
        }

        private bool SpawnOne(VectraPool pool)
        {
            var c = Config;
            Vector3 dir = RandomCone(pool, c.Direction, c.ConeAngleRad);
            float speed = pool.NextRange(c.SpeedMin, c.SpeedMax);
            float life = pool.NextRange(c.LifeMin, c.LifeMax);
            float size = pool.NextRange(c.SizeMin, c.SizeMax);
            float spreadAngle = pool.NextFloat() * MathF.PI * 2f;
            float spreadRadius = MathF.Sqrt(pool.NextFloat()) * Math.Max(0f, c.SpawnRadius);
            float offsetY = (pool.NextFloat() - 0.5f) * c.SpawnRadius * 0.45f;
            float mix = pool.NextFloat();
            // Per-puff alpha variance: breaks uniform stacking so overlap
            // contours can't form visible quad edges (0.4x-1.0x of base).
            float alphaVar = 0.4f + 0.6f * pool.NextFloat();
            return pool.Spawn(
                Position.X + MathF.Cos(spreadAngle) * spreadRadius,
                Position.Y + offsetY,
                Position.Z + MathF.Sin(spreadAngle) * spreadRadius,
                dir.X * speed, dir.Y * speed, dir.Z * speed,
                life, size, c.GrowRate,
                c.ColorA.X + (c.ColorB.X - c.ColorA.X) * mix,
                c.ColorA.Y + (c.ColorB.Y - c.ColorA.Y) * mix,
                c.ColorA.Z + (c.ColorB.Z - c.ColorA.Z) * mix,
                c.Alpha * alphaVar, c.Drag, c.Gravity, c.Turbulence,
                c.Additive ? 1f : 0f);
        }

        private static Vector3 RandomCone(VectraPool pool, Vector3 axis, float angleRad)
        {
            float len = axis.Length();
            Vector3 n = len > 1e-6f ? axis / len : Vector3.UnitY;
            float cosMax = MathF.Cos(Math.Clamp(angleRad, 0f, MathF.PI));
            float u = pool.NextFloat();
            float cosT = 1f - u * (1f - cosMax);
            float sinT = MathF.Sqrt(Math.Max(0f, 1f - cosT * cosT));
            float phi = pool.NextFloat() * MathF.PI * 2f;
            Vector3 t = Math.Abs(n.Y) < 0.99f
                ? Vector3.Normalize(Vector3.Cross(n, Vector3.UnitY))
                : Vector3.UnitX;
            Vector3 b = Vector3.Cross(n, t);
            return n * cosT + (t * MathF.Cos(phi) + b * MathF.Sin(phi)) * sinT;
        }
    }
}
