using System;
using System.Collections.Concurrent;

namespace BlueSky.Core.Memory
{
    public class ObjectPool<T> where T : class, new()
    {
        private readonly ConcurrentBag<T> _objects;
        private readonly Func<T> _objectGenerator;

        public ObjectPool(Func<T>? objectGenerator = null)
        {
            _objects = new ConcurrentBag<T>();
            _objectGenerator = objectGenerator ?? (() => new T());
        }

        public T Get()
        {
            if (_objects.TryTake(out var item))
                return item!;
            
            return _objectGenerator();
        }

        public void Return(T item)
        {
            _objects.Add(item);
        }
    }
}
