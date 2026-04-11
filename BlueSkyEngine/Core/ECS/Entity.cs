using System;

namespace BlueSky.Core.ECS
{
    /// <summary>
    /// Represents a lightweight handle to an entity in the game world.
    /// Uses an ID for array indexing and a Generation to solve the ABA problem (entity reuse).
    /// </summary>
    public readonly struct Entity : IEquatable<Entity>
    {
        public readonly int Id;
        public readonly int Generation;

        public Entity(int id, int generation)
        {
            Id = id;
            Generation = generation;
        }

        public bool Equals(Entity other) => Id == other.Id && Generation == other.Generation;
        public override bool Equals(object? obj) => obj is Entity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, Generation);
        
        public static bool operator ==(Entity left, Entity right) => left.Equals(right);
        public static bool operator !=(Entity left, Entity right) => !left.Equals(right);
        
        public override string ToString() => $"Entity(Id: {Id}, Gen: {Generation})";
    }
}
