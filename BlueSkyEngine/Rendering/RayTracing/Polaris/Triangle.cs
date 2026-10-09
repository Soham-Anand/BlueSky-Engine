using System.Numerics;

namespace BlueSky.Rendering.RayTracing;

/// <summary>Triangle input accepted by the experimental Polaris CPU tracer.</summary>
public struct Triangle
{
    public Vector3 V0, V1, V2;
    public Vector3 N0, N1, N2;
    public Vector2 UV0, UV1, UV2;
}
