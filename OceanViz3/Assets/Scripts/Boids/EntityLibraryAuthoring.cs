using System;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace OceanViz3
{
    public class EntityLibraryAuthoring : MonoBehaviour
    {
        public GameObject BoidSchoolPrefab;
        public GameObject BoidTargetPrefab;
        public GameObject BoidPrefab;

        class Baker : Baker<EntityLibraryAuthoring>
        {
            public override void Bake(EntityLibraryAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new EntityLibrary
                {
                    BoidSchoolEntity = GetEntity(authoring.BoidSchoolPrefab, TransformUsageFlags.None),
                    BoidEntity = GetEntity(authoring.BoidPrefab, TransformUsageFlags.Dynamic),
                    BoidTargetEntity = GetEntity(authoring.BoidTargetPrefab, TransformUsageFlags.Dynamic)
                });
            }
        }
    }

    /// <summary>
    /// Component that stores references to the baked entity prefabs.
    /// Used by systems to spawn new instances of boids, schools, and targets.
    /// </summary>
    public struct EntityLibrary : IComponentData
    {
        public Entity BoidSchoolEntity;
        public Entity BoidEntity;
        public Entity BoidTargetEntity;
    }
}
