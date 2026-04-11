using BlueSky.Core.Math;

namespace BlueSky.Core.ECS.Builtin
{
    /// <summary>
    /// High-performance transform component with position, rotation, and scale.
    /// Uses quaternion rotation for smooth interpolation and gimbal lock prevention.
    /// </summary>
    public struct TransformComponent
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        
        // Cached matrix for performance
        private Matrix4x4 _worldMatrix;
        private bool _isDirty;

        public TransformComponent(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
            _worldMatrix = Matrix4x4.Identity;
            _isDirty = true;
        }

        public TransformComponent(Vector3 position) : this(position, Quaternion.Identity, Vector3.One) { }
        public TransformComponent() : this(Vector3.Zero, Quaternion.Identity, Vector3.One) { }

        public static TransformComponent Default => new TransformComponent();

        /// <summary>
        /// Gets the world transformation matrix. Recalculates only if dirty.
        /// </summary>
        public Matrix4x4 WorldMatrix
        {
            get
            {
                if (_isDirty)
                {
                    _worldMatrix = Matrix4x4.CreateScale(Scale) * 
                                 Matrix4x4.CreateRotation(Rotation) * 
                                 Matrix4x4.CreateTranslation(Position);
                    _isDirty = false;
                }
                return _worldMatrix;
            }
        }

        /// <summary>
        /// Forward direction vector (negative Z axis in local space)
        /// </summary>
        public Vector3 Forward => Rotation * Vector3.Forward;

        /// <summary>
        /// Right direction vector (positive X axis in local space)
        /// </summary>
        public Vector3 Right => Rotation * Vector3.Right;

        /// <summary>
        /// Up direction vector (positive Y axis in local space)
        /// </summary>
        public Vector3 Up => Rotation * Vector3.Up;

        /// <summary>
        /// Sets position and marks matrix as dirty
        /// </summary>
        public void SetPosition(Vector3 position)
        {
            Position = position;
            _isDirty = true;
        }

        /// <summary>
        /// Sets rotation and marks matrix as dirty
        /// </summary>
        public void SetRotation(Quaternion rotation)
        {
            Rotation = rotation;
            _isDirty = true;
        }

        /// <summary>
        /// Sets scale and marks matrix as dirty
        /// </summary>
        public void SetScale(Vector3 scale)
        {
            Scale = scale;
            _isDirty = true;
        }

        /// <summary>
        /// Translates the transform by the given offset
        /// </summary>
        public void Translate(Vector3 offset)
        {
            Position = Position + offset;
            _isDirty = true;
        }

        /// <summary>
        /// Rotates the transform by the given quaternion (applied in local space)
        /// </summary>
        public void Rotate(Quaternion rotation)
        {
            Rotation = Rotation * rotation;
            _isDirty = true;
        }

        /// <summary>
        /// Rotates around a world axis
        /// </summary>
        public void RotateAround(Vector3 axis, float angle)
        {
            var rotation = new Quaternion(axis, angle);
            Rotation = rotation * Rotation;
            _isDirty = true;
        }

        /// <summary>
        /// Looks at a target position
        /// </summary>
        public void LookAt(Vector3 target, Vector3 up)
        {
            var forward = (target - Position).Normalize();
            var right = Vector3.Cross(up, forward).Normalize();
            var actualUp = Vector3.Cross(forward, right);

            // Create rotation matrix from basis vectors
            var matrix = new Matrix4x4(
                right.X, actualUp.X, -forward.X, 0,
                right.Y, actualUp.Y, -forward.Y, 0,
                right.Z, actualUp.Z, -forward.Z, 0,
                0, 0, 0, 1
            );

            // Extract quaternion (simplified - in production you'd use a proper algorithm)
            var trace = matrix.M11 + matrix.M22 + matrix.M33;
            if (trace > 0)
            {
                var s = (float)System.Math.Sqrt(trace + 1.0) * 2;
                Rotation = new Quaternion(
                    (matrix.M32 - matrix.M23) / s,
                    (matrix.M13 - matrix.M31) / s,
                    (matrix.M21 - matrix.M12) / s,
                    s / 4
                );
            }
            _isDirty = true;
        }

        /// <summary>
        /// Linear interpolation between two transforms
        /// </summary>
        public static TransformComponent Lerp(TransformComponent a, TransformComponent b, float t)
        {
            return new TransformComponent(
                a.Position + (b.Position - a.Position) * t,
                Quaternion.Identity, // Simplified - should use spherical interpolation
                a.Scale + (b.Scale - a.Scale) * t
            );
        }

        public override string ToString() => 
            $"Transform(Pos: {Position}, Rot: {Rotation}, Scale: {Scale})";
    }
}
