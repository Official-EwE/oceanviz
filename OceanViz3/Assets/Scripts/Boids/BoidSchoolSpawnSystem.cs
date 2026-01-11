using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Profiling;
using Unity.Rendering;

namespace OceanViz3
{
    /// <summary>
    /// System responsible for managing boid schools and their member boids.
    /// Boid schools are entities that manage the spawning and destruction of boids which are members of a single DynamicEntityGroup.
    /// </summary>
    [RequireMatchingQueriesForUpdate]
    // [BurstCompile] RandomGenerator is not supported in Burst
    public partial struct BoidSchoolSpawnSystem : ISystem
    {
        private int previousStaticGroupsCount;
        /// <summary>
        /// Main update loop that processes all boid schools and their members.
        /// Handles:
        /// - School/boid destruction when requested
        /// - Target entity management
        /// - Boid spawning and destroying to match requested counts
        /// - Shader property updates
        /// - Target repositioning and speed randomization
        /// </summary>
        public void OnUpdate(ref SystemState state)
        {
            var localToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>();
            var entityCommandBuffer = new EntityCommandBuffer(Allocator.Temp);
            var world = state.World.Unmanaged;
            float deltaTime = SystemAPI.Time.DeltaTime;

            // Reset deterministic sequences when there are no static entity groups
            var staticGroupsQuery = SystemAPI.QueryBuilder().WithAll<StaticEntitiesGroupComponent>().Build();
            int staticGroupsCount = staticGroupsQuery.CalculateEntityCount();
            if (previousStaticGroupsCount > 0 && staticGroupsCount == 0)
            {
                foreach (var (schoolRO, schoolEntity) in SystemAPI.Query<RefRO<BoidSchoolComponent>>().WithEntityAccess())
                {
                    var school = schoolRO.ValueRO;
                    school.TargetRepositionIteration = 0;
                    school.TargetRepositionTimer = 0f; // force immediate reposition after reset
                    entityCommandBuffer.SetComponent(schoolEntity, school);
                }
            }
            previousStaticGroupsCount = staticGroupsCount;

            // Iterate over all BoidSchool entities
            foreach (var (boidSchool, boidSchoolLocalToWorld, boidSchoolEntity) in
                     SystemAPI.Query<RefRO<BoidSchoolComponent>, RefRO<LocalToWorld>>()
                         .WithEntityAccess())
            {
                // School + boids destroy requested 
                if (boidSchool.ValueRO.DestroyRequested == true)
                {
                    // Destroy target
                    if (boidSchool.ValueRO.Target != Entity.Null)
                    {
                        Entity targetEntity = boidSchool.ValueRO.Target;
                        entityCommandBuffer.DestroyEntity(targetEntity);
                    }
                    
                    // Destroy boids
                    EntityQuery boidQuery = SystemAPI.QueryBuilder().WithAll<BoidShared>().WithAll<LocalToWorld>().WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build();
                    state.EntityManager.GetAllUniqueSharedComponents(out NativeList<BoidShared> uniqueBoidTypes, world.UpdateAllocator.ToAllocator);

                    var boidEntities = boidQuery.ToEntityArray(Allocator.TempJob);
                    foreach (Entity boidEntity in boidEntities)
                    {
                        BoidShared boidShared = state.EntityManager.GetSharedComponentManaged<BoidShared>(boidEntity);
                        if (boidShared.DynamicEntityId == boidSchool.ValueRO.DynamicEntityId && boidShared.BoidSchoolId == boidSchool.ValueRO.BoidSchoolId)
                        {
                            entityCommandBuffer.DestroyEntity(boidEntity);
                        }
                    }
                    boidEntities.Dispose();
                    
                    // Destroy BoidSchool entity
                    entityCommandBuffer.DestroyEntity(boidSchoolEntity);
                    
                    // Done with this BoidSchool
                    continue;
                }
                
                var boidSchoolCopy = boidSchool.ValueRO; // We will modify the BoidSchool component data, and then set it back to the entity at the end

                // Mandatory prerequisites
                //If no target spawned, spawn a target using TargetAuthoring, and set the target's BoidSchoolId and DynamicEntityId
                if (boidSchool.ValueRO.Target == Entity.Null)
                {
                    // Instantiate the target from the BoidSchool's BoidTargetPrefab and set its BoidTarget's BoidSchoolId to the BoidSchool's Id
                    Entity targetEntity = entityCommandBuffer.Instantiate(boidSchool.ValueRO.BoidTargetPrefab);
                    entityCommandBuffer.SetComponent(targetEntity, new BoidTarget
                    {
                        BoidSchoolId = boidSchool.ValueRO.BoidSchoolId,
                        DynamicEntityId = boidSchool.ValueRO.DynamicEntityId
                    });
                    
                    // Set the name of the target including the BoidSchoolId and DynamicEntityId
                    entityCommandBuffer.SetName(targetEntity, "BoidTarget_" + boidSchool.ValueRO.DynamicEntityId + "_" + boidSchool.ValueRO.BoidSchoolId);

                    // Set the target's LocalToWorld position deterministically within the bounds
                    var pos = GenerateDeterministicPositionWithinBounds(boidSchool.ValueRO.BoundsCenter, boidSchool.ValueRO.BoundsSize, (uint)boidSchool.ValueRO.DynamicEntityId, (uint)boidSchool.ValueRO.BoidSchoolId, 0u);
                    entityCommandBuffer.SetComponent(targetEntity, new LocalTransform
                    {
                        Position = pos,
                        Rotation = quaternion.identity,
                        Scale = 1.0f
                    });

                    // Set the boidSchool's Target to the targetEntity
                    boidSchoolCopy.Target = targetEntity;
                }
                // Mandatory prerequisites completed, manage boids
                else
                {
                    // Boid spawning/destroying
                    // Check if there is a difference between the BoidSchool's Count and the number of Boids with the BoidSchoolId
                    if (boidSchool.ValueRO.Count != boidSchool.ValueRO.RequestedCount)
                    {
                        // If the BoidSchool's Count is less than the requested count, instantiate the difference
                        if (boidSchool.ValueRO.RequestedCount > boidSchool.ValueRO.Count)
                        {
                            var amountToInstantiate = boidSchool.ValueRO.RequestedCount - boidSchool.ValueRO.Count;

                            // Create a native array of entities to hold the boids
                            var boidEntities =
                                CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(amountToInstantiate,
                                    ref world.UpdateAllocator); 

                            // Instantiate the boids
                            state.EntityManager.Instantiate(boidSchool.ValueRO.BoidPrototype, boidEntities);
                            
                            // Set up MaterialMeshInfo for each new boid
                            for (int i = 0; i < boidEntities.Length; i++)
                            {
                                // Get the RenderMeshArray from the prototype
                                var renderMeshArray = state.EntityManager.GetSharedComponentManaged<RenderMeshArray>(boidSchool.ValueRO.BoidPrototype);
                                entityCommandBuffer.SetSharedComponentManaged(boidEntities[i], renderMeshArray);
                                
                                // Initialize with LOD0
                                var materialMeshInfo = MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0);
                                entityCommandBuffer.SetComponent(boidEntities[i], materialMeshInfo);
                            }
                            
                            // If bone animated, add DisableRendering component
                            if (boidSchool.ValueRO.BoneAnimated)
                            {
                                for (int i = 0; i < boidEntities.Length; i++)
                                {
                                    entityCommandBuffer.AddComponent<DisableRendering>(boidEntities[i]);
                                }
                            }
                            
                            // If boid.Predator is true, use the command buffer to add the BoidPredator component to boidEntities. Command buffer is mandatory for structural changes
                            if (boidSchool.ValueRO.Predator)
                            {
                                for (int i = 0; i < boidEntities.Length; i++)
                                {
                                    entityCommandBuffer.AddComponent<BoidPredator>(boidEntities[i]);
                                }
                            }
                            
                            // If boid.Prey is true, use the command buffer to add the BoidPrey component to boidEntities. Command buffer is mandatory for structural changes
                            if (boidSchool.ValueRO.Prey)
                            {
                                for (int i = 0; i < boidEntities.Length; i++)
                                {
                                    entityCommandBuffer.AddComponent<BoidPrey>(boidEntities[i]);
                                    // Add EscapingPredator component (disabled by default) for prey boids
                                    entityCommandBuffer.AddComponent<EscapingPredator>(boidEntities[i]);
                                    entityCommandBuffer.SetComponentEnabled<EscapingPredator>(boidEntities[i], false);
                                }
                            }
                            
                            for (int i = 0; i < boidEntities.Length; i++)
                            {
                                // Set the name of the boids adding the group id and the school id
                                entityCommandBuffer.SetName(boidEntities[i], "Boid_" + boidSchool.ValueRO.DynamicEntityId + "_" + boidSchool.ValueRO.BoidSchoolId + "_" + i);

                                BoidShared boidShared = new BoidShared
                                {
                                    DynamicEntityId = boidSchool.ValueRO.DynamicEntityId,
                                    BoidSchoolId = boidSchool.ValueRO.BoidSchoolId,
                                    BoundsMax = boidSchool.ValueRO.BoundsCenter + (boidSchool.ValueRO.BoundsSize * 0.5f),
                                    BoundsMin = boidSchool.ValueRO.BoundsCenter - (boidSchool.ValueRO.BoundsSize * 0.5f),
                                    DefaultMoveSpeed = boidSchool.ValueRO.Speed,
                                    MaxVerticalAngle = boidSchool.ValueRO.MaxVerticalAngle,
                                    MaxTurnRate = boidSchool.ValueRO.MaxTurnRate,
                                    DefaultAnimationSpeed = boidSchool.ValueRO.AnimationSpeed,
                                    Predator = boidSchool.ValueRO.Predator,
                                    Prey = boidSchool.ValueRO.Prey,
                                    CellRadius = boidSchool.ValueRO.CellRadius,
                                    SeparationWeight = boidSchool.ValueRO.SeparationWeight,
                                    AlignmentWeight = boidSchool.ValueRO.AlignmentWeight,
                                    TargetWeight = boidSchool.ValueRO.TargetWeight,
                                    ObstacleAversionDistance = boidSchool.ValueRO.ObstacleAversionDistance,
                                    SeabedBound = boidSchool.ValueRO.SeabedBound,
                                    StateTransitionSpeed = boidSchool.ValueRO.StateTransitionSpeed,
                                    StateChangeTimerMin = boidSchool.ValueRO.StateChangeTimerMin,
                                    StateChangeTimerMax = boidSchool.ValueRO.StateChangeTimerMax,
                                    BoneAnimated = boidSchool.ValueRO.BoneAnimated,
                                    NumberOfLODs = boidSchool.ValueRO.NumberOfLODs,
                                    SpeedModifierMin = boidSchool.ValueRO.SpeedModifierMin,
                                    SpeedModifierMax = boidSchool.ValueRO.SpeedModifierMax,
                                    // Mesh dimensions
                                    MeshSize = boidSchool.ValueRO.MeshSize,
                                    MeshLargestDimension = boidSchool.ValueRO.MeshLargestDimension,
                                };

                                entityCommandBuffer.SetSharedComponentManaged(boidEntities[i], boidShared);
                                
                                // Unique
                                BoidUnique boidUnique = new BoidUnique
                                {
                                    Disabled = false,
                                    MoveSpeedModifier = 1.0f,
                                    TargetSpeedModifier = 1.0f,
                                    TargetVector = new float3(0, 0, 0),
                                    PreviousHeading = new float3(0, 0, 0),
                                };
                                entityCommandBuffer.SetComponent(boidEntities[i], boidUnique);

                                //// Shader overrides
                                
                                // CurrentVectorOverride
                                CurrentVectorOverride currentVectorOverride = state.EntityManager.GetComponentData<CurrentVectorOverride>(boidEntities[i]);
                                currentVectorOverride.Value = new float3(0, 0, 0);
                                entityCommandBuffer.SetComponent(boidEntities[i], currentVectorOverride);
                                
                                // AccumulatedTimeOverride
                                AccumulatedTimeOverride accumulatedTimeOverride = state.EntityManager.GetComponentData<AccumulatedTimeOverride>(boidEntities[i]);
                                accumulatedTimeOverride.Value = 0f;
                                entityCommandBuffer.SetComponent(boidEntities[i], accumulatedTimeOverride);
                                
                                // Set MeshZMin and MeshZMax
                                MeshZMinOverride meshZMinOverride = state.EntityManager.GetComponentData<MeshZMinOverride>(boidEntities[i]);
                                meshZMinOverride.Value = boidSchool.ValueRO.MeshZMin;
                                entityCommandBuffer.SetComponent(boidEntities[i], meshZMinOverride);
                                
                                MeshZMaxOverride meshZMaxOverride = state.EntityManager.GetComponentData<MeshZMaxOverride>(boidEntities[i]);
                                meshZMaxOverride.Value = boidSchool.ValueRO.MeshZMax;
                                entityCommandBuffer.SetComponent(boidEntities[i], meshZMaxOverride);
                                
                                AnimationSpeedOverride animationSpeedOverride = state.EntityManager.GetComponentData<AnimationSpeedOverride>(boidEntities[i]);
                                animationSpeedOverride.Value = boidSchool.ValueRO.AnimationSpeed;
                                entityCommandBuffer.SetComponent(boidEntities[i], animationSpeedOverride);
                                
                                SineWavelengthOverride sineWavelengthOverride = state.EntityManager.GetComponentData<SineWavelengthOverride>(boidEntities[i]);
                                sineWavelengthOverride.Value = boidSchool.ValueRO.SineWavelength;
                                entityCommandBuffer.SetComponent(boidEntities[i], sineWavelengthOverride);
                                
                                SineDeformationAmplitudeOverride sineDeformationAmplitudeOverride = state.EntityManager.GetComponentData<SineDeformationAmplitudeOverride>(boidEntities[i]);
                                sineDeformationAmplitudeOverride.Value = new float3(
                                    boidSchool.ValueRO.SineDeformationAmplitude.x,
                                    boidSchool.ValueRO.SineDeformationAmplitude.y,
                                    boidSchool.ValueRO.SineDeformationAmplitude.z
                                );
                                entityCommandBuffer.SetComponent(boidEntities[i], sineDeformationAmplitudeOverride);
                                
                                Secondary1AnimationAmplitudeOverride secondary1AnimationAmplitudeOverride = state.EntityManager.GetComponentData<Secondary1AnimationAmplitudeOverride>(boidEntities[i]);
                                secondary1AnimationAmplitudeOverride.Value = boidSchool.ValueRO.Secondary1AnimationAmplitude;
                                entityCommandBuffer.SetComponent(boidEntities[i], secondary1AnimationAmplitudeOverride);
                                
                                InvertSecondary1AnimationOverride invertSecondary1AnimationOverride = state.EntityManager.GetComponentData<InvertSecondary1AnimationOverride>(boidEntities[i]);
                                invertSecondary1AnimationOverride.Value = boidSchool.ValueRO.InvertSecondary1Animation;
                                entityCommandBuffer.SetComponent(boidEntities[i], invertSecondary1AnimationOverride);
                                
                                Secondary2AnimationAmplitudeOverride secondary2AnimationAmplitudeOverride = state.EntityManager.GetComponentData<Secondary2AnimationAmplitudeOverride>(boidEntities[i]);
                                secondary2AnimationAmplitudeOverride.Value = new float3(
                                    boidSchool.ValueRO.Secondary2AnimationAmplitude.x,
                                    boidSchool.ValueRO.Secondary2AnimationAmplitude.y,
                                    boidSchool.ValueRO.Secondary2AnimationAmplitude.z
                                );
                                entityCommandBuffer.SetComponent(boidEntities[i], secondary2AnimationAmplitudeOverride);
                                
                                InvertSecondary2AnimationOverride invertSecondary2AnimationOverride = state.EntityManager.GetComponentData<InvertSecondary2AnimationOverride>(boidEntities[i]);
                                invertSecondary2AnimationOverride.Value = boidSchool.ValueRO.InvertSecondary2Animation;
                                entityCommandBuffer.SetComponent(boidEntities[i], invertSecondary2AnimationOverride);
                                
                                SideToSideAmplitudeOverride sideToSideAmplitudeOverride = state.EntityManager.GetComponentData<SideToSideAmplitudeOverride>(boidEntities[i]);
                                sideToSideAmplitudeOverride.Value = new float3(
                                    boidSchool.ValueRO.SideToSideAmplitude.x,
                                    boidSchool.ValueRO.SideToSideAmplitude.y,
                                    boidSchool.ValueRO.SideToSideAmplitude.z
                                );
                                entityCommandBuffer.SetComponent(boidEntities[i], sideToSideAmplitudeOverride);
                                
                                YawAmplitudeOverride yawAmplitudeOverride = state.EntityManager.GetComponentData<YawAmplitudeOverride>(boidEntities[i]);
                                yawAmplitudeOverride.Value = new float3(
                                    boidSchool.ValueRO.YawAmplitude.x,
                                    boidSchool.ValueRO.YawAmplitude.y,
                                    boidSchool.ValueRO.YawAmplitude.z
                                );
                                entityCommandBuffer.SetComponent(boidEntities[i], yawAmplitudeOverride);
                                
                                RollingSpineAmplitudeOverride rollingSpineAmplitudeOverride = state.EntityManager.GetComponentData<RollingSpineAmplitudeOverride>(boidEntities[i]);
                                rollingSpineAmplitudeOverride.Value = new float3(
                                    boidSchool.ValueRO.RollingSpineAmplitude.x,
                                    boidSchool.ValueRO.RollingSpineAmplitude.y,
                                    boidSchool.ValueRO.RollingSpineAmplitude.z
                                );
                                entityCommandBuffer.SetComponent(boidEntities[i], rollingSpineAmplitudeOverride);
                                
                                PositiveYClipOverride positiveYClipOverride = state.EntityManager.GetComponentData<PositiveYClipOverride>(boidEntities[i]);
                                positiveYClipOverride.Value = boidSchool.ValueRO.PositiveYClip;
                                entityCommandBuffer.SetComponent(boidEntities[i], positiveYClipOverride);
                                
                                NegativeYClipOverride negativeYClipOverride = state.EntityManager.GetComponentData<NegativeYClipOverride>(boidEntities[i]);
                                negativeYClipOverride.Value = boidSchool.ValueRO.PositiveYClip;
                                entityCommandBuffer.SetComponent(boidEntities[i], negativeYClipOverride);
                                
                                // Set the AnimationRandomOffsetOverride to a random value between 0.0f and 1.0f
                                AnimationRandomOffsetOverride animationRandomOffsetOverride = state.EntityManager.GetComponentData<AnimationRandomOffsetOverride>(boidEntities[i]);
                                uint animationSeed = math.hash(new uint4((uint)boidSchool.ValueRO.DynamicEntityId, (uint)boidSchool.ValueRO.BoidSchoolId, (uint)i, 0u));
                                var animationRng = Unity.Mathematics.Random.CreateFromIndex(animationSeed);
                                animationRandomOffsetOverride.Value = animationRng.NextFloat(-100.0f, 100.0f);
                                entityCommandBuffer.SetComponent(boidEntities[i], animationRandomOffsetOverride);
                            }

                            // Place the boids
                            var setBoidLocalToWorldJob = new SetBoidLocalToWorld 
                            {
                                LocalToWorldFromEntity = localToWorldLookup,
                                Entities = boidEntities,
                                Bounds = new AABB
                                {
                                    Center = boidSchool.ValueRO.BoundsCenter,
                                    Extents = boidSchool.ValueRO.BoundsSize * 0.5f // Extents is half the size
                                },
                                DynamicEntityId = boidSchool.ValueRO.DynamicEntityId,
                                BoidSchoolId = boidSchool.ValueRO.BoidSchoolId

                            };
                            state.Dependency = setBoidLocalToWorldJob.Schedule(amountToInstantiate, 64, state.Dependency);
                            state.Dependency.Complete();

                            // Set the boidSchool Count to the RequestedCount
                            boidSchoolCopy.Count = boidSchool.ValueRO.RequestedCount;
                        }

                        // If the BoidSchool's Count is more than the number of Boids with the BoidSchoolId, destroy excess
                        else if (boidSchool.ValueRO.RequestedCount < boidSchool.ValueRO.Count)
                        {
                            int entitiesToDestroy = boidSchool.ValueRO.Count - boidSchool.ValueRO.RequestedCount;
                            
                            EntityQuery boidQuery = SystemAPI.QueryBuilder().WithAll<BoidShared>().WithAll<LocalToWorld>().WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build();
                            state.EntityManager.GetAllUniqueSharedComponents(out NativeList<BoidShared> uniqueBoidTypes, world.UpdateAllocator.ToAllocator);

                            var boidEntities = boidQuery.ToEntityArray(Allocator.TempJob);
                            foreach (Entity boidEntity in boidEntities)
                            {
                                BoidShared boidShared = state.EntityManager.GetSharedComponentManaged<BoidShared>(boidEntity);
                                if (boidShared.DynamicEntityId == boidSchool.ValueRO.DynamicEntityId && boidShared.BoidSchoolId == boidSchool.ValueRO.BoidSchoolId)
                                {
                                    entityCommandBuffer.DestroyEntity(boidEntity);
                                    entitiesToDestroy--;
                                }
                                if (entitiesToDestroy <= 0)
                                {
                                    break;
                                }
                            }
                            boidEntities.Dispose();

                            // Set the boidSchool Count to the RequestedCount
                            boidSchoolCopy.Count = boidSchool.ValueRO.RequestedCount;
                        }
                    }
                    // Shader overrides
                    else if (boidSchool.ValueRO.ShaderUpdateRequested == true)
                    {
                        // All boids (from all schools)
                        EntityQuery boidQuery = SystemAPI.QueryBuilder().WithAll<BoidShared>().WithAll<LocalToWorld>().WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build();
                        state.EntityManager.GetAllUniqueSharedComponents(out NativeList<BoidShared> uniqueBoidTypes, world.UpdateAllocator.ToAllocator);
                        NativeArray<Entity> boidEntities = boidQuery.ToEntityArray(Allocator.TempJob);
                        
                        // Count total from our school
                        int boidTotal = 0;
                        foreach (Entity boidEntity in boidEntities)
                        {
                            BoidShared boidShared = state.EntityManager.GetSharedComponentManaged<BoidShared>(boidEntity);
                            if (boidShared.DynamicEntityId == boidSchool.ValueRO.DynamicEntityId && boidShared.BoidSchoolId == boidSchool.ValueRO.BoidSchoolId)
                            {
                                boidTotal++;
                            }
                        }

                        // Iterate over boids, ignoring not our schools
                        int boidIndex = 0;
                        foreach (Entity boidEntity in boidEntities)
                        {
                            BoidShared boidShared = state.EntityManager.GetSharedComponentManaged<BoidShared>(boidEntity);
                            if (boidShared.DynamicEntityId != boidSchool.ValueRO.DynamicEntityId || boidShared.BoidSchoolId != boidSchool.ValueRO.BoidSchoolId)
                            {
                                // Not our school, skip this boid
                                continue;
                            }
                            
                            //// Visibility per view
                            float4 boidScreenDisplayStart = new float4();
                            float4 boidScreenDisplayEnd = new float4();

                            // Iterate over enabled views
                            for (int i = 0; i < boidSchool.ValueRO.ViewsCount; i++)
                            {
                                // If the boid's position on the agents list is in the percentage set in the viewVisibilityPercentageArray for this view
                                if (boidIndex < (int)(boidTotal * (boidSchool.ValueRO.ViewVisibilityPercentages[i] / 100.0f)))
                                {
                                    // Visible in this view
                                    // Start
                                    var startFloat = 1.0f / boidSchool.ValueRO.ViewsCount * i;
                                    if (i == 0) boidScreenDisplayStart.x = startFloat;
                                    else if (i == 1) boidScreenDisplayStart.y = startFloat;
                                    else if (i == 2) boidScreenDisplayStart.z = startFloat;
                                    else if (i == 3) boidScreenDisplayStart.w = startFloat;
                                    
                                    // End
                                    var endFloat = 1.0f / boidSchool.ValueRO.ViewsCount * (i + 1);
                                    if (i == 0) boidScreenDisplayEnd.x = endFloat;
                                    else if (i == 1) boidScreenDisplayEnd.y = endFloat;
                                    else if (i == 2) boidScreenDisplayEnd.z = endFloat;
                                    else if (i == 3) boidScreenDisplayEnd.w = endFloat;
                                }
                                else
                                {
                                    // Not visible in this view
                                    // Set the screenDisplayStart and screenDisplayEnd values for this view to 0.0f
                                    if (i == 0) boidScreenDisplayStart.x = 0.0f;
                                    else if (i == 1) boidScreenDisplayStart.y = 0.0f;
                                    else if (i == 2) boidScreenDisplayStart.z = 0.0f;
                                    else if (i == 3) boidScreenDisplayStart.w = 0.0f;
                                    
                                    if (i == 0) boidScreenDisplayEnd.x = 0.0f;
                                    else if (i == 1) boidScreenDisplayEnd.y = 0.0f;
                                    else if (i == 2) boidScreenDisplayEnd.z = 0.0f;
                                    else if (i == 3) boidScreenDisplayEnd.w = 0.0f;
                                }
                            }
                            
                            // boidScreenDisplayStart/End assembled, assign to Boid entity
                            ScreenDisplayStartOverride screenDisplayStartOverride = state.EntityManager.GetComponentData<ScreenDisplayStartOverride>(boidEntity);
                            screenDisplayStartOverride.Value = boidScreenDisplayStart;
                            entityCommandBuffer.SetComponent(boidEntity, screenDisplayStartOverride);
                                    
                            ScreenDisplayEndOverride screenDisplayEndOverride = state.EntityManager.GetComponentData<ScreenDisplayEndOverride>(boidEntity);
                            screenDisplayEndOverride.Value = boidScreenDisplayEnd;
                            entityCommandBuffer.SetComponent(boidEntity, screenDisplayEndOverride);

                            boidIndex++;
                        }
                        boidEntities.Dispose();

                        boidSchoolCopy.ShaderUpdateRequested = false;
                    }
                }

                // State change: Target repositioning and speed randomization
                if (boidSchool.ValueRO.Target != Entity.Null)
                {
                    // If the TargetRepositionTimer is less than or equal to 0
                    if (boidSchool.ValueRO.TargetRepositionTimer <= 0.0f) 
                    {
                        // Get the target entity
                        Entity targetEntity = boidSchool.ValueRO.Target;

                        // Get the current target position
                        float3 currentTargetPosition = state.EntityManager.GetComponentData<LocalToWorld>(targetEntity).Position;

                        // Generate a new deterministic position within the BoidSchool's BoundsSize
                        float3 newTargetPosition = GenerateDeterministicPositionWithinBounds(
                            boidSchool.ValueRO.BoundsCenter,
                            boidSchool.ValueRO.BoundsSize,
                            (uint)boidSchool.ValueRO.DynamicEntityId,
                            (uint)boidSchool.ValueRO.BoidSchoolId,
                            (uint)(1 + boidSchool.ValueRO.TargetRepositionIteration));

                        // Set up a deterministic random number generator (per school)
                        uint seed = math.hash(new uint3((uint)boidSchool.ValueRO.DynamicEntityId, (uint)boidSchool.ValueRO.BoidSchoolId, (uint)boidSchool.ValueRO.TargetRepositionIteration));
                        Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex(seed);

                        // Set the TargetRepositionTimer to a random value
                        float repositionDuration = GetRandomFloat(ref random, boidSchool.ValueRO.StateChangeTimerMin, boidSchool.ValueRO.StateChangeTimerMax);
                        boidSchoolCopy.TargetRepositionTimer = repositionDuration;
                        boidSchoolCopy.TargetRepositionIteration = boidSchool.ValueRO.TargetRepositionIteration + 1;

                        // Store the start and end positions for lerping
                        entityCommandBuffer.SetComponent(targetEntity, new BoidTarget
                        {
                            BoidSchoolId = boidSchool.ValueRO.BoidSchoolId,
                            DynamicEntityId = boidSchool.ValueRO.DynamicEntityId,
                            StartPosition = currentTargetPosition,
                            EndPosition = newTargetPosition,
                            LerpDuration = repositionDuration,
                            LerpTimer = 0f
                        });

                        // Update MoveSpeedModifier for all boids in this school
                        EntityQuery boidQuery = SystemAPI.QueryBuilder().WithAll<BoidShared, BoidUnique>().Build();
                        NativeArray<Entity> boidEntities = boidQuery.ToEntityArray(Allocator.Temp);

                        foreach (Entity boidEntity in boidEntities)
                        {
                            BoidShared boidShared = state.EntityManager.GetSharedComponentManaged<BoidShared>(boidEntity);
                            if (boidShared.DynamicEntityId == boidSchool.ValueRO.DynamicEntityId && boidShared.BoidSchoolId == boidSchool.ValueRO.BoidSchoolId)
                            {
                                // Check if this boid is currently escaping from a predator
                                // TODO This currently does not work correctly, IsComponentEnabled<EscapingPredator> is never set to true
                                // TODO The random target speed change is instantly overriden when escaping predator though
                                bool isEscapingPredator = false;
                                if (state.EntityManager.HasComponent<EscapingPredator>(boidEntity))
                                {
                                    isEscapingPredator = state.EntityManager.IsComponentEnabled<EscapingPredator>(boidEntity);
                                }

                                BoidUnique boidUnique = state.EntityManager.GetComponentData<BoidUnique>(boidEntity);
                                
                                // Set random speed for this state transition.
                                // The seed is deterministic but depends on the current TargetRepositionIteration,
                                // so each boid gets a new random target speed on every state change.
                                if (!isEscapingPredator)
                                {
                                    uint speedSeed = math.hash(new uint4(
                                        (uint)boidSchool.ValueRO.DynamicEntityId,
                                        (uint)boidSchool.ValueRO.BoidSchoolId,
                                        (uint)boidEntity.Index,
                                        (uint)boidSchoolCopy.TargetRepositionIteration));
                                    var speedRng = Unity.Mathematics.Random.CreateFromIndex(speedSeed);
                                    boidUnique.TargetSpeedModifier = speedRng.NextFloat(
                                        boidSchool.ValueRO.SpeedModifierMin,
                                        boidSchool.ValueRO.SpeedModifierMax);
                                    entityCommandBuffer.SetComponent(boidEntity, boidUnique);
                                }
                            }
                        }

                        boidEntities.Dispose();
                    }
                    else
                    {
                        // Update the lerp for the target position
                        Entity targetEntity = boidSchool.ValueRO.Target;
                        BoidTarget boidTarget = state.EntityManager.GetComponentData<BoidTarget>(targetEntity);
                        
                        boidTarget.LerpTimer += SystemAPI.Time.DeltaTime;
                        float t = math.saturate(boidTarget.LerpTimer / boidTarget.LerpDuration);
                        float3 newPosition = math.lerp(boidTarget.StartPosition, boidTarget.EndPosition, t);

                        entityCommandBuffer.SetComponent(targetEntity, new LocalTransform
                        {
                            Position = newPosition,
                            Rotation = quaternion.identity,
                            Scale = 1.0f
                        });

                        entityCommandBuffer.SetComponent(targetEntity, boidTarget);

                        // Decrement the TargetRepositionTimer
                        boidSchoolCopy.TargetRepositionTimer -= SystemAPI.Time.DeltaTime;
                    }
                }
                
                entityCommandBuffer.SetComponent(boidSchoolEntity, boidSchoolCopy); // We are done modifying the BoidSchool component data, so we set it back to the entity
            }

            entityCommandBuffer.Playback(state.EntityManager);
        }

        /// <summary>
        /// Generates a random position within the specified bounds.
        /// </summary>
        private float3 GenerateRandomPositionWithinBounds(float3 boundsCenter, float3 boundsSize)
        {
            float3 boundsMin = boundsCenter - (boundsSize * 0.5f);
            float3 boundsMax = boundsCenter + (boundsSize * 0.5f);

            // var random = new Unity.Mathematics.Random(1);
            var pos = new float3(
                RandomGenerator.GetRandomFloat(boundsMin.x, boundsMax.x),
                RandomGenerator.GetRandomFloat(boundsMin.y, boundsMax.y),
                RandomGenerator.GetRandomFloat(boundsMin.z, boundsMax.z)
            );
            return pos;
        }
        
        /// <summary>
        /// Generates a random float value between min and max using the provided random number generator.
        /// </summary>
        private static float GetRandomFloat(ref Unity.Mathematics.Random random, float min, float max)
        {
            return random.NextFloat(min, max);
        }
        
        /// <summary>
        /// Generates a deterministic position within the specified bounds, seeded by identifiers and a salt.
        /// </summary>
        private float3 GenerateDeterministicPositionWithinBounds(float3 boundsCenter, float3 boundsSize, uint dynamicEntityId, uint boidSchoolId, uint salt)
        {
            float3 boundsMin = boundsCenter - (boundsSize * 0.5f);
            float3 boundsMax = boundsCenter + (boundsSize * 0.5f);
            uint seed = math.hash(new uint3(dynamicEntityId, boidSchoolId, salt));
            var random = Unity.Mathematics.Random.CreateFromIndex(seed);
            var pos = new float3(
                random.NextFloat(boundsMin.x, boundsMax.x),
                random.NextFloat(boundsMin.y, boundsMax.y),
                random.NextFloat(boundsMin.z, boundsMax.z)
            );
            return pos;
        }
    }

    /// <summary>
    /// Job responsible for initializing boid positions and orientations when spawned.
    /// Places boids at random positions within bounds and assigns random scales.
    /// </summary>
    struct SetBoidLocalToWorld : IJobParallelFor
    {
        [NativeDisableContainerSafetyRestriction] [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalToWorld> LocalToWorldFromEntity;

        public NativeArray<Entity> Entities;
        public AABB Bounds;
        public int DynamicEntityId;
        public int BoidSchoolId;

        public void Execute(int i)
        {
            var entity = Entities[i];
            
            // Deterministic RNG seeded by group/school/index
            uint seed = math.hash(new uint3((uint)DynamicEntityId, (uint)BoidSchoolId, (uint)i));
            var random = Unity.Mathematics.Random.CreateFromIndex(seed);

            // Generate a deterministic direction only in the XZ plane (Y-axis rotation)
            float randomAngle = random.NextFloat(0.0f, 2.0f * math.PI);
            var dir = new float3(math.sin(randomAngle), 0.0f, math.cos(randomAngle));

            // Calculate bounds center
            float3 boundsCenter = (Bounds.Max + Bounds.Min) * 0.5f;
            
            // Spawn boids in a very tight cluster at the center
            // Use a small radius relative to the bounds size for tight grouping
            float maxSpawnRadius = 2.0f; // Fixed small radius for tight clustering
            
            // Generate deterministic position within a small sphere around the center
            float radius = random.NextFloat(0.0f, maxSpawnRadius);
            float theta = random.NextFloat(0.0f, 2.0f * math.PI);
            float phi = random.NextFloat(0.0f, math.PI);
            
            // Convert spherical coordinates to cartesian
            float x = radius * math.sin(phi) * math.cos(theta);
            float y = radius * math.sin(phi) * math.sin(theta);
            float z = radius * math.cos(phi);
            
            // Position relative to bounds center
            float3 pos = boundsCenter + new float3(x, y, z);

            // Clamp position to bounds (safety check)
            pos = math.clamp(pos, Bounds.Min, Bounds.Max);
            
            // Deterministic scale, rounded to 2 decimal places
            float scale = math.round(random.NextFloat(0.7f, 1.3f) * 100) / 100;

            var localToWorld = new LocalToWorld
            {
                Value = float4x4.TRS(pos, quaternion.LookRotationSafe(dir, math.up()), new float3(scale, scale, scale))
            };
            LocalToWorldFromEntity[entity] = localToWorld;
        }
    }
}