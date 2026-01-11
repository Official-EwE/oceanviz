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
    public int wind_turbine_pylon_amount;
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
    private LocationPreset currentLocationPreset;
    public GameObject dollyCart;
    public List<FloraHabitatPreset> habitatPresets = new List<FloraHabitatPreset>();
    
    private int viewsCount = 1;
    /// <summary>
    /// Default initial turbidity used when incoming value equals the old default (0.5) or is unavailable
    /// </summary>
    private const float DefaultInitialTurbidity = 0.25f;
    /// <summary>
    /// Current Universal Render Pipeline (URP) asset configuration. Used to enable/disable renderer features.
    /// </summary>
    public UniversalRenderPipelineAsset _pipelineAssetCurrent;
    private bool materialsInitialized = false;
    private Coroutine initializationCoroutine;
    private MainScene mainScene;
    
    // GUI
    public VisualElement turbidityRow;
    public List<Slider> turbiditySliders = new List<Slider>();
    
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
    
    // Turbidity colors applied to the turbidity post effect material on load
    public Color colorAtTurbidityValueOne = new Color32(0x30, 0x5C, 0x54, 0xFF);     // #305C54 for value +1
    public Color colorAtTurbidityValueZero = new Color32(0x26, 0x74, 0x7F, 0xFF);    // #26747F for value 0
    public Color colorAtTurbidityValueMinusOne = new Color32(0x57, 0x48, 0x26, 0xFF); // #574826 for value -1
    
    // Removed camera-driven turbidity color logic
    
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
    public void Setup(VisualElement turbidityRow, LocationPreset locationPreset, List<float> turbidityPerView, int viewsCount, MainScene mainScene)
    {
        IsReady = false;
        this.locationPreset = locationPreset;
        this.currentLocationPreset = locationPreset;
        this.viewsCount = viewsCount;
        this.mainScene = mainScene;
        
        // GUI
        this.turbidityRow = turbidityRow;
        
        // Turbidity Sliders
        for (int i = 0; i < 4; i++)
        {
            Slider slider = turbidityRow.Q<Slider>("TurbiditySlider" + i);
            if (slider != null)
            {
                slider.RegisterValueChangedCallback((evt) => mainScene.simulationModeManager.OnTurbiditySliderValueChanged(evt));
                turbiditySliders.Add(slider);
            }
            
            // If it's not the first slider, hide it
            if (i >= viewsCount)
            {
                if (slider != null) slider.style.display = DisplayStyle.None;
            }
            
            // Set initial turbidity value
            float initialTurbidity = DefaultInitialTurbidity;
            if (turbidityPerView != null)
            {
                if (i < turbidityPerView.Count)
                {
                    initialTurbidity = turbidityPerView[i];
                }
            }
            if (Mathf.Approximately(initialTurbidity, 0.5f))
            {
                initialTurbidity = DefaultInitialTurbidity;
            }
            SetTurbidityForView(i, initialTurbidity);
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
                ApplyTurbidityColorsIfAvailable();
                UpdateTurbidity();
                IsReady = true;
                Debug.Log("LocationScript is now ready");
            }
        }
    }

    // Removed Update() turbidity color adjustments

    void Update()
    {
        if (turbidityMaterial == null)
        {
            return;
        }
        if (mainScene == null || mainScene.mainCamera == null)
        {
            return;
        }

        float cameraY = mainScene.mainCamera.transform.position.y;
        float t = Mathf.InverseLerp(-10f, -180f, cameraY);
        float lightAmount = Mathf.Lerp(1.0f, 0.22f, t);
        turbidityMaterial.SetFloat("_LightAmount", lightAmount);
    }

    private bool InitializeMaterials()
    {
        if (turbidityMaterial != null && bloomMaterial != null)
        {
            return true;
        }
        return false;
    }
    
    private static void SetMaterialColorIfExists(Material targetMaterial, string propertyName, Color color)
    {
        if (targetMaterial.HasProperty(propertyName))
        {
            targetMaterial.SetColor(propertyName, color);
        }
    }
    
    // Applies the public colors to the turbidity material if the shader defines the properties
    private void ApplyTurbidityColorsIfAvailable()
    {
        if (turbidityMaterial == null)
        {
            return;
        }
        
        // Support both underscored and non-underscored property naming
        SetMaterialColorIfExists(turbidityMaterial, "ColorAtTurbidityValueOne", colorAtTurbidityValueOne);
        SetMaterialColorIfExists(turbidityMaterial, "_ColorAtTurbidityValueOne", colorAtTurbidityValueOne);
        
        SetMaterialColorIfExists(turbidityMaterial, "ColorAtTurbidityValueZero", colorAtTurbidityValueZero);
        SetMaterialColorIfExists(turbidityMaterial, "_ColorAtTurbidityValueZero", colorAtTurbidityValueZero);
        
        SetMaterialColorIfExists(turbidityMaterial, "ColorAtTurbidityValueMinusOne", colorAtTurbidityValueMinusOne);
        SetMaterialColorIfExists(turbidityMaterial, "_ColorAtTurbidityValueMinusOne", colorAtTurbidityValueMinusOne);
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
                if (i < turbiditySliders.Count) turbiditySliders[i].style.display = DisplayStyle.Flex;
            }
            else
            {
                if (i < turbiditySliders.Count) turbiditySliders[i].style.display = DisplayStyle.None;
            }
        }
        
        UpdateTurbidity();
    }
    


    /// <summary>
    /// Sets the turbidity value for a specific view
    /// </summary>
    /// <param name="viewIndex">Index of the view (0-3)</param>
    /// <param name="turbidityValue">Turbidity value between 0 and 1</param>
    public void SetTurbidityForView(int viewIndex, float turbidityValue)
    {
        // Clamp value between -1 and 1
        turbidityValue = Mathf.Clamp(turbidityValue, -1f, 1f);
        
        // Update the turbidity value in the SimulationModeManager
        var mainScene = FindObjectOfType<MainScene>();
        if (mainScene != null && mainScene.simulationModeManager != null)
        {
            mainScene.simulationModeManager.turbidityPerView[viewIndex] = turbidityValue;
        }
        
        // Update the GUI slider if it exists
        if (viewIndex < turbiditySliders.Count)
        {
            turbiditySliders[viewIndex].SetValueWithoutNotify(turbidityValue);
        }
        
        UpdateTurbidity();
    }
    
    /// <summary>
    /// Updates turbidity effects and bloom settings for all active views
    /// </summary>
    public void UpdateTurbidity()
    {
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
        
        var mainScene = FindObjectOfType<MainScene>();
        if (mainScene == null || mainScene.simulationModeManager == null)
        {
            Debug.LogError("SimulationModeManager not found");
            return;
        }

        // Set the turbidity and color values for each view
        for (int i = 0; i < viewsCount; i++)
        {
            float turbidityValue = mainScene.simulationModeManager.turbidityPerView[i];
            turbidityMaterial.SetFloat("_Turbidity" + i, turbidityValue);

            // Bloom mapping: t=0 -> 0.25, t=±1 -> 1.0 (lerped by |t|)
            float bloomWeight = Mathf.Lerp(0.25f, 1.0f, Mathf.Abs(turbidityValue));
            bloomMaterial.SetFloat("_BloomWeightView" + i, bloomWeight);
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
