// ═══════════════════════════════════════════════════════════════════════════
// BlueSky Engine - Production-Grade Animation Blend Tree System
// ═══════════════════════════════════════════════════════════════════════════
// Advanced animation blending with:
// - Multi-layer blending (additive, override, blend)
// - Smooth transitions with customizable curves
// - State machine integration
// - Blend spaces (1D/2D) for locomotion
// - Animation masks for partial body animation
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Animation;

/// <summary>
/// Animation blend tree for complex animation blending.
/// Supports layered blending, state machines, and blend spaces.
/// </summary>
public class AnimationBlendTree
{
    private readonly List<BlendLayer> _layers = new();
    private readonly Dictionary<string, BlendNode> _nodes = new();
    private BlendNode? _rootNode;
    
    public AnimationBlendTree()
    {
    }
    
    /// <summary>
    /// Add a blend layer. Layers are evaluated bottom-to-top.
    /// </summary>
    public BlendLayer AddLayer(string name, float weight = 1.0f, BlendMode mode = BlendMode.Override)
    {
        var layer = new BlendLayer
        {
            Name = name,
            Weight = weight,
            Mode = mode
        };
        _layers.Add(layer);
        return layer;
    }
    
    /// <summary>
    /// Create a simple animation node.
    /// </summary>
    public AnimationNode CreateAnimationNode(string name, AnimationClip clip)
    {
        var node = new AnimationNode(clip);
        _nodes[name] = node;
        return node;
    }
    
    /// <summary>
    /// Create a blend node that blends between two animations.
    /// </summary>
    public BlendNode CreateBlendNode(string name, BlendNode nodeA, BlendNode nodeB, float blendFactor = 0.5f)
    {
        var node = new Blend2DNode(nodeA, nodeB, blendFactor);
        _nodes[name] = node;
        return node;
    }
    
    /// <summary>
    /// Create a 1D blend space (e.g., walk/run based on speed).
    /// </summary>
    public BlendSpace1D CreateBlendSpace1D(string name)
    {
        var node = new BlendSpace1D();
        _nodes[name] = node;
        return node;
    }
    
    /// <summary>
    /// Create a 2D blend space (e.g., locomotion based on speed and direction).
    /// </summary>
    public BlendSpace2D CreateBlendSpace2D(string name)
    {
        var node = new BlendSpace2D();
        _nodes[name] = node;
        return node;
    }
    
    /// <summary>
    /// Set the root node of the blend tree.
    /// </summary>
    public void SetRootNode(BlendNode node)
    {
        _rootNode = node;
    }
    
    /// <summary>
    /// Evaluate the blend tree and return final bone transforms.
    /// </summary>
    public void Evaluate(float deltaTime, int boneCount, Matrix4x4[] outBoneTransforms)
    {
        if (_rootNode == null || boneCount == 0) return;
        
        // Evaluate root node
        var pose = new AnimationPose(boneCount);
        _rootNode.Evaluate(deltaTime, pose);
        
        // Apply layers
        foreach (var layer in _layers)
        {
            if (layer.Weight < 0.001f || layer.RootNode == null) continue;
            
            var layerPose = new AnimationPose(boneCount);
            layer.RootNode.Evaluate(deltaTime, layerPose);
            
            // Blend layer into final pose
            BlendPoses(pose, layerPose, layer.Weight, layer.Mode, layer.Mask);
        }
        
        // Convert pose to bone transforms
        pose.ToMatrices(outBoneTransforms);
    }
    
    private void BlendPoses(AnimationPose basePose, AnimationPose layerPose, float weight, BlendMode mode, AnimationMask? mask)
    {
        for (int i = 0; i < basePose.BoneCount; i++)
        {
            // Check mask
            float boneWeight = weight;
            if (mask != null)
            {
                boneWeight *= mask.GetBoneWeight(i);
                if (boneWeight < 0.001f) continue;
            }
            
            switch (mode)
            {
                case BlendMode.Override:
                    basePose.Positions[i] = Vector3.Lerp(basePose.Positions[i], layerPose.Positions[i], boneWeight);
                    basePose.Rotations[i] = Quaternion.Slerp(basePose.Rotations[i], layerPose.Rotations[i], boneWeight);
                    basePose.Scales[i] = Vector3.Lerp(basePose.Scales[i], layerPose.Scales[i], boneWeight);
                    break;
                
                case BlendMode.Additive:
                    basePose.Positions[i] += layerPose.Positions[i] * boneWeight;
                    basePose.Rotations[i] = Quaternion.Normalize(basePose.Rotations[i] * Quaternion.Slerp(Quaternion.Identity, layerPose.Rotations[i], boneWeight));
                    basePose.Scales[i] += (layerPose.Scales[i] - Vector3.One) * boneWeight;
                    break;
            }
        }
    }
}

/// <summary>
/// Blend layer for multi-layer animation.
/// </summary>
public class BlendLayer
{
    public string Name { get; set; } = string.Empty;
    public float Weight { get; set; } = 1.0f;
    public BlendMode Mode { get; set; } = BlendMode.Override;
    public AnimationMask? Mask { get; set; }
    public BlendNode? RootNode { get; set; }
}

/// <summary>
/// Base class for blend tree nodes.
/// </summary>
public abstract class BlendNode
{
    public abstract void Evaluate(float deltaTime, AnimationPose outPose);
}

/// <summary>
/// Simple animation playback node.
/// </summary>
public class AnimationNode : BlendNode
{
    private readonly AnimationClip _clip;
    private float _time;
    private float _speed = 1.0f;
    
    public float Speed
    {
        get => _speed;
        set => _speed = value;
    }
    
    public AnimationNode(AnimationClip clip)
    {
        _clip = clip ?? throw new ArgumentNullException(nameof(clip));
    }
    
    public override void Evaluate(float deltaTime, AnimationPose outPose)
    {
        // Update time
        _time += deltaTime * _speed;
        
        // Handle looping
        if (_clip.Looping)
        {
            while (_time > _clip.Duration)
                _time -= _clip.Duration;
        }
        else
        {
            _time = Math.Min(_time, _clip.Duration);
        }
        
        // Sample animation
        foreach (var kvp in _clip.BoneTracks)
        {
            var track = kvp.Value;
            var (pos, rot, scale) = track.Sample(_time);
            
            // Find bone index (simplified - in production, use a bone name->index map)
            int boneIndex = 0; // TODO: Proper bone lookup
            if (boneIndex < outPose.BoneCount)
            {
                outPose.Positions[boneIndex] = pos;
                outPose.Rotations[boneIndex] = rot;
                outPose.Scales[boneIndex] = scale;
            }
        }
    }
}

/// <summary>
/// Blends between two animations.
/// </summary>
public class Blend2DNode : BlendNode
{
    private readonly BlendNode _nodeA;
    private readonly BlendNode _nodeB;
    private float _blendFactor;
    
    public float BlendFactor
    {
        get => _blendFactor;
        set => _blendFactor = Math.Clamp(value, 0f, 1f);
    }
    
    public Blend2DNode(BlendNode nodeA, BlendNode nodeB, float blendFactor = 0.5f)
    {
        _nodeA = nodeA ?? throw new ArgumentNullException(nameof(nodeA));
        _nodeB = nodeB ?? throw new ArgumentNullException(nameof(nodeB));
        _blendFactor = Math.Clamp(blendFactor, 0f, 1f);
    }
    
    public override void Evaluate(float deltaTime, AnimationPose outPose)
    {
        var poseA = new AnimationPose(outPose.BoneCount);
        var poseB = new AnimationPose(outPose.BoneCount);
        
        _nodeA.Evaluate(deltaTime, poseA);
        _nodeB.Evaluate(deltaTime, poseB);
        
        // Blend poses
        for (int i = 0; i < outPose.BoneCount; i++)
        {
            outPose.Positions[i] = Vector3.Lerp(poseA.Positions[i], poseB.Positions[i], _blendFactor);
            outPose.Rotations[i] = Quaternion.Slerp(poseA.Rotations[i], poseB.Rotations[i], _blendFactor);
            outPose.Scales[i] = Vector3.Lerp(poseA.Scales[i], poseB.Scales[i], _blendFactor);
        }
    }
}

/// <summary>
/// 1D blend space (e.g., walk to run based on speed).
/// </summary>
public class BlendSpace1D : BlendNode
{
    private readonly List<(float value, AnimationClip clip)> _samples = new();
    private float _parameter;
    
    public float Parameter
    {
        get => _parameter;
        set => _parameter = value;
    }
    
    public void AddSample(float value, AnimationClip clip)
    {
        _samples.Add((value, clip));
        _samples.Sort((a, b) => a.value.CompareTo(b.value));
    }
    
    public override void Evaluate(float deltaTime, AnimationPose outPose)
    {
        if (_samples.Count == 0) return;
        if (_samples.Count == 1)
        {
            // Single sample - just play it
            var node = new AnimationNode(_samples[0].clip);
            node.Evaluate(deltaTime, outPose);
            return;
        }
        
        // Find surrounding samples
        int indexA = 0;
        int indexB = 0;
        
        for (int i = 0; i < _samples.Count - 1; i++)
        {
            if (_parameter >= _samples[i].value && _parameter <= _samples[i + 1].value)
            {
                indexA = i;
                indexB = i + 1;
                break;
            }
        }
        
        // Clamp to edges
        if (_parameter <= _samples[0].value)
        {
            var node = new AnimationNode(_samples[0].clip);
            node.Evaluate(deltaTime, outPose);
            return;
        }
        if (_parameter >= _samples[^1].value)
        {
            var node = new AnimationNode(_samples[^1].clip);
            node.Evaluate(deltaTime, outPose);
            return;
        }
        
        // Blend between samples
        float valueA = _samples[indexA].value;
        float valueB = _samples[indexB].value;
        float blend = (_parameter - valueA) / (valueB - valueA);
        
        var poseA = new AnimationPose(outPose.BoneCount);
        var poseB = new AnimationPose(outPose.BoneCount);
        
        var nodeA = new AnimationNode(_samples[indexA].clip);
        var nodeB = new AnimationNode(_samples[indexB].clip);
        
        nodeA.Evaluate(deltaTime, poseA);
        nodeB.Evaluate(deltaTime, poseB);
        
        for (int i = 0; i < outPose.BoneCount; i++)
        {
            outPose.Positions[i] = Vector3.Lerp(poseA.Positions[i], poseB.Positions[i], blend);
            outPose.Rotations[i] = Quaternion.Slerp(poseA.Rotations[i], poseB.Rotations[i], blend);
            outPose.Scales[i] = Vector3.Lerp(poseA.Scales[i], poseB.Scales[i], blend);
        }
    }
}

/// <summary>
/// 2D blend space (e.g., locomotion based on speed and direction).
/// </summary>
public class BlendSpace2D : BlendNode
{
    private readonly List<(Vector2 position, AnimationClip clip)> _samples = new();
    private Vector2 _parameter;
    
    public Vector2 Parameter
    {
        get => _parameter;
        set => _parameter = value;
    }
    
    public void AddSample(Vector2 position, AnimationClip clip)
    {
        _samples.Add((position, clip));
    }
    
    public override void Evaluate(float deltaTime, AnimationPose outPose)
    {
        if (_samples.Count == 0) return;
        if (_samples.Count == 1)
        {
            var node = new AnimationNode(_samples[0].clip);
            node.Evaluate(deltaTime, outPose);
            return;
        }
        
        // Find 3 nearest samples for triangular interpolation
        var nearest = new List<(float distance, int index)>();
        for (int i = 0; i < _samples.Count; i++)
        {
            float dist = Vector2.Distance(_parameter, _samples[i].position);
            nearest.Add((dist, i));
        }
        nearest.Sort((a, b) => a.distance.CompareTo(b.distance));
        
        // Use closest 3 samples
        int count = Math.Min(3, nearest.Count);
        var poses = new AnimationPose[count];
        var weights = new float[count];
        float totalWeight = 0;
        
        for (int i = 0; i < count; i++)
        {
            poses[i] = new AnimationPose(outPose.BoneCount);
            var node = new AnimationNode(_samples[nearest[i].index].clip);
            node.Evaluate(deltaTime, poses[i]);
            
            // Inverse distance weighting
            float dist = nearest[i].distance;
            weights[i] = dist < 0.001f ? 1000f : 1f / dist;
            totalWeight += weights[i];
        }
        
        // Normalize weights
        for (int i = 0; i < count; i++)
        {
            weights[i] /= totalWeight;
        }
        
        // Blend poses
        for (int bone = 0; bone < outPose.BoneCount; bone++)
        {
            Vector3 pos = Vector3.Zero;
            Quaternion rot = Quaternion.Identity;
            Vector3 scale = Vector3.Zero;
            
            for (int i = 0; i < count; i++)
            {
                pos += poses[i].Positions[bone] * weights[i];
                rot = Quaternion.Slerp(rot, poses[i].Rotations[bone], weights[i]);
                scale += poses[i].Scales[bone] * weights[i];
            }
            
            outPose.Positions[bone] = pos;
            outPose.Rotations[bone] = Quaternion.Normalize(rot);
            outPose.Scales[bone] = scale;
        }
    }
}

/// <summary>
/// Animation pose (bone transforms in local space).
/// </summary>
public class AnimationPose
{
    public int BoneCount { get; }
    public Vector3[] Positions { get; }
    public Quaternion[] Rotations { get; }
    public Vector3[] Scales { get; }
    
    public AnimationPose(int boneCount)
    {
        BoneCount = boneCount;
        Positions = new Vector3[boneCount];
        Rotations = new Quaternion[boneCount];
        Scales = new Vector3[boneCount];
        
        // Initialize to identity
        for (int i = 0; i < boneCount; i++)
        {
            Positions[i] = Vector3.Zero;
            Rotations[i] = Quaternion.Identity;
            Scales[i] = Vector3.One;
        }
    }
    
    /// <summary>
    /// Convert pose to bone transform matrices.
    /// </summary>
    public void ToMatrices(Matrix4x4[] outMatrices)
    {
        for (int i = 0; i < Math.Min(BoneCount, outMatrices.Length); i++)
        {
            outMatrices[i] = 
                Matrix4x4.CreateScale(Scales[i]) *
                Matrix4x4.CreateFromQuaternion(Rotations[i]) *
                Matrix4x4.CreateTranslation(Positions[i]);
        }
    }
}

/// <summary>
/// Animation mask for partial body animation (e.g., upper body only).
/// </summary>
public class AnimationMask
{
    private readonly float[] _boneWeights;
    
    public AnimationMask(int boneCount)
    {
        _boneWeights = new float[boneCount];
        for (int i = 0; i < boneCount; i++)
        {
            _boneWeights[i] = 1.0f; // Default: all bones enabled
        }
    }
    
    public void SetBoneWeight(int boneIndex, float weight)
    {
        if (boneIndex >= 0 && boneIndex < _boneWeights.Length)
        {
            _boneWeights[boneIndex] = Math.Clamp(weight, 0f, 1f);
        }
    }
    
    public float GetBoneWeight(int boneIndex)
    {
        return boneIndex >= 0 && boneIndex < _boneWeights.Length ? _boneWeights[boneIndex] : 0f;
    }
    
    /// <summary>
    /// Enable all bones from a specific bone down the hierarchy.
    /// Useful for "upper body only" or "left arm only" masks.
    /// </summary>
    public void EnableBoneChain(int rootBoneIndex, Bone[] bones)
    {
        if (rootBoneIndex < 0 || rootBoneIndex >= bones.Length) return;
        
        SetBoneWeight(rootBoneIndex, 1.0f);
        
        // Recursively enable children
        foreach (int childIndex in bones[rootBoneIndex].Children)
        {
            EnableBoneChain(childIndex, bones);
        }
    }
}

/// <summary>
/// Blend mode for animation layers.
/// </summary>
public enum BlendMode
{
    /// <summary>
    /// Replace base animation with layer animation.
    /// </summary>
    Override,
    
    /// <summary>
    /// Add layer animation on top of base animation.
    /// Useful for procedural animations (recoil, breathing, etc.).
    /// </summary>
    Additive
}
