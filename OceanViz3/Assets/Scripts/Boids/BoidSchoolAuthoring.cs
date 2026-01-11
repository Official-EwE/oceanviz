using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using System;
using UnityEngine.Serialization;

namespace OceanViz3
{
    /// <summary>
    /// Authoring component for creating and managing a school of boids.
    /// This component is responsible for setting up the initial configuration of a boid school
    /// and converting it into an ECS entity.
    /// </summary>
    public class BoidSchoolAuthoring : MonoBehaviour
    {
        /// <summary>
        /// The prefab that will be instantiated for each boid in the school
        /// </summary>
        public GameObject DefaultPrefab;
        /// <summary>
        /// The prefab that will be instantiated for the boid target
        /// </summary>
        private BoidTargetAuthoring boidTargetAuthoring;

        private void Awake()
        {
            GameObject boidTargetPrefab = new GameObject("BoidTargetPrefab");
            boidTargetAuthoring = boidTargetPrefab.AddComponent<BoidTargetAuthoring>();
        }

        /// <summary>
        /// Baker class responsible for converting the MonoBehaviour into ECS components
        /// </summary>
        class Baker : Baker<BoidSchoolAuthoring>
        {
            public override void Bake(BoidSchoolAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Renderable);
                AddComponent(entity, new BoidSchoolComponent
                {
                    DynamicEntityId = -1,
                    BoidSchoolId = -1,
                    BoidPrototype = GetEntity(authoring.DefaultPrefab, TransformUsageFlags.Renderable),
                    BoidTargetPrefab = GetEntity(authoring.boidTargetAuthoring, TransformUsageFlags.Dynamic),
                    Count = 0,
                    RequestedCount = -1,
                    DestroyRequested = false,
                    ShaderUpdateRequested = false,
                    NumberOfLODs = -1,
                });
            }
        }
    }

    /// <summary>
    /// Component data structure that holds all the configuration and state for a boid school
    /// </summary>
    public struct BoidSchoolComponent : IComponentData
    {
        #region Main Properties
        /// <summary>Unique identifier for the dynamic entity system</summary>
        public int DynamicEntityId;
        /// <summary>Unique identifier for this boid school</summary>
        public int BoidSchoolId;
        /// <summary>Entity prototype used for spawning individual boids</summary>
        public Entity BoidPrototype;
        /// <summary>Center point of the boid school bounds</summary>
        public float3 BoundsCenter;
        /// <summary>Size of the boid school bounds</summary>
        public float3 BoundsSize;
        /// <summary>Current number of boids in the school</summary>
        public int Count;
        /// <summary>Target number of boids for this school</summary>
        public int RequestedCount;
        /// <summary>Flag indicating if the school should be destroyed</summary>
        public bool DestroyRequested;
        /// <summary>Number of LOD levels available for this boid school</summary>
        public int NumberOfLODs;
        #endregion

        #region Boid Behavior Settings
        /// <summary>Weight of separation behavior (how much boids avoid each other)</summary>
        public float SeparationWeight;
        /// <summary>Weight of alignment behavior (how much boids align with neighbors)</summary>
        public float AlignmentWeight;
        /// <summary>Weight of target-seeking behavior</summary>
        public float TargetWeight;
        /// <summary>Distance at which boids start avoiding obstacles</summary>
        public float ObstacleAversionDistance;
        /// <summary>Movement speed of the boids</summary>
        public float Speed;
        /// <summary>Maximum vertical angle for boid movement</summary>
        public float MaxVerticalAngle;
        /// <summary>Whether boids are bound to the seabed</summary>
        public bool SeabedBound;
        /// <summary>Whether this school consists of predator boids</summary>
        public bool Predator;
        /// <summary>Whether this school consists of prey boids</summary>
        public bool Prey;
        /// <summary>Radius for spatial partitioning cells</summary>
        public float CellRadius;
        /// <summary>Maximum rate at which boids can turn</summary>
        public float MaxTurnRate;
        /// <summary>Speed of transitioning between states</summary>
        public float StateTransitionSpeed;
        /// <summary>Minimum time before state change</summary>
        public float StateChangeTimerMin;
        /// <summary>Maximum time before state change</summary>
        public float StateChangeTimerMax;
        /// <summary>Whether this boid uses bone-based animation instead of shader-based animation</summary>
        public bool BoneAnimated;
        /// <summary>Minimum speed modifier</summary>
        public float SpeedModifierMin;
        /// <summary>Maximum speed modifier</summary>
        public float SpeedModifierMax;
        #endregion

        #region Target Properties
        /// <summary>Prefab for the boid target entity</summary>
        public Entity BoidTargetPrefab;
        /// <summary>Current target entity for the school</summary>
        public Entity Target;
        /// <summary>Timer for target repositioning</summary>
        public float TargetRepositionTimer;
        /// <summary>Deterministic iteration counter for target repositioning</summary>
        public int TargetRepositionIteration;
        #endregion

        #region Shader Properties
        /// <summary>Flag indicating if shader needs updating</summary>
        public bool ShaderUpdateRequested;
        /// <summary>Number of views for this school</summary>
        public int ViewsCount;
        /// <summary>Visibility percentages for different views</summary>
        public float4 ViewVisibilityPercentages;
        #endregion
        
        #region Animation Shader Properties
        /// <summary>Speed of the animation</summary>
        public float AnimationSpeed;
        /// <summary>Wavelength of the sine deformation</summary>
        public float SineWavelength;
        /// <summary>Amplitude of the sine deformation</summary>
        public float3 SineDeformationAmplitude;
        /// <summary>Amplitude of the first secondary animation</summary>
        public float Secondary1AnimationAmplitude;
        /// <summary>Whether to invert the first secondary animation</summary>
        public float InvertSecondary1Animation;
        /// <summary>Amplitude of the second secondary animation</summary>
        public float3 Secondary2AnimationAmplitude;
        /// <summary>Whether to invert the second secondary animation</summary>
        public float InvertSecondary2Animation;
        /// <summary>Amplitude of side-to-side movement</summary>
        public float3 SideToSideAmplitude;
        /// <summary>Amplitude of yaw movement</summary>
        public float3 YawAmplitude;
        /// <summary>Amplitude of rolling spine movement</summary>
        public float3 RollingSpineAmplitude;
        /// <summary>Minimum Z coordinate of the mesh</summary>
        public float MeshZMin;
        /// <summary>Maximum Z coordinate of the mesh</summary>
        public float MeshZMax;
        /// <summary>Positive Y clipping value</summary>
        public float PositiveYClip;
        /// <summary>Negative Y clipping value</summary>
        public float NegativeYClip;
        
        // Base mesh size info (from source mesh, no per-instance scaling)
        public float3 MeshSize;
        public float MeshLargestDimension;
        #endregion
    }
}
