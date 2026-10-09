using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BlueSky.Core.ECS
{
    /// <summary>
    /// Describes a query for entities with specific component requirements.
    /// </summary>
    public readonly struct QueryDescription : IEquatable<QueryDescription>
    {
        public readonly int Id;
        private readonly int _hashCode;
        private readonly Type[]? _all;
        private readonly Type[]? _any;
        private readonly Type[]? _none;
        private readonly IReadOnlyList<Type>? _allView;
        private readonly IReadOnlyList<Type>? _anyView;
        private readonly IReadOnlyList<Type>? _noneView;
        private static readonly int EmptyHashCode = ComputeHash(Array.Empty<Type>(), Array.Empty<Type>(), Array.Empty<Type>());

        public IReadOnlyList<Type> All => _allView ?? Array.Empty<Type>();
        public IReadOnlyList<Type> Any => _anyView ?? Array.Empty<Type>();
        public IReadOnlyList<Type> None => _noneView ?? Array.Empty<Type>();

        public QueryDescription(Type[] all, Type[] any, Type[] none, int id)
        {
            _all = Normalize(all, nameof(all));
            _any = Normalize(any, nameof(any));
            _none = Normalize(none, nameof(none));
            _allView = Array.AsReadOnly(_all);
            _anyView = Array.AsReadOnly(_any);
            _noneView = Array.AsReadOnly(_none);
            Id = id;
            
            _hashCode = ComputeHash(_all, _any, _none);
        }

        private static Type[] Normalize(Type[]? types, string parameterName)
        {
            if (types == null || types.Length == 0)
                return Array.Empty<Type>();

            if (types.Any(type => type == null))
                throw new ArgumentException("Query component types cannot contain null.", parameterName);

            return types
                .Distinct()
                .OrderBy(type => type.AssemblyQualifiedName ?? type.FullName ?? type.Name, StringComparer.Ordinal)
                .ToArray();
        }

        private static int ComputeHash(Type[] all, Type[] any, Type[] none)
        {
            unchecked
            {
                int hash = 17;
                hash = AddTypesToHash(hash, all);
                hash = hash * 31 + 23;
                hash = AddTypesToHash(hash, any);
                hash = hash * 31 + 47;
                return AddTypesToHash(hash, none);
            }
        }

        private static int AddTypesToHash(int hash, Type[] types)
        {
            hash = hash * 31 + types.Length;
            foreach (var type in types)
                hash = hash * 31 + type.GetHashCode();
            return hash;
        }

        public bool Equals(QueryDescription other) =>
            (_all ?? Array.Empty<Type>()).SequenceEqual(other._all ?? Array.Empty<Type>()) &&
            (_any ?? Array.Empty<Type>()).SequenceEqual(other._any ?? Array.Empty<Type>()) &&
            (_none ?? Array.Empty<Type>()).SequenceEqual(other._none ?? Array.Empty<Type>());
        public override bool Equals(object? obj) => obj is QueryDescription other && Equals(other);
        public override int GetHashCode() =>
            _all == null && _any == null && _none == null ? EmptyHashCode : _hashCode;
        public static bool operator ==(QueryDescription left, QueryDescription right) => left.Equals(right);
        public static bool operator !=(QueryDescription left, QueryDescription right) => !left.Equals(right);

        /// <summary>
        /// Checks if an archetype matches this query.
        /// </summary>
        public bool Matches(ArchetypeType archetype)
        {
            // Must have all components in 'All'
            var all = _all ?? Array.Empty<Type>();
            var any = _any ?? Array.Empty<Type>();
            var none = _none ?? Array.Empty<Type>();

            foreach (var type in all)
            {
                if (!archetype.HasComponent(type))
                    return false;
            }
            
            // Must have at least one component in 'Any' (if Any is not empty)
            if (any.Length > 0)
            {
                bool hasAny = false;
                foreach (var type in any)
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
            foreach (var type in none)
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
        private static int _nextQueryId;

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
                Interlocked.Increment(ref _nextQueryId)
            );
        }
    }
}
