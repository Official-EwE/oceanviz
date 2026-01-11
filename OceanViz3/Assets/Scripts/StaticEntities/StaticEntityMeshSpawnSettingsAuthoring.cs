using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

namespace OceanViz3
{
    /// <summary>
    /// MonoBehaviour for configuring mesh habitat settings in the Unity Inspector
    /// </summary>
    public class StaticEntityMeshSpawnSettingsAuthoring : MonoBehaviour
    {
        [Header("Mesh Habitat Settings")]
        [Tooltip("Whether to enable spawning on mesh habitats")]
        public bool useMeshHabitats = true;
        
        [Tooltip("Distribution ratio between terrain and mesh habitats (0 = all on terrain, 1 = all on mesh)")]
        [Range(0f, 1f)]
        public float meshHabitatRatio = 0.5f;
        
        /// <summary>
        /// Baker that adds mesh spawning configuration to static entity groups
        /// </summary>
        public class Baker : Baker<StaticEntityMeshSpawnSettingsAuthoring>
        {
            public override void Bake(StaticEntityMeshSpawnSettingsAuthoring authoring)
            {
                // This component would be added to the same GameObject as StaticEntitiesGroup
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                
                // Add component with mesh habitat settings
                AddComponent(entity, new StaticEntityMeshSpawnSettings
                {
                    UseMeshHabitats = authoring.useMeshHabitats,
                    MeshHabitatRatio = authoring.meshHabitatRatio
                });
            }
        }
    }
    
    /// <summary>
    /// Component containing mesh habitat spawn settings
    /// </summary>
    public struct StaticEntityMeshSpawnSettings : IComponentData
    {
        public bool UseMeshHabitats;
        public float MeshHabitatRatio;
    }
} 