using System;
using System.Collections.Generic;
using UnityEngine;

namespace OceanViz3
{
    /// <summary>
    /// Applies a SceneSetupFileV1 to the running simulation using SimulationAPI calls.
    /// </summary>
    public static class SceneSetupApplier
    {
        public static void ClearCurrentSetup(SimulationAPI api, SimulationModeManager simulationModeManager)
        {
            Debug.Assert(api != null, "[SceneSetupApplier] ClearCurrentSetup requires SimulationAPI.");
            Debug.Assert(simulationModeManager != null, "[SceneSetupApplier] ClearCurrentSetup requires SimulationModeManager.");

            if (api == null || simulationModeManager == null)
            {
                Debug.Assert(false, "[SceneSetupApplier] ClearCurrentSetup called with null dependencies.");
                return;
            }

            // Remove dynamic groups
            if (simulationModeManager.dynamicEntitiesGroups != null)
            {
                List<DynamicEntitiesGroup> dynCopy = new List<DynamicEntitiesGroup>(simulationModeManager.dynamicEntitiesGroups);
                for (int i = 0; i < dynCopy.Count; i++)
                {
                    DynamicEntitiesGroup g = dynCopy[i];
                    if (g == null)
                    {
                        continue;
                    }
                    api.RemoveDynamicEntityGroup(g.name);
                }
            }

            // Remove static groups
            if (simulationModeManager.staticEntitiesGroups != null)
            {
                List<StaticEntitiesGroup> statCopy = new List<StaticEntitiesGroup>(simulationModeManager.staticEntitiesGroups);
                for (int i = 0; i < statCopy.Count; i++)
                {
                    StaticEntitiesGroup g = statCopy[i];
                    if (g == null)
                    {
                        continue;
                    }
                    api.RemoveStaticEntitiesGroup(g.name);
                }
            }
        }

        public static void Apply(SimulationAPI api, SceneSetupFileV1 setup)
        {
            Debug.Assert(api != null, "[SceneSetupApplier] Apply requires SimulationAPI.");
            Debug.Assert(setup != null, "[SceneSetupApplier] Apply requires setup data.");

            if (api == null || setup == null)
            {
                Debug.Assert(false, "[SceneSetupApplier] Apply called with null dependencies.");
                return;
            }

            Debug.Assert(setup.version == 1, "[SceneSetupApplier] Unsupported scene setup version.");

            // Location
            if (!string.IsNullOrEmpty(setup.locationName))
            {
                api.SetLocation(setup.locationName);
            }

            // Views
            int viewsCount = setup.viewsCount;
            if (viewsCount < 1)
            {
                viewsCount = 1;
            }
            if (viewsCount > 4)
            {
                viewsCount = 4;
            }
            api.SetViewCount(viewsCount);

            // Groups
            if (setup.groups != null)
            {
                for (int i = 0; i < setup.groups.Length; i++)
                {
                    GroupEntry group = setup.groups[i];
                    if (group == null)
                    {
                        continue;
                    }

                    Debug.Assert(!string.IsNullOrEmpty(group.presetName), "[SceneSetupApplier] Group presetName is required.");
                    Debug.Assert(!string.IsNullOrEmpty(group.groupName), "[SceneSetupApplier] Group groupName is required.");

                    if (string.IsNullOrEmpty(group.presetName) || string.IsNullOrEmpty(group.groupName))
                    {
                        Debug.Assert(false, "[SceneSetupApplier] Invalid group entry.");
                        continue;
                    }

                    bool hasOverrideHabitats = group.overrideHabitats != null && group.overrideHabitats.Length > 0;
                    if (hasOverrideHabitats)
                    {
                        api.SpawnEntityGroupInHabitats(group.presetName, group.groupName, group.overrideHabitats);
                    }
                    else
                    {
                        api.SpawnEntityGroup(group.presetName, group.groupName);
                    }

                    // Population fraction
                    float pop = group.population;
                    if (pop < 0f)
                    {
                        pop = 0f;
                    }
                    if (pop > 1f)
                    {
                        pop = 1f;
                    }
                    api.SetEntityGroupPopulation(group.groupName, pop);

                    // Per-view visibility
                    if (group.visibilities != null)
                    {
                        for (int v = 0; v < group.visibilities.Length; v++)
                        {
                            VisibilityEntry vis = group.visibilities[v];
                            if (vis == null)
                            {
                                continue;
                            }

                            int viewIndex = vis.viewIndex;
                            if (viewIndex < 0 || viewIndex > 3)
                            {
                                Debug.Assert(false, "[SceneSetupApplier] viewIndex must be in [0,3].");
                                continue;
                            }

                            float visibility = vis.visibility;
                            if (visibility < 0f)
                            {
                                visibility = 0f;
                            }
                            if (visibility > 1f)
                            {
                                visibility = 1f;
                            }
                            api.SetEntityGroupViewVisibility(group.groupName, viewIndex, visibility);
                        }
                    }
                }
            }

            // Per-view turbidity
            if (setup.turbidities != null)
            {
                for (int i = 0; i < setup.turbidities.Length; i++)
                {
                    TurbidityEntry t = setup.turbidities[i];
                    if (t == null)
                    {
                        continue;
                    }

                    int viewIndex = t.viewIndex;
                    if (viewIndex < 0 || viewIndex > 3)
                    {
                        Debug.Assert(false, "[SceneSetupApplier] turbidity viewIndex must be in [0,3].");
                        continue;
                    }

                    float turbidity = t.turbidity;
                    if (turbidity < -1f)
                    {
                        turbidity = -1f;
                    }
                    if (turbidity > 1f)
                    {
                        turbidity = 1f;
                    }

                    api.SetTurbidityForView(viewIndex, turbidity);
                }
            }
        }
    }
}



