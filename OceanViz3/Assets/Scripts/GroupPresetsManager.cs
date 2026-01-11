using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using GLTFast;

namespace OceanViz3
{
/// <summary>
/// Manages presets for both dynamic and static entities in the simulation.
/// This singleton class is responsible for loading and providing access to entity presets from JSON files.
/// </summary>
public class GroupPresetsManager : MonoBehaviour
{
    private static GroupPresetsManager instance;
    
    /// <summary>
    /// Singleton instance that ensures only one GroupPresetsManager exists in the scene.
    /// Creates a new GameObject with this component if none exists.
    /// </summary>
    public static GroupPresetsManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<GroupPresetsManager>();
                if (instance == null)
                {
                    GameObject go = new GameObject("GroupPresetsManager");
                    instance = go.AddComponent<GroupPresetsManager>();
                }
            }
            return instance;
        }
    }
    
    /// <summary>
    /// Collection of presets for dynamic entities (e.g., boid groups, moving objects)
    /// Loaded from JSON files in the StreamingAssets/DynamicEntities folder
    /// </summary>
    public List<DynamicEntityPreset> dynamicEntitiesPresetsList = new List<DynamicEntityPreset>();

    /// <summary>
    /// Collection of presets for static entities using ECS
    /// Loaded from JSON files in the StreamingAssets/StaticEntities folder
    /// </summary>
    public List<StaticEntityPreset> staticEntitiesPresetsList = new List<StaticEntityPreset>();
    
    /// <summary>
    /// Indicates whether the manager has finished loading presets and is ready for use
    /// </summary>
    public bool IsReady { get; private set; } = true;

    private World world;
    private EntityManager entityManager;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Initialize lists
            dynamicEntitiesPresetsList = new List<DynamicEntityPreset>();
            staticEntitiesPresetsList = new List<StaticEntityPreset>();
            
            // Initialize ECS components
            world = World.DefaultGameObjectInjectionWorld;
            entityManager = world.EntityManager;
            
            UpdatePresets();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    /// <summary>
    /// Reloads all presets from the StreamingAssets directory.
    /// Called during initialization and when preset files are modified.
    /// Sets IsReady to false while loading and true when complete.
    /// </summary>
    public void UpdatePresets()
    {
        IsReady = false;
        
        dynamicEntitiesPresetsList = new List<DynamicEntityPreset>();
        staticEntitiesPresetsList = new List<StaticEntityPreset>();

        // Read dynamic entity presets
        string dynamicPresetsPath = Path.Combine(Application.streamingAssetsPath, "DynamicEntities");
        ReadDynamicPresets(dynamicPresetsPath, dynamicEntitiesPresetsList);

        // Read static entity presets
        string staticPresetsPath = Path.Combine(Application.streamingAssetsPath, "StaticEntities");
        ReadStaticPresets(staticPresetsPath, staticEntitiesPresetsList);

        IsReady = true;
    }

    /// <summary>
    /// Reads preset files from the specified folder and deserializes them into the provided list.
    /// Handles both dynamic and static entity preset types through generic implementation.
    /// </summary>
    /// <typeparam name="T">The type of preset to deserialize (DynamicEntityPreset or StaticEntityPreset)</typeparam>
    /// <param name="folderPath">Path to the folder containing JSON preset files</param>
    /// <param name="presetList">List to populate with the deserialized presets</param>
    private void ReadDynamicPresets<T>(string folderPath, List<T> presetList) where T : class
    {
        string filePath = Path.Combine(folderPath, "entity_properties.json");

        if (!File.Exists(filePath))
        {
            Debug.LogError($"[GroupPresetsManager] entity_properties.json not found in {folderPath}");
            return;
        }
        
        Debug.Log($"[GroupPresetsManager] Reading dynamic presets from {filePath}");
        
        try
        {
            string jsonContent = File.ReadAllText(filePath);
            // Wrap the JSON content in a container object
            string wrappedJson = $"{{ \"presets\": {jsonContent} }}";
            
            // Deserialize the wrapped JSON into a temporary wrapper class
            var wrapper = JsonUtility.FromJson<PresetWrapper<T>>(wrappedJson);
            
            if (wrapper != null && wrapper.presets != null)
            {
                foreach (var preset in wrapper.presets)
                {
                    if (preset is DynamicEntityPreset dynamicPreset)
                    {
                        // Validate dynamic preset
                        if (dynamicPreset.habitats == null || dynamicPreset.habitats.Length == 0)
                        {
                            Debug.LogError($"[GroupPresetsManager] Invalid preset in {Path.GetFileName(filePath)}: {dynamicPreset.name} has no habitats defined");
                            continue;
                        }
                        Debug.Log($"[GroupPresetsManager] Loaded dynamic preset: {dynamicPreset.name} with {dynamicPreset.habitats.Length} habitats: {string.Join(", ", dynamicPreset.habitats)}");
                    }
                    presetList.Add(preset);
                }
            }
            else
            {
                Debug.LogError($"[GroupPresetsManager] Failed to parse presets from JSON file: {filePath}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[GroupPresetsManager] Error reading preset file {filePath}: {e.Message}\n{e.StackTrace}");
        }

        Debug.Log($"[GroupPresetsManager] Successfully loaded {presetList.Count} presets from {filePath}");
    }

    /// <summary>
    /// Reads and validates static entity presets, ensuring they have required ECS components
    /// </summary>
    private void ReadStaticPresets<T>(string folderPath, List<T> presetList) where T : class
    {
        string filePath = Path.Combine(folderPath, "entity_properties.json");

        if (!File.Exists(filePath))
        {
            Debug.LogError($"[GroupPresetsManager] entity_properties.json not found in {folderPath}");
            return;
        }
        
        Debug.Log($"[GroupPresetsManager] Reading static presets from {filePath}");
        
        try
        {
            string jsonContent = File.ReadAllText(filePath);
            // Wrap the JSON content in a container object
            string wrappedJson = $"{{ \"presets\": {jsonContent} }}";
            
            // Deserialize the wrapped JSON into a temporary wrapper class
            var wrapper = JsonUtility.FromJson<PresetWrapper<T>>(wrappedJson);
            
            if (wrapper != null && wrapper.presets != null)
            {
                foreach (var preset in wrapper.presets)
                {
                    if (preset is StaticEntityPreset staticPreset)
                    {
                        // Validate static preset
                        if (staticPreset.habitats == null || staticPreset.habitats.Length == 0)
                        {
                            Debug.LogError($"[GroupPresetsManager] Invalid preset in {Path.GetFileName(filePath)}: {staticPreset.name} has no habitats defined");
                            continue;
                        }
                        Debug.Log($"[GroupPresetsManager] Loaded static preset: {staticPreset.name} with {staticPreset.habitats.Length} habitats: {string.Join(", ", staticPreset.habitats)}");
                    }
                    presetList.Add(preset);
                }
            }
            else
            {
                Debug.LogError($"[GroupPresetsManager] Failed to parse presets from JSON file: {filePath}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[GroupPresetsManager] Error reading preset file {filePath}: {e.Message}\n{e.StackTrace}");
        }

        Debug.Log($"[GroupPresetsManager] Successfully loaded {presetList.Count} presets from {filePath}");
    }

    private Texture2D LoadTextureFromPath(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var texture = new Texture2D(2, 2);
        texture.LoadImage(bytes);
        return texture;
    }


    public DynamicEntityPreset GetDynamicPresetByName(string name)
    {
        return dynamicEntitiesPresetsList.FirstOrDefault(p => p.name == name);
    }

    /// <summary>
    /// Retrieves a static entity preset by its name.
    /// </summary>
    /// <param name="name">Name of the preset to find</param>
    /// <returns>The found preset, or null if not found</returns>
    public StaticEntityPreset GetStaticPresetByName(string name)
    {
        return staticEntitiesPresetsList.FirstOrDefault(p => p.name == name);
    }
    
    /// <summary>
    /// Wrapper class used for deserializing JSON arrays of presets
    /// </summary>
    [Serializable]
    private class PresetWrapper<T>
    {
        public T[] presets;
    }

    private void ValidatePresetAssets(StaticEntityPreset preset)
    {
        if (!ValidateRequiredAssets(preset))
        {
            Debug.LogError($"[GroupPresetsManager] Failed to validate required assets for preset: {preset.name}");
        }
        else
        {
            Debug.Log($"[GroupPresetsManager] Successfully validated assets for preset: {preset.name}");
        }
    }

    private bool ValidateRequiredAssets(StaticEntityPreset preset)
    {
        string presetPath = Path.Combine(Application.streamingAssetsPath, "StaticEntities", preset.name);
        string model1Path = Path.Combine(presetPath, "model1.glb");

        Debug.Log($"[GroupPresetsManager] Checking paths for {preset.name}:");
        Debug.Log($"  PresetPath: {presetPath}");
        Debug.Log($"  Model1Path: {model1Path}");
        
        // Create the directory if it doesn't exist
        if (!Directory.Exists(presetPath))
        {
            Debug.LogWarning($"[GroupPresetsManager] Creating preset directory for: {preset.name}");
            try
            {
                Directory.CreateDirectory(presetPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GroupPresetsManager] Failed to create directory for preset: {preset.name}. Error: {e.Message}");
                return false;
            }
        }
        
        // Log the directory contents if it exists
        if (Directory.Exists(presetPath))
        {
            try
            {
                string[] files = Directory.GetFiles(presetPath);
                Debug.Log($"  Files in preset folder: {string.Join(", ", files)}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[GroupPresetsManager] Error listing files in preset directory: {e.Message}");
            }
        }
        
        // Check if the model file exists
        if (!File.Exists(model1Path))
        {
            Debug.LogWarning($"[GroupPresetsManager] Missing required model1.glb for preset: {preset.name}. The model will need to be added manually.");
            // We'll return true anyway and let the StaticEntitiesGroup handle the fallback
            return true;
        }

        // We only check if files exist, but don't load them
        return true;
    }
}
}

