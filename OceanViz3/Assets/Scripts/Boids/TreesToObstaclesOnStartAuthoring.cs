using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using Collider = UnityEngine.Collider;
using Material = Unity.Physics.Material;
using SphereCollider = UnityEngine.SphereCollider;
using TerrainCollider = Unity.Physics.TerrainCollider;

namespace OceanViz3
{
    /// <summary>
    /// Converts 'trees' placed on a Unity Terrain into physical obstacle entities for boids to avoid.
    /// Unity's 'trees' can be any meshes sparsely placed on the terrain, like rocks or other static objects.
    /// This component should be attached to a GameObject with a Terrain component.
    /// </summary>
    public class TreesToObstaclesOnStartAuthoring : MonoBehaviour
    {
        public GameObject boidObstaclePrefab;
        public EntityManager entityManager;

        /// <summary>
        /// On start, finds all trees on the attached Terrain and creates corresponding
        /// obstacle entities using the boidObstaclePrefab. Each obstacle is scaled
        /// according to the tree's collider radius.
        /// </summary>
        public void Start()
        {
            Debug.Log("[TreesToObstaclesOnStartAuthoring] Start");

            // Get the EntityManager from the World
            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

            if (!TryGetComponent<Terrain>(out var terrain))
            {
                Debug.LogError("[TreesToObstaclesOnStartAuthoring] No terrain found!");
                return;
            }

            // For each tree in the terrain
            foreach (var tree in terrain.terrainData.treeInstances)
            {
                var treePosition = Vector3.Scale(tree.position, terrain.terrainData.size) + terrain.transform.position;
                
                // Check if the tree prefab has a SphereCollider component
                if (terrain.terrainData.treePrototypes[tree.prototypeIndex].prefab.TryGetComponent(out SphereCollider treeCollider))
                {
                    // Instantiate a new boid obstacle prefab and scale it according to the tree collider radius
                    var boidObstacle = Instantiate(boidObstaclePrefab, treePosition, Quaternion.identity);
                    boidObstacle.transform.localScale = Vector3.one * treeCollider.radius * 2;
                }
            }
        }
    }
}