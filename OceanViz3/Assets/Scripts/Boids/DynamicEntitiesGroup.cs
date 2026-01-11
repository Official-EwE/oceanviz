using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Numerics;
using System.Linq;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;
using System.IO;
using UnityEngine.Serialization;
using GameObject = UnityEngine.GameObject;
using System.Threading.Tasks;
using Unity.Collections;
using GLTFast.Loading;
using GLTFast;

namespace OceanViz3
{
/// <summary>
/// Represents a preset configuration for a dynamic entity, containing behavioral and visual parameters.
/// </summary>
[Serializable]
public class DynamicEntityPreset
{
    public string name;
    
    public int maxPopulation;
    
    public string[] habitats;
    public bool seabed_bound;
    public bool predator;
    public bool prey;
    public float cell_radius;
    public float separation_weight;
    public float alignment_weight;
    public float target_weight;
    public float obstacle_aversion_distance;
    public float move_speed;
    public float state_transition_speed;
    public float state_change_timer_min;
    public float state_change_timer_max;
    public float max_vertical_angle;
    
    public float max_turn_rate = 1.0f;
    public float speed_modifier_min;
    public float speed_modifier_max;
    
    // Animation Shader Properties
    public float animation_speed;
    public float sine_wavelength;
    public Vector3Data sine_deformation_amplitude;
    public float secondary1_animation_amplitude;
    public float invert_secondary1_animation;
    public Vector3Data secondary2_animation_amplitude;
    public float invert_secondary2_animation;
    public Vector3Data side_to_side_amplitude;
    public Vector3Data yaw_amplitude;
    public Vector3Data rolling_spine_amplitude;
    public float meshZMin;
    public float meshZMax;
    public float positive_y_clip;
    public float negative_y_clip;
    
    // Not in json
    public Texture baseColorTexture;
    public Texture normalTexture;
    public Texture roughnessTexture;
    public Texture metallicTexture;
    public bool bone_animated;
    
    // Computed at load time (base mesh dimensions, no per-instance scaling)
    public Vector3Data mesh_size; // x,y,z extents
    public float mesh_largest_dimension;
}

/// <summary>
/// Represents a boid school structure containing identification and entity references.
/// </summary>
[Serializable]
public struct BoidSchoolStruct
{
    public int BoidSchoolId;
    public Entity boidSchoolEntity;
    public GameObject boidBounds;
}
    
/// <summary>
/// Manages a group of dynamic entities in the ocean visualization system.
/// Handles entity spawning, configuration, and lifecycle management for groups of boids.
/// </summary>
[Serializable]
public class DynamicEntitiesGroup
{
    public int DynamicEntityId;
    public string name;
    public DynamicEntityPreset dynamicEntityPreset;
    public Material material;
    
    // Optional override for habitats used to compute boid bounds on location changes
    private string[] overrideHabitats;

    private EntityManager entityManager;
    private EntityLibrary entityLibrary;
    
    private List<GameObject> dynamicEntityGroupBounds;
    private int viewsCount = -1;
    private int[] viewVisibilityPercentageArray = new int[4] {100, 100, 100, 100};
    
    [SerializeField]
    public List<BoidSchoolStruct> boidSchoolStructs = new List<BoidSchoolStruct>();

    // GUI
    private VisualElement dataRow;
    private SliderInt populationSlider;
    private Button reloadButton;
    private Button deleteButton;
    // Sliders
    private List<SliderInt> populationPercentageSliderInts = new List<SliderInt>();

    // Delegates
    public delegate void GroupDeleteRequestHandler(DynamicEntitiesGroup dynamicEntitiesGroup);
    public event GroupDeleteRequestHandler OnDeleteRequest;

    private EventCallback<ChangeEvent<int>> populationSliderChangeCallback;

    private Entity boidPrototype;

    public GLTFast.GltfImport gltf { get; private set; }

    private GameObject templateGameObject;

    // Rename lodMeshes to meshes and make it public
    public Mesh[] meshes;
    private const int LOD_COUNT = 3; // LOD0, LOD1, LOD2

    /// <summary>
    /// Checks if the dynamic entities group is properly initialized and ready for use.
    /// This is used by the SimulationAPI.
    /// </summary>
    public bool IsReady
    {
        get
        {
            // Check if mesh and material are loaded
            if (meshes == null || material == null)
                return false;

            // Check if we have any boid schools created
            if (boidSchoolStructs == null || boidSchoolStructs.Count == 0)
                return false;

            // Check if all boid schools are properly set up with entities
            foreach (var boidSchool in boidSchoolStructs)
            {
                if (boidSchool.boidSchoolEntity == Entity.Null)
                    return false;

                // Check if the entity still exists and has the BoidSchool component
                if (!entityManager.Exists(boidSchool.boidSchoolEntity) || 
                    !entityManager.HasComponent<BoidSchoolComponent>(boidSchool.boidSchoolEntity))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Sets the habitat names to be used instead of the preset habitats when recalculating bounds.
    /// </summary>
    /// <param name="habitats">Array of habitat names</param>
    public void SetOverrideHabitats(string[] habitats)
    {
        if (habitats == null || habitats.Length == 0)
        {
            Debug.LogError("[DynamicEntitiesGroup] SetOverrideHabitats requires at least one habitat name.");
            Debug.Assert(false, "SetOverrideHabitats called with empty habitats.");
            return;
        }
        // Copy to avoid external mutation
        overrideHabitats = habitats.ToArray();
    }

    /// <summary>
    /// Returns the currently active habitats for this group (override first, else preset).
    /// </summary>
    public IEnumerable<string> GetActiveHabitats()
    {
        if (overrideHabitats != null && overrideHabitats.Length > 0)
        {
            return overrideHabitats;
        }
        if (dynamicEntityPreset != null && dynamicEntityPreset.habitats != null)
        {
            return dynamicEntityPreset.habitats;
        }
        return Array.Empty<string>();
    }

    /// <summary>
    /// Initializes the dynamic entities group with necessary components and UI elements.
    /// </summary>
    /// <param name="name">Name identifier for the group</param>
    /// <param name="dataRow">UI element containing group controls</param>
    /// <param name="viewsCount">Number of active views</param>
    /// <param name="dynamicEntityId">Unique identifier for the entity group</param>
    /// <param name="dynamicEntityPreset">Configuration preset for the entities</param>
    /// <param name="boidBounds">List of GameObjects defining the boundaries for boid movement</param>
    public void Setup(string name, VisualElement dataRow, int viewsCount, int dynamicEntityId, DynamicEntityPreset dynamicEntityPreset, List<GameObject> boidBounds)
    {
        // Resources
        World world = World.DefaultGameObjectInjectionWorld;
        entityManager = world.EntityManager;
        Entity entityLibraryEntity = entityManager.CreateEntityQuery(typeof(EntityLibrary)).GetSingletonEntity();
		entityLibrary = entityManager.GetComponentData<EntityLibrary>(entityLibraryEntity);

        this.DynamicEntityId = dynamicEntityId;
        this.name = name;
        this.dynamicEntityGroupBounds = boidBounds;
        this.viewsCount = viewsCount;
        this.dynamicEntityPreset = dynamicEntityPreset;

        // Ensure initial visibility is 100% for all active views
        for (int i = 0; i < viewVisibilityPercentageArray.Length; i++)
        {
            if (i < this.viewsCount)
            {
                viewVisibilityPercentageArray[i] = 100;
            }
        }

        //// GUI
        this.dataRow = dataRow;
        if (dataRow != null)
        {
            this.populationSlider = dataRow.Q<SliderInt>("PopulationSliderInt");
            Label nameLabel = dataRow.Q<Label>("NameLabel");
            if (nameLabel != null)
            {
                nameLabel.text = name;
            }
            else
            {
                Debug.LogWarning($"[DynamicEntitiesGroup] NameLabel not found in DataRow for group '{name}'.");
             // Fallback if NameLabel doesn't exist
            if (this.populationSlider != null) { /* Potentially set a label if needed, or rely on a separate NameLabel */ }
            }

        if (this.populationSlider != null)
            {
            this.populationSlider.lowValue = 0;
            if (this.dynamicEntityPreset.maxPopulation > 0)
            {
                this.populationSlider.highValue = this.dynamicEntityPreset.maxPopulation;
            }
            else
            {
                this.populationSlider.highValue = 1000; // Fallback
                Debug.LogWarning($"[DynamicEntitiesGroup] Setup: maxPopulation for {name} is invalid or zero ({this.dynamicEntityPreset.maxPopulation}), defaulting slider highValue to 1000.");
            }
            populationSliderChangeCallback = (evt) => OnPopulationSliderValueChanged(evt);
            this.populationSlider.RegisterValueChangedCallback(populationSliderChangeCallback);
            }
            else
            {
            Debug.LogError("[DynamicEntitiesGroup] PopulationSliderInt not found in DataRow!");
            }

        // Sliders
            for (int i = 0; i < 4; i++)
            {
                SliderInt sliderInt = dataRow.Q<SliderInt>("PopulationPercentageSliderInt" + i);
                    populationPercentageSliderInts.Add(sliderInt);

            sliderInt.RegisterValueChangedCallback((evt) => OnPopulationPercentageSliderIntChanged(evt));

            // Set the sliders visibility according to the amount of views
            if (i < viewsCount)
                    {
                sliderInt.style.display = DisplayStyle.Flex;
                }
                else
                {
                sliderInt.style.display = DisplayStyle.None;
                }

            // Set the value of the slider
            sliderInt.value = viewVisibilityPercentageArray[i];
            }
        deleteButton = dataRow.Q<Button>("DeleteButton");
        deleteButton.RegisterCallback<ClickEvent>((evt) => DeleteGroupClicked(evt));
        reloadButton = dataRow.Q<Button>("ReloadButton");
        reloadButton.RegisterCallback<ClickEvent>((evt) => ReloadGroup(evt));
        }
    }

    /// <summary>
    /// Reloads the entire group, destroying existing entities and recreating them with updated presets.
    /// Maintains the current population count.
    /// </summary>
    public async Task ReloadGroup(ClickEvent evt)
    {
        Debug.Log("Reload group " + name);

        // Remember current population, currently the population is the same for all boidSchoolStructs
        var oldPopulation = 0;
        if (boidSchoolStructs.Count > 0 && entityManager.Exists(boidSchoolStructs[0].boidSchoolEntity))
        {
            oldPopulation = entityManager.GetComponentData<BoidSchoolComponent>(boidSchoolStructs[0].boidSchoolEntity).RequestedCount;
        }

        foreach (BoidSchoolStruct boidSchoolStruct in boidSchoolStructs)
        {
            if (entityManager.Exists(boidSchoolStruct.boidSchoolEntity))
            {
                // Mark old boidSchool for destruction
                var boidSchool = entityManager.GetComponentData<BoidSchoolComponent>(boidSchoolStruct.boidSchoolEntity);
                boidSchool.DestroyRequested = true;
                entityManager.SetComponentData(boidSchoolStruct.boidSchoolEntity, boidSchool);
            }
        }
        
        // Clear the boidSchoolStructs list
        boidSchoolStructs.Clear();
        
        // Update all presets
        GroupPresetsManager.Instance.UpdatePresets();

        await LoadAndSpawnGroup(); // This will create new boidSchoolEntity/Entities

        // Set the population to the old population
        Debug.Log("Setting population back to " + oldPopulation);
        SetPopulationAndUpdateGUIState(oldPopulation);
    }

    /// <summary>
    /// Loads assets and spawns the dynamic entity group.
    /// Handles mesh, texture, and material loading, and creates boid schools.
    /// </summary>
    /// <returns>Task representing the async loading operation</returns>
    /// <exception cref="Exception">Thrown when required assets are missing or fail to load</exception>
    public async Task LoadAndSpawnGroup()
    {
        // Resolve preset using the preset's name, not the group's display name
        string presetName = dynamicEntityPreset != null && !string.IsNullOrEmpty(dynamicEntityPreset.name) ? dynamicEntityPreset.name : this.name;
        var preset = GroupPresetsManager.Instance.dynamicEntitiesPresetsList
            .FirstOrDefault(p => p.name == presetName);
        if (preset == null)
        {
            Debug.LogError("DynamicEntityPreset not found for " + presetName);
            throw new Exception("DynamicEntityPreset not found for " + presetName);
        }
        dynamicEntityPreset = preset;
        
        var presetFolderName = dynamicEntityPreset.name;
        var gltfPath = Application.streamingAssetsPath + "/DynamicEntities/" + presetFolderName + "/model.glb";
        if (!File.Exists(gltfPath))
        {
            Debug.LogError("model.glb not found in " + presetFolderName);
            throw new Exception("model.glb not found in " + presetFolderName);
        }

        var baseColorPath = Application.streamingAssetsPath + "/DynamicEntities/" + presetFolderName + "/base_color.png";
        if (!File.Exists(baseColorPath))
        {
            Debug.LogError("base_color.png not found in " + presetFolderName);
            throw new Exception("base_color.png not found in " + presetFolderName);
        }

        var normalPath = Application.streamingAssetsPath + "/DynamicEntities/" + presetFolderName + "/normal.png";
        if (!File.Exists(normalPath))
        {
            Debug.LogError("normal.png not found in " + presetFolderName);
            throw new Exception("normal.png not found in " + presetFolderName);
        }
        
        var roughnessPath = Application.streamingAssetsPath + "/DynamicEntities/" + presetFolderName + "/roughness.png";
        if (!File.Exists(roughnessPath))
        {
            Debug.LogWarning("roughness.png not found in " + presetFolderName);
        }

        var metallicPath = Application.streamingAssetsPath + "/DynamicEntities/" + presetFolderName + "/metallic.png";
        if (!File.Exists(metallicPath))
        {
            Debug.LogWarning("metallic.png not found in " + presetFolderName);
        }

        var emissionPath = Application.streamingAssetsPath + "/DynamicEntities/" + presetFolderName + "/emission.png";
        if (!File.Exists(emissionPath))
        {
            Debug.LogWarning("emission.png not found in " + presetFolderName);
        }
        
        // Determine initial population as 25% of maxPopulation
        int initialPopulation;
        if (dynamicEntityPreset.maxPopulation <= 0)
        {
            initialPopulation = 0;
            Debug.LogWarning($"[DynamicEntitiesGroup] LoadAndSpawnGroup: maxPopulation for {name} is {dynamicEntityPreset.maxPopulation}. Initial population set to 0.");
        }
        else
        {
            initialPopulation = Mathf.RoundToInt(dynamicEntityPreset.maxPopulation * 0.25f);
        }
        
        // Validate preset dynamic movement parameters early so we fail fast on bad JSON.
        // state_transition_speed is a rate (1/seconds), not a duration; it must be non-negative.
        Debug.Assert(dynamicEntityPreset.state_transition_speed >= 0.0f,
            $"[DynamicEntitiesGroup] Invalid state_transition_speed ({dynamicEntityPreset.state_transition_speed}) for preset {dynamicEntityPreset.name}");
        Debug.Assert(dynamicEntityPreset.speed_modifier_min <= dynamicEntityPreset.speed_modifier_max,
            $"[DynamicEntitiesGroup] speed_modifier_min ({dynamicEntityPreset.speed_modifier_min}) must be <= speed_modifier_max ({dynamicEntityPreset.speed_modifier_max}) for preset {dynamicEntityPreset.name}");

        gltf = new GLTFast.GltfImport();
        var importSettings = new ImportSettings {
            AnimationMethod = AnimationMethod.Legacy,
            GenerateMipMaps = true
        };
        
        // Load all LOD meshes
        var lodPaths = new string[LOD_COUNT];
        lodPaths[0] = gltfPath;  // Base model (LOD0)
        lodPaths[1] = Application.streamingAssetsPath + "/DynamicEntities/" + presetFolderName + "/model_LOD1.glb";
        lodPaths[2] = Application.streamingAssetsPath + "/DynamicEntities/" + presetFolderName + "/model_LOD2.glb";

        // Initialize meshes array
        meshes = new Mesh[LOD_COUNT];

        // Load LOD0 (base model)
        var success = await gltf.Load(lodPaths[0], importSettings);
        if (!success) {
            Debug.LogError("[DynamicEntitiesGroup] Loading LOD0 glTF failed for " + presetFolderName + "!");
            throw new Exception("Loading LOD0 glTF failed for " + presetFolderName + "!");
        }
        meshes[0] = gltf.GetMeshes()[0];
        
        // Load LOD1 and LOD2
        for (int i = 1; i < LOD_COUNT; i++)
        {
            if (File.Exists(lodPaths[i]))
            {
                var lodGltf = new GLTFast.GltfImport();
                success = await lodGltf.Load(lodPaths[i], importSettings);
                if (success)
                {
                    meshes[i] = lodGltf.GetMeshes()[0];
                    Debug.Log("[DynamicEntitiesGroup] Loaded LOD" + i + " for " + presetFolderName);
                }
                else
                {
                    Debug.LogWarning("[DynamicEntitiesGroup] Failed to load LOD" + i + " for " + presetFolderName + ", using LOD0 as fallback");
                    meshes[i] = meshes[0];
                }
            }
            else
            {
                Debug.LogWarning("[DynamicEntitiesGroup] LOD" + i + " file not found for " + presetFolderName + ", using LOD0 as fallback");
                meshes[i] = meshes[0];
            }
        }

        // After loading meshes
        Debug.Log("[DynamicEntitiesGroup] Loaded " + meshes.Length + " LOD meshes for " + presetFolderName + ":");
        for (int i = 0; i < meshes.Length; i++) {
            Debug.Log($"[DynamicEntitiesGroup] LOD{i}: {(meshes[i] != null ? "Loaded" : "NULL")}");
        }

        // Update mesh reference in vertex calculation
        UnityEngine.Vector3[] vertices = meshes[0].vertices;
        float meshXMin = float.MaxValue;
        float meshYMin = float.MaxValue;
        float meshZMin = float.MaxValue;
        float meshXMax = float.MinValue;
        float meshYMax = float.MinValue;
        float meshZMax = float.MinValue;
        foreach (UnityEngine.Vector3 vertex in vertices)
        {
            if (vertex.x < meshXMin) meshXMin = vertex.x;
            if (vertex.x > meshXMax) meshXMax = vertex.x;
            if (vertex.y < meshYMin) meshYMin = vertex.y;
            if (vertex.y > meshYMax) meshYMax = vertex.y;
            if (vertex.z < meshZMin) meshZMin = vertex.z;
            if (vertex.z > meshZMax) meshZMax = vertex.z;
        }

        float dx = meshXMax - meshXMin;
        float dy = meshYMax - meshYMin;
        float dz = meshZMax - meshZMin;

        dynamicEntityPreset.meshZMin = meshZMin;
        dynamicEntityPreset.meshZMax = meshZMax;
        dynamicEntityPreset.mesh_size = new Vector3Data { x = dx, y = dy, z = dz };
        float largest = dx;
        if (dy > largest) largest = dy;
        if (dz > largest) largest = dz;
        dynamicEntityPreset.mesh_largest_dimension = largest;

        try {
            byte[] fileData = File.ReadAllBytes(baseColorPath);
            Texture2D baseColorTexture = new Texture2D(2, 2);
            if (!baseColorTexture.LoadImage(fileData))
            {
                throw new Exception("Base color texture failed to load");
            }

            fileData = File.ReadAllBytes(normalPath);
            Texture2D loadedNormalTexture = new Texture2D(2, 2);
            if (!loadedNormalTexture.LoadImage(fileData))
            {
                throw new Exception("Normal texture failed to load");
            }
            Texture2D normalTexture = new Texture2D(loadedNormalTexture.width, loadedNormalTexture.height, loadedNormalTexture.format, true, true);
            Graphics.CopyTexture(loadedNormalTexture, normalTexture);

            // Optional textures
            Texture2D roughnessTexture = null;
            if (File.Exists(roughnessPath))
            {
                roughnessTexture = LoadTextureOnMainThread(roughnessPath);
            }

            Texture2D metallicTexture = null;
            if (File.Exists(metallicPath))
            {
                metallicTexture = LoadTextureOnMainThread(metallicPath);
            }

            Texture2D emissionTexture = null;
            if (File.Exists(emissionPath))
            {
                emissionTexture = LoadTextureOnMainThread(emissionPath);
            }

            // Create material
            material = new Material(Shader.Find("Shader Graphs/FishAdvancedShaderGraph"));
            material.SetTexture("_BaseColor", baseColorTexture);
            material.SetTexture("_Normal", normalTexture);
            if (roughnessTexture != null)
                material.SetTexture("_Roughness", roughnessTexture);
            if (metallicTexture != null)
                material.SetTexture("_Metallic", metallicTexture);
            if (emissionTexture != null)
                material.SetTexture("_Emission", emissionTexture);

        } catch (Exception e) {
            Debug.LogError($"Error loading textures: {e.Message}");
            throw;
        }

        // Create a unique prototype for this group by duplicating the library prototype
        boidPrototype = entityManager.Instantiate(entityLibrary.BoidEntity);
        entityManager.AddComponentData(boidPrototype, new Prefab());
        
        // Set up rendering with LOD support
        var renderMeshDescription = new RenderMeshDescription(
            shadowCastingMode: ShadowCastingMode.Off,
            receiveShadows: false);

        // Update the RenderMeshArray creation to use meshes
        var renderMeshArray = new RenderMeshArray(
            new Material[] { material },
            meshes
        );

        RenderMeshUtility.AddComponents(
            boidPrototype,
            entityManager,
            renderMeshDescription,
            renderMeshArray,
            MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)  // Use FromRenderMeshArrayIndices to get correct negative indices
        );

        // Create boid schools using the group's prototype
        for (int i = 0; i < dynamicEntityGroupBounds.Count; i++)
        {
            CreateBoidSchool(i, initialPopulation);
        }

        // After spawning, push initial view visibility to ECS
        foreach (BoidSchoolStruct boidSchoolStruct in boidSchoolStructs)
        {
            UpdateBoidSchoolViewportVisibility(boidSchoolStruct.boidSchoolEntity);
        }

        // Set NumberOfLODs based on actual unique mesh count AFTER creating boid schools
        foreach (BoidSchoolStruct boidSchoolStruct in boidSchoolStructs)
        {
            // Count unique meshes (don't count LOD0 duplicates used as fallbacks)
            int uniqueLODCount = 1; // Start with 1 for LOD0
            for (int i = 1; i < meshes.Length; i++)
            {
                if (meshes[i] != null && meshes[i] != meshes[0])
                {
                    uniqueLODCount++;
                }
            }

            var boidSchool = entityManager.GetComponentData<BoidSchoolComponent>(boidSchoolStruct.boidSchoolEntity);
            boidSchool.NumberOfLODs = uniqueLODCount;
            entityManager.SetComponentData(boidSchoolStruct.boidSchoolEntity, boidSchool);
        }
        
        if (populationSlider != null)
        {
            // Ensure the slider's high value is consistent with the (potentially reloaded) preset's maxPopulation
            int targetSliderHigh = (dynamicEntityPreset.maxPopulation > 0) ? dynamicEntityPreset.maxPopulation : 1000;
            if (populationSlider.highValue != targetSliderHigh)
            {
                if (populationSliderChangeCallback != null) populationSlider.UnregisterValueChangedCallback(populationSliderChangeCallback);
                populationSlider.highValue = targetSliderHigh;
                if (populationSliderChangeCallback != null) populationSlider.RegisterValueChangedCallback(populationSliderChangeCallback);
                
                if (targetSliderHigh == 1000 && dynamicEntityPreset.maxPopulation <= 0) // Log if fallback was used
                {
                     Debug.LogWarning($"[DynamicEntitiesGroup] LoadAndSpawnGroup: maxPopulation for {name} is invalid ({dynamicEntityPreset.maxPopulation}). Slider highValue set to fallback 1000.");
                }
            }
            populationSlider.SetValueWithoutNotify(initialPopulation);
        }

        // Create template GameObject for bone animation if needed
        if (dynamicEntityPreset.bone_animated)
        {
            templateGameObject = new GameObject($"Template_{name}");
            templateGameObject.SetActive(false);
            
            var instantiator = new GameObjectInstantiator(gltf, templateGameObject.transform);
            var instantiateSuccess = await gltf.InstantiateMainSceneAsync(instantiator);
            
            if (!instantiateSuccess)
            {
                Debug.LogError($"[DynamicEntitiesGroup] Failed to instantiate scene for {name}");
                throw new Exception($"Failed to instantiate scene for {name}");
            }

            // Setup animation
            var legacyAnimation = instantiator.SceneInstance.LegacyAnimation;
            if (legacyAnimation != null) {
                legacyAnimation.wrapMode = WrapMode.Loop;
                legacyAnimation.Play();
            }

            // Apply the material to all mesh renderers in the template
            var meshRenderers = templateGameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var renderer in meshRenderers)
            {
                renderer.material = material;
            }
        }

        // If this group uses bone animation, register with the manager
        if (dynamicEntityPreset.bone_animated)
        {
            var manager = GameObject.FindObjectOfType<BoneAnimatedEntityManager>();
            if (manager != null)
            {
                manager.RegisterDynamicEntityGroup(this, templateGameObject);
            }
        }
    }

    /// <summary>
    /// Creates a new boid school within the specified bounds.
    /// </summary>
    /// <param name="index">Index of the school within the group</param>
    /// <param name="population">Initial population count for the school</param>
    private void CreateBoidSchool(int index, int population)
    {
        GameObject schoolBoidBounds = dynamicEntityGroupBounds[index];
        
        BoidSchoolStruct boidSchoolStruct = new BoidSchoolStruct();
        boidSchoolStruct.boidBounds = schoolBoidBounds;
        boidSchoolStruct.BoidSchoolId = index;
        
        float3 boundsCenter = new float3(schoolBoidBounds.transform.position);
        
        BoxCollider boxCollider = schoolBoidBounds.GetComponent<BoxCollider>();
        float3 boundsSize = new float3(
            boxCollider.size.x * schoolBoidBounds.transform.localScale.x,
            boxCollider.size.y * schoolBoidBounds.transform.localScale.y,
            boxCollider.size.z * schoolBoidBounds.transform.localScale.z
        );

        Entity boidSchoolEntity = entityManager.Instantiate(entityLibrary.BoidSchoolEntity);
        entityManager.SetName(boidSchoolEntity, "BoidSchool_" + DynamicEntityId + "_" + boidSchoolStruct.BoidSchoolId);
        
        entityManager.SetComponentData(boidSchoolEntity, CreateBoidSchoolData(boidSchoolStruct.BoidSchoolId, boundsCenter, boundsSize, population));
        
        boidSchoolStruct.boidSchoolEntity = boidSchoolEntity;
        boidSchoolStructs.Add(boidSchoolStruct);
    }

    private BoidSchoolComponent CreateBoidSchoolData(int schoolId, float3 boundsCenter, float3 boundsSize, int population)
    {
        return new BoidSchoolComponent
        {
            DynamicEntityId = DynamicEntityId,
            BoidSchoolId = schoolId,
            BoidPrototype = boidPrototype,
            BoundsCenter = boundsCenter,
            BoundsSize = boundsSize,
            BoidTargetPrefab = entityLibrary.BoidTargetEntity,
            DestroyRequested = false,
            RequestedCount = population,
            ViewsCount = viewsCount,
            ViewVisibilityPercentages = new float4(
                viewVisibilityPercentageArray[0],
                viewVisibilityPercentageArray[1],
                viewVisibilityPercentageArray[2],
                viewVisibilityPercentageArray[3]),

            // Animation Shader Properties
            AnimationSpeed = dynamicEntityPreset.animation_speed,
            SineWavelength = dynamicEntityPreset.sine_wavelength,
            SineDeformationAmplitude = dynamicEntityPreset.sine_deformation_amplitude.ToUnityVector3(),
            Secondary1AnimationAmplitude = dynamicEntityPreset.secondary1_animation_amplitude,
            InvertSecondary1Animation = dynamicEntityPreset.invert_secondary1_animation,
            Secondary2AnimationAmplitude = dynamicEntityPreset.secondary2_animation_amplitude.ToUnityVector3(),
            InvertSecondary2Animation = dynamicEntityPreset.invert_secondary2_animation,
            SideToSideAmplitude = dynamicEntityPreset.side_to_side_amplitude.ToUnityVector3(),
            YawAmplitude = dynamicEntityPreset.yaw_amplitude.ToUnityVector3(),
            RollingSpineAmplitude = dynamicEntityPreset.rolling_spine_amplitude.ToUnityVector3(),
            MeshZMin = dynamicEntityPreset.meshZMin,
            MeshZMax = dynamicEntityPreset.meshZMax,
            PositiveYClip = dynamicEntityPreset.positive_y_clip,
            NegativeYClip = dynamicEntityPreset.negative_y_clip,
            MeshSize = dynamicEntityPreset.mesh_size.ToUnityVector3(),
            MeshLargestDimension = dynamicEntityPreset.mesh_largest_dimension,
            
            ShaderUpdateRequested = true,
            
            // Properties
            SeparationWeight = dynamicEntityPreset.separation_weight,
            AlignmentWeight = dynamicEntityPreset.alignment_weight,
            TargetWeight = dynamicEntityPreset.target_weight,
            ObstacleAversionDistance = dynamicEntityPreset.obstacle_aversion_distance,
            Speed = dynamicEntityPreset.move_speed,
            MaxVerticalAngle = dynamicEntityPreset.max_vertical_angle,
            MaxTurnRate = dynamicEntityPreset.max_turn_rate,
            SeabedBound = dynamicEntityPreset.seabed_bound,
            Predator = dynamicEntityPreset.predator,
            Prey = dynamicEntityPreset.prey,
            CellRadius = dynamicEntityPreset.cell_radius,
            StateTransitionSpeed = dynamicEntityPreset.state_transition_speed,
            StateChangeTimerMin = dynamicEntityPreset.state_change_timer_min,
            StateChangeTimerMax = dynamicEntityPreset.state_change_timer_max,
            BoneAnimated = dynamicEntityPreset.bone_animated,
            NumberOfLODs = -1,
            SpeedModifierMin = dynamicEntityPreset.speed_modifier_min,
            SpeedModifierMax = dynamicEntityPreset.speed_modifier_max,
        };
    }

    private Texture2D LoadTextureOnMainThread(string path, bool linear = false)
    {
        byte[] fileData = File.ReadAllBytes(path);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
        if (!texture.LoadImage(fileData))
        {
            Debug.LogError($"Failed to load texture from path: {path}");
            return null;
        }
        return texture;
    }

    private void DeleteGroupClicked(ClickEvent evt)
    {
        DeleteGroup();
    }
    
    public void DeleteGroup()
    {
        foreach (BoidSchoolStruct boidSchoolStruct in boidSchoolStructs)
        {
            var boidSchool = entityManager.GetComponentData<BoidSchoolComponent>(boidSchoolStruct.boidSchoolEntity);
            boidSchool.DestroyRequested = true;
            entityManager.SetComponentData(boidSchoolStruct.boidSchoolEntity, boidSchool);
        }
        
        // Clean up the prototype entity
        if (entityManager.Exists(boidPrototype))
        {
            entityManager.DestroyEntity(boidPrototype);
        }
        
        if (dataRow != null)
        {
            dataRow.RemoveFromHierarchy();
        }

        OnDeleteRequest?.Invoke(this);

        // If this group uses bone animation, unregister from the manager
        if (dynamicEntityPreset.bone_animated)
        {
            var manager = GameObject.FindObjectOfType<BoneAnimatedEntityManager>();
            if (manager != null)
            {
                manager.UnregisterDynamicEntityGroup(this);
            }
        }
    }

    private void OnPopulationPercentageSliderIntChanged(ChangeEvent<int> evt)
    {
        var slider = evt.target as SliderInt;

        // Get the index of the slider
        int view_index = int.Parse(slider.name.Substring(slider.name.Length - 1));
        
        SetViewVisibilityPercentage(view_index, evt.newValue);
    }

    public void SetViewVisibilityPercentageAndUpdateGUI(int viewIndex, int value)
    {
        // Temporarily unregister the event handler
        // Check if sliderInt exists and is not null
        if (viewIndex >= 0 && viewIndex < populationPercentageSliderInts.Count)
        {
            SliderInt sliderInt = populationPercentageSliderInts[viewIndex];
            if (sliderInt != null)
            {
                 sliderInt.SetValueWithoutNotify(value);
            }
        }
        
        // Update the actual visibility
        SetViewVisibilityPercentage(viewIndex, value);
    }
        
    public void SetViewVisibilityPercentage(int view_index, int value)
    {
        viewVisibilityPercentageArray[view_index] = value;

        foreach (BoidSchoolStruct boidSchoolStruct in boidSchoolStructs)
        {
            UpdateBoidSchoolViewportVisibility(boidSchoolStruct.boidSchoolEntity);
        }
    }

    // New handler for the population slider
    private void OnPopulationSliderValueChanged(ChangeEvent<int> evt)
    {
        Debug.Log("DynamicEntitiesGroup " + name + " population slider changed to " + evt.newValue);
        int value = evt.newValue;
        if (value < 0) // Slider lowValue should prevent this
        {
            value = 0;
            if (populationSlider != null) populationSlider.SetValueWithoutNotify(0);
        }
        SetPopulation(value);
    }

    /// <summary>
    /// Sets the population for all boid schools in the group.
    /// Updates the prototype entity and propagates changes to all schools.
    /// </summary>
    /// <param name="population">New population count</param>
    public void SetPopulation(int population)
    {
        Debug.Log("[DynamicEntitiesGroup] Setting population: " + population);

        // Ensure population is not negative. The slider's lowValue should handle this.
        if (population < 0) population = 0;

        // Update the mesh and material on this group's prototype entity while preserving all LOD meshes.
        var renderMeshDescription = new RenderMeshDescription(
            shadowCastingMode: ShadowCastingMode.Off,
            receiveShadows: false);
        RenderMeshUtility.AddComponents(
            boidPrototype,
            entityManager,
            renderMeshDescription,
            new RenderMeshArray(new Material[] { material }, meshes), // Use full LOD array here
            MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
        );
        
        foreach (BoidSchoolStruct boidSchoolStruct in boidSchoolStructs)
        {
            BoidSchoolComponent boidSchool = entityManager.GetComponentData<BoidSchoolComponent>(boidSchoolStruct.boidSchoolEntity);
            boidSchool.ShaderUpdateRequested = true;
            boidSchool.RequestedCount = population;
            entityManager.SetComponentData(boidSchoolStruct.boidSchoolEntity, boidSchool);
        }
    }
    
    public void SetPopulationAndUpdateGUIState(int population)
    {
        if (populationSlider != null)
        {
            populationSlider.SetValueWithoutNotify(population);
        }
        SetPopulation(population);
    }

    // Update the amount of views
    public void UpdateViewsCount(int viewsCount)
    {
        int previousViewsCount = this.viewsCount;
        this.viewsCount = viewsCount;

        // If views were added, default new views to 100% visibility
        if (previousViewsCount < 0) previousViewsCount = 0;
        if (this.viewsCount > previousViewsCount)
        {
            for (int i = previousViewsCount; i < this.viewsCount && i < viewVisibilityPercentageArray.Length; i++)
            {
                viewVisibilityPercentageArray[i] = 100;
            }
        }

        foreach (BoidSchoolStruct boidSchoolStruct in boidSchoolStructs)
        {
            UpdateBoidSchoolViewportVisibility(boidSchoolStruct.boidSchoolEntity);
        }

        // Update the sliders according to the viewVisibilityPercentageArray
        for (int i = 0; i < 4; i++)
        {
            populationPercentageSliderInts[i].value = viewVisibilityPercentageArray[i];
        }

        // Change the sliders visibility according to the amount of views
        for (int i = 0; i < 4; i++)
        {
            if (i < viewsCount)
            {
                populationPercentageSliderInts[i].style.display = DisplayStyle.Flex;
            }
            else
            {
                populationPercentageSliderInts[i].style.display = DisplayStyle.None;
            }
        }
    }

    // Size of array is amount of Views, the index of the element is the index of the view, and bool is whether the group is visible in that view
    private void UpdateBoidSchoolViewportVisibility(Entity boidSchoolEntity)
    {
        BoidSchoolComponent boidSchool = entityManager.GetComponentData<BoidSchoolComponent>(boidSchoolEntity);
        
        boidSchool.ViewsCount = viewsCount;
        boidSchool.ViewVisibilityPercentages = new float4(
            viewVisibilityPercentageArray[0],
            viewVisibilityPercentageArray[1],
            viewVisibilityPercentageArray[2],
            viewVisibilityPercentageArray[3]
        );
        boidSchool.ShaderUpdateRequested = true;
        
        entityManager.SetComponentData(boidSchoolEntity, boidSchool);
    }

    /// <summary>
    /// Updates the bounds for all boid schools in the group.
    /// Destroys existing schools and recreates them with new bounds while maintaining population.
    /// </summary>
    /// <param name="newBoidBounds">New list of boundary GameObjects</param>
    public void UpdateBoidBounds(List<GameObject> newBoidBounds)
    {
        dynamicEntityGroupBounds = newBoidBounds;
        
        // Remember current population from first school (they all have same population)
        var currentPopulation = entityManager.GetComponentData<BoidSchoolComponent>(boidSchoolStructs[0].boidSchoolEntity).RequestedCount;
        
        // Mark old boid schools for destruction
        foreach (BoidSchoolStruct boidSchoolStruct in boidSchoolStructs)
        {
            var boidSchool = entityManager.GetComponentData<BoidSchoolComponent>(boidSchoolStruct.boidSchoolEntity);
            boidSchool.DestroyRequested = true;
            entityManager.SetComponentData(boidSchoolStruct.boidSchoolEntity, boidSchool);
        }
        
        // Clear the boidSchoolStructs list
        boidSchoolStructs.Clear();
        
        // Create one BoidSchoolEntity per boidBounds
        for (int i = 0; i < dynamicEntityGroupBounds.Count; i++)
        {
            CreateBoidSchool(i, currentPopulation);
        }
    }

    public GameObject GetTemplateGameObject()
    {
        return templateGameObject;
    }

    /// <summary>
    /// Returns the current requested population for this dynamic group.
    /// Uses the first boid school as the source of truth.
    /// </summary>
    public int GetCurrentPopulation()
    {
        if (boidSchoolStructs == null || boidSchoolStructs.Count == 0)
        {
            Debug.LogError("[DynamicEntitiesGroup] GetCurrentPopulation called but boidSchoolStructs is empty.");
            Debug.Assert(false, "[DynamicEntitiesGroup] GetCurrentPopulation requires at least one boid school.");
            return 0;
        }

        BoidSchoolStruct firstSchool = boidSchoolStructs[0];
        if (!entityManager.Exists(firstSchool.boidSchoolEntity))
        {
            Debug.LogError("[DynamicEntitiesGroup] GetCurrentPopulation: first boid school entity does not exist.");
            Debug.Assert(false, "[DynamicEntitiesGroup] GetCurrentPopulation requires a valid boid school entity.");
            return 0;
        }

        BoidSchoolComponent boidSchool =
            entityManager.GetComponentData<BoidSchoolComponent>(firstSchool.boidSchoolEntity);
        return boidSchool.RequestedCount;
    }

    /// <summary>
    /// Returns a copy of the per-view visibility percentages array (0-100).
    /// Index corresponds to the view index.
    /// </summary>
    public int[] GetViewVisibilityPercentagesCopy()
    {
        int length = viewVisibilityPercentageArray.Length;
        int[] copy = new int[length];
        for (int i = 0; i < length; i++)
        {
            copy[i] = viewVisibilityPercentageArray[i];
        }
        return copy;
    }

    /// <summary>
    /// Attempts to get a copy of the override habitats array, if any has been set.
    /// Returns true when an override exists, false otherwise.
    /// </summary>
    public bool TryGetOverrideHabitats(out string[] habitats)
    {
        habitats = null;

        if (overrideHabitats == null)
        {
            return false;
        }

        if (overrideHabitats.Length == 0)
        {
            return false;
        }

        string[] copy = new string[overrideHabitats.Length];
        for (int i = 0; i < overrideHabitats.Length; i++)
        {
            copy[i] = overrideHabitats[i];
        }

        habitats = copy;
        return true;
    }
}
}

/// <summary>
/// Utility class for converting between serializable Vector3 data and Unity Vector3.
/// </summary>
[System.Serializable]
public class Vector3Data
{
    public float x;
    public float y;
    public float z;

    public UnityEngine.Vector3 ToUnityVector3()
    {
        return new UnityEngine.Vector3(x, y, z);
    }
}

