using System;
using System.Runtime.InteropServices;

namespace BlueSky.Core.Memory
{
    public unsafe class NativeBuffer<T> : IDisposable where T : unmanaged
    {
        public T* Pointer { get; private set; }
        public int Length { get; private set; }
        public int ByteLength => Length * sizeof(T);

        private bool _disposed;

        public NativeBuffer(int length)
        {
            Length = length;
            Pointer = (T*)NativeMemory.Alloc((nuint)ByteLength);
        }

        public ref T this[int index]
        {
            get
            {
                if (index < 0 || index >= Length) throw new IndexOutOfRangeException();
                return ref Pointer[index];
            }
        }

        public Span<T> AsSpan() => new Span<T>(Pointer, Length);

        public void Dispose()
        {
            if (!_disposed)
            {
                NativeMemory.Free(Pointer);
                Pointer = null;
                Length = 0;
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        ~NativeBuffer() => Dispose();
    }
}
