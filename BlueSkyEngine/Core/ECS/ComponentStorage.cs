using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BlueSky.Core.ECS
{
    internal interface IComponentStorage
    {
        bool HasComponent(int entityId);
        bool RemoveComponent(int entityId);
        object? GetBoxedComponent(int entityId);
        void SetBoxedComponent(int entityId, object component);
    }

    /// <summary>
    /// High-performance component storage with proper presence tracking.
    /// </summary>
    internal sealed class ComponentStorage<T> : IComponentStorage where T : unmanaged
    {
        private T[] _components;
        private readonly HashSet<int> _presentEntities;
        private int _capacity;

        public ComponentStorage(int initialCapacity = 1024)
        {
            _capacity = initialCapacity;
            _components = new T[initialCapacity];
            _presentEntities = new HashSet<int>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent(int entityId) => _presentEntities.Contains(entityId);

        public object? GetBoxedComponent(int entityId)
        {
            return HasComponent(entityId) ? _components[entityId] : null;
        }

        public void SetBoxedComponent(int entityId, object component)
        {
            AddComponent(entityId, (T)component);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent(int entityId)
        {
            if (!_presentEntities.Contains(entityId))
                throw new InvalidOperationException($"Entity {entityId} does not have component {typeof(T).Name}");
            
            EnsureCapacity(entityId);
            return ref _components[entityId];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddComponent(int entityId, T component)
        {
            EnsureCapacity(entityId);
            _components[entityId] = component;
            _presentEntities.Add(entityId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool RemoveComponent(int entityId)
        {
            if (!_presentEntities.Remove(entityId))
                return false;
            
            _components[entityId] = default;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> GetAllComponents() => _components.AsSpan(0, _capacity);

        public ReadOnlySpan<int> GetPresentEntities()
        {
            var entities = new int[_presentEntities.Count];
            _presentEntities.CopyTo(entities);
            return entities;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void EnsureCapacity(int entityId)
        {
            if (entityId >= _capacity)
            {
                int newCapacity = System.Math.Max(entityId + 1, _capacity * 2);
                Array.Resize(ref _components, newCapacity);
                _capacity = newCapacity;
            }
        }
    }
}
