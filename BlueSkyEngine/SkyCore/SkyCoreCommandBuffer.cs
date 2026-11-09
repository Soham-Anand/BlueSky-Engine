using System;
using System.Collections.Generic;

namespace BlueSky.SkyCore
{
    /// <summary>
    /// Deferred command buffer for recording structural SkyCore ECS mutations
    /// (entity creation/destruction, component addition/removal) safely during
    /// chunk span iteration or multi-threaded jobs, then playing them back in order.
    /// </summary>
    public sealed class SkyCoreCommandBuffer
    {
        private readonly List<Action<World, Dictionary<int, Entity>>> _commands = new();
        private int _nextTempId = -1;

        public int CommandCount => _commands.Count;

        /// <summary>
        /// Queues creation of a new entity and returns a temporary placeholder handle
        /// (negative Id) that can be passed to <see cref="AddComponent{T}"/> within the same buffer.
        /// </summary>
        public Entity CreateEntity()
        {
            int tempId = _nextTempId--;
            var placeholder = new Entity(tempId, 0);
            _commands.Add((world, remap) =>
            {
                remap[tempId] = world.CreateEntity();
            });
            return placeholder;
        }

        /// <summary>
        /// Queues destruction of an entity (or a deferred entity created in this buffer).
        /// </summary>
        public void DestroyEntity(Entity entity)
        {
            _commands.Add((world, remap) =>
            {
                var resolved = Resolve(entity, remap);
                if (world.IsEntityValid(resolved))
                    world.DestroyEntity(resolved);
            });
        }

        /// <summary>
        /// Queues adding or updating a component on an entity (or deferred entity handle).
        /// </summary>
        public void AddComponent<T>(Entity entity, T component) where T : unmanaged
        {
            _commands.Add((world, remap) =>
            {
                var resolved = Resolve(entity, remap);
                if (world.IsEntityValid(resolved))
                    world.AddComponent(resolved, component);
            });
        }

        /// <summary>
        /// Queues removing a component from an entity (or deferred entity handle).
        /// </summary>
        public void RemoveComponent<T>(Entity entity) where T : unmanaged
        {
            _commands.Add((world, remap) =>
            {
                var resolved = Resolve(entity, remap);
                if (world.IsEntityValid(resolved))
                    world.RemoveComponent<T>(resolved);
            });
        }

        /// <summary>
        /// Executes all recorded commands against <paramref name="world"/> in FIFO order and clears the buffer.
        /// </summary>
        public void Playback(World world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            if (_commands.Count == 0)
                return;

            var remap = new Dictionary<int, Entity>();
            for (int i = 0; i < _commands.Count; i++)
            {
                _commands[i](world, remap);
            }

            Clear();
        }

        public void Clear()
        {
            _commands.Clear();
            _nextTempId = -1;
        }

        private static Entity Resolve(Entity entity, Dictionary<int, Entity> remap)
        {
            if (entity.Id < 0 && remap.TryGetValue(entity.Id, out var actual))
                return actual;
            return entity;
        }
    }
}
