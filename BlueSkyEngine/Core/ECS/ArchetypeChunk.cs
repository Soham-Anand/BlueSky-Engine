using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BlueSky.Core.ECS
{
    /// <summary>
    /// A contiguous chunk of memory storing components for entities with the same archetype.
    /// Provides cache-friendly iteration and O(1) component access using SOA (Structure of Arrays) layout.
    /// </summary>
    public sealed class ArchetypeChunk
    {
        public const int ChunkSize = 16384; // 16KB chunks
        
        private readonly ArchetypeType _archetype;
        private readonly int[] _componentSizes;
        private readonly int[] _componentOffsets; // These are now offsets to the START of each component type array
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
            
            // Calculate component sizes
            int componentCount = archetype.ComponentCount;
            _componentSizes = new int[componentCount];
            _entitySize = 0;
            
            for (int i = 0; i < componentCount; i++)
            {
                var type = archetype.ComponentTypes[i];
                int size = Marshal.SizeOf(type);
                _componentSizes[i] = size;
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
            
            // Zero-initialize components for this row across all arrays
            for (int i = 0; i < _componentSizes.Length; i++)
            {
                int offset = _componentOffsets[i] + (row * _componentSizes[i]);
                _data.AsSpan(offset, _componentSizes[i]).Clear();
            }
            
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
                
                // Copy component data for each type (swap last into current)
                for (int i = 0; i < _componentSizes.Length; i++)
                {
                    int size = _componentSizes[i];
                    int destOffset = _componentOffsets[i] + (row * size);
                    int srcOffset = _componentOffsets[i] + (lastRow * size);
                    _data.AsSpan(srcOffset, size).CopyTo(_data.AsSpan(destOffset, size));
                }
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
            int offset = _componentOffsets[componentIndex] + (row * _componentSizes[componentIndex]);
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
            int offset = _componentOffsets[componentIndex] + (row * _componentSizes[componentIndex]);
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
            int srcOffset = _componentOffsets[sourceCompIndex] + (sourceRow * _componentSizes[sourceCompIndex]);
            int dstOffset = destChunk._componentOffsets[destCompIndex] + (destRow * destChunk._componentSizes[destCompIndex]);
            int size = _componentSizes[sourceCompIndex];
            
            _data.AsSpan(srcOffset, size).CopyTo(destChunk._data.AsSpan(dstOffset, size));
        }

        /// <summary>
        /// Returns a span of all components of a specific type in this chunk.
        /// Provides cache-friendly contiguous iteration for SIMD.
        /// </summary>
        public unsafe Span<T> GetComponentSpan<T>(int componentIndex) where T : unmanaged
        {
            int offset = _componentOffsets[componentIndex];
            fixed (byte* ptr = _data)
            {
                return new Span<T>(ptr + offset, _count);
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
