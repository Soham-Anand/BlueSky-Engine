using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BlueSky.Core.ECS.Builtin;

namespace BlueSky.Core.ECS
{
    /// <summary>
    /// Central ECS world managing entities, archetypes, and queries.
    /// Uses archetype-based chunk storage for cache-efficient component access.
    /// </summary>
    public sealed class World : IDisposable
    {
        private int _nextEntityId = 1;
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
        /// Cached query with matching archetypes.
        /// </summary>
        internal class Query
        {
            public QueryDescription Description;
            public List<Archetype> MatchingArchetypes = new();
            public bool IsDirty = true;
        }

        public World()
        {
            // Create empty archetype for entities with no components
            var emptyType = new ArchetypeType();
            var emptyArchetype = new Archetype
            {
                Id = new ArchetypeId(0, emptyType.GetHashCode()),
                Type = emptyType
            };
            _archetypes[emptyType] = emptyArchetype;
            _archetypesById[emptyArchetype.Id] = emptyArchetype;
        }

        #region Entity Management

        public Entity CreateEntity()
        {
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
            
            // Add to empty archetype initially
            var emptyArchetype = _archetypes[new ArchetypeType()];
            AddEntityToArchetype(entity, emptyArchetype);
            
            return entity;
        }

        public void DestroyEntity(Entity entity)
        {
            if (!IsEntityValid(entity))
                return;
            
            // Remove from current archetype
            if (_entityLocations.TryGetValue(entity, out var chunk))
            {
                int removedRow = chunk.GetRow(entity);
                Entity swappedEntity = chunk.RemoveEntity(entity);
                
                // CRITICAL FIX: Update World's row index for the swapped entity
                if (swappedEntity.Id != 0) // default Entity has Id = 0
                {
                    _entityRowIndices[swappedEntity] = removedRow;
                }
                
                // Remove empty chunks
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
            // Callers often destroy or move entities while processing this list.
            // Snapshot the keys so structural changes cannot invalidate a live
            // Dictionary enumerator.
            var snapshot = _entityLocations.Keys.ToArray();
            return snapshot.Where(IsEntityValid);
        }

        /// <summary>
        /// Allocation-free resolve of an entity by numeric id (editor selection path).
        /// Iterates live keys without ToArray; returns false when missing/invalid.
        /// Callers that need per-frame selection must use this instead of
        /// GetAllEntities().FirstOrDefault(e => e.Id == id), which allocates
        /// a full snapshot + LINQ closure on every call.
        /// </summary>
        public bool TryResolveEntity(uint id, out Entity entity)
        {
            foreach (var key in _entityLocations.Keys)
            {
                if (key.Id == (int)id && IsEntityValid(key))
                {
                    entity = key;
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
            
            var currentChunk = _entityLocations[entity];
            var currentArchetype = _archetypes[currentChunk.Archetype];
            
            // Check if already has this component
            if (currentChunk.Archetype.HasComponent(typeof(T)))
            {
                // Just update the component
                int row = _entityRowIndices[entity];
                int compIndex = currentChunk.GetComponentIndex(typeof(T));
                currentChunk.SetComponent(row, compIndex, component);
                return;
            }
            
            // Create new archetype with this component
            var newType = currentChunk.Archetype.AddComponent(typeof(T));
            var newArchetype = GetOrCreateArchetype(newType);
            
            // Move entity to new archetype
            MoveEntityToArchetype(entity, currentArchetype, newArchetype);
            
            // Set the new component
            var newChunk = _entityLocations[entity];
            int newRow = _entityRowIndices[entity];
            int newCompIndex = newChunk.GetComponentIndex(typeof(T));
            newChunk.SetComponent(newRow, newCompIndex, component);
            
            // Invalidate queries
            InvalidateQueries();
        }

        public void RemoveComponent<T>(Entity entity) where T : unmanaged
        {
            if (!IsEntityValid(entity))
                throw new InvalidOperationException("Entity is not valid");
            
            var currentChunk = _entityLocations[entity];
            var currentArchetype = _archetypes[currentChunk.Archetype];
            
            if (!currentChunk.Archetype.HasComponent(typeof(T)))
                return; // Doesn't have this component
            
            // Create new archetype without this component
            var newType = currentChunk.Archetype.RemoveComponent(typeof(T));
            var newArchetype = GetOrCreateArchetype(newType);
            
            // Move entity
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
        /// Gets all chunks matching a query for iteration.
        /// </summary>
        public List<ArchetypeChunk> GetQueryChunks(QueryDescription query)
        {
            var q = GetQuery(query);
            var chunks = new List<ArchetypeChunk>();
            
            foreach (var archetype in q.MatchingArchetypes)
            {
                chunks.AddRange(archetype.Chunks.Where(c => c.Count > 0));
            }
            
            return chunks;
        }

        /// <summary>
        /// Iterates entities with a single component type.
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
        /// Iterates entities with two component types.
        /// </summary>
        public void ForEach<T1, T2>(Action<Entity, T1, T2> action) 
            where T1 : unmanaged where T2 : unmanaged
        {
            var queryBuilder = new QueryBuilder();
            queryBuilder.All<T1>();
            queryBuilder.All<T2>();
            var query = queryBuilder.Build();
            foreach (var entity in SnapshotQueryEntities(query))
            {
                if (TryGetComponent<T1>(entity, out var component1) &&
                    TryGetComponent<T2>(entity, out var component2))
                    action(entity, component1, component2);
            }
        }

        /// <summary>
        /// Iterates entities with three component types.
        /// </summary>
        public void ForEach<T1, T2, T3>(Action<Entity, T1, T2, T3> action)
            where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
        {
            var queryBuilder = new QueryBuilder();
            queryBuilder.All<T1>();
            queryBuilder.All<T2>();
            queryBuilder.All<T3>();
            var query = queryBuilder.Build();
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
            foreach (var chunk in GetQueryChunks(query))
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
            // Systems can add or remove systems while updating. Iterate a copy
            // so lifecycle changes do not invalidate the active enumeration.
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
            
            // Invalidate queries since we have a new archetype
            InvalidateQueries();
            
            return archetype;
        }

        private void AddEntityToArchetype(Entity entity, Archetype archetype)
        {
            // Find or create a chunk with space
            ArchetypeChunk? chunk = archetype.LastChunk;
            if (chunk == null || chunk.IsFull)
            {
                chunk = new ArchetypeChunk(archetype.Type);
                archetype.Chunks.Add(chunk);
            }
            
            int row = chunk.AddEntity(entity);
            _entityLocations[entity] = chunk;
            _entityRowIndices[entity] = row;
        }

        private void MoveEntityToArchetype(Entity entity, Archetype source, Archetype destination)
        {
            var sourceChunk = _entityLocations[entity];
            int sourceRow = _entityRowIndices[entity];
            
            // Find or create a chunk in the destination archetype
            var newChunk = destination.LastChunk;
            if (newChunk == null || newChunk.IsFull)
            {
                newChunk = new ArchetypeChunk(destination.Type);
                destination.Chunks.Add(newChunk);
            }
            
            int newRow = newChunk.AddEntity(entity);
            
            // Copy component data that exists in both archetypes using raw byte copy
            foreach (var type in destination.Type.ComponentTypes)
            {
                if (source.Type.HasComponent(type))
                {
                    int sourceCompIdx = sourceChunk.GetComponentIndex(type);
                    int destCompIdx = newChunk.GetComponentIndex(type);
                    sourceChunk.CopyComponentRaw(sourceRow, sourceCompIdx, newChunk, newRow, destCompIdx);
                }
            }
            
            // Remove from source and handle swapped entity
            Entity swappedEntity = sourceChunk.RemoveEntity(entity);
            
            // CRITICAL FIX: Update World's row index for the swapped entity
            if (swappedEntity.Id != 0) // default Entity has Id = 0
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
            
            // Dispose all systems
            _systems.Clear();
            
            _isDisposed = true;
        }
    }
}
