using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace OceanViz3
{
    /// <summary>
    /// Component containing data for a group of static entities.
    /// Includes references to terrain and mesh habitat data.
    /// </summary>
    [Serializable]
    public struct StaticEntitiesGroupComponent : IComponentData
    {
        /// <summary>
        /// Unique identifier for this static entities group
        /// </summary>
        public int StaticEntitiesGroupId;
        
        /// <summary>
        /// Prototype entity for static entities in this group
        /// </summary>
        public Entity StaticEntityPrototype;
        
        /// <summary>
        /// Flag to indicate if the group should be destroyed
        /// </summary>
        public bool DestroyRequested;
        
        /// <summary>
        /// Requested count of static entities for this group
        /// </summary>
        public int RequestedCount;
        
        /// <summary>
        /// Current count of static entities in this group
        /// </summary>
        public int Count;
        
        /// <summary>
        /// Number of active LOD levels (negative if not yet determined)
        /// </summary>
        public int NumberOfLODs;
        
        /// <summary>
        /// Number of active views
        /// </summary>
        public int ViewsCount;
        
        /// <summary>
        /// View visibility percentages (0-1 for each view)
        /// </summary>
        public float4 ViewVisibilityPercentages;
        
        /// <summary>
        /// Flag to indicate if a shader update is requested
        /// </summary>
        public bool ShaderUpdateRequested;

        /// <summary>
        /// Flag to indicate if spawn data has been prepared
        /// </summary>
        public bool SpawnDataIsReady;
        
        // Terrain-related data
        
        /// <summary>
        /// Terrain size (assume square terrain)
        /// </summary>
        public float TerrainSize;
        
        /// <summary>
        /// Terrain height
        /// </summary>
        public float TerrainHeight;
        
        /// <summary>
        /// Terrain X offset in world space
        /// </summary>
        public float TerrainOffsetX;
        
        /// <summary>
        /// Terrain Y offset in world space
        /// </summary>
        public float TerrainOffsetY;
        
        /// <summary>
        /// Terrain Z offset in world space
        /// </summary>
        public float TerrainOffsetZ;
        
        /// <summary>
        /// Reference to heightmap data blob
        /// </summary>
        public BlobAssetReference<FloatBlob> HeightmapDataBlobRef;
        
        /// <summary>
        /// Heightmap width
        /// </summary>
        public int HeightmapWidth;
        
        /// <summary>
        /// Heightmap height
        /// </summary>
        public int HeightmapHeight;
        
        /// <summary>
        /// Flag to indicate if splatmap should be used for spawning
        /// </summary>
        public bool UseSplatmap;
        
        /// <summary>
        /// Reference to splatmap data blob
        /// </summary>
        public BlobAssetReference<ByteBlob> SplatmapDataBlobRef;
        
        /// <summary>
        /// Splatmap width
        /// </summary>
        public int SplatmapWidth;
        
        /// <summary>
        /// Splatmap height
        /// </summary>
        public int SplatmapHeight;
        
        /// <summary>
        /// Noise offset for this group
        /// </summary>
        public float3 GroupNoiseOffset;
        
        /// <summary>
        /// Noise scale factor
        /// </summary>
        public float NoiseScale;
        
        /// <summary>
        /// Minimum scale for entities
        /// </summary>
        public float MinScale;
        
        /// <summary>
        /// Maximum scale for entities
        /// </summary>
        public float MaxScale;
        
        /// <summary>
        /// Rigidity value for turbulence control (0 = flexible, 1 = rigid)
        /// </summary>
        public float Rigidity;

        /// <summary>
        /// Waves motion strength for shader (0 = no motion, 1 = full motion)
        /// </summary>
        public float WavesMotionStrength;
        
        // Mesh habitat-related data
        
        /// <summary>
        /// Flag to indicate if mesh habitats should be used for spawning
        /// </summary>
        public bool UseMeshHabitats;
        
        /// <summary>
        /// Target distribution ratio between terrain and mesh habitats (0-1)
        /// 0 = all on terrain, 1 = all on mesh habitats (if both are available)
        /// </summary>
        public float MeshHabitatRatio;
    }

    /// <summary>
    /// Buffer element to hold the names of valid habitats for a static entity group.
    /// </summary>
    public struct StaticEntityHabitat : IBufferElementData
    {
        public FixedString64Bytes Name;
    }

    /// <summary>
    /// Buffer element to store references to mesh habitat entities associated with a static entity group
    /// </summary>
    public struct MeshHabitatEntityRef : IBufferElementData
    {
        public Entity MeshEntity;
    }

    /// <summary>
    /// Blob asset for storing float array data (e.g. heightmap)
    /// </summary>
    public struct FloatBlob
    {
        public BlobArray<float> Values;
    }

    /// <summary>
    /// Blob asset for storing byte array data (e.g. splatmap)
    /// </summary>
    public struct ByteBlob
    {
        public BlobArray<byte> Values;
    }
} 