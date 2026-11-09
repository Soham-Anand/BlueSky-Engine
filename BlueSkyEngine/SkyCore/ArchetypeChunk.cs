using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BlueSky.SkyCore
{
    /// <summary>
    /// A contiguous 16KB chunk of memory storing components for entities with the same archetype.
    /// Provides cache-friendly SOA iteration, O(1) component access, and per-column change version tracking.
    /// </summary>
    public sealed class ArchetypeChunk
    {
        public const int ChunkSize = 16384; // 16KB chunks
        
        private readonly ArchetypeType _archetype;
        private readonly int[] _componentSizes;
        private readonly int[] _componentOffsets;
        private readonly uint[] _changeVersions;
        private readonly int _entitySize;
        private readonly int _capacity;
        private int _count;
        
        private readonly byte[] _data;
        private readonly Entity[] _entities;
        private int[] _entityToRow;
        
        public ArchetypeType Archetype => _archetype;
        public int Count => _count;
        public int Capacity => _capacity;
        public bool IsFull => _count >= _capacity;
        public bool IsEmpty => _count == 0;

        public ArchetypeChunk(ArchetypeType archetype, uint initialVersion = 1)
        {
            _archetype = archetype;
            
            int componentCount = archetype.ComponentCount;
            _componentSizes = new int[componentCount];
            _changeVersions = new uint[componentCount];
            _entitySize = 0;
            
            for (int i = 0; i < componentCount; i++)
            {
                var type = archetype.ComponentTypes[i];
                int size = Marshal.SizeOf(type);
                _componentSizes[i] = size;
                _changeVersions[i] = initialVersion;
                _entitySize += size;
            }
            
            _capacity = ChunkSize / System.Math.Max(_entitySize, 1);
            _componentOffsets = new int[componentCount];
            
            // Calculate offsets for SOA (each component type has its own contiguous block)
            int currentOffset = 0;
            for (int i = 0; i < componentCount; i++)
            {
                _componentOffsets[i] = currentOffset;
                currentOffset += _componentSizes[i] * _capacity;
            }
            
            _data = new byte[currentOffset];
            _entities = new Entity[_capacity];
            _entityToRow = new int[1024];
            Array.Fill(_entityToRow, -1);
        }

        /// <summary>
        /// Gets the last world version at which the specified component column was modified.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetChangeVersion(int componentIndex) => _changeVersions[componentIndex];

        /// <summary>
        /// Returns true if the specified component column changed since <paramref name="lastSystemVersion"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool DidChange(int componentIndex, uint lastSystemVersion) =>
            (int)(_changeVersions[componentIndex] - lastSystemVersion) > 0;

        /// <summary>
        /// Marks a component column as modified at the given world version.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkChanged(int componentIndex, uint version)
        {
            if ((uint)componentIndex < (uint)_changeVersions.Length)
                _changeVersions[componentIndex] = version;
        }

        /// <summary>
        /// Adds an entity to this chunk. Returns the row index.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddEntity(Entity entity, uint version = 1)
        {
            if (IsFull)
                throw new InvalidOperationException("Chunk is full");

            if (entity.Id <= 0)
                throw new ArgumentOutOfRangeException(nameof(entity), "Entity IDs must be positive.");

            EnsureEntityToRowCapacity(entity.Id);
            if (_entityToRow[entity.Id] >= 0)
                throw new InvalidOperationException($"Entity {entity} is already in this chunk.");

            int row = _count;
            _entities[row] = entity;
            _entityToRow[entity.Id] = row;
            
            for (int i = 0; i < _componentSizes.Length; i++)
            {
                int offset = _componentOffsets[i] + (row * _componentSizes[i]);
                _data.AsSpan(offset, _componentSizes[i]).Clear();
                _changeVersions[i] = version;
            }

            _count++;
            return row;
        }

        /// <summary>
        /// Removes an entity from this chunk. The last entity swaps into its place.
        /// Returns the swapped entity (if any), or default if the removed entity was last.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity RemoveEntity(Entity entity, uint version = 1)
        {
            int row = GetRow(entity);
            if (row < 0 || row >= _count)
                throw new InvalidOperationException("Entity not in chunk");
            
            int lastRow = --_count;
            Entity swappedEntity = default;
            
            if (row != lastRow)
            {
                var lastEntity = _entities[lastRow];
                _entities[row] = lastEntity;
                _entityToRow[lastEntity.Id] = row;
                swappedEntity = lastEntity;
                
                for (int i = 0; i < _componentSizes.Length; i++)
                {
                    int size = _componentSizes[i];
                    int destOffset = _componentOffsets[i] + (row * size);
                    int srcOffset = _componentOffsets[i] + (lastRow * size);
                    _data.AsSpan(srcOffset, size).CopyTo(_data.AsSpan(destOffset, size));
                    _changeVersions[i] = version;
                }
            }
            
            _entityToRow[entity.Id] = -1;
            return swappedEntity;
        }

        /// <summary>
        /// Gets the row index for an entity. Returns -1 if not found.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetRow(Entity entity)
        {
            if (entity.Id <= 0 || entity.Id >= _entityToRow.Length)
                return -1;

            int row = _entityToRow[entity.Id];
            return (uint)row < (uint)_count && _entities[row] == entity ? row : -1;
        }

        /// <summary>
        /// Returns a span of all entities in this chunk.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<Entity> GetEntities() => _entities.AsSpan(0, _count);

        /// <summary>
        /// Gets a reference to a component at a specific row.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>(int row, int componentIndex) where T : unmanaged
        {
            int offset = _componentOffsets[componentIndex] + (row * _componentSizes[componentIndex]);
            return ref Unsafe.As<byte, T>(ref _data[offset]);
        }

        /// <summary>
        /// Sets a component value at a specific row and updates the column change version.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetComponent<T>(int row, int componentIndex, T value, uint version = 1) where T : unmanaged
        {
            int offset = _componentOffsets[componentIndex] + (row * _componentSizes[componentIndex]);
            Unsafe.As<byte, T>(ref _data[offset]) = value;
            _changeVersions[componentIndex] = version;
        }

        /// <summary>
        /// Gets the component index for a type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetComponentIndex(Type type) => _archetype.GetComponentIndex(type);

        /// <summary>
        /// Copies raw component bytes from this chunk to another chunk.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyComponentRaw(int sourceRow, int sourceCompIndex, ArchetypeChunk destChunk, int destRow, int destCompIndex, uint version = 1)
        {
            int srcOffset = _componentOffsets[sourceCompIndex] + (sourceRow * _componentSizes[sourceCompIndex]);
            int dstOffset = destChunk._componentOffsets[destCompIndex] + (destRow * destChunk._componentSizes[destCompIndex]);
            int size = _componentSizes[sourceCompIndex];
            
            _data.AsSpan(srcOffset, size).CopyTo(destChunk._data.AsSpan(dstOffset, size));
            destChunk._changeVersions[destCompIndex] = version;
        }

        /// <summary>
        /// Returns a span of all components of a specific type in this chunk.
        /// Provides cache-friendly contiguous iteration for SIMD.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> GetComponentSpan<T>(int componentIndex) where T : unmanaged
        {
            int offset = _componentOffsets[componentIndex];
            int sizeInBytes = _componentSizes[componentIndex] * _count;
            return MemoryMarshal.Cast<byte, T>(_data.AsSpan(offset, sizeInBytes));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void EnsureEntityToRowCapacity(int entityId)
        {
            if (entityId >= _entityToRow.Length)
            {
                int oldSize = _entityToRow.Length;
                int newSize = System.Math.Max(entityId + 1, _entityToRow.Length * 2);
                Array.Resize(ref _entityToRow, newSize);
                Array.Fill(_entityToRow, -1, oldSize, newSize - oldSize);
            }
        }
    }
}
