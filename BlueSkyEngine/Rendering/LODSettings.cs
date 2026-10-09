namespace BlueSky.Rendering;

/// <summary>
/// LOD metadata authored by the static mesh editor. Runtime mesh selection and
/// generation are not implemented yet.
/// </summary>
public sealed class LODSettings
{
    public int LODCount;
    public float LOD0Distance;
    public float LOD1Distance;
    public float LOD2Distance;
    public float LOD3Distance;
    public float LOD4Distance;
    public float ScreenSizeTransition;
    public int ForceLOD = -1;
}

/// <summary>Preset LOD metadata for the static mesh editor.</summary>
public static class LODPresets
{
    public static LODSettings Low => new()
    {
        LODCount = 3,
        LOD0Distance = 5.0f,
        LOD1Distance = 15.0f,
        LOD2Distance = 30.0f,
        ScreenSizeTransition = 0.3f,
        ForceLOD = 2
    };

    public static LODSettings Medium => new()
    {
        LODCount = 4,
        LOD0Distance = 8.0f,
        LOD1Distance = 20.0f,
        LOD2Distance = 40.0f,
        LOD3Distance = 60.0f,
        ScreenSizeTransition = 0.4f,
        ForceLOD = -1
    };

    public static LODSettings High => new()
    {
        LODCount = 5,
        LOD0Distance = 10.0f,
        LOD1Distance = 25.0f,
        LOD2Distance = 50.0f,
        LOD3Distance = 80.0f,
        LOD4Distance = 120.0f,
        ScreenSizeTransition = 0.5f,
        ForceLOD = -1
    };
}
