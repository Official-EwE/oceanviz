using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using System;
using UnityEngine.Serialization;

namespace OceanViz3
{
    /// <summary>
    /// Authoring component for creating and managing a group of static entities.
    /// This component is responsible for setting up the initial configuration of a static entity group
    /// and converting it into an ECS entity.
    /// </summary>
    public class StaticEntitiesGroupAuthoring : MonoBehaviour
    {
        [FormerlySerializedAs("density")] 
        public int InitialCount = 1000;
        public GameObject DefaultPrefab;
        
        public float MinScale = 0.8f;
        public float MaxScale = 1.2f;
        
        /// <summary>
        /// Baker class that converts the authoring MonoBehaviour into ECS components.
        /// </summary>
        class Baker : Baker<StaticEntitiesGroupAuthoring>
        {
            public override void Bake(StaticEntitiesGroupAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                
                // Get the entity for the prototype prefab
                Entity prototypeEntity = GetEntity(authoring.DefaultPrefab, TransformUsageFlags.Renderable);
                
                // Ensure the prototype has necessary components (optional, but good practice)
                // AddComponent<StaticEntityShared>(prototypeEntity); // Assuming StaticEntityAuthoring handles this
                
                AddComponent(entity, new StaticEntitiesGroupComponent
                {
                    StaticEntitiesGroupId = -1, // Will be assigned at runtime by StaticEntitiesGroup.cs
                    StaticEntityPrototype = prototypeEntity,
                    Count = 0,
                    RequestedCount = authoring.InitialCount,
                    DestroyRequested = false,
                    NumberOfLODs = -1, // Will be determined at runtime

                    // Shader Properties
                    ShaderUpdateRequested = false,
                    ViewsCount = 1,
                    ViewVisibilityPercentages = new float4(1, 1, 1, 1),

                    // Appearance Properties
                    MinScale = authoring.MinScale,
                    MaxScale = authoring.MaxScale,

                    // Pre-calculation Data (initialized here, populated by StaticEntityDataSetupSystem)
                    SpawnDataIsReady = false,
                    TerrainSize = 0f,
                    TerrainHeight = 0f,
                    TerrainOffsetX = 0f,
                    TerrainOffsetY = 0f,
                    TerrainOffsetZ = 0f,
                    HeightmapWidth = 0,
                    HeightmapHeight = 0,
                    HeightmapDataBlobRef = BlobAssetReference<FloatBlob>.Null, // Initialize as Null
                    UseSplatmap = false,
                    SplatmapWidth = 0,
                    SplatmapHeight = 0,
                    SplatmapDataBlobRef = BlobAssetReference<ByteBlob>.Null, // Initialize as Null
                    NoiseScale = 6.0f, // Default, can be adjusted by setup system if needed
                    GroupNoiseOffset = float3.zero, // Default, assigned unique value by setup system
                    
                    // Mesh Habitat Settings
                    UseMeshHabitats = false, // Default, updated by StaticEntityDataSetupSystem
                    MeshHabitatRatio = 0.5f // Default, updated by StaticEntityDataSetupSystem
                });
                
                // Add the dynamic buffer for mesh habitat references
                AddBuffer<MeshHabitatEntityRef>(entity);
                // Add the dynamic buffer for habitat names
                AddBuffer<StaticEntityHabitat>(entity);
            }
        }
    }
} 