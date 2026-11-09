using System;
using BlueSky.Ramanujan;
using System.Collections.Generic;
using BlueSky.SkyCore.Builtin;

namespace BlueSky.SkyCore
{
    /// <summary>
    /// Propagates parent transforms down to child entities that have
    /// <see cref="ParentComponent"/>, <see cref="LocalTransformComponent"/>, and <see cref="TransformComponent"/>.
    /// Supports multi-level hierarchy trees without cycle hangs.
    /// </summary>
    public sealed class HierarchySystem : SystemBase
    {
        private QueryDescription _hierarchyQuery;
        private readonly HashSet<int> _visitedChain = new();

        protected override void OnInitialize()
        {
            if (World != null)
            {
                _hierarchyQuery = World.CreateQuery()
                    .All<ParentComponent>()
                    .All<LocalTransformComponent>()
                    .All<TransformComponent>()
                    .Build();
            }
        }

        public override void Update(float dt)
        {
            if (World == null)
                return;

            foreach (var chunk in World.EnumerateQueryChunks(_hierarchyQuery))
            {
                int parentIdx = chunk.GetComponentIndex(typeof(ParentComponent));
                int localIdx = chunk.GetComponentIndex(typeof(LocalTransformComponent));
                int worldIdx = chunk.GetComponentIndex(typeof(TransformComponent));

                var entities = chunk.GetEntities();
                var parents = chunk.GetComponentSpan<ParentComponent>(parentIdx);
                var locals = chunk.GetComponentSpan<LocalTransformComponent>(localIdx);
                var worlds = chunk.GetComponentSpan<TransformComponent>(worldIdx);

                for (int i = 0; i < entities.Length; i++)
                {
                    ResolveWorldTransform(entities[i], parents[i].Parent, in locals[i], ref worlds[i]);
                }

                chunk.MarkChanged(worldIdx, World.Version);
            }

            LastSystemVersion = World.Version;
        }

        private void ResolveWorldTransform(
            Entity entity,
            Entity parent,
            in LocalTransformComponent local,
            ref TransformComponent outputWorld)
        {
            if (World == null || !World.IsEntityValid(parent) || parent == entity)
            {
                outputWorld.Position = local.Position;
                outputWorld.Rotation = local.Rotation;
                outputWorld.Scale = local.Scale;
                return;
            }

            // Walk ancestor chain up to root (guarded against cycles up to depth 32)
            _visitedChain.Clear();
            _visitedChain.Add(entity.Id);

            Vector3 accPos = local.Position;
            Quaternion accRot = local.Rotation;
            Vector3 accScale = local.Scale;

            Entity current = parent;
            int depth = 0;
            while (World.IsEntityValid(current) && depth < 32 && _visitedChain.Add(current.Id))
            {
                if (World.TryGetComponent<ParentComponent>(current, out var grandParent) &&
                    World.TryGetComponent<LocalTransformComponent>(current, out var parentLocal) &&
                    World.IsEntityValid(grandParent.Parent))
                {
                    var scaledLocalPos = new Vector3(
                        accPos.X * parentLocal.Scale.X,
                        accPos.Y * parentLocal.Scale.Y,
                        accPos.Z * parentLocal.Scale.Z);
                    accPos = parentLocal.Position + (parentLocal.Rotation * scaledLocalPos);
                    accRot = (parentLocal.Rotation * accRot).Normalize();
                    accScale = new Vector3(
                        accScale.X * parentLocal.Scale.X,
                        accScale.Y * parentLocal.Scale.Y,
                        accScale.Z * parentLocal.Scale.Z);
                    current = grandParent.Parent;
                    depth++;
                }
                else if (World.TryGetComponent<TransformComponent>(current, out var rootTransform))
                {
                    var scaledLocalPos = new Vector3(
                        accPos.X * rootTransform.Scale.X,
                        accPos.Y * rootTransform.Scale.Y,
                        accPos.Z * rootTransform.Scale.Z);
                    accPos = rootTransform.Position + (rootTransform.Rotation * scaledLocalPos);
                    accRot = (rootTransform.Rotation * accRot).Normalize();
                    accScale = new Vector3(
                        accScale.X * rootTransform.Scale.X,
                        accScale.Y * rootTransform.Scale.Y,
                        accScale.Z * rootTransform.Scale.Z);
                    break;
                }
                else
                {
                    break;
                }
            }

            outputWorld.Position = accPos;
            outputWorld.Rotation = accRot;
            outputWorld.Scale = accScale;
        }
    }
}
