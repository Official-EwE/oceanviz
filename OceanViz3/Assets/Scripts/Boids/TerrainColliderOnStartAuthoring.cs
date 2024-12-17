using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using Collider = UnityEngine.Collider;
using Material = Unity.Physics.Material;
using TerrainCollider = Unity.Physics.TerrainCollider;

/// <summary>
/// Creates and manages a terrain collider entity in the ECS system.
/// This component should be attached to a GameObject with a Terrain component.
/// </summary>
public class TerrainColliderOnStartAuthoring : MonoBehaviour
{
    // The entity that will hold the terrain collider
    public Entity entity;
    
    public EntityManager entityManager;

    /// <summary>
    /// Initializes the terrain collider on start.
    /// Creates a new entity with a terrain collider if one doesn't exist,
    /// or updates the existing terrain collider entity.
    /// </summary>
    public void Start()
    {
        Debug.Log("TerrainColliderAuthoring Start");
        
        // Get the EntityManager from the World
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        if (!TryGetComponent<Terrain>(out var terrain))
        {
            Debug.LogError("No terrain found!");
            return;
        }

        CollisionFilter collisionFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = ~0u, // All 1s, so all layers - collide with everything
            GroupIndex = 0
        };

        PhysicsCollider collider = CreateTerrainCollider(terrain.terrainData, collisionFilter);
        
        World world = World.DefaultGameObjectInjectionWorld;
        entityManager = world.EntityManager;
        
        // Target entity
        Entity entity;
        
        // Check if the TerrainCollider component already exists
        EntityQuery query = entityManager.CreateEntityQuery(typeof(TerrainCollider));
        
        // If a singleton entity with the TerrainCollider component already exists, we use that entity
        if (query.CalculateEntityCount() > 0)
        {
            Debug.Log("TerrainCollider entity already exists, updating the collider.");

            entity = entityManager.CreateEntityQuery(typeof(TerrainCollider)).GetSingletonEntity();
            
            entityManager.SetComponentData(entity, new LocalToWorld
            {
                Value = float4x4.TRS(terrain.transform.position, terrain.transform.rotation, terrain.transform.lossyScale)
            });
            entityManager.SetComponentData(entity, collider);
        }
        // If an entity with the TerrainCollider component does not exist, we set the terrain collider on new entity
        else
        {
            Debug.Log("TerrainCollider entity does not exist yet, creating a new entity.");
            
            entity = entityManager.CreateEntity();
            entityManager.SetName(entity, "TerrainCollider");
            entityManager.AddComponent<PhysicsCollider>(entity);
            entityManager.SetComponentData(entity, collider);
            entityManager.AddComponent<PhysicsWorldIndex>(entity);
            
            entityManager.AddComponent<LocalToWorld>(entity);
            entityManager.SetComponentData(entity, new LocalToWorld
            {
                Value = float4x4.TRS(terrain.transform.position, terrain.transform.rotation, terrain.transform.lossyScale)
            });
            
            entityManager.AddComponent<TerrainCollider>(entity); // Tag component
        }
    }

    /// <summary>
    /// Creates a PhysicsCollider component for the terrain using the provided TerrainData.
    /// </summary>
    /// <param name="terrainData">The TerrainData asset containing height information</param>
    /// <param name="filter">Collision filter determining collision layers and masks</param>
    /// <returns>A PhysicsCollider component configured for the terrain</returns>
    private PhysicsCollider CreateTerrainCollider(TerrainData terrainData, CollisionFilter filter)
    {
        int resolution = terrainData.heightmapResolution;
        int2 size = new int2(resolution, resolution);
        Vector3 scale = terrainData.heightmapScale;

        NativeArray<float> colliderHeights = new NativeArray<float>(resolution * resolution, Allocator.TempJob);
        float[,] terrainHeights = terrainData.GetHeights(0, 0, resolution, resolution);

        for (int j = 0; j < size.y; j++)
        for (int i = 0; i < size.x; i++)
        {
            var h = terrainHeights[i, j];
            colliderHeights[j + i * size.x] = h;
        }

        PhysicsCollider physicsCollider = new PhysicsCollider
        {
            Value = Unity.Physics.TerrainCollider.Create(colliderHeights, size, scale, Unity.Physics.TerrainCollider.CollisionMethod.Triangles, filter)
        };

        colliderHeights.Dispose();

        return physicsCollider;
    }
    
    /// <summary>
    /// Tag component to identify entities with terrain colliders.
    /// Used for querying and identifying terrain collider entities in the ECS world.
    /// </summary>
    public struct TerrainCollider : IComponentData
    {
    }
}