using System;
using OceanViz3;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace OceanViz3
{
    /// <summary>
    /// Baker class that converts BoidObstacleAuthoring MonoBehaviour into ECS components
    /// during the baking process.
    /// </summary>
    public class BoidObstacleAuthoringBaker : Baker<BoidObstacleAuthoring>
    {
        /// <summary>
        /// Bakes the BoidObstacleAuthoring MonoBehaviour into ECS components.
        /// Creates an entity with a BoidObstacle component.
        /// </summary>
        public override void Bake(BoidObstacleAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Renderable);
            AddComponent(entity, new BoidObstacle());
        }
    }

    /// <summary>
    /// Component that marks an entity as an obstacle that boids should avoid.
    /// Contains the dimensions of the obstacle for collision detection.
    /// </summary>
    public struct BoidObstacle : IComponentData
    {
        /// <summary>
        /// The width, height, and depth of the obstacle in world space
        /// </summary>
        public float3 Dimensions;
    }

    /// <summary>
    /// MonoBehaviour that marks a GameObject as a boid obstacle.
    /// This component is converted to ECS components during the baking process.
    /// </summary>
    public class BoidObstacleAuthoring : MonoBehaviour
    {
    }
}
