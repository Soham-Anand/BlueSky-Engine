using System.Runtime.CompilerServices;
using BVec3 = BlueSky.Core.Math.Vector3;
using BQuat = BlueSky.Core.Math.Quaternion;
using SVec3 = System.Numerics.Vector3;
using SQuat = System.Numerics.Quaternion;

namespace BlueSky.Core.Gameplay
{
    public static class PhysicsConversions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVec3 ToSys(this BVec3 v) => new(v.X, v.Y, v.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BVec3 ToBlue(this SVec3 v) => new(v.X, v.Y, v.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SQuat ToSys(this BQuat q) => new(q.X, q.Y, q.Z, q.W);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BQuat ToBlue(this SQuat q) => new(q.X, q.Y, q.Z, q.W);
    }
}
