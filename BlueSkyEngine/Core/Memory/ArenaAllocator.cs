using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BlueSky.Core.Memory
{
    /// <summary>
    /// Bump-pointer arena allocator for per-frame temporary allocations.
    /// Zero GC pressure — allocates from a pre-allocated native memory block.
    /// Call Reset() once per frame to reclaim all memory at zero cost.
    /// </summary>
    public sealed unsafe class ArenaAllocator : IDisposable
    {
        private byte* _buffer;
        private int _capacity;
        private int _offset;
        private bool _disposed;

        /// <summary>Current number of bytes allocated this frame.</summary>
        public int Used => _offset;
        /// <summary>Total capacity in bytes.</summary>
        public int Capacity => _capacity;
        /// <summary>Remaining bytes available.</summary>
        public int Remaining => _capacity - _offset;

        /// <param name="capacityBytes">Size of the arena in bytes. Default 4MB.</param>
        public ArenaAllocator(int capacityBytes = 4 * 1024 * 1024)
        {
            _capacity = capacityBytes;
            _buffer = (byte*)NativeMemory.AllocZeroed((nuint)capacityBytes);
            _offset = 0;
        }

        /// <summary>Allocate a span of T from the arena. O(1) bump pointer. No GC.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> Alloc<T>(int count) where T : unmanaged
        {
            int size = sizeof(T) * count;
            // Align to 16 bytes for SIMD compatibility
            int aligned = (size + 15) & ~15;

            if (_offset + aligned > _capacity)
                throw new OutOfMemoryException($"ArenaAllocator exhausted: requested {size}B, remaining {Remaining}B");

            byte* ptr = _buffer + _offset;
            _offset += aligned;
            return new Span<T>(ptr, count);
        }

        /// <summary>Allocate a single T and return a pointer. O(1).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T* AllocOne<T>() where T : unmanaged
        {
            int size = sizeof(T);
            int aligned = (size + 15) & ~15;
            if (_offset + aligned > _capacity)
                throw new OutOfMemoryException($"ArenaAllocator exhausted");
            T* ptr = (T*)(_buffer + _offset);
            _offset += aligned;
            *ptr = default;
            return ptr;
        }

        /// <summary>Reset the arena. All allocations are instantly freed. O(1).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _offset = 0;
            // No need to zero memory — callers get fresh spans
        }

        /// <summary>Reset and zero-fill (slower, for security/debugging).</summary>
        public void ResetAndClear()
        {
            new Span<byte>(_buffer, _offset).Clear();
            _offset = 0;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                NativeMemory.Free(_buffer);
                _buffer = null;
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        ~ArenaAllocator() => Dispose();
    }

    /// <summary>
    /// Fixed-size block pool allocator. O(1) alloc and free via free-list.
    /// Zero GC pressure for objects that are frequently created/destroyed.
    /// </summary>
    public sealed unsafe class PoolAllocator : IDisposable
    {
        private byte* _buffer;
        private int _blockSize;
        private int _blockCount;
        private int _freeHead;      // Index of first free block (-1 = exhausted)
        private int _allocatedCount;
        private bool _disposed;

        public int BlockSize => _blockSize;
        public int TotalBlocks => _blockCount;
        public int AllocatedBlocks => _allocatedCount;
        public int FreeBlocks => _blockCount - _allocatedCount;

        /// <param name="blockSize">Size of each block in bytes (min 4 for free-list pointer).</param>
        /// <param name="blockCount">Number of blocks in the pool.</param>
        public PoolAllocator(int blockSize, int blockCount)
        {
            _blockSize = System.Math.Max(blockSize, 4); // Min 4 bytes for next-pointer
            _blockCount = blockCount;
            _buffer = (byte*)NativeMemory.AllocZeroed((nuint)(_blockSize * _blockCount));
            _allocatedCount = 0;

            // Build free-list: each block's first 4 bytes stores index of next free block
            for (int i = 0; i < _blockCount - 1; i++)
                *(int*)(_buffer + i * _blockSize) = i + 1;
            *(int*)(_buffer + (_blockCount - 1) * _blockSize) = -1; // End of list
            _freeHead = 0;
        }

        /// <summary>Allocate one block. O(1). Returns null if exhausted.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void* Alloc()
        {
            if (_freeHead < 0) return null;
            byte* block = _buffer + _freeHead * _blockSize;
            _freeHead = *(int*)block; // Follow free-list
            _allocatedCount++;
            // Zero the block
            new Span<byte>(block, _blockSize).Clear();
            return block;
        }

        /// <summary>Free a block back to the pool. O(1).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Free(void* block)
        {
            if (block == null) return;
            int index = (int)((byte*)block - _buffer) / _blockSize;
            *(int*)block = _freeHead;
            _freeHead = index;
            _allocatedCount--;
        }

        /// <summary>Reset all blocks to free state. O(n).</summary>
        public void Reset()
        {
            for (int i = 0; i < _blockCount - 1; i++)
                *(int*)(_buffer + i * _blockSize) = i + 1;
            *(int*)(_buffer + (_blockCount - 1) * _blockSize) = -1;
            _freeHead = 0;
            _allocatedCount = 0;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                NativeMemory.Free(_buffer);
                _buffer = null;
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        ~PoolAllocator() => Dispose();
    }
}
