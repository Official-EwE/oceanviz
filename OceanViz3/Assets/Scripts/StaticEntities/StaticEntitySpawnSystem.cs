using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Unity.Rendering;
using Unity.Jobs;
using UnityEngine;
using Random = Unity.Mathematics.Random;
using SystemMath = System.Math;

namespace OceanViz3
{
    /// <summary>
    /// System responsible for managing static entity groups and their member entities.
    /// Static entity groups are entities that manage the spawning and destruction of static entities.
    /// Supports spawning on both terrain and mesh habitats.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(StaticEntityDataSetupSystem))]
    [RequireMatchingQueriesForUpdate]
    public partial struct StaticEntitySpawnSystem : ISystem
    {
        /// <summary>
        /// Maximum number of entities to create or destroy per update
        /// </summary>
        private const int MAX_ENTITIES_PER_UPDATE = 10000;
        
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<StaticEntitiesGroupComponent>();
            Debug.Log("[StaticEntitySpawnSystem] OnCreate called.");
        }
        
        public void OnUpdate(ref SystemState state)
        {
            var localToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>();
            var meshHabitatBlobRefLookup = SystemAPI.GetComponentLookup<MeshHabitatBlobRef>(true);
            var world = state.World.Unmanaged;
            var entityManager = state.EntityManager;
            float deltaTime = SystemAPI.Time.DeltaTime;
            var meshHabitatBufferLookup = SystemAPI.GetBufferLookup<MeshHabitatEntityRef>(true);

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var entityCommandBuffer = ecbSingleton.CreateCommandBuffer(world);

            JobHandle combinedDependencies = state.Dependency;

            foreach (var (staticEntitiesGroup, staticEntitiesGroupLocalToWorld, staticEntitiesGroupEntity) in
                     SystemAPI.Query<RefRW<StaticEntitiesGroupComponent>, RefRO<LocalToWorld>>()
                         .WithEntityAccess())
            {
                int groupId = staticEntitiesGroup.ValueRO.StaticEntitiesGroupId;
                // Debug.Log($"[StaticEntitySpawnSystem] Processing Group ID: {groupId}");

                if (staticEntitiesGroup.ValueRO.DestroyRequested == true)
                {
                    // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Destroy requested.");

                    // Query for all static entity groups to check if this is the last one.
                    // This is not a perfect solution. If multiple groups are destroyed in the same frame,
                    // the heightmap blob might be leaked. However, it prevents a crash when one of
                    // multiple groups is destroyed, which is the immediate bug. A more robust solution
                    // would involve a centralized manager for shared blob assets.
                    EntityQuery allGroupsQuery = SystemAPI.QueryBuilder().WithAll<StaticEntitiesGroupComponent>().Build();
                    int totalGroupCount = allGroupsQuery.CalculateEntityCount();

                    // Dispose BlobAssets associated with this group
                    // Only dispose the shared heightmap blob if this is the last group
                    if (staticEntitiesGroup.ValueRO.HeightmapDataBlobRef.IsCreated && totalGroupCount <= 1)
                    {
                        staticEntitiesGroup.ValueRW.HeightmapDataBlobRef.Dispose();
                    }

                    // The splatmap blob is not shared, so it's safe to dispose.
                    if (staticEntitiesGroup.ValueRO.SplatmapDataBlobRef.IsCreated)
                    {
                        staticEntitiesGroup.ValueRW.SplatmapDataBlobRef.Dispose();
                    }
                    
                    // Destroy static entities
                    EntityQuery staticEntityQuery = SystemAPI.QueryBuilder()
                        .WithAll<StaticEntityShared>()
                        .WithAll<LocalToWorld>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build();
                    
                    // Filter by shared component manually
                    staticEntityQuery.SetSharedComponentFilter(new StaticEntityShared { 
                        StaticEntitiesGroupId = groupId 
                    });
                    
                    entityCommandBuffer.DestroyEntity(staticEntityQuery, EntityQueryCaptureMode.AtPlayback);
                    
                    // Destroy StaticEntitiesGroupComponent entity
                    entityCommandBuffer.DestroyEntity(staticEntitiesGroupEntity);
                    
                    // Done with this StaticEntitiesGroupComponent
                    continue;
                }
                
                var staticEntitiesGroupCopy = staticEntitiesGroup.ValueRO;

                // Entity spawning/destroying
                // Check if there is a difference between current Count and RequestedCount
                int currentCount = staticEntitiesGroup.ValueRO.Count;
                int requestedCount = staticEntitiesGroup.ValueRO.RequestedCount;
                // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Current Count = {currentCount}, Requested Count = {requestedCount}");

                if (currentCount != requestedCount)
                {
                    if (!staticEntitiesGroup.ValueRO.SpawnDataIsReady)
                    {
                        Debug.LogWarning($"[StaticEntitySpawnSystem] Group {groupId}: Spawn data not ready, skipping spawn/destroy.");
                        continue;
                    }
                    // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Spawn data is ready.");
                
                    // If RequestedCount > Count, instantiate more entities
                    if (requestedCount > currentCount)
                    {
                        var amountToInstantiate = requestedCount - currentCount;
                        // Limit the number of entities to create per update
                        amountToInstantiate = SystemMath.Min(amountToInstantiate, MAX_ENTITIES_PER_UPDATE);

                        // Check if the prototype is valid and has required components
                        if (staticEntitiesGroup.ValueRO.StaticEntityPrototype == Entity.Null || 
                            !entityManager.HasComponent<RenderMeshArray>(staticEntitiesGroup.ValueRO.StaticEntityPrototype))
                        {
                            Debug.LogError($"[StaticEntitySpawnSystem] Group {groupId}: Prototype entity is Null or missing RenderMeshArray. Cannot instantiate.");
                            continue;
                        }
                        
                        // Create a native array to hold the new static entities
                        var staticEntityEntities =
                            CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(amountToInstantiate,
                                ref world.UpdateAllocator);

                        try
                        {
                            // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Instantiating {amountToInstantiate} entities...");
                            entityManager.Instantiate(staticEntitiesGroup.ValueRO.StaticEntityPrototype, staticEntityEntities);
                            // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Instantiation successful.");
                        } 
                        catch (Exception ex)
                        {
                            Debug.LogError($"[StaticEntitySpawnSystem] Group {groupId}: Failed to instantiate static entities: {ex.Message}");
                            // Clean up allocated array if instantiation fails
                            if (staticEntityEntities.IsCreated) staticEntityEntities.Dispose();
                            continue;
                        }
                        
                        // Set up each new static entity with shared components and shader properties
                        // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Setting up components for {amountToInstantiate} new entities...");
                        for (int i = 0; i < staticEntityEntities.Length; i++)
                        {
                            // Set the shared component for group identification
                            StaticEntityShared staticEntityShared = new StaticEntityShared
                            {
                                StaticEntitiesGroupId = groupId
                            };
                            entityCommandBuffer.SetSharedComponentManaged(staticEntityEntities[i], staticEntityShared);
                            
                            // Set up rendering components
                            if (entityManager.HasComponent<RenderMeshArray>(staticEntitiesGroup.ValueRO.StaticEntityPrototype))
                            {
                                // Copy RenderMeshArray from prototype
                                var renderMeshArray = entityManager.GetSharedComponentManaged<RenderMeshArray>(staticEntitiesGroup.ValueRO.StaticEntityPrototype);
                                entityCommandBuffer.SetSharedComponentManaged(staticEntityEntities[i], renderMeshArray);
                                
                                // Initialize with LOD0
                                var materialMeshInfo = MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0);
                                entityCommandBuffer.SetComponent(staticEntityEntities[i], materialMeshInfo);
                            }
                            
                            // Set up shader property overrides
                            if (entityManager.HasComponent<ScreenDisplayStartOverride>(staticEntityEntities[i]))
                            {
                                entityCommandBuffer.SetComponent(staticEntityEntities[i], new ScreenDisplayStartOverride { Value = new float4(0, 0, 0, 0) });
                            }
                            
                            if (entityManager.HasComponent<ScreenDisplayEndOverride>(staticEntityEntities[i]))
                            {
                                entityCommandBuffer.SetComponent(staticEntityEntities[i], new ScreenDisplayEndOverride { Value = new float4(1, 0, 0, 0) });
                            }

                            // Set up turbulence strength based on rigidity (1 - rigidity)
                            if (entityManager.HasComponent<TurbulenceStrengthOverride>(staticEntityEntities[i]))
                            {
                                float turbulenceStrength = 1.0f - staticEntitiesGroup.ValueRO.Rigidity;
                                entityCommandBuffer.SetComponent(staticEntityEntities[i], new TurbulenceStrengthOverride { Value = turbulenceStrength });
                            }

                            // Set up waves motion strength
                            if (entityManager.HasComponent<WavesMotionStrengthOverride>(staticEntityEntities[i]))
                            {
                                entityCommandBuffer.SetComponent(staticEntityEntities[i], new WavesMotionStrengthOverride { Value = staticEntitiesGroup.ValueRO.WavesMotionStrength });
                            }

                            // Add culling component
                            entityCommandBuffer.AddComponent(staticEntityEntities[i], new CullingComponent { MaxDistance = 70.0f });
                        }
                        // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Component setup complete.");

                        // Decide how many entities to spawn on terrain vs mesh habitats
                        int terrainSpawnCount = 0;
                        int meshSpawnCount = 0;
                        
                        bool useMeshHabitats = staticEntitiesGroup.ValueRO.UseMeshHabitats;
                        bool canSpawnOnTerrain = staticEntitiesGroup.ValueRO.UseSplatmap;
                        
                        // Get mesh habitat entities if available
                        NativeList<Entity> meshHabitatEntities = new NativeList<Entity>(16, Allocator.TempJob);
                        bool canSpawnOnMesh = false;
                        
                        if (useMeshHabitats && meshHabitatBufferLookup.HasBuffer(staticEntitiesGroupEntity))
                        {
                            // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Checking for mesh habitats in buffer...");
                            var meshHabitatBuffer = meshHabitatBufferLookup[staticEntitiesGroupEntity];
                            
                            // Collect valid mesh habitat entities from the buffer
                            for (int i = 0; i < meshHabitatBuffer.Length; i++)
                            {
                                Entity meshEntity = meshHabitatBuffer[i].MeshEntity;
                                if (entityManager.Exists(meshEntity) && 
                                    entityManager.HasComponent<MeshHabitatBlobRef>(meshEntity) &&
                                    entityManager.GetComponentData<MeshHabitatBlobRef>(meshEntity).BlobRef.IsCreated)
                                {
                                    meshHabitatEntities.Add(meshEntity);
                                }
                                else
                                {
                                    Debug.LogWarning($"[StaticEntitySpawnSystem] Group {groupId}: Found invalid mesh habitat entity ({meshEntity}) in buffer.");
                                }
                            }
                            
                            if (meshHabitatEntities.Length > 0)
                            {
                                canSpawnOnMesh = true;
                            }
                        }
                        
                        // Distribute entities based on available habitat types
                        if (canSpawnOnTerrain && canSpawnOnMesh)
                        {
                            float meshRatio = staticEntitiesGroup.ValueRO.MeshHabitatRatio;
                            meshSpawnCount = Mathf.RoundToInt(amountToInstantiate * meshRatio);
                            terrainSpawnCount = amountToInstantiate - meshSpawnCount;
                            Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Distributing {amountToInstantiate} entities: Terrain={terrainSpawnCount}, Mesh={meshSpawnCount} (Ratio={meshRatio:F2})");
                        }
                        else if (canSpawnOnTerrain)
                        {
                            terrainSpawnCount = amountToInstantiate;
                            Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Spawning {amountToInstantiate} entities on Terrain only.");
                        }
                        else if (canSpawnOnMesh)
                        {
                            meshSpawnCount = amountToInstantiate;
                            Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Spawning {amountToInstantiate} entities on Mesh only.");
                        }
                        else
                        {
                            Debug.LogWarning($"[StaticEntitySpawnSystem] Group {groupId}: No valid terrain or mesh habitats found. Spawning 0 entities.");
                        }
                        
                        // Create arrays for spawn positions
                        NativeArray<int> terrainSpawnIndices = new NativeArray<int>(terrainSpawnCount, Allocator.TempJob);
                        NativeArray<MeshSpawnPoint> meshSpawnPoints = new NativeArray<MeshSpawnPoint>(meshSpawnCount, Allocator.TempJob);
                        
                        // --- Schedule Terrain Pre-calculation Job --- 
                        JobHandle terrainJob = default;
                        if (terrainSpawnCount > 0)
                        {
                            // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Scheduling terrain index calculation job for {terrainSpawnCount} entities.");
                            var calculateTerrainIndicesJob = new CalculateSpawnIndicesJob
                            {
                                SplatmapDataBlobRef = staticEntitiesGroup.ValueRO.SplatmapDataBlobRef,
                                SplatmapWidth = staticEntitiesGroup.ValueRO.SplatmapWidth,
                                SplatmapHeight = staticEntitiesGroup.ValueRO.SplatmapHeight,
                                GroupNoiseOffset = staticEntitiesGroup.ValueRO.GroupNoiseOffset,
                                NoiseScale = staticEntitiesGroup.ValueRO.NoiseScale,
                                TerrainOffsetX = staticEntitiesGroup.ValueRO.TerrainOffsetX,
                                TerrainOffsetZ = staticEntitiesGroup.ValueRO.TerrainOffsetZ,
                                TerrainSize = staticEntitiesGroup.ValueRO.TerrainSize,
                                AmountToInstantiate = terrainSpawnCount,
                                Seed = (uint)(state.WorldUnmanaged.Time.ElapsedTime * 1000 + groupId + 1),
                                SpawnPositionIndices = terrainSpawnIndices,
                                UseSplatmap = staticEntitiesGroup.ValueRO.UseSplatmap
                            };
                            
                            terrainJob = calculateTerrainIndicesJob.Schedule(combinedDependencies);
                        }
                        
                        // --- Schedule Mesh Pre-calculation Job ---
                        JobHandle meshJob = default;
                        if (meshSpawnCount > 0)
                        {
                            // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Scheduling mesh position calculation job for {meshSpawnCount} entities.");
                            var meshHabitatEntitiesArray = meshHabitatEntities.AsArray();
                            var calculateMeshSpawnPointsJob = new MeshSpawnPositionJob
                            {
                                MeshHabitatEntities = meshHabitatEntitiesArray,
                                MeshHabitatBlobRefs = meshHabitatBlobRefLookup,
                                GroupNoiseOffset = staticEntitiesGroup.ValueRO.GroupNoiseOffset,
                                NoiseScale = staticEntitiesGroup.ValueRO.NoiseScale,
                                Seed = (uint)(state.WorldUnmanaged.Time.ElapsedTime * 1000 + groupId + 2),
                                SpawnCount = meshSpawnCount,
                                MeshSpawnPoints = meshSpawnPoints
                            };
                            
                            meshJob = calculateMeshSpawnPointsJob.Schedule(combinedDependencies);
                        }
                        
                        // Combine pre-calculation jobs
                        JobHandle precalcJobHandle = JobHandle.CombineDependencies(terrainJob, meshJob);
                        
                        // --- Schedule Terrain Placement Job ---
                        JobHandle terrainPlacementJob = default;
                        if (terrainSpawnCount > 0)
                        {
                            // Additional safety check for heightmap data before scheduling the job
                            if (!staticEntitiesGroup.ValueRO.HeightmapDataBlobRef.IsCreated)
                            {
                                Debug.LogWarning($"[StaticEntitySpawnSystem] Group {groupId}: HeightmapDataBlobRef is not created, skipping terrain spawning for {terrainSpawnCount} entities.");
                                // Still need to update the count properly
                                staticEntitiesGroupCopy.Count += terrainSpawnCount;
                            }
                            else
                            {
                                // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Scheduling terrain placement job.");
                                var terrainEntities = staticEntityEntities.GetSubArray(0, terrainSpawnCount);
                                var setTerrainEntityLocalToWorldJob = new SetStaticEntityLocalToWorld 
                                {
                                    LocalToWorldFromEntity = localToWorldLookup,
                                    Entities = terrainEntities,
                                    SpawnPositionIndices = terrainSpawnIndices,
                                    MinScale = staticEntitiesGroup.ValueRO.MinScale <= 0 ? 0.8f : staticEntitiesGroup.ValueRO.MinScale,
                                    MaxScale = staticEntitiesGroup.ValueRO.MaxScale <= 0 ? 1.2f : staticEntitiesGroup.ValueRO.MaxScale,
                                    TerrainSize = staticEntitiesGroup.ValueRO.TerrainSize,
                                    TerrainHeight = staticEntitiesGroup.ValueRO.TerrainHeight,
                                    TerrainOffsetX = staticEntitiesGroup.ValueRO.TerrainOffsetX,
                                    TerrainOffsetY = staticEntitiesGroup.ValueRO.TerrainOffsetY,
                                    TerrainOffsetZ = staticEntitiesGroup.ValueRO.TerrainOffsetZ,
                                    HeightmapDataBlobRef = staticEntitiesGroup.ValueRO.HeightmapDataBlobRef,
                                    HeightmapWidth = staticEntitiesGroup.ValueRO.HeightmapWidth,
                                    HeightmapHeight = staticEntitiesGroup.ValueRO.HeightmapHeight,
                                    UseSplatmap = staticEntitiesGroup.ValueRO.UseSplatmap,
                                    SplatmapWidth = staticEntitiesGroup.ValueRO.SplatmapWidth,
                                    SplatmapHeight = staticEntitiesGroup.ValueRO.SplatmapHeight
                                };
                                
                                terrainPlacementJob = setTerrainEntityLocalToWorldJob.Schedule(terrainSpawnCount, 64, precalcJobHandle);
                            }
                        }
                        
                        // --- Schedule Mesh Placement Job ---
                        JobHandle meshPlacementJob = default;
                        if (meshSpawnCount > 0)
                        {
                            // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Scheduling mesh placement job.");
                            var meshEntities = staticEntityEntities.GetSubArray(terrainSpawnCount, meshSpawnCount);
                            var setMeshEntityLocalToWorldJob = new SetMeshEntityLocalToWorld
                            {
                                LocalToWorldFromEntity = localToWorldLookup,
                                Entities = meshEntities,
                                SpawnPoints = meshSpawnPoints,
                                MinScale = staticEntitiesGroup.ValueRO.MinScale <= 0 ? 0.8f : staticEntitiesGroup.ValueRO.MinScale,
                                MaxScale = staticEntitiesGroup.ValueRO.MaxScale <= 0 ? 1.2f : staticEntitiesGroup.ValueRO.MaxScale,
                                Seed = (uint)(state.WorldUnmanaged.Time.ElapsedTime * 1000 + groupId + 3)
                            };
                            
                            meshPlacementJob = setMeshEntityLocalToWorldJob.Schedule(meshSpawnCount, 64, precalcJobHandle);
                        }
                        
                        // Combine placement jobs
                        JobHandle placementJobHandle = JobHandle.CombineDependencies(terrainPlacementJob, meshPlacementJob);
                        
                        // Combine all dependencies including disposals
                        NativeList<JobHandle> disposalHandles = new NativeList<JobHandle>(4, Allocator.Temp);
                        disposalHandles.Add(placementJobHandle);
                        disposalHandles.Add(meshHabitatEntities.Dispose(placementJobHandle));
                        if (terrainSpawnCount > 0) disposalHandles.Add(terrainSpawnIndices.Dispose(placementJobHandle));
                        if (meshSpawnCount > 0) disposalHandles.Add(meshSpawnPoints.Dispose(placementJobHandle));
                        
                        combinedDependencies = JobHandle.CombineDependencies(disposalHandles.AsArray());
                        disposalHandles.Dispose();
                        
                        // Update the staticEntitiesGroup Count
                        staticEntitiesGroupCopy.Count += amountToInstantiate;
                        entityCommandBuffer.SetComponent(staticEntitiesGroupEntity, staticEntitiesGroupCopy);
                        // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Updated Count to {staticEntitiesGroupCopy.Count}.");
                    }
                    // If RequestedCount < Count, destroy excess entities
                    else if (requestedCount < currentCount)
                    {
                        int entitiesToDestroy = currentCount - requestedCount;
                        // Limit the number of entities to destroy per update
                        entitiesToDestroy = SystemMath.Min(entitiesToDestroy, MAX_ENTITIES_PER_UPDATE);
                        
                        // Query for entities in this group
                        EntityQuery staticEntityQuery = SystemAPI.QueryBuilder()
                            .WithAll<StaticEntityShared>()
                            .WithAll<LocalToWorld>()
                            .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                            .Build();
                        
                        // Filter by shared component manually
                        staticEntityQuery.SetSharedComponentFilter(new StaticEntityShared { 
                            StaticEntitiesGroupId = groupId 
                        });
                        
                        var allGroupEntities = staticEntityQuery.ToEntityArray(Allocator.TempJob);
                        int destroyedCount = 0;

                        if (allGroupEntities.Length > 0 && entitiesToDestroy > 0)
                        {
                            var random = Random.CreateFromIndex((uint)(state.WorldUnmanaged.Time.ElapsedTime * 1000 + groupId + 4)); // Unique seed

                            if (entitiesToDestroy >= allGroupEntities.Length)
                            {
                                // If we need to destroy all or more than available, destroy all
                                for (int i = 0; i < allGroupEntities.Length; i++)
                                {
                                    entityCommandBuffer.DestroyEntity(allGroupEntities[i]);
                                    destroyedCount++;
                                }
                            }
                            else
                            {
                                // Use a list to manage entities for random removal
                                var entitiesList = new NativeList<Entity>(allGroupEntities.Length, Allocator.Temp);
                                entitiesList.AddRange(allGroupEntities);

                                for (int i = 0; i < entitiesToDestroy && entitiesList.Length > 0; i++)
                                {
                                    int randomIndex = random.NextInt(0, entitiesList.Length);
                                    entityCommandBuffer.DestroyEntity(entitiesList[randomIndex]);
                                    entitiesList.RemoveAtSwapBack(randomIndex); // Efficient removal
                                    destroyedCount++;
                                }
                                entitiesList.Dispose();
                            }
                        }
                        
                        allGroupEntities.Dispose();

                        // Update the staticEntitiesGroup Count
                        staticEntitiesGroupCopy.Count -= destroyedCount;
                        entityCommandBuffer.SetComponent(staticEntitiesGroupEntity, staticEntitiesGroupCopy);
                        // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Updated Count to {staticEntitiesGroupCopy.Count}.");
                    }
                }
                // Handle shader updates
                else if (staticEntitiesGroup.ValueRO.ShaderUpdateRequested == true)
                {
                    // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: Shader update requested.");
                    
                    // Query for all static entities in the current group
                    EntityQuery staticEntityQuery = SystemAPI.QueryBuilder()
                        .WithAll<StaticEntityShared, ScreenDisplayStartOverride, ScreenDisplayEndOverride>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities) // Include disabled if they might be re-enabled with shader changes
                        .Build();
                    staticEntityQuery.SetSharedComponentFilter(new StaticEntityShared { 
                        StaticEntitiesGroupId = groupId 
                    });

                    NativeArray<Entity> groupMemberEntities = staticEntityQuery.ToEntityArray(Allocator.Temp);
                    int totalCountInGroup = groupMemberEntities.Length;

                    if (totalCountInGroup > 0)
                    {
                        var groupData = staticEntitiesGroup.ValueRO; // Use a read-only copy for group data

                        for (int entityIdx = 0; entityIdx < totalCountInGroup; entityIdx++)
                        {
                            Entity currentEntity = groupMemberEntities[entityIdx];
                            float4 entityScreenDisplayStart = float4.zero;
                            float4 entityScreenDisplayEnd = float4.zero;

                            for (int viewIdx = 0; viewIdx < groupData.ViewsCount; viewIdx++)
                            {
                                float visibilityForThisView = 0;
                                if (viewIdx == 0) visibilityForThisView = groupData.ViewVisibilityPercentages.x;
                                else if (viewIdx == 1) visibilityForThisView = groupData.ViewVisibilityPercentages.y;
                                else if (viewIdx == 2) visibilityForThisView = groupData.ViewVisibilityPercentages.z;
                                else if (viewIdx == 3) visibilityForThisView = groupData.ViewVisibilityPercentages.w;

                                // Check if this entity instance falls within the percentage for this view
                                // (float)entityIdx / totalCountInGroup gives a normalized index from 0 to nearly 1
                                if ((float)entityIdx / totalCountInGroup < visibilityForThisView)
                                {
                                    var startFloat = 1.0f / groupData.ViewsCount * viewIdx;
                                    var endFloat = 1.0f / groupData.ViewsCount * (viewIdx + 1);

                                    if (viewIdx == 0) { entityScreenDisplayStart.x = startFloat; entityScreenDisplayEnd.x = endFloat; }
                                    else if (viewIdx == 1) { entityScreenDisplayStart.y = startFloat; entityScreenDisplayEnd.y = endFloat; }
                                    else if (viewIdx == 2) { entityScreenDisplayStart.z = startFloat; entityScreenDisplayEnd.z = endFloat; }
                                    else if (viewIdx == 3) { entityScreenDisplayStart.w = startFloat; entityScreenDisplayEnd.w = endFloat; }
                                }
                                // Else, the entity is not visible in this view slice, start/end remain 0 for this view's components
                            }
                            
                            // It's important to check if the components actually exist before trying to set them,
                            // though the query should guarantee this.
                            if (SystemAPI.HasComponent<ScreenDisplayStartOverride>(currentEntity))
                            {
                                entityCommandBuffer.SetComponent(currentEntity, new ScreenDisplayStartOverride { Value = entityScreenDisplayStart });
                            }
                            if (SystemAPI.HasComponent<ScreenDisplayEndOverride>(currentEntity))
                            {
                                entityCommandBuffer.SetComponent(currentEntity, new ScreenDisplayEndOverride { Value = entityScreenDisplayEnd });
                            }
                        }
                    }
                    
                    groupMemberEntities.Dispose();
                    // staticEntityQuery.Dispose(); // DO NOT DISPOSE - System Managed

                    // Reset the flag
                    staticEntitiesGroupCopy.ShaderUpdateRequested = false;
                    entityCommandBuffer.SetComponent(staticEntitiesGroupEntity, staticEntitiesGroupCopy);
                }
                else
                {
                    // Debug.Log($"[StaticEntitySpawnSystem] Group {groupId}: No density change or shader update requested.");
                }
            }

            // Assign the final combined handle back to state.Dependency
            state.Dependency = combinedDependencies;
        }
    }

    /// <summary>
    /// Job that calculates the spawn indices based on splatmap probability and noise.
    /// </summary>
    [BurstCompile]
    struct CalculateSpawnIndicesJob : IJob
    {
        [ReadOnly] public BlobAssetReference<ByteBlob> SplatmapDataBlobRef;
        [ReadOnly] public int SplatmapWidth;
        [ReadOnly] public int SplatmapHeight;
        [ReadOnly] public float3 GroupNoiseOffset;
        [ReadOnly] public float NoiseScale;
        [ReadOnly] public float TerrainOffsetX;
        [ReadOnly] public float TerrainOffsetZ;
        [ReadOnly] public float TerrainSize;
        [ReadOnly] public int AmountToInstantiate;
        [ReadOnly] public uint Seed;
        [ReadOnly] public bool UseSplatmap;

        public NativeArray<int> SpawnPositionIndices;

        private const int MAX_ATTEMPTS_PER_INDEX = 100;

        public void Execute()
        {
            var random = Random.CreateFromIndex(Seed);
            int splatmapPixelCount = SplatmapWidth * SplatmapHeight;

            if (!UseSplatmap || !SplatmapDataBlobRef.IsCreated || splatmapPixelCount <= 0)
            {
                int maxIndex = splatmapPixelCount > 0 ? splatmapPixelCount : 1;
                for (int i = 0; i < AmountToInstantiate; ++i)
                {
                    SpawnPositionIndices[i] = random.NextInt(0, maxIndex);
                }
                return;
            }

            ref var splatmapValues = ref SplatmapDataBlobRef.Value.Values;

            for (int i = 0; i < AmountToInstantiate; ++i)
            {
                int attempts = 0;
                bool foundValid = false;
                while (attempts < MAX_ATTEMPTS_PER_INDEX)
                {
                    attempts++;

                    float normX = random.NextFloat();
                    float normZ = random.NextFloat();
                    float approxWorldX = TerrainOffsetX + normX * TerrainSize;
                    float approxWorldZ = TerrainOffsetZ + normZ * TerrainSize;
                    float noiseValue = noise.snoise(new float3(approxWorldX / NoiseScale + GroupNoiseOffset.x, approxWorldZ / NoiseScale + GroupNoiseOffset.z, GroupNoiseOffset.y));
                    noiseValue = (noiseValue * 0.5f) + 0.5f;
                    float noiseFactor = (noiseValue - 0.5f) * 2f + 0.5f;
                    noiseFactor = math.clamp(noiseFactor, 0f, 1f);

                    int splatX = (int)(normX * SplatmapWidth);
                    int splatZ = (int)(normZ * SplatmapHeight);
                    splatX = math.clamp(splatX, 0, SplatmapWidth - 1);
                    splatZ = math.clamp(splatZ, 0, SplatmapHeight - 1);
                    int pixelIndex = splatZ * SplatmapWidth + splatX;

                    if (pixelIndex >= splatmapValues.Length) continue;
                    float greenWeight = splatmapValues[pixelIndex] / 255f;

                    if (greenWeight < 0.01f)
                    {
                        continue;
                    }

                    float combinedWeight = greenWeight * noiseFactor;
                    float randomCheckValue = random.NextFloat();
                    if (randomCheckValue < combinedWeight)
                    {
                        SpawnPositionIndices[i] = pixelIndex;
                        foundValid = true;
                        break;
                    }
                }

                if (!foundValid)
                {
                    int fallbackIndex = -1;
                    for(int pIdx = 0; pIdx < splatmapPixelCount; ++pIdx)
                    {
                        if (pIdx < splatmapValues.Length)
                        {
                           float fallbackGreenWeight = splatmapValues[pIdx] / 255f;
                           if (fallbackGreenWeight >= 0.01f)
                           {
                               fallbackIndex = pIdx;
                               break;
                           }
                        }
                    }
                    SpawnPositionIndices[i] = (fallbackIndex != -1) ? fallbackIndex : random.NextInt(0, splatmapPixelCount);
                }
            }
        }
    }

    /// <summary>
    /// Job responsible for initializing static entity positions and orientations when spawned on terrain.
    /// </summary>
    [BurstCompile]
    struct SetStaticEntityLocalToWorld : IJobParallelFor
    {
        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalToWorld> LocalToWorldFromEntity;

        [ReadOnly] public NativeArray<Entity> Entities;
        [ReadOnly] public float MinScale;
        [ReadOnly] public float MaxScale;
        [ReadOnly] public float TerrainSize;
        [ReadOnly] public float TerrainHeight;
        [ReadOnly] public float TerrainOffsetX;
        [ReadOnly] public float TerrainOffsetY;
        [ReadOnly] public float TerrainOffsetZ;
        [ReadOnly] public BlobAssetReference<FloatBlob> HeightmapDataBlobRef;
        [ReadOnly] public int HeightmapWidth;
        [ReadOnly] public int HeightmapHeight;
        [ReadOnly] public bool UseSplatmap;
        [ReadOnly] public int SplatmapWidth;
        [ReadOnly] public int SplatmapHeight;
        [ReadOnly] public NativeArray<int> SpawnPositionIndices;

        public void Execute(int i)
        {
            var entity = Entities[i];
            var random = Random.CreateFromIndex((uint)(entity.Index + i + SpawnPositionIndices[i]));
            
            float3 pos;
            float worldX, worldZ;

            if (UseSplatmap && SpawnPositionIndices.Length > i && HeightmapDataBlobRef.IsCreated)
            {
                int spawnIndex = SpawnPositionIndices[i];
                int splatX = spawnIndex % SplatmapWidth;
                int splatZ = spawnIndex / SplatmapWidth;
                float offsetX = random.NextFloat(-0.5f, 0.5f);
                float offsetZ = random.NextFloat(-0.5f, 0.5f);
                float terrainNormX = ((float)splatX + offsetX + 0.5f) / SplatmapWidth;
                float terrainNormZ = ((float)splatZ + offsetZ + 0.5f) / SplatmapHeight;
                terrainNormX = math.clamp(terrainNormX, 0f, 0.999f);
                terrainNormZ = math.clamp(terrainNormZ, 0f, 0.999f);
                worldX = TerrainOffsetX + terrainNormX * TerrainSize;
                worldZ = TerrainOffsetZ + terrainNormZ * TerrainSize;
                float height = SampleHeightmapWithInterpolation(terrainNormX, terrainNormZ);
                float terrainY = height * TerrainHeight;
                pos = new float3(worldX, TerrainOffsetY + terrainY, worldZ);
            }
            else
            {
                float normalizedX = random.NextFloat(0f, 1f);
                float normalizedZ = random.NextFloat(0f, 1f);
                worldX = TerrainOffsetX + normalizedX * TerrainSize;
                worldZ = TerrainOffsetZ + normalizedZ * TerrainSize;
                float height = SampleHeightmapWithInterpolation(normalizedX, normalizedZ);
                float terrainY = height * TerrainHeight;
                pos = new float3(worldX, TerrainOffsetY + terrainY, worldZ);
            }
            
            float randomYaw = random.NextFloat(0f, 360f);
            float randomPitch = random.NextFloat(-5f, 5f);
            float randomRoll = random.NextFloat(-5f, 5f);
            quaternion rotation = quaternion.Euler(math.radians(randomPitch), math.radians(randomYaw), math.radians(randomRoll));
            
            float baseScale = random.NextFloat(MinScale, MaxScale);
            float3 scale = new float3(baseScale * random.NextFloat(0.9f, 1.1f),
                                    baseScale,
                                    baseScale * random.NextFloat(0.9f, 1.1f));

            LocalToWorldFromEntity[entity] = new LocalToWorld
            {
                Value = float4x4.TRS(pos, rotation, scale)
            };
        }

        private float SampleHeightmapWithInterpolation(float normalizedX, float normalizedZ)
        {
            // Early return for invalid heightmap configuration
            if (!HeightmapDataBlobRef.IsCreated || HeightmapWidth <= 1 || HeightmapHeight <= 1)
                return 0.5f;
            
            // Try to access the blob value safely - this might be where the null reference occurs
            ref var heightmapBlobValue = ref HeightmapDataBlobRef.Value;
            ref var heightmapValues = ref heightmapBlobValue.Values;
            
            // Check if the Values array has the expected length
            int expectedLength = HeightmapWidth * HeightmapHeight;
            if (heightmapValues.Length != expectedLength || heightmapValues.Length == 0)
            {
                return 0.5f;
            }
            
            float heightmapX = normalizedX * (HeightmapWidth - 1);
            float heightmapZ = normalizedZ * (HeightmapHeight - 1);
            
            int x0 = (int)heightmapX;
            int z0 = (int)heightmapZ;
            int x1 = math.min(x0 + 1, HeightmapWidth - 1);
            int z1 = math.min(z0 + 1, HeightmapHeight - 1);
            
            float tx = heightmapX - x0;
            float tz = heightmapZ - z0;
            
            float h00 = SampleHeightmapPoint(ref heightmapValues, x0, z0);
            float h01 = SampleHeightmapPoint(ref heightmapValues, x0, z1);
            float h10 = SampleHeightmapPoint(ref heightmapValues, x1, z0);
            float h11 = SampleHeightmapPoint(ref heightmapValues, x1, z1);
            
            float h0 = math.lerp(h00, h10, tx);
            float h1 = math.lerp(h01, h11, tx);
            float finalHeight = math.lerp(h0, h1, tz);
            
            return finalHeight;
        }
        
        private float SampleHeightmapPoint(ref BlobArray<float> values, int x, int z)
        {
            int index = z * HeightmapWidth + x;
             if (index < 0 || index >= values.Length)
                 return 0.5f;
            return values[index];
        }
    }

    /// <summary>
    /// Job responsible for placing static entities on mesh surfaces
    /// </summary>
    [BurstCompile]
    struct SetMeshEntityLocalToWorld : IJobParallelFor
    {
        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalToWorld> LocalToWorldFromEntity;

        [ReadOnly] public NativeArray<Entity> Entities;
        [ReadOnly] public NativeArray<MeshSpawnPoint> SpawnPoints;
        [ReadOnly] public float MinScale;
        [ReadOnly] public float MaxScale;
        [ReadOnly] public uint Seed;

        public void Execute(int i)
        {
            var entity = Entities[i];
            var spawnPoint = SpawnPoints[i];
            
            // Skip if the mesh entity is null (invalid spawn point)
            if (spawnPoint.MeshEntity == Entity.Null)
            {
                return;
            }
            
            var random = Random.CreateFromIndex(Seed + (uint)i);
            
            // Get spawn position and normal from the pre-calculated data
            float3 position = spawnPoint.Position;
            float3 normal = spawnPoint.Normal;
            
            // Calculate rotation to align with surface normal
            quaternion alignWithNormal = quaternion.LookRotation(
                math.cross(normal, new float3(random.NextFloat(-1f, 1f), 0, random.NextFloat(-1f, 1f))), 
                normal
            );
            
            // Add random rotation around normal axis
            float randomYawAngle = random.NextFloat(0, math.PI * 2);
            quaternion randomYaw = quaternion.AxisAngle(normal, randomYawAngle);
            quaternion finalRotation = math.mul(randomYaw, alignWithNormal);
            
            // Random scale
            float baseScale = random.NextFloat(MinScale, MaxScale);
            float3 scale = new float3(
                baseScale * random.NextFloat(0.9f, 1.1f), 
                baseScale, 
                baseScale * random.NextFloat(0.9f, 1.1f)
            );

            // Set LocalToWorld component
            LocalToWorldFromEntity[entity] = new LocalToWorld
            {
                Value = float4x4.TRS(position, finalRotation, scale)
            };
        }
    }
} 