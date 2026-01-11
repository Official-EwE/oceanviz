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
    /// <summary>
    /// Tracks which IAPICallsExecutor instances have already executed to prevent duplicate setup calls.
    /// An executor instance is expected to run once per scene load; when a location is unloaded and
    /// loaded again, new executor instances will run as they will have different references.
    /// </summary>
    private readonly HashSet<IAPICallsExecutor> executedExecutors = new HashSet<IAPICallsExecutor>();
    
    [SerializeField] private int apiCallIntervalFrames = 1; // Delay between API calls in frames
    private int framesSinceLastApiCall = 0;
    [SerializeField] private MonoBehaviour simulationSetupComponent; // Should implement IAPICallsExecutor

    public void Setup(MainScene mainScene)
    {
        this.mainScene = mainScene;

        // Run any setup executors that are already loaded (e.g., on the main scene or first location).
        // New executors discovered later (for newly loaded locations) are triggered from OnLocationReady.
        RunPendingExecutors();
        // StartCoroutine(RunTestFunctions2AfterDelay());
    }

    /// <summary>
    /// Returns the MainScene associated with this SimulationAPI.
    /// </summary>
    public MainScene GetMainScene()
    {
        Debug.Assert(mainScene != null, "SimulationAPI mainScene is null. Setup must be called before accessing it.");
        return mainScene;
    }

    private System.Collections.IEnumerator RunTestFunctions2AfterDelay()
    {
        yield return new WaitForSeconds(5f);
        RunDelayedTestCalls();
    }
    
    /// <summary>
    /// Discovers and executes any IAPICallsExecutor components that have not yet run.
    /// This is called once from Setup (for already loaded scenes) and again whenever a new
    /// location becomes ready (from SimulationModeManager.OnLocationReady), ensuring that
    /// per-location setup components execute exactly once per load.
    /// </summary>
    public void RunPendingExecutors()
    {
        Debug.Assert(mainScene != null, "[SimulationAPI] mainScene is null in RunPendingExecutors. Setup must be called before running executors.");

        List<IAPICallsExecutor> executorsToRun = new List<IAPICallsExecutor>();

        // Prefer explicit reference first
        if (simulationSetupComponent != null)
        {
            IAPICallsExecutor explicitExecutor = simulationSetupComponent as IAPICallsExecutor;
            Debug.Assert(explicitExecutor != null, "Assigned Simulation Setup Component does not implement IAPICallsExecutor.");
            if (explicitExecutor != null)
            {
                bool alreadyExecuted = executedExecutors.Contains(explicitExecutor);
                if (!alreadyExecuted)
                {
                    executorsToRun.Add(explicitExecutor);
                }
            }
        }

        // Then scan all behaviours in loaded scenes
        MonoBehaviour[] allBehaviours = FindObjectsOfType<MonoBehaviour>();
        if (allBehaviours != null)
        {
            for (int i = 0; i < allBehaviours.Length; i++)
            {
                MonoBehaviour behaviour = allBehaviours[i];
                if (behaviour == null)
                {
                    continue;
                }
                IAPICallsExecutor candidate = behaviour as IAPICallsExecutor;
                if (candidate == null)
                {
                    continue;
                }

                bool alreadyExecuted = executedExecutors.Contains(candidate);
                if (alreadyExecuted)
                {
                    continue;
                }

                bool alreadyQueued = executorsToRun.Contains(candidate);
                if (!alreadyQueued)
                {
                    executorsToRun.Add(candidate);
                }
            }
        }

        if (executorsToRun.Count == 0)
        {
            if (executedExecutors.Count == 0)
            {
                Debug.Log("[SimulationAPI] No simulation setup executors found; skipping scripted setup.");
            }
            return;
        }

        for (int i = 0; i < executorsToRun.Count; i++)
        {
            IAPICallsExecutor executor = executorsToRun[i];
            if (executor == null)
            {
                continue;
            }

            try
            {
                executor.Execute(this);
                executedExecutors.Add(executor);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[SimulationAPI] Error while executing IAPICallsExecutor: " + e);
            }
        }

        // // Switch location
        // SetLocation("Testing");
                
        // // Set view count
        // SetViewCount(2);
        
        // // Spawn dynamic preset
        // SpawnEntityGroup("Sea Bass", "Sea Bass 1");
        // SetEntityGroupPopulation("Sea Bass 1", 0.1f);
        // SetEntityGroupViewVisibility("Sea Bass 1", 0, 0.25f);
        // SetEntityGroupViewVisibility("Sea Bass 1", 1, 0.75f);
        
        // // // Spawn static preset
        // SpawnEntityGroup("Cystoseira", "Cystoseira 1");
        // SetEntityGroupPopulation("Cystoseira 1", 0.1f);
        // SetEntityGroupViewVisibility("Cystoseira 1", 0, 0.25f);
        // SetEntityGroupViewVisibility("Cystoseira 1", 1, 0.75f);

        // // Set turbidity for views
        // SetTurbidityForView(0, 0.33f);
        // SetTurbidityForView(1, 0.66f);
        
        
        
        // Western Med shot 1
        // SpawnEntityGroup("Sea Bass", "Sea Bass 1");
        // SetEntityGroupPopulation("Sea Bass 1", 0.3f);
        
        // SpawnEntityGroup("Mediterranean Parrotfish", "Mediterranean Parrotfish 1");
        // SetEntityGroupPopulation("Mediterranean Parrotfish 1", 0.2f);

        // SpawnEntityGroup("Posidonia Oceanica", "Posidonia Oceanica 1");
        // SetEntityGroupPopulation("Posidonia Oceanica 1", 0.2f);
        
        // SpawnEntityGroup("Fishing Net", "Fishing Net 1");
        // SetEntityGroupPopulation("Fishing Net 1", 0.6f);

        // SpawnEntityGroup("Rusted Wheel Rim", "Rusted Wheel Rim 1");
        // SetEntityGroupPopulation("Rusted Wheel Rim 1", 1.0f);
        
        // SpawnEntityGroup("Old Tire", "Old Tire 1");
        // SetEntityGroupPopulation("Old Tire 1", 1.0f);
        
        // SetTurbidityForView(0, -0.43f);
        
        
        // Western Med shot 3
        // SpawnEntityGroup("Sea Bass", "Sea Bass 1");
        // SetEntityGroupPopulation("Sea Bass 1", 0.1f);
        
        // SpawnEntityGroup("Mediterranean Parrotfish", "Mediterranean Parrotfish 1");
        // SetEntityGroupPopulation("Mediterranean Parrotfish 1", 0.2f);

        // SpawnEntityGroup("Posidonia Oceanica", "Posidonia Oceanica 1");
        // SetEntityGroupPopulation("Posidonia Oceanica 1", 0.1f);
        
        // SetTurbidityForView(0, 0.23f);
        
        
        
        // // Messinian Salinity Crisis shot 1
        // SpawnEntityGroup("Myctophids", "Myctophids 1");
        
        // SpawnEntityGroup("Sea Bass", "Sea Bass 1");
        
        // SpawnEntityGroup("Megalodon", "Megalodon 1");
        // SetEntityGroupPopulation("Megalodon 1", 0.1f);
        
        // SpawnEntityGroup("Posidonia Oceanica", "Posidonia Oceanica 1");
        
        // SpawnEntityGroup("Dungon", "Dungon 1");
        // SetEntityGroupPopulation("Dungon 1", 0.1f);
        // SpawnEntityGroupInHabitats("European Anchovy", "European Anchovy 1", "Dungon");
        
        // SpawnEntityGroup("Porites", "Porites 1");
        // SetEntityGroupPopulation("Porites 1", 0.5f);
        
        
        
        // // Messinian Salinity Crisis shot 3
        // SetLocation("MessinianSalinityCrisis");
        // SpawnEntityGroup("Gorgonian Coral Dead", "Gorgonian Coral Dead");
        // SetEntityGroupPopulation("Gorgonian Coral Dead", 0.01f);
        // SpawnEntityGroup("Red Coral Dead", "Red Coral Dead");
        
        
        
        // // Messinian Salinity Crisis shot 4b
        // SpawnEntityGroup("Great White Shark", "Great White Shark 1");
        // SetEntityGroupPopulation("Great White Shark 1", 0.5f);
        
        // SpawnEntityGroup("Sea Bass", "Sea Bass 1");
        
        // SpawnEntityGroup("Posidonia Oceanica", "Posidonia Oceanica 1");
        // SetEntityGroupPopulation("Posidonia Oceanica 1", 0.1f);
        
        // SpawnEntityGroup("Red Coral", "Red Coral 1");
        // SetEntityGroupPopulation("Red Coral 1", 0.12f);
        
        // SetTurbidityForView(0, 0.5f);
    }

    private void RunDelayedTestCalls()
    {
        // Switch location
        SetLocation("Testing");
        
        // Remove dynamic preset
        RemoveDynamicEntityGroup("Sea Bass 1");
        RemoveStaticEntitiesGroup("Cystoseira 1");
    }

    private bool AreAllStaticGroupsReady()
    {
        if (mainScene == null || mainScene.simulationModeManager == null || mainScene.simulationModeManager.staticEntitiesGroups == null)
            return false;  // Changed from true to false since system isn't ready
        
        return mainScene.simulationModeManager.staticEntitiesGroups.All(group => group != null && group.IsReady);
    }

    private bool AreAllDynamicGroupsReady()
    {
        if (mainScene == null || mainScene.simulationModeManager == null || mainScene.simulationModeManager.dynamicEntitiesGroups == null)
            return false;  // Changed from true to false since system isn't ready
        
        return mainScene.simulationModeManager.dynamicEntitiesGroups.All(group => group != null && group.IsReady);
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
    public void SetLocation(string locationName)
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
    /// <param name="turbidity">Turbidity value between -1 and 1</param>
    public void SetTurbidityForView(int viewIndex, float turbidity)
    {
        Debug.Log($"[SimulationAPI] API call queued: Set turbidity for view {viewIndex} to: {turbidity}");
        float clamped = Mathf.Clamp(turbidity, -1f, 1f);
        EnqueueApiCall(() => {
            if (mainScene.currentLocationScript != null)
            {
                mainScene.currentLocationScript.SetTurbidityForView(viewIndex, clamped);
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
        Debug.LogWarning($"[SimulationAPI] SetDynamicEntityGroupPopulation is deprecated. Use SetEntityGroupPopulation with percentage instead.");
        // Forwarder: estimate percentage by dividing by max if available
        EnqueueApiCall(() => {
            var group = mainScene.simulationModeManager.dynamicEntitiesGroups.Find(g => g.name == groupName);
            if (group != null)
            {
                float denom;
                if (group.dynamicEntityPreset != null && group.dynamicEntityPreset.maxPopulation > 0)
                {
                    denom = group.dynamicEntityPreset.maxPopulation;
                }
                else
                {
                    denom = 1f;
                }
                float percent = Mathf.Clamp01(population / denom);
                int maxForGroup = population;
                if (group.dynamicEntityPreset != null)
                {
                    maxForGroup = group.dynamicEntityPreset.maxPopulation;
                }
                int target = Mathf.RoundToInt(percent * maxForGroup);
                group.SetPopulationAndUpdateGUIState(target);
            }
            else
            {
                Debug.LogError($"[SimulationAPI] Dynamic entity group '{groupName}' not found.");
            }
        });
    }

    /// <summary>
    /// Sets the visibility percentage for a dynamic entity group in a specific view.
    /// Deprecated: use SetEntityGroupViewVisibility.
    /// </summary>
    public void SetDynamicEntityViewVisibility(string groupName, int viewIndex, float percentage)
    {
        Debug.LogWarning("[SimulationAPI] SetDynamicEntityViewVisibility is deprecated. Use SetEntityGroupViewVisibility.");
        SetEntityGroupViewVisibility(groupName, viewIndex, percentage);
    }

    /// <summary>
    /// Removes a dynamic entity group from the simulation.
    /// </summary>
    /// <param name="name">Name of the group to remove</param>
    public void RemoveDynamicEntityGroup(string name)
    {
        Debug.Log($"[SimulationAPI] API call queued: Remove dynamic entity group: {name}");
        EnqueueApiCall(() => {
            var group = mainScene.simulationModeManager.dynamicEntitiesGroups.Find(g => g.name == name);
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
    /// Spawns an entity group. Checks both dynamic and static presets by name and spawns accordingly.
    /// - For dynamic presets: spawns from preset name; if groupName is provided, it becomes the session display name
    /// - For static presets: spawns from preset name; groupName is used as the session display name (falls back to preset name)
    /// </summary>
    /// <param name="presetName">Preset to spawn</param>
    /// <param name="groupName">Optional group name (session display name)</param>
    public void SpawnEntityGroup(string presetName, string groupName = null)
    {
        string logSuffix = string.Empty;
        if (!string.IsNullOrEmpty(groupName))
        {
            logSuffix = " as '" + groupName + "'";
        }
        Debug.Log("[SimulationAPI] API call queued: Spawn entity group preset: " + presetName + logSuffix);
        EnqueueApiCall(() => {
            var dyn = GroupPresetsManager.Instance.GetDynamicPresetByName(presetName);
            if (dyn != null)
            {
                if (!string.IsNullOrEmpty(groupName))
                {
                    mainScene.SpawnDynamicPreset(presetName, groupName);
                }
                else
                {
                    mainScene.SpawnDynamicPreset(presetName);
                }
                return;
            }

            var stat = GroupPresetsManager.Instance.GetStaticPresetByName(presetName);
            if (stat != null)
            {
                string finalName = groupName;
                if (string.IsNullOrEmpty(finalName))
                {
                    finalName = presetName;
                }
                mainScene.SpawnStaticPreset(presetName, finalName);
                return;
            }

            Debug.LogError($"[SimulationAPI] Preset '{presetName}' not found in dynamic or static presets.");
        });
    }

    /// <summary>
    /// Spawns an entity group into the specified habitat names, overriding the preset habitats.
    /// Works for both dynamic and static presets.
    /// </summary>
    /// <param name="presetName">Name of the preset to spawn</param>
    /// <param name="groupName">Session display name for the new group</param>
    /// <param name="habitatNames">One or more habitat/biome names to constrain spawning</param>
    public void SpawnEntityGroupInHabitats(string presetName, string groupName, params string[] habitatNames)
    {
        Debug.Log("[SimulationAPI] API call queued: Spawn entity group preset '" + presetName + "' in habitats: " + (habitatNames == null ? "<null>" : string.Join(", ", habitatNames)));
        EnqueueApiCall(() => {
            // Validate inputs
            bool hasValidHabitats = habitatNames != null && habitatNames.Length > 0;
            if (!hasValidHabitats)
            {
                Debug.LogError("[SimulationAPI] SpawnEntityGroupInHabitats requires at least one habitat name.");
                Debug.Assert(false, "No habitat names provided for SpawnEntityGroupInHabitats.");
                return;
            }

            // Check preset type and dispatch
            var dynamicPreset = GroupPresetsManager.Instance.GetDynamicPresetByName(presetName);
            if (dynamicPreset != null)
            {
                if (string.IsNullOrEmpty(groupName))
                {
                    groupName = presetName;
                }
                mainScene.simulationModeManager.SpawnDynamicPresetInHabitats(presetName, groupName, habitatNames);
                return;
            }

            var staticPreset = GroupPresetsManager.Instance.GetStaticPresetByName(presetName);
            if (staticPreset != null)
            {
                if (string.IsNullOrEmpty(groupName))
                {
                    groupName = presetName;
                }
                mainScene.simulationModeManager.SpawnStaticPresetInHabitats(presetName, groupName, habitatNames);
                return;
            }

            Debug.LogError("[SimulationAPI] Preset '" + presetName + "' not found in dynamic or static presets.");
            Debug.Assert(false, "Preset not found for SpawnEntityGroupInHabitats.");
        });
    }

    /// <summary>
    /// Sets the population of entities in a static entity group.
    /// </summary>
    /// <param name="name">Name of the group to modify</param>
    /// <param name="population">New population value</param>
    public void SetStaticEntitiesGroupPopulation(string name, int population)
    {
        Debug.LogWarning($"[SimulationAPI] SetStaticEntitiesGroupPopulation is deprecated. Use SetEntityGroupPopulation with percentage instead.");
        // Forwarder: estimate percentage by dividing by max if available
        EnqueueApiCall(() => {
            var group = mainScene.simulationModeManager.staticEntitiesGroups.Find(g => g.name == name);
            if (group != null)
            {
                float denom;
                if (group.staticEntityPreset != null && group.staticEntityPreset.maxPopulation > 0)
                {
                    denom = group.staticEntityPreset.maxPopulation;
                }
                else
                {
                    denom = 1f;
                }
                float percent = Mathf.Clamp01(population / denom);
                int maxForGroup = population;
                if (group.staticEntityPreset != null)
                {
                    maxForGroup = group.staticEntityPreset.maxPopulation;
                }
                int target = Mathf.RoundToInt(percent * maxForGroup);
                group.SetPopulationAndUpdateGUIState(target);
            }
            else
            {
                Debug.LogWarning($"[SimulationAPI] Static entity group '{name}' not found.");
            }
        });
    }

    /// <summary>
    /// Sets the population for a group (dynamic or static) by percentage of its maxPopulation.
    /// </summary>
    /// <param name="groupName">Name of the group (display name)</param>
    /// <param name="percentage">0.0 – 1.0 inclusive</param>
    public void SetEntityGroupPopulation(string groupName, float percentage)
    {
        Debug.Log($"[SimulationAPI] API call queued: Set population percentage for group '{groupName}' to: {percentage}");
        float clamped = Mathf.Clamp01(percentage);
        EnqueueApiCall(() => {
            // Try dynamic first
            var dGroup = mainScene.simulationModeManager.dynamicEntitiesGroups.Find(g => g.name == groupName);
            if (dGroup != null)
            {
                int maxPop = 0;
                if (dGroup.dynamicEntityPreset != null)
                {
                    maxPop = dGroup.dynamicEntityPreset.maxPopulation;
                }
                int target = Mathf.RoundToInt(maxPop * clamped);
                dGroup.SetPopulationAndUpdateGUIState(target);
                return;
            }

            // Try static
            var sGroup = mainScene.simulationModeManager.staticEntitiesGroups.Find(g => g.name == groupName);
            if (sGroup != null)
            {
                int maxPop = 0;
                if (sGroup.staticEntityPreset != null)
                {
                    maxPop = sGroup.staticEntityPreset.maxPopulation;
                }
                int target = Mathf.RoundToInt(maxPop * clamped);
                sGroup.SetPopulationAndUpdateGUIState(target);
                return;
            }

            Debug.LogWarning($"[SimulationAPI] Entity group '{groupName}' not found in dynamic or static groups.");
        });
    }
    
    /// <summary>
    /// Sets the visibility of a static entity group in a specific view.
    /// Deprecated: use SetEntityGroupViewVisibility.
    /// </summary>
    public void SetStaticEntitiesGroupViewVisibility(string name, int viewIndex, bool isVisible)
    {
        Debug.LogWarning("[SimulationAPI] SetStaticEntitiesGroupViewVisibility is deprecated. Use SetEntityGroupViewVisibility.");
        SetEntityGroupViewVisibility(name, viewIndex, isVisible ? 1.0f : 0.0f);
    }

    /// <summary>
    /// Sets the visibility for a group (dynamic or static) in a specific view using a fraction (0.0 - 1.0).
    /// </summary>
    public void SetEntityGroupViewVisibility(string groupName, int viewIndex, float percentage)
    {
        Debug.Log("[SimulationAPI] API call queued: Set group '" + groupName + "' view " + viewIndex + " visibility to: " + percentage);
        float clamped = Mathf.Clamp01(percentage);
        int percentInt = Mathf.RoundToInt(clamped * 100f);
        EnqueueApiCall(() => {
            var dGroup = mainScene.simulationModeManager.dynamicEntitiesGroups.Find(g => g.name == groupName);
            if (dGroup != null)
            {
                dGroup.SetViewVisibilityPercentageAndUpdateGUI(viewIndex, percentInt);
                return;
            }
            var sGroup = mainScene.simulationModeManager.staticEntitiesGroups.Find(g => g.name == groupName);
            if (sGroup != null)
            {
                sGroup.SetViewVisibilityPercentageAndUpdateGUI(viewIndex, percentInt);
                return;
            }
            Debug.LogWarning("[SimulationAPI] Entity group '" + groupName + "' not found for view visibility update.");
        });
    }

    /// <summary>
    /// Removes a static entity group from the simulation.
    /// </summary>
    /// <param name="name">Name of the group to remove</param>
    public void RemoveStaticEntitiesGroup(string name)
    {
        Debug.Log($"[SimulationAPI] API call queued: Remove static entity group: {name}");
        EnqueueApiCall(() => {
            var group = mainScene.simulationModeManager.staticEntitiesGroups.Find(g => g.name == name);
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
