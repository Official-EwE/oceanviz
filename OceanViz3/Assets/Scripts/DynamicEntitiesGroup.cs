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
    
    public int population;
    public int maxPopulation;
    
    public string habitat;
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
    public Mesh mesh;
    public Texture baseColorTexture;
    public Texture normalTexture;
    public Texture roughnessTexture;
    public Texture metallicTexture;
    public bool bone_animated;
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
    public Mesh mesh;
    public Material material;

    private EntityManager entityManager;
    private EntityLibrary entityLibrary;
    
    private List<GameObject> dynamicEntityGroupBounds;
    private int viewsCount = -1;
    private int[] viewVisibilityPercentageArray = new int[4] {100, 0, 0, 0};
    
    [SerializeField]
    public List<BoidSchoolStruct> boidSchoolStructs = new List<BoidSchoolStruct>();

    // GUI
    private VisualElement dataRow;
    private IntegerField integerField;
    private Button reloadButton;
    private Button deleteButton;
    // Sliders
    private List<SliderInt> populationPercentageSliderInts = new List<SliderInt>();

    // Delegates
    public delegate void GroupDeleteRequestHandler(DynamicEntitiesGroup dynamicEntitiesGroup);
    public event GroupDeleteRequestHandler OnDeleteRequest;

    private EventCallback<ChangeEvent<int>> populationChangeCallback;

    private Entity boidPrototype;

    public GLTFast.GltfImport gltf { get; private set; }

    private GameObject templateGameObject;

    /// <summary>
    /// Checks if the dynamic entities group is properly initialized and ready for use.
    /// This is used by the SimulationAPI.
    /// </summary>
    public bool IsReady
    {
        get
        {
            // Check if mesh and material are loaded
            if (mesh == null || material == null)
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
                    !entityManager.HasComponent<BoidSchool>(boidSchool.boidSchoolEntity))
                    return false;
            }

            return true;
        }
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

        //// GUI
        this.dataRow = dataRow;
        this.integerField = dataRow.Q<IntegerField>("IntegerField");
		integerField.label = name;
        
        // Store the callback reference
        populationChangeCallback = (evt) => OnPopulationIntegerValueChanged(evt);
        integerField.RegisterValueChangedCallback(populationChangeCallback);
        
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

    /// <summary>
    /// Reloads the entire group, destroying existing entities and recreating them with updated presets.
    /// Maintains the current population count.
    /// </summary>
    public void ReloadGroup(ClickEvent evt)
    {
        Debug.Log("Reload group " + name);

        // Remember current population, currently the population is the same for all boidSchoolStructs
        var oldPopulation = entityManager.GetComponentData<BoidSchool>(boidSchoolStructs[0].boidSchoolEntity).RequestedCount;

        foreach (BoidSchoolStruct boidSchoolStruct in boidSchoolStructs)
        {
            // Mark old boidSchool for destruction
            var boidSchool = entityManager.GetComponentData<BoidSchool>(boidSchoolStruct.boidSchoolEntity);
            boidSchool.DestroyRequested = true;
            entityManager.SetComponentData(boidSchoolStruct.boidSchoolEntity, boidSchool);
        }
        
        // Clear the boidSchoolStructs list
        boidSchoolStructs.Clear();
        
        // Update all presets
        GroupPresetsManager.Instance.UpdatePresets();

        LoadAndSpawnGroup(); // This will create new boidSchoolEntity/Entities

        // Set the population to the old population
        Debug.Log("Setting population back to " + oldPopulation);
        integerField.value = oldPopulation;
    }

    /// <summary>
    /// Loads assets and spawns the dynamic entity group.
    /// Handles mesh, texture, and material loading, and creates boid schools.
    /// </summary>
    /// <returns>Task representing the async loading operation</returns>
    /// <exception cref="Exception">Thrown when required assets are missing or fail to load</exception>
    public async Task LoadAndSpawnGroup()
    {
        var preset = GroupPresetsManager.Instance.dynamicEntitiesPresetsList
            .FirstOrDefault(p => p.name == name);
        if (string.IsNullOrEmpty(preset.name))
        {
            Debug.LogError($"DynamicEntityPreset not found for {name}");
            throw new Exception($"DynamicEntityPreset not found for {name}");
        }
        dynamicEntityPreset = preset;
        
        var gltfPath = Application.streamingAssetsPath + "/DynamicEntities/" + name + "/model.glb";
        if (!File.Exists(gltfPath))
        {
            Debug.LogError("model.glb not found in " + name);
            throw new Exception("model.glb not found in " + name);
        }

        var baseColorPath = Application.streamingAssetsPath + "/DynamicEntities/" + name + "/base_color.png";
        if (!File.Exists(baseColorPath))
        {
            Debug.LogError("base_color.png not found in " + name);
            throw new Exception("base_color.png not found in " + name);
        }

        var normalPath = Application.streamingAssetsPath + "/DynamicEntities/" + name + "/normal.png";
        if (!File.Exists(normalPath))
        {
            Debug.LogError("normal.png not found in " + name);
            throw new Exception("normal.png not found in " + name);
        }
        
        var roughnessPath = Application.streamingAssetsPath + "/DynamicEntities/" + name + "/roughness.png";
        if (!File.Exists(roughnessPath))
        {
            Debug.LogWarning("roughness.png not found in " + name);
        }

        var metallicPath = Application.streamingAssetsPath + "/DynamicEntities/" + name + "/metallic.png";
        if (!File.Exists(metallicPath))
        {
            Debug.LogWarning("metallic.png not found in " + name);
        }
        
        int population = dynamicEntityPreset.population;
        
        gltf = new GLTFast.GltfImport();
        var importSettings = new ImportSettings {
            AnimationMethod = AnimationMethod.Legacy,
            GenerateMipMaps = true
        };
        
        var success = await gltf.Load(gltfPath, importSettings);
        if (!success) {
            Debug.LogError($"[DynamicEntitiesGroup] Loading glTF failed for {name}!");
            throw new Exception($"Loading glTF failed for {name}!");
        }

        mesh = gltf.GetMeshes()[0];

        UnityEngine.Vector3[] vertices = mesh.vertices;
        float meshZMin = float.MaxValue;
        float meshZMax = float.MinValue;
        foreach (UnityEngine.Vector3 vertex in vertices)
        {
            if (vertex.z < meshZMin) meshZMin = vertex.z;
            if (vertex.z > meshZMax) meshZMax = vertex.z;
        }

        dynamicEntityPreset.meshZMin = meshZMin;
        dynamicEntityPreset.meshZMax = meshZMax;

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

            // Create material
            material = new Material(Shader.Find("Shader Graphs/FishAdvancedShaderGraph"));
            material.SetTexture("_BaseColor", baseColorTexture);
            material.SetTexture("_Normal", normalTexture);
            if (roughnessTexture != null)
                material.SetTexture("_Roughness", roughnessTexture);
            if (metallicTexture != null)
                material.SetTexture("_Metallic", metallicTexture);

        } catch (Exception e) {
            Debug.LogError($"Error loading textures: {e.Message}");
            throw;
        }

        // Create a unique prototype for this group by duplicating the library prototype
        boidPrototype = entityManager.Instantiate(entityLibrary.BoidEntity);
        entityManager.AddComponentData(boidPrototype, new Prefab());
        
        // Set up rendering for this group's prototype
        var renderMeshDescription = new RenderMeshDescription(
            shadowCastingMode: ShadowCastingMode.Off,
            receiveShadows: false);
        RenderMeshUtility.AddComponents(
            boidPrototype,
            entityManager,
            renderMeshDescription,
            new RenderMeshArray(new Material[] { material }, new Mesh[] { mesh }),
            MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
        );
        
        // Create boid schools using the group's prototype
        for (int i = 0; i < dynamicEntityGroupBounds.Count; i++)
        {
            CreateBoidSchool(i, population);
        }

        integerField.value = population;

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

    private BoidSchool CreateBoidSchoolData(int schoolId, float3 boundsCenter, float3 boundsSize, int population)
    {
        return new BoidSchool
        {
            DynamicEntityId = DynamicEntityId,
            BoidSchoolId = schoolId,
            Prefab = boidPrototype,
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
            
            ShaderUpdateRequested = true,
            
            // Properties
            SeparationWeight = dynamicEntityPreset.separation_weight,
            AlignmentWeight = dynamicEntityPreset.alignment_weight,
            TargetWeight = dynamicEntityPreset.target_weight,
            ObstacleAversionDistance = dynamicEntityPreset.obstacle_aversion_distance,
            Speed = dynamicEntityPreset.move_speed,
            MaxVerticalAngle = dynamicEntityPreset.max_vertical_angle,
            SeabedBound = dynamicEntityPreset.seabed_bound,
            Predator = dynamicEntityPreset.predator,
            Prey = dynamicEntityPreset.prey,
            CellRadius = dynamicEntityPreset.cell_radius,
            StateTransitionSpeed = dynamicEntityPreset.state_transition_speed,
            StateChangeTimerMin = dynamicEntityPreset.state_change_timer_min,
            StateChangeTimerMax = dynamicEntityPreset.state_change_timer_max,
            BoneAnimated = dynamicEntityPreset.bone_animated,
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
            var boidSchool = entityManager.GetComponentData<BoidSchool>(boidSchoolStruct.boidSchoolEntity);
            boidSchool.DestroyRequested = true;
            entityManager.SetComponentData(boidSchoolStruct.boidSchoolEntity, boidSchool);
        }
        
        // Clean up the prototype entity
        if (entityManager.Exists(boidPrototype))
        {
            entityManager.DestroyEntity(boidPrototype);
        }
        
        dataRow.RemoveFromHierarchy();

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
        populationPercentageSliderInts[viewIndex].UnregisterValueChangedCallback(OnPopulationPercentageSliderIntChanged);
        
        // Update the slider value
        populationPercentageSliderInts[viewIndex].value = value;
        
        // Update the actual visibility
        SetViewVisibilityPercentage(viewIndex, value);
        
        // Re-register the event handler
        populationPercentageSliderInts[viewIndex].RegisterValueChangedCallback(OnPopulationPercentageSliderIntChanged);
    }
        
    public void SetViewVisibilityPercentage(int view_index, int value)
    {
        viewVisibilityPercentageArray[view_index] = value;

        foreach (BoidSchoolStruct boidSchoolStruct in boidSchoolStructs)
        {
            UpdateBoidSchoolViewportVisibility(boidSchoolStruct.boidSchoolEntity);
        }
    }

    public void OnPopulationIntegerValueChanged(ChangeEvent<int> evt)
	{
        Debug.Log("DynamicEntitiesGroup " + name + " population changed to " + evt.newValue);

        // Floor to 0
        int value = evt.newValue;
        if (value < 0)
        {
            value = 0;
            integerField.value = 0;
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

        // Update the mesh and material on this group's prototype entity
        var renderMeshDescription = new RenderMeshDescription(
            shadowCastingMode: ShadowCastingMode.Off,
            receiveShadows: false);
        RenderMeshUtility.AddComponents(
            boidPrototype,
            entityManager,
            renderMeshDescription,
            new RenderMeshArray(new Material[] { material }, new Mesh[] { mesh }),
            MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
        );
        
        foreach (BoidSchoolStruct boidSchoolStruct in boidSchoolStructs)
        {
            BoidSchool boidSchool = entityManager.GetComponentData<BoidSchool>(boidSchoolStruct.boidSchoolEntity);
            boidSchool.ShaderUpdateRequested = true;
            boidSchool.RequestedCount = population;
            entityManager.SetComponentData(boidSchoolStruct.boidSchoolEntity, boidSchool);
        }
    }
    
    public void SetPopulationAndUpdateGUIState(int population)
    {
        integerField.UnregisterValueChangedCallback(populationChangeCallback);
        integerField.value = population;
        SetPopulation(population);
        integerField.RegisterValueChangedCallback(populationChangeCallback);
    }

    // Update the amount of views
    public void UpdateViewsCount(int viewsCount)
    {
        this.viewsCount = viewsCount;

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
        BoidSchool boidSchool = entityManager.GetComponentData<BoidSchool>(boidSchoolEntity);
        
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
        var currentPopulation = entityManager.GetComponentData<BoidSchool>(boidSchoolStructs[0].boidSchoolEntity).RequestedCount;
        
        // Mark old boid schools for destruction
        foreach (BoidSchoolStruct boidSchoolStruct in boidSchoolStructs)
        {
            var boidSchool = entityManager.GetComponentData<BoidSchool>(boidSchoolStruct.boidSchoolEntity);
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

