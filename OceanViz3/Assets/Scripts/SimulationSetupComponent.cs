using UnityEngine;
using OceanViz3;
using System.Collections.Generic;

/// <summary>
/// Inspector-driven configuration for initial simulation setup.
/// Allows setting location, spawning entity groups with population and per-view visibility,
/// view count, and per-view turbidity. Executes via SimulationAPI's queuing once per scene/location load
/// when discovered by the SimulationAPI (either at startup or when a new location becomes ready).
/// </summary>
public class SimulationSetupComponent : MonoBehaviour, IAPICallsExecutor
{
    [Header("Override Camera")]
    [Tooltip("Optional override camera that the simulation rig will follow each frame.")]
    public Camera overrideCamera;

    private MainScene cachedMainScene;
    private SimulationModeCameraRig cachedCameraRig;

    [Header("Location")]
    [Tooltip("Optional: name of the location to load at start.")]
    public string locationName = string.Empty;

    [System.Serializable]
    public class VisibilityEntry
    {
        [Tooltip("Target view index (0-3). Max supported views is 4.")]
        public int viewIndex = 0;

        [Range(0f, 1f)]
        [Tooltip("Visibility for the group in the given view (0-1).")]
        public float visibility = 1f;
    }

    [System.Serializable]
    public class GroupConfig
    {
        [Header("Preset and Naming")]
        [Tooltip("Entity group preset name to spawn (dynamic or static). Required.")]
        public string presetName = string.Empty;

        [Tooltip("Session display name for the group. If empty, presetName may be used by the system.")]
        public string groupName = string.Empty;

        [Header("Population and Visibility")]
        [Range(0f, 1f)]
        [Tooltip("Population as a fraction of preset maxPopulation (0-1).")]
        public float population = 0f;

        [Tooltip("Per-view visibility entries for this group.")]
        public VisibilityEntry[] visibilities = new VisibilityEntry[0];

        [Header("Habitat Override")]
        [Tooltip("Optional habitat names to override preset spawning habitats. Leave empty to use preset defaults.")]
        public string[] overrideHabitats = new string[0];
    }

    [Header("Groups")]
    [Tooltip("List of groups to spawn and configure.")]
    public GroupConfig[] groups = new GroupConfig[0];

    [Header("Views")]
    [Range(1, 4)]
    [Tooltip("Number of simultaneous views to display (1-4).")]
    public int viewsCount = 1;

    [System.Serializable]
    public class TurbidityEntry
    {
        [Tooltip("Target view index (0-3). Max supported views is 4.")]
        public int viewIndex = 0;

        [Range(-1f, 1f)]
        [Tooltip("Turbidity value for the given view (-1 to 1).")]
        public float turbidity = 0f;
    }

    [Header("Per-View Turbidity")]
    [Tooltip("Optional turbidity overrides per view.")]
    public TurbidityEntry[] turbidities = new TurbidityEntry[0];

    /// <summary>
    /// Execute configured API calls using the provided SimulationAPI instance.
    /// Called by SimulationAPI.RunPendingExecutors when this component's scene/location is active.
    /// </summary>
    /// <param name="api">SimulationAPI to invoke</param>
    public void Execute(SimulationAPI api)
    {
        Debug.Assert(api != null, "SimulationAPI reference is null in SimulationSetupComponent.Execute");
        if (api == null)
        {
            return;
        }

        CacheMainScene(api);

        // Location
        if (!string.IsNullOrEmpty(locationName))
        {
            api.SetLocation(locationName);
        }

        // Views count
        Debug.Assert(viewsCount >= 1 && viewsCount <= 4, "viewsCount must be in [1,4]");
        if (viewsCount >= 1 && viewsCount <= 4)
        {
            api.SetViewCount(viewsCount);
        }

        // Groups
        if (groups != null)
        {
            for (int i = 0; i < groups.Length; i++)
            {
                GroupConfig group = groups[i];
                if (group == null)
                {
                    continue;
                }

                bool hasPresetName = !string.IsNullOrEmpty(group.presetName);
                Debug.Assert(hasPresetName, "Group presetName is required.");
                if (!hasPresetName)
                {
                    continue;
                }

                string resolvedGroupName = group.groupName;
                if (string.IsNullOrEmpty(resolvedGroupName))
                {
                    resolvedGroupName = group.presetName;
                }
                Debug.Assert(!string.IsNullOrEmpty(resolvedGroupName), "Group name could not be resolved. Provide groupName or ensure preset uses presetName as display name.");

                // Determine if habitat override should be used
                string[] habitatsToUse = null;
                bool hasOverrideHabitats = group.overrideHabitats != null && group.overrideHabitats.Length > 0;
                if (hasOverrideHabitats)
                {
                    List<string> cleanedHabitats = new List<string>();
                    for (int h = 0; h < group.overrideHabitats.Length; h++)
                    {
                        string habitatName = group.overrideHabitats[h];
                        if (string.IsNullOrEmpty(habitatName))
                        {
                            continue;
                        }
                        string trimmedName = habitatName.Trim();
                        if (trimmedName.Length == 0)
                        {
                            continue;
                        }
                        cleanedHabitats.Add(trimmedName);
                    }

                    Debug.Assert(cleanedHabitats.Count > 0, "OverrideHabitat requires at least one non-empty habitat name.");
                    if (cleanedHabitats.Count > 0)
                    {
                        habitatsToUse = cleanedHabitats.ToArray();
                    }
                }

                // Spawn group (optionally constrained to override habitats)
                if (habitatsToUse != null)
                {
                    api.SpawnEntityGroupInHabitats(group.presetName, group.groupName, habitatsToUse);
                }
                else
                {
                    api.SpawnEntityGroup(group.presetName, group.groupName);
                }

                // Population
                bool populationInRange = group.population >= 0f && group.population <= 1f;
                Debug.Assert(populationInRange, "Population must be in [0,1].");
                float clampedPopulation = group.population;
                if (clampedPopulation < 0f)
                {
                    clampedPopulation = 0f;
                }
                if (clampedPopulation > 1f)
                {
                    clampedPopulation = 1f;
                }
                api.SetEntityGroupPopulation(resolvedGroupName, clampedPopulation);

                // Per-view visibility
                if (group.visibilities != null)
                {
                    for (int j = 0; j < group.visibilities.Length; j++)
                    {
                        VisibilityEntry entry = group.visibilities[j];
                        if (entry == null)
                        {
                            continue;
                        }
                        bool viewIndexInRange = entry.viewIndex >= 0 && entry.viewIndex < 4;
                        Debug.Assert(viewIndexInRange, "Visibility viewIndex must be in [0,3]");
                        if (!viewIndexInRange)
                        {
                            continue;
                        }

                        bool visibilityInRange = entry.visibility >= 0f && entry.visibility <= 1f;
                        Debug.Assert(visibilityInRange, "Visibility must be in [0,1].");
                        float clampedVisibility = entry.visibility;
                        if (clampedVisibility < 0f)
                        {
                            clampedVisibility = 0f;
                        }
                        if (clampedVisibility > 1f)
                        {
                            clampedVisibility = 1f;
                        }
                        api.SetEntityGroupViewVisibility(resolvedGroupName, entry.viewIndex, clampedVisibility);
                    }
                }
            }
        }

        // Per-view turbidity
        if (turbidities != null)
        {
            for (int k = 0; k < turbidities.Length; k++)
            {
                TurbidityEntry turb = turbidities[k];
                if (turb == null)
                {
                    continue;
                }
                bool turbIndexInRange = turb.viewIndex >= 0 && turb.viewIndex < 4;
                Debug.Assert(turbIndexInRange, "Turbidity viewIndex must be in [0,3]");
                if (!turbIndexInRange)
                {
                    continue;
                }

                bool turbidityInRange = turb.turbidity >= -1f && turb.turbidity <= 1f;
                Debug.Assert(turbidityInRange, "Turbidity must be in [-1,1].");
                float clampedTurbidity = turb.turbidity;
                if (clampedTurbidity < -1f)
                {
                    clampedTurbidity = -1f;
                }
                if (clampedTurbidity > 1f)
                {
                    clampedTurbidity = 1f;
                }
                api.SetTurbidityForView(turb.viewIndex, clampedTurbidity);
            }
        }
    }

    private void CacheMainScene(SimulationAPI api)
    {
        if (cachedMainScene != null)
        {
            return;
        }

        if (api != null)
        {
            cachedMainScene = api.GetMainScene();
        }

        if (cachedMainScene == null)
        {
            cachedMainScene = FindObjectOfType<MainScene>();
        }
    }

    private void LateUpdate()
    {
        if (overrideCamera == null)
        {
            return;
        }

        if (cachedMainScene == null)
        {
            cachedMainScene = FindObjectOfType<MainScene>();
        }

        if (cachedMainScene == null || cachedMainScene.simulationModeManager == null)
        {
            return;
        }

        GameObject rigObject = cachedMainScene.simulationModeManager.cameraRig;
        if (rigObject == null)
        {
            return;
        }

        if (cachedCameraRig == null || cachedCameraRig.gameObject != rigObject)
        {
            cachedCameraRig = rigObject.GetComponent<SimulationModeCameraRig>();
        }

        if (cachedCameraRig == null)
        {
            return;
        }

        cachedCameraRig.SnapToTransform(overrideCamera.transform);
    }
}


