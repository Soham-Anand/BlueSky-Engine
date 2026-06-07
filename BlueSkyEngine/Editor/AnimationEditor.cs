using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BlueSky.Animation;
using BlueSky.Editor.UI;
using NotBSRenderer;

namespace BlueSky.Editor;

/// <summary>
/// NotBSAnimation Editor - Full-featured animation editor window.
/// Supports keyframe editing, bone manipulation, and timeline scrubbing.
/// </summary>
public class AnimationEditor
{
    private SkeletalMeshAsset? _currentAsset;
    private AnimationClip? _currentClip;
    private float _currentTime = 0;
    private bool _isPlaying = false;
    private int _selectedBoneIndex = -1;
    private string _clipName = "NewAnimation";
    
    // Timeline
    private float _timelineZoom = 1.0f;
    private float _timelineScroll = 0;
    private int _selectedKeyframe = -1;
    
    // Recording
    private bool _isRecording = false;
    private Dictionary<string, (Vector3 pos, Quaternion rot, Vector3 scale)> _recordedPoses = new();
    
    public bool IsOpen { get; set; } = false;
    
    /// <summary>
    /// Open the animation editor with a skeletal mesh asset
    /// </summary>
    public void Open(SkeletalMeshAsset asset)
    {
        _currentAsset = asset;
        _currentClip = asset.Animations.FirstOrDefault();
        _currentTime = 0;
        IsOpen = true;
        
        Console.WriteLine($"[NotBSAnimation] Opened editor for: {asset.Name}");
    }
    
    /// <summary>
    /// Render the animation editor UI
    /// </summary>
    public void Render(NotBSUI ui, float x, float y, float width, float height)
    {
        if (!IsOpen || _currentAsset == null) return;
        
        // Main background panel
        ui.RoundedPanel(x, y, width, height, new Vector4(0.15f, 0.15f, 0.17f, 1), 8f);
        
        // Title bar
        ui.RoundedGradientPanel(x, y, width, 35, 
            new Vector4(0.25f, 0.35f, 0.45f, 1), 
            new Vector4(0.2f, 0.3f, 0.4f, 1), 8f);
        
        float panelX = x + 10;
        float panelY = y + 45;
        float panelW = width - 20;
        float panelH = height - 55;
        
        // Split into sections
        float leftPanelW = panelW * 0.25f;
        float centerPanelW = panelW * 0.5f;
        float rightPanelW = panelW * 0.25f;
        
        // Left: Bone hierarchy
        RenderBoneHierarchy(ui, panelX, panelY, leftPanelW, panelH);
        
        // Center: Viewport + Timeline
        float centerX = panelX + leftPanelW + 10;
        RenderViewport(ui, centerX, panelY, centerPanelW, panelH * 0.6f);
        RenderTimeline(ui, centerX, panelY + panelH * 0.6f + 10, centerPanelW, panelH * 0.4f - 10);
        
        // Right: Properties
        float rightX = centerX + centerPanelW + 10;
        RenderProperties(ui, rightX, panelY, rightPanelW, panelH);
    }
    
    private void RenderBoneHierarchy(NotBSUI ui, float x, float y, float width, float height)
    {
        // Panel background
        ui.RoundedPanel(x, y, width, height, new Vector4(0.18f, 0.18f, 0.2f, 1), 6f);
        
        if (_currentAsset == null) return;
        
        float itemY = y + 10;
        float itemH = 25;
        
        // Render bone tree
        for (int i = 0; i < _currentAsset.Mesh.Bones.Length; i++)
        {
            var bone = _currentAsset.Mesh.Bones[i];
            
            // Indent based on hierarchy depth
            int depth = GetBoneDepth(i);
            float indent = depth * 15;
            
            bool isSelected = i == _selectedBoneIndex;
            var normalColor = isSelected ? new Vector4(0.3f, 0.5f, 0.8f, 1) : new Vector4(0.22f, 0.22f, 0.24f, 1);
            var hoverColor = isSelected ? new Vector4(0.35f, 0.55f, 0.85f, 1) : new Vector4(0.28f, 0.28f, 0.3f, 1);
            var pressedColor = new Vector4(0.25f, 0.45f, 0.75f, 1);
            var shadowColor = new Vector4(0, 0, 0, 0.3f);
            var textColor = new Vector4(0.9f, 0.9f, 0.9f, 1);
            
            if (ui.ButtonEx(x + 10 + indent, itemY, width - 20 - indent, itemH, bone.Name, 
                normalColor, hoverColor, pressedColor, shadowColor, textColor))
            {
                _selectedBoneIndex = i;
            }
            
            itemY += itemH + 5;
        }
    }
    
    private void RenderViewport(NotBSUI ui, float x, float y, float width, float height)
    {
        // Viewport background
        ui.RoundedPanel(x, y, width, height, new Vector4(0.1f, 0.1f, 0.12f, 1), 6f);
        
        // TODO: Render 3D preview of skeleton
        // For now, just show placeholder text centered
        // Note: NotBSUI Text doesn't support positioning, so we skip it for now
    }
    
    private void RenderTimeline(NotBSUI ui, float x, float y, float width, float height)
    {
        // Timeline background
        ui.RoundedPanel(x, y, width, height, new Vector4(0.18f, 0.18f, 0.2f, 1), 6f);
        
        if (_currentClip == null) return;
        
        float timelineY = y + 10;
        float timelineH = height - 50;
        
        // Playback controls at bottom
        float btnW = 70;
        float btnH = 30;
        float btnY = y + height - btnH - 5;
        float btnX = x + 10;
        
        var normalColor = new Vector4(0.25f, 0.35f, 0.45f, 1);
        var hoverColor = new Vector4(0.3f, 0.4f, 0.5f, 1);
        var pressedColor = new Vector4(0.2f, 0.3f, 0.4f, 1);
        var shadowColor = new Vector4(0, 0, 0, 0.3f);
        var textColor = new Vector4(0.9f, 0.9f, 0.9f, 1);
        
        if (ui.ButtonEx(btnX, btnY, btnW, btnH, _isPlaying ? "Pause" : "Play", 
            normalColor, hoverColor, pressedColor, shadowColor, textColor))
        {
            _isPlaying = !_isPlaying;
        }
        
        btnX += btnW + 5;
        if (ui.ButtonEx(btnX, btnY, btnW, btnH, "Stop", 
            normalColor, hoverColor, pressedColor, shadowColor, textColor))
        {
            _isPlaying = false;
            _currentTime = 0;
        }
        
        btnX += btnW + 5;
        var recColor = _isRecording ? new Vector4(0.8f, 0.2f, 0.2f, 1) : normalColor;
        if (ui.ButtonEx(btnX, btnY, btnW, btnH, _isRecording ? "Stop Rec" : "Record", 
            recColor, hoverColor, pressedColor, shadowColor, textColor))
        {
            _isRecording = !_isRecording;
            if (_isRecording)
            {
                Console.WriteLine("[NotBSAnimation] Recording started");
            }
        }
        
        // Timeline scrubber
        float scrubberX = x + 10;
        float scrubberW = width - 20;
        float scrubberY = timelineY + 10;
        float scrubberH = 40;
        
        // Draw timeline background
        ui.RoundedPanel(scrubberX, scrubberY, scrubberW, scrubberH, new Vector4(0.12f, 0.12f, 0.14f, 1), 4f);
        
        // Draw playhead
        if (_currentClip.Duration > 0)
        {
            float playheadX = scrubberX + (scrubberW * (_currentTime / _currentClip.Duration));
            ui.Panel(playheadX - 2, scrubberY, 4, scrubberH, new Vector4(1, 0.3f, 0.3f, 1));
        }
        
        // Draw keyframes
        if (_selectedBoneIndex >= 0 && _currentAsset != null)
        {
            var boneName = _currentAsset.Mesh.Bones[_selectedBoneIndex].Name;
            if (_currentClip.BoneTracks.TryGetValue(boneName, out var track))
            {
                foreach (var key in track.PositionKeys)
                {
                    if (_currentClip.Duration > 0)
                    {
                        float keyX = scrubberX + (scrubberW * (key.Time / _currentClip.Duration));
                        ui.Circle(keyX, scrubberY + scrubberH / 2, 4, new Vector4(0.3f, 1, 0.3f, 1), true);
                    }
                }
            }
        }
    }
    
    private void RenderProperties(NotBSUI ui, float x, float y, float width, float height)
    {
        // Properties panel background
        ui.RoundedPanel(x, y, width, height, new Vector4(0.18f, 0.18f, 0.2f, 1), 6f);
        
        float propY = y + 10;
        float propH = 30;
        
        // Clip properties section
        if (_currentClip != null)
        {
            // Clip name display (read-only for now)
            ui.RoundedPanel(x + 10, propY, width - 20, propH, new Vector4(0.22f, 0.22f, 0.24f, 1), 4f);
            propY += propH + 10;
            
            propY += 10; // Spacing
        }
        
        // Selected bone properties
        if (_selectedBoneIndex >= 0 && _currentAsset != null)
        {
            var bone = _currentAsset.Mesh.Bones[_selectedBoneIndex];
            
            propY += 20; // Spacing for bone info
            
            var normalColor = new Vector4(0.25f, 0.45f, 0.35f, 1);
            var hoverColor = new Vector4(0.3f, 0.5f, 0.4f, 1);
            var pressedColor = new Vector4(0.2f, 0.4f, 0.3f, 1);
            var shadowColor = new Vector4(0, 0, 0, 0.3f);
            var textColor = new Vector4(0.9f, 0.9f, 0.9f, 1);
            
            // Add keyframe button
            if (ui.ButtonEx(x + 10, propY, width - 20, propH, "Add Keyframe", 
                normalColor, hoverColor, pressedColor, shadowColor, textColor))
            {
                AddKeyframe();
            }
            propY += propH + 5;
            
            // Delete keyframe button
            if (_selectedKeyframe >= 0)
            {
                var delColor = new Vector4(0.6f, 0.2f, 0.2f, 1);
                var delHover = new Vector4(0.7f, 0.25f, 0.25f, 1);
                
                if (ui.ButtonEx(x + 10, propY, width - 20, propH, "Delete Keyframe", 
                    delColor, delHover, pressedColor, shadowColor, textColor))
                {
                    DeleteKeyframe();
                }
            }
        }
    }
    
    /// <summary>
    /// Update animation playback
    /// </summary>
    public void Update(float deltaTime)
    {
        if (!IsOpen || !_isPlaying || _currentClip == null) return;
        
        _currentTime += deltaTime;
        
        if (_currentClip.Looping)
        {
            while (_currentTime > _currentClip.Duration)
                _currentTime -= _currentClip.Duration;
        }
        else
        {
            if (_currentTime > _currentClip.Duration)
            {
                _currentTime = _currentClip.Duration;
                _isPlaying = false;
            }
        }
    }
    
    private void AddKeyframe()
    {
        if (_currentClip == null || _selectedBoneIndex < 0 || _currentAsset == null) return;
        
        var boneName = _currentAsset.Mesh.Bones[_selectedBoneIndex].Name;
        
        if (!_currentClip.BoneTracks.TryGetValue(boneName, out var track))
        {
            track = new BoneTrack { BoneName = boneName };
            _currentClip.BoneTracks[boneName] = track;
        }
        
        // Add keyframe at current time
        // TODO: Get actual bone transform from viewport
        track.PositionKeys.Add(new PositionKeyframe { Time = _currentTime, Value = Vector3.Zero });
        track.RotationKeys.Add(new RotationKeyframe { Time = _currentTime, Value = Quaternion.Identity });
        track.ScaleKeys.Add(new ScaleKeyframe { Time = _currentTime, Value = Vector3.One });
        
        // Sort by time
        track.PositionKeys.Sort((a, b) => a.Time.CompareTo(b.Time));
        track.RotationKeys.Sort((a, b) => a.Time.CompareTo(b.Time));
        track.ScaleKeys.Sort((a, b) => a.Time.CompareTo(b.Time));
        
        Console.WriteLine($"[NotBSAnimation] Added keyframe for {boneName} at {_currentTime:F2}s");
    }
    
    private void DeleteKeyframe()
    {
        // TODO: Implement keyframe deletion
        Console.WriteLine("[NotBSAnimation] Delete keyframe not yet implemented");
    }
    
    private int GetBoneDepth(int boneIndex)
    {
        if (_currentAsset == null) return 0;
        
        int depth = 0;
        int current = boneIndex;
        
        while (current >= 0)
        {
            var bone = _currentAsset.Mesh.Bones[current];
            if (bone.ParentIndex < 0) break;
            current = bone.ParentIndex;
            depth++;
        }
        
        return depth;
    }
    
    /// <summary>
    /// Save current animation clip
    /// </summary>
    public void SaveClip(string path)
    {
        if (_currentClip == null)
        {
            Console.WriteLine("[NotBSAnimation] No clip to save");
            return;
        }
        
        AnimationAsset.SaveClip(path, _currentClip);
    }
    
    /// <summary>
    /// Create new animation clip
    /// </summary>
    public void CreateNewClip(string name, float duration = 1.0f, float frameRate = 30.0f)
    {
        _currentClip = new AnimationClip
        {
            Name = name,
            Duration = duration,
            FrameRate = frameRate,
            Looping = true
        };
        
        if (_currentAsset != null)
        {
            _currentAsset.Animations.Add(_currentClip);
        }
        
        _currentTime = 0;
        _clipName = name;
        
        Console.WriteLine($"[NotBSAnimation] Created new clip: {name}");
    }
}
