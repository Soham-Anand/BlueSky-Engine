using BlueSky.Rendering.PostProcessing;

namespace BlueSky.Rendering;

public enum ReflectionQualityProfile
{
    Off,
    Performance,
    Ultra60,
    Cinematic
}

public readonly struct ReflectionSettings
{
    private ReflectionSettings(
        ReflectionQualityProfile profile,
        bool enableScreenSpaceReflections,
        bool presentScreenSpaceReflections,
        SSRQuality ssrQuality,
        float maxFrameCostMs,
        bool preferGpuReflections,
        bool allowAvxFallback)
    {
        Profile = profile;
        EnableScreenSpaceReflections = enableScreenSpaceReflections;
        PresentScreenSpaceReflections = presentScreenSpaceReflections;
        SsrQuality = ssrQuality;
        MaxFrameCostMs = maxFrameCostMs;
        PreferGpuReflections = preferGpuReflections;
        AllowAvxFallback = allowAvxFallback;
    }

    public ReflectionQualityProfile Profile { get; }
    public bool EnableScreenSpaceReflections { get; }
    public bool PresentScreenSpaceReflections { get; }
    public SSRQuality SsrQuality { get; }
    public float MaxFrameCostMs { get; }
    public bool PreferGpuReflections { get; }
    public bool AllowAvxFallback { get; }

    public static ReflectionSettings Ultra60 => new(
        ReflectionQualityProfile.Ultra60,
        enableScreenSpaceReflections: true,
        presentScreenSpaceReflections: true,
        ssrQuality: SSRQuality.Medium,
        maxFrameCostMs: 2.0f,
        preferGpuReflections: true,
        allowAvxFallback: true);

    public static ReflectionSettings Performance => new(
        ReflectionQualityProfile.Performance,
        enableScreenSpaceReflections: false,
        presentScreenSpaceReflections: false,
        ssrQuality: SSRQuality.Low,
        maxFrameCostMs: 0.8f,
        preferGpuReflections: true,
        allowAvxFallback: true);

    public static ReflectionSettings Cinematic => new(
        ReflectionQualityProfile.Cinematic,
        enableScreenSpaceReflections: true,
        presentScreenSpaceReflections: true,
        ssrQuality: SSRQuality.High,
        maxFrameCostMs: 2.5f,
        preferGpuReflections: true,
        allowAvxFallback: true);
}
