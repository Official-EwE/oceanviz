using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using OceanViz3;
using System.Linq;

/// <summary>
/// StateMatcher reads a requested simulation state from a JSON file and synchronizes the current simulation state to match it using SimulationAPI calls.
/// </summary>
/// <remarks>
/// The JSON follows this schema:
/// <code>
/// {
///     "location": {
///         "label": "Testing",         // Display name
///         "locationId": "Testing"     // Location identifier
///     },
///     "views": [                      // Array of views - length determines split screen count
///         {
///             "label": "Screen 1",
///             "dynamic_entities": [
///                 {
///                     "name": "Sea Bass",
///                     "population": 300
///                 }
///             ],
///             "static_entities": [
///                 {
///                     "name": "Cystoseira",
///                     "density": 0.5
///                 }
///             ],
///             "environment": {
///                 "turbidity": 0.5
///             }
///         }
///     ]
/// }
/// </code>
/// </remarks>

public class StateMatcher : MonoBehaviour
{
    private SimulationAPI simulationAPI;
    private MainScene mainScene;
    
    private void Start()
    {
        simulationAPI = FindObjectOfType<SimulationAPI>();
        if (simulationAPI == null)
        {
            Debug.LogError("[StateMatcher] SimulationAPI not found in scene");
            return;
        }

        mainScene = FindObjectOfType<MainScene>();
        if (mainScene == null)
        {
            Debug.LogError("[StateMatcher] MainScene not found in scene");
            return;
        }
        
        //StartCoroutine(TestStateMatcherAfterInit1());
        //StartCoroutine(TestStateMatcherAfterInit2());
    }

    private IEnumerator TestStateMatcherAfterInit1()
    {
        Debug.Log("[StateMatcher] Starting TestStateMatcherAfterInit1 coroutine");
        
        // Wait until SimulationAPI is ready
        while (!MainScene.IsReady)
        {
            Debug.Log("[StateMatcher] Waiting for MainScene.IsReady - current value: " + MainScene.IsReady);
            yield return null;
        }

        // Get the path to schema_session.json
        string schemaPath = Path.Combine(Application.streamingAssetsPath, "state_matcher_test1.json");

        Debug.Log("[StateMatcher] Testing with schema at: " + schemaPath);
        ApplyRequestedStateFromFile(schemaPath);
        Debug.Log("[StateMatcher] Completed TestStateMatcherAfterInit1 coroutine");
    }

    private IEnumerator TestStateMatcherAfterInit2()
    {
        // Wait until SimulationAPI is ready
        while (!MainScene.IsReady)
        {
            yield return null;
        }

        // Wait additional 10 seconds
        yield return new WaitForSeconds(10f);

        string schemaPath = Path.Combine(Application.streamingAssetsPath, "state_matcher_test2.json");

        Debug.Log("[StateMatcher] Testing with schema at: " + schemaPath);
        ApplyRequestedStateFromFile(schemaPath);
    }

    /// <summary>
    /// Applies a requested state from a JSON file at the specified path.
    /// </summary>
    /// <param name="filePath">Path to the JSON file containing the state configuration.</param>
    public void ApplyRequestedStateFromFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            string jsonContent = File.ReadAllText(filePath);
            ApplyRequestedState(jsonContent);
        }
        else
        {
            Debug.LogError("[StateMatcher] State file not found at path: " + filePath);
        }
    }

    /// <summary>
    /// Applies a requested state from a JSON string.
    /// </summary>
    /// <param name="jsonState">JSON string containing the state configuration.</param>
    public void ApplyRequestedState(string jsonState)
    {
        Debug.Log("[StateMatcher] JSON Content: " + jsonState);
      
        // Parse the JSON state
        SessionState requestedState = JsonUtility.FromJson<SessionState>(jsonState);
        
        // Get split screen count from views array length
        int requestedViewCount = requestedState.views?.Length ?? 0;
        Debug.Log($"[StateMatcher] Applying state - Split Screen: {requestedViewCount}, Location: {requestedState.location.locationId}");
        
        // Validate state data
        if (requestedState == null || requestedState.location == null)
        {
            Debug.LogError("[StateMatcher] Failed to parse state JSON");
            return;
        }

        if (string.IsNullOrEmpty(requestedState.location.locationId))
        {
            Debug.LogError("[StateMatcher] Location ID is missing in state");
            return;
        }

        if (requestedViewCount <= 0)
        {
            Debug.LogError("[StateMatcher] Invalid view count in state");
            return;
        }

        if (simulationAPI == null || mainScene == null)
        {
            Debug.LogError("[StateMatcher] Required components are not initialized");
            return;
        }
        
        // Compare and update location state
        if (mainScene.currentLocationName != requestedState.location.locationId)
        {
            Debug.Log($"[StateMatcher] Location change detected. Removing all static entity groups before switching from {mainScene.currentLocationName} to {requestedState.location.locationId}");
            
            // Remove all static entity groups before location switch
            foreach (var group in mainScene.staticEntitiesGroups.ToList())
            {
                Debug.Log($"[StateMatcher] Removing static entity group {group.presetName} before location switch");
                simulationAPI.RemoveStaticEntityGroup(group.presetName);
            }
            
            Debug.Log($"[StateMatcher] Switching location to {requestedState.location.locationId}");
            simulationAPI.SwitchLocation(requestedState.location.locationId);
        }
        
        // Compare and update split screen state
        if (mainScene.views.Count != requestedViewCount)
        {
            Debug.Log($"[StateMatcher] Updating split screen from {mainScene.views.Count} to {requestedViewCount}");
            simulationAPI.SetViewCount(requestedViewCount);
        }

        // Handle views state
        if (requestedState.views != null)
        {
            // Collect all unique entities in the new state
            HashSet<string> newDynamicEntities = new HashSet<string>();
            HashSet<string> newStaticEntities = new HashSet<string>();
            Dictionary<string, int> maxDynamicEntityPopulations = new Dictionary<string, int>();

            foreach (var view in requestedState.views)
            {
                if (view.dynamic_entities != null)
                {
                    foreach (var entity in view.dynamic_entities)
                    {
                        newDynamicEntities.Add(entity.name);
                        if (!maxDynamicEntityPopulations.ContainsKey(entity.name))
                        {
                            maxDynamicEntityPopulations[entity.name] = entity.population;
                        }
                        else
                        {
                            maxDynamicEntityPopulations[entity.name] = Math.Max(maxDynamicEntityPopulations[entity.name], entity.population);
                        }
                    }
                }

                if (view.static_entities != null)
                {
                    foreach (var entity in view.static_entities)
                    {
                        newStaticEntities.Add(entity.name);
                    }
                }
            }

            // Remove dynamic entities that aren't in the new state
            foreach (var group in mainScene.dynamicEntitiesGroups.ToList())
            {
                if (!newDynamicEntities.Contains(group.name))
                {
                    Debug.Log($"[StateMatcher] Removing dynamic entity group {group.name} as it's not in new state");
                    simulationAPI.RemoveDynamicEntityGroup(group.name);
                }
            }

            // Remove static entities that aren't in the new state
            foreach (var group in mainScene.staticEntitiesGroups.ToList())
            {
                if (!newStaticEntities.Contains(group.presetName))
                {
                    Debug.Log($"[StateMatcher] Removing static entity group {group.presetName} as it's not in new state");
                    simulationAPI.RemoveStaticEntityGroup(group.presetName);
                }
            }

            // Update or add dynamic entities from the new state
            foreach (var entityEntry in maxDynamicEntityPopulations)
            {
                string entityName = entityEntry.Key;
                int maxPopulation = entityEntry.Value;

                var existingGroup = mainScene.dynamicEntitiesGroups.Find(g => g.name == entityName);
                if (existingGroup == null)
                {
                    // Spawn new dynamic entity
                    Debug.Log($"[StateMatcher] Spawning new dynamic entity {entityName}");
                    simulationAPI.SpawnDynamicPreset(entityName);
                }
                
                Debug.Log($"[StateMatcher] Setting population for dynamic entity {entityName} to {maxPopulation}");
                simulationAPI.SetDynamicEntityGroupPopulation(entityName, maxPopulation);
            }

            // Handle static entities with the new method
            HandleStaticEntities(requestedState.views);

            // Update view-specific settings
            for (int viewIndex = 0; viewIndex < requestedState.views.Length; viewIndex++)
            {
                var view = requestedState.views[viewIndex];
                
                // Update dynamic entity visibility per view
                if (view.dynamic_entities != null)
                {
                    foreach (var entityName in newDynamicEntities)
                    {
                        var entityInView = view.dynamic_entities.FirstOrDefault(e => e.name == entityName);
                        int percentage = 0;
                        
                        if (entityInView != null)
                        {
                            percentage = (int)((float)entityInView.population / maxDynamicEntityPopulations[entityName] * 100);
                        }
                        
                        Debug.Log($"[StateMatcher] Setting visibility for dynamic entity {entityName} in view {viewIndex} to {percentage}%");
                        simulationAPI.SetDynamicEntityViewVisibilityPercentage(entityName, viewIndex, percentage);
                    }
                }

                // Handle environment settings
                if (view.environment != null)
                {
                    Debug.Log($"[StateMatcher] Setting turbidity for view {viewIndex} to {view.environment.turbidity}");
                    simulationAPI.SetTurbidityForView(viewIndex, view.environment.turbidity);
                }
            }
        }
    }

    /// <summary>
    /// Handles the creation, update, and removal of static entities based on the requested views configuration.
    /// </summary>
    /// <param name="views">Array of view configurations containing static entity settings.</param>
    private void HandleStaticEntities(StateView[] views)
    {
        // First collect all static entities and their densities per view
        Dictionary<string, Dictionary<float, HashSet<int>>> entityDensityViewGroups = new Dictionary<string, Dictionary<float, HashSet<int>>>();

        // Group views by density for each entity
        for (int viewIndex = 0; viewIndex < views.Length; viewIndex++)
        {
            var view = views[viewIndex];
            if (view.static_entities == null) continue;

            foreach (var entity in view.static_entities)
            {
                if (!entityDensityViewGroups.ContainsKey(entity.name))
                {
                    entityDensityViewGroups[entity.name] = new Dictionary<float, HashSet<int>>();
                }
                
                if (!entityDensityViewGroups[entity.name].ContainsKey(entity.density))
                {
                    entityDensityViewGroups[entity.name][entity.density] = new HashSet<int>();
                }
                entityDensityViewGroups[entity.name][entity.density].Add(viewIndex);
            }
        }

        // Calculate all group names that should exist in the new state
        HashSet<string> validGroupNames = new HashSet<string>();
        foreach (var entityEntry in entityDensityViewGroups)
        {
            string entityName = entityEntry.Key;
            var densityViewGroups = entityEntry.Value;

            foreach (var densityGroup in densityViewGroups)
            {
                float density = densityGroup.Key;
                string formattedDensity = density.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                string groupName = $"{entityName}_{formattedDensity}";
                validGroupNames.Add(groupName);
            }
        }

        Debug.Log("[StateMatcher] Current static entity groups:");
        foreach (var group in mainScene.staticEntitiesGroups)
        {
            Debug.Log($"[StateMatcher] - {group.groupName} (preset: {group.presetName}, density: {group.densityFactor:0.00})");
        }

        // First remove all groups that aren't needed in the new state
        foreach (var group in mainScene.staticEntitiesGroups.ToList())
        {
            bool isValid = validGroupNames.Contains(group.groupName);
            Debug.Log($"[StateMatcher] Checking group {group.groupName} - Valid: {isValid}");
            if (!isValid)
            {
                Debug.Log($"[StateMatcher] Removing static entity group {group.groupName} as it's no longer needed");
                simulationAPI.RemoveStaticEntityGroup(group.groupName);
            }
        }

        // Create or update groups for the new state
        foreach (var entityEntry in entityDensityViewGroups)
        {
            string entityName = entityEntry.Key;
            var densityViewGroups = entityEntry.Value;

            foreach (var densityGroup in densityViewGroups)
            {
                float density = densityGroup.Key;
                HashSet<int> viewIndices = densityGroup.Value;
                string formattedDensity = density.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                string groupName = $"{entityName}_{formattedDensity}";

                Debug.Log($"[StateMatcher] Processing group {groupName} for views: {string.Join(", ", viewIndices)}");

                // Try to find existing group with exact name match
                var existingGroup = mainScene.staticEntitiesGroups
                    .FirstOrDefault(g => string.Equals(g.groupName, groupName, StringComparison.OrdinalIgnoreCase));

                if (existingGroup != null)
                {
                    Debug.Log($"[StateMatcher] Updating existing static entity group {groupName}");
                    simulationAPI.SetStaticEntityGroupDensity(groupName, density);
                }
                else
                {
                    Debug.Log($"[StateMatcher] Creating new static entity group {groupName}");
                    simulationAPI.SpawnStaticPreset(entityName, groupName);
                    simulationAPI.SetStaticEntityGroupDensity(groupName, density);
                }

                // Update visibility for all views
                for (int i = 0; i < views.Length; i++)
                {
                    simulationAPI.SetStaticEntityGroupViewVisibility(groupName, i, viewIndices.Contains(i));
                }
            }
        }

        Debug.Log("[StateMatcher] Valid group names for new state:");
        foreach (var name in validGroupNames)
        {
            Debug.Log($"[StateMatcher] - {name}");
        }
    }
    
    /// <summary>
    /// Represents the requested simulation state from a JSON file.
    /// </summary>
    [System.Serializable]
    public class SessionState
    {
        [SerializeField]
        public Location location;

        [SerializeField]
        public StateView[] views;
    }

    [System.Serializable]
    public class Location
    {
        [SerializeField]
        public string label;
        
        [SerializeField]
        public string locationId;
    }

    [System.Serializable]
    public class StateView
    {
        [SerializeField]
        public string label;
        
        [SerializeField]
        public DynamicEntityPreset[] dynamic_entities;
        
        [SerializeField]
        public StaticEntityPreset[] static_entities;
        
        [SerializeField]
        public Environment environment;
    }

    [System.Serializable]
    public class DynamicEntityPreset
    {
        [SerializeField]
        public string name;
        
        [SerializeField]
        public int population;
    }

    [System.Serializable]
    public class StaticEntityPreset
    {
        [SerializeField]
        public string name;
        
        [SerializeField]
        public float density;
    }

    [System.Serializable]
    public class Environment
    {
        [SerializeField]
        public float turbidity;
    }
}
