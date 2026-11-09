using System;
using BlueSky.Ramanujan;

namespace BlueSky.SkyCore.Builtin
{
    /// <summary>
    /// High-performance transform component with position, rotation, and scale.
    /// Hardened against NaN/Infinity and uninitialized default(TransformComponent) structs.
    /// </summary>
    public struct TransformComponent
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        
        private Matrix4x4 _worldMatrix;
        private bool _isDirty;

        public TransformComponent(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Position = SanitizeVector(position, Vector3.Zero);
            Rotation = SanitizeRotation(rotation);
            Scale = SanitizeScale(scale);
            _worldMatrix = Matrix4x4.Identity;
            _isDirty = true;
        }

        public TransformComponent(Vector3 position) : this(position, Quaternion.Identity, Vector3.One) { }
        public TransformComponent() : this(Vector3.Zero, Quaternion.Identity, Vector3.One) { }

        public static TransformComponent Default => new TransformComponent();

        /// <summary>
        /// Gets the world transformation matrix. Automatically guards against zero-initialized
        /// quaternions or zero/NaN scales so Matrix4x4.Invert never encounters a singular matrix.
        /// </summary>
        public Matrix4x4 WorldMatrix
        {
            get
            {
                var safePos = SanitizeVector(Position, Vector3.Zero);
                var safeRot = SanitizeRotation(Rotation);
                var safeScale = SanitizeScale(Scale);

                return Matrix4x4.CreateScale(safeScale) * 
                       Matrix4x4.CreateRotation(safeRot) * 
                       Matrix4x4.CreateTranslation(safePos);
            }
        }

        public Vector3 Forward => SanitizeRotation(Rotation) * Vector3.Forward;
        public Vector3 Right => SanitizeRotation(Rotation) * Vector3.Right;
        public Vector3 Up => SanitizeRotation(Rotation) * Vector3.Up;

        public void SetPosition(Vector3 position)
        {
            Position = SanitizeVector(position, Position);
            _isDirty = true;
        }

        public void SetRotation(Quaternion rotation)
        {
            Rotation = SanitizeRotation(rotation);
            _isDirty = true;
        }

        public void SetScale(Vector3 scale)
        {
            Scale = SanitizeScale(scale);
            _isDirty = true;
        }

        public void Translate(Vector3 offset)
        {
            Position = SanitizeVector(Position + offset, Position);
            _isDirty = true;
        }

        public void Rotate(Quaternion rotation)
        {
            Rotation = SanitizeRotation(SanitizeRotation(Rotation) * SanitizeRotation(rotation));
            _isDirty = true;
        }

        public void RotateAround(Vector3 axis, float angle)
        {
            var rotation = new Quaternion(axis, angle);
            Rotation = SanitizeRotation(rotation * SanitizeRotation(Rotation));
            _isDirty = true;
        }

        public void LookAt(Vector3 target, Vector3 up)
        {
            var delta = target - Position;
            if (delta.LengthSquared < 1e-8f)
                return;
            var forward = delta.Normalize();
            Rotation = SanitizeRotation(Quaternion.LookRotation(-forward, up));
            _isDirty = true;
        }

        public static TransformComponent Lerp(TransformComponent a, TransformComponent b, float t)
        {
            float clampedT = System.Math.Clamp(float.IsFinite(t) ? t : 0f, 0f, 1f);
            return new TransformComponent(
                a.Position + (b.Position - a.Position) * clampedT,
                Quaternion.Slerp(SanitizeRotation(a.Rotation), SanitizeRotation(b.Rotation), clampedT),
                a.Scale + (b.Scale - a.Scale) * clampedT
            );
        }

        private static Vector3 SanitizeVector(Vector3 v, Vector3 fallback)
        {
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
                return fallback;
            return v;
        }

        private static Quaternion SanitizeRotation(Quaternion q)
        {
            if (!float.IsFinite(q.X) || !float.IsFinite(q.Y) || !float.IsFinite(q.Z) || !float.IsFinite(q.W))
                return Quaternion.Identity;
            float lenSq = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
            if (lenSq < 1e-8f)
                return Quaternion.Identity;
            return q;
        }

        private static Vector3 SanitizeScale(Vector3 s)
        {
            if (!float.IsFinite(s.X) || !float.IsFinite(s.Y) || !float.IsFinite(s.Z))
                return Vector3.One;
            // Default-initialized struct has (0,0,0) scale — treat all-zero as Vector3.One
            if (MathF.Abs(s.X) < 1e-6f && MathF.Abs(s.Y) < 1e-6f && MathF.Abs(s.Z) < 1e-6f)
                return Vector3.One;
            return new Vector3(
                MathF.Abs(s.X) < 1e-5f ? 0.001f : s.X,
                MathF.Abs(s.Y) < 1e-5f ? 0.001f : s.Y,
                MathF.Abs(s.Z) < 1e-5f ? 0.001f : s.Z);
        }

        public override string ToString() => 
            $"Transform(Pos: {Position}, Rot: {Rotation}, Scale: {Scale})";
    }
}
