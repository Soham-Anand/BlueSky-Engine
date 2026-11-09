using System;

namespace BlueSky.SkyCore.Builtin
{
    /// <summary>
    /// Discrete 2D grid coordinate for a SkyWorld spatial cell (X, Z).
    /// </summary>
    public readonly struct WorldCellCoord : IEquatable<WorldCellCoord>
    {
        public readonly int X;
        public readonly int Z;

        public WorldCellCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        public static WorldCellCoord FromWorldPosition(float worldX, float worldZ, float cellSize)
        {
            float safeSize = MathF.Max(1.0f, cellSize);
            int cx = (int)MathF.Floor(worldX / safeSize);
            int cz = (int)MathF.Floor(worldZ / safeSize);
            return new WorldCellCoord(cx, cz);
        }

        public int ChebyshevDistanceTo(WorldCellCoord other)
        {
            int dx = System.Math.Abs(X - other.X);
            int dz = System.Math.Abs(Z - other.Z);
            return System.Math.Max(dx, dz);
        }

        public bool Equals(WorldCellCoord other) => X == other.X && Z == other.Z;
        public override bool Equals(object? obj) => obj is WorldCellCoord other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Z);
        public static bool operator ==(WorldCellCoord left, WorldCellCoord right) => left.Equals(right);
        public static bool operator !=(WorldCellCoord left, WorldCellCoord right) => !left.Equals(right);
        public override string ToString() => $"Cell({X}, {Z})";
    }

    /// <summary>
    /// Residency state of an entity managed by SkyWorld streaming.
    /// </summary>
    public enum WorldResidencyTier : byte
    {
        /// <summary>Full simulation: Physics, TeaScript, AI, and rendering active.</summary>
        ActiveSimulation = 0,
        /// <summary>Visible proxy/LOD only: rendered in viewport, physics/scripts paused.</summary>
        VisibleLodProxy = 1,
        /// <summary>Dormant on disk/memory snapshot: evicted from active simulation.</summary>
        Unloaded = 2
    }

    /// <summary>
    /// Component tagging an entity with its spatial grid cell and streaming residency state.
    /// </summary>
    public struct WorldCellComponent
    {
        public WorldCellCoord Cell;
        public WorldResidencyTier Tier;
        /// <summary>If true, this entity is never unloaded when leaving the active streaming radius (e.g. Player, Sun, NetworkManager).</summary>
        public bool IsPersistent;

        public WorldCellComponent(WorldCellCoord cell, bool isPersistent = false)
        {
            Cell = cell;
            Tier = WorldResidencyTier.ActiveSimulation;
            IsPersistent = isPersistent;
        }
    }
}
