using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace OceanViz3
{
    /// <summary>
    /// Blob asset to store mesh vertex positions and color data
    /// </summary>
    public struct MeshHabitatBlobData
    {
        // Vertices in local space
        public BlobArray<float3> Vertices;
        
        // Vertex colors (red channel)
        public BlobArray<float> Colors;
        
        // Triangles (indices)
        public BlobArray<int> Triangles;
        
        // Surface area calculation for density distribution
        public float SurfaceArea;
    }

    /// <summary>
    /// Component to mark a mesh habitat as processed
    /// </summary>
    public struct MeshHabitatProcessedTag : IComponentData { }

    /// <summary>
    /// Component to store reference to the mesh habitat blob data
    /// </summary>
    public struct MeshHabitatBlobRef : IComponentData
    {
        public BlobAssetReference<MeshHabitatBlobData> BlobRef;
        public float4x4 LocalToWorld; // Snapshot of transform at setup time
    }

    /// <summary>
    /// System to process mesh habitat entities and prepare mesh vertex data for spawning static entities
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(StaticEntityDataSetupSystem))]
    public partial struct MeshHabitatSetupSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MeshHabitatComponent>();
        }

        public void OnDestroy(ref SystemState state)
        {
            // Dispose of blob assets when the system is destroyed
            EntityQuery query = SystemAPI.QueryBuilder()
                .WithAll<MeshHabitatBlobRef>()
                .Build();

            // Use .ToComponentDataArray to avoid modifying the query during iteration
            var blobRefs = query.ToComponentDataArray<MeshHabitatBlobRef>(Allocator.Temp);
            foreach (var blobRef in blobRefs)
            {
                if (blobRef.BlobRef.IsCreated)
                {
                    blobRef.BlobRef.Dispose();
                }
            }
            blobRefs.Dispose();
            
            // Also need to clean up any un-processed invisible habitats
            foreach (var (invisible, entity) in SystemAPI.Query<InvisibleMeshHabitatComponent>().WithEntityAccess())
            {
                // This component is managed, so the GC will handle the object itself,
                // but we should remove the component from the entity if it still exists.
                state.EntityManager.RemoveComponent<InvisibleMeshHabitatComponent>(entity);
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            // We can't use Burst here because we need managed components (RenderMeshArray, InvisibleMeshHabitatComponent)
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            var entityManager = state.EntityManager;

            // Process unprocessed mesh habitats (with renderers)
            foreach (var (meshHabitat, localToWorld, renderMeshArray, entity) in 
                SystemAPI.Query<RefRO<MeshHabitatComponent>, RefRO<LocalToWorld>, RenderMeshArray>()
                    .WithNone<MeshHabitatProcessedTag>()
                    .WithEntityAccess())
            {
                if (renderMeshArray.MeshReferences == null || renderMeshArray.MeshReferences.Length == 0 || renderMeshArray.MeshReferences[0].Value == null)
                {
                    Debug.LogError($"Mesh habitat entity {entity} has no valid mesh in its RenderMeshArray.");
                    ecb.AddComponent<MeshHabitatProcessedTag>(entity); // Mark as processed to avoid trying again
                    continue;
                }
                
                // Use the first mesh (LOD0) for habitat data
                var mesh = renderMeshArray.MeshReferences[0].Value; 
                
                if (!mesh.isReadable)
                {
                    Debug.LogError($"Mesh '{mesh.name}' for habitat entity {entity} is not readable. Please enable Read/Write in mesh import settings.");
                    ecb.AddComponent<MeshHabitatProcessedTag>(entity); // Mark as processed
                    continue;
                }

                ProcessMesh(mesh, meshHabitat.ValueRO.HabitatName, entity, localToWorld.ValueRO.Value, ecb);
            }
            
            // Process unprocessed INVISIBLE mesh habitats (without renderers, using our custom component)
            foreach (var (meshHabitat, localToWorld, invisibleHabitat, entity) in 
                     SystemAPI.Query<RefRO<MeshHabitatComponent>, RefRO<LocalToWorld>, InvisibleMeshHabitatComponent>()
                         .WithNone<MeshHabitatProcessedTag>()
                         .WithEntityAccess())
            {
                var mesh = invisibleHabitat.mesh;
                
                if (mesh == null)
                {
                    Debug.LogError($"Invisible mesh habitat entity {entity} has a null mesh in its InvisibleMeshHabitatComponent.");
                    ecb.AddComponent<MeshHabitatProcessedTag>(entity); // Mark as processed to avoid trying again
                    ecb.RemoveComponent<InvisibleMeshHabitatComponent>(entity); // Clean up
                    continue;
                }

                if (!mesh.isReadable)
                {
                    Debug.LogError($"Mesh '{mesh.name}' for invisible habitat entity {entity} is not readable. Please enable Read/Write in mesh import settings.");
                    ecb.AddComponent<MeshHabitatProcessedTag>(entity); // Mark as processed
                    ecb.RemoveComponent<InvisibleMeshHabitatComponent>(entity); // Clean up
                    continue;
                }
                
                ProcessMesh(mesh, meshHabitat.ValueRO.HabitatName, entity, localToWorld.ValueRO.Value, ecb);
                
                // Clean up the managed component now that we're done with it
                ecb.RemoveComponent<InvisibleMeshHabitatComponent>(entity);
            }
        }

        // Extracted mesh processing logic into a separate method to be reused
        private void ProcessMesh(Mesh mesh, FixedString64Bytes habitatName, Entity entity, float4x4 localToWorld, EntityCommandBuffer ecb)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            
            // Get vertex colors (red channel) or default to all 1.0
            var colors = new Color[vertices.Length];
            bool hasColors = false;
            
            if (mesh.colors.Length == vertices.Length)
            {
                colors = mesh.colors;
                hasColors = true;
            }
            else if (mesh.colors32.Length == vertices.Length)
            {
                // Convert Color32 to Color
                var colors32 = mesh.colors32;
                for (int i = 0; i < colors32.Length; i++)
                {
                    colors[i] = colors32[i];
                }
                hasColors = true;
            }
            else
            {
                // Default to white if no colors
                for (int i = 0; i < colors.Length; i++)
                {
                    colors[i] = Color.white;
                }
            }

            // Create blob asset with mesh data
            BlobAssetReference<MeshHabitatBlobData> blobRef;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<MeshHabitatBlobData>();
                
                // Store vertices
                var verticesBuilder = builder.Allocate(ref root.Vertices, vertices.Length);
                for (int i = 0; i < vertices.Length; i++)
                {
                    verticesBuilder[i] = (float3)vertices[i]; // Explicit cast
                }
                
                // Store red channel of colors
                var colorsBuilder = builder.Allocate(ref root.Colors, colors.Length);
                for (int i = 0; i < colors.Length; i++)
                {
                    colorsBuilder[i] = colors[i].r; // Only keep red channel for density
                }
                
                // Store triangles
                var trianglesBuilder = builder.Allocate(ref root.Triangles, triangles.Length);
                for (int i = 0; i < triangles.Length; i++)
                {
                    trianglesBuilder[i] = triangles[i];
                }
                
                // Calculate approximate surface area for density distribution
                float totalArea = 0f;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    if (i + 2 < triangles.Length) // Safety check
                    {
                        var v1 = vertices[triangles[i]];
                        var v2 = vertices[triangles[i + 1]];
                        var v3 = vertices[triangles[i + 2]];
                        
                        // Calculate triangle area using cross product
                        float3 edge1 = (float3)v2 - (float3)v1;
                        float3 edge2 = (float3)v3 - (float3)v1;
                        float3 cross = math.cross(edge1, edge2);
                        float area = math.length(cross) * 0.5f;
                        totalArea += area;
                    }
                }
                root.SurfaceArea = totalArea;
                
                blobRef = builder.CreateBlobAssetReference<MeshHabitatBlobData>(Allocator.Persistent);
            }

            // Add components to the entity
            ecb.AddComponent(entity, new MeshHabitatBlobRef
            {
                BlobRef = blobRef,
                LocalToWorld = localToWorld
            });
            
            ecb.AddComponent<MeshHabitatProcessedTag>(entity);
            
            Debug.Log($"Processed mesh habitat '{habitatName}' (Entity: {entity}) with {vertices.Length} vertices, " +
                        $"surface area: {blobRef.Value.SurfaceArea}, " +
                        $"has colors: {hasColors}");
        }
    }
} 