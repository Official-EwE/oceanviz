using Unity.Entities;

namespace OceanViz3
{
    /// <summary>
    /// Component holding data required for distance culling.
    /// </summary>
    public struct CullingComponent : IComponentData
    {
        /// <summary>
        /// The maximum distance from the camera before the entity is disabled.
        /// </summary>
        public float MaxDistance;
    }
} 