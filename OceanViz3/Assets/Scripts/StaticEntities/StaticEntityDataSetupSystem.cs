using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using Unity.Burst;
using System.Collections.Generic;
using System.Linq;
using Unity.Transforms;

namespace OceanViz3
{
    /// <summary>
    /// Component to store references to mesh habitats for a static entity group
    /// </summary>
    public struct StaticEntityMeshHabitatRefs : IComponentData
    {
        // Store up to 16 mesh habitat entity references, which should be enough for most cases
        // For more, we'd need a DynamicBuffer or a different approach
        public FixedList128Bytes<Entity> MeshHabitatEntities;
    }

    /// <summary>
    /// System responsible for gathering terrain/splatmap/noise data for StaticEntityGroups
    /// and storing it efficiently (e.g., in BlobAssets) within the StaticEntitiesGroupComponent.
    /// Also associates mesh habitats with static entity groups based on matching habitat names.
    /// Runs before StaticEntitySpawnSystem.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(StaticEntitySpawnSystem))]
    [UpdateAfter(typeof(MeshHabitatSetupSystem))]
    [RequireMatchingQueriesForUpdate]
    public partial struct StaticEntityDataSetupSystem : ISystem
    {
        private EntityQuery groupsNeedingSetupQuery;
        private EntityQuery meshHabitatsQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            groupsNeedingSetupQuery = SystemAPI.QueryBuilder()
                .WithAllRW<StaticEntitiesGroupComponent>()
                .WithNone<SpawnDataReadyTag>() // Use a tag to mark completion
                .Build();
                
            // Query for *processed* mesh habitats
            meshHabitatsQuery = SystemAPI.QueryBuilder()
                .WithAll<MeshHabitatComponent, MeshHabitatProcessedTag, MeshHabitatBlobRef>()
                .Build();
                
            state.RequireForUpdate(groupsNeedingSetupQuery);
            // No need to require meshHabitatsQuery here, as we check its length later
            // state.RequireForUpdate(meshHabitatsQuery); 
            Debug.Log("[StaticEntityDataSetupSystem] OnCreate completed.");
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            // Disposal of shared heightmap blob should perhaps be handled elsewhere
            // if it's truly shared across scenes/lifetimes.
            // Disposal of splatmap blobs is handled by StaticEntitySpawnSystem on group destruction.
            Debug.Log("[StaticEntityDataSetupSystem] OnDestroy called.");
        }

        public void OnUpdate(ref SystemState state)
        {
            Debug.Log("[StaticEntityDataSetupSystem] OnUpdate Start"); // Log system start

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            var meshHabitatBufferLookup = SystemAPI.GetBufferLookup<MeshHabitatEntityRef>(false); 

            // --- Find Main Scene/Location --- 
            GameObject mainSceneObj = GameObject.Find("MainSceneScript");
            if (mainSceneObj == null) 
            {
                Debug.LogError("[StaticEntityDataSetupSystem] MainSceneScript GameObject not found. Exiting OnUpdate.");
                return; 
            }
            MainScene mainScene = mainSceneObj.GetComponent<MainScene>();
            if (mainScene == null) 
            {
                 Debug.LogError("[StaticEntityDataSetupSystem] MainScene component not found. Exiting OnUpdate.");
                 return;
            }
            if (mainScene.currentLocationScript == null)
            {
                 Debug.LogWarning("[StaticEntityDataSetupSystem] MainScene.currentLocationScript is null. Waiting for location to load? Exiting OnUpdate.");
                 return; // Might be between location loads
            }
            LocationScript locationScript = mainScene.currentLocationScript;
            Terrain terrain = locationScript.GetTerrain();
            if (terrain == null) 
            {
                 Debug.LogError("[StaticEntityDataSetupSystem] LocationScript.GetTerrain() returned null. Exiting OnUpdate.");
                 return;
            }
            if (terrain.terrainData == null)
            {
                 Debug.LogError("[StaticEntityDataSetupSystem] Terrain.terrainData is null. Exiting OnUpdate.");
                 return;
            }
            Debug.Log("[StaticEntityDataSetupSystem] Found MainScene, LocationScript, and Terrain.");

            // --- Gather Static Terrain Data --- 
            TerrainData terrainData = terrain.terrainData;
            float terrainSize = terrainData.size.x; 
            float terrainHeight = terrainData.size.y;
            float terrainOffsetX = terrain.transform.position.x;
            float terrainOffsetY = terrain.transform.position.y;
            float terrainOffsetZ = terrain.transform.position.z;
            int heightmapRes = terrainData.heightmapResolution;

            // --- Shared Heightmap Blob Management --- 
            bool heightmapBlobCreatedThisFrame = false;
            BlobAssetReference<FloatBlob> heightmapBlobRef = BlobAssetReference<FloatBlob>.Null;

            // --- Mesh Habitat Lookup Preparation ---
            var allMeshHabitatEntities = meshHabitatsQuery.ToEntityArray(Allocator.Temp);
            var allMeshHabitatComponents = meshHabitatsQuery.ToComponentDataArray<MeshHabitatComponent>(Allocator.Temp);
            Debug.Log($"[StaticEntityDataSetupSystem] Found {allMeshHabitatEntities.Length} processed mesh habitat entities.");

            // --- Base Noise Settings --- 
            float3 baseNoiseOffset = new float3(123.45f, 678.90f, 111.22f);
            float defaultNoiseScale = 6.0f;

            // Process each group needing setup
            int groupsFound = 0;
            foreach (var (groupComponentRW, entity) in SystemAPI.Query<RefRW<StaticEntitiesGroupComponent>>()
                         .WithNone<SpawnDataReadyTag>()
                         .WithEntityAccess())
            {
                groupsFound++;
                ref var groupComponent = ref groupComponentRW.ValueRW;
                int currentGroupId = groupComponent.StaticEntitiesGroupId;
                Debug.Log($"[StaticEntityDataSetupSystem] ---------- Processing Group {currentGroupId} (Entity: {entity}) ----------");

                // --- Create/Assign Shared Heightmap Blob --- 
                if (!heightmapBlobRef.IsCreated) // Only check/create if the shared ref isn't set yet
                {
                     // Check if ANY existing *processed* group already has it
                     var existingGroupsQuery = SystemAPI.QueryBuilder().WithAll<StaticEntitiesGroupComponent, SpawnDataReadyTag>().Build();
                     if (!existingGroupsQuery.IsEmpty)
                     {
                         // Iterate through the processed groups query to find one with a valid blob
                         // We only need one, so we can break after finding it.
                         foreach (var (existingGroup, existingEntity) in 
                                  SystemAPI.Query<RefRO<StaticEntitiesGroupComponent>>()
                                  .WithAll<SpawnDataReadyTag>()
                                  .WithEntityAccess())
                          { 
                             if (existingGroup.ValueRO.HeightmapDataBlobRef.IsCreated)
                             {
                                 heightmapBlobRef = existingGroup.ValueRO.HeightmapDataBlobRef;
                                 Debug.Log("[StaticEntityDataSetupSystem] Reusing existing shared Heightmap BlobAsset.");
                                 break; // Found one, stop checking
                             }
                         }
                     }
                     
                     // If still not found after checking, create it now
                     if (!heightmapBlobRef.IsCreated)
                     {
                         Debug.Log("[StaticEntityDataSetupSystem] Creating new shared Heightmap BlobAsset.");
                         using (var blobBuilder = new BlobBuilder(Allocator.Temp))
                         {
                            ref FloatBlob heightmapBlobAsset = ref blobBuilder.ConstructRoot<FloatBlob>();
                            int length = heightmapRes * heightmapRes;
                            BlobBuilderArray<float> heightmapArrayBuilder = blobBuilder.Allocate(ref heightmapBlobAsset.Values, length);
                            float[,] heightmapData = terrainData.GetHeights(0, 0, heightmapRes, heightmapRes);
                            for (int y = 0; y < heightmapRes; y++)
                            {
                                for (int x = 0; x < heightmapRes; x++)
                                {
                                    heightmapArrayBuilder[y * heightmapRes + x] = heightmapData[y, x];
                                }
                            }
                            heightmapBlobRef = blobBuilder.CreateBlobAssetReference<FloatBlob>(Allocator.Persistent);
                            heightmapBlobCreatedThisFrame = true; 
                         }
                     }
                }

                // --- Assign Terrain Data --- 
                groupComponent.TerrainSize = terrainSize;
                groupComponent.TerrainHeight = terrainHeight;
                groupComponent.TerrainOffsetX = terrainOffsetX;
                groupComponent.TerrainOffsetY = terrainOffsetY;
                groupComponent.TerrainOffsetZ = terrainOffsetZ;
                groupComponent.HeightmapWidth = heightmapRes;
                groupComponent.HeightmapHeight = heightmapRes;
                groupComponent.HeightmapDataBlobRef = heightmapBlobRef; 
                Debug.Log($"[StaticEntityDataSetupSystem] Group {currentGroupId}: Assigned terrain data and heightmap blob (IsCreated={heightmapBlobRef.IsCreated})");
                
                // --- Get Habitat Info & Scale --- 
                var groupHabitatsBuffer = SystemAPI.GetBuffer<StaticEntityHabitat>(entity);
                var groupHabitats = new NativeHashSet<FixedString64Bytes>(groupHabitatsBuffer.Length, Allocator.Temp);
                foreach (var h in groupHabitatsBuffer)
                {
                    groupHabitats.Add(h.Name);
                }
                
                // Find the StaticEntitiesGroup MonoBehaviour in either Simulation or Asset Browser mode
                StaticEntitiesGroup staticGroupMono = null;
                if (mainScene.simulationModeManager != null)
                {
                    staticGroupMono = mainScene.simulationModeManager.staticEntitiesGroups.FirstOrDefault(g => g != null && g.StaticEntitiesGroupId == currentGroupId);
                }

                if (staticGroupMono == null && mainScene.assetBrowserModeManager != null)
                {
                    var assetBrowserGroup = mainScene.assetBrowserModeManager.GetCurrentStaticGroup();
                    if (assetBrowserGroup != null && assetBrowserGroup.StaticEntitiesGroupId == currentGroupId)
                    {
                        staticGroupMono = assetBrowserGroup;
                    }
                }
                
                if (staticGroupMono != null && staticGroupMono.staticEntityPreset != null)
                {
                    groupComponent.MinScale = staticGroupMono.staticEntityPreset.minScale > 0 ? staticGroupMono.staticEntityPreset.minScale : 0.8f;
                    groupComponent.MaxScale = staticGroupMono.staticEntityPreset.maxScale > 0 ? staticGroupMono.staticEntityPreset.maxScale : 1.2f;
                    groupComponent.Rigidity = staticGroupMono.staticEntityPreset.rigidity; // Rigidity from 0 to 1
                    Debug.Log($"[StaticEntityDataSetupSystem] Group {currentGroupId}: Found MonoBehaviour. Scale={groupComponent.MinScale:F2}-{groupComponent.MaxScale:F2}, Rigidity={groupComponent.Rigidity:F2}");
                }
                else
                {
                    Debug.LogWarning($"[StaticEntityDataSetupSystem] Group {currentGroupId}: Could not find StaticEntitiesGroup MonoBehaviour. Using default scale/rigidity.");
                    groupComponent.MinScale = 0.8f;
                    groupComponent.MaxScale = 1.2f;
                    groupComponent.Rigidity = 0.5f; // Default rigidity value
                }

                // --- Noise Setup --- 
                groupComponent.NoiseScale = defaultNoiseScale;
                groupComponent.GroupNoiseOffset = baseNoiseOffset + new float3(currentGroupId * 13.7f, currentGroupId * 29.1f, currentGroupId * 43.3f);
                Debug.Log($"[StaticEntityDataSetupSystem] Group {currentGroupId}: Set noise parameters (Scale={groupComponent.NoiseScale:F1})");

                // --- Splatmap Setup --- 
                BlobAssetReference<ByteBlob> splatmapBlobRef = BlobAssetReference<ByteBlob>.Null;
                bool useSplatmap = false;
                int splatmapWidth = 0;
                int splatmapHeight = 0;
                
                Texture2D splatmapTexture = null;
                string foundSplatmapHabitat = string.Empty;

                foreach (var habitatName in groupHabitats)
                {
                    splatmapTexture = locationScript.GetFloraBiomeSplatmap(habitatName.ToString());
                    if (splatmapTexture != null)
                    {
                        foundSplatmapHabitat = habitatName.ToString();
                        break;
                    }
                }

                if (splatmapTexture != null)
                {
                    if (!splatmapTexture.isReadable)
                    {
                        Debug.LogError($"[StaticEntityDataSetupSystem] Splatmap texture '{splatmapTexture.name}' for habitat '{foundSplatmapHabitat}' is not readable.");
                    }
                    else
                    {
                        Debug.Log($"[StaticEntityDataSetupSystem] Group {currentGroupId}: Found readable splatmap texture '{splatmapTexture.name}' for habitat '{foundSplatmapHabitat}'. Creating BlobAsset.");
                        useSplatmap = true;
                        splatmapWidth = splatmapTexture.width;
                        splatmapHeight = splatmapTexture.height;
                        Color32[] pixels = splatmapTexture.GetPixels32();
                        int pixelLength = pixels.Length;
                        using (var splatBlobBuilder = new BlobBuilder(Allocator.Temp))
                        {
                            ref ByteBlob splatmapBlobAsset = ref splatBlobBuilder.ConstructRoot<ByteBlob>();
                            BlobBuilderArray<byte> splatmapArrayBuilder = splatBlobBuilder.Allocate(ref splatmapBlobAsset.Values, pixelLength);
                            for (int i = 0; i < pixelLength; i++) { splatmapArrayBuilder[i] = pixels[i].g; }
                            splatmapBlobRef = splatBlobBuilder.CreateBlobAssetReference<ByteBlob>(Allocator.Persistent);
                            Debug.Log($"[StaticEntityDataSetupSystem] Group {currentGroupId}: Created Splatmap BlobAsset (Width={splatmapWidth}, Height={splatmapHeight}).");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"[StaticEntityDataSetupSystem] Group {currentGroupId}: No matching splatmap texture found for any of its habitats.");
                }

                groupComponent.UseSplatmap = useSplatmap;
                groupComponent.SplatmapWidth = splatmapWidth;
                groupComponent.SplatmapHeight = splatmapHeight;
                groupComponent.SplatmapDataBlobRef = splatmapBlobRef;
                Debug.Log($"[StaticEntityDataSetupSystem] Group {currentGroupId}: Splatmap setup complete (UseSplatmap={useSplatmap}, BlobCreated={splatmapBlobRef.IsCreated})");

                // --- Mesh Habitat Matching --- 
                bool useMeshHabitats = false;
                int addedMeshCount = 0;
                if (groupHabitats.Count > 0 && allMeshHabitatEntities.Length > 0)
                {
                    if (meshHabitatBufferLookup.HasBuffer(entity))
                    {
                        var meshHabitatBuffer = meshHabitatBufferLookup[entity];
                        meshHabitatBuffer.Clear();
                        for(int i = 0; i < allMeshHabitatEntities.Length; ++i)
                        {
                            if (groupHabitats.Contains(allMeshHabitatComponents[i].HabitatName))
                            {
                                meshHabitatBuffer.Add(new MeshHabitatEntityRef { MeshEntity = allMeshHabitatEntities[i] });
                                useMeshHabitats = true;
                                addedMeshCount++;
                            }
                        }
                        if (addedMeshCount > 0)
                        {
                             Debug.Log($"[StaticEntityDataSetupSystem] Group {currentGroupId}: Found and added {addedMeshCount} matching mesh habitats to buffer.");
                        }
                        else
                        {
                            Debug.Log($"[StaticEntityDataSetupSystem] Group {currentGroupId}: No mesh habitats found matching the group's habitats.");
                        }
                    }
                    else
                    {
                        // This should not happen if the buffer is added correctly in the authoring baker
                        Debug.LogError($"[StaticEntityDataSetupSystem] Group {currentGroupId}: Entity {entity} is MISSING the MeshHabitatEntityRef buffer component!");
                    }
                }
                else
                {
                     Debug.Log($"[StaticEntityDataSetupSystem] Group {currentGroupId}: Skipping mesh habitat matching (GroupHabitats={groupHabitats.Count}, AvailableMeshes={allMeshHabitatEntities.Length})");
                }
                groupComponent.UseMeshHabitats = useMeshHabitats;

                // --- Mesh Ratio Settings --- 
                if (SystemAPI.HasComponent<StaticEntityMeshSpawnSettings>(entity))
                {
                    var settings = SystemAPI.GetComponent<StaticEntityMeshSpawnSettings>(entity);
                    groupComponent.MeshHabitatRatio = settings.MeshHabitatRatio;
                    Debug.Log($"[StaticEntityDataSetupSystem] Group {currentGroupId}: Found MeshSpawnSettings, Ratio set to {settings.MeshHabitatRatio:F2}");
                }
                else
                {
                    groupComponent.MeshHabitatRatio = 0.5f; // Default if no settings component
                    Debug.Log($"[StaticEntityDataSetupSystem] Group {currentGroupId}: No MeshSpawnSettings found, using default Ratio=0.5");
                }

                // --- Mark as Ready --- 
                groupComponent.SpawnDataIsReady = true; 
                ecb.AddComponent<SpawnDataReadyTag>(entity); 
                Debug.Log($"[StaticEntityDataSetupSystem] Group {currentGroupId}: Added SpawnDataReadyTag and set SpawnDataIsReady=true.");

                Debug.Log($"[StaticEntityDataSetupSystem] ---------- FINAL Setup complete for group {currentGroupId} ---------- " +
                         $"UseSplatmap: {groupComponent.UseSplatmap}, UseMeshHabitats: {useMeshHabitats}, MeshRatio: {groupComponent.MeshHabitatRatio:F2}");
                
                // Dispose the hash set used for this group
                groupHabitats.Dispose();
            }

            if (groupsFound == 0)
            {
                Debug.Log("[StaticEntityDataSetupSystem] No groups found needing setup this frame.");
            }
            else
            {
                Debug.Log($"[StaticEntityDataSetupSystem] Processed {groupsFound} group(s) needing setup.");
            }

            // --- Cleanup --- 
            allMeshHabitatEntities.Dispose();
            allMeshHabitatComponents.Dispose();
            Debug.Log("[StaticEntityDataSetupSystem] OnUpdate End"); // Log system end
        }
    }

    /// <summary>
    /// Tag component added to StaticEntitiesGroup entities once their
    /// terrain/splatmap data has been processed by StaticEntityDataSetupSystem.
    /// </summary>
    public struct SpawnDataReadyTag : IComponentData { }
} 