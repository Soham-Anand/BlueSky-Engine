using System.Numerics;

namespace BlueSky.Core.WorldEnvironment
{
    /// <summary>
    /// Global environment settings shared across the engine.
    /// Optimized for O(1) access during rendering.
    /// </summary>
    public static class GlobalEnvironment
    {
        // x: Speed, y: Strength, z: Frequency, w: TimeOffset
        public static Vector4 WindParams = new Vector4(1.0f, 0.2f, 2.0f, 0.0f);
        
        public static Vector3 SunDirection = Vector3.Normalize(new Vector3(0.5f, 0.6f, 0.3f));
        public static Vector3 SunColor = new Vector3(1.0f, 0.95f, 0.8f);
    }
}
