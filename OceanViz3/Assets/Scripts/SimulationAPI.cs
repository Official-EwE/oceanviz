using UnityEngine;
using System.Collections.Generic;
using OceanViz3;
using System.Linq;

/// <summary>
/// Provides an API interface for controlling the simulation, including entity spawning, 
/// view management, and environmental settings.
/// </summary>
public class SimulationAPI : MonoBehaviour
{
    private MainScene mainScene;
    private Queue<System.Action> apiCallQueue = new Queue<System.Action>();
    
    [SerializeField] private int apiCallIntervalFrames = 1; // Delay between API calls in frames
    private int framesSinceLastApiCall = 0;

    public void Setup(MainScene mainScene)
    {
        this.mainScene = mainScene;

        // RunTestFunctions1();
        // StartCoroutine(RunTestFunctions2AfterDelay());
    }

    private System.Collections.IEnumerator RunTestFunctions2AfterDelay()
    {
        yield return new WaitForSeconds(5f);
        RunTestFunctions2();
    }
    
    private void RunTestFunctions1()
    {
        // Switch location
        SwitchLocation("Testing");
        
        // Spawn dynamic preset
        // SpawnDynamicPreset("Sea Bass");
        // SetDynamicEntityGroupPopulation("Sea Bass", 300); // "Sea Bass" is a name of the preset, not a group name
        // SetViewCount(2);
        // // Population percentage slider per view
        // SetDynamicEntityViewVisibilityPercentage("Sea Bass", 0, 100);
        // SetDynamicEntityViewVisibilityPercentage("Sea Bass", 1, 50);
        
        // // Spawn static preset
        // SpawnStaticPreset("Cystoseira", "Cystoseira 1");
        // SetStaticEntityGroupDensity("Cystoseira 1", 0.1f); // "Cystoseira" is a name of the preset, not a group name
        // SetStaticEntityGroupViewVisibility("Cystoseira 1", 0, true);
        // SetStaticEntityGroupViewVisibility("Cystoseira 1", 1, false);

        // SetTurbidityForView(0, 0.33f);
        // SetTurbidityForView(1, 0.66f);
    }

    private void RunTestFunctions2()
    {
        // Switch location
        SwitchLocation("Testing");
        
        // Remove dynamic preset
        RemoveDynamicEntityGroup("Sea Bass");
        RemoveStaticEntityGroup("Cystoseira");
        
        // Spawn dynamic preset
        SpawnDynamicPreset("Common Dolphin");
        SetDynamicEntityGroupPopulation("Common Dolphin", 3); // "Sea Bass" is a name of the preset, not a group name
        SetViewCount(3);
        // Population percentage slider per view
        SetDynamicEntityViewVisibilityPercentage("Common Dolphin", 0, 100);
        SetDynamicEntityViewVisibilityPercentage("Common Dolphin", 1, 75);
        SetDynamicEntityViewVisibilityPercentage("Common Dolphin", 2, 25);
        
        // Spawn static preset
        SpawnStaticPreset("Posidonia Oceanica", "Posidonia Oceanica 1");
        SetStaticEntityGroupDensity("Posidonia Oceanica 1", 0.1f); // "Cystoseira" is a name of the preset, not a group name
        
        // Set turbidity for views
        SetTurbidityForView(0, 1f);
        SetTurbidityForView(1, 0.5f);
        SetTurbidityForView(2, 0f);
    }

    private bool AreAllStaticGroupsReady()
    {
        if (mainScene.staticEntitiesGroups == null || mainScene.staticEntitiesGroups.Count == 0)
            return true;
        
        return mainScene.staticEntitiesGroups.All(group => group.IsReady);
    }

    private bool AreAllDynamicGroupsReady()
    {
        if (mainScene.dynamicEntitiesGroups == null || mainScene.dynamicEntitiesGroups.Count == 0)
            return true;
        
        return mainScene.dynamicEntitiesGroups.All(group => group.IsReady);
    }

    private void Update()
    {
        if (MainScene.IsReady && 
            LocationScript.IsReady && 
            GroupPresetsManager.Instance.IsReady && 
            AreAllStaticGroupsReady() &&
            AreAllDynamicGroupsReady())
        {
            framesSinceLastApiCall++;

            // Check if enough frames have passed since the last API call
            if (framesSinceLastApiCall >= apiCallIntervalFrames && apiCallQueue.Count > 0)
            {
                System.Action apiCall = apiCallQueue.Dequeue();
                apiCall.Invoke();
                framesSinceLastApiCall = 0; // Reset the frame counter
            }
        }
    }

    private void EnqueueApiCall(System.Action apiCall)
    {
        apiCallQueue.Enqueue(apiCall);
    }
    
    // Scene Management API

    /// <summary>
    /// Switches the current location to the specified location.
    /// </summary>
    /// <param name="locationName">Name of the location to load</param>
    public void SwitchLocation(string locationName)
    {
        Debug.Log($"[SimulationAPI] API call queued: Switch location to: {locationName}");
        EnqueueApiCall(() => {
            mainScene.UnloadLocation();
            mainScene.LoadLocationAndUpdateGUIState(locationName);
        });
    }

    /// <summary>
    /// Sets the number of simultaneous views in the simulation.
    /// </summary>
    /// <param name="viewsCount">Number of views to display. Max is 4</param>
    public void SetViewCount(int viewsCount)
    {
        Debug.Log($"[SimulationAPI] API call queued: Set view count to: {viewsCount}");
        EnqueueApiCall(() => mainScene.SetViewCountAndUpdateGUIState(viewsCount));
    }

    /// <summary>
    /// Sets the turbidity level for a specific view.
    /// </summary>
    /// <param name="viewIndex">Index of the view to modify</param>
    /// <param name="turbidity">Turbidity value between 0 and 1</param>
    public void SetTurbidityForView(int viewIndex, float turbidity)
    {
        Debug.Log($"[SimulationAPI] API call queued: Set turbidity for view {viewIndex} to: {turbidity}");
        EnqueueApiCall(() => {
            if (mainScene.currentLocationScript != null)
            {
                mainScene.currentLocationScript.SetTurbidityForView(viewIndex, turbidity);
            }
            else
            {
                Debug.LogError("[SimulationAPI] Cannot set turbidity - no location is currently loaded");
            }
        });
    }
    
    // Dynamic Entities API

    /// <summary>
    /// Spawns a dynamic entity group using a preset configuration.
    /// </summary>
    /// <param name="name">Name of the preset to spawn</param>
    public void SpawnDynamicPreset(string name)
    {
        Debug.Log($"[SimulationAPI] API call queued: Spawn dynamic preset: {name}");
        EnqueueApiCall(() => {
            mainScene.SpawnDynamicPreset(name);
        });
    }

    /// <summary>
    /// Sets the population size for a dynamic entity group.
    /// </summary>
    /// <param name="groupName">Name of the group to modify</param>
    /// <param name="population">New population size</param>
    public void SetDynamicEntityGroupPopulation(string groupName, int population)
    {
        Debug.Log($"[SimulationAPI] API call queued: Set population for dynamic group '{groupName}' to: {population}");
        EnqueueApiCall(() => {
            var group = mainScene.dynamicEntitiesGroups.Find(g => g.name == groupName);
            if (group != null)
            {
                group.SetPopulationAndUpdateGUIState(population);
            }
            else
            {
                Debug.LogError($"[SimulationAPI] Dynamic entity group '{groupName}' not found.");
            }
        });
    }

    /// <summary>
    /// Sets the visibility percentage for a dynamic entity group in a specific view.
    /// </summary>
    /// <param name="groupName">Name of the group to modify</param>
    /// <param name="viewIndex">Index of the view</param>
    /// <param name="percentage">Visibility percentage (0-100)</param>
    public void SetDynamicEntityViewVisibilityPercentage(string groupName, int viewIndex, int percentage)
    {
        Debug.Log($"[SimulationAPI] API call queued: Set visibility percentage for dynamic group '{groupName}' view {viewIndex} to: {percentage}%");
        EnqueueApiCall(() => {
            var group = mainScene.dynamicEntitiesGroups.Find(g => g.name == groupName);
            if (group != null)
            {
                group.SetViewVisibilityPercentageAndUpdateGUI(viewIndex, percentage);
            }
            else
            {
                Debug.LogError($"[SimulationAPI] Dynamic entity group '{groupName}' not found.");
            }
        });
    }

    /// <summary>
    /// Removes a dynamic entity group from the simulation.
    /// </summary>
    /// <param name="name">Name of the group to remove</param>
    public void RemoveDynamicEntityGroup(string name)
    {
        Debug.Log($"[SimulationAPI] API call queued: Remove dynamic entity group: {name}");
        EnqueueApiCall(() => {
            var group = mainScene.dynamicEntitiesGroups.Find(g => g.name == name);
            if (group != null)
            {
                group.DeleteGroup();
            }
            else
            {
                Debug.LogWarning($"[SimulationAPI] Dynamic entity group '{name}' not found for removal.");
            }
        });
    }
    
    // Static Entities API

    /// <summary>
    /// Spawns a static entity group using a preset configuration.
    /// </summary>
    /// <param name="presetName">Name of the preset to use</param>
    /// <param name="groupName">Name to assign to the new group</param>
    public void SpawnStaticPreset(string presetName, string groupName)
    {
        Debug.Log($"[SimulationAPI] API call queued: Spawn static preset '{presetName}' with group name: {groupName}");
        EnqueueApiCall(() => {
            mainScene.SpawnStaticPreset(presetName, groupName);
        });
    }

    /// <summary>
    /// Sets the density of entities in a static entity group.
    /// </summary>
    /// <param name="groupName">Name of the group to modify</param>
    /// <param name="density">New density value</param>
    public void SetStaticEntityGroupDensity(string groupName, float density)
    {
        Debug.Log($"[SimulationAPI] API call queued: Set density for static group '{groupName}' to: {density}");
        EnqueueApiCall(() => {
            var group = mainScene.staticEntitiesGroups.Find(g => g.groupName == groupName);
            if (group != null)
            {
                group.SetDensityAndUpdateGUI(density);
            }
            else
            {
                Debug.LogWarning($"[SimulationAPI] Static entity group '{groupName}' not found.");
            }
        });
    }
    
    /// <summary>
    /// Sets the visibility of a static entity group in a specific view.
    /// </summary>
    /// <param name="groupName">Name of the group to modify</param>
    /// <param name="viewIndex">Index of the view</param>
    /// <param name="isVisible">Whether the group should be visible</param>
    public void SetStaticEntityGroupViewVisibility(string groupName, int viewIndex, bool isVisible)
    {
        Debug.Log($"[SimulationAPI] API call queued: Set visibility for static group '{groupName}' view {viewIndex} to: {isVisible}");
        EnqueueApiCall(() => {
            var group = mainScene.staticEntitiesGroups.Find(g => g.groupName == groupName);
            if (group != null)
            {
                group.SetGroupViewVisibilityAndUpdateGUI(viewIndex, isVisible);
            }
        });
    }

    /// <summary>
    /// Removes a static entity group from the simulation.
    /// </summary>
    /// <param name="name">Name of the group to remove</param>
    public void RemoveStaticEntityGroup(string name)
    {
        Debug.Log($"[SimulationAPI] API call queued: Remove static entity group: {name}");
        EnqueueApiCall(() => {
            var group = mainScene.staticEntitiesGroups.Find(g => g.groupName == name);
            if (group != null)
            {
                group.DeleteGroup();
            }
            else
            {
                Debug.LogWarning($"[SimulationAPI] Static entity group '{name}' not found for removal.");
            }
        });
    }
}
