using System;
using BlueSky.Core.Math;

namespace BlueSky.Core.Gameplay
{
    /// <summary>
    /// Runtime per-wheel state (updated every physics tick)
    /// </summary>
    public class WheelState
    {
        /// <summary>
        /// Wheel configuration
        /// </summary>
        public WheelConfig Config;
        
        /// <summary>
        /// Raycast hit terrain?
        /// </summary>
        public bool IsGrounded;
        
        /// <summary>
        /// 0.0 = fully extended, 1.0 = bottomed out
        /// </summary>
        public float SuspensionCompression;
        
        /// <summary>
        /// Newtons pushing car up
        /// </summary>
        public float SuspensionForce;

        /// <summary>
        /// Current spring length in meters.
        /// </summary>
        public float SuspensionLength;

        /// <summary>
        /// Spring length from the previous physics tick.
        /// </summary>
        public float PreviousSuspensionLength;
        
        /// <summary>
        /// World-space ground contact
        /// </summary>
        public Vector3 ContactPoint;
        
        /// <summary>
        /// Surface normal at contact
        /// </summary>
        public Vector3 ContactNormal;
        
        /// <summary>
        /// Longitudinal slip (0 = rolling, 1 = locked)
        /// </summary>
        public float SlipRatio;
        
        /// <summary>
        /// Lateral slip angle (radians)
        /// </summary>
        public float SlipAngle;
        
        /// <summary>
        /// Wheel spin speed (rad/s)
        /// </summary>
        public float AngularVelocity;
        
        /// <summary>
        /// Current steer angle (degrees)
        /// </summary>
        public float SteerAngle;
        
        /// <summary>
        /// World position of wheel center
        /// </summary>
        public Vector3 WorldPosition;

        /// <summary>
        /// Accumulated wheel spin angle (in radians)
        /// </summary>
        public float SpinAngle;
    }
}
