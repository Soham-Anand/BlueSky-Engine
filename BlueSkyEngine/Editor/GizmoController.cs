using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using BlueSky.Editor.UI;
using BlueSky.Platform;
using BlueSky.Platform.Input;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Math;
using BlueSky.Rendering;
using BlueSky.Core.Scripting;
using BlueSky.Core.Scene;
using NotBSRenderer;

namespace BlueSky.Editor;

partial class Program
{
    private static void UpdateGizmoInteraction()
    {
        if (_viewport == null || _editorViewportRenderer == null || _selectedEntityId == 0 || _selectedEntityId >= 200)
        {
            if (_editorViewportRenderer != null) _editorViewportRenderer.HoveredAxis = -1;
            _isDraggingGizmo = false;
            return;
        }

        var mousePos = _input!.MousePosition;
        var mouseDown = _input.IsMouseButtonDown(MouseButton.Left);

        // 1. Get the actual entity from the world
        var entity = _world!.GetAllEntities().FirstOrDefault(e => e.Id == (int)_selectedEntityId);
        if (entity.Id == 0 || !_world.IsEntityValid(entity)) return;

        // 2. Get entity position
        if (!_world.HasComponent<TransformComponent>(entity)) return;
        ref var transform = ref _world.GetComponent<TransformComponent>(entity);
        var entityPos = transform.Position;

        // 3. Calculate gizmo scale (must match ViewportRenderer)
        var cameraPos = _viewport.GetCameraTransform().Position;
        float dist = BlueSky.Core.Math.Vector3.Distance(cameraPos, entityPos);
        float gizmoScale = MathF.Max(0.5f, dist * 0.15f);

        var ray = _viewport.GetRayFromMouse(mousePos);

        if (!_isDraggingGizmo)
        {
            // Hover test
            int hitAxis = _editorViewportRenderer.HitTestGizmo(ray, entityPos, gizmoScale);
            _editorViewportRenderer.HoveredAxis = hitAxis;

            if (mouseDown && hitAxis != -1)
            {
                _isDraggingGizmo = true;
                _draggedGizmoAxis = hitAxis;
                _gizmoDragStartEntityPos = entityPos;
                _gizmoDragStartRot = transform.Rotation;
                _gizmoDragStartScale = transform.Scale;
                
                // Define axis direction
                BlueSky.Core.Math.Vector3[] dirs = { 
                    BlueSky.Core.Math.Vector3.Right, 
                    BlueSky.Core.Math.Vector3.Up, 
                    BlueSky.Core.Math.Vector3.Back,
                    _viewport.GetCameraTransform().Forward * -1f // Center drag
                };
                _gizmoDragAxisDir = (hitAxis == 3) ? BlueSky.Core.Math.Vector3.Zero : dirs[hitAxis];

                if (_editorViewportRenderer.CurrentGizmoMode == ViewportRenderer.GizmoMode.Translate)
                {
                    if (hitAxis < 3)
                    {
                        ClosestPointOnAxis(ray, entityPos, _gizmoDragAxisDir, out float tAxis);
                        _gizmoDragDistanceOffset = tAxis;
                    }
                }
                else if (_editorViewportRenderer.CurrentGizmoMode == ViewportRenderer.GizmoMode.Rotate)
                {
                    if (hitAxis < 3)
                    {
                        // Calculate initial hit vector on the plane
                        var plane = new BlueSky.Core.Math.Plane(_gizmoDragAxisDir, -BlueSky.Core.Math.Vector3.Dot(_gizmoDragAxisDir, entityPos));
                        if (ray.Intersects(plane, out float t))
                        {
                            var hitPoint = ray.GetPoint(t);
                            _gizmoDragStartHitVec = (hitPoint - entityPos).Normalize();
                        }
                    }
                }
                else if (_editorViewportRenderer.CurrentGizmoMode == ViewportRenderer.GizmoMode.Scale)
                {
                    if (hitAxis < 3)
                    {
                        ClosestPointOnAxis(ray, entityPos, _gizmoDragAxisDir, out float tAxis);
                        _gizmoDragDistanceOffset = tAxis;
                    }
                }
            }
        }
        else
        {
            // Currently dragging
            if (!mouseDown)
            {
                _isDraggingGizmo = false;
                return;
            }

            var mode = _editorViewportRenderer.CurrentGizmoMode;

            if (mode == ViewportRenderer.GizmoMode.Translate)
            {
                if (_draggedGizmoAxis < 3)
                {
                    ClosestPointOnAxis(ray, _gizmoDragStartEntityPos, _gizmoDragAxisDir, out float tAxis);
                    float delta = tAxis - _gizmoDragDistanceOffset;
                    var newPos = _gizmoDragStartEntityPos + _gizmoDragAxisDir * delta;
                    transform.SetPosition(newPos);
                }
                else
                {
                    // Center drag
                    var planeNormal = _viewport.GetCameraTransform().Forward * -1f;
                    var plane = new BlueSky.Core.Math.Plane(planeNormal, -BlueSky.Core.Math.Vector3.Dot(planeNormal, _gizmoDragStartEntityPos));
                    if (ray.Intersects(plane, out float t))
                    {
                        var hitPoint = ray.GetPoint(t);
                        transform.SetPosition(hitPoint);
                    }
                }
            }
            else if (mode == ViewportRenderer.GizmoMode.Rotate)
            {
                if (_draggedGizmoAxis < 3)
                {
                    var plane = new BlueSky.Core.Math.Plane(_gizmoDragAxisDir, -BlueSky.Core.Math.Vector3.Dot(_gizmoDragAxisDir, _gizmoDragStartEntityPos));
                    if (ray.Intersects(plane, out float t))
                    {
                        var hitPoint = ray.GetPoint(t);
                        var currentHitVec = (hitPoint - _gizmoDragStartEntityPos).Normalize();
                        
                        // Calculate angle between start and current
                        float angle = BlueSky.Core.Math.Vector3.Angle(_gizmoDragStartHitVec, currentHitVec);
                        
                        // Determine sign via cross product
                        var cross = BlueSky.Core.Math.Vector3.Cross(_gizmoDragStartHitVec, currentHitVec);
                        if (BlueSky.Core.Math.Vector3.Dot(cross, _gizmoDragAxisDir) < 0) angle = -angle;

                        var deltaRot = new BlueSky.Core.Math.Quaternion(_gizmoDragAxisDir, angle * BlueSky.Core.Math.BlueMath.Deg2Rad);
                        // Apply global rotation (Delta * Original)
                        transform.SetRotation(deltaRot * _gizmoDragStartRot);
                    }
                }
            }
            else if (mode == ViewportRenderer.GizmoMode.Scale)
            {
                if (_draggedGizmoAxis < 3)
                {
                    ClosestPointOnAxis(ray, _gizmoDragStartEntityPos, _gizmoDragAxisDir, out float tAxis);
                    float delta = tAxis - _gizmoDragDistanceOffset;
                    
                    // Apply scale delta along the axis
                    var scaleDelta = _gizmoDragAxisDir * delta;
                    var newScale = _gizmoDragStartScale + scaleDelta;
                    
                    // Clamp to avoid negative or zero scale
                    newScale = BlueSky.Core.Math.Vector3.Max(newScale, new BlueSky.Core.Math.Vector3(0.01f));
                    
                    transform.SetScale(newScale);
                }
                else
                {
                    // Uniform scale (center drag)
                    // Use mouse delta instead of ray-plane for center scale as it feels better
                    // ... but for now, let's just do axis-based.
                }
            }
        }
    }

    private static void ClosestPointOnAxis(Ray ray, BlueSky.Core.Math.Vector3 axisOrigin, BlueSky.Core.Math.Vector3 axisDir, out float tAxis)
    {
        // Line-line shortest distance algorithm
        // Ray: P = O1 + t1*D1
        // Axis: Q = O2 + t2*D2
        var p1 = ray.Origin;
        var d1 = ray.Direction;
        var p2 = axisOrigin;
        var d2 = axisDir;

        var r = p1 - p2;
        float a = BlueSky.Core.Math.Vector3.Dot(d1, d1);
        float b = BlueSky.Core.Math.Vector3.Dot(d1, d2);
        float c = BlueSky.Core.Math.Vector3.Dot(d1, r);
        float e = BlueSky.Core.Math.Vector3.Dot(d2, d2);
        float f = BlueSky.Core.Math.Vector3.Dot(d2, r);

        float det = a * e - b * b;
        if (MathF.Abs(det) > 1e-6f)
        {
            tAxis = (a * f - b * c) / det;
        }
        else
        {
            tAxis = 0;
        }
    }

}
