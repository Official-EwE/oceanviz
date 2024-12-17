using System;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;

namespace OceanViz3
{
    /// <summary>
    /// Authoring component for storing and managing scene-wide configuration data.
    /// This component allows setting up global scene parameters that need to be accessible
    /// across multiple systems in the ECS architecture.
    /// </summary>
    public class SceneDataAuthoring : MonoBehaviour
    {
        /// <summary>
        /// The position for the main camera in the scene.
        /// This value will be converted to a SceneData component during entity conversion.
        /// </summary>
        public float3 CameraPosition;

        class Baker : Baker<SceneDataAuthoring>
        {
            public override void Bake(SceneDataAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new SceneData
                {
                    CameraPosition = authoring.CameraPosition
                });
            }
        }
    }

    /// <summary>
    /// Component that stores scene-wide configuration data in the ECS world.
    /// Systems can query for this component to access global scene parameters.
    /// </summary>
    public struct SceneData : IComponentData
    {
        /// <summary>
        /// The position for the main camera in the scene
        /// </summary>
        public float3 CameraPosition;
    }
}
