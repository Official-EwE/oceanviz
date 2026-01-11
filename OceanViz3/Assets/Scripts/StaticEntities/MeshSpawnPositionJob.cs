using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Transforms;
using UnityEngine;

namespace OceanViz3
{
    /// <summary>
    /// Job that calculates spawn positions for static entities on mesh habitats
    /// </summary>
    [BurstCompile]
    public struct MeshSpawnPositionJob : IJob
    {
        [ReadOnly] public NativeArray<Entity> MeshHabitatEntities;
        [ReadOnly] public ComponentLookup<MeshHabitatBlobRef> MeshHabitatBlobRefs;
        [ReadOnly] public float3 GroupNoiseOffset;
        [ReadOnly] public float NoiseScale;
        [ReadOnly] public uint Seed;
        [ReadOnly] public int SpawnCount;
        
        // Output
        public NativeArray<MeshSpawnPoint> MeshSpawnPoints;
        
        public void Execute()
        {
            if (MeshHabitatEntities.Length == 0)
            {
                // No mesh habitats found, nothing to do
                return;
            }
            
            var random = Unity.Mathematics.Random.CreateFromIndex(Seed);
            
            // If no mesh habitats, return early
            if (MeshHabitatEntities.Length == 0)
            {
                return;
            }
            
            // Calculate total surface area of all mesh habitats
            float totalArea = 0f;
            for (int i = 0; i < MeshHabitatEntities.Length; i++)
            {
                var meshHabitatEntity = MeshHabitatEntities[i];
                if (MeshHabitatBlobRefs.HasComponent(meshHabitatEntity))
                {
                    var blobRef = MeshHabitatBlobRefs[meshHabitatEntity].BlobRef;
                    if (blobRef.IsCreated)
                    {
                        totalArea += blobRef.Value.SurfaceArea;
                    }
                }
            }
            
            if (totalArea <= 0f)
            {
                // No valid surface area, nothing to do
                return;
            }
            
            // Distribute spawn points among mesh habitats proportionally to their surface area
            int spawnedCount = 0;
            for (int meshIndex = 0; meshIndex < MeshHabitatEntities.Length && spawnedCount < SpawnCount; meshIndex++)
            {
                var meshHabitatEntity = MeshHabitatEntities[meshIndex];
                if (!MeshHabitatBlobRefs.HasComponent(meshHabitatEntity))
                {
                    continue;
                }
                
                var meshBlobRef = MeshHabitatBlobRefs[meshHabitatEntity].BlobRef;
                var meshTransform = MeshHabitatBlobRefs[meshHabitatEntity].LocalToWorld;
                
                if (!meshBlobRef.IsCreated)
                {
                    continue;
                }
                
                // Calculate how many entities to spawn on this mesh based on its surface area proportion
                float areaRatio = meshBlobRef.Value.SurfaceArea / totalArea;
                int meshSpawnCount = math.min(SpawnCount - spawnedCount, (int)(SpawnCount * areaRatio));
                
                // Ensure at least one entity on each mesh if possible
                if (meshSpawnCount == 0 && spawnedCount < SpawnCount)
                {
                    meshSpawnCount = 1;
                }
                
                // Create spawn points on this mesh
                for (int i = 0; i < meshSpawnCount && spawnedCount < SpawnCount; i++)
                {
                    // Select a random triangle weighted by area
                    int triangleIndex = SelectRandomTriangle(ref random, ref meshBlobRef.Value);
                    if (triangleIndex < 0)
                    {
                        continue;
                    }
                    
                    // Get triangle vertices
                    int idx1 = meshBlobRef.Value.Triangles[triangleIndex * 3];
                    int idx2 = meshBlobRef.Value.Triangles[triangleIndex * 3 + 1];
                    int idx3 = meshBlobRef.Value.Triangles[triangleIndex * 3 + 2];
                    
                    var v1 = meshBlobRef.Value.Vertices[idx1];
                    var v2 = meshBlobRef.Value.Vertices[idx2];
                    var v3 = meshBlobRef.Value.Vertices[idx3];
                    
                    // Get vertex colors (density weights from red channel)
                    float c1 = meshBlobRef.Value.Colors[idx1];
                    float c2 = meshBlobRef.Value.Colors[idx2];
                    float c3 = meshBlobRef.Value.Colors[idx3];
                    
                    // Random barycentric coordinates for position within triangle
                    float u = random.NextFloat();
                    float v = random.NextFloat();
                    if (u + v > 1f)
                    {
                        u = 1f - u;
                        v = 1f - v;
                    }
                    float w = 1f - u - v;
                    
                    // Calculate position in local space
                    float3 localPos = v1 * u + v2 * v + v3 * w;
                    
                    // Calculate interpolated color weight
                    float colorWeight = c1 * u + c2 * v + c3 * w;
                    
                    // Noise-based density factor (similar to terrain spawning)
                    float3 noisePos = new float3(localPos.x + GroupNoiseOffset.x, localPos.y + GroupNoiseOffset.y, localPos.z + GroupNoiseOffset.z);
                    float noiseValue = noise.snoise(noisePos / NoiseScale);
                    noiseValue = (noiseValue * 0.5f) + 0.5f; // Map from [-1, 1] to [0, 1]
                    
                    // Combine color weight with noise for final density
                    float density = colorWeight * noiseValue;
                    
                    // Acceptance check
                    if (random.NextFloat() <= density)
                    {
                        // Transform local position to world space
                        float4 worldPos4 = math.mul(meshTransform, new float4(localPos, 1f));
                        float3 worldPos = new float3(worldPos4.x, worldPos4.y, worldPos4.z);
                        
                        // Calculate normal for rotation
                        float3 edge1 = v2 - v1;
                        float3 edge2 = v3 - v1;
                        float3 normal = math.normalize(math.cross(edge1, edge2));
                        
                        // Transform normal to world space (ignoring translation)
                        float4 worldNormal4 = math.mul(meshTransform, new float4(normal, 0f));
                        float3 worldNormal = math.normalize(new float3(worldNormal4.x, worldNormal4.y, worldNormal4.z));
                        
                        // Store the spawn point
                        MeshSpawnPoints[spawnedCount] = new MeshSpawnPoint
                        {
                            Position = worldPos,
                            Normal = worldNormal,
                            MeshEntity = meshHabitatEntity
                        };
                        
                        spawnedCount++;
                    }
                }
            }
            
            // Mark any unused elements with a null entity
            for (int i = spawnedCount; i < MeshSpawnPoints.Length; i++)
            {
                MeshSpawnPoints[i] = new MeshSpawnPoint
                {
                    Position = float3.zero,
                    Normal = new float3(0, 1, 0),
                    MeshEntity = Entity.Null
                };
            }
        }
        
        // Select a random triangle from the mesh, weighted by triangle area
        private int SelectRandomTriangle(ref Unity.Mathematics.Random random, ref MeshHabitatBlobData blobData)
        {
            int triangleCount = blobData.Triangles.Length / 3;
            if (triangleCount <= 0)
            {
                return -1;
            }
            
            // For simplicity, use uniform distribution
            // In a more advanced version, this could weight by triangle area
            return random.NextInt(0, triangleCount);
        }
    }
    
    /// <summary>
    /// Represents a spawn point on a mesh surface
    /// </summary>
    public struct MeshSpawnPoint
    {
        public float3 Position;
        public float3 Normal;
        public Entity MeshEntity; // Reference to the mesh habitat entity this point belongs to
    }
} 