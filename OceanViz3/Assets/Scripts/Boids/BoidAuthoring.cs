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
    /// Authoring component for boid entities in the ECS system.
    /// Handles the initial setup and baking of boid properties and material overrides.
    /// </summary>
    public class BoidAuthoring : MonoBehaviour
    {
        public float DefaultCellRadius = 8.0f;
        public float DefaultSeparationWeight = 1.0f;
        public float DefaultAlignmentWeight = 1.0f;
        public float DefaultTargetWeight = 1.0f;
        public float DefaultObstacleAversionDistance = 1.0f;
        public float DefaultMoveSpeed = 0.1f;

        /// <summary>
        /// Baker class that converts the authoring MonoBehaviour into ECS components.
        /// </summary>
        class Baker : Baker<BoidAuthoring>
        {
            public override void Bake(BoidAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddSharedComponent(entity, new BoidShared
                {
                    DynamicEntityId = -1,
                    CellRadius = authoring.DefaultCellRadius,
                    SeparationWeight = authoring.DefaultSeparationWeight,
                    AlignmentWeight = authoring.DefaultAlignmentWeight,
                    TargetWeight = authoring.DefaultTargetWeight,
                    ObstacleAversionDistance = authoring.DefaultObstacleAversionDistance,
                    DefaultMoveSpeed = authoring.DefaultMoveSpeed,
                    BoneAnimated = false,
                });
                AddComponent(entity, new BoidUnique
                {
                    Disabled = false,
                    MoveSpeedModifier = 1.0f,
                    TargetSpeedModifier = 1.0f,
                    TargetVector = new float3(0,0,0),
                    PreviousHeading = new float3(0,0,0),
                });

                // Material overrides
                AddComponent(entity, new ScreenDisplayStartOverride { Value = new float4(0, 0, 0, 0) });
                AddComponent(entity, new ScreenDisplayEndOverride { Value = new float4(0, 0, 0, 0) });
                AddComponent(entity, new MetalnessOverride { Value = 0.0f });
                AddComponent(entity, new AnimationRandomOffsetOverride { Value = 0.0f });
                AddComponent(entity, new AnimationSpeedOverride { Value = 1.0f });
                AddComponent(entity, new SineWavelengthOverride { Value = 1.0f });
                AddComponent(entity, new SineDeformationAmplitudeOverride { Value = new float3(0, 0, 0) });
                AddComponent(entity, new Secondary1AnimationAmplitudeOverride { Value = 0.0f });
                AddComponent(entity, new InvertSecondary1AnimationOverride { Value = 0.0f });
                AddComponent(entity, new Secondary2AnimationAmplitudeOverride { Value = new float3(0, 0, 0) });
                AddComponent(entity, new InvertSecondary2AnimationOverride { Value = 0.0f });
                AddComponent(entity, new SideToSideAmplitudeOverride { Value = new float3(0, 0, 0) });
                AddComponent(entity, new YawAmplitudeOverride { Value = new float3(0, 0, 0) });
                AddComponent(entity, new RollingSpineAmplitudeOverride { Value = new float3(0, 0, 0) });
                AddComponent(entity, new CurrentVectorOverride { Value = new float3(0, 0, 0) });
                AddComponent(entity, new AccumulatedTimeOverride { Value = 0f });
                AddComponent(entity, new MeshZMinOverride { Value = 0f });
                AddComponent(entity, new MeshZMaxOverride { Value = 0f });
                AddComponent(entity, new PositiveYClipOverride { Value = 0f });
                AddComponent(entity, new NegativeYClipOverride { Value = 0f });

                // Add CullingComponent
                AddComponent(entity, new CullingComponent { MaxDistance = 70.0f }); // 70
            }
        }
    }

    /// <summary>
    /// Shared component containing settings that apply to an entire group of boids.
    /// </summary>
    [Serializable]
    [WriteGroup(typeof(LocalToWorld))]
    public struct BoidShared : ISharedComponentData
    {
        /// <summary>
        /// Unique identifier for the dynamic entity group this boid belongs to
        /// </summary>
        public int DynamicEntityId;
        
        /// <summary>
        /// Identifier for the school/group this boid belongs to
        /// </summary>
        public int BoidSchoolId;
        
        /// <summary>
        /// Radius used for spatial partitioning and neighbor detection
        /// </summary>
        public float CellRadius;
        
        /// <summary>
        /// Maximum bounds for boid movement
        /// </summary>
        public float3 BoundsMax;
        
        /// <summary>
        /// Minimum bounds for boid movement
        /// </summary>
        public float3 BoundsMin;

        /// <summary>
        /// Weight factor controlling how strongly boids separate from nearby neighbors
        /// </summary>
        public float SeparationWeight;

        /// <summary>
        /// Weight factor controlling how strongly boids align their movement with neighbors
        /// </summary>
        public float AlignmentWeight;

        /// <summary>
        /// Weight factor controlling how strongly boids move toward their target position
        /// </summary>
        public float TargetWeight;

        /// <summary>
        /// Distance at which boids start avoiding obstacles in their path
        /// </summary>
        public float ObstacleAversionDistance;

        /// <summary>
        /// Base movement speed when no modifiers are applied
        /// </summary>
        public float DefaultMoveSpeed;

        /// <summary>
        /// Base animation speed when no modifiers are applied
        /// </summary>
        public float DefaultAnimationSpeed;

        /// <summary>
        /// Maximum angle in degrees that boids can pitch up or down
        /// </summary>
        public float MaxVerticalAngle;

        /// <summary>
        /// Maximum rate at which boids can turn, with 1.0 being the default turn rate
        /// </summary>
        public float MaxTurnRate;

        /// <summary>
        /// If true, boid will maintain minimum distance from seabed
        /// </summary>
        public bool SeabedBound;

        /// <summary>
        /// Marks this boid as a predator, affecting its interaction with prey boids
        /// </summary>
        public bool Predator;

        /// <summary>
        /// Marks this boid as prey, affecting its interaction with predator boids
        /// </summary>
        public bool Prey;
        
        /// <summary>
        /// Speed at which boids transition between different behavioral states
        /// </summary>
        public float StateTransitionSpeed;

        /// <summary>
        /// Minimum time before a boid can change its behavioral state
        /// </summary>
        public float StateChangeTimerMin;

        /// <summary>
        /// Maximum time before a boid must change its behavioral state
        /// </summary>
        public float StateChangeTimerMax;

        /// <summary>
        /// If true, this boid uses bone-based animation instead of shader-based animation
        /// </summary>
        public bool BoneAnimated;

        /// <summary>
        /// Number of LOD levels available for this boid type
        /// </summary>
        public int NumberOfLODs;

        /// <summary>
        /// Minimum speed modifier for boid movement.
        /// </summary>
        public float SpeedModifierMin;

        /// <summary>
        /// Maximum speed modifier for boid movement.
        /// </summary>
        public float SpeedModifierMax;

        // Base mesh size info (from source mesh, no per-instance scaling)
        public float3 MeshSize;
        public float MeshLargestDimension;

        
    }
    
    /// <summary>
    /// Component containing per-boid instance data and behavior settings.
    /// </summary>
    [Serializable]
    public struct BoidUnique : IComponentData, IEnableableComponent
    {
        /// <summary>
        /// Whether this boid's behavior is currently disabled
        /// </summary>
        public bool Disabled;
        
        /// <summary>
        /// Current modifier affecting both movement and animation speed
        /// </summary>
        public float MoveSpeedModifier;
        
        /// <summary>
        /// Target speed modifier that MoveSpeedModifier will lerp towards
        /// </summary>
        public float TargetSpeedModifier;
        
        /// <summary>
        /// Target direction vector set by BoidSystem each frame
        /// </summary>
        public float3 TargetVector;
        
        /// <summary>
        /// Previous frame's heading vector, used for calculating the next TargetVector
        /// </summary>
        public float3 PreviousHeading;

            /// <summary>
            /// Smoothed reference heading used only for stable bend calculation.
            /// </summary>
            public float3 BendRefHeading;
    }    
    
    /// <summary>
    /// Component used for querying boids by their predator status
    /// </summary>
    [Serializable]
    public struct BoidPredator : IComponentData
    {
    }
    
    /// <summary>
    /// Component used for querying boids by their prey status
    /// </summary>
    [Serializable]
    public struct BoidPrey : IComponentData
    {
    }

    /// <summary>
    /// Tag component indicating a prey boid is currently escaping from a predator
    /// </summary>
    [Serializable]
    public struct EscapingPredator : IComponentData, IEnableableComponent
    {
    }

    #region Material Property Overrides

    /// <summary>
    /// Controls the screen-space display start position for the boid.
    /// Used for fade-in/fade-out effects based on screen position.
    /// Material Property: _ScreenDisplayStart
    /// </summary>
    [MaterialProperty("_ScreenDisplayStart")]
    public struct ScreenDisplayStartOverride : IComponentData
    {
        public float4 Value;
    }

    /// <summary>
    /// Controls the screen-space display end position for the boid.
    /// Used for fade-in/fade-out effects based on screen position.
    /// Material Property: _ScreenDisplayEnd
    /// </summary>
    [MaterialProperty("_ScreenDisplayEnd")]
    public struct ScreenDisplayEndOverride : IComponentData
    {
        public float4 Value;
    }

    /// <summary>
    /// Controls the metallic material property of the boid's shader.
    /// Material Property: _Metalness
    /// Range: 0-1
    /// </summary>
    [MaterialProperty("_Metalness")]
    public struct MetalnessOverride : IComponentData
    {
        public float Value;
    }

    /// <summary>
    /// Random offset applied to animation timing to prevent synchronized animations.
    /// Material Property: _AnimationRandomOffset
    /// </summary>
    [MaterialProperty("_AnimationRandomOffset")]
    public struct AnimationRandomOffsetOverride : IComponentData
    {
        public float Value;
    }

    /// <summary>
    /// Controls the animation speed of the boid's shader.
    /// Material Property: _AnimationSpeed
    /// </summary>
    [MaterialProperty("_AnimationSpeed")]
    public struct AnimationSpeedOverride : IComponentData
    {
        public float Value;
    }

    /// <summary>
    /// Controls the wavelength of the sine wave used in the boid's shader.
    /// Material Property: _SineWavelength
    /// </summary>
    [MaterialProperty("_SineWavelength")]
    public struct SineWavelengthOverride : IComponentData
    {
        public float Value;
    }

    /// <summary>
    /// Controls the amplitude of the sine wave used in the boid's shader.
    /// Material Property: _SineDeformationAmplitude
    /// </summary>
    [MaterialProperty("_SineDeformationAmplitude")]
    public struct SineDeformationAmplitudeOverride : IComponentData
    {
        public float3 Value;
    }

    /// <summary>
    /// Controls the amplitude of the secondary animation used in the boid's shader.
    /// Material Property: _Secondary1AnimationAmplitude
    /// </summary>
    [MaterialProperty("_Secondary1AnimationAmplitude")]
    public struct Secondary1AnimationAmplitudeOverride : IComponentData
    {
        public float Value;
    }

    /// <summary>
    /// Controls the inversion of the secondary animation used in the boid's shader.
    /// Material Property: _InvertSecondary1Animation
    /// </summary>
    [MaterialProperty("_InvertSecondary1Animation")]
    public struct InvertSecondary1AnimationOverride : IComponentData
    {
        public float Value;
    }

    /// <summary>
    /// Controls the amplitude of the secondary animation used in the boid's shader.
    /// Material Property: _Secondary2AnimationAmplitude
    /// </summary>
    [MaterialProperty("_Secondary2AnimationAmplitude")]
    public struct Secondary2AnimationAmplitudeOverride : IComponentData
    {
        public float3 Value;
    }

    /// <summary>
    /// Controls the inversion of the secondary animation used in the boid's shader.
    /// Material Property: _InvertSecondary2Animation
    /// </summary>
    [MaterialProperty("_InvertSecondary2Animation")]
    public struct InvertSecondary2AnimationOverride : IComponentData
    {
        public float Value;
    }

    /// <summary>
    /// Controls the amplitude of the side-to-side movement used in the boid's shader.
    /// Material Property: _SideToSideAmplitude
    /// </summary>
    [MaterialProperty("_SideToSideAmplitude")]
    public struct SideToSideAmplitudeOverride : IComponentData
    {
        public float3 Value;
    }

    /// <summary>
    /// Controls the amplitude of the yaw movement used in the boid's shader.
    /// Material Property: _YawAmplitude
    /// </summary>
    [MaterialProperty("_YawAmplitude")]
    public struct YawAmplitudeOverride : IComponentData
    {
        public float3 Value;
    }

    /// <summary>
    /// Controls the amplitude of the rolling spine used in the boid's shader.
    /// Material Property: _RollingSpineAmplitude
    /// </summary>
    [MaterialProperty("_RollingSpineAmplitude")]
    public struct RollingSpineAmplitudeOverride : IComponentData
    {
        public float3 Value;
    }    
    
    /// <summary>
    /// Controls the current vector used in the boid's shader.
    /// Material Property: _CurrentVector
    /// </summary>
    [MaterialProperty("_CurrentVector")]
    public struct CurrentVectorOverride : IComponentData
    {
        public float3 Value;
    }

    /// <summary>
    /// Controls the accumulated time used in the boid's shader. AccumulatedTime is used to animate the boid's shader.
    /// Material Property: _AccumulatedTime
    /// </summary>
    [MaterialProperty("_AccumulatedTime")]
    public struct AccumulatedTimeOverride : IComponentData
    {
        public float Value;
    }
    
    /// <summary>
    /// Controls the minimum Z value of the mesh used in the boid's shader.
    /// Material Property: _MeshZMin
    /// </summary>
    [MaterialProperty("_MeshZMin")]
    public struct MeshZMinOverride : IComponentData
    {
        public float Value;
    }
    
    /// <summary>
    /// Controls the maximum Z value of the mesh used in the boid's shader.
    /// Material Property: _MeshZMax
    /// </summary>
    [MaterialProperty("_MeshZMax")]
    public struct MeshZMaxOverride : IComponentData
    {
        public float Value;
    }
    
    /// <summary>
    /// Controls the positive Y clip value used in the boid's shader.
    /// Material Property: _PositiveYClip
    /// </summary>
    [MaterialProperty("_PositiveYClip")]
    public struct PositiveYClipOverride : IComponentData
    {
        public float Value;
    }
    
    /// <summary>
    /// Controls the negative Y clip value used in the boid's shader.
    /// Material Property: _NegativeYClip
    /// </summary>
    [MaterialProperty("_NegativeYClip")]
    public struct NegativeYClipOverride : IComponentData
    {
        public float Value;
    }

    #endregion

}
