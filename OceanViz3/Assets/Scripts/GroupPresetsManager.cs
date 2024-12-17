using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Linq;

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
    
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    /// <summary>
    /// Collection of presets for dynamic entities (e.g., boid groups, moving objects)
    /// Loaded from JSON files in the StreamingAssets/DynamicEntities folder
    /// </summary>
    public List<DynamicEntityPreset> dynamicEntitiesPresetsList;

    /// <summary>
    /// Collection of presets for static entities (e.g., obstacles, terrain features)
    /// Loaded from JSON files in the StreamingAssets/StaticEntities folder
    /// </summary>
    public List<StaticEntityPreset> staticEntitiesPresetsList;
    
    /// <summary>
    /// Indicates whether the manager has finished loading presets and is ready for use
    /// </summary>
    public bool IsReady { get; private set; } = true;

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
        ReadPresets(dynamicPresetsPath, dynamicEntitiesPresetsList);

        // Read static entity presets
        string staticPresetsPath = Path.Combine(Application.streamingAssetsPath, "StaticEntities");
        ReadPresets(staticPresetsPath, staticEntitiesPresetsList);

        IsReady = true;
    }

    /// <summary>
    /// Reads preset files from the specified folder and deserializes them into the provided list.
    /// Handles both dynamic and static entity preset types through generic implementation.
    /// </summary>
    /// <typeparam name="T">The type of preset to deserialize (DynamicEntityPreset or StaticEntityPreset)</typeparam>
    /// <param name="folderPath">Path to the folder containing JSON preset files</param>
    /// <param name="presetList">List to populate with the deserialized presets</param>
    private void ReadPresets<T>(string folderPath, List<T> presetList) where T : class
    {
        if (!Directory.Exists(folderPath))
        {
            Debug.LogError($"Preset folder not found: {folderPath}");
            return;
        }

        string[] presetFiles = Directory.GetFiles(folderPath, "*.json");
        foreach (string file in presetFiles)
        {
            try
            {
                string jsonContent = File.ReadAllText(file);
                // Wrap the JSON content in a container object
                string wrappedJson = $"{{ \"presets\": {jsonContent} }}";
                
                // Deserialize the wrapped JSON into a temporary wrapper class
                var wrapper = JsonUtility.FromJson<PresetWrapper<T>>(wrappedJson);
                
                if (wrapper != null && wrapper.presets != null)
                {
                    presetList.AddRange(wrapper.presets);
                }
                else
                {
                    Debug.LogError($"Failed to parse presets from JSON file: {file}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error reading preset file {file}: {e.Message}");
            }
        }
    }

    public DynamicEntityPreset GetPresetByName(string presetName)
    {
        return dynamicEntitiesPresetsList.FirstOrDefault(preset => preset.name == presetName);
    }

    public StaticEntityPreset GetStaticPresetByName(string presetName)
    {
        return staticEntitiesPresetsList.FirstOrDefault(preset => preset.name == presetName);
    }   
    
    /// <summary>
    /// Wrapper class used for deserializing JSON arrays of presets
    /// </summary>
    [Serializable]
    private class PresetWrapper<T>
    {
        public T[] presets;
    }
}
}
