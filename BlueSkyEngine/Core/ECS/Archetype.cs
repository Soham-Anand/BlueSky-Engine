using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BlueSky.Core.ECS
{
    /// <summary>
    /// Identifies a unique combination of component types.
    /// Entities with the same components share an archetype.
    /// </summary>
    public readonly struct ArchetypeId : IEquatable<ArchetypeId>
    {
        public readonly int Id;
        public readonly int Hash;

        public ArchetypeId(int id, int hash)
        {
            Id = id;
            Hash = hash;
        }

        public bool Equals(ArchetypeId other) => Id == other.Id;
        public override bool Equals(object? obj) => obj is ArchetypeId other && Equals(other);
        public override int GetHashCode() => Hash;
        public static bool operator ==(ArchetypeId left, ArchetypeId right) => left.Equals(right);
        public static bool operator !=(ArchetypeId left, ArchetypeId right) => !left.Equals(right);
    }

    /// <summary>
    /// Describes the structure of an archetype - which components it contains.
    /// </summary>
    public class ArchetypeType
    {
        private readonly List<Type> _componentTypes;
        private readonly Dictionary<Type, int> _componentIndices;
        private int _hashCode;

        public IReadOnlyList<Type> ComponentTypes => _componentTypes;
        public int ComponentCount => _componentTypes.Count;

        public ArchetypeType(params Type[] componentTypes)
        {
            _componentTypes = componentTypes.OrderBy(t => t.FullName).ToList();
            _componentIndices = new Dictionary<Type, int>();
            
            for (int i = 0; i < _componentTypes.Count; i++)
            {
                _componentIndices[_componentTypes[i]] = i;
            }
            
            ComputeHash();
        }

        private void ComputeHash()
        {
            _hashCode = 17;
            foreach (var type in _componentTypes)
            {
                _hashCode = _hashCode * 31 + type.GetHashCode();
            }
        }

        public int GetComponentIndex(Type type)
        {
            return _componentIndices.TryGetValue(type, out var index) ? index : -1;
        }

        public bool HasComponent(Type type) => _componentIndices.ContainsKey(type);

        public override int GetHashCode() => _hashCode;

        public override bool Equals(object? obj)
        {
            if (obj is not ArchetypeType other) return false;
            if (_componentTypes.Count != other._componentTypes.Count) return false;
            
            for (int i = 0; i < _componentTypes.Count; i++)
            {
                if (_componentTypes[i] != other._componentTypes[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Creates a new archetype type with an additional component.
        /// </summary>
        public ArchetypeType AddComponent(Type type)
        {
            if (HasComponent(type))
                return this;
            
            var newTypes = new Type[_componentTypes.Count + 1];
            _componentTypes.CopyTo(newTypes, 0);
            newTypes[^1] = type;
            return new ArchetypeType(newTypes);
        }

        /// <summary>
        /// Creates a new archetype type without a component.
        /// </summary>
        public ArchetypeType RemoveComponent(Type type)
        {
            if (!HasComponent(type))
                return this;
            
            return new ArchetypeType(_componentTypes.Where(t => t != type).ToArray());
        }
    }
}
