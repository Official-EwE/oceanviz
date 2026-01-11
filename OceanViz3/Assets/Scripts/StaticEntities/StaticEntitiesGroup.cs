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
public class StaticEntityPreset
{
    public string name;
    
    public int maxPopulation;
    
    public string[] habitats;
    
    // Added min/max scale properties
    public float minScale;
    public float maxScale;
    
    // Added rigidity property for turbulence control
    public float rigidity;
    
    // Added waves motion strength property for shader
    public float wavesMotionStrength;
    
    // Not in json
    public Texture baseColorTexture;
    public Texture normalTexture;
}

/// <summary>
/// Represents a boid school structure containing identification and entity references.
/// </summary>
[Serializable]
public struct StaticEntitiesGroupStruct
{
    public int StaticEntitiesGroupId;
    public Entity StaticEntitiesGroupEntity;
}
    
/// <summary>
/// Manages a group of dynamic entities in the ocean visualization system.
/// Handles entity spawning, configuration, and lifecycle management for groups of boids.
/// </summary>
[Serializable]
public class StaticEntitiesGroup
{
    public int StaticEntitiesGroupId;
    public string name;
    public StaticEntityPreset staticEntityPreset;
    public Material material;
    
    // Mesh habitat settings
    public bool useMeshHabitats = true;
    public float meshHabitatRatio = 0.5f; // 0 = all on terrain, 1 = all on mesh

    private EntityManager entityManager;
    private EntityLibrary entityLibrary;
    
    private int viewsCount = -1;
    private int[] viewVisibilityPercentageArray = new int[4] {100, 100, 100, 100};
    
    [SerializeField]
    public List<StaticEntitiesGroupStruct> staticEntitiesGroupStructs = new List<StaticEntitiesGroupStruct>();

    // GUI
    private VisualElement dataRow;
    private SliderInt populationSlider;
    private Button reloadButton;
    private Button deleteButton;
    // Sliders
    private List<SliderInt> populationPercentageSliderInts = new List<SliderInt>();

    // Delegates
    public delegate void GroupDeleteRequestHandler(StaticEntitiesGroup staticEntitiesGroup);
    public event GroupDeleteRequestHandler OnDeleteRequest;

    private EventCallback<ChangeEvent<int>> populationSliderChangeCallback;

    private Entity staticEntityPrototype;

    public GLTFast.GltfImport gltf { get; private set; }

    private GameObject templateGameObject;

    // Rename lodMeshes to meshes and make it public
    public Mesh[] meshes;
    private const int LOD_COUNT = 3; // LOD0, LOD1, LOD2

    // Optional override for habitats used when writing ECS buffer
    private string[] overrideHabitats;

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
            if (staticEntitiesGroupStructs == null || staticEntitiesGroupStructs.Count == 0)
                return false;

            // Check if all boid schools are properly set up with entities
            foreach (var staticEntitiesGroupStruct in staticEntitiesGroupStructs)
            {
                if (staticEntitiesGroupStruct.StaticEntitiesGroupEntity == Entity.Null)
                    return false;

                // Check if the entity still exists and has the BoidSchool component
                if (!entityManager.Exists(staticEntitiesGroupStruct.StaticEntitiesGroupEntity) || 
                    !entityManager.HasComponent<StaticEntitiesGroupComponent>(staticEntitiesGroupStruct.StaticEntitiesGroupEntity))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Initializes the static entities group with necessary components and UI elements.
    /// </summary>
    /// <param name="name">Name identifier for the group</param>
    /// <param name="dataRow">UI element containing group controls</param>
    /// <param name="viewsCount">Number of active views</param>
    /// <param name="staticEntitiesGroupId">Unique identifier for the entity group</param>
    /// <param name="staticEntityPreset">Configuration preset for the entities</param>
    public void Setup(string name, VisualElement dataRow, int viewsCount, int staticEntitiesGroupId, StaticEntityPreset staticEntityPreset)
    {
        // Resources
        World world = World.DefaultGameObjectInjectionWorld;
        entityManager = world.EntityManager;
        Entity entityLibraryEntity = entityManager.CreateEntityQuery(typeof(EntityLibrary)).GetSingletonEntity();
		entityLibrary = entityManager.GetComponentData<EntityLibrary>(entityLibraryEntity);

        this.StaticEntitiesGroupId = staticEntitiesGroupId;
        this.name = name;
        this.viewsCount = viewsCount;
        this.staticEntityPreset = staticEntityPreset;

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

        if(dataRow != null)
        {
            this.populationSlider = dataRow.Q<SliderInt>("PopulationSliderInt");
            Label nameLabel = dataRow.Q<Label>("NameLabel");
            if (nameLabel != null)
            {
                nameLabel.text = name;
            }
            else
            {
                Debug.LogWarning($"[StaticEntitiesGroup] NameLabel not found in DataRow for group '{name}'.");
                if (this.populationSlider != null) { /* Potentially set a label if needed, or rely on a separate NameLabel */ }
            }

            if (this.populationSlider != null)
            {
                this.populationSlider.lowValue = 0;
                this.populationSlider.highValue = staticEntityPreset.maxPopulation;
                populationSliderChangeCallback = (evt) => OnPopulationSliderValueChanged(evt);
                this.populationSlider.RegisterValueChangedCallback(populationSliderChangeCallback);
            }
            else
            {
                Debug.LogError("[StaticEntitiesGroup] PopulationSliderInt not found in DataRow!");
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
        MainScene.SetReadyState(false);

        try
        {
            // Remember current population
            var oldPopulation = 0;
            if (staticEntitiesGroupStructs.Count > 0 && entityManager.Exists(staticEntitiesGroupStructs[0].StaticEntitiesGroupEntity))
            {
                oldPopulation = entityManager.GetComponentData<StaticEntitiesGroupComponent>(staticEntitiesGroupStructs[0].StaticEntitiesGroupEntity).RequestedCount;
            }

            // --- Destruction Phase ---
            // Mark old entities for destruction
            foreach (StaticEntitiesGroupStruct staticEntitiesGroupStruct in staticEntitiesGroupStructs)
            {
                if (entityManager.Exists(staticEntitiesGroupStruct.StaticEntitiesGroupEntity))
                {
                    var staticEntitiesGroupComponent = entityManager.GetComponentData<StaticEntitiesGroupComponent>(staticEntitiesGroupStruct.StaticEntitiesGroupEntity);
                    staticEntitiesGroupComponent.DestroyRequested = true;
                    entityManager.SetComponentData(staticEntitiesGroupStruct.StaticEntitiesGroupEntity, staticEntitiesGroupComponent);
                }
            }

            // Clear the list now that the entities are gone
            staticEntitiesGroupStructs.Clear();

            // --- Recreation Phase ---
            // Update all presets
            GroupPresetsManager.Instance.UpdatePresets();

            await LoadAndSpawnGroup(); // This will create new StaticEntitiesGroupEntity/Entities

            // Set the population to the old population
            Debug.Log("Setting population back to " + oldPopulation);
            SetPopulationAndUpdateGUIState(oldPopulation);
        }
        finally
        {
            MainScene.SetReadyState(true);
        }
    }

    /// <summary>
    /// Loads assets and spawns the dynamic entity group.
    /// Handles mesh, texture, and material loading, and creates boid schools.
    /// </summary>
    /// <returns>Task representing the async loading operation</returns>
    /// <exception cref="Exception">Thrown when required assets are missing or fail to load</exception>
    public async Task LoadAndSpawnGroup()
    {
        // Resolve the preset using the preset's name, not the group's display name
        string presetName = staticEntityPreset != null && !string.IsNullOrEmpty(staticEntityPreset.name) ? staticEntityPreset.name : this.name;
        var preset = GroupPresetsManager.Instance.staticEntitiesPresetsList
            .FirstOrDefault(p => p.name == presetName);
        if (preset == null) 
        {
            Debug.LogError($"[StaticEntitiesGroup] StaticEntityPreset not found for preset '{presetName}' in LoadAndSpawnGroup. This group was set up with display name: {this.name}");
            throw new Exception($"StaticEntityPreset not found for {presetName}");
        }
        this.staticEntityPreset = preset;

        // Ensure slider's highValue is consistent with the current preset's maxPopulation
        if (this.populationSlider != null)
        {
            int newHighValue = this.staticEntityPreset.maxPopulation > 0 ? this.staticEntityPreset.maxPopulation : 0;
            // Check if highValue actually needs changing to avoid unnecessary callback unregister/register
            if (this.populationSlider.highValue != newHighValue)
            {
                // Temporarily unregister to prevent callback if value is clamped by new highValue change.
                // This is a precaution; ideally, changing highValue shouldn't trigger ValueChanged unless the value itself is affected.
                if (populationSliderChangeCallback != null) this.populationSlider.UnregisterValueChangedCallback(populationSliderChangeCallback);
                
                this.populationSlider.highValue = newHighValue;
                
                if (populationSliderChangeCallback != null) this.populationSlider.RegisterValueChangedCallback(populationSliderChangeCallback);
            }
        }
        
        var presetFolderName = staticEntityPreset.name;
        var gltfPath = Application.streamingAssetsPath + "/StaticEntities/" + presetFolderName + "/model1.glb";
        if (!File.Exists(gltfPath))
        {
            Debug.LogError("model1.glb not found in " + presetFolderName);
            throw new Exception("model1.glb not found in " + presetFolderName);
        }

        var baseColorPath = Application.streamingAssetsPath + "/StaticEntities/" + presetFolderName + "/base_color.png";
        if (!File.Exists(baseColorPath))
        {
            Debug.LogError("base_color.png not found in " + presetFolderName);
            throw new Exception("base_color.png not found in " + presetFolderName);
        }

        var normalPath = Application.streamingAssetsPath + "/StaticEntities/" + presetFolderName + "/normal.png";
        if (!File.Exists(normalPath))
        {
            Debug.LogError("normal.png not found in " + presetFolderName);
            throw new Exception("normal.png not found in " + presetFolderName);
        }
        
        var emissivePath = Application.streamingAssetsPath + "/StaticEntities/" + presetFolderName + "/emissive.png";
        
        // Determine initial population based on the preset.
        int initialPopulation;

        if (this.dataRow == null) // In Asset Browser mode
        {
            initialPopulation = 1;
        }
        else if (this.staticEntityPreset.maxPopulation <= 0)
        {
            initialPopulation = 0; // If maxPopulation from preset is 0 or negative, initial population must be 0.
        }
        else
        {
            // Set initial population to 25% of maxPopulation.
            initialPopulation = Mathf.RoundToInt(this.staticEntityPreset.maxPopulation * 0.25f);
        }
        
        gltf = new GLTFast.GltfImport();
        var importSettings = new ImportSettings {
            AnimationMethod = AnimationMethod.Legacy,
            GenerateMipMaps = true
        };
        
        // Load all LOD meshes
        var lodPaths = new string[LOD_COUNT];
        lodPaths[0] = gltfPath;  // Base model (LOD0)
        lodPaths[1] = Application.streamingAssetsPath + "/StaticEntities/" + presetFolderName + "/model_LOD1.glb";
        lodPaths[2] = Application.streamingAssetsPath + "/StaticEntities/" + presetFolderName + "/model_LOD2.glb";

        // Initialize meshes array
        meshes = new Mesh[LOD_COUNT];

        // Load LOD0 (base model)
        var success = await gltf.Load(lodPaths[0], importSettings);
        if (!success) {
            Debug.LogError($"[StaticEntitiesGroup] Loading LOD0 glTF failed for {presetFolderName}!");
            throw new Exception($"Loading LOD0 glTF failed for {presetFolderName}!");
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
                    Debug.Log($"[StaticEntitiesGroup] Loaded LOD{i} for {presetFolderName}");
                }
                else
                {
                    Debug.LogWarning($"[StaticEntitiesGroup] Failed to load LOD{i} for {presetFolderName}, using LOD0 as fallback");
                    meshes[i] = meshes[0];
                }
            }
            else
            {
                Debug.LogWarning($"[StaticEntitiesGroup] LOD{i} file not found for {presetFolderName}, using LOD0 as fallback");
                meshes[i] = meshes[0];
            }
        }

        // After loading meshes
        Debug.Log($"[StaticEntitiesGroup] Loaded {meshes.Length} LOD meshes for {presetFolderName}:");
        for (int i = 0; i < meshes.Length; i++) {
            Debug.Log($"[StaticEntitiesGroup] LOD{i}: {(meshes[i] != null ? "Loaded" : "NULL")}");
        }

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

            Texture2D emissionTexture = null;
            if (File.Exists(emissivePath))
            {
                emissionTexture = LoadTextureOnMainThread(emissivePath);
                if (emissionTexture == null)
                {
                    throw new Exception($"Failed to load emissive texture at {emissivePath}");
                }
            }

            // Create material
            material = new Material(Shader.Find("Shader Graphs/StaticEntityShaderGraph"));
            material.SetTexture("_BaseColor", baseColorTexture);
            material.SetTexture("_Normal", normalTexture);
            if (emissionTexture != null)
                material.SetTexture("_Emission", emissionTexture);
            material.enableInstancing = true;  // Enable GPU instancing for better performance
            
            // Set up alpha properties
            material.SetFloat("_AlphaClipThreshold", 0.5f); // Adjust threshold as needed
            material.EnableKeyword("_ALPHATEST_ON");

        } catch (Exception e) {
            Debug.LogError($"Error loading textures: {e.Message}");
            throw;
        }

        // Create a unique prototype for this group by duplicating the library prototype
        staticEntityPrototype = entityManager.Instantiate(entityLibrary.StaticEntity);
        entityManager.AddComponentData(staticEntityPrototype, new Prefab());
        
        // Add shared component with unique group ID
        entityManager.SetSharedComponentManaged(staticEntityPrototype, new StaticEntityShared { StaticEntitiesGroupId = this.StaticEntitiesGroupId });
        
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
            staticEntityPrototype,
            entityManager,
            renderMeshDescription,
            renderMeshArray,
            MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)  // Use FromRenderMeshArrayIndices to get correct negative indices
        );

        // Set initial population to 100000 for static groups
        CreateStaticEntitiesGroupEntity(this.StaticEntitiesGroupId, initialPopulation);

        // After spawning, push initial view visibility to ECS
        foreach (StaticEntitiesGroupStruct staticEntitiesGroupStruct in staticEntitiesGroupStructs)
        {
            UpdateStaticEntitiesGroupViewportVisibility(staticEntitiesGroupStruct.StaticEntitiesGroupEntity);
        }

        // Set NumberOfLODs based on actual unique mesh count AFTER creating boid schools
        foreach (StaticEntitiesGroupStruct staticEntitiesGroupStruct in staticEntitiesGroupStructs)
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

            var staticEntitiesGroup = entityManager.GetComponentData<StaticEntitiesGroupComponent>(staticEntitiesGroupStruct.StaticEntitiesGroupEntity);
            staticEntitiesGroup.NumberOfLODs = uniqueLODCount;
            entityManager.SetComponentData(staticEntitiesGroupStruct.StaticEntitiesGroupEntity, staticEntitiesGroup);
        }
        
        // Set the slider value to the initial population
        if (populationSlider != null)
        {
            // initialPopulation is now guaranteed to be <= populationSlider.highValue,
            // so SetValueWithoutNotify should not cause a clamping that changes the value itself
            // in a way that would trigger the callback with an unexpectedly low (e.g., 0) value.
            populationSlider.SetValueWithoutNotify(initialPopulation);
        }
    }

    /// <summary>
    /// Sets the habitat names to be used instead of the preset habitats when creating group entities.
    /// </summary>
    /// <param name="habitats">Array of habitat names</param>
    public void SetOverrideHabitats(string[] habitats)
    {
        if (habitats == null || habitats.Length == 0)
        {
            Debug.LogError("[StaticEntitiesGroup] SetOverrideHabitats requires at least one habitat name.");
            Debug.Assert(false, "SetOverrideHabitats called with empty habitats.");
            return;
        }
        overrideHabitats = habitats.ToArray();
    }

    /// <summary>
    /// Creates a new static entities group entity.
    /// </summary>
    /// <param name="uniqueGroupId">The UNIQUE identifier for this specific group instance.</param>
    /// <param name="population">Initial population count for the group</param>
    private void CreateStaticEntitiesGroupEntity(int uniqueGroupId, int population)
    {
        StaticEntitiesGroupStruct staticEntitiesGroupStruct = new StaticEntitiesGroupStruct();
        staticEntitiesGroupStruct.StaticEntitiesGroupId = uniqueGroupId;

        Entity StaticEntitiesGroupEntity = entityManager.Instantiate(entityLibrary.StaticEntitiesGroupEntity);
        entityManager.SetName(StaticEntitiesGroupEntity, "StaticEntitiesGroup_" + this.name + "_" + uniqueGroupId);
        
        entityManager.SetComponentData(StaticEntitiesGroupEntity, CreateStaticEntitiesGroupData(uniqueGroupId, population));
        
        // Add habitats to the buffer (prefer overrideHabits if provided)
        var habitatBuffer = entityManager.AddBuffer<StaticEntityHabitat>(StaticEntitiesGroupEntity);
        if (overrideHabitats != null && overrideHabitats.Length > 0)
        {
            for (int i = 0; i < overrideHabitats.Length; i++)
            {
                var habitatName = overrideHabitats[i];
                if (!string.IsNullOrEmpty(habitatName))
                {
                    habitatBuffer.Add(new StaticEntityHabitat { Name = habitatName });
                }
            }
        }
        else if (staticEntityPreset != null && staticEntityPreset.habitats != null)
        {
            foreach (var habitatName in staticEntityPreset.habitats)
            {
                if (!string.IsNullOrEmpty(habitatName))
                {
                    habitatBuffer.Add(new StaticEntityHabitat { Name = habitatName });
                }
            }
        }

        staticEntitiesGroupStruct.StaticEntitiesGroupEntity = StaticEntitiesGroupEntity;
        staticEntitiesGroupStructs.Add(staticEntitiesGroupStruct);
    }

    private StaticEntitiesGroupComponent CreateStaticEntitiesGroupData(int uniqueGroupId, int population)
    {
        return new StaticEntitiesGroupComponent
        {
            StaticEntitiesGroupId = uniqueGroupId,
            StaticEntityPrototype = staticEntityPrototype,
            Count = 0,
            DestroyRequested = false,
            RequestedCount = population,
            ViewsCount = viewsCount,
            ViewVisibilityPercentages = new float4(
                viewVisibilityPercentageArray[0] / 100f, // Convert percentage to 0-1 range
                viewVisibilityPercentageArray[1] / 100f,
                viewVisibilityPercentageArray[2] / 100f,
                viewVisibilityPercentageArray[3] / 100f),
            
            ShaderUpdateRequested = true,
            NumberOfLODs = -1, // Will be set after prototype is fully configured
            
            // Mesh habitat settings
            UseMeshHabitats = useMeshHabitats,
            MeshHabitatRatio = meshHabitatRatio,
            
            // Make sure habitat name is set from preset if available
            /*HabitatName = staticEntityPreset != null && staticEntityPreset.habitats != null && staticEntityPreset.habitats.Length > 0 
                ? staticEntityPreset.habitats[0] 
                : string.Empty*/
            WavesMotionStrength = staticEntityPreset.wavesMotionStrength
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
        foreach (StaticEntitiesGroupStruct staticEntitiesGroupStruct in staticEntitiesGroupStructs)
        {
            var staticEntitiesGroupComponent = entityManager.GetComponentData<StaticEntitiesGroupComponent>(staticEntitiesGroupStruct.StaticEntitiesGroupEntity);
            staticEntitiesGroupComponent.DestroyRequested = true;
            entityManager.SetComponentData(staticEntitiesGroupStruct.StaticEntitiesGroupEntity, staticEntitiesGroupComponent);
        }
        
        // Clean up the prototype entity
        if (entityManager.Exists(staticEntityPrototype))
        {
            entityManager.DestroyEntity(staticEntityPrototype);
        }
        
        if(dataRow != null)
            dataRow.RemoveFromHierarchy();

        OnDeleteRequest?.Invoke(this);
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
        if (viewIndex < 0 || viewIndex >= populationPercentageSliderInts.Count) return;

        // Temporarily unregister the event handler
        // Check if sliderInt exists and is not null
        SliderInt sliderInt = populationPercentageSliderInts[viewIndex];
        if (sliderInt != null)
        {
            sliderInt.SetValueWithoutNotify(value);
        }
        
        // Update the actual visibility
        SetViewVisibilityPercentage(viewIndex, value);
    }
        
    public void SetViewVisibilityPercentage(int view_index, int value)
    {
        if (view_index < 0 || view_index >= viewVisibilityPercentageArray.Length) return;

        viewVisibilityPercentageArray[view_index] = value;

        foreach (StaticEntitiesGroupStruct staticEntitiesGroupStruct in staticEntitiesGroupStructs)
        {
            UpdateStaticEntitiesGroupViewportVisibility(staticEntitiesGroupStruct.StaticEntitiesGroupEntity);
        }
    }

    // New handler for the population slider
    private void OnPopulationSliderValueChanged(ChangeEvent<int> evt)
    {
        Debug.Log("StaticEntitiesGroup " + name + " population slider changed to " + evt.newValue);
        int value = evt.newValue;
        if (value < 0) // Slider lowValue should prevent this, but good for safety
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
        Debug.Log("[StaticEntitiesGroup] Setting population: " + population);

        // Ensure population is not negative. The slider's lowValue should handle this.
        if (population < 0) population = 0;

        // Update the mesh and material on this group's prototype entity while preserving all LOD meshes.
        var renderMeshDescription = new RenderMeshDescription(
            shadowCastingMode: ShadowCastingMode.Off,
            receiveShadows: false);
        RenderMeshUtility.AddComponents(
            staticEntityPrototype,
            entityManager,
            renderMeshDescription,
            new RenderMeshArray(new Material[] { material }, meshes), // Use full LOD array here
            MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
        );
        
        foreach (StaticEntitiesGroupStruct staticEntitiesGroupStruct in staticEntitiesGroupStructs)
        {
            StaticEntitiesGroupComponent staticEntitiesGroupEntity = entityManager.GetComponentData<StaticEntitiesGroupComponent>(staticEntitiesGroupStruct.StaticEntitiesGroupEntity);
            staticEntitiesGroupEntity.ShaderUpdateRequested = true;
            staticEntitiesGroupEntity.RequestedCount = population;
            entityManager.SetComponentData(staticEntitiesGroupStruct.StaticEntitiesGroupEntity, staticEntitiesGroupEntity);
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

        foreach (StaticEntitiesGroupStruct staticEntitiesGroupStruct in staticEntitiesGroupStructs)
        {
            UpdateStaticEntitiesGroupViewportVisibility(staticEntitiesGroupStruct.StaticEntitiesGroupEntity);
        }

        // Update the sliders according to the viewVisibilityPercentageArray
        for (int i = 0; i < 4; i++)
        {
            if (i < populationPercentageSliderInts.Count && populationPercentageSliderInts[i] != null)
            {
                populationPercentageSliderInts[i].SetValueWithoutNotify(viewVisibilityPercentageArray[i]);
            }
        }

        // Change the sliders visibility according to the amount of views
        for (int i = 0; i < 4; i++)
        {
            if (i < populationPercentageSliderInts.Count && populationPercentageSliderInts[i] != null)
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
    }

    // Size of array is amount of Views, the index of the element is the index of the view, and bool is whether the group is visible in that view
    private void UpdateStaticEntitiesGroupViewportVisibility(Entity staticEntitiesGroupEntity)
    {
        if (!entityManager.Exists(staticEntitiesGroupEntity) || !entityManager.HasComponent<StaticEntitiesGroupComponent>(staticEntitiesGroupEntity))
        {
            Debug.LogWarning($"[StaticEntitiesGroup] Entity {staticEntitiesGroupEntity} does not exist or has no StaticEntitiesGroupComponent. Skipping visibility update.");
            return;
        }
        StaticEntitiesGroupComponent staticEntitiesGroupComponent = entityManager.GetComponentData<StaticEntitiesGroupComponent>(staticEntitiesGroupEntity);
        
        staticEntitiesGroupComponent.ViewsCount = viewsCount;
        staticEntitiesGroupComponent.ViewVisibilityPercentages = new float4(
            viewVisibilityPercentageArray[0] / 100f, // Convert percentage to 0-1 range
            viewVisibilityPercentageArray[1] / 100f,
            viewVisibilityPercentageArray[2] / 100f,
            viewVisibilityPercentageArray[3] / 100f
        );
        staticEntitiesGroupComponent.ShaderUpdateRequested = true;
        
        entityManager.SetComponentData(staticEntitiesGroupEntity, staticEntitiesGroupComponent);
    }

    public GameObject GetTemplateGameObject()
    {
        return templateGameObject;
    }

    /// <summary>
    /// Returns the current requested population for this static group.
    /// Uses the first StaticEntitiesGroupStruct as the source of truth.
    /// </summary>
    public int GetCurrentPopulation()
    {
        if (staticEntitiesGroupStructs == null || staticEntitiesGroupStructs.Count == 0)
        {
            Debug.LogError("[StaticEntitiesGroup] GetCurrentPopulation called but staticEntitiesGroupStructs is empty.");
            Debug.Assert(false, "[StaticEntitiesGroup] GetCurrentPopulation requires at least one StaticEntitiesGroupStruct.");
            return 0;
        }

        StaticEntitiesGroupStruct firstGroup = staticEntitiesGroupStructs[0];
        if (!entityManager.Exists(firstGroup.StaticEntitiesGroupEntity) ||
            !entityManager.HasComponent<StaticEntitiesGroupComponent>(firstGroup.StaticEntitiesGroupEntity))
        {
            Debug.LogError("[StaticEntitiesGroup] GetCurrentPopulation: first StaticEntitiesGroupEntity is invalid.");
            Debug.Assert(false, "[StaticEntitiesGroup] GetCurrentPopulation requires a valid StaticEntitiesGroupComponent.");
            return 0;
        }

        StaticEntitiesGroupComponent component =
            entityManager.GetComponentData<StaticEntitiesGroupComponent>(firstGroup.StaticEntitiesGroupEntity);
        return component.RequestedCount;
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


