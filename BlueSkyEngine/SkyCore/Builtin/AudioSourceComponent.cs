using System;
using BlueSky.Echo;

namespace BlueSky.SkyCore.Builtin
{
    /// <summary>
    /// SkyCore 3D spatial audio source component backed by the Echo audio subsystem.
    /// </summary>
    public unsafe struct AudioSourceComponent
    {
        private const int PathCapacity = 256;
        private fixed char _clipPath[PathCapacity];

        public float Volume;
        public float Pitch;
        public float MinDistance;
        public float MaxDistance;
        public bool Loop;
        public bool Is3D;
        public bool PlayOnStart;
        public bool IsPlaying;
        public DistanceAttenuationCurve AttenuationCurve;

        public string ClipPath
        {
            get
            {
                fixed (char* ptr = _clipPath)
                {
                    int len = 0;
                    while (len < PathCapacity && ptr[len] != '\0') len++;
                    return len == 0 ? string.Empty : new string(ptr, 0, len);
                }
            }
            set
            {
                value ??= string.Empty;
                int len = System.Math.Min(PathCapacity - 1, value.Length);
                fixed (char* ptr = _clipPath)
                {
                    for (int i = 0; i < len; i++) ptr[i] = value[i];
                    ptr[len] = '\0';
                }
            }
        }

        public AudioSourceComponent(string clipPath, float volume = 1.0f, bool loop = false, bool is3D = true)
        {
            _clipPath[0] = '\0';
            Volume = volume;
            Pitch = 1.0f;
            MinDistance = 1.0f;
            MaxDistance = 100.0f;
            Loop = loop;
            Is3D = is3D;
            PlayOnStart = false;
            IsPlaying = false;
            AttenuationCurve = DistanceAttenuationCurve.Linear;
            ClipPath = clipPath;
        }
    }
}
