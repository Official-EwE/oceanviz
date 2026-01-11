using UnityEngine;
using UnityEditor;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using System.Collections.Generic;
using OceanViz3;
using System.Collections;
using System.Threading.Tasks;
using System;
using UnityEngine.Animations;
using UnityEngine.Playables;
using GLTFast;
using System.IO;
using static GLTFast.AnimationMethod;

/// <summary>
/// System responsible for managing bone animated entities in a hybrid ECS's entity to GameObject mapping.
/// </summary>
public class BoneAnimatedEntityManager : MonoBehaviour
{
    private EntityManager entityManager;
    private EntityQuery entitiesWithBoneAnimationQuery;
    private Dictionary<Entity, GameObject> entityToGameObject = new Dictionary<Entity, GameObject>();
    private Dictionary<int, DynamicEntitiesGroup> dynamicEntityGroups = new Dictionary<int, DynamicEntitiesGroup>();
    private Dictionary<int, GameObject> templateModels = new Dictionary<int, GameObject>();
    
    private void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
    }

    private void Update()
    {
        UpdateQueryFilter();
        
        var entities = entitiesWithBoneAnimationQuery.ToEntityArray(Allocator.Temp);
        int enabledCount = 0;
        int totalTrackedCount = entityToGameObject.Count;
        
        try
        {
            // First clean up any destroyed or invalid entities
            var entitiesToRemove = new List<Entity>();
            foreach (var kvp in entityToGameObject)
            {
                if (!entityManager.Exists(kvp.Key))
                {
                    if (kvp.Value != null)
                    {
                        Destroy(kvp.Value);
                    }
                    entitiesToRemove.Add(kvp.Key);
                }
            }
            foreach (var entity in entitiesToRemove)
            {
                entityToGameObject.Remove(entity);
            }

            // Now process current entities
            foreach (var entity in entities)
            {
                if (!entityManager.Exists(entity))
                {
                    continue;
                }

                var boidShared = entityManager.GetSharedComponentManaged<BoidShared>(entity);
                
                // Skip if not bone animated
                if (!boidShared.BoneAnimated)
                {
                    continue;
                }

                bool isDisabled = entityManager.HasComponent<Disabled>(entity);

                if (!isDisabled)
                {
                    enabledCount++;
                }
                
                // Handle GameObject creation/destruction based on disabled state
                if (isDisabled && entityToGameObject.ContainsKey(entity))
                {
                    var go = entityToGameObject[entity];
                    if (go != null)
                    {
                        Destroy(go);
                    }
                    entityToGameObject.Remove(entity);
                }
                else if (!isDisabled && !entityToGameObject.ContainsKey(entity))
                {
                    CreateGameObjectPair(entity);
                }
                else if (!isDisabled && entityToGameObject.TryGetValue(entity, out GameObject go) && go != null)
                {
                    var localToWorld = entityManager.GetComponentData<LocalToWorld>(entity);
                    go.transform.position = localToWorld.Position;
                    go.transform.rotation = localToWorld.Rotation;
                    go.transform.localScale = new Vector3(localToWorld.Value.Scale().x, 
                                                        localToWorld.Value.Scale().y, 
                                                        localToWorld.Value.Scale().z);

                    // Update animation speed
                    var legacyAnimation = go.GetComponentInChildren<Animation>();
                    if (legacyAnimation != null)
                    {
                        var boidUnique = entityManager.GetComponentData<BoidUnique>(entity);
                        foreach (AnimationState state in legacyAnimation)
                        {
                            state.speed = boidUnique.MoveSpeedModifier;
                        }
                    }

                    var renderer = go.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (renderer != null)
                    {
                        var material = renderer.material;
                        material.SetFloat("_AccumulatedTime", 
                            entityManager.GetComponentData<AccumulatedTimeOverride>(entity).Value);
                        material.SetVector("_ScreenDisplayStart", 
                            entityManager.GetComponentData<ScreenDisplayStartOverride>(entity).Value);
                        material.SetVector("_ScreenDisplayEnd", 
                            entityManager.GetComponentData<ScreenDisplayEndOverride>(entity).Value);
                    }
                }
            }
        }
        finally
        {
            if (entities.IsCreated)
            {
                entities.Dispose();
            }
        }
    }

    private void UpdateQueryFilter()
    {
        if (entitiesWithBoneAnimationQuery == default)
        {
            entitiesWithBoneAnimationQuery = entityManager.CreateEntityQuery(
                new EntityQueryDesc
                {
                    All = new Unity.Entities.ComponentType[] 
                    { 
                        Unity.Entities.ComponentType.ReadOnly<BoidShared>(),
                        Unity.Entities.ComponentType.ReadOnly<LocalToWorld>(),
                        Unity.Entities.ComponentType.ReadOnly<BoidUnique>()
                    },
                    Options = EntityQueryOptions.IncludeDisabledEntities
                }
            );
        }
    }

    public void RegisterDynamicEntityGroup(DynamicEntitiesGroup group, GameObject template)
    {
        // Remove any existing registration first
        if (dynamicEntityGroups.ContainsKey(group.DynamicEntityId))
        {
            UnregisterDynamicEntityGroup(group);
        }

        dynamicEntityGroups[group.DynamicEntityId] = group;
        templateModels[group.DynamicEntityId] = template;
        template.transform.parent = transform;
        Debug.Log($"[BoneAnimatedEntityManager] Successfully registered template for {group.name}");
    }

    private void CreateGameObjectPair(Entity entity)
    {
        var boidShared = entityManager.GetSharedComponentManaged<BoidShared>(entity);
        
        if (!dynamicEntityGroups.TryGetValue(boidShared.DynamicEntityId, out var group))
        {
            Debug.LogError($"[BoneAnimatedEntityManager] No DynamicEntitiesGroup found for entity {boidShared.DynamicEntityId}_{boidShared.BoidSchoolId}");
            return;
        }

        if (!templateModels.TryGetValue(boidShared.DynamicEntityId, out var template))
        {
            Debug.LogError($"[BoneAnimatedEntityManager] No template model found for entity {boidShared.DynamicEntityId}_{boidShared.BoidSchoolId}");
            return;
        }

        GameObject go = new GameObject($"BoneAnimatedMesh_{boidShared.DynamicEntityId}_{boidShared.BoidSchoolId}");
        go.transform.parent = transform;

        GameObject instance = Instantiate(template, go.transform);
        instance.name = $"Model_{boidShared.DynamicEntityId}_{boidShared.BoidSchoolId}";
        instance.SetActive(true);
        
        var boidUnique = entityManager.GetComponentData<BoidUnique>(entity);
        var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
        if (renderer != null)
        {
            var material = renderer.material;
            material.SetFloat("_AnimationSpeed", 
                entityManager.GetComponentData<AnimationSpeedOverride>(entity).Value);
            material.SetFloat("_SineWavelength", entityManager.GetComponentData<SineWavelengthOverride>(entity).Value);
            var sineDeform = entityManager.GetComponentData<SineDeformationAmplitudeOverride>(entity).Value;
            material.SetVector("_SineDeformationAmplitude", new Vector4(sineDeform.x, sineDeform.y, sineDeform.z, 0));
            material.SetFloat("_Secondary1AnimationAmplitude", entityManager.GetComponentData<Secondary1AnimationAmplitudeOverride>(entity).Value);
            material.SetFloat("_InvertSecondary1Animation", entityManager.GetComponentData<InvertSecondary1AnimationOverride>(entity).Value);
            var sec2Anim = entityManager.GetComponentData<Secondary2AnimationAmplitudeOverride>(entity).Value;
            material.SetVector("_Secondary2AnimationAmplitude", new Vector4(sec2Anim.x, sec2Anim.y, sec2Anim.z, 0));
            material.SetFloat("_InvertSecondary2Animation", entityManager.GetComponentData<InvertSecondary2AnimationOverride>(entity).Value);
            var sideToSide = entityManager.GetComponentData<SideToSideAmplitudeOverride>(entity).Value;
            material.SetVector("_SideToSideAmplitude", new Vector4(sideToSide.x, sideToSide.y, sideToSide.z, 0));
            var yaw = entityManager.GetComponentData<YawAmplitudeOverride>(entity).Value;
            material.SetVector("_YawAmplitude", new Vector4(yaw.x, yaw.y, yaw.z, 0));
            var rolling = entityManager.GetComponentData<RollingSpineAmplitudeOverride>(entity).Value;
            material.SetVector("_RollingSpineAmplitude", new Vector4(rolling.x, rolling.y, rolling.z, 0));
            material.SetFloat("_MeshZMin", entityManager.GetComponentData<MeshZMinOverride>(entity).Value);
            material.SetFloat("_MeshZMax", entityManager.GetComponentData<MeshZMaxOverride>(entity).Value);
            material.SetFloat("_PositiveYClip", entityManager.GetComponentData<PositiveYClipOverride>(entity).Value);
            material.SetFloat("_NegativeYClip", entityManager.GetComponentData<NegativeYClipOverride>(entity).Value);
            material.SetFloat("_AccumulatedTime", 
                entityManager.GetComponentData<AccumulatedTimeOverride>(entity).Value);
            material.SetVector("_ScreenDisplayStart", 
                entityManager.GetComponentData<ScreenDisplayStartOverride>(entity).Value);
            material.SetVector("_ScreenDisplayEnd", 
                entityManager.GetComponentData<ScreenDisplayEndOverride>(entity).Value);
        }

        var legacyAnimation = instance.GetComponentInChildren<Animation>();
        if (legacyAnimation != null)
        {
            legacyAnimation.wrapMode = WrapMode.Loop;
            foreach (AnimationState state in legacyAnimation)
            {
                state.speed = boidUnique.MoveSpeedModifier;
            }
            legacyAnimation.Play();
        }
        
        entityToGameObject.Add(entity, go);
    }

    public async void ReloadGroupModels(DynamicEntitiesGroup group)
    {
        var newTemplate = group.GetTemplateGameObject();
        if (newTemplate == null)
        {
            Debug.LogError($"[BoneAnimatedEntityManager] No template available for {group.name}");
            return;
        }

        if (templateModels.TryGetValue(group.DynamicEntityId, out var oldTemplate))
        {
            Destroy(oldTemplate);
        }
        templateModels[group.DynamicEntityId] = newTemplate;
        newTemplate.transform.parent = transform;

        // Update all existing instances
        foreach (var kvp in entityToGameObject)
        {
            var entity = kvp.Key;
            var go = kvp.Value;
            
            if (!entityManager.Exists(entity)) continue;
            
            var boidShared = entityManager.GetSharedComponentManaged<BoidShared>(entity);
            if (boidShared.DynamicEntityId == group.DynamicEntityId)
            {
                foreach (Transform child in go.transform)
                {
                    Destroy(child.gameObject);
                }
                
                foreach (Transform child in newTemplate.transform)
                {
                    Instantiate(child.gameObject, go.transform);
                }
            }
        }
    }

    public void UnregisterDynamicEntityGroup(DynamicEntitiesGroup group)
    {
        _ = UnregisterDynamicEntityGroupAsync(group);
    }

    private async Task UnregisterDynamicEntityGroupAsync(DynamicEntitiesGroup group)
    {
        if (templateModels.TryGetValue(group.DynamicEntityId, out var template))
        {
            Destroy(template);
            templateModels.Remove(group.DynamicEntityId);
        }
        dynamicEntityGroups.Remove(group.DynamicEntityId);
    }

    private void OnDestroy()
    {
        foreach (var go in entityToGameObject.Values)
        {
            if (go != null)
            {
                Destroy(go);
            }
        }
        entityToGameObject.Clear();
    }
} 