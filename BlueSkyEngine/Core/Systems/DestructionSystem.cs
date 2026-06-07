using System;
using System.Collections.Generic;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Math;

namespace BlueSky.Core.Systems
{
    /// <summary>
    /// Manages entity destruction and shard spawning.
    /// Optimized for high-performance impact effects.
    /// </summary>
    public sealed class DestructionSystem : SystemBase
    {
        private readonly Queue<Entity> _shardPool = new();
        private const int MaxShards = 500;

        protected override void OnInitialize()
        {
            // Pre-allocate shard pool for extreme optimization (zero runtime allocations)
            for (int i = 0; i < MaxShards; i++)
            {
                var shard = World!.CreateEntity();
                World.AddComponent(shard, new TransformComponent { Scale = Vector3.Zero }); // Hidden
                World.AddComponent(shard, new ShardTag());
                // We'll add MeshComponent only when activated to keep the draw call count low
                _shardPool.Enqueue(shard);
            }
        }

        public override void Update(float dt)
        {
            var query = World!.CreateQuery()
                .All<HealthComponent>()
                .All<FracturableComponent>()
                .All<TransformComponent>()
                .Build();

            var chunks = World.GetQueryChunks(query);
            foreach (var chunk in chunks)
            {
                var healths = chunk.GetComponentSpan<HealthComponent>(chunk.GetComponentIndex(typeof(HealthComponent)));
                var fracts = chunk.GetComponentSpan<FracturableComponent>(chunk.GetComponentIndex(typeof(FracturableComponent)));
                var transforms = chunk.GetComponentSpan<TransformComponent>(chunk.GetComponentIndex(typeof(TransformComponent)));
                var entities = chunk.GetEntities();

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (healths[i].IsDead)
                    {
                        Shatter(entities[i], transforms[i], fracts[i]);
                    }
                }
            }
        }

        private void Shatter(Entity entity, TransformComponent transform, FracturableComponent fract)
        {
            // 1. Hide the original entity
            World!.RemoveComponent<StaticMeshComponent>(entity);
            World.RemoveComponent<FracturableComponent>(entity); // Prevent double-shatter

            // 2. Spawn shards from pool
            int shardsToSpawn = System.Math.Min(fract.ShardCount, _shardPool.Count);
            var random = new Random();

            for (int i = 0; i < shardsToSpawn; i++)
            {
                var shard = _shardPool.Dequeue();
                
                // Position at center with random offset
                var offset = new Vector3(
                    (float)(random.NextDouble() * 2 - 1) * 0.5f,
                    (float)(random.NextDouble() * 2 - 1) * 0.5f,
                    (float)(random.NextDouble() * 2 - 1) * 0.5f
                );

                var shardTransform = new TransformComponent(transform.Position + offset);
                shardTransform.SetScale(new Vector3(0.3f)); // Small shards
                
                World.AddComponent(shard, shardTransform);
                World.AddComponent(shard, new StaticMeshComponent { MeshAssetId = fract.ShardMeshAssetId });
                
                // If we had a velocity component, we'd apply it here
                // World.AddComponent(shard, new VelocityComponent(offset * fract.ExplodeForce));
            }

            LogDestruction(entity);
        }

        private void LogDestruction(Entity e)
        {
            // In a real engine, we'd trigger a sound or particle effect here
        }
    }
}
