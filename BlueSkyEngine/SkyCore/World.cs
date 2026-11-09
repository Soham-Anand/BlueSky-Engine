using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BlueSky.SkyCore
{
    public delegate void RefAction<T1>(Entity entity, ref T1 c1) where T1 : unmanaged;
    public delegate void RefAction<T1, T2>(Entity entity, ref T1 c1, ref T2 c2) where T1 : unmanaged where T2 : unmanaged;
    public delegate void RefAction<T1, T2, T3>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged;

    /// <summary>
    /// Central SkyCore ECS world managing entities, archetypes, and zero-allocation queries.
    /// Uses archetype-based 16KB SOA chunk storage for cache-efficient component access.
    /// </summary>
    public sealed class World : IDisposable
    {
        private int _nextEntityId = 1;
        private uint _version = 1;
        private readonly List<int> _freeEntityIds = new();
        private readonly Dictionary<int, int> _entityGenerations = new();
        private readonly Dictionary<Entity, ArchetypeChunk> _entityLocations = new();
        private readonly Dictionary<Entity, int> _entityRowIndices = new();
        
        // Archetype management
        private int _nextArchetypeId = 1;
        private readonly Dictionary<ArchetypeType, Archetype> _archetypes = new();
        private readonly Dictionary<ArchetypeId, Archetype> _archetypesById = new();
        
        // Query caching
        private readonly Dictionary<QueryDescription, Query> _queries = new();
        private readonly Dictionary<int, Query> _queriesById = new();
        
        // Systems
        private readonly List<SystemBase> _systems = new();
        private bool _isDisposed;

        /// <summary>
        /// Monotonically increasing world mutation version used for chunk change filtering.
        /// </summary>
        public uint Version => _version;

        /// <summary>
        /// Total number of live entities currently registered in the world.
        /// </summary>
        public int EntityCount => _entityLocations.Count;

        /// <summary>
        /// Represents an archetype with its chunks.
        /// </summary>
        internal class Archetype
        {
            public ArchetypeId Id;
            public ArchetypeType Type = new();
            public List<ArchetypeChunk> Chunks = new();
            public ArchetypeChunk? LastChunk => Chunks.Count > 0 ? Chunks[^1] : null;
        }

        /// <summary>
        /// Cached query with matching archetypes and reusable chunk list.
        /// </summary>
        internal class Query
        {
            public QueryDescription Description;
            public List<Archetype> MatchingArchetypes = new();
            public List<ArchetypeChunk> CachedChunks = new();
            public bool IsDirty = true;
        }

        public World()
        {
            var emptyType = new ArchetypeType();
            var emptyArchetype = new Archetype
            {
                Id = new ArchetypeId(0, emptyType.GetHashCode()),
                Type = emptyType
            };
            _archetypes[emptyType] = emptyArchetype;
            _archetypesById[emptyArchetype.Id] = emptyArchetype;
        }

        /// <summary>
        /// Creates a deferred command buffer for recording structural changes during iteration.
        /// </summary>
        public SkyCoreCommandBuffer CreateCommandBuffer() => new();

        #region Entity Management

        public Entity CreateEntity()
        {
            unchecked { _version++; }
            int id;
            int generation;
            
            if (_freeEntityIds.Count > 0)
            {
                id = _freeEntityIds[^1];
                _freeEntityIds.RemoveAt(_freeEntityIds.Count - 1);
                generation = _entityGenerations[id] + 1;
            }
            else
            {
                id = _nextEntityId++;
                generation = 1;
            }
            
            _entityGenerations[id] = generation;
            var entity = new Entity(id, generation);
            
            var emptyArchetype = _archetypes[new ArchetypeType()];
            AddEntityToArchetype(entity, emptyArchetype);
            
            return entity;
        }

        public void DestroyEntity(Entity entity)
        {
            if (!IsEntityValid(entity))
                return;
            
            unchecked { _version++; }

            if (_entityLocations.TryGetValue(entity, out var chunk))
            {
                int removedRow = chunk.GetRow(entity);
                Entity swappedEntity = chunk.RemoveEntity(entity, _version);
                
                if (swappedEntity.Id != 0)
                {
                    _entityRowIndices[swappedEntity] = removedRow;
                }
                
                if (chunk.IsEmpty && chunk.Archetype.ComponentCount > 0)
                {
                    var archetype = _archetypes[chunk.Archetype];
                    archetype.Chunks.Remove(chunk);
                }
            }
            
            _entityLocations.Remove(entity);
            _entityRowIndices.Remove(entity);
            _freeEntityIds.Add(entity.Id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEntityValid(Entity entity)
        {
            return entity.Id > 0 &&
                   _entityGenerations.TryGetValue(entity.Id, out var gen) &&
                   gen == entity.Generation &&
                   _entityLocations.ContainsKey(entity);
        }

        public IEnumerable<Entity> GetAllEntities()
        {
            var snapshot = _entityLocations.Keys.ToArray();
            return snapshot.Where(IsEntityValid);
        }

        /// <summary>
        /// Allocation-free resolve of an entity by numeric id.
        /// </summary>
        public bool TryResolveEntity(uint id, out Entity entity)
        {
            if (id > 0 && id <= int.MaxValue && _entityGenerations.TryGetValue((int)id, out int gen))
            {
                var candidate = new Entity((int)id, gen);
                if (_entityLocations.ContainsKey(candidate))
                {
                    entity = candidate;
                    return true;
                }
            }
            entity = default;
            return false;
        }

        #endregion

        #region Component Management

        public void AddComponent<T>(Entity entity, T component) where T : unmanaged
        {
            if (!IsEntityValid(entity))
                throw new InvalidOperationException("Entity is not valid");
            
            unchecked { _version++; }

            var currentChunk = _entityLocations[entity];
            var currentArchetype = _archetypes[currentChunk.Archetype];
            
            if (currentChunk.Archetype.HasComponent(typeof(T)))
            {
                int row = _entityRowIndices[entity];
                int compIndex = currentChunk.GetComponentIndex(typeof(T));
                currentChunk.SetComponent(row, compIndex, component, _version);
                return;
            }
            
            var newType = currentChunk.Archetype.AddComponent(typeof(T));
            var newArchetype = GetOrCreateArchetype(newType);
            
            MoveEntityToArchetype(entity, currentArchetype, newArchetype);
            
            var newChunk = _entityLocations[entity];
            int newRow = _entityRowIndices[entity];
            int newCompIndex = newChunk.GetComponentIndex(typeof(T));
            newChunk.SetComponent(newRow, newCompIndex, component, _version);
            
            InvalidateQueries();
        }

        public void RemoveComponent<T>(Entity entity) where T : unmanaged
        {
            if (!IsEntityValid(entity))
                throw new InvalidOperationException("Entity is not valid");
            
            var currentChunk = _entityLocations[entity];
            var currentArchetype = _archetypes[currentChunk.Archetype];
            
            if (!currentChunk.Archetype.HasComponent(typeof(T)))
                return;
            
            unchecked { _version++; }

            var newType = currentChunk.Archetype.RemoveComponent(typeof(T));
            var newArchetype = GetOrCreateArchetype(newType);
            
            MoveEntityToArchetype(entity, currentArchetype, newArchetype);
            
            InvalidateQueries();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent<T>(Entity entity) where T : unmanaged
        {
            if (!IsEntityValid(entity) || !_entityLocations.TryGetValue(entity, out var chunk))
                return false;
            return chunk.Archetype.HasComponent(typeof(T));
        }

        public bool HasComponent(Entity entity, Type type)
        {
            if (!IsEntityValid(entity) || !_entityLocations.TryGetValue(entity, out var chunk))
                return false;
            return chunk.Archetype.HasComponent(type);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>(Entity entity) where T : unmanaged
        {
            if (!IsEntityValid(entity))
                throw new InvalidOperationException("Entity is not valid");
            
            var chunk = _entityLocations[entity];
            int row = _entityRowIndices[entity];
            int compIndex = chunk.GetComponentIndex(typeof(T));
            
            if (compIndex < 0)
                throw new InvalidOperationException($"Entity does not have component {typeof(T).Name}");
            
            unchecked { _version++; }
            chunk.MarkChanged(compIndex, _version);
            return ref chunk.GetComponent<T>(row, compIndex);
        }

        public bool TryGetComponent<T>(Entity entity, out T component) where T : unmanaged
        {
            component = default;
            
            if (!IsEntityValid(entity) || !_entityLocations.TryGetValue(entity, out var chunk))
                return false;
            
            int compIndex = chunk.GetComponentIndex(typeof(T));
            if (compIndex < 0)
                return false;
            
            int row = _entityRowIndices[entity];
            component = chunk.GetComponent<T>(row, compIndex);
            return true;
        }

        #endregion

        #region Query System

        public QueryBuilder CreateQuery() => new QueryBuilder();

        internal Query GetQuery(QueryDescription description)
        {
            if (_queries.TryGetValue(description, out var query))
            {
                if (query.IsDirty)
                    UpdateQuery(query);
                return query;
            }
            
            query = new Query { Description = description };
            UpdateQuery(query);
            _queries[description] = query;
            _queriesById[description.Id] = query;
            
            return query;
        }

        private void UpdateQuery(Query query)
        {
            query.MatchingArchetypes.Clear();
            
            foreach (var archetype in _archetypes.Values)
            {
                if (query.Description.Matches(archetype.Type))
                    query.MatchingArchetypes.Add(archetype);
            }
            
            query.IsDirty = false;
        }

        private void InvalidateQueries()
        {
            foreach (var query in _queries.Values)
                query.IsDirty = true;
        }

        /// <summary>
        /// Zero-allocation struct enumeration over all non-empty chunks matching a query.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public QueryChunkEnumerable EnumerateQueryChunks(QueryDescription query)
        {
            var q = GetQuery(query);
            return new QueryChunkEnumerable(q.MatchingArchetypes);
        }

        /// <summary>
        /// Gets all non-empty chunks matching a query, reusing the query's cached list to avoid heap allocations.
        /// </summary>
        public List<ArchetypeChunk> GetQueryChunks(QueryDescription query)
        {
            var q = GetQuery(query);
            var chunks = q.CachedChunks;
            chunks.Clear();
            
            for (int a = 0; a < q.MatchingArchetypes.Count; a++)
            {
                var archChunks = q.MatchingArchetypes[a].Chunks;
                for (int c = 0; c < archChunks.Count; c++)
                {
                    var chunk = archChunks[c];
                    if (chunk.Count > 0)
                        chunks.Add(chunk);
                }
            }
            
            return chunks;
        }

        /// <summary>
        /// Zero-allocation in-place ref iteration over entities with component T1.
        /// </summary>
        public void ForEachRef<T1>(RefAction<T1> action) where T1 : unmanaged
        {
            unchecked { _version++; }
            var query = new QueryBuilder().All<T1>().Build();
            foreach (var chunk in EnumerateQueryChunks(query))
            {
                int idx1 = chunk.GetComponentIndex(typeof(T1));
                chunk.MarkChanged(idx1, _version);
                var entities = chunk.GetEntities();
                var span1 = chunk.GetComponentSpan<T1>(idx1);
                for (int i = 0; i < entities.Length; i++)
                {
                    action(entities[i], ref span1[i]);
                }
            }
        }

        /// <summary>
        /// Zero-allocation in-place ref iteration over entities with components T1 and T2.
        /// </summary>
        public void ForEachRef<T1, T2>(RefAction<T1, T2> action)
            where T1 : unmanaged where T2 : unmanaged
        {
            unchecked { _version++; }
            var query = new QueryBuilder().All<T1>().All<T2>().Build();
            foreach (var chunk in EnumerateQueryChunks(query))
            {
                int idx1 = chunk.GetComponentIndex(typeof(T1));
                int idx2 = chunk.GetComponentIndex(typeof(T2));
                chunk.MarkChanged(idx1, _version);
                chunk.MarkChanged(idx2, _version);
                var entities = chunk.GetEntities();
                var span1 = chunk.GetComponentSpan<T1>(idx1);
                var span2 = chunk.GetComponentSpan<T2>(idx2);
                for (int i = 0; i < entities.Length; i++)
                {
                    action(entities[i], ref span1[i], ref span2[i]);
                }
            }
        }

        /// <summary>
        /// Zero-allocation in-place ref iteration over entities with components T1, T2, and T3.
        /// </summary>
        public void ForEachRef<T1, T2, T3>(RefAction<T1, T2, T3> action)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
        {
            unchecked { _version++; }
            var query = new QueryBuilder().All<T1>().All<T2>().All<T3>().Build();
            foreach (var chunk in EnumerateQueryChunks(query))
            {
                int idx1 = chunk.GetComponentIndex(typeof(T1));
                int idx2 = chunk.GetComponentIndex(typeof(T2));
                int idx3 = chunk.GetComponentIndex(typeof(T3));
                chunk.MarkChanged(idx1, _version);
                chunk.MarkChanged(idx2, _version);
                chunk.MarkChanged(idx3, _version);
                var entities = chunk.GetEntities();
                var span1 = chunk.GetComponentSpan<T1>(idx1);
                var span2 = chunk.GetComponentSpan<T2>(idx2);
                var span3 = chunk.GetComponentSpan<T3>(idx3);
                for (int i = 0; i < entities.Length; i++)
                {
                    action(entities[i], ref span1[i], ref span2[i], ref span3[i]);
                }
            }
        }

        /// <summary>
        /// Iterates entities with a single component type (snapshot-safe for structural changes).
        /// </summary>
        public void ForEach<T>(Action<Entity, T> action) where T : unmanaged
        {
            var query = new QueryBuilder().All<T>().Build();
            foreach (var entity in SnapshotQueryEntities(query))
            {
                if (TryGetComponent<T>(entity, out var component))
                    action(entity, component);
            }
        }

        /// <summary>
        /// Iterates entities with two component types (snapshot-safe for structural changes).
        /// </summary>
        public void ForEach<T1, T2>(Action<Entity, T1, T2> action) 
            where T1 : unmanaged where T2 : unmanaged
        {
            var query = new QueryBuilder().All<T1>().All<T2>().Build();
            foreach (var entity in SnapshotQueryEntities(query))
            {
                if (TryGetComponent<T1>(entity, out var component1) &&
                    TryGetComponent<T2>(entity, out var component2))
                    action(entity, component1, component2);
            }
        }

        /// <summary>
        /// Iterates entities with three component types (snapshot-safe for structural changes).
        /// </summary>
        public void ForEach<T1, T2, T3>(Action<Entity, T1, T2, T3> action)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
        {
            var query = new QueryBuilder().All<T1>().All<T2>().All<T3>().Build();
            foreach (var entity in SnapshotQueryEntities(query))
            {
                if (TryGetComponent<T1>(entity, out var component1) &&
                    TryGetComponent<T2>(entity, out var component2) &&
                    TryGetComponent<T3>(entity, out var component3))
                    action(entity, component1, component2, component3);
            }
        }

        private Entity[] SnapshotQueryEntities(QueryDescription query)
        {
            var entities = new List<Entity>();
            foreach (var chunk in EnumerateQueryChunks(query))
                entities.AddRange(chunk.GetEntities().ToArray());
            return entities.ToArray();
        }

        #endregion

        #region System Management

        public void AddSystem(SystemBase system)
        {
            if (_systems.Contains(system))
                return;

            system.Initialize(this);
            _systems.Add(system);
        }

        public void RemoveSystem(SystemBase system)
        {
            _systems.Remove(system);
        }

        public void Update(float deltaTime)
        {
            foreach (var system in _systems.ToArray())
            {
                if (_systems.Contains(system))
                    system.Update(deltaTime);
            }
        }

        #endregion

        #region Private Helpers

        private Archetype GetOrCreateArchetype(ArchetypeType type)
        {
            if (_archetypes.TryGetValue(type, out var archetype))
                return archetype;
            
            archetype = new Archetype
            {
                Id = new ArchetypeId(_nextArchetypeId++, type.GetHashCode()),
                Type = type
            };
            
            _archetypes[type] = archetype;
            _archetypesById[archetype.Id] = archetype;
            
            InvalidateQueries();
            
            return archetype;
        }

        private void AddEntityToArchetype(Entity entity, Archetype archetype)
        {
            ArchetypeChunk? chunk = archetype.LastChunk;
            if (chunk == null || chunk.IsFull)
            {
                chunk = new ArchetypeChunk(archetype.Type, _version);
                archetype.Chunks.Add(chunk);
            }
            
            int row = chunk.AddEntity(entity, _version);
            _entityLocations[entity] = chunk;
            _entityRowIndices[entity] = row;
        }

        private void MoveEntityToArchetype(Entity entity, Archetype source, Archetype destination)
        {
            var sourceChunk = _entityLocations[entity];
            int sourceRow = _entityRowIndices[entity];
            
            var newChunk = destination.LastChunk;
            if (newChunk == null || newChunk.IsFull)
            {
                newChunk = new ArchetypeChunk(destination.Type, _version);
                destination.Chunks.Add(newChunk);
            }
            
            int newRow = newChunk.AddEntity(entity, _version);
            
            foreach (var type in destination.Type.ComponentTypes)
            {
                if (source.Type.HasComponent(type))
                {
                    int sourceCompIdx = sourceChunk.GetComponentIndex(type);
                    int destCompIdx = newChunk.GetComponentIndex(type);
                    sourceChunk.CopyComponentRaw(sourceRow, sourceCompIdx, newChunk, newRow, destCompIdx, _version);
                }
            }
            
            Entity swappedEntity = sourceChunk.RemoveEntity(entity, _version);
            
            if (swappedEntity.Id != 0)
            {
                _entityRowIndices[swappedEntity] = sourceRow;
            }
            
            if (sourceChunk.IsEmpty && source.Type.ComponentCount > 0)
            {
                source.Chunks.Remove(sourceChunk);
            }
            
            _entityLocations[entity] = newChunk;
            _entityRowIndices[entity] = newRow;
        }

        #endregion

        public void Dispose()
        {
            if (_isDisposed)
                return;
            
            _systems.Clear();
            _isDisposed = true;
        }
    }
}
