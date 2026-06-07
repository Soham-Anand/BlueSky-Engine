using System;
using System.Numerics;
using BlueSky.Physics;
using BlueSky.Core.ECS;
using BVec3 = BlueSky.Core.Math.Vector3;
using BQuat = BlueSky.Core.Math.Quaternion;

namespace BlueSky.Core.Gameplay;

/// <summary>
/// Vehicle physics system for car dynamics simulation.
/// Handles suspension, tire forces, steering, and drivetrain response.
/// </summary>
public class VehiclePhysics
{
    private readonly IPhysicsWorld _physicsWorld;
    private readonly WheelState[] _wheels;
    private readonly float _vehicleMass;
    private readonly Vector3 _centerOfMassOffset;

    private const float TireGripCoefficient = 1.15f;
    private const float RollingResistanceCoefficient = 0.018f;
    private const float LateralGripResponse = 4.0f;
    private const float MaxForwardSpeed = 75.0f;       // m/s, about 168 mph
    private const float MaxGroundedVerticalSpeed = 2.5f;
    private const float MaxAirborneVerticalSpeed = 10.0f;
    private const float SuspensionForceSafety = 1.18f; // Gentle assist; the collider carries hard landings.
    private const float RaycastSkin = 0.08f;
    private const float AirborneExtraGravityScale = 1.35f;

    public VehiclePhysics(IPhysicsWorld physicsWorld, WheelState[] wheelStates, float mass, BVec3 centerOfMass,
                          float motorForce = 8000f, float brakeForce = 12000f, float maxSteerAngle = 30f)
    {
        _physicsWorld = physicsWorld;
        _wheels = wheelStates;
        _vehicleMass = mass;
        _centerOfMassOffset = new Vector3(centerOfMass.X, centerOfMass.Y, centerOfMass.Z);
        MotorForce = motorForce;
        BrakeForce = brakeForce;
        MaxSteerAngle = maxSteerAngle;
    }

    public float MotorForce { get; }
    public float BrakeForce { get; }
    public float MaxSteerAngle { get; }

    /// <summary>
    /// Main physics solver. Called once per frame.
    /// </summary>
    public void Solve(float deltaTime, float throttleInput, float brakePressure, float steerInput,
                     Entity vehicleEntity, BVec3 vehiclePos, BQuat vehicleRot)
    {
        if (_physicsWorld == null || _wheels == null || _wheels.Length == 0)
            return;
        if (!_physicsWorld.HasBody(vehicleEntity))
            return;

        Vector3 pos = new(vehiclePos.X, vehiclePos.Y, vehiclePos.Z);
        Quaternion rot = new(vehicleRot.X, vehicleRot.Y, vehicleRot.Z, vehicleRot.W);
        if (rot.LengthSquared() < 0.0001f)
            rot = Quaternion.Identity;
        else
            rot = Quaternion.Normalize(rot);

        float dt = MathF.Max(deltaTime, 0.0001f);
        Vector3 forward = SafeNormalize(Vector3.Transform(Vector3.UnitZ, rot), Vector3.UnitZ);
        Vector3 right = SafeNormalize(Vector3.Transform(Vector3.UnitX, rot), Vector3.UnitX);
        Vector3 up = SafeNormalize(Vector3.Transform(Vector3.UnitY, rot), Vector3.UnitY);
        Vector3 velocity = _physicsWorld.GetVelocity(vehicleEntity);

        int groundedCount = UpdateWheelContact(vehicleEntity, pos, rot, up, dt);

        if (groundedCount > 0)
        {
            ApplySuspension(vehicleEntity, up, dt);
            ApplyTireForces(vehicleEntity, forward, right, up, velocity, throttleInput, brakePressure, steerInput, groundedCount);
        }
        else
        {
            _physicsWorld.AddForce(vehicleEntity, Vector3.UnitY * (-_vehicleMass * 9.81f * AirborneExtraGravityScale));
        }

        ApplyAeroAndStability(vehicleEntity, forward, right, groundedCount);
        UpdateWheelState(vehicleEntity, forward, right, throttleInput, brakePressure, steerInput, dt);
    }

    private int UpdateWheelContact(Entity vehicleEntity, Vector3 vehiclePos, Quaternion vehicleRot, Vector3 suspensionUp, float deltaTime)
    {
        Vector3 rayDirection = -suspensionUp;
        int groundedCount = 0;

        for (int i = 0; i < _wheels.Length; i++)
        {
            WheelState wheel = _wheels[i];
            WheelConfig config = wheel.Config;

            Vector3 wheelLocalPos = new(config.LocalPosition.X, config.LocalPosition.Y, config.LocalPosition.Z);
            Vector3 restWheelWorldPos = vehiclePos + Vector3.Transform(wheelLocalPos, vehicleRot);
            Vector3 rayOrigin = restWheelWorldPos + suspensionUp * RaycastSkin;
            float raycastDistance = config.SuspensionRestLength + config.WheelRadius + RaycastSkin;

            bool wasGrounded = wheel.IsGrounded;
            float previousLength = wheel.SuspensionLength > 0.0001f
                ? wheel.SuspensionLength
                : config.SuspensionRestLength;

            if (_physicsWorld.Raycast(rayOrigin, rayDirection, raycastDistance, out RaycastHit hit, vehicleEntity))
            {
                float centerToGround = MathF.Max(0.0f, hit.Distance - RaycastSkin);
                float suspensionLength = System.Math.Clamp(centerToGround - config.WheelRadius, 0.0f, config.SuspensionRestLength);
                float compressionDistance = config.SuspensionRestLength - suspensionLength;
                float compression01 = compressionDistance / MathF.Max(0.01f, config.SuspensionRestLength);

                wheel.IsGrounded = true;
                wheel.PreviousSuspensionLength = wasGrounded ? previousLength : suspensionLength;
                wheel.SuspensionLength = suspensionLength;
                wheel.SuspensionCompression = System.Math.Clamp(compression01, 0.0f, 1.0f);
                wheel.ContactPoint = new BVec3(hit.Point.X, hit.Point.Y, hit.Point.Z);
                Vector3 normal = SafeNormalize(hit.Normal, Vector3.UnitY);
                wheel.ContactNormal = new BVec3(normal.X, normal.Y, normal.Z);
                Vector3 visualWheelPos = hit.Point + normal * config.WheelRadius;
                wheel.WorldPosition = new BVec3(visualWheelPos.X, visualWheelPos.Y, visualWheelPos.Z);
                groundedCount++;
            }
            else
            {
                wheel.IsGrounded = false;
                wheel.PreviousSuspensionLength = config.SuspensionRestLength;
                wheel.SuspensionLength = config.SuspensionRestLength;
                wheel.SuspensionCompression = 0.0f;
                wheel.SuspensionForce = 0.0f;
                wheel.ContactNormal = new BVec3(suspensionUp.X, suspensionUp.Y, suspensionUp.Z);
                wheel.WorldPosition = new BVec3(restWheelWorldPos.X, restWheelWorldPos.Y, restWheelWorldPos.Z);
            }
        }

        return groundedCount;
    }

    private void ApplySuspension(Entity vehicleEntity, Vector3 suspensionUp, float deltaTime)
    {
        float staticWheelLoad = _vehicleMass * 9.81f / MathF.Max(1, _wheels.Length);
        float maxSuspensionForce = staticWheelLoad * SuspensionForceSafety;

        for (int i = 0; i < _wheels.Length; i++)
        {
            WheelState wheel = _wheels[i];
            if (!wheel.IsGrounded)
                continue;

            WheelConfig config = wheel.Config;
            float compressionDistance = config.SuspensionRestLength - wheel.SuspensionLength;
            float compressionVelocity = (wheel.PreviousSuspensionLength - wheel.SuspensionLength) / MathF.Max(0.0001f, deltaTime);
            float springForce = compressionDistance * config.SuspensionStiffness;
            float dampingForce = compressionVelocity * config.SuspensionDamping;
            float suspensionForce = System.Math.Clamp(springForce + dampingForce, 0.0f, maxSuspensionForce);

            wheel.SuspensionForce = suspensionForce;
            _physicsWorld.AddForce(vehicleEntity, Vector3.UnitY * suspensionForce);
        }
    }

    private void ApplyTireForces(Entity vehicleEntity, Vector3 forward, Vector3 right, Vector3 up,
                                 Vector3 velocity, float throttleInput, float brakePressure,
                                 float steerInput, int groundedCount)
    {
        int driveWheelCount = 0;
        for (int i = 0; i < _wheels.Length; i++)
        {
            if (_wheels[i].IsGrounded && _wheels[i].Config.IsDriveWheel)
                driveWheelCount++;
        }
        driveWheelCount = System.Math.Max(1, driveWheelCount);

        for (int i = 0; i < _wheels.Length; i++)
        {
            WheelState wheel = _wheels[i];
            if (!wheel.IsGrounded)
                continue;

            WheelConfig config = wheel.Config;
            Vector3 normal = ToNumerics(wheel.ContactNormal, Vector3.UnitY);

            float steerRadians = config.IsSteerWheel
                ? steerInput * config.MaxSteerAngle * (MathF.PI / 180.0f)
                : 0.0f;
            Vector3 wheelForward = SafeNormalize(forward * MathF.Cos(steerRadians) + right * MathF.Sin(steerRadians), forward);
            wheelForward = ProjectOnPlane(wheelForward, Vector3.UnitY, forward);
            Vector3 wheelRight = ProjectOnPlane(Vector3.Cross(Vector3.UnitY, wheelForward), Vector3.UnitY, right);

            float forwardSpeed = Vector3.Dot(velocity, wheelForward);
            float lateralSpeed = Vector3.Dot(velocity, wheelRight);
            float normalLoad = MathF.Max(_vehicleMass * 9.81f / MathF.Max(1, groundedCount), wheel.SuspensionForce);
            float gripLimit = normalLoad * TireGripCoefficient * MathF.Max(0.2f, config.TractionMultiplier);

            Vector3 totalForce = Vector3.Zero;

            if (config.IsDriveWheel && MathF.Abs(throttleInput) > 0.005f)
            {
                totalForce += wheelForward * (MotorForce * throttleInput / driveWheelCount);
            }

            if (brakePressure > 0.005f && MathF.Abs(forwardSpeed) > 0.05f)
            {
                float brakeForce = MathF.Min(BrakeForce * brakePressure / groundedCount, gripLimit);
                totalForce += -wheelForward * MathF.Sign(forwardSpeed) * brakeForce;
            }

            totalForce += -wheelForward * forwardSpeed * normalLoad * RollingResistanceCoefficient;
            totalForce += -wheelRight * lateralSpeed * normalLoad * LateralGripResponse / MathF.Max(1.0f, MathF.Abs(forwardSpeed) + 4.0f);

            totalForce = ClampMagnitude(totalForce, gripLimit);
            totalForce.Y = 0.0f;
            _physicsWorld.AddForce(vehicleEntity, totalForce);
        }

        ApplySteeringYaw(vehicleEntity, steerInput, velocity, groundedCount);
    }

    private void ApplyAeroAndStability(Entity vehicleEntity, Vector3 forward, Vector3 right, int groundedCount)
    {
        Vector3 velocity = _physicsWorld.GetVelocity(vehicleEntity);
        float speed = velocity.Length();

        if (speed > 0.1f)
        {
            Vector3 dragDir = -velocity / speed;
            _physicsWorld.AddForce(vehicleEntity, dragDir * speed * speed * 0.42f);
        }

        if (groundedCount > 0 && speed > 6.0f)
        {
            _physicsWorld.AddForce(vehicleEntity, Vector3.UnitY * (-speed * speed * 18.0f));
        }

        Vector3 angVel = _physicsWorld.GetAngularVelocity(vehicleEntity);
        if (angVel.LengthSquared() > 0.0001f)
        {
            float damping = groundedCount > 0 ? 0.84f : 0.96f;
            _physicsWorld.SetAngularVelocity(vehicleEntity, angVel * damping);
        }

        velocity = _physicsWorld.GetVelocity(vehicleEntity);
        float maxVerticalSpeed = groundedCount > 0 ? MaxGroundedVerticalSpeed : MaxAirborneVerticalSpeed;
        float verticalSpeed = System.Math.Clamp(velocity.Y, -maxVerticalSpeed, maxVerticalSpeed);
        Vector3 horizontal = new(velocity.X, 0.0f, velocity.Z);
        float horizontalSpeed = horizontal.Length();
        if (horizontalSpeed > MaxForwardSpeed)
        {
            horizontal *= MaxForwardSpeed / horizontalSpeed;
        }

        Vector3 clamped = new(horizontal.X, verticalSpeed, horizontal.Z);
        if ((clamped - velocity).LengthSquared() > 0.0001f)
            _physicsWorld.SetVelocity(vehicleEntity, clamped);
    }

    private void ApplySteeringYaw(Entity vehicleEntity, float steerInput, Vector3 velocity, int groundedCount)
    {
        if (groundedCount <= 0 || MathF.Abs(steerInput) < 0.01f)
            return;

        float planarSpeed = new Vector2(velocity.X, velocity.Z).Length();
        if (planarSpeed < 1.0f)
            return;

        Vector3 angularVelocity = _physicsWorld.GetAngularVelocity(vehicleEntity);
        float desiredYaw = steerInput * MathF.Min(2.2f, planarSpeed * 0.28f);
        angularVelocity.Y += (desiredYaw - angularVelocity.Y) * 0.18f;
        _physicsWorld.SetAngularVelocity(vehicleEntity, angularVelocity);
    }

    private void UpdateWheelState(Entity vehicleEntity, Vector3 forward, Vector3 right,
                                  float throttleInput, float brakePressure, float steerInput, float deltaTime)
    {
        Vector3 velocity = _physicsWorld.GetVelocity(vehicleEntity);

        for (int i = 0; i < _wheels.Length; i++)
        {
            WheelState wheel = _wheels[i];
            WheelConfig config = wheel.Config;

            wheel.SteerAngle = config.IsSteerWheel ? steerInput * config.MaxSteerAngle : 0.0f;

            float steerRadians = config.IsSteerWheel
                ? steerInput * config.MaxSteerAngle * (MathF.PI / 180.0f)
                : 0.0f;
            Vector3 wheelForward = SafeNormalize(forward * MathF.Cos(steerRadians) + right * MathF.Sin(steerRadians), forward);
            Vector3 wheelRight = SafeNormalize(Vector3.Cross(Vector3.UnitY, wheelForward), right);
            float forwardSpeed = Vector3.Dot(velocity, wheelForward);
            float lateralSpeed = Vector3.Dot(velocity, wheelRight);

            if (config.WheelRadius > 0.001f)
            {
                float rollingAngularVelocity = forwardSpeed / config.WheelRadius;
                float spinTarget = rollingAngularVelocity;
                if (config.IsDriveWheel && MathF.Abs(throttleInput) > 0.05f && wheel.IsGrounded)
                    spinTarget += throttleInput * 18.0f;
                if (brakePressure > 0.05f && wheel.IsGrounded)
                    spinTarget *= 1.0f - System.Math.Clamp(brakePressure, 0.0f, 1.0f);

                float response = wheel.IsGrounded ? 10.0f : 2.5f;
                wheel.AngularVelocity += (spinTarget - wheel.AngularVelocity) * System.Math.Clamp(response * deltaTime, 0.0f, 1.0f);
                wheel.SlipRatio = System.Math.Clamp((wheel.AngularVelocity * config.WheelRadius - forwardSpeed) / MathF.Max(2.0f, MathF.Abs(forwardSpeed)), -2.0f, 2.0f);
                wheel.SlipAngle = System.Math.Clamp(MathF.Atan2(lateralSpeed, MathF.Max(1.0f, MathF.Abs(forwardSpeed))), -1.2f, 1.2f);
            }

            wheel.SpinAngle += wheel.AngularVelocity * deltaTime;
        }
    }

    private static Vector3 ToNumerics(BVec3 value, Vector3 fallback)
    {
        Vector3 result = new(value.X, value.Y, value.Z);
        return result.LengthSquared() > 0.000001f ? result : fallback;
    }

    private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
    {
        float lenSq = value.LengthSquared();
        return lenSq > 0.000001f ? value / MathF.Sqrt(lenSq) : fallback;
    }

    private static Vector3 ProjectOnPlane(Vector3 value, Vector3 normal, Vector3 fallback)
    {
        Vector3 projected = value - normal * Vector3.Dot(value, normal);
        return SafeNormalize(projected, fallback);
    }

    private static Vector3 ClampMagnitude(Vector3 value, float maxLength)
    {
        float lenSq = value.LengthSquared();
        if (lenSq <= maxLength * maxLength)
            return value;
        return value / MathF.Sqrt(lenSq) * maxLength;
    }
}
