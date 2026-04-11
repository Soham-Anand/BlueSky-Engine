using System;

namespace BlueSky.Core.Memory
{
    public class StructPool<T> where T : unmanaged
    {
        private T[] _data;
        private int _count;

        public int Count => _count;
        public int Capacity => _data.Length;

        public StructPool(int initialCapacity = 256)
        {
            _data = new T[initialCapacity];
            _count = 0;
        }

        public int Add(in T item)
        {
            if (_count == _data.Length)
            {
                Array.Resize(ref _data, _data.Length * 2);
            }
            
            _data[_count] = item;
            return _count++;
        }

        public ref T Get(int index)
        {
            if (index < 0 || index >= _count) throw new IndexOutOfRangeException();
            return ref _data[index];
        }

        public Span<T> AsSpan() => new Span<T>(_data, 0, _count);
        
        public void Clear()
        {
            _count = 0;
            // No need to clear actual data, just reset the count since it's unmanaged structs
        }
    }
}
