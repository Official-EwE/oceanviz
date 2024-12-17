using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities;
using Unity.VisualScripting;
using Unity.Mathematics;
using Unity.Transforms;
using System.Reflection.Emit;
using System.Threading.Tasks;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace OceanViz3
{
/// <summary>
/// Represents a preset configuration for static entities including meshes and textures
/// </summary>
[Serializable]
public class StaticEntityPreset
{
    /// <summary>
    /// Name identifier of the preset
    /// </summary>
    public string name;

    /// <summary>
    /// Array of habitat types this preset can be used in
    /// </summary>
    public string[] habitats;
    
    /// <summary>
    /// Collection of meshes used by this preset. Not serialized in JSON.
    /// </summary>
    [NonSerialized] public List<Mesh> meshes;

    /// <summary>
    /// Base color texture for the entity. Not serialized in JSON.
    /// </summary>
    [NonSerialized] public Texture baseColorTexture;

    /// <summary>
    /// Normal map texture for the entity. Not serialized in JSON.
    /// </summary>
    [NonSerialized] public Texture normalTexture;
}

/// <summary>
/// Manages a group of static entities placed on terrain using Unity's detail system
/// </summary>
[Serializable]  
public class StaticEntitiesGroup
{
    /// <summary>
    /// The terrain where static entities will be placed
    /// </summary>
    public Terrain terrain;
    
    /// <summary>
    /// The preset configuration for this group of static entities
    /// </summary>
    public StaticEntityPreset staticEntityPreset;
    
    /// <summary>
    /// Name of the preset being used
    /// </summary>
    public string presetName;

    /// <summary>
    /// Unique identifier for this group
    /// </summary>
    public string groupName;

    /// <summary>
    /// The habitat type this group belongs to
    /// </summary>
    public string habitat;
    
    /// <summary>
    /// List of detail prototypes used for entity placement
    /// </summary>
    public List<DetailPrototype> detailPrototypes = new List<DetailPrototype>();

    /// <summary>
    /// Shared material for the static entities
    /// </summary>
    public Material material;

    /// <summary>
    /// Indices of this group's detail prototypes in the terrain's detail prototypes array
    /// </summary>
    public List<int> detailPrototypesIndices = new List<int>();
    
    /// <summary>
    /// Texture defining where entities can be placed
    /// </summary>
    public Texture2D splatmap;

    /// <summary>
    /// Noise texture for randomizing entity placement
    /// </summary>
    public Texture2D noiseTexture;

    /// <summary>
    /// Cached result of splatmap and noise calculations for performance
    /// </summary>
    public float[,] splatmapTimesNoiseDensityMap;

    /// <summary>
    /// Controls the density of entity placement, modified by UI slider
    /// </summary>
    public float densityFactor = 0.5f;

    private int currentMaxDensity = 16;
    private int detailMapWidth;
    private int detailMapHeight;
    private int splatMapWidth;
    private int splatMapHeight;

    // GUI elements
    private VisualElement dataRow;
    private Slider densitySlider;
    private Button reloadButton;
    private Button deleteButton;
    private List<Toggle> viewToggles = new List<Toggle>();
    
    /// <summary>
    /// Visibility state for each view (up to 4 views supported)
    /// </summary>
    public bool[] viewVisibilityBoolArray = new bool[4] {true, true, true, true};
    private int viewsCount = 1;
    
    /// <summary>
    /// Event triggered when this group should be deleted
    /// </summary>
    public event Action<StaticEntitiesGroup> OnDeleteRequested;

    /// <summary>
    /// Indicates if the group is fully initialized and ready for use
    /// </summary>
    public bool IsReady { get; private set; } = false;

    public void Setup(string presetName, string groupName, StaticEntityPreset staticEntityPreset, VisualElement dataRow, int viewsCount, Terrain terrain, Texture2D splatmap)
    {
        this.presetName = presetName;
        this.groupName = groupName;
        this.staticEntityPreset = staticEntityPreset;
        this.dataRow = dataRow;
        this.terrain = terrain;
        this.splatmap = splatmap;
        
        //// GUI
        // Density slider
        densitySlider = dataRow.Q<Slider>("Slider");
        densitySlider.value = densityFactor;
        densitySlider.RegisterCallback<ChangeEvent<float>>(OnSliderChange);
        
        // View toggles
        for (int i = 0; i < 4; i++)
        {
            Toggle viewToggle = dataRow.Q<Toggle>("ViewToggle" + i);
            viewToggles.Add(viewToggle);

            // Debug print
            //Debug.Log("StaticEntitiesGroup " + name + " viewToggle" + i + " value: " + viewVisibilityBoolArray[i]);

            viewToggle.RegisterValueChangedCallback((evt) => OnViewToggleValueChanged(evt));

            // Set the toggle visibility according to the amount of views
            if (i < viewsCount)
            {
                viewToggle.style.display = DisplayStyle.Flex;
            }
            else
            {
                viewToggle.style.display = DisplayStyle.None;
            }

            // Set the value of the toggle
            viewToggle.value = viewVisibilityBoolArray[i];
        }
        deleteButton = dataRow.Q<Button>("DeleteButton");
        deleteButton.RegisterCallback<ClickEvent>((evt) => DeleteGroupClicked(evt));
        reloadButton = dataRow.Q<Button>("ReloadButton");
        reloadButton.RegisterCallback<ClickEvent>((evt) => ReloadGroup(evt));
        
        //// Terrain
        // Get dimensions of the detail map and splatmap
        detailMapWidth = terrain.terrainData.detailWidth;
        detailMapHeight = terrain.terrainData.detailHeight;
        splatMapWidth = splatmap.width;
        splatMapHeight = splatmap.height;

        // Calculate splatmap times noise density map
        noiseTexture = NoiseGenerator.GenerateNoiseTexture(detailMapWidth, detailMapHeight, 0, 0, 20, 3);
        
        splatmapTimesNoiseDensityMap = new float[detailMapWidth, detailMapHeight];
        
        for (int y = 0; y < detailMapHeight; y++)
        {
            for (int x = 0; x < detailMapWidth; x++)
            {
                float noiseValue = noiseTexture.GetPixel(x, y).r;
                float splatmapGreenValue = SampleSplatmap(x, y);
                    
                splatmapTimesNoiseDensityMap[x, y] = splatmapGreenValue * noiseValue * currentMaxDensity;
            }
        }
    }

    private void DeleteGroupClicked(ClickEvent evt)
    {
        DeleteGroup();
    }

    public void DeleteGroup()
    {   
        IsReady = false;
        
        // Clear the detail layers for the removed prototypes
        int[,] emptyDetailLayer = new int[terrain.terrainData.detailWidth, terrain.terrainData.detailHeight];
        foreach (int index in detailPrototypesIndices)
        {
            // Only set the detail layer if the index is valid
            if (index < terrain.terrainData.detailPrototypes.Length)
            {
                terrain.terrainData.SetDetailLayer(0, 0, index, emptyDetailLayer);
            }
        }

        // Clear local data
        detailPrototypes.Clear();
        detailPrototypesIndices.Clear();

        // Destroy GUI
        if (dataRow != null && dataRow.parent != null)
        {
            dataRow.parent.Remove(dataRow);
        }

        // Raise the delete request event
        OnDeleteRequested?.Invoke(this);
    }

    private async void ReloadGroup(ClickEvent evt)
    {
        // Remove existing detail prototypes
        DetailPrototype[] currentPrototypes = terrain.terrainData.detailPrototypes;
        List<DetailPrototype> remainingPrototypes = new List<DetailPrototype>();

        for (int i = 0; i < currentPrototypes.Length; i++)
        {
            if (!detailPrototypesIndices.Contains(i))
            {
                remainingPrototypes.Add(currentPrototypes[i]);
            }
            else
            {
                // Destroy the GameObject associated with this detail prototype
                GameObject.Destroy(currentPrototypes[i].prototype);
            }
        }

        terrain.terrainData.detailPrototypes = remainingPrototypes.ToArray();

        // Clear existing data
        detailPrototypes.Clear();
        detailPrototypesIndices.Clear();

        // Update all presets
        GroupPresetsManager.Instance.UpdatePresets();

        // Reload and spawn group
        LoadAndSpawnStaticGroup();

        // Update GUI elements
        UpdateGroupVisibility();
        densitySlider.value = densityFactor;
        UpdateDetailPrototypesDensity(densityFactor);
    }

    private void OnViewToggleValueChanged(ChangeEvent<bool> evt)
    {
        var toggle = evt.target as Toggle;

        // Get the index of the toggle
        int viewIndex = int.Parse(toggle.name.Substring(toggle.name.Length - 1));
        
        SetGroupViewVisibility(viewIndex, evt.newValue);
    }
    
    public void SetGroupViewVisibility(int viewIndex, bool isVisible)
    {
        // Set the viewVisibilityPercentageArray value at index to the value of the toggle
        viewVisibilityBoolArray[viewIndex] = isVisible;

        // Debug print
        //Debug.Log("StaticEntitiesGroup " + name + " viewToggle" + v + " value: " + viewVisibilityBoolArray[v]);

        UpdateGroupVisibility();
    }
    
    public void SetGroupViewVisibilityAndUpdateGUI(int viewIndex, bool isVisible)
    {
        // First unregister the callback
        viewToggles[viewIndex].UnregisterValueChangedCallback(OnViewToggleValueChanged);
        
        // Update the toggle value
        viewToggles[viewIndex].value = isVisible;
        
        // Re-register the callback
        viewToggles[viewIndex].RegisterValueChangedCallback(OnViewToggleValueChanged);
        
        // Update the visibility
        SetGroupViewVisibility(viewIndex, isVisible);
    }

    public void UpdateGroupVisibility()
    {
        DetailPrototype[] currentPrototypes = terrain.terrainData.detailPrototypes;
    
        foreach (int detailPrototypeIndex in detailPrototypesIndices)
        {
            Material material = currentPrototypes[detailPrototypeIndex].prototype.GetComponent<MeshRenderer>().sharedMaterial;
        
            Vector4 screenDisplayStart = Vector4.zero;
            Vector4 screenDisplayEnd = Vector4.zero;
        
            for (int viewIndex = 0; viewIndex < viewsCount; viewIndex++)
            {
                if (viewVisibilityBoolArray[viewIndex])
                {
                    float startFloat = (float)viewIndex / viewsCount;
                    float endFloat = (float)(viewIndex + 1) / viewsCount;
                
                    screenDisplayStart[viewIndex] = startFloat;
                    screenDisplayEnd[viewIndex] = endFloat;
                }
            }
        
            material.SetVector("_ScreenDisplayStart", screenDisplayStart);
            material.SetVector("_ScreenDisplayEnd", screenDisplayEnd);
        
            currentPrototypes[detailPrototypeIndex].prototype.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    
        terrain.terrainData.detailPrototypes = currentPrototypes;
    }
    
    public void UpdateViewsCount(int viewsCount)
    {
        // Debug print
        //Debug.Log("StaticEntitiesGroup " + name + " viewsCount: " + viewsCount);
        
        this.viewsCount = viewsCount;

        // Update the toggles according to the viewVisibilityBoolArray
        for (int i = 0; i < 4; i++)
        {
            viewToggles[i].value = viewVisibilityBoolArray[i];
        }

        // Change the toggles visibility according to the amount of views
        for (int i = 0; i < 4; i++)
        {
            if (i < viewsCount)
            {
                viewToggles[i].style.display = DisplayStyle.Flex;
            }
            else
            {
                viewToggles[i].style.display = DisplayStyle.None;
            }
        }
        
        UpdateGroupVisibility();
    }

    private float SampleSplatmap(int detailX, int detailY)
    {
        // Convert detail map coordinates to splatmap coordinates
        // Swap X and Y, keep Y as is to correct the mirroring
        float splatX = (float)detailY / detailMapHeight * splatMapWidth;
        float splatY = (float)detailX / detailMapWidth * splatMapHeight;

        // Use bilinear filtering to sample the splatmap
        return SampleSplatmapBilinear(splatX, splatY);
    }

    private float SampleSplatmapBilinear(float x, float y)
    {
        // This method remains unchanged
        int x1 = Mathf.FloorToInt(x);
        int y1 = Mathf.FloorToInt(y);
        int x2 = Mathf.Min(x1 + 1, splatMapWidth - 1);
        int y2 = Mathf.Min(y1 + 1, splatMapHeight - 1);

        float fx = x - x1;
        float fy = y - y1;

        float c11 = splatmap.GetPixel(x1, y1).g;
        float c12 = splatmap.GetPixel(x1, y2).g;
        float c21 = splatmap.GetPixel(x2, y1).g;
        float c22 = splatmap.GetPixel(x2, y2).g;

        float bottomMix = Mathf.Lerp(c11, c21, fx);
        float topMix = Mathf.Lerp(c12, c22, fx);

        return Mathf.Lerp(bottomMix, topMix, fy);
    }
    
    private void OnSliderChange(ChangeEvent<float> evt)
    {
        SetDensity(evt.newValue);
    }
    
    public void SetDensity(float density)
    {
        densityFactor = density;
        UpdateDetailPrototypesDensity(densityFactor);
    }
    
    public void SetDensityAndUpdateGUI(float density)
    {
        SetDensity(density);
        densitySlider.value = density;
    }

    public void UpdateDetailPrototypesDensity(float factor)
    {
        // For each detail prototype in the terrain, if the prototype is a static entity prototype
        DetailPrototype[] modifiedPrototypes = terrain.terrainData.detailPrototypes;
        for (int i = 0; i < modifiedPrototypes.Length; i++)
        {
            if (detailPrototypesIndices.Contains(i))
            {
                modifiedPrototypes[i].density = factor * 12;
            }
        }
        
        terrain.terrainData.detailPrototypes = modifiedPrototypes;
    }

    public async void LoadAndSpawnStaticGroup()
    {
        IsReady = false;
        
        // Get the StaticEntityPreset from GroupPresetsManager
        var preset = GroupPresetsManager.Instance.staticEntitiesPresetsList
            .FirstOrDefault(p => p.name == presetName);
        if (!string.IsNullOrEmpty(preset.name)) // Check if the preset is not null or empty
        {
            staticEntityPreset = preset;
        }
        else
        {
            Debug.LogError($"StaticEntityPreset not found for {presetName}");
            return;
        }
        
        // Load textures
        Texture2D baseColorTexture = await LoadTexture("base_color.png");
        Texture2D loadedNormalTexture = await LoadTexture("normal.png");
        if (baseColorTexture == null || loadedNormalTexture == null)
        {
            Debug.LogError($"Failed to load textures for {presetName}");
            return;
        }

        // Set normalTexture to be Linear (not sRGB)
        Texture2D normalTexture = new Texture2D(loadedNormalTexture.width, loadedNormalTexture.height, loadedNormalTexture.format, true, true);
        Graphics.CopyTexture(loadedNormalTexture, normalTexture);

        // Load meshes
        List<Mesh> meshes = await LoadMeshes();
        if (meshes.Count == 0)
        {
            Debug.LogError($"No valid mesh files found for {presetName}");
            return;
        }

        // Check if terrain is still valid after async operations
        if (terrain == null)
        {
            Debug.LogError($"Terrain became null while loading assets for static group {presetName}");
            return;
        }

        try
        {
            // Create GameObjects for each mesh
            List<GameObject> staticEntityGameObjects = new List<GameObject>();
            foreach (var mesh in meshes)
            {
                GameObject staticEntityGameObjectFromMesh = CreateStaticEntityGameObjectFromMesh(mesh, baseColorTexture, normalTexture);
                
                // Add is as a child of the terrain
                staticEntityGameObjectFromMesh.transform.parent = terrain.transform;
                
                // Hide the GameObject
                staticEntityGameObjectFromMesh.SetActive(false);
                
                // Add a PROC_ prefix to the GameObject name
                staticEntityGameObjectFromMesh.name = "PROC_" + staticEntityGameObjectFromMesh.name;
                
                staticEntityGameObjects.Add(staticEntityGameObjectFromMesh);
            }
            
            // From each GameObject, create a DetailPrototype
            foreach (var staticEntityGameObject in staticEntityGameObjects)
            {
                DetailPrototype detailPrototype = new DetailPrototype
                {
                    prototype = staticEntityGameObject,
                    renderMode = DetailRenderMode.VertexLit,
                    useInstancing = true,
                    usePrototypeMesh = true,
                    minWidth = 0.5f,
                    maxWidth = 3,
                    minHeight = 0.5f,
                    maxHeight = 3,
                    noiseSpread = 100,
                    healthyColor = Color.white,
                    dryColor = Color.white,
                };
                
                detailPrototypes.Add(detailPrototype);
            }

            // Final terrain null check before modification
            if (terrain == null)
            {
                Debug.LogError($"Terrain became null while creating detail prototypes for static group {presetName}");
                // Clean up created objects
                foreach (var obj in staticEntityGameObjects)
                {
                    GameObject.Destroy(obj);
                }
                return;
            }

            // Add the detail prototypes to the terrain
            DetailPrototype[] existingPrototypes = terrain.terrainData.detailPrototypes;
            DetailPrototype[] newPrototypes = new DetailPrototype[existingPrototypes.Length + detailPrototypes.Count];
            existingPrototypes.CopyTo(newPrototypes, 0);
            
            for (int i = 0; i < detailPrototypes.Count; i++)
            {
                int index = existingPrototypes.Length + i;
                newPrototypes[index] = detailPrototypes[i];
                detailPrototypesIndices.Add(index);
            }
            
            terrain.terrainData.detailPrototypes = newPrototypes;
            
            SetInitialDensity(currentMaxDensity);
            IsReady = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error spawning static group {presetName}: {e.Message}");
            // Clean up any partially created objects
            foreach (var prototype in detailPrototypes)
            {
                if (prototype.prototype != null)
                {
                    GameObject.Destroy(prototype.prototype);
                }
            }
            detailPrototypes.Clear();
            detailPrototypesIndices.Clear();
            IsReady = false;
        }
    }
    
    public void SetInitialDensity(int newMaxDensity) // A map based on splatmap and noise. Final static group depends on Prototype density and is modified with UpdateDetailPrototypesDensity
    {
        currentMaxDensity = newMaxDensity;
        int variationCount = detailPrototypes.Count;
        
        // Set the detail layers for each variation
        for (int i = 0; i < variationCount; i++)
        {
            int[,] detailLayer = new int[terrain.terrainData.detailWidth, terrain.terrainData.detailHeight];

            for (int y = 0; y < terrain.terrainData.detailHeight; y++)
            {
                for (int x = 0; x < terrain.terrainData.detailWidth; x++)
                {
                    detailLayer[x, y] = Mathf.RoundToInt(splatmapTimesNoiseDensityMap[x, y] * currentMaxDensity / variationCount);
                }
            }

            terrain.terrainData.SetDetailLayer(0, 0, detailPrototypesIndices[i], detailLayer);
        }

        UpdateDetailPrototypesDensity(densityFactor);
    }

    private async Task<Texture2D> LoadTexture(string fileName)
    {
        var path = Application.streamingAssetsPath + "/StaticEntities/" + presetName + "/" + fileName;
        if (!File.Exists(path))
        {
            Debug.LogError(fileName + " not found in " + presetName);
            return null;
        }

        byte[] fileData = File.ReadAllBytes(path);
        Texture2D texture = new Texture2D(2, 2);
        texture.LoadImage(fileData);

        if (texture == null)
        {
            Debug.LogError(fileName + " failed to load from path: " + path);
            return null;
        }

        return texture;
    }

    private async Task<List<Mesh>> LoadMeshes()
    {
        List<Mesh> meshes = new List<Mesh>();
        int modelIndex = 1;

        while (true)
        {
            string gltfPath = Application.streamingAssetsPath + $"/StaticEntities/{presetName}/model{modelIndex}.glb";
            if (!File.Exists(gltfPath))
            {
                break;
            }

            var gltf = new GLTFast.GltfImport();
            bool success = await gltf.Load(gltfPath);
            if (success)
            {
                Mesh mesh = gltf.GetMeshes()[0];
                meshes.Add(mesh);
            }
            else
            {
                Debug.LogError($"Loading glTF failed for model{modelIndex}.glb");
            }

            modelIndex++;
        }

        return meshes;
    }

    GameObject CreateStaticEntityGameObjectFromMesh(Mesh mesh, Texture2D baseColorTexture, Texture2D normalTexture)
    {
        GameObject gameObject = new GameObject(presetName);
        MeshFilter meshFilter = gameObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;
        
        MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
        
        material = new Material(Shader.Find("Shader Graphs/StaticEntityShaderGraph"));
        material.SetTexture("_Albedo", baseColorTexture);
        material.SetTexture("_Normal", normalTexture);
        material.EnableKeyword("_NORMALMAP");
        material.EnableKeyword("_ALPHATEST_ON");
        material.SetInt("_Cull", (int) CullMode.Off);
        
        meshRenderer.material = material;
        
        return gameObject;
    }

    public async void UpdateTerrainAndSplatmap(Terrain newTerrain, Texture2D newSplatmap)
    {
        if (newTerrain == null)
        {
            Debug.LogError($"Cannot update terrain for static group {presetName}: New terrain is null");
            return;
        }

        // Clear old detail prototypes first
        if (terrain != null)
        {
            DetailPrototype[] currentPrototypes = terrain.terrainData.detailPrototypes;
            List<DetailPrototype> remainingPrototypes = new List<DetailPrototype>();

            for (int i = 0; i < currentPrototypes.Length; i++)
            {
                if (!detailPrototypesIndices.Contains(i))
                {
                    remainingPrototypes.Add(currentPrototypes[i]);
                }
                else
                {
                    // Destroy the GameObject associated with this detail prototype
                    GameObject.Destroy(currentPrototypes[i].prototype);
                }
            }

            terrain.terrainData.detailPrototypes = remainingPrototypes.ToArray();
        }

        // Wait for a frame to ensure the old terrain is properly cleaned up
        await Task.Yield();

        // Update references
        terrain = newTerrain;
        splatmap = newSplatmap;
        
        // Reset indices since we're creating new detail prototypes
        detailPrototypesIndices.Clear();
        detailPrototypes.Clear();
        
        // Update splatmap dimensions
        splatMapWidth = splatmap.width;
        splatMapHeight = splatmap.height;
        detailMapWidth = terrain.terrainData.detailWidth;
        detailMapHeight = terrain.terrainData.detailHeight;
        
        // Recalculate splatmap times noise density map
        splatmapTimesNoiseDensityMap = new float[detailMapWidth, detailMapHeight];
        for (int y = 0; y < detailMapHeight; y++)
        {
            for (int x = 0; x < detailMapWidth; x++)
            {
                float noiseValue = noiseTexture.GetPixel(x, y).r;
                float splatmapGreenValue = SampleSplatmap(x, y);
                splatmapTimesNoiseDensityMap[x, y] = splatmapGreenValue * noiseValue * currentMaxDensity;
            }
        }

        // Wait another frame to ensure everything is properly set up
        await Task.Yield();

        // Verify terrain is still valid
        if (terrain == null)
        {
            Debug.LogError($"Terrain became null while updating terrain for static group {presetName}");
            return;
        }

        // Reload the static entities with new terrain and splatmap
        await Task.Yield(); // Give one more frame for safety
        LoadAndSpawnStaticGroup();
    }
}
}
