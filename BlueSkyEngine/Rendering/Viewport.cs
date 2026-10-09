using System;
using BlueSky.Core.Math;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Platform;
using BlueSky.Platform.Input;

namespace BlueSky.Rendering
{
    public class Viewport : IDisposable
    {
        private readonly BlueSky.Platform.IWindow _window;
        private readonly BlueSky.Platform.IInputContext _input;
        private readonly IRenderer _renderer;
        private readonly World _world;
        
        private Entity _cameraEntity;
        private CameraComponent _camera;
        private TransformComponent _cameraTransform;
        
        // Fly camera state
        private float _pitch = -26.57f; // degrees, matches initial LookAt from (0,5,10) to origin
        private float _yaw = -90f;      // degrees, looking toward -Z
        
        // Input state
        private bool _isCapturing; // true when cursor is captured for camera rotation
        private bool _firstMouse = true;
        
        // Configuration
        private float _cameraSpeed = 10.0f; // Increased for better feel
        // The input layer already provides relative mouse deltas. Keep this
        // at the scale used by the stable editor camera path; applying a
        // second tiny scale makes the camera feel effectively frozen.
        private float _mouseSensitivity = 0.1f;
        
        private const float DEG2RAD = MathF.PI / 180f;
        
        private bool _disposed;

        public Viewport(BlueSky.Platform.IWindow window, BlueSky.Platform.IInputContext input, World world, IRenderer renderer)
        {
            _window = window;
            _world = world;
            _renderer = renderer;
            _input = input;
            
            InitializeCamera();
        }

        private void InitializeCamera()
        {
            _cameraEntity = _world.CreateEntity();
            _camera = new CameraComponent(60f, 0.1f, 1000f);
            _cameraTransform = new TransformComponent(
                new Vector3(0, 5, 10),
                Quaternion.Identity,
                Vector3.One
            );
            
            // Build initial rotation from pitch/yaw
            UpdateCameraRotation();
            
            _world.AddComponent(_cameraEntity, _camera);
            _world.AddComponent(_cameraEntity, _cameraTransform);
            // NameComponent skipped - contains string field, not unmanaged
        }

        /// <summary>
        /// Reinitialize the camera entity (call after clearing/loading scenes)
        /// </summary>
        public void ReinitializeCamera()
        {
            InitializeCamera();
        }

        public void Update(float deltaTime)
        {
            // Cap delta time to prevent camera teleporting on frame spikes
            deltaTime = MathF.Min(deltaTime, 0.1f);
            
            // Only update fly camera when in free camera mode
            if (BlueSky.Core.Gameplay.PlayerController.Instance.IsInFreeCameraMode)
            {
                // 1. Process mouse look (right button held)
                ProcessMouseLook();
                
                // 2. Process keyboard movement (continuous polling)
                ProcessKeyboardMovement(deltaTime);
            }
            
            // 3. Sync camera with ECS
            if (_vpW < 1 || _vpH < 1)
            {
                _camera.AspectRatio = (float)_window.Size.X / _window.Size.Y;
            }
            
            _world.AddComponent(_cameraEntity, _camera);
            _world.AddComponent(_cameraEntity, _cameraTransform);
        }

        private void ProcessMouseLook()
        {
            bool rightButtonHeld = _input.IsMouseButtonDown(MouseButton.Right);

            if (!rightButtonHeld)
            {
                // Release capture when right mouse button is released
                if (_isCapturing)
                {
                    _window.SetCursorCaptured(false);
                    _window.SetCursorVisible(true);
                    _isCapturing = false;
                }
                _firstMouse = true;
                return;
            }

            // On first frame of right-click, check if mouse is inside the viewport
            if (_firstMouse)
            {
                var mousePos = _input.MousePosition;

                // Hit-test against the viewport rect (set by the docking system)
                if (_vpW > 1 && _vpH > 1)
                {
                    bool insideViewport = mousePos.X >= _vpX && mousePos.X <= _vpX + _vpW
                                       && mousePos.Y >= _vpY && mousePos.Y <= _vpY + _vpH;
                    if (!insideViewport)
                        return; // Right-clicked outside viewport — do nothing
                }

                // Start capture: hide cursor + freeze system cursor position
                _window.SetCursorVisible(false);
                _window.SetCursorCaptured(true);
                _isCapturing = true;
                _firstMouse = false;
                return; // Consume this first frame to avoid a snap
            }

            // Use raw mouse deltas (accumulated by CocoaInput) — works infinitely
            var delta = _input.MouseDelta;
            if (MathF.Abs(delta.X) > 0.001f || MathF.Abs(delta.Y) > 0.001f)
            {
                // Reconciled rotation logic: Positive delta increases yaw/pitch
                _yaw += delta.X * _mouseSensitivity;
                _pitch += delta.Y * _mouseSensitivity;
                UpdateCameraRotation();
            }
        }

        private void UpdateCameraRotation()
        {
            // Build quaternion from Euler angles: yaw around world Y, then pitch around local X
            var yawQuat = new Quaternion(Vector3.Up, _yaw * DEG2RAD);
            var pitchQuat = new Quaternion(Vector3.Right, _pitch * DEG2RAD);
            _cameraTransform.SetRotation(yawQuat * pitchQuat);
        }

        private void ProcessKeyboardMovement(float deltaTime)
        {
            float speed = _cameraSpeed;
            if (_input.IsKeyDown(KeyCode.LeftShift))
                speed *= 2f;
            else if (_input.IsKeyDown(KeyCode.LeftControl))
                speed *= 0.5f;
            
            var forward = _cameraTransform.Forward;
            var right = _cameraTransform.Right;
            
            float moveX = 0f, moveY = 0f, moveZ = 0f;
            
            if (_input.IsKeyDown(KeyCode.W))
            {
                moveX += forward.X; moveY += forward.Y; moveZ += forward.Z; // FIX: W moves forward (was inverted)
            }
            if (_input.IsKeyDown(KeyCode.S))
            {
                moveX -= forward.X; moveY -= forward.Y; moveZ -= forward.Z; // FIX: S moves backward (was inverted)
            }
            if (_input.IsKeyDown(KeyCode.A))
            {
                moveX -= right.X; moveY -= right.Y; moveZ -= right.Z;
            }
            if (_input.IsKeyDown(KeyCode.D))
            {
                moveX += right.X; moveY += right.Y; moveZ += right.Z;
            }
            if (_input.IsKeyDown(KeyCode.E))
            {
                moveY += 1f; // World up
            }
            if (_input.IsKeyDown(KeyCode.Q))
            {
                moveY -= 1f; // World down
            }
            
            // Normalize and apply speed
            float lengthSq = moveX * moveX + moveY * moveY + moveZ * moveZ;
            if (lengthSq > 0.0001f)
            {
                float invLen = 1f / MathF.Sqrt(lengthSq);
                var movement = new Vector3(
                    moveX * invLen * speed * deltaTime,
                    moveY * invLen * speed * deltaTime,
                    moveZ * invLen * speed * deltaTime
                );
                _cameraTransform.Translate(movement);
            }
        }

        public void Render()
        {
            if (_vpW > 0 && _vpH > 0)
            {
                var (renderW, renderH) = GetPhysicalViewportSize();
                _renderer.SetViewport(0, 0, renderW, renderH);
            }

            _renderer.BeginFrame(0.1f, 0.1f, 0.1f);
            try
            {
                _renderer.RenderScene(_world, _camera, _cameraTransform);
            }
            finally
            {
                _renderer.EndFrame();
            }
        }

        public Entity GetCameraEntity() => _cameraEntity;
        public ref CameraComponent GetCamera() => ref _camera;
        public ref TransformComponent GetCameraTransform() => ref _cameraTransform;
        public IRenderer Renderer => _renderer;

        public int Width => (int)_window.Size.X;
        public int Height => (int)_window.Size.Y;
        public void SetMouseSensitivity(float sensitivity) => _mouseSensitivity = sensitivity;

        // ── Viewport rect for sub-region rendering ─────────────────────
        private float _vpX, _vpY, _vpW, _vpH;
        public void SetViewportRect(float x, float y, float w, float h)
        {
            _vpX = x; _vpY = y; _vpW = w; _vpH = h;
            if (_vpW > 0 && _vpH > 0)
            {
                var (renderW, renderH) = GetPhysicalViewportSize();
                _camera.AspectRatio = (float)renderW / renderH;

                _renderer.SetViewport(0, 0, renderW, renderH);
            }
        }

        private (int Width, int Height) GetPhysicalViewportSize()
        {
            float scaleX = _window.Size.X > 0 ? _window.FramebufferSize.X / _window.Size.X : 1.0f;
            float scaleY = _window.Size.Y > 0 ? _window.FramebufferSize.Y / _window.Size.Y : 1.0f;
            int renderW = Math.Max(1, (int)MathF.Round(_vpW * scaleX));
            int renderH = Math.Max(1, (int)MathF.Round(_vpH * scaleY));
            return (renderW, renderH);
        }

        // ── System.Numerics helpers for RHI rendering ──────────────────
        private static System.Numerics.Matrix4x4 ToNumerics(BlueSky.Core.Math.Matrix4x4 m) =>
            new System.Numerics.Matrix4x4(
                m.M11, m.M12, m.M13, m.M14,
                m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34,
                m.M41, m.M42, m.M43, m.M44);

        /// <summary>Returns the view matrix in System.Numerics format.</summary>
        public System.Numerics.Matrix4x4 GetViewMatrixNumerics()
        {
            var eye = _cameraTransform.Position;
            var target = eye + _cameraTransform.Forward;
            var up = BlueSky.Core.Math.Vector3.Up;
            return ToNumerics(BlueSky.Core.Math.Matrix4x4.CreateLookAt(eye, target, up));
        }

        /// <summary>Returns the projection matrix in System.Numerics format.</summary>
        public System.Numerics.Matrix4x4 GetProjectionMatrixNumerics()
        {
            return ToNumerics(_camera.GetProjectionMatrix());
        }

        public System.Numerics.Vector3 GetCameraPositionNumerics()
        {
            var p = _cameraTransform.Position;
            return new System.Numerics.Vector3(p.X, p.Y, p.Z);
        }

        /// <summary>
        /// Converts a screen/window coordinate (logical space) into a 3D ray for picking.
        /// </summary>
        public Ray GetRayFromMouse(System.Numerics.Vector2 mousePos)
        {
            // 1. Transform mouse to viewport local coordinates
            float localX = mousePos.X - _vpX;
            float localY = mousePos.Y - _vpY;

            // 2. Map to NDC space [-1, 1]
            // We use the viewport width/height stored in _vpW, _vpH
            float nx = (2.0f * localX) / _vpW - 1.0f;
            float ny = 1.0f - (2.0f * localY) / _vpH; // Flip Y as window 0 is top

            // 3. Unproject
            var view = GetViewMatrixNumerics();
            var proj = GetProjectionMatrixNumerics();
            var viewProj = view * proj;
            
            if (!System.Numerics.Matrix4x4.Invert(viewProj, out var invViewProj))
            {
                return new Ray(_cameraTransform.Position, _cameraTransform.Forward);
            }

            // Near point (depth 0)
            var nearPoint = System.Numerics.Vector4.Transform(new System.Numerics.Vector4(nx, ny, 0, 1), invViewProj);
            // Far point (depth 1)
            var farPoint = System.Numerics.Vector4.Transform(new System.Numerics.Vector4(nx, ny, 1, 1), invViewProj);

            var nearPos = new Vector3(nearPoint.X / nearPoint.W, nearPoint.Y / nearPoint.W, nearPoint.Z / nearPoint.W);
            var farPos = new Vector3(farPoint.X / farPoint.W, farPoint.Y / farPoint.W, farPoint.Z / farPoint.W);

            return new Ray(nearPos, farPos - nearPos);
        }

        public void FocusOnEntity(Entity entity)
        {
            if (_world.HasComponent<TransformComponent>(entity))
            {
                var targetTransform = _world.GetComponent<TransformComponent>(entity);
                var offset = _cameraTransform.Forward * -10f;
                _cameraTransform.SetPosition(targetTransform.Position + offset);
                _cameraTransform.LookAt(targetTransform.Position, Vector3.Up);
            }
        }

        public void ResetCamera()
        {
            _cameraTransform.SetPosition(new Vector3(0, 5, 10));
            _pitch = -26.57f;
            _yaw = -90f;
            UpdateCameraRotation();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }

}
