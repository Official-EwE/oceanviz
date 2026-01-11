using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Unity.Rendering;
using UnityEngine.Rendering.Universal;

namespace OceanViz3
{
    /// <summary>
    /// System that calculates boid flocking behavior and updates positions using jobs.
    /// Uses multiple jobs to handle different aspects: InitialPerBoidJob, InitialPerTargetJob, 
    /// InitialPerObstacleJob, MergeCells, and SteerBoidJob.
    /// </summary>
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct BoidSystem : ISystem
    {
        // Bend tuning constants (shader _CurrentVector.x behavior)
        private const float BEND_GAIN = 0.5f;                 // Multiplies signed angular velocity to get bend
        private const float BEND_MAX_ABS = 0.3f;               // Absolute clamp for bend (match shader expectation)
        private const float BEND_FLIP_TIME_SEC = 0.8f;         // Time to go from -max to +max (seconds)
        private const float BEND_SLEW_RATE = (2.0f * BEND_MAX_ABS) / BEND_FLIP_TIME_SEC; // Units per second
        private const float BEND_ANGVEL_DEADZONE = 0.0f;       // rad/s below which bend is zero
        private const float PREDATOR_SIZE_TO_RADIUS_FACTOR = 1.0f; // Half of largest mesh dimension contributes to radius
        
        /// <summary>Query to get the SceneData entity</summary>
        private EntityQuery sceneDataQuery;

        /// <summary>Query to get the PhysicsCollider entity</summary>
        private EntityQuery terrainColliderQuery;

        /// <summary>
        /// Initializes the system by setting up required queries.
        /// </summary>
        public void OnCreate(ref SystemState state)
        {
            // Get the singleton SceneData entity
            sceneDataQuery = state.EntityManager.CreateEntityQuery(typeof(SceneData));
            state.RequireForUpdate(sceneDataQuery);

            // Get the PhysicsCollider entity
            terrainColliderQuery = state.EntityManager.CreateEntityQuery(typeof(PhysicsCollider));
            state.RequireForUpdate(terrainColliderQuery);
        }

        /// <summary>
        /// Updates the boid simulation each frame. Handles:
        /// - Enabling/disabling boids based on camera distance
        /// - Processing each unique boid variant (school)
        /// - Running jobs for flocking calculations
        /// - Updating boid positions and rotations
        /// </summary>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Main Queries
            EntityQuery enabledBoidsQuery = SystemAPI.QueryBuilder()
                .WithAllRW<LocalToWorld>()
                .WithAllRW<CurrentVectorOverride>()
                .WithAllRW<BoidUnique>()
                .WithAll<BoidShared>()
                .WithAll<AccumulatedTimeOverride>()
                .WithAll<AnimationSpeedOverride>()
                .Build();            
            int enabledBoidCount = enabledBoidsQuery.CalculateEntityCount();
            
            EntityQuery targetQuery = SystemAPI.QueryBuilder()
                .WithAll<BoidTarget, LocalToWorld>()
                .Build();            
            int targetCount = targetQuery.CalculateEntityCount();
            
            EntityQuery obstacleQuery = SystemAPI.QueryBuilder()
                .WithAll<BoidObstacle, LocalToWorld>()
                .Build();            
            int obstacleCount = obstacleQuery.CalculateEntityCount();     
            
            EntityQuery predatorQuery = SystemAPI.QueryBuilder()
                .WithAll<BoidShared, BoidUnique, BoidPredator, LocalToWorld>()
                .Build();            
            int predatorCount = predatorQuery.CalculateEntityCount();   
            
            EntityQuery preyQuery = SystemAPI.QueryBuilder()
                .WithAll<BoidShared, BoidUnique, BoidPrey, LocalToWorld>()
                .Build();            
            int preyCount = preyQuery.CalculateEntityCount();
            
            // The system requires at least one enabled boid, target and obstacle to run
            if (enabledBoidCount == 0 || targetCount == 0 || obstacleCount == 0)
            {
                return;
            }

            var world = state.WorldUnmanaged;

            // Get unique boid types list (BoidSchoolId means each school of boids will have it's own boidSettings)
            state.EntityManager.GetAllUniqueSharedComponents(out NativeList<BoidShared> uniqueBoidTypes, world.UpdateAllocator.ToAllocator);

            float deltaTime = math.min(0.05f, SystemAPI.Time.DeltaTime);

            // Each variant of the Boid represents a different value of the SharedComponentData and is self-contained,
            // meaning Boids of the same variant only interact with one another (meaning separation/cohesion). Thus, this loop processes each
            // variant type individually.
            foreach (var boidSettings in uniqueBoidTypes) // Iterate over all unique Boid variants (BoidSchoolId means each school of boids will have it's own boidSettings)
            {
                // Filter the boidQuery to only include boids with the current boidSettings
                enabledBoidsQuery.AddSharedComponentFilter(boidSettings); 

                int boidCount = enabledBoidsQuery.CalculateEntityCount();
                if (boidCount == 0)
                {
                    // Early out. If the given variant includes no Boids, move on to the next loop.
                    // For example, variant 0 will always exit early bc it's it represents a default, uninitialized
                    // Boid struct, which does not appear in this sample.
                    enabledBoidsQuery.ResetFilter();
                    continue;
                }
                
                // Find the targetPosition for the current boidSettings by using the BoidSchool's targetEntity
                float3 targetPosition = float3.zero;

                // Find the targetEntity by using the BoidSchoolId and DynamicEntityId
                foreach (var targetEntity in targetQuery.ToEntityArray(Allocator.Temp))
                {
                    var target = state.EntityManager.GetComponentData<BoidTarget>(targetEntity);
                    if (target.BoidSchoolId == boidSettings.BoidSchoolId && target.DynamicEntityId == boidSettings.DynamicEntityId)
                    {
                        targetPosition = state.EntityManager.GetComponentData<LocalToWorld>(targetEntity).Position;
                        break;
                    }
                }

                // The following calculates spatial cells of neighboring Boids
                // note: working with a sparse grid and not a dense bounded grid so there
                // are no predefined borders of the space.
                var hashMap                                 = new NativeParallelMultiHashMap<int, int>(boidCount, world.UpdateAllocator.ToAllocator);
                var cellIndices               = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(boidCount, ref world.UpdateAllocator);
                var cellCount                 = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(boidCount, ref world.UpdateAllocator);
                
                var cellTargetPositionIndex   = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(boidCount, ref world.UpdateAllocator);
                
                var copyObstaclePositions   = CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(obstacleCount, ref world.UpdateAllocator);
                var cellObstaclePositionIndex = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(boidCount, ref world.UpdateAllocator);
                var cellObstacleDistance     = CollectionHelper.CreateNativeArray<float, RewindableAllocator>(boidCount, ref world.UpdateAllocator);

                var copyTargetPositions     = CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(targetCount, ref world.UpdateAllocator);
                
                var copyPredatorPositions   = CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(predatorCount, ref world.UpdateAllocator);
                var copyPredatorSizes       = CollectionHelper.CreateNativeArray<float, RewindableAllocator>(predatorCount, ref world.UpdateAllocator);
                var cellPredatorPositionIndex = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(boidCount, ref world.UpdateAllocator);
                var cellPredatorDistance     = CollectionHelper.CreateNativeArray<float, RewindableAllocator>(boidCount, ref world.UpdateAllocator);
                
                var copyPreyPositions        = CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(preyCount, ref world.UpdateAllocator);
                
                var cellAlignment            = CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(boidCount, ref world.UpdateAllocator);
                var cellSeparation           = CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(boidCount, ref world.UpdateAllocator);
                var copyObstacleSizes        = CollectionHelper.CreateNativeArray<float3, RewindableAllocator>(obstacleCount, ref world.UpdateAllocator);

                // The following jobs all run in parallel because the same JobHandle is passed for their
                // input dependencies when the jobs are scheduled; thus, they can run in any order (or concurrently).
                // The concurrency is property of how they're scheduled, not of the job structs themselves.
                var boidChunkBaseEntityIndexArray = enabledBoidsQuery.CalculateBaseEntityIndexArrayAsync(
                    world.UpdateAllocator.ToAllocator, state.Dependency,
                    out var boidChunkBaseIndexJobHandle);
                var targetChunkBaseEntityIndexArray = targetQuery.CalculateBaseEntityIndexArrayAsync(
                    world.UpdateAllocator.ToAllocator, state.Dependency,
                    out var targetChunkBaseIndexJobHandle);
                var obstacleChunkBaseEntityIndexArray = obstacleQuery.CalculateBaseEntityIndexArrayAsync(
                    world.UpdateAllocator.ToAllocator, state.Dependency,
                    out var obstacleChunkBaseIndexJobHandle);
                var predatorChunkBaseEntityIndexArray = predatorQuery.CalculateBaseEntityIndexArrayAsync(
                    world.UpdateAllocator.ToAllocator, state.Dependency,
                    out var predatorChunkBaseIndexJobHandle);
                var preyChunkBaseEntityIndexArray = preyQuery.CalculateBaseEntityIndexArrayAsync(
                    world.UpdateAllocator.ToAllocator, state.Dependency,
                    out var preyChunkBaseIndexJobHandle);

                // These jobs extract the relevant position, heading component
                // to NativeArrays so that they can be randomly accessed by the `MergeCells` and `Steer` jobs.
                // These jobs are defined using the IJobEntity syntax.
                var initialBoidJob = new InitialPerBoidJob
                {
                    ChunkBaseEntityIndices = boidChunkBaseEntityIndexArray,
                    CellAlignment = cellAlignment,
                    CellSeparation = cellSeparation,
                    ParallelHashMap = hashMap.AsParallelWriter(),
                    InverseBoidCellRadius = 1.0f / boidSettings.CellRadius,
                };
                var initialBoidJobHandle = initialBoidJob.ScheduleParallel(enabledBoidsQuery, boidChunkBaseIndexJobHandle);

                var initialTargetJob = new InitialPerTargetJob
                {
                    ChunkBaseEntityIndices = targetChunkBaseEntityIndexArray,
                    TargetPositions = copyTargetPositions,
                };
                var initialTargetJobHandle = initialTargetJob.ScheduleParallel(targetQuery, targetChunkBaseIndexJobHandle);

                var initialObstacleJob = new InitialPerObstacleJob
                {
                    ChunkBaseEntityIndices = obstacleChunkBaseEntityIndexArray,
                    ObstaclePositions = copyObstaclePositions,
                    ObstacleSizes = copyObstacleSizes
                };
                var initialObstacleJobHandle = initialObstacleJob.ScheduleParallel(obstacleQuery, obstacleChunkBaseIndexJobHandle);
                
                var initialPredatorJob = new InitialPerPredatorJob
                {
                    ChunkBaseEntityIndices = predatorChunkBaseEntityIndexArray,
                    PredatorPositions = copyPredatorPositions,
                    PredatorSizes = copyPredatorSizes,
                };
                var initialPredatorJobHandle = initialPredatorJob.ScheduleParallel(predatorQuery, predatorChunkBaseIndexJobHandle);
                
                var initialPreyJob = new InitialPerPreyJob
                {
                    ChunkBaseEntityIndices = preyChunkBaseEntityIndexArray,
                    PreyPositions = copyPreyPositions,
                };
                var initialPreyJobHandle = initialPreyJob.ScheduleParallel(preyQuery, preyChunkBaseIndexJobHandle);

                var initialCellCountJob = new MemsetNativeArray<int>
                {
                    Source = cellCount,
                    Value  = 1
                };
                var initialCellCountJobHandle = initialCellCountJob.Schedule(boidCount, 64, state.Dependency);

                var initialCellBarrierJobHandle = JobHandle.CombineDependencies(initialBoidJobHandle, initialCellCountJobHandle);
                var copyTargetObstacleBarrierJobHandle = JobHandle.CombineDependencies(initialTargetJobHandle, initialObstacleJobHandle);
                
                copyTargetObstacleBarrierJobHandle = JobHandle.CombineDependencies(copyTargetObstacleBarrierJobHandle, initialPredatorJobHandle);
                copyTargetObstacleBarrierJobHandle = JobHandle.CombineDependencies(copyTargetObstacleBarrierJobHandle, initialPreyJobHandle);
                
                var mergeCellsBarrierJobHandle = JobHandle.CombineDependencies(initialCellBarrierJobHandle, copyTargetObstacleBarrierJobHandle);

                var mergeCellsJob = new MergeCells
                {
                    cellIndices               = cellIndices,
                    cellAlignment             = cellAlignment,
                    cellSeparation            = cellSeparation,
                    
                    obstaclePositions         = copyObstaclePositions,
                    cellObstacleDistance      = cellObstacleDistance,
                    cellObstaclePositionIndex = cellObstaclePositionIndex,
                    
                    targetPositions           = copyTargetPositions,
                    cellTargetPositionIndex   = cellTargetPositionIndex,
                    
                    predatorPositions         = copyPredatorPositions,
                    cellPredatorDistance      = cellPredatorDistance,
                    cellPredatorPositionIndex = cellPredatorPositionIndex,
                    
                    preyPositions             = copyPreyPositions,
                    
                    cellCount                 = cellCount,
                };
                var mergeCellsJobHandle = mergeCellsJob.Schedule(hashMap, 64, mergeCellsBarrierJobHandle);

                // This reads the previously calculated boid information for all the boids of each cell to update
                // the `localToWorld` of each of the boids based on their newly calculated headings using
                // the standard boid flocking algorithm.
                var steerBoidJob = new SteerBoidJob
                {
                    ChunkBaseEntityIndices = boidChunkBaseEntityIndexArray,
                    CellIndices = cellIndices,
                    CellCount = cellCount,
                    CellAlignment = cellAlignment,
                    CellSeparation = cellSeparation,
                    
                    ObstaclePositions = copyObstaclePositions,
                    CellObstacleDistance = cellObstacleDistance,
                    CellObstaclePositionIndex = cellObstaclePositionIndex,
                    ObstacleDimensions = copyObstacleSizes,
                    
                    //TargetPositions = copyTargetPositions,
                    TargetPosition = targetPosition,
                    CellTargetPositionIndex = cellTargetPositionIndex,
                    
                    PredatorPositions = copyPredatorPositions,
                    PredatorSizes = copyPredatorSizes,
                    CellPredatorDistance = cellPredatorDistance,
                    CellPredatorPositionIndex = cellPredatorPositionIndex,
                    
                    CurrentBoidSharedVariant = boidSettings,
                    DeltaTime = deltaTime,
                    MoveDistance = boidSettings.DefaultMoveSpeed * deltaTime,
                    
                    BoundsMax = boidSettings.BoundsMax,
                    BoundsMin = boidSettings.BoundsMin,
                    
                    SeabedBound = boidSettings.SeabedBound,
                    Prey = boidSettings.Prey,
                    
                    MaxVerticalAngle = boidSettings.MaxVerticalAngle,
                    MaxTurnRate = boidSettings.MaxTurnRate,
                };
                var steerBoidJobHandle = steerBoidJob.ScheduleParallel(enabledBoidsQuery, mergeCellsJobHandle);
                
                var updateTargetVectorJob = new UpdateTargetVectorJob
                {
                    DeltaTime = deltaTime,
                    BendGain = BEND_GAIN,
                    DeadzoneRadians = 0.0f,
                    AngularVelocityDeadzone = BEND_ANGVEL_DEADZONE,
                    MaxBendAbs = BEND_MAX_ABS,
                };
                var updateTargetVectorJobHandle = updateTargetVectorJob.ScheduleParallel(enabledBoidsQuery, steerBoidJobHandle); // Making the steerBoidJobHandle a dependency of wrapBoidJob makes sure the first job is completed to run this job
                
                var updateAccumulatedTimeJob = new UpdateAccumulatedTimeJob
                {
                    DeltaTime = deltaTime
                };
                var updateJobHandle = updateAccumulatedTimeJob.ScheduleParallel(enabledBoidsQuery, updateTargetVectorJobHandle);

                var smoothSpeedTransitionJob = new SmoothSpeedTransitionJob
                {
                    DeltaTime = deltaTime
                };
                var speedJobHandle = smoothSpeedTransitionJob.ScheduleParallel(enabledBoidsQuery, updateJobHandle);

                var smoothVectorTransitionJob = new SmoothVectorTransitionJob
                {
                    DeltaTime = deltaTime,
                    TransitionSpeed = BEND_SLEW_RATE
                };
                var vectorJobHandle = smoothVectorTransitionJob.ScheduleParallel(enabledBoidsQuery, speedJobHandle);
                
                state.Dependency = vectorJobHandle;
                
                // We pass the job handle and add the dependency so that we keep the proper ordering between the jobs
                // as the looping iterates. For our purposes of execution, this ordering isn't necessary; however, without
                // the add dependency call here, the safety system will throw an error, because we're accessing multiple
                // pieces of boid data and it would think there could possibly be a race condition.
                enabledBoidsQuery.AddDependency(state.Dependency);
                
                vectorJobHandle.Complete(); // Wait for the job to complete
                
                // Snap boids to the seabed
                if (boidSettings.SeabedBound)
                {
                    foreach (Entity entity in enabledBoidsQuery.ToEntityArray(Allocator.Temp))
                    {
                        PhysicsWorldSingleton physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

                        var localToWorld = state.EntityManager.GetComponentData<LocalToWorld>(entity);
                        var currentPosition = localToWorld.Position;
                        //RefRW<LocalToWorld> localToWorld = SystemAPI.GetComponentRW<LocalToWorld>(entity);

                        // Create a raycast input
                        RaycastInput rayInput = new RaycastInput
                        {
                            Start = new float3(currentPosition.x, 1000f,
                                currentPosition.z), // Start high above the terrain
                            End = new float3(currentPosition.x, -1000f, currentPosition.z), // End below the terrain
                            Filter = CollisionFilter.Default
                        };

                        // Perform the raycast
                        if (physicsWorld.CollisionWorld.CastRay(rayInput, out RaycastHit hit))
                        {
                            quaternion newRotation = math.mul(
                                localToWorld.Rotation,
                                CalculateFromToRotation(localToWorld.Up, hit.SurfaceNormal)
                            );

                            localToWorld = new LocalToWorld
                            {
                                Value = float4x4.TRS(
                                    hit.Position,
                                    // math.slerp(localToWorld.Rotation, newRotation, deltaTime * 5f), 
                                    //newRotation, // TODO: Setting the rotation according to the raycast breaks the algorithm
                                    localToWorld.Rotation,
                                    localToWorld.Value.Scale()
                                )
                            };
                        }

                        state.EntityManager.SetComponentData(entity, localToWorld);
                    }
                }

                enabledBoidsQuery.ResetFilter();
            }

            uniqueBoidTypes.Dispose();
        }

        /// <summary>
        /// Calculates rotation between two direction vectors.
        /// </summary>
        /// <returns>Quaternion representing the rotation</returns>
        private static quaternion CalculateFromToRotation(float3 fromDirection, float3 toDirection)
        {
            // Normalize input vectors
            fromDirection = math.normalize(fromDirection);
            toDirection = math.normalize(toDirection);

            // Calculate the rotation axis (cross product of input vectors)
            float3 rotationAxis = math.cross(fromDirection, toDirection);

            // Calculate the angle between input vectors (dot product)
            float dotProduct = math.dot(fromDirection, toDirection);
            float angle = math.acos(dotProduct);

            // Create the quaternion representing the rotation
            quaternion rotation = quaternion.AxisAngle(rotationAxis, angle);

            return rotation;
        }

        /// <summary>
        /// Converts a quaternion to Euler angles.
        /// </summary>
        private static float3 ToEuler(quaternion quaternion)
        {
            float4 q = quaternion.value;
            double3 res;
        
            double sinr_cosp = +2.0 * (q.w * q.x + q.y * q.z);
            double cosr_cosp = +1.0 - 2.0 * (q.x * q.x + q.y * q.y);
            res.x = math.atan2(sinr_cosp, cosr_cosp);
        
            double sinp = +2.0 * (q.w * q.y - q.z * q.x);
            if (math.abs(sinp) >= 1)
            {
                res.y = math.PI / 2 * math.sign(sinp);
            }
            else
            {
                res.y = math.asin(sinp);
            }
        
            double siny_cosp = +2.0 * (q.w * q.z + q.x * q.y);
            double cosy_cosp = +1.0 - 2.0 * (q.y * q.y + q.z * q.z);
            res.z = math.atan2(siny_cosp, cosy_cosp);
        
            return (float3)res;
        }

        /// <summary>
        /// Initial job that processes each boid to prepare for flocking calculations.
        /// Extracts position and heading data and populates the spatial hash map.
        /// </summary>
        [BurstCompile]
        partial struct InitialPerBoidJob : IJobEntity
        {
            [ReadOnly] public NativeArray<int> ChunkBaseEntityIndices;
            [NativeDisableParallelForRestriction] public NativeArray<float3> CellAlignment;
            [NativeDisableParallelForRestriction] public NativeArray<float3> CellSeparation;
            public NativeParallelMultiHashMap<int, int>.ParallelWriter ParallelHashMap;
            public float InverseBoidCellRadius;
            void Execute([ChunkIndexInQuery] int chunkIndexInQuery, [EntityIndexInChunk] int entityIndexInChunk, in LocalToWorld localToWorld)
            {
                int entityIndexInQuery = ChunkBaseEntityIndices[chunkIndexInQuery] + entityIndexInChunk;
                CellAlignment[entityIndexInQuery] = localToWorld.Forward;
                CellSeparation[entityIndexInQuery] = localToWorld.Position;
                // Populates a hash map, where each bucket contains the indices of all Boids whose positions quantize
                // to the same value for a given cell radius so that the information can be randomly accessed by
                // the `MergeCells` and `Steer` jobs.
                // This is useful in terms of the algorithm because it limits the number of comparisons that will
                // actually occur between the different boids. Instead of for each boid, searching through all
                // boids for those within a certain radius, this limits those by the hash-to-bucket simplification.
                var hash = (int)math.hash(new int3(math.floor(localToWorld.Position * InverseBoidCellRadius)));
                ParallelHashMap.Add(hash, entityIndexInQuery);
            }
        }

        /// <summary>
        /// Job that extracts target positions for boid flocking behavior.
        /// </summary>
        [BurstCompile]
        partial struct InitialPerTargetJob : IJobEntity // Get positions
        {
            [ReadOnly] public NativeArray<int> ChunkBaseEntityIndices;
            [NativeDisableParallelForRestriction] public NativeArray<float3> TargetPositions;
            void Execute([ChunkIndexInQuery] int chunkIndexInQuery, [EntityIndexInChunk] int entityIndexInChunk, in LocalToWorld localToWorld)
            {
                int entityIndexInQuery = ChunkBaseEntityIndices[chunkIndexInQuery] + entityIndexInChunk;
                TargetPositions[entityIndexInQuery] = localToWorld.Position;
            }
        }

        /// <summary>
        /// Job that extracts obstacle positions and dimensions for collision avoidance.
        /// </summary>
        [BurstCompile]
        partial struct InitialPerObstacleJob : IJobEntity // Get positions and sizes
        {
            [ReadOnly] public NativeArray<int> ChunkBaseEntityIndices;
            [NativeDisableParallelForRestriction] public NativeArray<float3> ObstaclePositions;
            [NativeDisableParallelForRestriction] public NativeArray<float3> ObstacleSizes; // New array for obstacle sizes
            void Execute([ChunkIndexInQuery] int chunkIndexInQuery, [EntityIndexInChunk] int entityIndexInChunk, in LocalToWorld localToWorld, in BoidObstacle obstacle)
            {
                int entityIndexInQuery = ChunkBaseEntityIndices[chunkIndexInQuery] + entityIndexInChunk;
                ObstaclePositions[entityIndexInQuery] = localToWorld.Position;
                ObstacleSizes[entityIndexInQuery] = obstacle.Dimensions; // Store the size of each obstacle
            }
        }
        
        /// <summary>
        /// Job that extracts predator positions for prey avoidance behavior.
        /// </summary>
        [BurstCompile]
        partial struct InitialPerPredatorJob : IJobEntity // Get positions
        {
            [ReadOnly] public NativeArray<int> ChunkBaseEntityIndices;
            [NativeDisableParallelForRestriction] public NativeArray<float3> PredatorPositions;
            [NativeDisableParallelForRestriction] public NativeArray<float> PredatorSizes;
            void Execute([ChunkIndexInQuery] int chunkIndexInQuery, [EntityIndexInChunk] int entityIndexInChunk, in LocalToWorld localToWorld, in BoidPredator predator, in BoidShared boidShared)
            {
                int entityIndexInQuery = ChunkBaseEntityIndices[chunkIndexInQuery] + entityIndexInChunk;
                PredatorPositions[entityIndexInQuery] = localToWorld.Position;
                PredatorSizes[entityIndexInQuery] = boidShared.MeshLargestDimension;
            }
        }
        
        /// <summary>
        /// Job that extracts prey positions for predator pursuit behavior.
        /// </summary>
        [BurstCompile]
        partial struct InitialPerPreyJob : IJobEntity // Get positions
        {
            [ReadOnly] public NativeArray<int> ChunkBaseEntityIndices;
            [NativeDisableParallelForRestriction] public NativeArray<float3> PreyPositions;
            void Execute([ChunkIndexInQuery] int chunkIndexInQuery, [EntityIndexInChunk] int entityIndexInChunk, in LocalToWorld localToWorld, in BoidPrey prey)
            {
                int entityIndexInQuery = ChunkBaseEntityIndices[chunkIndexInQuery] + entityIndexInChunk;
                PreyPositions[entityIndexInQuery] = localToWorld.Position;
            }
        }
        
        /// <summary>
        /// Merges boid data within spatial cells to calculate:
        /// - Number of boids per cell
        /// - Accumulated alignment and separation vectors
        /// - Nearest obstacles and targets for each cell
        /// 
        /// A "cell" represents a spatial bucket containing boids that are near each other,
        /// created by quantizing boid positions to a grid using the cell radius.
        /// </summary>
        [BurstCompile]
        struct MergeCells : IJobNativeParallelMultiHashMapMergedSharedKeyIndices
        {
            public NativeArray<int>                 cellIndices;
            public NativeArray<float3>              cellAlignment;
            public NativeArray<float3>              cellSeparation;
            
            [ReadOnly] public NativeArray<float3>   obstaclePositions;
            public NativeArray<int>                 cellObstaclePositionIndex;
            public NativeArray<float>               cellObstacleDistance;
            
            public NativeArray<int>                 cellTargetPositionIndex;
            
            [ReadOnly] public NativeArray<float3>   predatorPositions; // Can be empty
            public NativeArray<int>                 cellPredatorPositionIndex; // New field
            public NativeArray<float>               cellPredatorDistance;
            
            public NativeArray<int>                 cellCount;
            [ReadOnly] public NativeArray<float3>   targetPositions;
            [ReadOnly] public NativeArray<float3>   preyPositions;

            void NearestPosition(NativeArray<float3> targets, float3 position, out int nearestPositionIndex, out float nearestDistance)
            {
                nearestPositionIndex = 0;
                nearestDistance      = math.lengthsq(position - targets[0]);
                for (int i = 1; i < targets.Length; i++)
                {
                    var targetPosition = targets[i];
                    var distance       = math.lengthsq(position - targetPosition);
                    var nearest        = distance < nearestDistance;

                    nearestDistance      = math.select(nearestDistance, distance, nearest);
                    nearestPositionIndex = math.select(nearestPositionIndex, i, nearest);
                }
                nearestDistance = math.sqrt(nearestDistance);
            }

            // Resolves the distance of the nearest obstacle and target and stores the cell index.
            public void ExecuteFirst(int index)
            {
                var position = cellSeparation[index] / cellCount[index];

                int obstaclePositionIndex;
                float obstacleDistance;
                NearestPosition(obstaclePositions, position, out obstaclePositionIndex, out obstacleDistance);
                cellObstaclePositionIndex[index] = obstaclePositionIndex;
                cellObstacleDistance[index] = obstacleDistance;

                int targetPositionIndex;
                float targetDistance; // Not used
                NearestPosition(targetPositions, position, out targetPositionIndex, out targetDistance);
                cellTargetPositionIndex[index] = targetPositionIndex;

                int predatorPositionIndex = -1;
                float predatorDistance; // Not used
                // Check if there are any predators
                if (predatorPositions.Length > 0) 
                {
                    NearestPosition(predatorPositions, position, out predatorPositionIndex, out predatorDistance);
                    cellPredatorPositionIndex[index] = predatorPositionIndex; // Store the index of the nearest predator
                    cellPredatorDistance[index] = predatorDistance; // Assign the calculated distance to the array
                }
                else // If there are no predators, set distance to a large value
                {
                    cellPredatorPositionIndex[index] = -1; // Or some other indicator for no predator
                    cellPredatorDistance[index] = float.MaxValue;
                }

                cellIndices[index] = index;
            }

            // Sums the alignment and separation of the actual index being considered and stores
            // the index of this first value where we're storing the cells.
            // note: these items are summed so that in `Steer` their average for the cell can be resolved.
            public void ExecuteNext(int cellIndex, int index)
            {
                cellCount[cellIndex]      += 1;
                cellAlignment[cellIndex]  += cellAlignment[cellIndex]; // Changing ths to use cellAlignment[index] breaks boid movement
                cellSeparation[cellIndex] += cellSeparation[cellIndex]; // Changing ths to use cellSeparation[index] breaks boid movement
                cellIndices[index]        = cellIndex;
            }
        }

        /// <summary>
        /// Main steering job that updates each boid's position and rotation based on:
        /// - Alignment with neighbors
        /// - Separation from neighbors
        /// - Target seeking
        /// - Obstacle avoidance
        /// - Predator avoidance (for prey)
        /// - Bounds checking
        /// </summary>
        [BurstCompile]
        partial struct SteerBoidJob : IJobEntity // This runs for each boid
        {
            [ReadOnly] public NativeArray<int> ChunkBaseEntityIndices;
            [ReadOnly] public NativeArray<int> CellIndices;
            [ReadOnly] public NativeArray<int> CellCount;
            [ReadOnly] public NativeArray<float3> CellAlignment;
            [ReadOnly] public NativeArray<float3> CellSeparation;
            
            [ReadOnly] public NativeArray<float3> ObstaclePositions;
            [ReadOnly] public NativeArray<float> CellObstacleDistance; // Distance per cell to nearest obstacle
            [ReadOnly] public NativeArray<int> CellObstaclePositionIndex; // Nearest obstacle index per cell index
            [ReadOnly] public NativeArray<float3> ObstacleDimensions; 
            
            [ReadOnly] public float3 TargetPosition;
            [ReadOnly] public NativeArray<int> CellTargetPositionIndex; // Nearest target index per cell index
            
            [ReadOnly] public NativeArray<float3> PredatorPositions;
            [ReadOnly] public NativeArray<float> PredatorSizes;
            [ReadOnly] public NativeArray<float> CellPredatorDistance; // Distance per cell to nearest predator
            [ReadOnly] public NativeArray<int> CellPredatorPositionIndex; // Nearest predator index per cell index
            
            [ReadOnly] public BoidShared CurrentBoidSharedVariant;
            [ReadOnly] public float DeltaTime;
            [ReadOnly] public float MoveDistance;
            [ReadOnly] public float3 BoundsMax;
            [ReadOnly] public float3 BoundsMin;
            [ReadOnly] public bool SeabedBound; // Will do the steering in 2D only
            [ReadOnly] public bool Prey;
            [ReadOnly] public bool Disabled;
            
            [ReadOnly] public float MaxVerticalAngle;
            [ReadOnly] public float MaxTurnRate;
            
            private const float BoundsForce = 0.01f;
            
            void Execute([ChunkIndexInQuery] int chunkIndexInQuery, [EntityIndexInChunk] int entityIndexInChunk, Entity entity, ref LocalToWorld localToWorld, ref BoidUnique boidUnique)
            {
                if (Disabled || boidUnique.Disabled)
                {
                    return;
                }

                if (SeabedBound == false)
                {
                    int entityIndexInQuery = ChunkBaseEntityIndices[chunkIndexInQuery] + entityIndexInChunk;
                    var forward                           = localToWorld.Forward;
                    var currentPosition                   = localToWorld.Position;
                    var cellIndex                         = CellIndices[entityIndexInQuery];
                    var neighborCount                     = CellCount[cellIndex];
                    var alignment                         = CellAlignment[cellIndex];
                    var separation                        = CellSeparation[cellIndex];
                    
                    var nearestObstaclePositionIndex      = CellObstaclePositionIndex[cellIndex];
                    var nearestObstacleDistance           = CellObstacleDistance[cellIndex];
                    var nearestObstaclePosition           = ObstaclePositions[nearestObstaclePositionIndex];
                    
                    
                    var nearestTargetPositionIndex        = CellTargetPositionIndex[cellIndex];
                    var nearestTargetPosition              = TargetPosition;

                    // Setting up the directions for the three main biocrowds influencing directions adjusted based
                    // on the predefined weights:
                    // 1) alignment - how much should it move in a direction similar to those around it?
                    // note: we use `alignment/neighborCount`, because we need the average alignment in this case; however
                    // alignment is currently the summation of all those of the boids within the cellIndex being considered.
                    var alignmentResult     = CurrentBoidSharedVariant.AlignmentWeight
                                            * math.normalizesafe((alignment / neighborCount) - forward);
                    // 2) separation - how close is it to other boids and are there too many or too few for comfort?
                    // note: here separation represents the summed possible center of the cell. We perform the multiplication
                    // so that both `currentPosition` and `separation` are weighted to represent the cell as a whole and not
                    // the current individual boid.
                    var separationResult    = CurrentBoidSharedVariant.SeparationWeight
                                            * math.normalizesafe((currentPosition * neighborCount) - separation);
                    // 3) target - is it still towards its destination?
                    var targetHeading       = CurrentBoidSharedVariant.TargetWeight
                                            * math.normalizesafe(nearestTargetPosition - currentPosition);

                    // creating the obstacle avoidant vector s.t. it's pointing towards the nearest obstacle
                    // but at the specified 'ObstacleAversionDistance'. If this distance is greater than the
                    // current distance to the obstacle, the direction becomes inverted. This simulates the
                    // idea that if `currentPosition` is too close to an obstacle, the weight of this pushes
                    // the current boid to escape in the fastest direction; however, if the obstacle isn't
                    // too close, the weighting denotes that the boid doesnt need to escape but will move
                    // slower if still moving in that direction (note: we end up not using this move-slower
                    // case, because of `targetForward`'s decision to not use obstacle avoidance if an obstacle
                    // isn't close enough).
                    // var obstacleSteering                  = currentPosition - nearestObstaclePosition;
                    // var avoidObstacleHeading              = (nearestObstaclePosition + math.normalizesafe(obstacleSteering)
                    //     * CurrentBoidVariant.ObstacleAversionDistance) - currentPosition;

                    float3 obstacleSteering;
                    float3 avoidObstacleHeading;
                    float nearestObstacleDistanceFromRadius;
                    bool preyInPredatorRange = false; // New variable for direct predator range check
                    bool predatorDataAvailable = false;
                    float3 avoidPredatorHeading = float3.zero;
                    float nearestPredatorDistance = 0f;
                    float nearestPredatorDistanceFromRadius = 0f;
                    
                    // If the boid is prey and there are predators
                    // Regular obstacle avoidance parameters
                    float obstacleEffectiveRadius = CurrentBoidSharedVariant.ObstacleAversionDistance + ObstacleDimensions[nearestObstaclePositionIndex].x;
                    obstacleSteering = currentPosition - nearestObstaclePosition;
                    avoidObstacleHeading = (nearestObstaclePosition + math.normalizesafe(obstacleSteering) * obstacleEffectiveRadius) - currentPosition;
                    nearestObstacleDistanceFromRadius = nearestObstacleDistance - obstacleEffectiveRadius;

                    // Predator avoidance parameters (if applicable)
                    if (Prey == true && PredatorPositions.Length > 0)
                    {
                        var nearestPredatorPositionIndex = CellPredatorPositionIndex[cellIndex];
                        nearestPredatorDistance = CellPredatorDistance[cellIndex];
                        var nearestPredatorPosition = PredatorPositions[nearestPredatorPositionIndex];

                        // Check if prey is within predator detection range (independent of obstacles)
                        float predatorHalf = 0f;
                        if (PredatorSizes.Length > 0)
                        {
                            predatorHalf = PredatorSizes[nearestPredatorPositionIndex] * PREDATOR_SIZE_TO_RADIUS_FACTOR;
                        }
                        float predatorDetectionRange = CurrentBoidSharedVariant.ObstacleAversionDistance + 2.0f + predatorHalf;
                        preyInPredatorRange = nearestPredatorDistance < predatorDetectionRange;

                        // Build predator avoidance heading for soft switching
                        float predatorEffectiveRadius = CurrentBoidSharedVariant.ObstacleAversionDistance + predatorHalf;
                        float3 predatorSteering = currentPosition - nearestPredatorPosition;
                        avoidPredatorHeading = (nearestPredatorPosition + math.normalizesafe(predatorSteering) * predatorEffectiveRadius) - currentPosition;
                        nearestPredatorDistanceFromRadius = nearestPredatorDistance - predatorEffectiveRadius;
                        predatorDataAvailable = true;
                    }
                    
                    // the updated heading direction. If not needing to be avoidant (ie obstacle is not within
                    // predefined radius) then go with the usual defined heading that uses the amalgamation of
                    // the weighted alignment, separation, and target direction vectors.
                    var normalHeading = math.normalizesafe(alignmentResult + separationResult + targetHeading);
                    // Smooth blend between normal and avoidance based on penetration depth into the avoidance radius
                    float obstacleBlendWidth = math.max(0.001f, obstacleEffectiveRadius * 0.5f);
                    float tObstacle = math.saturate(-nearestObstacleDistanceFromRadius / obstacleBlendWidth);

                    float tPredator = 0f;
                    float3 blendedAvoidHeading = avoidObstacleHeading;
                    if (predatorDataAvailable)
                    {
                        int nearestPredatorPositionIndex2 = CellPredatorPositionIndex[cellIndex];
                        float predatorHalf2 = 0f;
                        if (PredatorSizes.Length > 0 && nearestPredatorPositionIndex2 >= 0 && nearestPredatorPositionIndex2 < PredatorSizes.Length)
                        {
                            predatorHalf2 = PredatorSizes[nearestPredatorPositionIndex2] * PREDATOR_SIZE_TO_RADIUS_FACTOR;
                        }
                        float predatorEffectiveRadius = (CurrentBoidSharedVariant.ObstacleAversionDistance + predatorHalf2);
                        float predatorBlendWidth = math.max(0.001f, predatorEffectiveRadius * 0.5f);
                        tPredator = math.saturate(-nearestPredatorDistanceFromRadius / predatorBlendWidth);

                        // Softly switch between obstacle and predator avoid headings based on relative distances
                        float switchBand = math.max(obstacleBlendWidth, predatorBlendWidth);
                        float switchWeight = 0.5f + (nearestObstacleDistance - nearestPredatorDistance) / (2f * switchBand);
                        switchWeight = math.saturate(switchWeight);
                        blendedAvoidHeading = math.normalizesafe(math.lerp(avoidObstacleHeading, avoidPredatorHeading, switchWeight));
                    }

                    float tAvoid = math.max(tObstacle, tPredator);
                    var targetForward = math.normalizesafe(math.lerp(normalHeading, blendedAvoidHeading, tAvoid));

                    // updates using the newly calculated heading direction
                    var nextHeading = math.normalizesafe(forward + DeltaTime * (targetForward - forward) * MaxTurnRate);
                    
                    // Correct the vertical component of the nextHeading
                    float3 horizontalHeading = new float3(nextHeading.x, 0, nextHeading.z);
                    float horizontalMagnitude = math.length(horizontalHeading);
                    
                    if (horizontalMagnitude > 0.0001f) // Avoid division by zero
                    {
                        float currentVerticalAngle = math.degrees(math.asin(nextHeading.y));
                        float clampedVerticalAngle = math.clamp(currentVerticalAngle, -MaxVerticalAngle, MaxVerticalAngle);
                        
                        float3 newHeading = math.normalize(horizontalHeading) * math.cos(math.radians(clampedVerticalAngle));
                        newHeading.y = math.sin(math.radians(clampedVerticalAngle));
                        
                        nextHeading = math.normalize(newHeading);
                    }
                    
                    // Check if the boid is outside the bounds
                    bool isOutsideBounds = currentPosition.x < BoundsMin.x || currentPosition.y < BoundsMin.y || currentPosition.z < BoundsMin.z ||
                                           currentPosition.x > BoundsMax.x || currentPosition.y > BoundsMax.y || currentPosition.z > BoundsMax.z;

                    // Fade-in bounds pull based on penetration depth past the bounds
                    float dx = math.max(0f, math.max(BoundsMin.x - currentPosition.x, currentPosition.x - BoundsMax.x));
                    float dy = math.max(0f, math.max(BoundsMin.y - currentPosition.y, currentPosition.y - BoundsMax.y));
                    float dz = math.max(0f, math.max(BoundsMin.z - currentPosition.z, currentPosition.z - BoundsMax.z));
                    float distanceOutside = math.length(new float3(dx, dy, dz));
                    float boundsMargin = math.max(0.001f, CurrentBoidSharedVariant.CellRadius * 2.0f);
                    float tBound = math.saturate(distanceOutside / boundsMargin);
                    if (tBound > 0f)
                    {
                        float3 directionToCenter = math.normalizesafe(new float3((BoundsMax + BoundsMin) / 2) - currentPosition);
                        nextHeading = math.normalizesafe(nextHeading + directionToCenter * (BoundsForce * tBound));
                    }

                    // After all forces are applied (including bounds force)
                    // Calculate the angle between current forward and the proposed nextHeading
                    float angle = math.acos(math.clamp(math.dot(math.normalize(forward), math.normalize(nextHeading)), -1f, 1f));
                    
                    // Calculate the maximum angle allowed to turn this frame based on MaxTurnRate
                    float maxAngleThisFrame = MaxTurnRate * DeltaTime;
                    
                    // If the angle exceeds the maximum allowed angle, limit it
                    if (angle > maxAngleThisFrame)
                    {
                        // Get the rotation axis
                        float3 axis = math.normalize(math.cross(forward, nextHeading));
                        // Create a quaternion for the maximum allowed rotation
                        quaternion maxRotation = quaternion.AxisAngle(axis, maxAngleThisFrame);
                        // Apply the maximum allowed rotation to the current forward vector
                        nextHeading = math.rotate(maxRotation, forward);
                    }

                    // Manage speed modifiers for prey based on predator proximity
                    if (Prey)
                    {
                        if (preyInPredatorRange)
                        {
                            // Set target speed when in predator range
                            boidUnique.TargetSpeedModifier = 3.0f;
                            // Note: EscapingPredator component should be enabled via EntityCommandBuffer
                            // in a separate system or job that can handle structural changes
                        }
                        // Note: Speed reset should only happen when transitioning out of escape state
                        // This should be handled by checking the EscapingPredator component state
                        // and only resetting speeds when that component is disabled
                    }

                    // Apply the individual speed modifier when updating the position
                    float individualMoveDistance = MoveDistance * boidUnique.MoveSpeedModifier;

                    // Update the boid's position and rotation
                    localToWorld.Value = float4x4.TRS(
                        new float3(localToWorld.Position + (nextHeading * individualMoveDistance)),
                        quaternion.LookRotationSafe(nextHeading, math.up()),
                        localToWorld.Value.Scale()
                    );
                }
                else if (SeabedBound == true)
                {
                    int entityIndexInQuery = ChunkBaseEntityIndices[chunkIndexInQuery] + entityIndexInChunk;
                    var forward = new float3(localToWorld.Forward.x, 0, localToWorld.Forward.z);
                    var currentPosition = new float3(localToWorld.Position.x, 0, localToWorld.Position.z);
                    var cellIndex = CellIndices[entityIndexInQuery];
                    var neighborCount = CellCount[cellIndex];
                    var alignment = new float3(CellAlignment[cellIndex].x, 0, CellAlignment[cellIndex].z);
                    var separation = new float3(CellSeparation[cellIndex].x, 0, CellSeparation[cellIndex].z);
                    var nearestObstacleDistance = CellObstacleDistance[cellIndex];
                    var nearestObstaclePositionIndex = CellObstaclePositionIndex[cellIndex];
                    var nearestTargetPositionIndex = CellTargetPositionIndex[cellIndex];
                    var nearestObstaclePosition = new float3(ObstaclePositions[nearestObstaclePositionIndex].x, 0, ObstaclePositions[nearestObstaclePositionIndex].z);
                    var nearestTargetPosition = TargetPosition;

                    // Added predator detection logic for SeabedBound boids
                    bool preyInPredatorRange = false;
                    if (Prey && PredatorPositions.Length > 0)
                    {
                        var nearestPredatorPositionDataIndex = CellPredatorPositionIndex[cellIndex]; // Note: This index is into PredatorPositions
                        var nearestPredatorDist = CellPredatorDistance[cellIndex]; 
                        // var nearestPredatorPos = PredatorPositions[nearestPredatorPositionDataIndex]; // Position of the nearest predator

                        int nearestPredatorIndex2D = CellPredatorPositionIndex[cellIndex];
                        float predatorHalf2D = 0f;
                        if (PredatorSizes.Length > 0 && nearestPredatorIndex2D >= 0 && nearestPredatorIndex2D < PredatorSizes.Length)
                        {
                            predatorHalf2D = PredatorSizes[nearestPredatorIndex2D] * PREDATOR_SIZE_TO_RADIUS_FACTOR;
                        }
                        float predatorDetectionRange = CurrentBoidSharedVariant.ObstacleAversionDistance + 15.0f + predatorHalf2D; 
                        preyInPredatorRange = nearestPredatorDist < predatorDetectionRange;
                    }
                    
                    var alignmentResult = CurrentBoidSharedVariant.AlignmentWeight * math.normalizesafe((alignment / neighborCount) - forward);
                    var separationResult = CurrentBoidSharedVariant.SeparationWeight * math.normalizesafe((currentPosition * neighborCount) - separation);
                    var targetHeading = CurrentBoidSharedVariant.TargetWeight * math.normalizesafe(nearestTargetPosition - currentPosition);
                    var obstacleSteering = currentPosition - nearestObstaclePosition;
                    
                    float obstacleEffectiveRadius2D = CurrentBoidSharedVariant.ObstacleAversionDistance + ObstacleDimensions[nearestObstaclePositionIndex].x;
                    var avoidObstacleHeading = (nearestObstaclePosition + math.normalizesafe(obstacleSteering) * obstacleEffectiveRadius2D) - currentPosition;
                    var nearestObstacleDistanceFromRadius = nearestObstacleDistance - obstacleEffectiveRadius2D;
                    var normalHeading = math.normalizesafe(alignmentResult + separationResult + targetHeading);

                    float blendWidth2D = math.max(0.001f, obstacleEffectiveRadius2D * 0.5f);
                    float t2D = math.saturate(-nearestObstacleDistanceFromRadius / blendWidth2D);
                    var targetForward = math.normalizesafe(math.lerp(normalHeading, avoidObstacleHeading, t2D));

                    var nextHeading = math.normalizesafe(forward + DeltaTime * (targetForward - forward) * MaxTurnRate);

                    // After all forces are applied (including bounds force)
                    // Calculate the angle between current forward and the proposed nextHeading
                    float angle = math.acos(math.clamp(math.dot(math.normalize(forward), math.normalize(nextHeading)), -1f, 1f));
                    
                    // Calculate the maximum angle allowed to turn this frame based on MaxTurnRate
                    float maxAngleThisFrame = MaxTurnRate * DeltaTime;
                    
                    // If the angle exceeds the maximum allowed angle, limit it
                    if (angle > maxAngleThisFrame)
                    {
                        // Get the rotation axis
                        float3 axis = math.normalize(math.cross(forward, nextHeading));
                        // Create a quaternion for the maximum allowed rotation
                        quaternion maxRotation = quaternion.AxisAngle(axis, maxAngleThisFrame);
                        // Apply the maximum allowed rotation to the current forward vector
                        nextHeading = math.rotate(maxRotation, forward);
                    }

                    // Manage speed modifiers for prey based on predator proximity
                    if (Prey)
                    {
                        if (preyInPredatorRange)
                        {
                            boidUnique.TargetSpeedModifier = 3.0f;
                            // Note: EscapingPredator component should be enabled via EntityCommandBuffer
                            // in a separate system or job that can handle structural changes
                        }
                    }

                    // Apply the individual speed modifier when updating the position
                    float individualMoveDistance = MoveDistance * boidUnique.MoveSpeedModifier;

                    // Update the boid's position and rotation
                    localToWorld.Value = float4x4.TRS(
                        new float3(localToWorld.Position.x + (nextHeading.x * individualMoveDistance), 0, localToWorld.Position.z + (nextHeading.z * individualMoveDistance)),
                        quaternion.LookRotationSafe(nextHeading, math.up()),
                        localToWorld.Value.Scale());
                }

            }
        }
        
        /// <summary>
        /// Updates the target vector used by the shader.
        /// Option A: use angular velocity (turn rate) as bend signal.
        /// </summary>
        [BurstCompile]
        partial struct UpdateTargetVectorJob : IJobEntity
        {
            public float DeltaTime;
            public float BendGain; // scales the x component fed to the shader
            public float DeadzoneRadians; // legacy angle deadzone (unused for angular velocity)
            public float AngularVelocityDeadzone; // rad/s deadzone
            public float MaxBendAbs; // clamp target bend magnitude

            void Execute(ref BoidUnique boidUnique, in LocalToWorld localToWorld)
            {
                // Normalize forward vectors
                float3 currentForward = math.normalizesafe(localToWorld.Forward);
                float3 previousForward = math.normalizesafe(boidUnique.PreviousHeading);

                // Horizontal plane (Y up)
                float3 up = new float3(0, 1, 0);
                float3 currentForwardHorizontal = math.normalizesafe(new float3(currentForward.x, 0, currentForward.z));
                float3 previousForwardHorizontal = math.normalizesafe(new float3(previousForward.x, 0, previousForward.z));

                // Angular displacement this frame (radians)
                float deltaAngle = SignedAngleBetween(previousForwardHorizontal, currentForwardHorizontal, up);
                // Convert to angular velocity (radians/sec)
                float angularVelocity = 0f;
                if (DeltaTime > 0f)
                {
                    angularVelocity = deltaAngle / DeltaTime;
                }

                // Optional micro deadzone on angular velocity
                if (AngularVelocityDeadzone > 0f)
                {
                    float absW = math.abs(angularVelocity);
                    if (absW <= AngularVelocityDeadzone)
                    {
                        angularVelocity = 0f;
                    }
                    else
                    {
                        float sign = math.sign(angularVelocity);
                        angularVelocity = sign * (absW - AngularVelocityDeadzone);
                    }
                }

                // Apply gain to produce shader bend
                float bend = angularVelocity * BendGain;
                // Clamp to avoid targets beyond shader-visible range
                bend = math.clamp(bend, -MaxBendAbs, MaxBendAbs);

                // Write only x component for shader bending
                boidUnique.TargetVector = new float3(bend, 0, 0);

                // Persist for next frame
                boidUnique.PreviousHeading = currentForward;
            }

            // Helper function to calculate the signed angle
            private float SignedAngleBetween(float3 from, float3 to, float3 axis)
            {
                float unsignedAngle = math.acos(math.clamp(math.dot(from, to), -1f, 1f));
                float3 crossProduct = math.cross(from, to);
                float sign = math.sign(math.dot(crossProduct, axis));
                return unsignedAngle * sign; // Angle in radians
            }
        }
        
        /// <summary>
        /// Updates the accumulated time used for shader animation.
        /// Handles animation speed scaling based on movement speed.
        /// </summary>
        [BurstCompile]
        public partial struct UpdateAccumulatedTimeJob : IJobEntity
        {
            public float DeltaTime;

            void Execute(ref AccumulatedTimeOverride accumulatedTimeOverride, in BoidShared boidShared, in BoidUnique boidUnique, ref AnimationSpeedOverride animationSpeedOverride)
            {
                // Shader uses both AccumulatedTime and AnimationSpeed
                accumulatedTimeOverride.Value += DeltaTime * boidShared.DefaultAnimationSpeed * boidUnique.MoveSpeedModifier;
                animationSpeedOverride.Value = boidShared.DefaultAnimationSpeed * boidUnique.MoveSpeedModifier;
            
                if (accumulatedTimeOverride.Value >= 1000000)
                {
                    accumulatedTimeOverride.Value -= 1000000;
                }
            }
        }
        
        /// <summary>
        /// Smoothly transitions between different movement speeds.
        /// </summary>
        [BurstCompile]
        public partial struct SmoothSpeedTransitionJob : IJobEntity
        {
            public float DeltaTime;

            void Execute(ref BoidUnique boidUnique, in BoidShared boidShared)
            {
                // Exponential smoothing that behaves well near zero speeds
                float k = math.max(0.0001f, boidShared.StateTransitionSpeed);
                float alpha = 1f - math.exp(-k * DeltaTime);
                boidUnique.MoveSpeedModifier = math.lerp(boidUnique.MoveSpeedModifier, boidUnique.TargetSpeedModifier, alpha);
            }
        }
        
        /// <summary>
        /// Smoothly transitions between different vector states for animation.
        /// </summary>
        [BurstCompile]
        public partial struct SmoothVectorTransitionJob : IJobEntity
        {
            public float DeltaTime;
            public float TransitionSpeed; // Slew rate (units per second)

            void Execute(ref CurrentVectorOverride currentVector, in BoidUnique boidUnique, in BoidShared boidShared)
            {
                // Constant slew rate to avoid sudden sign flips; symmetric for +/-
                // Always slew toward a clamped target so we don't chase unreachable values
                float xTarget = math.clamp(boidUnique.TargetVector.x, -BEND_MAX_ABS, BEND_MAX_ABS);
                float3 target = new float3(xTarget, 0f, 0f);
                float3 diff = target - currentVector.Value;
                float maxStep = TransitionSpeed * DeltaTime; // amount we can move this frame
                float dist = math.length(diff);
                if (dist <= maxStep)
                {
                    currentVector.Value = target;
                }
                else
                {
                    float3 dir = diff / dist;
                    currentVector.Value += dir * maxStep;
                }
            }
        }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct BoidLODSystem : ISystem
    {
        private EntityQuery sceneDataQuery;

        [BurstCompile]
        private partial struct LODJob : IJobEntity
        {
            [ReadOnly] public float3 CameraPosition;
            
            void Execute(in BoidShared boidShared, in LocalToWorld localToWorld, ref MaterialMeshInfo materialMeshInfo)
            {
                if (boidShared.NumberOfLODs <= 0)
                    return;

                float distanceToCamera = math.length(localToWorld.Position - CameraPosition);
                
                int lodLevel = 0;
                if (boidShared.NumberOfLODs > 2 && distanceToCamera > 60.0f)
                    lodLevel = 2;
                else if (boidShared.NumberOfLODs > 1 && distanceToCamera > 30.0f)
                    lodLevel = 1;

                lodLevel = math.clamp(lodLevel, 0, boidShared.NumberOfLODs - 1);
                
                materialMeshInfo = MaterialMeshInfo.FromRenderMeshArrayIndices(0, lodLevel);
            }
        }

        public void OnCreate(ref SystemState state)
        {
            sceneDataQuery = state.EntityManager.CreateEntityQuery(typeof(SceneData));
            state.RequireForUpdate(sceneDataQuery);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var sceneData = sceneDataQuery.GetSingleton<SceneData>();
            
            var job = new LODJob
            {
                CameraPosition = sceneData.CameraPosition
            };
            
            job.ScheduleParallel();
        }
    }
}
