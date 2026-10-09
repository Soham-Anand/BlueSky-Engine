using System;
using System.Numerics;
using BlueSky.Airborne;
using BlueSky.Core.ECS;
using BVec3 = BlueSky.Core.Math.Vector3;
using BQuat = BlueSky.Core.Math.Quaternion;

namespace BlueSky.Core.Gameplay;

/// <summary>
/// Container for vehicle inputs passed to VehiclePhysics.Step()
/// </summary>
public struct VehicleInput
{
    public float Throttle;  // 0 to 1
    public float Brake;     // 0 to 1
    public float Steer;     // -1 to +1
    public float Handbrake; // 0 or 1
}

/// <summary>
/// High-precision, Jolt-Authoritative Vehicle Physics Solver.
/// 
/// ABSOLUTE INVARIANT:
/// VehiclePhysics NEVER writes chassis position (x), orientation (q),
/// linear velocity (v), or angular velocity (w). It ONLY calculates and
/// applies physical forces and torques to the Jolt dynamic body. Jolt is the
/// single authoritative owner of body motion and integration.
/// </summary>
public class VehiclePhysics
{
    private readonly IPhysicsWorld _physicsWorld;
    private readonly WheelState[] _wheels;
    private readonly float _vehicleMass;

    // ── Tunable Vehicle Parameters ─────────────────────────────────────────
    public float MotorForce { get; set; }           // Max engine drive force (N)
    public float BrakeForce { get; set; }           // Max braking force (N)
    public float MaxSteerAngle { get; set; }        // Max steer angle at low speed (degrees)
    public float SuspensionStiffness { get; set; }  // Spring constant k (N/m)
    public float SuspensionDamping { get; set; }    // Damper constant c (N*s/m)
    public float SuspensionRestLength { get; set; }// Rest length (m)
    public float WheelRadius { get; set; }          // Radius of wheel (m)
    public float AntiRollStiffness { get; set; }    // Anti-roll bar spring rate (N/m)
    public float TireGripCoefficient { get; set; }  // Base friction coefficient mu

    // ── Drivetrain State ──────────────────────────────────────────────────
    public float EngineRPM { get; private set; } = 1000f;
    public int CurrentGear { get; private set; } = 1;
    public float IdleRPM { get; set; } = 900f;
    public float RedlineRPM { get; set; } = 7500f;

    private static readonly float[] GearRatios = { 3.66f, 2.15f, 1.52f, 1.15f, 0.92f, 0.74f };
    private static readonly float FinalDriveRatio = 3.44f;

    public VehiclePhysics(
        IPhysicsWorld physicsWorld,
        WheelState[] wheelStates,
        float mass = 1500f,
        float motorForce = 9000f,
        float brakeForce = 14000f,
        float maxSteerAngle = 32f)
    {
        _physicsWorld = physicsWorld;
        _wheels = wheelStates;
        _vehicleMass = mass;
        MotorForce = motorForce;
        BrakeForce = brakeForce;
        MaxSteerAngle = maxSteerAngle;

        // Default balanced tuning
        SuspensionStiffness = 38000f;
        SuspensionDamping = 4500f;
        SuspensionRestLength = 0.45f;
        WheelRadius = 0.35f;
        AntiRollStiffness = 12000f;
        TireGripCoefficient = 1.15f;
    }

    /// <summary>
    /// Executes ONE simulation step for the vehicle.
    /// MUST be called on a fixed physics timestep (e.g., 60 Hz = 0.01667s).
    /// </summary>
    public void Step(float fixedDt, in VehicleInput input, Entity chassisEntity)
    {
        if (_physicsWorld == null || !_physicsWorld.HasBody(chassisEntity))
            return;

        float dt = MathF.Max(fixedDt, 0.0001f);

        // 1. Read Jolt authoritative chassis state (READ ONLY — NEVER WRITE!)
        Vector3 chassisPos = _physicsWorld.GetPosition(chassisEntity);
        Quaternion chassisRot = _physicsWorld.GetRotation(chassisEntity);
        Vector3 chassisVel = _physicsWorld.GetVelocity(chassisEntity);
        Vector3 chassisAngVel = _physicsWorld.GetAngularVelocity(chassisEntity);

        if (chassisRot.LengthSquared() < 0.0001f)
            chassisRot = Quaternion.Identity;
        else
            chassisRot = Quaternion.Normalize(chassisRot);

        // Vehicle local direction vectors
        Vector3 forward = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, chassisRot));
        Vector3 up      = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, chassisRot));
        Vector3 right   = Vector3.Normalize(Vector3.Cross(forward, up));

        // 2. Perform 4-Point Independent Suspension Raycasts
        int groundedCount = 0;
        float[] compressions = new float[4];

        for (int i = 0; i < 4 && i < _wheels.Length; i++)
        {
            WheelState wheel = _wheels[i];
            WheelConfig config = wheel.Config;

            Vector3 localMount = new(config.LocalPosition.X, config.LocalPosition.Y, config.LocalPosition.Z);
            Vector3 mountPointWorld = chassisPos + Vector3.Transform(localMount, chassisRot);

            // Ray origin is slightly above mount point
            Vector3 rayOrigin = mountPointWorld + up * 0.4f;
            Vector3 rayDirection = -up;
            float maxRayDistance = MathF.Abs(config.LocalPosition.Y) + config.SuspensionRestLength + config.WheelRadius + 1.2f;

            if (_physicsWorld.Raycast(rayOrigin, rayDirection, maxRayDistance, out RaycastHit hit, chassisEntity))
            {
                // Contact point geometry
                Vector3 contactNormal = hit.Normal.LengthSquared() > 0.0001f
                    ? Vector3.Normalize(hit.Normal)
                    : up;

                // Calculate distance from mount point to contact along ray
                float distFromMount = MathF.Max(0.0f, hit.Distance - 0.4f);
                float currentSuspensionLength = MathF.Max(0.0f, distFromMount - config.WheelRadius);
                float compression = MathF.Max(0.0f, config.SuspensionRestLength - currentSuspensionLength);

                wheel.IsGrounded = true;
                wheel.SuspensionLength = currentSuspensionLength;
                wheel.SuspensionCompression = System.Math.Clamp(compression / config.SuspensionRestLength, 0f, 1f);
                wheel.ContactPoint = new BVec3(hit.Point.X, hit.Point.Y, hit.Point.Z);
                wheel.ContactNormal = new BVec3(contactNormal.X, contactNormal.Y, contactNormal.Z);

                // Visual wheel placement
                Vector3 visualWheelPos = hit.Point + contactNormal * config.WheelRadius;
                wheel.WorldPosition = new BVec3(visualWheelPos.X, visualWheelPos.Y, visualWheelPos.Z);

                compressions[i] = compression;
                groundedCount++;
            }
            else
            {
                // ── RAY MISS SAFETY INVARIANT ──
                // Ray miss → zero ground forces, wheel free-spins in air
                wheel.IsGrounded = false;
                wheel.SuspensionLength = config.SuspensionRestLength;
                wheel.SuspensionCompression = 0f;
                wheel.SuspensionForce = 0f;
                wheel.ContactNormal = new BVec3(up.X, up.Y, up.Z);
                Vector3 airWheelPos = mountPointWorld - up * config.SuspensionRestLength;
                wheel.WorldPosition = new BVec3(airWheelPos.X, airWheelPos.Y, airWheelPos.Z);
                compressions[i] = 0f;
            }
        }

        // 3. Compute Drivetrain & Engine RPM
        UpdateDrivetrain(input, chassisVel, forward, groundedCount, dt);

        // 4. Calculate & Apply Wheel Forces to Jolt Body
        float staticWheelLoad = _vehicleMass * 9.81f / MathF.Max(1, groundedCount > 0 ? groundedCount : 4);

        // Anti-Roll Bar calculation (Front: 0&1, Rear: 2&3)
        float frontAntiRollDelta = compressions[0] - compressions[1];
        float rearAntiRollDelta = compressions[2] - compressions[3];

        for (int i = 0; i < 4 && i < _wheels.Length; i++)
        {
            WheelState wheel = _wheels[i];
            WheelConfig config = wheel.Config;

            // Speed-sensitive steering angle
            float speedMPH = chassisVel.Length() * 2.23694f;
            float speedSteerScale = System.Math.Clamp(1.0f - (speedMPH / 120.0f) * 0.5f, 0.4f, 1.0f);
            wheel.SteerAngle = config.IsSteerWheel ? input.Steer * config.MaxSteerAngle * speedSteerScale : 0.0f;

            if (!wheel.IsGrounded)
            {
                // Free-spin angular velocity decay in air
                wheel.AngularVelocity *= 0.98f;
                wheel.SpinAngle += wheel.AngularVelocity * dt;
                continue;
            }

            Vector3 contactNormal = new(wheel.ContactNormal.X, wheel.ContactNormal.Y, wheel.ContactNormal.Z);
            Vector3 contactPoint  = new(wheel.ContactPoint.X, wheel.ContactPoint.Y, wheel.ContactPoint.Z);

            // ── GRAM-SCHMIDT CONTACT TANGENT BASIS ──
            // Construct tangent plane vectors on exact road surface geometry
            float steerRad = wheel.SteerAngle * (MathF.PI / 180f);
            Vector3 steeredForward = Vector3.Normalize(forward * MathF.Cos(steerRad) + right * MathF.Sin(steerRad));

            Vector3 fwdProjected = Vector3.Normalize(steeredForward - contactNormal * Vector3.Dot(steeredForward, contactNormal));
            Vector3 rightProjected = Vector3.Normalize(Vector3.Cross(contactNormal, fwdProjected));

            Vector3 localMount = new(config.LocalPosition.X, config.LocalPosition.Y, config.LocalPosition.Z);
            Vector3 mountPointWorld = chassisPos + Vector3.Transform(localMount, chassisRot);

            // ── SUSPENSION FORCE (Hooke's Law + Damping) ──
            float compression = compressions[i];
            float suspensionVel = (wheel.PreviousSuspensionLength - wheel.SuspensionLength) / dt;
            wheel.PreviousSuspensionLength = wheel.SuspensionLength;

            float springForce = compression * SuspensionStiffness;
            float damperForce = System.Math.Clamp(suspensionVel * SuspensionDamping, -staticWheelLoad * 1.5f, staticWheelLoad * 1.5f);

            // Anti-roll bar adjustment
            float antiRollForce = 0f;
            if (i == 0) antiRollForce = frontAntiRollDelta * AntiRollStiffness;
            else if (i == 1) antiRollForce = -frontAntiRollDelta * AntiRollStiffness;
            else if (i == 2) antiRollForce = rearAntiRollDelta * AntiRollStiffness;
            else if (i == 3) antiRollForce = -rearAntiRollDelta * AntiRollStiffness;

            // Clamp total suspension normal load to prevent 100,000 N rocket launches
            float maxAllowedNormalLoad = staticWheelLoad * 2.5f;
            float totalNormalLoad = System.Math.Clamp(springForce + damperForce + antiRollForce + staticWheelLoad * 0.5f, 0.0f, maxAllowedNormalLoad);
            wheel.SuspensionForce = totalNormalLoad;

            // Apply suspension force at wheel mount point on chassis frame (mountPointWorld), NOT ground contact point.
            // This keeps vertical suspension forces at chassis frame level, eliminating ground lever-arm flip torques!
            _physicsWorld.AddForceAtPosition(chassisEntity, contactNormal * totalNormalLoad, mountPointWorld);

            // ── CONTACT PATCH VELOCITY & SLIP ──
            Vector3 leverArm = contactPoint - chassisPos;
            Vector3 vPatch = chassisVel + Vector3.Cross(chassisAngVel, leverArm);

            float vLong = Vector3.Dot(vPatch, fwdProjected);
            float vLat  = Vector3.Dot(vPatch, rightProjected);

            // Integrate wheel rotational speed (omega) from drivetrain / road speed
            if (config.IsDriveWheel && MathF.Abs(input.Throttle) > 0.01f)
            {
                float targetWheelSpeed = (input.Throttle * MotorForce / MathF.Max(100f, _vehicleMass)) * 0.5f;
                wheel.AngularVelocity = MathF.Max(wheel.AngularVelocity, vLong / config.WheelRadius + targetWheelSpeed * 0.1f);
            }
            else
            {
                wheel.AngularVelocity = vLong / MathF.Max(0.05f, config.WheelRadius);
            }
            wheel.SpinAngle += wheel.AngularVelocity * dt;

            // ── LOAD-SENSITIVE PACEJKA & TRACTION CIRCLE ──
            float slipAngle = MathF.Atan2(vLat, MathF.Abs(vLong) + 0.5f);

            // Pacejka lateral force curve: F_y = D * sin(C * atan(B * alpha))
            float B = 8.5f, C = 1.3f, D = TireGripCoefficient;
            float fyNormalized = D * MathF.Sin(C * MathF.Atan(B * slipAngle));

            float maxTireGrip = totalNormalLoad * TireGripCoefficient;
            float longitudinalForce = 0f;

            if (config.IsDriveWheel && MathF.Abs(input.Throttle) > 0.01f)
            {
                longitudinalForce = input.Throttle * (MotorForce / MathF.Max(1, groundedCount));
            }
            if (input.Brake > 0.01f)
            {
                longitudinalForce -= MathF.Sign(vLong) * input.Brake * (BrakeForce / MathF.Max(1, groundedCount));
            }
            if (input.Handbrake > 0.5f && !config.IsSteerWheel)
            {
                longitudinalForce -= MathF.Sign(vLong) * BrakeForce * 1.5f;
            }

            float lateralForce = -fyNormalized * totalNormalLoad;

            // Enforce Traction Circle: (Fx/Fx_max)^2 + (Fy/Fy_max)^2 <= 1
            float longRatio = longitudinalForce / MathF.Max(1f, maxTireGrip);
            float latRatio  = lateralForce / MathF.Max(1f, maxTireGrip);
            float combinedSq = longRatio * longRatio + latRatio * latRatio;

            if (combinedSq > 1.0f)
            {
                float scale = 1.0f / MathF.Sqrt(combinedSq);
                longitudinalForce *= scale;
                lateralForce *= scale;
            }

            // Apply combined tire forces at chassis mount point
            Vector3 totalTireForce = fwdProjected * longitudinalForce + rightProjected * lateralForce;
            _physicsWorld.AddForceAtPosition(chassisEntity, totalTireForce, mountPointWorld);
        }

        // 5. Aerodynamic Drag & Downforce
        float speed = chassisVel.Length();
        if (speed > 1.0f)
        {
            Vector3 airDrag = -Vector3.Normalize(chassisVel) * (0.35f * speed * speed);
            _physicsWorld.AddForce(chassisEntity, airDrag);

            if (groundedCount > 0)
            {
                Vector3 downforce = -up * (0.45f * speed * speed);
                _physicsWorld.AddForce(chassisEntity, downforce);
            }
        }
    }

    private void UpdateDrivetrain(in VehicleInput input, Vector3 chassisVel, Vector3 forward, int groundedCount, float dt)
    {
        float speedMPS = Vector3.Dot(chassisVel, forward);
        float driveWheelSpeed = MathF.Abs(speedMPS);

        float gearRatio = CurrentGear > 0 && CurrentGear <= GearRatios.Length ? GearRatios[CurrentGear - 1] : 1.0f;
        float wheelRPM = (driveWheelSpeed / (MathF.Tau * WheelRadius)) * 60f;
        float calculatedRPM = wheelRPM * gearRatio * FinalDriveRatio;

        EngineRPM = System.Math.Clamp(MathF.Max(IdleRPM, calculatedRPM), IdleRPM, RedlineRPM);

        // Auto-shift up
        if (EngineRPM >= RedlineRPM * 0.92f && CurrentGear < GearRatios.Length)
        {
            CurrentGear++;
        }
        // Auto-shift down
        else if (EngineRPM <= IdleRPM * 1.3f && CurrentGear > 1 && input.Throttle > 0.1f)
        {
            CurrentGear--;
        }
    }
}
