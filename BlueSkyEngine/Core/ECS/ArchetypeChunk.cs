using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BlueSky.Core.ECS
{
    /// <summary>
    /// A contiguous chunk of memory storing components for entities with the same archetype.
    /// Provides cache-friendly iteration and O(1) component access.
    /// </summary>
    public sealed class ArchetypeChunk
    {
        public const int ChunkSize = 16384; // 16KB chunks
        
        private readonly ArchetypeType _archetype;
        private readonly int[] _componentSizes;
        private readonly int[] _componentOffsets;
        private readonly int _entitySize;
        private int _capacity;
        private int _count;
        
        private byte[] _data;
        private Entity[] _entities;
        private int[] _entityToRow;
        
        public ArchetypeType Archetype => _archetype;
        public int Count => _count;
        public int Capacity => _capacity;
        public bool IsFull => _count >= _capacity;
        public bool IsEmpty => _count == 0;

        public ArchetypeChunk(ArchetypeType archetype)
        {
            _archetype = archetype;
            
            // Calculate component sizes and offsets
            int componentCount = archetype.ComponentCount;
            _componentSizes = new int[componentCount];
            _componentOffsets = new int[componentCount];
            
            int offset = 0;
            for (int i = 0; i < componentCount; i++)
            {
                var type = archetype.ComponentTypes[i];
                int size = Marshal.SizeOf(type);
                _componentSizes[i] = size;
                _componentOffsets[i] = offset;
                offset += size;
            }
            
            _entitySize = offset;
            _capacity = ChunkSize / System.Math.Max(_entitySize, 1);
            _data = new byte[_capacity * _entitySize];
            _entities = new Entity[_capacity];
            _entityToRow = new int[1024]; // Will resize as needed
            
            for (int i = 0; i < _entityToRow.Length; i++)
                _entityToRow[i] = -1;
        }

        /// <summary>
        /// Adds an entity to this chunk. Returns the row index.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddEntity(Entity entity)
        {
            if (IsFull)
                throw new InvalidOperationException("Chunk is full");
            
            int row = _count++;
            _entities[row] = entity;
            EnsureEntityToRowCapacity(entity.Id);
            _entityToRow[entity.Id] = row;
            
            // Zero-initialize components
            int offset = row * _entitySize;
            _data.AsSpan(offset, _entitySize).Clear();
            
            return row;
        }

        /// <summary>
        /// Removes an entity from this chunk. The last entity swaps into its place.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveEntity(Entity entity)
        {
            int row = GetRow(entity);
            if (row < 0 || row >= _count)
                throw new InvalidOperationException("Entity not in chunk");
            
            int lastRow = --_count;
            
            // If not the last row, swap the last entity into this slot
            if (row != lastRow)
            {
                var lastEntity = _entities[lastRow];
                _entities[row] = lastEntity;
                _entityToRow[lastEntity.Id] = row;
                
                // Copy component data
                int destOffset = row * _entitySize;
                int srcOffset = lastRow * _entitySize;
                _data.AsSpan(srcOffset, _entitySize).CopyTo(_data.AsSpan(destOffset));
            }
            
            _entityToRow[entity.Id] = -1;
        }

        /// <summary>
        /// Gets the row index for an entity. Returns -1 if not found.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetRow(Entity entity)
        {
            if (entity.Id >= _entityToRow.Length)
                return -1;
            return _entityToRow[entity.Id];
        }

        /// <summary>
        /// Returns a span of all entities in this chunk.
        /// </summary>
        public Span<Entity> GetEntities() => _entities.AsSpan(0, _count);

        /// <summary>
        /// Gets a reference to a component at a specific row.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe ref T GetComponent<T>(int row, int componentIndex) where T : unmanaged
        {
            int offset = row * _entitySize + _componentOffsets[componentIndex];
            fixed (byte* ptr = &_data[offset])
            {
                return ref *(T*)ptr;
            }
        }

        /// <summary>
        /// Sets a component value at a specific row.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void SetComponent<T>(int row, int componentIndex, T value) where T : unmanaged
        {
            int offset = row * _entitySize + _componentOffsets[componentIndex];
            fixed (byte* ptr = &_data[offset])
            {
                *(T*)ptr = value;
            }
        }

        /// <summary>
        /// Gets the component index for a type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetComponentIndex(Type type) => _archetype.GetComponentIndex(type);

        /// <summary>
        /// Copies raw component bytes from this chunk to another chunk.
        /// Used during archetype migration to preserve component data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyComponentRaw(int sourceRow, int sourceCompIndex, ArchetypeChunk destChunk, int destRow, int destCompIndex)
        {
            int srcOffset = sourceRow * _entitySize + _componentOffsets[sourceCompIndex];
            int dstOffset = destRow * destChunk._entitySize + destChunk._componentOffsets[destCompIndex];
            int size = _componentSizes[sourceCompIndex];
            
            _data.AsSpan(srcOffset, size).CopyTo(destChunk._data.AsSpan(dstOffset, size));
        }

        /// <summary>
        /// Returns a span of all components of a specific type in this chunk.
        /// Provides cache-friendly contiguous iteration.
        /// </summary>
        public unsafe Span<T> GetComponentSpan<T>(int componentIndex) where T : unmanaged
        {
            int size = _componentSizes[componentIndex];
            int offset = _componentOffsets[componentIndex];
            
            // This creates a view into the data - components are NOT contiguous
            // For true SOA, we'd need a different layout
            fixed (byte* ptr = _data)
            {
                return new Span<T>(ptr + offset, _count * _entitySize / size);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void EnsureEntityToRowCapacity(int entityId)
        {
            if (entityId >= _entityToRow.Length)
            {
                int newSize = System.Math.Max(entityId + 1, _entityToRow.Length * 2);
                Array.Resize(ref _entityToRow, newSize);
                for (int i = _entityToRow.Length / 2; i < _entityToRow.Length; i++)
                    _entityToRow[i] = -1;
            }
        }
    }
}
