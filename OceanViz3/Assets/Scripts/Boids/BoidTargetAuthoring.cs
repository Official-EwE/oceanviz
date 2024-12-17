using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Serialization;

namespace OceanViz3
{
    public class BoidTargetAuthoring : MonoBehaviour
    {
        class Baker : Baker<BoidTargetAuthoring>
        {
            public override void Bake(BoidTargetAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Renderable);
                AddComponent(entity, new BoidTarget{});
            }
        }
    }

    /// <summary>
    /// Component data for a boid target entity. Contains information about the target's
    /// position, movement, and associated boid school. Boid targets are points in space that groups of boids will move towards.
    /// </summary>
    public struct BoidTarget : IComponentData
    {
        public int BoidSchoolId;
        public int DynamicEntityId;
        public Entity Value; // Used in the closest target calculation
        public float3 StartPosition;
        public float3 EndPosition;
        public float LerpDuration;
        public float LerpTimer;
    }
}
