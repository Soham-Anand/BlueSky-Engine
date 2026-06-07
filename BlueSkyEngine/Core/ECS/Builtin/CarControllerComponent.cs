using System.Numerics;

namespace BlueSky.Core.ECS.Builtin;

public struct CarControllerComponent
{
    // Physics settings
    public float MotorForce;
    public float BrakeForce;
    public float MaxSteerAngle;
    public float DownForce;
    public Vector3 CenterOfMassOffset;

    // Camera settings
    public Vector3 CameraOffset;
    public Vector3 CameraTargetOffset;

    // Wheel configuration (per-wheel, up to 4 wheels)
    public Vector3 WheelPositionFL;
    public Vector3 WheelPositionFR;
    public Vector3 WheelPositionRL;
    public Vector3 WheelPositionRR;
    public float SuspensionRestLength;
    public float SuspensionStiffness;
    public float SuspensionDamping;
    public float WheelRadius;

    // State
    public bool IsInitialized;
    public bool IsPossessed;

    // Entity reference for runtime controller lookup
    public uint EntityId;

    public static CarControllerComponent CreateDefault()
    {
        return new CarControllerComponent
        {
            MotorForce = 12000f,
            BrakeForce = 22000f,
            MaxSteerAngle = 30f,
            DownForce = 100f,
            CenterOfMassOffset = new Vector3(0, -0.5f, 0),
            CameraOffset = new Vector3(0, 2.5f, -6f),
            CameraTargetOffset = new Vector3(0, 0.5f, 0),
            // Default Corvette wheel positions
            WheelPositionFL = new Vector3(-0.8f, -0.3f, 1.5f),
            WheelPositionFR = new Vector3(0.8f, -0.3f, 1.5f),
            WheelPositionRL = new Vector3(-0.8f, -0.3f, -1.5f),
            WheelPositionRR = new Vector3(0.8f, -0.3f, -1.5f),
            SuspensionRestLength = 0.24f,
            SuspensionStiffness = 22000.0f,
            SuspensionDamping = 2800.0f,
            WheelRadius = 0.30f,
            IsInitialized = false,
            IsPossessed = false,
            EntityId = 0
        };
    }
}
