using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BlueSky.Core.Math
{
    /// <summary>Axis-Aligned Bounding Box for spatial queries and culling.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct AABB : IEquatable<AABB>
    {
        public readonly Vector3 Min, Max;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AABB(Vector3 min, Vector3 max) => (Min, Max) = (min, max);

        public Vector3 Center { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (Min + Max) * 0.5f; }
        public Vector3 Extents { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (Max - Min) * 0.5f; }
        public Vector3 Size { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Max - Min; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AABB FromCenterExtents(Vector3 center, Vector3 extents) => new(center - extents, center + extents);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Vector3 point) =>
            point.X >= Min.X && point.X <= Max.X &&
            point.Y >= Min.Y && point.Y <= Max.Y &&
            point.Z >= Min.Z && point.Z <= Max.Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(AABB other) =>
            Min.X <= other.Max.X && Max.X >= other.Min.X &&
            Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
            Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AABB Encapsulate(Vector3 point) => new(Vector3.Min(Min, point), Vector3.Max(Max, point));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AABB Merge(AABB other) => new(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AABB Expand(float amount) => new(Min - new Vector3(amount), Max + new Vector3(amount));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AABB Transform(Matrix4x4 matrix)
        {
            // Fast AABB transform: project all 3 axes and accumulate min/max
            var center = Center;
            var extents = Extents;
            var newCenter = new Vector3(
                matrix.M11 * center.X + matrix.M21 * center.Y + matrix.M31 * center.Z + matrix.M41,
                matrix.M12 * center.X + matrix.M22 * center.Y + matrix.M32 * center.Z + matrix.M42,
                matrix.M13 * center.X + matrix.M23 * center.Y + matrix.M33 * center.Z + matrix.M43);
            var newExtents = new Vector3(
                MathF.Abs(matrix.M11) * extents.X + MathF.Abs(matrix.M21) * extents.Y + MathF.Abs(matrix.M31) * extents.Z,
                MathF.Abs(matrix.M12) * extents.X + MathF.Abs(matrix.M22) * extents.Y + MathF.Abs(matrix.M32) * extents.Z,
                MathF.Abs(matrix.M13) * extents.X + MathF.Abs(matrix.M23) * extents.Y + MathF.Abs(matrix.M33) * extents.Z);
            return FromCenterExtents(newCenter, newExtents);
        }

        public bool Equals(AABB o) => Min == o.Min && Max == o.Max;
        public override bool Equals(object? obj) => obj is AABB o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(Min, Max);
        public override string ToString() => $"AABB({Min} → {Max})";
    }

    /// <summary>Bounding sphere for fast broad-phase tests.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct BoundingSphere : IEquatable<BoundingSphere>
    {
        public readonly Vector3 Center;
        public readonly float Radius;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BoundingSphere(Vector3 center, float radius) => (Center, Radius) = (center, radius);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Vector3 point) => Vector3.DistanceSquared(Center, point) <= Radius * Radius;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(BoundingSphere other)
        {
            float r = Radius + other.Radius;
            return Vector3.DistanceSquared(Center, other.Center) <= r * r;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(AABB box)
        {
            // Find closest point on AABB to sphere center
            var closest = Vector3.Clamp(Center, box.Min, box.Max);
            return Vector3.DistanceSquared(Center, closest) <= Radius * Radius;
        }

        public bool Equals(BoundingSphere o) => Center == o.Center && Radius == o.Radius;
        public override bool Equals(object? obj) => obj is BoundingSphere o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(Center, Radius);
        public override string ToString() => $"Sphere({Center}, r={Radius:F2})";
    }

    /// <summary>Ray for raycasting and picking.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Ray
    {
        public readonly Vector3 Origin;
        public readonly Vector3 Direction;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Ray(Vector3 origin, Vector3 direction) => (Origin, Direction) = (origin, direction.Normalize());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 GetPoint(float t) => Origin + Direction * t;

        /// <summary>Slab method AABB intersection. Returns true with t parameter on hit.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(AABB box, out float t)
        {
            t = 0;
            float tMin = float.MinValue, tMax = float.MaxValue;

            if (MathF.Abs(Direction.X) > BlueMath.Epsilon)
            {
                float invD = 1f / Direction.X;
                float t1 = (box.Min.X - Origin.X) * invD, t2 = (box.Max.X - Origin.X) * invD;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tMin = MathF.Max(tMin, t1); tMax = MathF.Min(tMax, t2);
                if (tMin > tMax) return false;
            }
            else if (Origin.X < box.Min.X || Origin.X > box.Max.X) return false;

            if (MathF.Abs(Direction.Y) > BlueMath.Epsilon)
            {
                float invD = 1f / Direction.Y;
                float t1 = (box.Min.Y - Origin.Y) * invD, t2 = (box.Max.Y - Origin.Y) * invD;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tMin = MathF.Max(tMin, t1); tMax = MathF.Min(tMax, t2);
                if (tMin > tMax) return false;
            }
            else if (Origin.Y < box.Min.Y || Origin.Y > box.Max.Y) return false;

            if (MathF.Abs(Direction.Z) > BlueMath.Epsilon)
            {
                float invD = 1f / Direction.Z;
                float t1 = (box.Min.Z - Origin.Z) * invD, t2 = (box.Max.Z - Origin.Z) * invD;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tMin = MathF.Max(tMin, t1); tMax = MathF.Min(tMax, t2);
                if (tMin > tMax) return false;
            }
            else if (Origin.Z < box.Min.Z || Origin.Z > box.Max.Z) return false;

            t = tMin >= 0 ? tMin : tMax;
            return t >= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(Plane plane, out float t)
        {
            t = 0;
            float denom = Plane.DotNormal(plane, Direction);
            if (MathF.Abs(denom) < BlueMath.Epsilon) return false;
            t = -Plane.DotCoordinate(plane, Origin) / denom;
            return t >= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(BoundingSphere sphere, out float t)
        {
            t = 0;
            var oc = Origin - sphere.Center;
            float b = Vector3.Dot(oc, Direction);
            float c = oc.LengthSquared - sphere.Radius * sphere.Radius;
            float discriminant = b * b - c;
            if (discriminant < 0) return false;
            t = -b - MathF.Sqrt(discriminant);
            if (t < 0) { t = -b + MathF.Sqrt(discriminant); if (t < 0) return false; }
            return true;
        }

        public override string ToString() => $"Ray({Origin} → {Direction})";
    }

    /// <summary>View frustum for culling (6 planes extracted from VP matrix).</summary>
    public struct Frustum
    {
        public Plane Left, Right, Bottom, Top, Near, Far;

        /// <summary>Extract frustum planes from a View*Projection matrix.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Frustum FromViewProjection(Matrix4x4 vp)
        {
            var f = new Frustum();
            // Left:   row3 + row0
            f.Left = Plane.Normalize(new Plane(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41));
            // Right:  row3 - row0
            f.Right = Plane.Normalize(new Plane(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41));
            // Bottom: row3 + row1
            f.Bottom = Plane.Normalize(new Plane(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42));
            // Top:    row3 - row1
            f.Top = Plane.Normalize(new Plane(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42));
            // Near:   row3 + row2
            f.Near = Plane.Normalize(new Plane(vp.M14 + vp.M13, vp.M24 + vp.M23, vp.M34 + vp.M33, vp.M44 + vp.M43));
            // Far:    row3 - row2
            f.Far = Plane.Normalize(new Plane(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43));
            return f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsPoint(Vector3 point)
        {
            if (Plane.DotCoordinate(Left, point) < 0) return false;
            if (Plane.DotCoordinate(Right, point) < 0) return false;
            if (Plane.DotCoordinate(Bottom, point) < 0) return false;
            if (Plane.DotCoordinate(Top, point) < 0) return false;
            if (Plane.DotCoordinate(Near, point) < 0) return false;
            if (Plane.DotCoordinate(Far, point) < 0) return false;
            return true;
        }

        /// <summary>Tests if an AABB is at least partially inside the frustum.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IntersectsAABB(AABB box)
        {
            Span<Plane> planes = stackalloc Plane[6];
            planes[0] = Left; planes[1] = Right; planes[2] = Bottom;
            planes[3] = Top; planes[4] = Near; planes[5] = Far;

            for (int i = 0; i < 6; i++)
            {
                var n = planes[i].Normal;
                // Find the positive vertex (farthest along the plane normal)
                var pVertex = new Vector3(
                    n.X >= 0 ? box.Max.X : box.Min.X,
                    n.Y >= 0 ? box.Max.Y : box.Min.Y,
                    n.Z >= 0 ? box.Max.Z : box.Min.Z);
                if (Plane.DotCoordinate(planes[i], pVertex) < 0)
                    return false; // Entirely outside this plane
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IntersectsSphere(BoundingSphere sphere)
        {
            Span<Plane> planes = stackalloc Plane[6];
            planes[0] = Left; planes[1] = Right; planes[2] = Bottom;
            planes[3] = Top; planes[4] = Near; planes[5] = Far;

            for (int i = 0; i < 6; i++)
            {
                if (Plane.DotCoordinate(planes[i], sphere.Center) < -sphere.Radius)
                    return false;
            }
            return true;
        }
    }
}
