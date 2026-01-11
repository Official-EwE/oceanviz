using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace OceanViz3
{
    /// <summary>
    /// Disables/Enables entities based on their distance to the camera.
    /// </summary>
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)] // Run early
    public partial struct DistanceCullingSystem : ISystem
    {
        private EntityQuery sceneDataQuery;
        private EntityQuery cullingEnabledQuery;
        private EntityQuery cullingDisabledQuery;

        public void OnCreate(ref SystemState state)
        {
            sceneDataQuery = state.EntityManager.CreateEntityQuery(typeof(SceneData));
            state.RequireForUpdate(sceneDataQuery);

            // Query for entities that ARE currently enabled
            cullingEnabledQuery = SystemAPI.QueryBuilder()
                .WithAll<CullingComponent, LocalToWorld>()
                .WithNone<Disabled>() // Exclude already disabled entities
                .Build();

            // Query for entities that ARE currently disabled
            cullingDisabledQuery = SystemAPI.QueryBuilder()
                .WithAll<CullingComponent, LocalToWorld, Disabled>() // Require disabled entities
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities) // Necessary to find disabled entities
                .Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int enabledCount = cullingEnabledQuery.CalculateEntityCount();
            int disabledCount = cullingDisabledQuery.CalculateEntityCount();

            if (enabledCount == 0 && disabledCount == 0) return;

            SceneData sceneData = sceneDataQuery.GetSingleton<SceneData>();
            float3 cameraPosition = sceneData.CameraPosition;

            // Get the ECB system singleton *inside OnUpdate*
            var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();

            // Get ECB from the system
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            var ecbParallel = ecb.AsParallelWriter();

            // Schedule job to potentially disable entities that are currently enabled
            var disableJob = new DisableOutOfRangeJob
            {
                CameraPosition = cameraPosition,
                ECB = ecbParallel
            };
            var disableHandle = disableJob.ScheduleParallel(cullingEnabledQuery, state.Dependency);

            // Schedule job to potentially enable entities that are currently disabled
            var enableJob = new EnableInRangeJob
            {
                CameraPosition = cameraPosition,
                ECB = ecbParallel
            };
            var enableHandle = enableJob.ScheduleParallel(cullingDisabledQuery, disableHandle); // Chain dependencies

            state.Dependency = enableHandle;
        }

        /// <summary>
        /// Job to check enabled entities and disable them if they are out of range.
        /// </summary>
        [BurstCompile]
        partial struct DisableOutOfRangeJob : IJobEntity
        {
            [ReadOnly] public float3 CameraPosition;
            public EntityCommandBuffer.ParallelWriter ECB;

            // Reads CullingComponent and LocalToWorld for currently ENABLED entities.
            void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in CullingComponent cullingData, in LocalToWorld localToWorld)
            {
                float maxDistanceSq = cullingData.MaxDistance * cullingData.MaxDistance;
                float distanceSq = math.distancesq(localToWorld.Position, CameraPosition);

                if (distanceSq > maxDistanceSq)
                {
                    ECB.AddComponent<Disabled>(chunkIndex, entity);
                }
            }
        }

        /// <summary>
        /// Job to check disabled entities and enable them if they are in range.
        /// </summary>
        [BurstCompile]
        partial struct EnableInRangeJob : IJobEntity
        {
            [ReadOnly] public float3 CameraPosition;
            public EntityCommandBuffer.ParallelWriter ECB;

            // Reads CullingComponent and LocalToWorld for currently DISABLED entities.
            // The presence of the Disabled component is implicit from the query.
            void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in CullingComponent cullingData, in LocalToWorld localToWorld)
            {
                float maxDistanceSq = cullingData.MaxDistance * cullingData.MaxDistance;
                float distanceSq = math.distancesq(localToWorld.Position, CameraPosition);

                if (distanceSq <= maxDistanceSq)
                {
                    ECB.RemoveComponent<Disabled>(chunkIndex, entity);
                }
            }
        }
    }
} 