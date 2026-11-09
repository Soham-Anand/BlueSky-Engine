using System;
using System.Threading;

namespace BlueSky.Echo;

/// <summary>
/// Live DSP chain for one Echo Super voice.
///
/// The chain is owned by the audio thread. UI edits are posted as immutable
/// clones through <see cref="PostSuper"/> and picked up at the next block, so
/// knobs never allocate on the audio thread. Only a structural rack edit
/// (add/remove/reorder) rebuilds processors.
/// </summary>
internal sealed class EchoEffectChain
{
    private interface IProcessor
    {
        EchoEffectType EffectType { get; }
        void Update(EchoEffectSettings settings, int sampleRate);
        void Process(float[] buffer, int frames, int channels);
        void Reset();
    }

    private readonly int _sampleRate;
    private IProcessor[] _processors = Array.Empty<IProcessor>();
    private EchoSuperAsset? _pending;
    private bool _bypassed;

    public EchoEffectChain(int sampleRate, int channels)
    {
        _sampleRate = Math.Max(8000, sampleRate);
    }

    public void PostSuper(EchoSuperAsset? super)
    {
        Interlocked.Exchange(ref _pending, super?.Clone());
    }

    public void SetBypassed(bool bypassed) => _bypassed = bypassed;

    public void Process(float[] buffer, int frames, int channels)
    {
        var pending = Interlocked.Exchange(ref _pending, null);
        if (pending != null)
            Apply(pending);

        if (_bypassed || _processors.Length == 0)
            return;

        for (int i = 0; i < _processors.Length; i++)
            _processors[i].Process(buffer, frames, channels);
    }

    public void Reset()
    {
        for (int i = 0; i < _processors.Length; i++)
            _processors[i].Reset();
    }

    private void Apply(EchoSuperAsset? super)
    {
        var effects = super?.Effects;
        int count = effects?.Count ?? 0;

        bool rebuild = _processors.Length != count;
        if (!rebuild)
        {
            for (int i = 0; i < count; i++)
            {
                if (_processors[i].EffectType != effects![i].Type)
                {
                    rebuild = true;
                    break;
                }
            }
        }

        if (rebuild)
        {
            var processors = new IProcessor[count];
            for (int i = 0; i < count; i++)
                processors[i] = CreateProcessor(effects![i].Type);
            _processors = processors;
        }

        for (int i = 0; i < count; i++)
            _processors[i].Update(effects![i], _sampleRate);
    }

    private static IProcessor CreateProcessor(EchoEffectType type) => type switch
    {
        EchoEffectType.Gain => new GainProcessor(),
        EchoEffectType.Equalizer => new EqualizerProcessor(),
        EchoEffectType.LowPass => new FilterProcessor(highPass: false),
        EchoEffectType.HighPass => new FilterProcessor(highPass: true),
        EchoEffectType.Delay => new DelayProcessor(),
        EchoEffectType.Reverb => new ReverbProcessor(),
        EchoEffectType.Compressor => new CompressorProcessor(),
        EchoEffectType.Distortion => new DistortionProcessor(),
        _ => new GainProcessor()
    };

    private static float DbToLinear(float db) => MathF.Pow(10f, db / 20f);
    private static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);

    // ── Gain ──────────────────────────────────────────────────────────────
    private sealed class GainProcessor : IProcessor
    {
        private bool _enabled = true;
        private float _gain = 1f;

        public EchoEffectType EffectType => EchoEffectType.Gain;

        public void Update(EchoEffectSettings settings, int sampleRate)
        {
            _enabled = settings.Enabled;
            _gain = DbToLinear(Clamp(settings.GainDb, -48f, 24f));
        }

        public void Process(float[] buffer, int frames, int channels)
        {
            if (!_enabled) return;
            int count = frames * channels;
            for (int i = 0; i < count; i++)
                buffer[i] *= _gain;
        }

        public void Reset() { }
    }

    // ── 3-band EQ ─────────────────────────────────────────────────────────
    private sealed class EqualizerProcessor : IProcessor
    {
        private readonly EchoBiquad _lowL = new(), _lowR = new();
        private readonly EchoBiquad _midL = new(), _midR = new();
        private readonly EchoBiquad _highL = new(), _highR = new();
        private bool _enabled = true;
        private float _mix = 1f;

        public EchoEffectType EffectType => EchoEffectType.Equalizer;

        public void Update(EchoEffectSettings settings, int sampleRate)
        {
            _enabled = settings.Enabled;
            _mix = Clamp(settings.Mix, 0f, 1f);
            float low = Clamp(settings.LowGainDb, -18f, 18f);
            float mid = Clamp(settings.MidGainDb, -18f, 18f);
            float high = Clamp(settings.HighGainDb, -18f, 18f);
            float midFreq = Clamp(settings.MidFrequency, 100f, sampleRate * 0.4f);

            _lowL.SetLowShelf(low, 200f, sampleRate);
            _lowR.SetLowShelf(low, 200f, sampleRate);
            _midL.SetPeaking(mid, midFreq, 0.9f, sampleRate);
            _midR.SetPeaking(mid, midFreq, 0.9f, sampleRate);
            _highL.SetHighShelf(high, 4000f, sampleRate);
            _highR.SetHighShelf(high, 4000f, sampleRate);
        }

        public void Process(float[] buffer, int frames, int channels)
        {
            if (!_enabled || _mix <= 0.0001f) return;
            int stride = channels;
            for (int f = 0; f < frames; f++)
            {
                int i = f * stride;
                float dryL = buffer[i];
                float dryR = buffer[i + 1];
                float wetL = _highL.Process(_midL.Process(_lowL.Process(dryL)));
                float wetR = _highR.Process(_midR.Process(_lowR.Process(dryR)));
                buffer[i] = dryL + (wetL - dryL) * _mix;
                buffer[i + 1] = dryR + (wetR - dryR) * _mix;
            }
        }

        public void Reset()
        {
            _lowL.Reset(); _lowR.Reset();
            _midL.Reset(); _midR.Reset();
            _highL.Reset(); _highR.Reset();
        }
    }

    // ── Low/high pass ─────────────────────────────────────────────────────
    private sealed class FilterProcessor : IProcessor
    {
        private readonly bool _highPass;
        private readonly EchoBiquad _left = new(), _right = new();
        private bool _enabled = true;
        private float _mix = 1f;

        public FilterProcessor(bool highPass) => _highPass = highPass;

        public EchoEffectType EffectType => _highPass ? EchoEffectType.HighPass : EchoEffectType.LowPass;

        public void Update(EchoEffectSettings settings, int sampleRate)
        {
            _enabled = settings.Enabled;
            _mix = Clamp(settings.Mix, 0f, 1f);
            float cutoff = Clamp(settings.Cutoff, 20f, sampleRate * 0.45f);
            float q = Clamp(settings.Resonance, 0.1f, 20f);

            if (_highPass)
            {
                _left.SetHighPass(cutoff, q, sampleRate);
                _right.SetHighPass(cutoff, q, sampleRate);
            }
            else
            {
                _left.SetLowPass(cutoff, q, sampleRate);
                _right.SetLowPass(cutoff, q, sampleRate);
            }
        }

        public void Process(float[] buffer, int frames, int channels)
        {
            if (!_enabled || _mix <= 0.0001f) return;
            int stride = channels;
            for (int f = 0; f < frames; f++)
            {
                int i = f * stride;
                float dryL = buffer[i];
                float dryR = buffer[i + 1];
                float wetL = _left.Process(dryL);
                float wetR = _right.Process(dryR);
                buffer[i] = dryL + (wetL - dryL) * _mix;
                buffer[i + 1] = dryR + (wetR - dryR) * _mix;
            }
        }

        public void Reset() { _left.Reset(); _right.Reset(); }
    }

    // ── Delay ─────────────────────────────────────────────────────────────
    private sealed class DelayProcessor : IProcessor
    {
        private float[] _left = Array.Empty<float>();
        private float[] _right = Array.Empty<float>();
        private int _write;
        private int _size;
        private int _delaySamples = 1;
        private float _feedback;
        private float _mix = 0.25f;
        private bool _pingPong;
        private bool _enabled = true;

        public EchoEffectType EffectType => EchoEffectType.Delay;

        public void Update(EchoEffectSettings settings, int sampleRate)
        {
            _enabled = settings.Enabled;
            int size = Math.Max(16, sampleRate * 2 + 4);
            if (_size != size)
            {
                _size = size;
                _left = new float[size];
                _right = new float[size];
                _write = 0;
            }

            float time = Clamp(settings.DelayTime, 0.001f, 2.0f);
            _delaySamples = Math.Clamp((int)(time * sampleRate), 1, _size - 2);
            _feedback = Clamp(settings.Feedback, 0f, 0.95f);
            _mix = Clamp(settings.Mix, 0f, 1f);
            _pingPong = settings.PingPong;
        }

        public void Process(float[] buffer, int frames, int channels)
        {
            if (!_enabled || _mix <= 0.0001f) return;
            int stride = channels;
            for (int f = 0; f < frames; f++)
            {
                int i = f * stride;
                int read = _write - _delaySamples;
                if (read < 0) read += _size;

                float wetL = _left[read];
                float wetR = _right[read];

                float feedL = _pingPong ? wetR : wetL;
                float feedR = _pingPong ? wetL : wetR;

                _left[_write] = buffer[i] + feedL * _feedback;
                _right[_write] = buffer[i + 1] + feedR * _feedback;

                _write++;
                if (_write >= _size) _write = 0;

                buffer[i] = buffer[i] * (1f - _mix) + wetL * _mix;
                buffer[i + 1] = buffer[i + 1] * (1f - _mix) + wetR * _mix;
            }
        }

        public void Reset()
        {
            Array.Clear(_left);
            Array.Clear(_right);
            _write = 0;
        }
    }

    // ── Freeverb-style reverb ─────────────────────────────────────────────
    private sealed class ReverbProcessor : IProcessor
    {
        private sealed class Comb
        {
            private readonly float[] _buffer;
            private int _index;
            private float _filter;
            private float _feedback;
            private float _damp1;
            private float _damp2;

            public Comb(int size)
            {
                _buffer = new float[Math.Max(1, size)];
            }

            public void Set(float feedback, float damping)
            {
                _feedback = feedback;
                _damp1 = damping;
                _damp2 = 1f - damping;
            }

            public float Process(float input)
            {
                float output = _buffer[_index];
                _filter = output * _damp2 + _filter * _damp1;
                _buffer[_index] = input + _filter * _feedback;
                if (++_index >= _buffer.Length) _index = 0;
                return output;
            }

            public void Reset()
            {
                Array.Clear(_buffer);
                _index = 0;
                _filter = 0f;
            }
        }

        private sealed class AllPass
        {
            private readonly float[] _buffer;
            private int _index;

            public AllPass(int size)
            {
                _buffer = new float[Math.Max(1, size)];
            }

            public float Process(float input)
            {
                float buffered = _buffer[_index];
                float output = -input + buffered;
                _buffer[_index] = input + buffered * 0.5f;
                if (++_index >= _buffer.Length) _index = 0;
                return output;
            }

            public void Reset()
            {
                Array.Clear(_buffer);
                _index = 0;
            }
        }

        private static readonly int[] CombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        private static readonly int[] AllPassTuning = { 556, 441, 341, 225 };
        private const int StereoSpread = 23;

        private readonly Comb[] _combL = new Comb[8];
        private readonly Comb[] _combR = new Comb[8];
        private readonly AllPass[] _allL = new AllPass[4];
        private readonly AllPass[] _allR = new AllPass[4];

        private bool _enabled = true;
        private float _mix = 0.25f;
        private float _wet1;
        private float _wet2;

        public ReverbProcessor()
        {
            for (int i = 0; i < 8; i++)
            {
                _combL[i] = new Comb(CombTuning[i]);
                _combR[i] = new Comb(CombTuning[i] + StereoSpread);
            }
            for (int i = 0; i < 4; i++)
            {
                _allL[i] = new AllPass(AllPassTuning[i]);
                _allR[i] = new AllPass(AllPassTuning[i] + StereoSpread);
            }
            UpdateParameters(0.5f, 0.5f, 1f, 0.25f);
        }

        public EchoEffectType EffectType => EchoEffectType.Reverb;

        public void Update(EchoEffectSettings settings, int sampleRate)
        {
            _enabled = settings.Enabled;
            UpdateParameters(
                Clamp(settings.RoomSize, 0f, 1f),
                Clamp(settings.Damping, 0f, 1f),
                Clamp(settings.Width, 0f, 1f),
                Clamp(settings.Mix, 0f, 1f));
        }

        private void UpdateParameters(float roomSize, float damping, float width, float mix)
        {
            _mix = mix;
            float feedback = 0.7f + roomSize * 0.28f;
            float damp = damping * 0.4f;
            for (int i = 0; i < 8; i++)
            {
                _combL[i].Set(feedback, damp);
                _combR[i].Set(feedback, damp);
            }
            _wet1 = mix * (width * 0.5f + 0.5f);
            _wet2 = mix * ((1f - width) * 0.5f);
        }

        public void Process(float[] buffer, int frames, int channels)
        {
            if (!_enabled || _mix <= 0.0001f) return;
            int stride = channels;
            for (int f = 0; f < frames; f++)
            {
                int i = f * stride;
                float dryL = buffer[i];
                float dryR = buffer[i + 1];
                float input = (dryL + dryR) * 0.015f;

                float outL = 0f, outR = 0f;
                for (int c = 0; c < 8; c++)
                {
                    outL += _combL[c].Process(input);
                    outR += _combR[c].Process(input);
                }
                for (int a = 0; a < 4; a++)
                {
                    outL = _allL[a].Process(outL);
                    outR = _allR[a].Process(outR);
                }

                float wetL = outL * _wet1 + outR * _wet2;
                float wetR = outR * _wet1 + outL * _wet2;
                float dryMix = 1f - _mix;
                buffer[i] = dryL * dryMix + wetL;
                buffer[i + 1] = dryR * dryMix + wetR;
            }
        }

        public void Reset()
        {
            for (int i = 0; i < 8; i++)
            {
                _combL[i].Reset();
                _combR[i].Reset();
            }
            for (int i = 0; i < 4; i++)
            {
                _allL[i].Reset();
                _allR[i].Reset();
            }
        }
    }

    // ── Compressor ────────────────────────────────────────────────────────
    private sealed class CompressorProcessor : IProcessor
    {
        private bool _enabled = true;
        private float _threshold = 0.125f;
        private float _ratio = 4f;
        private float _attack = 0.99f;
        private float _release = 0.999f;
        private float _makeup = 1f;
        private float _mix = 1f;
        private float _envelope;

        public EchoEffectType EffectType => EchoEffectType.Compressor;

        public void Update(EchoEffectSettings settings, int sampleRate)
        {
            _enabled = settings.Enabled;
            _threshold = MathF.Max(0.0001f, DbToLinear(Clamp(settings.ThresholdDb, -60f, 0f)));
            _ratio = Clamp(settings.Ratio, 1f, 20f);
            _makeup = DbToLinear(Clamp(settings.MakeupDb, -24f, 24f));
            _mix = Clamp(settings.Mix, 0f, 1f);

            float attackSeconds = MathF.Max(0.0005f, settings.AttackMs / 1000f);
            float releaseSeconds = MathF.Max(0.005f, settings.ReleaseMs / 1000f);
            _attack = MathF.Exp(-1f / (sampleRate * attackSeconds));
            _release = MathF.Exp(-1f / (sampleRate * releaseSeconds));
        }

        public void Process(float[] buffer, int frames, int channels)
        {
            if (!_enabled) return;
            int stride = channels;
            for (int f = 0; f < frames; f++)
            {
                int i = f * stride;
                float abs = MathF.Max(MathF.Abs(buffer[i]), MathF.Abs(buffer[i + 1]));
                float coefficient = abs > _envelope ? _attack : _release;
                _envelope = coefficient * _envelope + (1f - coefficient) * abs;

                float gain = 1f;
                if (_envelope > _threshold && _envelope > 1e-6f)
                    gain = MathF.Pow(_envelope / _threshold, 1f / _ratio - 1f);

                float targetL = buffer[i] * gain * _makeup;
                float targetR = buffer[i + 1] * gain * _makeup;
                buffer[i] = buffer[i] * (1f - _mix) + targetL * _mix;
                buffer[i + 1] = buffer[i + 1] * (1f - _mix) + targetR * _mix;
            }
        }

        public void Reset() => _envelope = 0f;
    }

    // ── Distortion ────────────────────────────────────────────────────────
    private sealed class DistortionProcessor : IProcessor
    {
        private bool _enabled = true;
        private float _drive = 10f;
        private float _normalizer = 1f;
        private float _toneAlpha = 0.5f;
        private float _mix = 0.5f;
        private float _toneL;
        private float _toneR;

        public EchoEffectType EffectType => EchoEffectType.Distortion;

        public void Update(EchoEffectSettings settings, int sampleRate)
        {
            _enabled = settings.Enabled;
            _drive = 1f + Clamp(settings.Drive, 0f, 1f) * 24f;
            _normalizer = 1f / MathF.Max(0.001f, MathF.Tanh(_drive));
            float cutoff = 400f + Clamp(settings.Tone, 0f, 1f) * 11000f;
            _toneAlpha = 1f - MathF.Exp(-2f * MathF.PI * cutoff / sampleRate);
            _mix = Clamp(settings.Mix, 0f, 1f);
        }

        public void Process(float[] buffer, int frames, int channels)
        {
            if (!_enabled || _mix <= 0.0001f) return;
            int stride = channels;
            for (int f = 0; f < frames; f++)
            {
                int i = f * stride;
                float dryL = buffer[i];
                float dryR = buffer[i + 1];
                float shapedL = MathF.Tanh(dryL * _drive) * _normalizer;
                float shapedR = MathF.Tanh(dryR * _drive) * _normalizer;
                _toneL += _toneAlpha * (shapedL - _toneL);
                _toneR += _toneAlpha * (shapedR - _toneR);
                buffer[i] = dryL * (1f - _mix) + _toneL * _mix;
                buffer[i + 1] = dryR * (1f - _mix) + _toneR * _mix;
            }
        }

        public void Reset() { _toneL = 0f; _toneR = 0f; }
    }
}

/// <summary>Minimal RBJ biquad with per-instance state.</summary>
internal struct EchoBiquad
{
    private float _b0, _b1, _b2, _a1, _a2;
    private float _z1, _z2;

    public float Process(float input)
    {
        float output = _b0 * input + _z1;
        _z1 = _b1 * input - _a1 * output + _z2;
        _z2 = _b2 * input - _a2 * output;
        return output;
    }

    public void Reset() { _z1 = 0f; _z2 = 0f; }

    private void Set(float b0, float b1, float b2, float a0, float a1, float a2)
    {
        if (MathF.Abs(a0) < 1e-9f) a0 = 1f;
        float inv = 1f / a0;
        _b0 = b0 * inv;
        _b1 = b1 * inv;
        _b2 = b2 * inv;
        _a1 = a1 * inv;
        _a2 = a2 * inv;
    }

    public void SetLowShelf(float gainDb, float frequency, int sampleRate)
    {
        float a = MathF.Pow(10f, gainDb / 40f);
        float w0 = 2f * MathF.PI * Math.Clamp(frequency, 20f, sampleRate * 0.45f) / sampleRate;
        float cos = MathF.Cos(w0);
        float sin = MathF.Sin(w0);
        float alpha = sin * 0.5f * MathF.Sqrt((a + 1f / a) * (1f - 1f) + 2f);
        float twoSqrtAAlpha = 2f * MathF.Sqrt(a) * alpha;

        float b0 = a * ((a + 1f) + (a - 1f) * cos + twoSqrtAAlpha);
        float b1 = -2f * a * ((a - 1f) + (a + 1f) * cos);
        float b2 = a * ((a + 1f) + (a - 1f) * cos - twoSqrtAAlpha);
        float a0 = (a + 1f) - (a - 1f) * cos + twoSqrtAAlpha;
        float a1 = 2f * ((a - 1f) - (a + 1f) * cos);
        float a2 = (a + 1f) - (a - 1f) * cos - twoSqrtAAlpha;
        Set(b0, b1, b2, a0, a1, a2);
    }

    public void SetHighShelf(float gainDb, float frequency, int sampleRate)
    {
        float a = MathF.Pow(10f, gainDb / 40f);
        float w0 = 2f * MathF.PI * Math.Clamp(frequency, 20f, sampleRate * 0.45f) / sampleRate;
        float cos = MathF.Cos(w0);
        float sin = MathF.Sin(w0);
        float alpha = sin * 0.5f * MathF.Sqrt((a + 1f / a) * (1f - 1f) + 2f);
        float twoSqrtAAlpha = 2f * MathF.Sqrt(a) * alpha;

        float b0 = a * ((a + 1f) - (a - 1f) * cos + twoSqrtAAlpha);
        float b1 = 2f * a * ((a - 1f) - (a + 1f) * cos);
        float b2 = a * ((a + 1f) - (a - 1f) * cos - twoSqrtAAlpha);
        float a0 = (a + 1f) + (a - 1f) * cos + twoSqrtAAlpha;
        float a1 = -2f * ((a - 1f) + (a + 1f) * cos);
        float a2 = (a + 1f) + (a - 1f) * cos - twoSqrtAAlpha;
        Set(b0, b1, b2, a0, a1, a2);
    }

    public void SetPeaking(float gainDb, float frequency, float q, int sampleRate)
    {
        float a = MathF.Pow(10f, gainDb / 40f);
        float w0 = 2f * MathF.PI * Math.Clamp(frequency, 20f, sampleRate * 0.45f) / sampleRate;
        float cos = MathF.Cos(w0);
        float sin = MathF.Sin(w0);
        float alpha = sin / (2f * MathF.Max(0.1f, q));

        float b0 = 1f + alpha * a;
        float b1 = -2f * cos;
        float b2 = 1f - alpha * a;
        float a0 = 1f + alpha / a;
        float a1 = -2f * cos;
        float a2 = 1f - alpha / a;
        Set(b0, b1, b2, a0, a1, a2);
    }

    public void SetLowPass(float frequency, float q, int sampleRate)
    {
        float w0 = 2f * MathF.PI * Math.Clamp(frequency, 20f, sampleRate * 0.45f) / sampleRate;
        float cos = MathF.Cos(w0);
        float sin = MathF.Sin(w0);
        float alpha = sin / (2f * MathF.Max(0.1f, q));

        float b0 = (1f - cos) * 0.5f;
        float b1 = 1f - cos;
        float b2 = b0;
        float a0 = 1f + alpha;
        float a1 = -2f * cos;
        float a2 = 1f - alpha;
        Set(b0, b1, b2, a0, a1, a2);
    }

    public void SetHighPass(float frequency, float q, int sampleRate)
    {
        float w0 = 2f * MathF.PI * Math.Clamp(frequency, 20f, sampleRate * 0.45f) / sampleRate;
        float cos = MathF.Cos(w0);
        float sin = MathF.Sin(w0);
        float alpha = sin / (2f * MathF.Max(0.1f, q));

        float b0 = (1f + cos) * 0.5f;
        float b1 = -(1f + cos);
        float b2 = b0;
        float a0 = 1f + alpha;
        float a1 = -2f * cos;
        float a2 = 1f - alpha;
        Set(b0, b1, b2, a0, a1, a2);
    }
}
