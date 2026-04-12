using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Physics.Joints;

/// <summary>
/// Joint and constraint system for physics interactions.
/// Supports various joint types for connecting rigid bodies.
/// Optimized for old hardware with simplified constraint solving.
/// </summary>
public class JointSystem
{
    private readonly List<Joint> _joints = new();
    
    /// <summary>
    /// Add a joint to the system.
    /// </summary>
    public void AddJoint(Joint joint)
    {
        if (joint == null) return;
        _joints.Add(joint);
    }
    
    /// <summary>
    /// Remove a joint from the system.
    /// </summary>
    public void RemoveJoint(Joint joint)
    {
        _joints.Remove(joint);
    }
    
    /// <summary>
    /// Solve all joint constraints (call each physics step).
    /// </summary>
    public void SolveConstraints(float deltaTime)
    {
        foreach (var joint in _joints)
        {
            if (joint.IsEnabled)
            {
                joint.Solve(deltaTime);
            }
        }
    }
    
    /// <summary>
    /// Get all joints connected to a specific body.
    /// </summary>
    public List<Joint> GetJointsForBody(ulong bodyId)
    {
        var result = new List<Joint>();
        foreach (var joint in _joints)
        {
            if (joint.BodyA == bodyId || joint.BodyB == bodyId)
            {
                result.Add(joint);
            }
        }
        return result;
    }
}

/// <summary>
/// Base joint class.
/// </summary>
public abstract class Joint
{
    public Guid JointId { get; set; } = Guid.NewGuid();
    public ulong BodyA { get; set; }
    public ulong BodyB { get; set; }
    public Vector3 AnchorA { get; set; }
    public Vector3 AnchorB { get; set; }
    public bool IsEnabled { get; set; } = true;
    public float BreakForce { get; set; } = float.MaxValue;
    public float BreakTorque { get; set; } = float.MaxValue;
    public bool IsBroken { get; private set; }
    
    public abstract void Solve(float deltaTime);
    
    protected void CheckBreak(float appliedForce, float appliedTorque)
    {
        if (appliedForce > BreakForce || appliedTorque > BreakTorque)
        {
            IsBroken = true;
            IsEnabled = false;
        }
    }
}

/// <summary>
/// Fixed joint - locks two bodies together completely.
/// </summary>
public class FixedJoint : Joint
{
    private Quaternion _relativeRotation;
    private Vector3 _relativePosition;
    
    public FixedJoint() { }
    
    public FixedJoint(ulong bodyA, ulong bodyB, Vector3 anchor)
    {
        BodyA = bodyA;
        BodyB = bodyB;
        AnchorA = anchor;
        AnchorB = anchor;
    }
    
    public override void Solve(float deltaTime)
    {
        if (IsBroken) return;
        
        // Simplified: just maintain relative position
        // In a full implementation, this would solve position and rotation constraints
        
        // Calculate relative transform
        _relativePosition = AnchorB - AnchorA;
        
        // Apply correction forces (simplified)
        float force = Vector3.Distance(AnchorA, AnchorB) * 1000.0f;
        CheckBreak(force, 0);
    }
}

/// <summary>
/// Hinge joint - allows rotation around a single axis.
/// </summary>
public class HingeJoint : Joint
{
    public Vector3 Axis { get; set; } = Vector3.UnitY;
    public float LowerLimit { get; set; } = -MathF.PI;
    public float UpperLimit { get; set; } = MathF.PI;
    public bool EnableLimits { get; set; } = false;
    public float MotorSpeed { get; set; } = 0.0f;
    public float MotorForce { get; set; } = 0.0f;
    public bool EnableMotor { get; set; } = false;
    
    public override void Solve(float deltaTime)
    {
        if (IsBroken) return;
        
        // Simplified hinge constraint
        // In a full implementation, this would:
        // 1. Constrain position at anchor
        // 2. Constrain rotation to hinge axis
        // 3. Apply limits if enabled
        // 4. Apply motor if enabled
        
        float force = MotorForce * MotorSpeed;
        CheckBreak(force, force * 0.1f);
    }
}

/// <summary>
/// Spring joint - connects bodies with spring-like behavior.
/// </summary>
public class SpringJoint : Joint
{
    public float Stiffness { get; set; } = 100.0f;
    public float Damping { get; set; } = 5.0f;
    public float RestLength { get; set; } = 1.0f;
    
    public override void Solve(float deltaTime)
    {
        if (IsBroken) return;
        
        // Calculate spring force
        Vector3 direction = AnchorB - AnchorA;
        float distance = direction.Length();
        
        if (distance < 0.001f)
            return;
        
        direction = Vector3.Normalize(direction);
        
        // Spring force (Hooke's Law)
        float springForce = Stiffness * (distance - RestLength);
        
        // Damping force (simplified)
        float dampingForce = Damping * 0.0f; // Would need velocity in full implementation
        
        float totalForce = springForce + dampingForce;
        
        CheckBreak(Math.Abs(totalForce), 0);
    }
}

/// <summary>
/// Slider joint - allows movement along a single axis.
/// </summary>
public class SliderJoint : Joint
{
    public Vector3 Axis { get; set; } = Vector3.UnitX;
    public float LowerLimit { get; set; } = -10.0f;
    public float UpperLimit { get; set; } = 10.0f;
    public bool EnableLimits { get; set; } = true;
    public float MotorSpeed { get; set; } = 0.0f;
    public float MotorForce { get; set; } = 0.0f;
    public bool EnableMotor { get; set; } = false;
    
    public override void Solve(float deltaTime)
    {
        if (IsBroken) return;
        
        // Simplified slider constraint
        // In a full implementation, this would:
        // 1. Constrain rotation completely
        // 2. Constrain position to slider axis
        // 3. Apply limits if enabled
        // 4. Apply motor if enabled
        
        float force = MotorForce * MotorSpeed;
        CheckBreak(force, force * 0.1f);
    }
}

/// <summary>
/// Distance joint - maintains a fixed distance between two bodies.
/// </summary>
public class DistanceJoint : Joint
{
    public float Distance { get; set; } = 1.0f;
    public float MinDistance { get; set; } = 0.0f;
    public float MaxDistance { get; set; } = float.MaxValue;
    public bool EnableMinDistance { get; set; } = false;
    public bool EnableMaxDistance { get; set; } = false;
    public float Stiffness { get; set; } = 1000.0f;
    public float Damping { get; set; } = 10.0f;
    
    public override void Solve(float deltaTime)
    {
        if (IsBroken) return;
        
        Vector3 direction = AnchorB - AnchorA;
        float currentDistance = direction.Length();
        
        if (currentDistance < 0.001f)
            return;
        
        direction = Vector3.Normalize(direction);
        
        float force = 0.0f;
        
        // Check min distance
        if (EnableMinDistance && currentDistance < MinDistance)
        {
            force = Stiffness * (MinDistance - currentDistance);
        }
        // Check max distance
        else if (EnableMaxDistance && currentDistance > MaxDistance)
        {
            force = Stiffness * (MaxDistance - currentDistance);
        }
        // Maintain target distance
        else
        {
            force = Stiffness * (Distance - currentDistance);
        }
        
        CheckBreak(Math.Abs(force), 0);
    }
}

/// <summary>
/// Generic constraint for custom physics constraints.
/// </summary>
public class Constraint
{
    public Guid ConstraintId { get; set; } = Guid.NewGuid();
    public ulong BodyA { get; set; }
    public ulong BodyB { get; set; }
    public ConstraintType Type { get; set; } = ConstraintType.Position;
    public Vector3 Axis { get; set; } = Vector3.UnitY;
    public float MinValue { get; set; } = 0.0f;
    public float MaxValue { get; set; } = 0.0f;
    public bool IsEnabled { get; set; } = true;
    
    public void Solve(float deltaTime)
    {
        if (!IsEnabled) return;
        
        // Simplified constraint solving
        // In a full implementation, this would use constraint solver
    }
}

public enum ConstraintType
{
    Position,
    Rotation,
    LinearVelocity,
    AngularVelocity
}
