using System;
using System.Collections.Generic;

namespace BlueSky.Core.ECS
{
    /// <summary>
    /// Describes a query for entities with specific component requirements.
    /// </summary>
    public readonly struct QueryDescription : IEquatable<QueryDescription>
    {
        public readonly int Id;
        private readonly int _hashCode;
        private readonly Type[] _all;
        private readonly Type[] _any;
        private readonly Type[] _none;

        public IReadOnlyList<Type> All => _all;
        public IReadOnlyList<Type> Any => _any;
        public IReadOnlyList<Type> None => _none;

        public QueryDescription(Type[] all, Type[] any, Type[] none, int id)
        {
            _all = all ?? Array.Empty<Type>();
            _any = any ?? Array.Empty<Type>();
            _none = none ?? Array.Empty<Type>();
            Id = id;
            
            // Compute hash
            int hash = 17;
            foreach (var t in _all) hash = hash * 31 + t.GetHashCode();
            hash = hash * 31 + 17;
            foreach (var t in _any) hash = hash * 31 + t.GetHashCode();
            hash = hash * 31 + 17;
            foreach (var t in _none) hash = hash * 31 + t.GetHashCode();
            _hashCode = hash;
        }

        public bool Equals(QueryDescription other) => Id == other.Id;
        public override bool Equals(object? obj) => obj is QueryDescription other && Equals(other);
        public override int GetHashCode() => _hashCode;
        public static bool operator ==(QueryDescription left, QueryDescription right) => left.Equals(right);
        public static bool operator !=(QueryDescription left, QueryDescription right) => !left.Equals(right);

        /// <summary>
        /// Checks if an archetype matches this query.
        /// </summary>
        public bool Matches(ArchetypeType archetype)
        {
            // Must have all components in 'All'
            foreach (var type in _all)
            {
                if (!archetype.HasComponent(type))
                    return false;
            }
            
            // Must have at least one component in 'Any' (if Any is not empty)
            if (_any.Length > 0)
            {
                bool hasAny = false;
                foreach (var type in _any)
                {
                    if (archetype.HasComponent(type))
                    {
                        hasAny = true;
                        break;
                    }
                }
                if (!hasAny)
                    return false;
            }
            
            // Must not have any component in 'None'
            foreach (var type in _none)
            {
                if (archetype.HasComponent(type))
                    return false;
            }
            
            return true;
        }
    }

    /// <summary>
    /// Builder for creating queries fluently.
    /// </summary>
    public class QueryBuilder
    {
        private readonly List<Type> _all = new();
        private readonly List<Type> _any = new();
        private readonly List<Type> _none = new();
        private int _nextQueryId = 1;

        public QueryBuilder All<T>() where T : unmanaged
        {
            _all.Add(typeof(T));
            return this;
        }

        public QueryBuilder All(params Type[] types)
        {
            _all.AddRange(types);
            return this;
        }

        public QueryBuilder Any<T>() where T : unmanaged
        {
            _any.Add(typeof(T));
            return this;
        }

        public QueryBuilder Any(params Type[] types)
        {
            _any.AddRange(types);
            return this;
        }

        public QueryBuilder None<T>() where T : unmanaged
        {
            _none.Add(typeof(T));
            return this;
        }

        public QueryBuilder None(params Type[] types)
        {
            _none.AddRange(types);
            return this;
        }

        public QueryDescription Build()
        {
            return new QueryDescription(
                _all.ToArray(),
                _any.ToArray(),
                _none.ToArray(),
                _nextQueryId++
            );
        }
    }
}
