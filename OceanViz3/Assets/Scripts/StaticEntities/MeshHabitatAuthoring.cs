using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections;

namespace OceanViz3
{
    /// <summary>
    /// Component to hold mesh data for invisible habitats where RenderMeshArray is not available.
    /// This is a managed component.
    /// </summary>
    public class InvisibleMeshHabitatComponent : IComponentData
    {
        public Mesh mesh;
    }

    /// <summary>
    /// Authoring component for mesh-based habitats in the ECS system.
    /// Handles converting mesh habitat GameObjects to ECS entities with necessary components.
    /// </summary>
    public class MeshHabitatAuthoring : MonoBehaviour
    {
        /// <summary>
        /// Name of the habitat this mesh represents
        /// </summary>
        public string habitatName;

        /// <summary>
        /// Baker class that converts the authoring MonoBehaviour into ECS components
        /// </summary>
        class Baker : Baker<MeshHabitatAuthoring>
        {
            public override void Bake(MeshHabitatAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                
                // Add the main habitat component
                AddComponent(entity, new MeshHabitatComponent
                {
                    HabitatName = authoring.habitatName,
                });
                
                // Get mesh filter and mesh renderer for vertex color data
                MeshFilter meshFilter = authoring.GetComponent<MeshFilter>();
                MeshRenderer meshRenderer = authoring.GetComponent<MeshRenderer>();

                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    // If the mesh renderer is disabled, we add a managed component with the mesh
                    // so the setup system can still process it.
                    if (meshRenderer != null && !meshRenderer.enabled)
                    {
                        AddComponentObject(entity, new InvisibleMeshHabitatComponent { mesh = meshFilter.sharedMesh });
                    }

                    // Check if mesh has vertex colors
                    if (meshFilter.sharedMesh.colors.Length > 0 || meshFilter.sharedMesh.colors32.Length > 0)
                    {
                        // Add the vertex color data component
                        // The actual mesh data will be processed at runtime
                        AddComponent(entity, new MeshHabitatVertexColorData
                        {
                            HasVertexColors = true
                        });
                    }
                    else
                    {
                        Debug.LogWarning($"Mesh habitat '{authoring.name}' does not have vertex colors. Red channel vertex colors are used for habitat density.");
                        AddComponent(entity, new MeshHabitatVertexColorData
                        {
                            HasVertexColors = false
                        });
                    }
                }
                else
                {
                    Debug.LogError($"Mesh habitat '{authoring.name}' is missing a MeshFilter or Mesh!");
                }
            }
        }
    }

    /// <summary>
    /// Component representing a mesh-based habitat
    /// </summary>
    public struct MeshHabitatComponent : IComponentData
    {
        public FixedString64Bytes HabitatName;
    }

    /// <summary>
    /// Component containing mesh vertex color data for habitat density
    /// </summary>
    public struct MeshHabitatVertexColorData : IComponentData
    {
        public bool HasVertexColors;
        // References to blob assets containing vertex data will be added in the setup system
    }
} 