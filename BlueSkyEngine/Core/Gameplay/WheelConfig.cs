using System;
using BlueSky.Core.Math;

namespace BlueSky.Core.Gameplay
{
    /// <summary>
    /// Per-wheel configuration data structure
    /// </summary>
    public struct WheelConfig
    {
        /// <summary>
        /// Wheel mount point relative to car center
        /// </summary>
        public Vector3 LocalPosition;
        
        /// <summary>
        /// Natural spring length (0.5m typical)
        /// </summary>
        public float SuspensionRestLength;
        
        /// <summary>
        /// Spring constant (35000 N/m for sports car)
        /// </summary>
        public float SuspensionStiffness;
        
        /// <summary>
        /// Damping coefficient (4500 Ns/m)
        /// </summary>
        public float SuspensionDamping;
        
        /// <summary>
        /// Tire radius (0.35m)
        /// </summary>
        public float WheelRadius;
        
        /// <summary>
        /// Receives motor torque?
        /// </summary>
        public bool IsDriveWheel;
        
        /// <summary>
        /// Turns with steering input?
        /// </summary>
        public bool IsSteerWheel;
        
        /// <summary>
        /// Degrees (for front wheels)
        /// </summary>
        public float MaxSteerAngle;
        
        /// <summary>
        /// 1.0 = normal grip
        /// </summary>
        public float TractionMultiplier;
    }
}