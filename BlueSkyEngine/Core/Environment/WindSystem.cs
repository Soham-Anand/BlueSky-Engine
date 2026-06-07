using BlueSky.Core.ECS;

namespace BlueSky.Core.WorldEnvironment
{
    /// <summary>
    /// Updates the global wind parameters. 
    /// High-performance system with zero per-entity cost.
    /// </summary>
    public sealed class WindSystem : SystemBase
    {
        public float BaseSpeed { get; set; } = 1.2f;
        public float BaseStrength { get; set; } = 0.25f;
        public float BaseFrequency { get; set; } = 1.8f;

        public override void Update(float dt)
        {
            // We could add procedural variation here (e.g. gusting)
            // For now, keep it stable but allow external adjustments via properties
            GlobalEnvironment.WindParams.X = BaseSpeed;
            GlobalEnvironment.WindParams.Y = BaseStrength;
            GlobalEnvironment.WindParams.Z = BaseFrequency;
        }
    }
}
