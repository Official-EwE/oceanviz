using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Serialization;

namespace OceanViz3
{
    /// <summary>
    /// Authoring component for static entities in the ECS system.
    /// Handles the initial setup and baking of static entity properties and material overrides.
    /// </summary>
    public class StaticEntityAuthoring : MonoBehaviour
    {
        /// <summary>
        /// Baker class that converts the authoring MonoBehaviour into ECS components.
        /// </summary>
        class Baker : Baker<StaticEntityAuthoring>
        {
            public override void Bake(StaticEntityAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Renderable);
                
                // Add shared component for group identification
                AddSharedComponent(entity, new StaticEntityShared
                {
                    StaticEntitiesGroupId = -1,
                });

                // Material overrides for shader properties
                AddComponent(entity, new ScreenDisplayStartOverride { Value = new float4(0, 0, 0, 0) });
                AddComponent(entity, new ScreenDisplayEndOverride { Value = new float4(1, 0, 0, 0) });
                AddComponent(entity, new TurbulenceStrengthOverride { Value = 0.0f });
                AddComponent(entity, new WavesMotionStrengthOverride { Value = 0.0f });
                
                // Note: We don't set up rendering components here
                // The rendering components (RenderMeshArray, MaterialMeshInfo) will be set up at runtime
                // in StaticEntitiesGroup.cs, similar to how it's done in DynamicEntitiesGroup.cs
                Debug.Log($"[StaticEntityAuthoring] Successfully baked entity {authoring.name}. Rendering components will be set up at runtime.");
            }
        }
    }

    /// <summary>
    /// Shared component containing settings that apply to an entire group of static entities.
    /// </summary>
    [Serializable]
    [WriteGroup(typeof(LocalToWorld))]
    public struct StaticEntityShared : ISharedComponentData
    {
        /// <summary>
        /// Identifier for the school/group this static entity belongs to
        /// </summary>
        public int StaticEntitiesGroupId;
    }

    /// <summary>
    /// Material property override for turbulence strength shader property
    /// </summary>
    [MaterialProperty("_TurbulenceStrength")]
    public struct TurbulenceStrengthOverride : IComponentData
    {
        public float Value;
    }

    /// <summary>
    /// Material property override for waves motion strength shader property
    /// </summary>
    [MaterialProperty("_WavesMotionStrength")]
    public struct WavesMotionStrengthOverride : IComponentData
    {
        public float Value;
    }
}
