using System;
using System.Numerics;
using BVec3 = BlueSky.Core.Math.Vector3;
using BQuat = BlueSky.Core.Math.Quaternion;

namespace BlueSky.Core.Gameplay
{
    public class ChaseCameraController
    {
        public float BaseDistance = 7.0f;
        public float BaseHeight = 2.5f;
        public float RotationDamping = 0.25f;
        public float SpeedDistanceScale = 0.02f;
        public float LookAheadDistance = 2.0f;
        public float MaxShakeAmplitude = 0.3f;

        private Vector3 _smoothPosition;
        private Vector3 _smoothTarget;
        private Vector3 _cameraForward;
        private float _shakeAmount;
        private Vector3 _lastVelocity;

        public ChaseCameraController()
        {
            _smoothPosition = Vector3.Zero;
            _smoothTarget = Vector3.Zero;
            _cameraForward = Vector3.Zero;
            _shakeAmount = 0f;
            _lastVelocity = Vector3.Zero;
        }

        public void Update(float deltaTime, Vector3 carPosition, Quaternion carRotation, Vector3 carVelocity,
                          out Vector3 cameraPosition, out Vector3 cameraTarget)
        {
            // Determine local up and forward vectors based on car rotation
            Vector3 carForward = Vector3.Transform(Vector3.UnitZ, carRotation);
            Vector3 carUp = Vector3.Transform(Vector3.UnitY, carRotation);

            float speed = carVelocity.Length();

            // Initialize or smoothly update the camera's follow direction
            Vector3 targetDirection = carForward;

            if (speed > 1.0f)
            {
                Vector3 velocityDir = Vector3.Normalize(carVelocity);
                float forwardDot = Vector3.Dot(velocityDir, carForward);

                // If moving forward, blend the velocity direction to create a beautiful drift lag
                if (forwardDot > 0.1f)
                {
                    // 0.35f blend allows the camera to slide out beautifully when drifting
                    targetDirection = Vector3.Normalize(Vector3.Lerp(carForward, velocityDir, 0.35f));
                }
            }

            if (_cameraForward == Vector3.Zero || _cameraForward.LengthSquared() < 0.01f)
            {
                _cameraForward = targetDirection;
            }
            else
            {
                // Smoothly interpolate the follow direction. A damping speed of 4.5f feels extremely natural.
                _cameraForward = Vector3.Normalize(Vector3.Lerp(_cameraForward, targetDirection, deltaTime * 4.5f));
            }

            // Calculate dynamic target centered slightly above the car (e.g. roof/windshield level)
            // This is much more stable than look-ahead based on velocity, and keeps the car beautifully framed.
            Vector3 targetLookAt = carPosition + carUp * 1.0f;

            // Elastic dynamic distance and height relative to speed
            // As speed increases:
            // - The camera pulls back further (elastic tension)
            // - The camera raises slightly higher (better road vision)
            float dynamicDistance = BaseDistance + speed * 0.06f; 
            float dynamicHeight = BaseHeight + speed * 0.01f;

            // Clamp values to sane ranges using MathF.Max and MathF.Min for compatibility
            dynamicDistance = MathF.Max(BaseDistance, MathF.Min(dynamicDistance, BaseDistance * 2.0f));
            dynamicHeight = MathF.Max(BaseHeight, MathF.Min(dynamicHeight, BaseHeight * 1.5f));

            // Calculate the ideal world position of the camera
            Vector3 idealPosition = targetLookAt - _cameraForward * dynamicDistance + carUp * dynamicHeight;

            // Handle initialization
            if (_smoothPosition == Vector3.Zero || _smoothPosition.LengthSquared() < 0.01f)
            {
                _smoothPosition = idealPosition;
                _smoothTarget = targetLookAt;
            }

            // Elastic tracking using a smooth spring-like Lerp. 
            // 7.5f for position and 10f for target gives that perfect elastic NFS feel without jarring jitter.
            _smoothPosition = Vector3.Lerp(_smoothPosition, idealPosition, deltaTime * 7.5f);
            _smoothTarget = Vector3.Lerp(_smoothTarget, targetLookAt, deltaTime * 10.0f);

            // Impact camera shake (retained and tuned)
            Vector3 velocityChange = carVelocity - _lastVelocity;
            float impact = velocityChange.Length() * 0.015f; // slightly more sensitive impact
            if (impact > _shakeAmount)
            {
                _shakeAmount = MathF.Min(impact, MaxShakeAmplitude);
            }
            _shakeAmount *= 1f - deltaTime * 5f;
            if (_shakeAmount < 0f) _shakeAmount = 0f;

            // High-speed engine vibration (added premium feature)
            float speedVibration = 0f;
            if (speed > 20f) // Starts vibrating above ~45 mph
            {
                speedVibration = (speed - 20f) * 0.0015f;
            }
            float totalShake = _shakeAmount + speedVibration;
            totalShake = MathF.Min(totalShake, MaxShakeAmplitude);

            Vector3 shakeOffset = Vector3.Zero;
            if (totalShake > 0.005f)
            {
                var rand = new Random();
                float shakeX = (float)(rand.NextDouble() * 2 - 1) * totalShake;
                float shakeY = (float)(rand.NextDouble() * 2 - 1) * totalShake;
                float shakeZ = (float)(rand.NextDouble() * 2 - 1) * totalShake;
                shakeOffset = new Vector3(shakeX, shakeY, shakeZ);
            }

            cameraPosition = _smoothPosition + shakeOffset;
            cameraTarget = _smoothTarget;

            _lastVelocity = carVelocity;
        }

        public void Reset()
        {
            _smoothPosition = Vector3.Zero;
            _smoothTarget = Vector3.Zero;
            _cameraForward = Vector3.Zero;
            _shakeAmount = 0f;
            _lastVelocity = Vector3.Zero;
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * MathF.Min(t, 1.0f);
        }
    }
}
