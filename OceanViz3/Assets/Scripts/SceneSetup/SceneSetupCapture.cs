using System;
using System.Collections.Generic;
using UnityEngine;

namespace OceanViz3
{
    /// <summary>
    /// Utility for capturing the current simulation state (location, views, turbidity, entity groups)
    /// into a serializable SceneSetupFileV1 instance.
    /// </summary>
    public static class SceneSetupCapture
    {
        public static SceneSetupFileV1 Capture(MainScene mainScene)
        {
            Debug.Assert(mainScene != null, "[SceneSetupCapture] mainScene is null.");
            if (mainScene == null)
            {
                throw new ArgumentNullException("mainScene");
            }

            SimulationModeManager simulationModeManager = mainScene.simulationModeManager;
            Debug.Assert(simulationModeManager != null, "[SceneSetupCapture] SimulationModeManager is null on MainScene.");
            if (simulationModeManager == null)
            {
                throw new InvalidOperationException("[SceneSetupCapture] SimulationModeManager is required.");
            }

            SceneSetupFileV1 result = new SceneSetupFileV1();
            result.version = 1;
            result.locationName = mainScene.currentLocationName;

            if (simulationModeManager.views != null)
            {
                result.viewsCount = simulationModeManager.views.Count;
            }

            if (result.viewsCount < 1)
            {
                result.viewsCount = 1;
            }
            if (result.viewsCount > 4)
            {
                result.viewsCount = 4;
            }

            // Per-view turbidity
            List<TurbidityEntry> turbidityEntries = new List<TurbidityEntry>();
            if (simulationModeManager.turbidityPerView != null)
            {
                int count = simulationModeManager.turbidityPerView.Count;
                for (int i = 0; i < count && i < 4; i++)
                {
                    TurbidityEntry entry = new TurbidityEntry();
                    entry.viewIndex = i;

                    float value = simulationModeManager.turbidityPerView[i];
                    if (value < -1f)
                    {
                        value = -1f;
                    }
                    if (value > 1f)
                    {
                        value = 1f;
                    }
                    entry.turbidity = value;

                    turbidityEntries.Add(entry);
                }
            }
            result.turbidities = turbidityEntries.ToArray();

            // Groups (dynamic + static)
            List<GroupEntry> groups = new List<GroupEntry>();

            if (simulationModeManager.dynamicEntitiesGroups != null)
            {
                for (int i = 0; i < simulationModeManager.dynamicEntitiesGroups.Count; i++)
                {
                    DynamicEntitiesGroup group = simulationModeManager.dynamicEntitiesGroups[i];
                    if (group == null)
                    {
                        continue;
                    }

                    GroupEntry entry = BuildGroupEntryFromDynamic(group);
                    groups.Add(entry);
                }
            }

            if (simulationModeManager.staticEntitiesGroups != null)
            {
                for (int i = 0; i < simulationModeManager.staticEntitiesGroups.Count; i++)
                {
                    StaticEntitiesGroup group = simulationModeManager.staticEntitiesGroups[i];
                    if (group == null)
                    {
                        continue;
                    }

                    GroupEntry entry = BuildGroupEntryFromStatic(group);
                    groups.Add(entry);
                }
            }

            result.groups = groups.ToArray();
            return result;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }
            if (value > 1f)
            {
                return 1f;
            }
            return value;
        }

        private static float ComputePopulationFraction(int requestedCount, int maxPopulation)
        {
            if (maxPopulation <= 0)
            {
                return 0f;
            }

            float fraction = (float)requestedCount / (float)maxPopulation;
            return Clamp01(fraction);
        }

        private static string[] CopyOverrideHabitatsOrEmpty(bool hasOverride, string[] overrideHabitats)
        {
            if (hasOverride && overrideHabitats != null)
            {
                return overrideHabitats;
            }
            return new string[0];
        }

        private static VisibilityEntry[] BuildVisibilitiesFromPercentages(int[] percentages)
        {
            if (percentages == null)
            {
                return new VisibilityEntry[0];
            }

            List<VisibilityEntry> visEntries = new List<VisibilityEntry>();
            int length = percentages.Length;
            for (int i = 0; i < length; i++)
            {
                int percent = percentages[i];
                float frac = Clamp01((float)percent / 100f);

                VisibilityEntry v = new VisibilityEntry();
                v.viewIndex = i;
                v.visibility = frac;
                visEntries.Add(v);
            }

            return visEntries.ToArray();
        }

        private static GroupEntry BuildGroupEntryFromDynamic(DynamicEntitiesGroup group)
        {
            GroupEntry entry = new GroupEntry();

            entry.groupName = group.name;

            string presetName = null;
            if (group.dynamicEntityPreset != null)
            {
                presetName = group.dynamicEntityPreset.name;
            }
            if (string.IsNullOrEmpty(presetName))
            {
                presetName = group.name;
            }
            entry.presetName = presetName;

            int population = group.GetCurrentPopulation();
            int maxPopulation = 0;
            if (group.dynamicEntityPreset != null)
            {
                maxPopulation = group.dynamicEntityPreset.maxPopulation;
            }
            entry.population = ComputePopulationFraction(population, maxPopulation);

            string[] overrideHabitats;
            bool hasOverrideHabitats = group.TryGetOverrideHabitats(out overrideHabitats);
            entry.overrideHabitats = CopyOverrideHabitatsOrEmpty(hasOverrideHabitats, overrideHabitats);

            int[] visPercentages = group.GetViewVisibilityPercentagesCopy();
            entry.visibilities = BuildVisibilitiesFromPercentages(visPercentages);

            return entry;
        }

        private static GroupEntry BuildGroupEntryFromStatic(StaticEntitiesGroup group)
        {
            GroupEntry entry = new GroupEntry();

            entry.groupName = group.name;

            string presetName = null;
            if (group.staticEntityPreset != null)
            {
                presetName = group.staticEntityPreset.name;
            }
            if (string.IsNullOrEmpty(presetName))
            {
                presetName = group.name;
            }
            entry.presetName = presetName;

            int population = group.GetCurrentPopulation();
            int maxPopulation = 0;
            if (group.staticEntityPreset != null)
            {
                maxPopulation = group.staticEntityPreset.maxPopulation;
            }
            entry.population = ComputePopulationFraction(population, maxPopulation);

            string[] overrideHabitats;
            bool hasOverrideHabitats = group.TryGetOverrideHabitats(out overrideHabitats);
            entry.overrideHabitats = CopyOverrideHabitatsOrEmpty(hasOverrideHabitats, overrideHabitats);

            int[] visPercentages = group.GetViewVisibilityPercentagesCopy();
            entry.visibilities = BuildVisibilitiesFromPercentages(visPercentages);

            return entry;
        }
    }
}


