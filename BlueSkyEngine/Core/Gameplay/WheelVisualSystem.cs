using System;
using System.Collections.Generic;
using BlueSky.Core.Math;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;

namespace BlueSky.Core.Gameplay
{
    /// <summary>
    /// Handles wheel visuals as a fallback when no skeletal mesh is present.
    /// When a skeletal mesh with wheel bones is attached to the car entity,
    /// the CarController drives bone transforms directly instead.
    /// </summary>
    public class WheelVisualSystem
    {
        private readonly Dictionary<uint, List<Entity>> _entityWheels = new();
        private const float WheelWidth = 0.25f;

        /// <summary>
        /// Create fallback wheel entities for cars without a skeletal mesh.
        /// Cars with skeletal mesh bone-driven wheels do not need these.
        /// </summary>
        public void CreateWheelEntities(Entity carEntity, World world, WheelState[] wheelStates)
        {
            if (world == null || wheelStates == null) return;

            var wheelEntities = new List<Entity>();

            for (int i = 0; i < 4; i++)
            {
                if (i >= wheelStates.Length) break;

                Entity wheelEntity = world.CreateEntity();

                var transform = new TransformComponent
                {
                    Position = new Vector3(
                        wheelStates[i].WorldPosition.X,
                        wheelStates[i].WorldPosition.Y,
                        wheelStates[i].WorldPosition.Z),
                    Rotation = Quaternion.Identity,
                    Scale = new Vector3(WheelWidth, wheelStates[i].Config.WheelRadius, WheelWidth)
                };
                world.AddComponent(wheelEntity, transform);

                wheelEntities.Add(wheelEntity);
            }

            _entityWheels[(uint)carEntity.Id] = wheelEntities;
        }

        /// <summary>
        /// Update fallback wheel entity positions/rotations.
        /// No-op if no fallback entities exist (i.e. skeletal mesh is driving the bones).
        /// </summary>
        public void Update(World world, Entity carEntity, WheelState[] wheelStates)
        {
            if (world == null || wheelStates == null) return;

            uint carId = (uint)carEntity.Id;
            if (!_entityWheels.TryGetValue(carId, out var wheelEntities)) return;

            if (!world.TryGetComponent<TransformComponent>(carEntity, out var carTransform)) return;
            Quaternion carRotation = carTransform.Rotation;

            for (int i = 0; i < wheelEntities.Count && i < wheelStates.Length; i++)
            {
                Entity wheelEntity = wheelEntities[i];
                WheelState wheel = wheelStates[i];

                if (!world.TryGetComponent(wheelEntity, out TransformComponent transform)) continue;

                transform.Position = new Vector3(
                    wheel.WorldPosition.X,
                    wheel.WorldPosition.Y,
                    wheel.WorldPosition.Z);

                Quaternion steerRot = Quaternion.Euler(0, wheel.SteerAngle, 0);
                Quaternion spinRot = new Quaternion(new Vector3(1, 0, 0), wheel.SpinAngle);

                transform.Rotation = (carRotation * steerRot * spinRot).Normalize();

                transform.Scale = new Vector3(
                    WheelWidth,
                    wheel.Config.WheelRadius,
                    WheelWidth);
            }
        }

        public void RemoveWheelEntities(Entity carEntity, World world)
        {
            if (world == null) return;

            uint entityId = (uint)carEntity.Id;
            if (_entityWheels.TryGetValue(entityId, out var wheelEntities))
            {
                foreach (var wheelEntity in wheelEntities)
                {
                    if (world.IsEntityValid(wheelEntity))
                    {
                        world.DestroyEntity(wheelEntity);
                    }
                }

                _entityWheels.Remove(entityId);
            }
        }

        public void Cleanup()
        {
            _entityWheels.Clear();
        }
    }
}