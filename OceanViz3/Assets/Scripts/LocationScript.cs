using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OceanViz3;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UIElements;
using Cinemachine;

/// <summary>
/// Represents a location preset with values like turbidity color settings
/// </summary>
[Serializable]
public struct LocationPreset
{
    public string name;
    public Color turbidity_low_color;
    public Color turbidity_high_color;
}

/// <summary>
/// Represents a flora habitat preset for a location with associated splatmap texture. The splatmap is used to place the flora on the terrain.
/// </summary>
[Serializable]
public struct FloraHabitatPreset
{
    public string name;
    public Texture2D splatmap;
}

/// <summary>
/// Manages location-specific settings and behaviors including terrain, turbidity effects, and habitat management.
/// This script handles the initialization and updates of post-processing effects for water turbidity visualization.
/// </summary>
public class LocationScript : MonoBehaviour
{
    /// <summary>
    /// Indicates whether the LocationScript has completed initialization and is ready for use
    /// </summary>
    public static bool IsReady { get; private set; }

    public Terrain terrain;
    [HideInInspector] public LocationPreset locationPreset;
    public GameObject dollyCart;
    public List<FloraHabitatPreset> habitatPresets = new List<FloraHabitatPreset>();
    private int viewsCount = 1;
    [HideInInspector] public List<float> turbidityPerView = new List<float> {0.5f, 0.5f, 0.5f, 0.5f};
    /// <summary>
    /// Current Universal Render Pipeline (URP) asset configuration. Used to enable/disable renderer features.
    /// </summary>
    public UniversalRenderPipelineAsset _pipelineAssetCurrent;
    private bool materialsInitialized = false;
    private Coroutine initializationCoroutine;
    
    // GUI
    public VisualElement turbidityRow;
    public List<SliderInt> turbidityPercentageSliderInts = new List<SliderInt>();
    
    // Post effect materials
    private Material turbidityMaterial;
    /// <summary>
    /// Original turbidity material. Used in editor to reset the material to starting state 
    /// </summary>
    private Material originalTurbidityMaterial;
    private Material bloomMaterial;
    /// <summary>
    /// Original bloom material. Used in editor to reset the material to starting state
    /// </summary>
    private Material originalBloomMaterial;
    
    /// <summary>
    /// Initializes the location, sets up terrain settings, and prepares post-processing materials.
    /// Notifies MainScene when initialization is complete.
    /// </summary>
    void Start()
    {
        IsReady = false;
        
        SceneManager.SetActiveScene(gameObject.scene);
        
        //// Terrain
        // Set terrain detail scattering mode
        terrain.terrainData.SetDetailScatterMode(DetailScatterMode.CoverageMode);
        
        //// Turbidity
        // Get the turbidity renderer feature
        var _rendererFeatures = _pipelineAssetCurrent.scriptableRenderer.GetType()
            .GetProperty("rendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance) ?.GetValue(_pipelineAssetCurrent.scriptableRenderer, null) as List<ScriptableRendererFeature>;
        
        var turbidityFeature = _rendererFeatures.Find(x => x.name == "Turbidity");
        var bloomFeature = _rendererFeatures.Find(x => x.name == "Bloom");
        
        Debug.Log("Turbidity Feature: " + (turbidityFeature != null ? "Found" : "Not Found"));
        Debug.Log("Bloom Feature: " + (bloomFeature != null ? "Found" : "Not Found"));
        
        turbidityMaterial = turbidityFeature?.GetType().GetField("passMaterial").GetValue(turbidityFeature) as Material;
        bloomMaterial = bloomFeature?.GetType().GetField("passMaterial").GetValue(bloomFeature) as Material;
        
        Debug.Log("Turbidity Material: " + (turbidityMaterial != null ? "Found" : "Not Found"));
        Debug.Log("Bloom Material: " + (bloomMaterial != null ? "Found" : "Not Found"));
        
        // Store the original materials
        originalTurbidityMaterial = turbidityMaterial;
        originalBloomMaterial = bloomMaterial;
        
        // Create temporary material instances
        turbidityMaterial = new Material(turbidityMaterial);
        bloomMaterial = new Material(bloomMaterial);
        
        // Assign the temporary material instances to the features
        turbidityFeature.GetType().GetField("passMaterial").SetValue(turbidityFeature, turbidityMaterial);
        bloomFeature.GetType().GetField("passMaterial").SetValue(bloomFeature, bloomMaterial);
        
        // After all is loaded, notify the MainScene script. It will run Setup()
        GameObject mainSceneScript = GameObject.Find("MainSceneScript");
        if (mainSceneScript != null)
        {
            Debug.Log("MainSceneScript found by LocationScript");
            
            // Send the loaded location Scene to the MainScene script
            mainSceneScript.GetComponent<MainScene>().OnLocationLoaded(this);
        }
        else
        {
            Debug.LogError("MainSceneScript not found by LocationScript");
        }
    }
    
    /// <summary>
    /// Sets up the location with specified parameters and initializes turbidity controls
    /// </summary>
    public void Setup(VisualElement turbidityRow, LocationPreset locationPreset)
    {
        IsReady = false;
        this.locationPreset = locationPreset;
        
        // GUI
        this.turbidityRow = turbidityRow;
        
        // Turbidity Sliders
        for (int i = 0; i < 4; i++)
        {
            SliderInt sliderInt = turbidityRow.Q<SliderInt>("TurbidityPercentageSliderInt" + i);
            
            sliderInt.RegisterValueChangedCallback((evt) => OnTurbiditySliderIntValueChanged(evt));
            turbidityPercentageSliderInts.Add(sliderInt);
            
            // If it's not the first slider, hide it
            if (i != 0)
            {
                sliderInt.style.display = DisplayStyle.None;
            }
            
            // Set initial turbidity value to 0.33 (33%)
            SetTurbidityForView(i, 0.33f);
        }
        
        // Start the coroutine to check if materials are initialized
        initializationCoroutine = StartCoroutine(InitializeMaterialsCoroutine());
    }

    /// <summary>
    /// Coroutine to check if materials are initialized
    /// </summary>
    private IEnumerator InitializeMaterialsCoroutine()
    {
        while (!materialsInitialized)
        {
            yield return new WaitForSeconds(0.1f);

            if (InitializeMaterials())
            {
                materialsInitialized = true;
                Debug.Log("Materials initialized successfully");
                UpdateTurbidity();
                IsReady = true;
                Debug.Log("LocationScript is now ready");
            }
        }
    }

    private bool InitializeMaterials()
    {
        if (turbidityMaterial != null && bloomMaterial != null)
        {
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Editor only. Resets the turbidity and bloom materials to their original state when the script is destroyed (when exiting play mode)
    /// </summary>
    private void OnDestroy()
    {
        if (originalTurbidityMaterial != null)
        {
            var _rendererFeatures = _pipelineAssetCurrent.scriptableRenderer.GetType()
                .GetProperty("rendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance) ?.GetValue(_pipelineAssetCurrent.scriptableRenderer, null) as List<ScriptableRendererFeature>;
            var turbidityFeature = _rendererFeatures.Find(x => x.name == "Turbidity");
            turbidityFeature.GetType().GetField("passMaterial").SetValue(turbidityFeature, originalTurbidityMaterial);
        }
        
        if (originalBloomMaterial != null)
        {
            var _rendererFeatures = _pipelineAssetCurrent.scriptableRenderer.GetType()
                .GetProperty("rendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance) ?.GetValue(_pipelineAssetCurrent.scriptableRenderer, null) as List<ScriptableRendererFeature>;
            var bloomFeature = _rendererFeatures.Find(x => x.name == "Bloom");
            bloomFeature.GetType().GetField("passMaterial").SetValue(bloomFeature, originalBloomMaterial);
        }
    }    
    
    /// <summary>
    /// Editor only. Resets the turbidity and bloom materials to their original state when the script is disabled
    /// </summary>
    private void OnDisable()
    {
        if (originalTurbidityMaterial != null)
        {
            var _rendererFeatures = _pipelineAssetCurrent.scriptableRenderer.GetType()
                .GetProperty("rendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(_pipelineAssetCurrent.scriptableRenderer, null) as List<ScriptableRendererFeature>;
            var turbidityFeature = _rendererFeatures.Find(x => x.name == "Turbidity");
            turbidityFeature.GetType().GetField("passMaterial").SetValue(turbidityFeature, originalTurbidityMaterial);
        }
        
        if (originalBloomMaterial != null)
        {
            var _rendererFeatures = _pipelineAssetCurrent.scriptableRenderer.GetType()
                .GetProperty("rendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(_pipelineAssetCurrent.scriptableRenderer, null) as List<ScriptableRendererFeature>;
            var bloomFeature = _rendererFeatures.Find(x => x.name == "Bloom");
            bloomFeature.GetType().GetField("passMaterial").SetValue(bloomFeature, originalBloomMaterial);
        }
    }
    
    /// <summary>
    /// Updates the number of active views and adjusts UI elements accordingly
    /// </summary>
    public void UpdateViewsCount(int incViewsCount)
    {
        viewsCount = incViewsCount;
        
        // Change the sliders visibility according to the amount of views
        for (int i = 0; i < 4; i++)
        {
            if (i < incViewsCount)
            {
                turbidityPercentageSliderInts[i].style.display = DisplayStyle.Flex;
            }
            else
            {
                turbidityPercentageSliderInts[i].style.display = DisplayStyle.None;
            }
        }
        
        UpdateTurbidity();
    }
    
    public void OnTurbiditySliderIntValueChanged(ChangeEvent<int> evt)
    {
        var slider = evt.target as SliderInt;
        
        // Get the index of the slider
        int viewIndex = int.Parse(slider.name.Substring(slider.name.Length - 1));
        
        // Convert the percentage value to a float value between 0 and 1
        float turbidityStrength = evt.newValue / 100f;
        
        SetTurbidityForView(viewIndex, turbidityStrength);
    }

    /// <summary>
    /// Sets the turbidity value for a specific view
    /// </summary>
    /// <param name="viewIndex">Index of the view (0-3)</param>
    /// <param name="turbidityValue">Turbidity value between 0 and 1</param>
    public void SetTurbidityForView(int viewIndex, float turbidityValue)
    {
        // Clamp value between 0 and 1
        turbidityValue = Mathf.Clamp01(turbidityValue);
        
        // Update the turbidity value
        turbidityPerView[viewIndex] = turbidityValue;
        
        // Update the GUI slider if it exists
        if (viewIndex < turbidityPercentageSliderInts.Count)
        {
            // Convert turbidity value (0-1) to percentage (0-100)
            int percentageValue = Mathf.RoundToInt(turbidityValue * 100);
            turbidityPercentageSliderInts[viewIndex].SetValueWithoutNotify(percentageValue);
        }
        
        UpdateTurbidity();
    }
    
    /// <summary>
    /// Updates turbidity effects and bloom settings for all active views
    /// </summary>
    public void UpdateTurbidity()
    {
        Debug.Log("Updating turbidity");
        Debug.Log("Views count: " + viewsCount);
        
        if (turbidityMaterial == null)
        {
            Debug.LogError("turbidityMaterial is null");
            return;
        }
        
        if (bloomMaterial == null)
        {
            Debug.LogError("bloomMaterial is null");
            return;
        }
        
        // Set the turbidity and color values for each view
        for (int i = 0; i < viewsCount; i++)
        {
            turbidityMaterial.SetFloat("_Turbidity" + i, turbidityPerView[i]);
            Debug.Log("Setting Turbidity" + i + " to: " + turbidityPerView[i]);
            
            // Set the bloom weight for each view, according to the turbidity value. At turbidity 0 the weight is 0, at turbidity 1 the weight is 1
            // The name of the properties are _BloomWeightView0, _BloomWeightView1, _BloomWeightView2 and _BloomWeightView3
            bloomMaterial.SetFloat("_BloomWeightView" + i, Mathf.Lerp(0f, 1f, turbidityPerView[i]));

            // Set Color for each view based on turbidity value
            Color lerpedColor = Color.Lerp(locationPreset.turbidity_low_color, locationPreset.turbidity_high_color, turbidityPerView[i]);
            turbidityMaterial.SetColor("_Color" + i, lerpedColor);
        }

        // Set the mask for each view
        switch (viewsCount)
        {
            case 1:
                turbidityMaterial.SetVector("_MaskView0", new Vector2(0.0f, 1.0f));
                turbidityMaterial.SetVector("_MaskView1", new Vector2(0.0f, 0.0f));
                turbidityMaterial.SetVector("_MaskView2", new Vector2(0.0f, 0.0f));
                turbidityMaterial.SetVector("_MaskView3", new Vector2(0.0f, 0.0f));
                bloomMaterial.SetVector("_MaskView0", new Vector2(0.0f, 1.0f));
                bloomMaterial.SetVector("_MaskView1", new Vector2(0.0f, 0.0f));
                bloomMaterial.SetVector("_MaskView2", new Vector2(0.0f, 0.0f));
                bloomMaterial.SetVector("_MaskView3", new Vector2(0.0f, 0.0f));
                break;
            case 2:
                turbidityMaterial.SetVector("_MaskView0", new Vector2(0.0f, 0.5f));
                turbidityMaterial.SetVector("_MaskView1", new Vector2(0.5f, 1.0f));
                turbidityMaterial.SetVector("_MaskView2", new Vector2(0.0f, 0.0f));
                turbidityMaterial.SetVector("_MaskView3", new Vector2(0.0f, 0.0f));
                bloomMaterial.SetVector("_MaskView0", new Vector2(0.0f, 0.5f));
                bloomMaterial.SetVector("_MaskView1", new Vector2(0.5f, 1.0f));
                bloomMaterial.SetVector("_MaskView2", new Vector2(0.0f, 0.0f));
                bloomMaterial.SetVector("_MaskView3", new Vector2(0.0f, 0.0f));
                break;
            case 3:
                turbidityMaterial.SetVector("_MaskView0", new Vector2(0.0f, 0.3333f));
                turbidityMaterial.SetVector("_MaskView1", new Vector2(0.3333f, 0.6666f));
                turbidityMaterial.SetVector("_MaskView2", new Vector2(0.6666f, 1.0f));
                turbidityMaterial.SetVector("_MaskView3", new Vector2(0.0f, 0.0f));
                bloomMaterial.SetVector("_MaskView0", new Vector2(0.0f, 0.3333f));
                bloomMaterial.SetVector("_MaskView1", new Vector2(0.3333f, 0.6666f));
                bloomMaterial.SetVector("_MaskView2", new Vector2(0.6666f, 1.0f));
                bloomMaterial.SetVector("_MaskView3", new Vector2(0.0f, 0.0f));
                break;
            case 4:
                turbidityMaterial.SetVector("_MaskView0", new Vector2(0.0f, 0.25f));
                turbidityMaterial.SetVector("_MaskView1", new Vector2(0.25f, 0.5f));
                turbidityMaterial.SetVector("_MaskView2", new Vector2(0.5f, 0.75f));
                turbidityMaterial.SetVector("_MaskView3", new Vector2(0.75f, 1.0f));
                bloomMaterial.SetVector("_MaskView0", new Vector2(0.0f, 0.25f));
                bloomMaterial.SetVector("_MaskView1", new Vector2(0.25f, 0.5f));
                bloomMaterial.SetVector("_MaskView2", new Vector2(0.5f, 0.75f));
                bloomMaterial.SetVector("_MaskView3", new Vector2(0.75f, 1.0f));
                break;
        }
    }
    
    /// <summary>
    /// Retrieves boid bounds GameObjects associated with a specific habitat name
    /// </summary>
    public List<GameObject> GetBoidBoundsByBiomeName(string requestedBiomeName)
    {
        Debug.Log("Getting boidBounds for habitat name: " + requestedBiomeName);
        
        Dictionary<string, List<GameObject>> biomeNameToBoidBoundsList = new Dictionary<string, List<GameObject>>();
        
        // If the biomeName is empty, set it to "Default"
        if (requestedBiomeName == null || requestedBiomeName == "")
        {
            requestedBiomeName = "Default";
        }
        
        // Find all boidBounds in the scene and separate them by habitat name
        GameObject[] boidBoundsArray = GameObject.FindGameObjectsWithTag("BoidBounds");
        foreach (GameObject boidBounds in boidBoundsArray)
        {
            string biomeName = boidBounds.GetComponent<BoidBounds>().HabitatName;
            
            // If there is no habitat name, set it to "Default"
            if (biomeName == null || biomeName == "")
            {
                biomeName = "Default";
            }
            
            // If the habitat name is not in the dictionary, add it
            if (!biomeNameToBoidBoundsList.ContainsKey(biomeName))
            {
                biomeNameToBoidBoundsList[biomeName] = new List<GameObject>();
            }
            
            // Add the boidBounds to the list of boidBounds for the habitat
            biomeNameToBoidBoundsList[biomeName].Add(boidBounds);
            Debug.Log("Added boidBounds for habitat: " + biomeName);
        }
        
        // If the habitat name is in the dictionary, return the list of boidBounds
        if (biomeNameToBoidBoundsList.ContainsKey(requestedBiomeName))
        {
            Debug.Log("Found boidBounds amount: " + biomeNameToBoidBoundsList[requestedBiomeName].Count);
            
            return biomeNameToBoidBoundsList[requestedBiomeName];
        }
        
        // Else, throw error
        Debug.LogError("Biome not found: " + requestedBiomeName);
        return null;
    }

    /// <summary>
    /// Returns the terrain component associated with this location
    /// </summary>
    public Terrain GetTerrain()
    {
        return terrain;
    }

    /// <summary>
    /// Retrieves the splatmap texture for a specific flora habitat
    /// </summary>
    /// <param name="habitat">Name of the habitat</param>
    public Texture2D GetFloraBiomeSplatmap(string habitat)
    {
        foreach (var floraBiomePreset in habitatPresets)
        {
            if (floraBiomePreset.name == habitat)
            {
                return floraBiomePreset.splatmap;
            }
        }

        return null;
    }
}
