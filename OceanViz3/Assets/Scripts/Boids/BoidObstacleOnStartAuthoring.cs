using System;
using OceanViz3;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace OceanViz3
{
    /// <summary>
    /// Component that creates a boid obstacle entity at runtime from a GameObject.
    /// This component converts a GameObject with a MeshCollider into an ECS entity
    /// that can be used as an obstacle in the boid simulation.
    /// </summary>
    public class BoidObstacleOnStartAuthoring : MonoBehaviour
    {
        public EntityManager entityManager;

        /// <summary>
        /// Initializes the boid obstacle entity when the GameObject starts.
        /// Creates an entity with LocalToWorld and BoidObstacle components,
        /// using the GameObject's transform and MeshCollider for initialization.
        /// </summary>
        public void Start()
        {
            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

            Entity entity = entityManager.CreateEntity();
            entityManager.SetName(entity, "BoidObstacle");
            
            // Add LocalToWorld component to the entity
            entityManager.AddComponentData(entity, new LocalToWorld
            {
                Value = float4x4.TRS(transform.position, transform.rotation, transform.lossyScale)
            });
            
            // Calculate the global dimensions of the collider component
            MeshCollider meshCollider = GetComponent<MeshCollider>();
            if (meshCollider == null)
            {
                Debug.LogError("[BoidObstacleOnStartAuthoring] No mesh collider found!");
                return;
            }
            Bounds bounds = meshCollider.bounds;
            float3 dimensions = bounds.size;
            
            // Add the BoidObstacle component to the entity
            entityManager.AddComponentData(entity, new BoidObstacle
            {
                // Set the dimensions of the obstacle according to the dimensions of the mesh collider
                Dimensions = dimensions
            });
        }
    }
}