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
        private float _mouseSensitivity = 0.1f; // Increased sensitivity
        
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
            
            // 1. Process mouse look (right button held)
            ProcessMouseLook();
            
            // 2. Process keyboard movement (continuous polling)
            ProcessKeyboardMovement(deltaTime);
            
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
                moveX -= forward.X; moveY -= forward.Y; moveZ -= forward.Z; // W moves forward
            }
            if (_input.IsKeyDown(KeyCode.S))
            {
                moveX += forward.X; moveY += forward.Y; moveZ += forward.Z; // S moves backward
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
            _renderer.BeginFrame(0.1f, 0.1f, 0.1f);
            _renderer.RenderScene(_world, _camera, _cameraTransform);
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
                _camera.AspectRatio = _vpW / _vpH;
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

    public static class Primitives
    {
        public static (float[] vertices, uint[] indices) CreateCube(float size = 1.0f)
        {
            var s = size * 0.5f;
            var vertices = new float[]
            {
                // Front face
                -s, -s,  s,  0,  0,  1,  0, 0,
                 s, -s,  s,  0,  0,  1,  1, 0,
                 s,  s,  s,  0,  0,  1,  1, 1,
                -s,  s,  s,  0,  0,  1,  0, 1,
                
                // Back face
                -s, -s, -s,  0,  0, -1,  1, 0,
                -s,  s, -s,  0,  0, -1,  1, 1,
                 s,  s, -s,  0,  0, -1,  0, 1,
                 s, -s, -s,  0,  0, -1,  0, 0,
                
                // Top face
                -s,  s, -s,  0,  1,  0,  0, 1,
                -s,  s,  s,  0,  1,  0,  0, 0,
                 s,  s,  s,  0,  1,  0,  1, 0,
                 s,  s, -s,  0,  1,  0,  1, 1,
                
                // Bottom face
                -s, -s, -s,  0, -1,  0,  0, 0,
                 s, -s, -s,  0, -1,  0,  1, 0,
                 s, -s,  s,  0, -1,  0,  1, 1,
                -s, -s,  s,  0, -1,  0,  0, 1,
                
                // Right face
                 s, -s, -s,  1,  0,  0,  1, 0,
                 s,  s, -s,  1,  0,  0,  1, 1,
                 s,  s,  s,  1,  0,  0,  0, 1,
                 s, -s,  s,  1,  0,  0,  0, 0,
                
                // Left face
                -s, -s, -s, -1,  0,  0,  0, 0,
                -s, -s,  s, -1,  0,  0,  1, 0,
                -s,  s,  s, -1,  0,  0,  1, 1,
                -s,  s, -s, -1,  0,  0,  0, 1
            };

            var indices = new uint[]
            {
                0,  1,  2,  0,  2,  3,   // Front
                4,  5,  6,  4,  6,  7,   // Back
                8,  9,  10, 8,  10, 11,  // Top
                12, 13, 14, 12, 14, 15,  // Bottom
                16, 17, 18, 16, 18, 19,  // Right
                20, 21, 22, 20, 22, 23   // Left
            };

            return (vertices, indices);
        }
        
        /// <summary>
        /// Creates a smooth cube with shared vertices and averaged normals for better shading
        /// </summary>
        public static (float[] vertices, uint[] indices) CreateSmoothCube(float size = 1.0f)
        {
            var s = size * 0.5f;
            
            // Use shared vertices (8 corners instead of 24 separate vertices)
            var positions = new Vector3[]
            {
                new Vector3(-s, -s, -s), // 0: left-bottom-back
                new Vector3( s, -s, -s), // 1: right-bottom-back
                new Vector3( s,  s, -s), // 2: right-top-back
                new Vector3(-s,  s, -s), // 3: left-top-back
                new Vector3(-s, -s,  s), // 4: left-bottom-front
                new Vector3( s, -s,  s), // 5: right-bottom-front
                new Vector3( s,  s,  s), // 6: right-top-front
                new Vector3(-s,  s,  s)  // 7: left-top-front
            };
            
            // Calculate smooth normals by averaging face normals at each vertex
            var normals = new Vector3[8];
            
            // Each vertex normal is the average of its adjacent face normals
            normals[0] = new Vector3(-1, -1, -1).Normalize(); // left + bottom + back
            normals[1] = new Vector3( 1, -1, -1).Normalize(); // right + bottom + back
            normals[2] = new Vector3( 1,  1, -1).Normalize(); // right + top + back
            normals[3] = new Vector3(-1,  1, -1).Normalize(); // left + top + back
            normals[4] = new Vector3(-1, -1,  1).Normalize(); // left + bottom + front
            normals[5] = new Vector3( 1, -1,  1).Normalize(); // right + bottom + front
            normals[6] = new Vector3( 1,  1,  1).Normalize(); // right + top + front
            normals[7] = new Vector3(-1,  1,  1).Normalize(); // left + top + front
            
            // Texture coordinates for each vertex
            var texCoords = new Vector2[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1)
            };
            
            // Pack into vertex array (position + normal + texcoord = 8 floats per vertex)
            var vertices = new float[8 * 8];
            for (int i = 0; i < 8; i++)
            {
                int offset = i * 8;
                vertices[offset + 0] = positions[i].X;
                vertices[offset + 1] = positions[i].Y;
                vertices[offset + 2] = positions[i].Z;
                vertices[offset + 3] = normals[i].X;
                vertices[offset + 4] = normals[i].Y;
                vertices[offset + 5] = normals[i].Z;
                vertices[offset + 6] = texCoords[i].X;
                vertices[offset + 7] = texCoords[i].Y;
            }
            
            // Indices for the 12 triangles (6 faces * 2 triangles each)
            var indices = new uint[]
            {
                // Front face (z = +s)
                4, 5, 6,  4, 6, 7,
                
                // Back face (z = -s)
                1, 0, 3,  1, 3, 2,
                
                // Top face (y = +s)
                3, 7, 6,  3, 6, 2,
                
                // Bottom face (y = -s)
                0, 1, 5,  0, 5, 4,
                
                // Right face (x = +s)
                1, 2, 6,  1, 6, 5,
                
                // Left face (x = -s)
                0, 4, 7,  0, 7, 3
            };
            
            return (vertices, indices);
        }

        public static (float[] vertices, uint[] indices) CreateSphere(float radius = 1.0f, int segments = 32, int rings = 16)
        {
            var vertices = new System.Collections.Generic.List<float>();
            var indices = new System.Collections.Generic.List<uint>();

            for (int ring = 0; ring <= rings; ring++)
            {
                float theta = ring * MathF.PI / rings;
                float sinTheta = MathF.Sin(theta);
                float cosTheta = MathF.Cos(theta);

                for (int segment = 0; segment <= segments; segment++)
                {
                    float phi = segment * 2 * MathF.PI / segments;
                    float sinPhi = MathF.Sin(phi);
                    float cosPhi = MathF.Cos(phi);

                    float x = cosPhi * sinTheta;
                    float y = cosTheta;
                    float z = sinPhi * sinTheta;

                    vertices.Add(radius * x);
                    vertices.Add(radius * y);
                    vertices.Add(radius * z);
                    vertices.Add(x);
                    vertices.Add(y);
                    vertices.Add(z);
                    vertices.Add((float)segment / segments);
                    vertices.Add((float)ring / rings);
                }
            }

            for (int ring = 0; ring < rings; ring++)
            {
                for (int segment = 0; segment < segments; segment++)
                {
                    uint current = (uint)(ring * (segments + 1) + segment);
                    uint next = current + (uint)(segments + 1);

                    indices.Add(current);
                    indices.Add(next);
                    indices.Add(current + 1);

                    indices.Add(current + 1);
                    indices.Add(next);
                    indices.Add(next + 1);
                }
            }

            return (vertices.ToArray(), indices.ToArray());
        }
    }
}
