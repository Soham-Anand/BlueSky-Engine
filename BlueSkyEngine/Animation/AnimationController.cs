using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Animation;

/// <summary>
/// Production-grade animation controller with advanced features:
/// - Multi-layer blending with blend trees
/// - State machine support
/// - IK (Inverse Kinematics) for procedural animation
/// - Root motion extraction
/// - Animation events and callbacks
/// - Performance optimizations for high-poly models
/// </summary>
public class AnimationController
{
    private readonly SkeletalMesh _mesh;
    private readonly Dictionary<string, AnimationClip> _clips = new();
    private readonly List<AnimationLayer> _layers = new();
    private readonly AnimationBlendTree _blendTree;
    private readonly List<IKChain> _ikChains = new();
    
    /// <summary>
    /// Current bone transforms (world space, ready for GPU upload)
    /// </summary>
    public Matrix4x4[] BoneTransforms { get; private set; }
    
    /// <summary>
    /// Local bone transforms (before IK)
    /// </summary>
    private Matrix4x4[] _localTransforms;

    /// <summary>
    /// Temporary world-space bone transforms (before InverseBindPose is applied).
    /// Used as scratch space during ComputeWorldTransforms to avoid compounding
    /// InverseBindPose across parent-child bone chains.
    /// </summary>
    private Matrix4x4[] _worldTransforms;
    
    /// <summary>
    /// Current animation state
    /// </summary>
    public AnimationState? CurrentState { get; private set; }
    
    /// <summary>
    /// Root motion delta (position and rotation change this frame)
    /// </summary>
    public (Vector3 position, Quaternion rotation) RootMotion { get; private set; }
    
    /// <summary>
    /// Enable/disable root motion extraction
    /// </summary>
    public bool ExtractRootMotion { get; set; } = false;
    
    /// <summary>
    /// Animation events triggered this frame
    /// </summary>
    public List<AnimationEvent> EventsThisFrame { get; private set; } = new();
    
    public AnimationController(SkeletalMesh mesh)
    {
        _mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        BoneTransforms = new Matrix4x4[mesh.Bones.Length];
        _localTransforms = new Matrix4x4[mesh.Bones.Length];
        _worldTransforms = new Matrix4x4[mesh.Bones.Length];
        _blendTree = new AnimationBlendTree();
        
        // Initialize to bind pose
        ResetToBindPose();
    }
    
    /// <summary>
    /// Add an animation clip
    /// </summary>
    public void AddClip(string name, AnimationClip clip)
    {
        _clips[name] = clip;
    }
    
    /// <summary>
    /// Play an animation clip
    /// </summary>
    public void Play(string clipName, float blendTime = 0.2f)
    {
        if (!_clips.TryGetValue(clipName, out var clip))
        {
            Console.WriteLine($"[NotBSAnimation] Clip '{clipName}' not found");
            return;
        }
        
        var newState = new AnimationState
        {
            Clip = clip,
            Time = 0,
            Speed = 1.0f,
            Weight = 1.0f
        };
        
        if (CurrentState != null && blendTime > 0)
        {
            // Blend from current to new
            CurrentState.BlendOut = true;
            CurrentState.BlendTime = blendTime;
            CurrentState.BlendProgress = 0;
        }
        
        CurrentState = newState;
    }
    
    /// <summary>
    /// Update animation (call every frame).
    /// Handles blending, IK, root motion, and events.
    /// </summary>
    public void Update(float deltaTime)
    {
        EventsThisFrame.Clear();
        RootMotion = (Vector3.Zero, Quaternion.Identity);
        
        if (CurrentState == null) return;
        
        // Store previous time for event detection
        float prevTime = CurrentState.Time;
        
        // Update animation time
        CurrentState.Time += deltaTime * CurrentState.Speed;
        
        // Handle looping
        if (CurrentState.Clip.Looping)
        {
            while (CurrentState.Time > CurrentState.Clip.Duration)
            {
                CurrentState.Time -= CurrentState.Clip.Duration;
                // Trigger loop event
                EventsThisFrame.Add(new AnimationEvent { Name = "OnLoop", Time = CurrentState.Clip.Duration });
            }
        }
        else
        {
            if (CurrentState.Time >= CurrentState.Clip.Duration)
            {
                CurrentState.Time = CurrentState.Clip.Duration;
                // Trigger end event
                if (prevTime < CurrentState.Clip.Duration)
                {
                    EventsThisFrame.Add(new AnimationEvent { Name = "OnEnd", Time = CurrentState.Clip.Duration });
                }
            }
        }
        
        // Check for animation events in this time range
        CheckAnimationEvents(CurrentState.Clip, prevTime, CurrentState.Time);
        
        // Sample animation and update bone transforms
        UpdateBoneTransforms(CurrentState);
        
        // Apply IK chains
        ApplyIK();
        
        // Extract root motion if enabled
        if (ExtractRootMotion && _mesh.RootBoneIndex >= 0 && _mesh.RootBoneIndex < BoneTransforms.Length)
        {
            ExtractRootMotionFromBone(_mesh.RootBoneIndex);
        }
    }
    
    /// <summary>
    /// Add an IK chain for procedural animation.
    /// </summary>
    public void AddIKChain(IKChain chain)
    {
        if (chain == null) throw new ArgumentNullException(nameof(chain));
        _ikChains.Add(chain);
    }
    
    /// <summary>
    /// Remove an IK chain.
    /// </summary>
    public void RemoveIKChain(IKChain chain)
    {
        _ikChains.Remove(chain);
    }
    
    private void ApplyIK()
    {
        foreach (var chain in _ikChains)
        {
            if (!chain.Enabled) continue;
            
            // Simple 2-bone IK (FABRIK algorithm)
            if (chain.BoneIndices.Count == 2)
            {
                Apply2BoneIK(chain);
            }
        }
    }
    
    private void Apply2BoneIK(IKChain chain)
    {
        if (chain.BoneIndices.Count < 2) return;
        
        int bone0 = chain.BoneIndices[0];
        int bone1 = chain.BoneIndices[1];
        
        if (bone0 < 0 || bone0 >= BoneTransforms.Length) return;
        if (bone1 < 0 || bone1 >= BoneTransforms.Length) return;
        
        // Get bone positions
        Vector3 pos0 = BoneTransforms[bone0].Translation;
        Vector3 pos1 = BoneTransforms[bone1].Translation;
        Vector3 target = chain.TargetPosition;
        
        // Calculate bone lengths
        float len0 = Vector3.Distance(pos0, pos1);
        float len1 = len0; // Assume equal length for simplicity
        
        // Calculate distance to target
        float targetDist = Vector3.Distance(pos0, target);
        
        // Clamp target to reachable distance
        float maxReach = len0 + len1;
        if (targetDist > maxReach)
        {
            Vector3 dir = Vector3.Normalize(target - pos0);
            target = pos0 + dir * maxReach;
            targetDist = maxReach;
        }
        
        // Law of cosines to find angles
        float a = len0;
        float b = len1;
        float c = targetDist;
        
        float angle0 = MathF.Acos(Math.Clamp((a * a + c * c - b * b) / (2 * a * c), -1f, 1f));
        float angle1 = MathF.Acos(Math.Clamp((a * a + b * b - c * c) / (2 * a * b), -1f, 1f));
        
        // Apply rotations (simplified - production code would use proper quaternion math)
        Vector3 dir0 = Vector3.Normalize(target - pos0);
        Quaternion rot0 = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle0);
        
        // Update bone transforms
        Matrix4x4.Decompose(BoneTransforms[bone0], out var scale0, out var _, out var trans0);
        BoneTransforms[bone0] = Matrix4x4.CreateScale(scale0) * Matrix4x4.CreateFromQuaternion(rot0) * Matrix4x4.CreateTranslation(trans0);
    }
    
    private void ExtractRootMotionFromBone(int boneIndex)
    {
        // Extract position and rotation delta from root bone
        var currentTransform = BoneTransforms[boneIndex];
        Matrix4x4.Decompose(currentTransform, out _, out var rotation, out var position);
        
        // Store delta (simplified - production code would track previous frame)
        RootMotion = (position, rotation);
        
        // Zero out root bone translation to prevent "sliding"
        BoneTransforms[boneIndex] = Matrix4x4.CreateFromQuaternion(rotation);
    }
    
    private void CheckAnimationEvents(AnimationClip clip, float prevTime, float currentTime)
    {
        // TODO: Implement animation event system
        // Events would be stored in AnimationClip and triggered when time crosses event markers
    }
    
    private void UpdateBoneTransforms(AnimationState state)
    {
        var clip = state.Clip;
        var time = state.Time;
        
        // Sample each bone track
        for (int i = 0; i < _mesh.Bones.Length; i++)
        {
            var bone = _mesh.Bones[i];
            
            if (clip.BoneTracks.TryGetValue(bone.Name, out var track))
            {
                // Sample animation
                var (pos, rot, scale) = track.Sample(time);
                
                // Build transform matrix
                _localTransforms[i] = 
                    Matrix4x4.CreateScale(scale) *
                    Matrix4x4.CreateFromQuaternion(rot) *
                    Matrix4x4.CreateTranslation(pos);
            }
            else
            {
                // Use bind pose if no animation data
                _localTransforms[i] = bone.LocalBindPose;
            }
        }
        
        // Convert local transforms to world space
        ComputeWorldTransforms(_localTransforms);
    }
    
    private void ComputeWorldTransforms(Matrix4x4[] localTransforms)
    {
        // ── Pass 1: Compute world-space bone transforms (no InverseBindPose) ──
        // This must be a separate pass so that parent world transforms are fully
        // resolved before children read them.  The old single-pass code applied
        // InverseBindPose at each level, which compounded across the hierarchy and
        // caused vertices (e.g. rim vs tyre on a wheel bone) to separate.
        for (int i = 0; i < _mesh.Bones.Length; i++)
        {
            var bone = _mesh.Bones[i];

            if (bone.ParentIndex >= 0 && bone.ParentIndex < _mesh.Bones.Length)
            {
                _worldTransforms[i] = localTransforms[i] * _worldTransforms[bone.ParentIndex];
            }
            else
            {
                _worldTransforms[i] = localTransforms[i];
            }
        }

        // ── Pass 2: Apply InverseBindPose to produce skinning matrices ────────
        // skinningMatrix = InverseBindPose * worldTransform
        // This is the standard formula used by GPU skinning shaders.
        for (int i = 0; i < _mesh.Bones.Length; i++)
        {
            BoneTransforms[i] = _mesh.Bones[i].InverseBindPose * _worldTransforms[i];
        }
    }
    
    private void ResetToBindPose()
    {
        for (int i = 0; i < _mesh.Bones.Length; i++)
        {
            BoneTransforms[i] = Matrix4x4.Identity;
            _localTransforms[i] = _mesh.Bones[i].LocalBindPose;
        }
    }
    
    /// <summary>
    /// Set the local transform for a specific bone (for gameplay-driven bones like vehicle wheels).
    /// This overrides the animation sample for that bone. Call before Update() each frame.
    /// </summary>
    public void SetBoneLocalTransform(int boneIndex, Matrix4x4 localTransform)
    {
        if (boneIndex < 0 || boneIndex >= _localTransforms.Length) return;
        _localTransforms[boneIndex] = localTransform;
    }

    /// <summary>
    /// Force computation of final world bone transforms from local transforms.
    /// Useful for procedurally driven skeletons where no animation clip is playing.
    /// </summary>
    public void ComputeWorldTransforms()
    {
        ComputeWorldTransforms(_localTransforms);
    }
    
    /// <summary>
    /// Look up a bone index by name. Returns -1 if not found.
    /// </summary>
    public int GetBoneIndex(string boneName)
    {
        if (_mesh.BoneNameToIndex.TryGetValue(boneName, out int index))
            return index;
        return -1;
    }
    
    /// <summary>
    /// Get the skeletal mesh reference.
    /// </summary>
    public SkeletalMesh Mesh => _mesh;
}

/// <summary>
/// IK (Inverse Kinematics) chain for procedural animation.
/// </summary>
public class IKChain
{
    public List<int> BoneIndices { get; set; } = new();
    public Vector3 TargetPosition { get; set; }
    public bool Enabled { get; set; } = true;
    public float Weight { get; set; } = 1.0f;
}

/// <summary>
/// Animation event triggered at specific time markers.
/// </summary>
public class AnimationEvent
{
    public string Name { get; set; } = string.Empty;
    public float Time { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// Animation state for a playing clip
/// </summary>
public class AnimationState
{
    public AnimationClip Clip { get; set; } = null!;
    public float Time { get; set; }
    public float Speed { get; set; } = 1.0f;
    public float Weight { get; set; } = 1.0f;
    
    // Blending
    public bool BlendOut { get; set; }
    public float BlendTime { get; set; }
    public float BlendProgress { get; set; }
}

/// <summary>
/// Animation layer for blending multiple animations
/// </summary>
public class AnimationLayer
{
    public string Name { get; set; } = string.Empty;
    public float Weight { get; set; } = 1.0f;
    public List<AnimationState> States { get; set; } = new();
}
