using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities;
using Unity.VisualScripting;
using Unity.Mathematics;
using Unity.Transforms;
using System.Reflection.Emit;
using Unity.Collections;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using System.Linq;
using Cinemachine;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Unity.Rendering;

namespace OceanViz3
{
    public class SimulationModeManager : AppModeManager
    {
        [Header("UI")]
        public GameObject mainGUIUIDocument;
        
        [Header("Scene Objects")]
        public GameObject cameraRig;
        
        // Fields moved from MainScene
        /// <summary>
        /// List of active views in the scene.
        /// </summary>
        public List<View> views = new List<View>();
        public List<float> turbidityPerView = new List<float> { 0.5f, 0.5f, 0.5f, 0.5f };
        
        /// <summary>
        /// List of active dynamic entity groups (e.g., fish schools) in the scene.
        /// </summary>
        [SerializeField]
        public List<DynamicEntitiesGroup> dynamicEntitiesGroups = new List<DynamicEntitiesGroup>();
        
        /// <summary>
        /// Unique identifier counter for dynamic entity groups.
        /// </summary>
        private int nextDynamicEntityGroupId = 0;
        private int nextStaticEntityGroupId = 0;
        
        /// <summary>
        /// List of active static entity groups (e.g., coral, seaweed) in the scene.
        /// </summary>
        [SerializeField]
        public List<StaticEntitiesGroup> staticEntitiesGroups = new List<StaticEntitiesGroup>();

        //// UI
        public VisualElement mainGui;
        private DropdownField addDynamicRowDropdownField;
        private DropdownField addStaticRowDropdownField;
        private DropdownField locationsDropdownField;
        private VisualElement addDynamicRowButton;
        private VisualElement addStaticRowButton;
        public VisualTreeAsset DynamicGroupDataRow;
        public VisualTreeAsset StaticGroupDataRow;
        
        private EventCallback<ChangeEvent<string>> locationChangeCallback;

        private EntityManager entityManager;
        private bool isHudless;
        private bool isAutomaticCameraModeActive;

        public override void Setup(MainScene mainScene)
        {
            base.Setup(mainScene);
            
            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

            mainGui = mainGUIUIDocument.GetComponent<UIDocument>().rootVisualElement;

            // Main Menu Button
            var mainMenuButton = mainGui.Q<Button>("MainMenuButton");
            mainMenuButton.RegisterCallback<ClickEvent>((evt) => this.mainScene.ToggleMainMenu());

            // LocationsDropdownField
            locationsDropdownField = mainGui.Q<DropdownField>("LocationsDropdownField");
            locationsDropdownField.choices.Clear();
            for (int i = 0; i < mainScene.locationNames.Count; i++)
            {
                locationsDropdownField.choices.Add(mainScene.locationNames[i]);
            }
            if (mainScene.locationNames.Count > 0)
            {
                locationsDropdownField.value = mainScene.currentLocationName;
            }
            locationChangeCallback = (evt) => OnLocationLocationDropdownFieldChanged(evt.newValue);
            locationsDropdownField.RegisterCallback(locationChangeCallback);
            
            //// Presets
            if (DynamicGroupDataRow == null){Debug.LogError("[SimulationModeManager] DynamicGroupDataRow is null");}
            if (StaticGroupDataRow == null){Debug.LogError("[SimulationModeManager] StaticGroupDataRow is null");}

            // Read StreamingAssets folder to populate the presets lists
            GroupPresetsManager.Instance.UpdatePresets();
            
            addDynamicRowDropdownField = mainGui.Q<DropdownField>("AddDynamicRowDropdownField");
            addStaticRowDropdownField = mainGui.Q<DropdownField>("AddStaticRowDropdownField");

            // Clear existing choices to avoid duplicates
            addDynamicRowDropdownField.choices.Clear();
            addStaticRowDropdownField.choices.Clear();

            // Populate the dropdowns with preset names
            foreach (DynamicEntityPreset dynamicEntityPreset in GroupPresetsManager.Instance.dynamicEntitiesPresetsList)
            {
                addDynamicRowDropdownField.choices.Add(dynamicEntityPreset.name);
            }
            foreach (StaticEntityPreset staticEntityPreset in GroupPresetsManager.Instance.staticEntitiesPresetsList)
            {
                addStaticRowDropdownField.choices.Add(staticEntityPreset.name);
            }

            // Set the default value of the dropdown to the first element of the choices list
            addDynamicRowDropdownField.value = addDynamicRowDropdownField.choices[0];
            addStaticRowDropdownField.value = addStaticRowDropdownField.choices[0];

            // AddRowButton
            addDynamicRowButton = mainGui.Q<Button>("AddDynamicRowButton");
            addDynamicRowButton.RegisterCallback<ClickEvent>((evt) => SpawnSelectedDynamicPreset());
            addStaticRowButton = mainGui.Q<Button>("AddStaticRowButton");
            addStaticRowButton.RegisterCallback<ClickEvent>((evt) => SpawnSelectedStaticPreset());

            // ViewCount buttons callbacks
            mainGui.Q<Button>("SetViews1").RegisterCallback<ClickEvent>((evt) => SetViewCountAndUpdateGUIState(1));
            mainGui.Q<Button>("SetViews2").RegisterCallback<ClickEvent>((evt) => SetViewCountAndUpdateGUIState(2));
            mainGui.Q<Button>("SetViews3").RegisterCallback<ClickEvent>((evt) => SetViewCountAndUpdateGUIState(3));
            mainGui.Q<Button>("SetViews4").RegisterCallback<ClickEvent>((evt) => SetViewCountAndUpdateGUIState(4));
            
            // Swim mode
            mainGui.Q<Button>("ActivateSwimModeButton").RegisterCallback<ClickEvent>((evt) => ActivateSwimMode());
            
            // Automatic camera mode
            mainGui.Q<Button>("ActivateAutomaticCameraModeButton").RegisterCallback<ClickEvent>((evt) => ActivateAutomaticCameraMode());

            // Save/Load scene setup
            Button saveSceneButton = mainGui.Q<Button>("SaveScene");
            Debug.Assert(saveSceneButton != null, "[SimulationModeManager] SaveScene button not found in UI.");
            if (saveSceneButton != null)
            {
                saveSceneButton.RegisterCallback<ClickEvent>((evt) => SaveSceneSetup());
            }

            Button loadSceneButton = mainGui.Q<Button>("LoadScene");
            Debug.Assert(loadSceneButton != null, "[SimulationModeManager] LoadScene button not found in UI.");
            if (loadSceneButton != null)
            {
                loadSceneButton.RegisterCallback<ClickEvent>((evt) => LoadSceneSetup());
            }
        }

        private string GetDefaultSceneSetupDirectory()
        {
            string assetsPath = Application.dataPath;
            string projectRoot = Path.GetFullPath(Path.Combine(assetsPath, ".."));
            return Path.Combine(projectRoot, "SavedScenes");
        }

        private void SaveSceneSetup()
        {
            Debug.Assert(mainScene != null, "[SimulationModeManager] SaveSceneSetup requires mainScene.");
            if (mainScene == null)
            {
                Debug.Assert(false, "[SimulationModeManager] mainScene is null in SaveSceneSetup.");
                return;
            }

            if (!MainScene.IsReady || !LocationScript.IsReady)
            {
                Debug.LogWarning("[SimulationModeManager] Cannot save scene setup; simulation is not ready.");
                return;
            }

            string initialDir = GetDefaultSceneSetupDirectory();
            if (!Directory.Exists(initialDir))
            {
                Directory.CreateDirectory(initialDir);
            }

            string path;
            bool ok = SceneSetupFileDialogs.TryGetSavePath(initialDir, out path);
            if (!ok)
            {
                return;
            }
            SaveSceneSetupToPath(path);
        }

        private void SaveSceneSetupToPath(string path)
        {
            Debug.Assert(!string.IsNullOrEmpty(path), "[SimulationModeManager] Save path is empty.");
            if (string.IsNullOrEmpty(path))
            {
                Debug.Assert(false, "[SimulationModeManager] Save path is empty.");
                return;
            }

            string requiredExt = "." + SceneSetupFileDialogs.SceneSetupExtension;
            if (!path.EndsWith(requiredExt, System.StringComparison.OrdinalIgnoreCase))
            {
                path = path + requiredExt;
            }

            SceneSetupFileV1 dto = SceneSetupCapture.Capture(mainScene);
            string json = JsonUtility.ToJson(dto, true);
            File.WriteAllText(path, json);
            Debug.Log("[SimulationModeManager] Scene setup saved: " + path);
        }

        private void LoadSceneSetup()
        {
            Debug.Assert(mainScene != null, "[SimulationModeManager] LoadSceneSetup requires mainScene.");
            if (mainScene == null)
            {
                Debug.Assert(false, "[SimulationModeManager] mainScene is null in LoadSceneSetup.");
                return;
            }

            if (!MainScene.IsReady)
            {
                Debug.LogWarning("[SimulationModeManager] Cannot load scene setup; MainScene is not ready.");
                return;
            }

            string initialDir = GetDefaultSceneSetupDirectory();
            if (!Directory.Exists(initialDir))
            {
                Directory.CreateDirectory(initialDir);
            }

            string path;
            bool ok = SceneSetupFileDialogs.TryGetOpenPath(initialDir, out path);
            if (!ok)
            {
                return;
            }
            LoadSceneSetupFromPath(path);
        }

        private void LoadSceneSetupFromPath(string path)
        {
            Debug.Assert(!string.IsNullOrEmpty(path), "[SimulationModeManager] Open path is empty.");
            if (string.IsNullOrEmpty(path))
            {
                Debug.Assert(false, "[SimulationModeManager] Open path is empty.");
                return;
            }

            if (!File.Exists(path))
            {
                Debug.LogError("[SimulationModeManager] Scene setup file does not exist: " + path);
                Debug.Assert(false, "[SimulationModeManager] Selected scene setup file does not exist.");
                return;
            }

            string json = File.ReadAllText(path);
            SceneSetupFileV1 setup = JsonUtility.FromJson<SceneSetupFileV1>(json);
            if (setup == null)
            {
                Debug.LogError("[SimulationModeManager] Failed to parse scene setup JSON.");
                Debug.Assert(false, "[SimulationModeManager] Failed to parse scene setup JSON.");
                return;
            }

            Debug.Assert(mainScene.simulationAPI != null, "[SimulationModeManager] SimulationAPI is null; cannot apply setup.");
            if (mainScene.simulationAPI == null)
            {
                Debug.Assert(false, "[SimulationModeManager] SimulationAPI is null; cannot apply setup.");
                return;
            }

            // Loading clears current scene setup first.
            SceneSetupApplier.ClearCurrentSetup(mainScene.simulationAPI, this);
            SceneSetupApplier.Apply(mainScene.simulationAPI, setup);

            Debug.Log("[SimulationModeManager] Scene setup loaded: " + path);
        }

        public override void EnterMode()
        {
            mainGui.style.display = DisplayStyle.Flex;
            
            // Re-enable camera controls if they were disabled
            if (cameraRig != null) cameraRig.SetActive(true);

            // Add first view if none exist
            if (views.Count == 0)
            {
                SetViewCountAndUpdateGUIState(1);
            }
            
            UpdateHudVisibility();
        }

        public override void ExitMode()
        {
            mainGui.style.display = DisplayStyle.None;
            if (cameraRig != null)
            {
                if(cameraRig.GetComponent<SimulationModeCameraRig>().isActive)
                {
                    DectivateSwimMode();
                }
                cameraRig.SetActive(false);
            }
        }

        public override void EnterMenu()
        {
            mainGui.style.display = DisplayStyle.None;
        }

        public override void ExitMenu()
        {
            mainGui.style.display = DisplayStyle.Flex;
            UpdateHudVisibility();
        }

        public override void OnUpdate()
        {
            // RMB exits swim mode and automatic camera mode
            if (Input.GetMouseButtonDown(1)) // Right mouse button
            {
                if (cameraRig.GetComponent<SimulationModeCameraRig>().isActive)
                {
                    DectivateSwimMode();
                }
                else if (mainScene.mainCamera.transform.parent == mainScene.currentLocationScript.dollyCart.transform)
                {
                    DectivateAutomaticCameraMode();
                }
            }
        }
        
        public override void OnLocationReady()
        {
            // After the new location is loaded, update all dynamic entities groups
            foreach (DynamicEntitiesGroup group in dynamicEntitiesGroups)
            {
                // Get new boid bounds for all of the group's active habitats (override or preset)
                List<GameObject> newBoidBounds = new List<GameObject>();
                foreach (string habitat in group.GetActiveHabitats())
                {
                    var habitatBounds = mainScene.currentLocationScript.GetBoidBoundsByBiomeName(habitat);
                    if (habitatBounds != null && habitatBounds.Count > 0)
                    {
                        newBoidBounds.AddRange(habitatBounds);
                    }
                    else
                    {
                        Debug.LogWarning($"[MainScene] No boid bounds found for habitat: {habitat} in new location");
                    }
                }

                if (newBoidBounds.Count > 0)
                {
                    // Update the group with new bounds
                    group.UpdateBoidBounds(newBoidBounds);
                }
                else
                {
                    Debug.LogError($"[MainScene] No valid boid bounds found for group {group.name} in any of its habitats");
                }
            }

            // Update all static entities groups
            UpdateStaticEntitiesGroups();

            // Run any pending simulation setup executors for components in the newly loaded location.
            Debug.Assert(mainScene != null, "[SimulationModeManager] mainScene is null in OnLocationReady.");
            if (mainScene != null)
            {
                Debug.Assert(mainScene.simulationAPI != null, "[SimulationModeManager] SimulationAPI is null in OnLocationReady.");
                if (mainScene.simulationAPI != null)
                {
                    mainScene.simulationAPI.RunPendingExecutors();
                }
            }
        }

        private void OnLocationLocationDropdownFieldChanged(string locationName)
        {
            if (cameraRig != null)
            {
                var cameraRigController = cameraRig.GetComponent<SimulationModeCameraRig>();
                if (cameraRigController != null)
                {
                    cameraRigController.ResetPositionAndRotation();
                }
            }
            mainScene.SwitchLocation(locationName);
            locationsDropdownField.value = locationName;
        }

        private void UpdateStaticEntitiesGroups()
        {
            foreach (var group in staticEntitiesGroups)
            {
                // Force reload of the group to update entities
                if (group != null)
                {
                    group.ReloadGroup(new ClickEvent());
                }
            }
        }
        
        public void SpawnSelectedDynamicPreset()
        {
            SpawnDynamicPreset(addDynamicRowDropdownField.value);
        }
        
        public async void SpawnDynamicPreset(string name)
        {
            MainScene.SetReadyState(false);
            if (GroupPresetsManager.Instance == null)
            {
                Debug.LogError("[MainScene] GroupPresetsManager.Instance is null. Make sure it's properly initialized.");
                return;
            }
            
            Debug.Log($"[MainScene] Attempting to spawn dynamic preset: {name}");
            DynamicEntityPreset selectedPreset = GroupPresetsManager.Instance.GetDynamicPresetByName(name);
            
            if (selectedPreset == null)
            {
                Debug.LogError($"[MainScene] Error: Dynamic preset '{name}' not found in the presets list.");
                return;
            }

            if (selectedPreset.habitats == null || selectedPreset.habitats.Length == 0)
            {
                Debug.LogError($"[MainScene] Error: Dynamic preset '{name}' has no habitats defined.");
                return;
            }

            VisualElement dataRow = DynamicGroupDataRow.CloneTree();
            mainGui.Q<VisualElement>("DataRows").Add(dataRow);
            
            // Get boid bounds for all habitats
            List<GameObject> filteredBoidBounds = new List<GameObject>();
            foreach (string habitat in selectedPreset.habitats)
            {
                if (string.IsNullOrEmpty(habitat))
                {
                    Debug.LogError($"[MainScene] Empty habitat found in preset '{name}'");
                    continue;
                }

                var habitatBounds = mainScene.currentLocationScript.GetBoidBoundsByBiomeName(habitat);
                if (habitatBounds != null && habitatBounds.Count > 0)
                {
                    filteredBoidBounds.AddRange(habitatBounds);
                    Debug.Log($"[MainScene] Found {habitatBounds.Count} boid bounds for habitat: {habitat}");
                }
                else
                {
                    Debug.LogWarning($"[MainScene] No boid bounds found for habitat: {habitat}");
                }
            }

            if (filteredBoidBounds.Count == 0)
            {
                Debug.LogError($"[MainScene] No valid boid bounds found for any of the specified habitats");
                mainGui.Q<VisualElement>("DataRows").Remove(dataRow);
                return;
            }

            DynamicEntitiesGroup dynamicEntitiesGroup = new DynamicEntitiesGroup();
            dynamicEntitiesGroup.Setup(
                name: selectedPreset.name,
                dynamicEntityId: nextDynamicEntityGroupId,
                dynamicEntityPreset: selectedPreset,
                dataRow: dataRow,
                viewsCount: views.Count,
                boidBounds: filteredBoidBounds
            );
            
            try
            {
                await dynamicEntitiesGroup.LoadAndSpawnGroup();
                dynamicEntitiesGroups.Add(dynamicEntitiesGroup);
                dynamicEntitiesGroup.OnDeleteRequest += HandleGroupDeleteRequest;
                nextDynamicEntityGroupId++;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MainScene] Failed to load and spawn dynamic entities group: {e.Message}");
                mainGui.Q<VisualElement>("DataRows").Remove(dataRow);
                return;
            }
            finally
            {
                MainScene.SetReadyState(true);
                Debug.Log("[MainScene] SpawnDynamicPreset completed, IsReady set to true");
            }
        }

        /// <summary>
        /// Spawns a dynamic preset into explicitly provided habitats, overriding the preset habitats.
        /// </summary>
        /// <param name="presetName">Dynamic preset name</param>
        /// <param name="groupName">Display name for this session's group</param>
        /// <param name="habitats">Habitats to use to collect boid bounds</param>
        public async void SpawnDynamicPresetInHabitats(string presetName, string groupName, string[] habitats)
        {
            MainScene.SetReadyState(false);
            if (GroupPresetsManager.Instance == null)
            {
                Debug.LogError("[MainScene] GroupPresetsManager.Instance is null. Make sure it's properly initialized.");
                return;
            }

            if (habitats == null || habitats.Length == 0)
            {
                Debug.LogError("[MainScene] SpawnDynamicPresetInHabitats requires at least one habitat.");
                Debug.Assert(false, "SpawnDynamicPresetInHabitats called with empty habitats.");
                return;
            }

            Debug.Log("[MainScene] Attempting to spawn dynamic preset in habitats: " + presetName + " as '" + groupName + "'");
            DynamicEntityPreset selectedPreset = GroupPresetsManager.Instance.GetDynamicPresetByName(presetName);

            if (selectedPreset == null)
            {
                Debug.LogError("[MainScene] Error: Dynamic preset '" + presetName + "' not found in the presets list.");
                return;
            }

            VisualElement dataRow = DynamicGroupDataRow.CloneTree();
            mainGui.Q<VisualElement>("DataRows").Add(dataRow);

            // Get boid bounds for provided habitats
            List<GameObject> filteredBoidBounds = new List<GameObject>();
            foreach (string habitat in habitats)
            {
                if (string.IsNullOrEmpty(habitat))
                {
                    Debug.LogError("[MainScene] Empty habitat provided for SpawnDynamicPresetInHabitats");
                    continue;
                }

                var habitatBounds = mainScene.currentLocationScript.GetBoidBoundsByBiomeName(habitat);
                if (habitatBounds != null && habitatBounds.Count > 0)
                {
                    filteredBoidBounds.AddRange(habitatBounds);
                    Debug.Log("[MainScene] Found " + habitatBounds.Count + " boid bounds for habitat: " + habitat);
                }
                else
                {
                    Debug.LogWarning("[MainScene] No boid bounds found for habitat: " + habitat);
                }
            }

            if (filteredBoidBounds.Count == 0)
            {
                Debug.LogError("[MainScene] No valid boid bounds found for any of the specified habitats");
                mainGui.Q<VisualElement>("DataRows").Remove(dataRow);
                return;
            }

            DynamicEntitiesGroup dynamicEntitiesGroup = new DynamicEntitiesGroup();
            dynamicEntitiesGroup.Setup(
                name: groupName,
                dynamicEntityId: nextDynamicEntityGroupId,
                dynamicEntityPreset: selectedPreset,
                dataRow: dataRow,
                viewsCount: views.Count,
                boidBounds: filteredBoidBounds
            );
            dynamicEntitiesGroup.SetOverrideHabitats(habitats);

            try
            {
                await dynamicEntitiesGroup.LoadAndSpawnGroup();
                dynamicEntitiesGroups.Add(dynamicEntitiesGroup);
                dynamicEntitiesGroup.OnDeleteRequest += HandleGroupDeleteRequest;
                nextDynamicEntityGroupId++;
            }
            catch (Exception e)
            {
                Debug.LogError("[MainScene] Failed to load and spawn dynamic entities group: " + e.Message);
                mainGui.Q<VisualElement>("DataRows").Remove(dataRow);
                return;
            }
            finally
            {
                MainScene.SetReadyState(true);
                Debug.Log("[MainScene] SpawnDynamicPresetInHabitats completed, IsReady set to true");
            }
        }

        /// <summary>
        /// Spawns a dynamic preset with a custom group display name for this session.
        /// </summary>
        /// <param name="presetName">Name of the dynamic preset to spawn</param>
        /// <param name="groupName">Display name for this session's group</param>
        public async void SpawnDynamicPreset(string presetName, string groupName)
        {
            MainScene.SetReadyState(false);
            if (GroupPresetsManager.Instance == null)
            {
                Debug.LogError("[MainScene] GroupPresetsManager.Instance is null. Make sure it's properly initialized.");
                return;
            }

            Debug.Log("[MainScene] Attempting to spawn dynamic preset: " + presetName + " as '" + groupName + "'");
            DynamicEntityPreset selectedPreset = GroupPresetsManager.Instance.GetDynamicPresetByName(presetName);

            if (selectedPreset == null)
            {
                Debug.LogError("[MainScene] Error: Dynamic preset '" + presetName + "' not found in the presets list.");
                return;
            }

            if (selectedPreset.habitats == null || selectedPreset.habitats.Length == 0)
            {
                Debug.LogError("[MainScene] Error: Dynamic preset '" + presetName + "' has no habitats defined.");
                return;
            }

            VisualElement dataRow = DynamicGroupDataRow.CloneTree();
            mainGui.Q<VisualElement>("DataRows").Add(dataRow);

            // Get boid bounds for all habitats
            List<GameObject> filteredBoidBounds = new List<GameObject>();
            foreach (string habitat in selectedPreset.habitats)
            {
                if (string.IsNullOrEmpty(habitat))
                {
                    Debug.LogError("[MainScene] Empty habitat found in preset '" + presetName + "'");
                    continue;
                }

                var habitatBounds = mainScene.currentLocationScript.GetBoidBoundsByBiomeName(habitat);
                if (habitatBounds != null && habitatBounds.Count > 0)
                {
                    filteredBoidBounds.AddRange(habitatBounds);
                    Debug.Log("[MainScene] Found " + habitatBounds.Count + " boid bounds for habitat: " + habitat);
                }
                else
                {
                    Debug.LogWarning("[MainScene] No boid bounds found for habitat: " + habitat);
                }
            }

            if (filteredBoidBounds.Count == 0)
            {
                Debug.LogError("[MainScene] No valid boid bounds found for any of the specified habitats");
                mainGui.Q<VisualElement>("DataRows").Remove(dataRow);
                return;
            }

            DynamicEntitiesGroup dynamicEntitiesGroup = new DynamicEntitiesGroup();
            dynamicEntitiesGroup.Setup(
                name: groupName,
                dynamicEntityId: nextDynamicEntityGroupId,
                dynamicEntityPreset: selectedPreset,
                dataRow: dataRow,
                viewsCount: views.Count,
                boidBounds: filteredBoidBounds
            );

            try
            {
                await dynamicEntitiesGroup.LoadAndSpawnGroup();
                dynamicEntitiesGroups.Add(dynamicEntitiesGroup);
                dynamicEntitiesGroup.OnDeleteRequest += HandleGroupDeleteRequest;
                nextDynamicEntityGroupId++;
            }
            catch (Exception e)
            {
                Debug.LogError("[MainScene] Failed to load and spawn dynamic entities group: " + e.Message);
                mainGui.Q<VisualElement>("DataRows").Remove(dataRow);
                return;
            }
            finally
            {
                MainScene.SetReadyState(true);
                Debug.Log("[MainScene] SpawnDynamicPreset (custom name) completed, IsReady set to true");
            }
        }
        
        public async void SpawnSelectedStaticPreset()
        {
            await SpawnStaticPreset(addStaticRowDropdownField.value, addStaticRowDropdownField.value);
        }

        public async Task SpawnStaticPreset(string presetName, string groupName)
        {
            MainScene.SetReadyState(false);
            if (GroupPresetsManager.Instance == null)
            {
                Debug.LogError("[MainScene] GroupPresetsManager.Instance is null. Make sure it's properly initialized.");
                return;
            }
            
            Debug.Log($"[MainScene] Attempting to spawn static preset: {presetName}");
            StaticEntityPreset selectedPreset = GroupPresetsManager.Instance.GetStaticPresetByName(presetName);
            
            if (selectedPreset == null)
            {
                Debug.LogError($"[MainScene] Error: Static preset '{presetName}' not found in the presets list.");
                return;
            }

            if (selectedPreset.habitats == null || selectedPreset.habitats.Length == 0)
            {
                Debug.LogError($"[MainScene] Error: Static preset '{presetName}' has no habitats defined.");
                return;
            }

            VisualElement dataRow = StaticGroupDataRow.CloneTree();
            mainGui.Q<VisualElement>("DataRows").Add(dataRow);

            StaticEntitiesGroup staticEntitiesGroup = new StaticEntitiesGroup();
            staticEntitiesGroup.Setup(
                name: groupName,
                dataRow: dataRow,
                viewsCount: views.Count,
                staticEntitiesGroupId: nextStaticEntityGroupId,
                staticEntityPreset: selectedPreset
            );
            
            try
            {
                await staticEntitiesGroup.LoadAndSpawnGroup();
                staticEntitiesGroups.Add(staticEntitiesGroup);
                staticEntitiesGroup.OnDeleteRequest += HandleStaticGroupDeleteRequest;
                nextStaticEntityGroupId++;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MainScene] Failed to load and spawn static entities group: {e.Message}");
                mainGui.Q<VisualElement>("DataRows").Remove(dataRow);
                return;
            }
            finally
            {
                MainScene.SetReadyState(true);
                Debug.Log("[MainScene] SpawnStaticPreset completed, IsReady set to true");
            }
        }

        /// <summary>
        /// Spawns a static preset and writes the provided habitats to the group's ECS buffer instead of the preset habitats.
        /// </summary>
        /// <param name="presetName">Static preset name</param>
        /// <param name="groupName">Display name for the group</param>
        /// <param name="habitats">Habitats to use when creating the group's ECS habitat buffer</param>
        public async Task SpawnStaticPresetInHabitats(string presetName, string groupName, string[] habitats)
        {
            MainScene.SetReadyState(false);
            if (GroupPresetsManager.Instance == null)
            {
                Debug.LogError("[MainScene] GroupPresetsManager.Instance is null. Make sure it's properly initialized.");
                return;
            }

            if (habitats == null || habitats.Length == 0)
            {
                Debug.LogError("[MainScene] SpawnStaticPresetInHabitats requires at least one habitat.");
                Debug.Assert(false, "SpawnStaticPresetInHabitats called with empty habitats.");
                return;
            }

            Debug.Log("[MainScene] Attempting to spawn static preset in habitats: " + presetName + " as '" + groupName + "'");
            StaticEntityPreset selectedPreset = GroupPresetsManager.Instance.GetStaticPresetByName(presetName);

            if (selectedPreset == null)
            {
                Debug.LogError("[MainScene] Error: Static preset '" + presetName + "' not found in the presets list.");
                return;
            }

            VisualElement dataRow = StaticGroupDataRow.CloneTree();
            mainGui.Q<VisualElement>("DataRows").Add(dataRow);

            StaticEntitiesGroup staticEntitiesGroup = new StaticEntitiesGroup();
            staticEntitiesGroup.Setup(
                name: groupName,
                dataRow: dataRow,
                viewsCount: views.Count,
                staticEntitiesGroupId: nextStaticEntityGroupId,
                staticEntityPreset: selectedPreset
            );
            staticEntitiesGroup.SetOverrideHabitats(habitats);

            try
            {
                await staticEntitiesGroup.LoadAndSpawnGroup();
                staticEntitiesGroups.Add(staticEntitiesGroup);
                staticEntitiesGroup.OnDeleteRequest += HandleStaticGroupDeleteRequest;
                nextStaticEntityGroupId++;
            }
            catch (Exception e)
            {
                Debug.LogError("[MainScene] Failed to load and spawn static entities group: " + e.Message);
                mainGui.Q<VisualElement>("DataRows").Remove(dataRow);
                return;
            }
            finally
            {
                MainScene.SetReadyState(true);
                Debug.Log("[MainScene] SpawnStaticPresetInHabitats completed, IsReady set to true");
            }
        }

        private void HandleGroupDeleteRequest(DynamicEntitiesGroup dynamicEntitiesGroup)
        {
            dynamicEntitiesGroups.Remove(dynamicEntitiesGroup);
        }

        private void HandleStaticGroupDeleteRequest(StaticEntitiesGroup staticEntitiesGroup)
        {
            staticEntitiesGroups.Remove(staticEntitiesGroup);
        }
        
        
        public void OnTurbiditySliderValueChanged(ChangeEvent<float> evt)
        {
            var slider = evt.target as Slider;
            int viewIndex = int.Parse(slider.name.Substring(slider.name.Length - 1));
            float turbidityStrength = Mathf.Clamp(evt.newValue, -1f, 1f);
            turbidityPerView[viewIndex] = turbidityStrength;
            mainScene.currentLocationScript.SetTurbidityForView(viewIndex, turbidityStrength);
        }
        
        public void SetViewCountAndUpdateGUIState(int viewCount)
        {
            MainScene.SetReadyState(false);
            try
            {
                // Dark gray color
                StyleColor defaultColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));

                // Selected cyan color
                StyleColor selectedColor = new StyleColor(new Color(0.0f, 0.8f, 1.0f));

                // Reset the background color of the view count buttons to default
                mainGui.Q<Button>("SetViews1").style.backgroundColor = defaultColor;
                mainGui.Q<Button>("SetViews2").style.backgroundColor = defaultColor;
                mainGui.Q<Button>("SetViews3").style.backgroundColor = defaultColor;
                mainGui.Q<Button>("SetViews4").style.backgroundColor = defaultColor;

                // Change the background color of the correct view count button to blue to indicate the current view count
                if (viewCount == 1)
                {
                    mainGui.Q<Button>("SetViews1").style.backgroundColor = selectedColor;
                }
                else if (viewCount == 2)
                {
                    mainGui.Q<Button>("SetViews2").style.backgroundColor = selectedColor;
                }
                else if (viewCount == 3)
                {
                    mainGui.Q<Button>("SetViews3").style.backgroundColor = selectedColor;
                }
                else if (viewCount == 4)
                {
                    mainGui.Q<Button>("SetViews4").style.backgroundColor = selectedColor;
                }

                // If the view count is less than the current view count
                if (viewCount < views.Count)
                {
                    // Remove views until the view count is equal to the desired view count
                    while (views.Count > viewCount)
                    {
                        RemoveView();
                    }
                }
                // If the view count is greater than the current view count
                else if (viewCount > views.Count)
                {
                    // Add views until the view count is equal to the desired view count
                    while (views.Count < viewCount)
                    {
                        AddView();
                    }
                }
            }
            finally
            {
                MainScene.SetReadyState(true);
            }
        }

        private void AddView()
        {
            MainScene.SetReadyState(false);
            try
            {
                // Debug
                Debug.Log("[MainScene] AddView");

                // Instantiate a View object and add it to the views list
                View view = new View();
                views.Add(view);

                // Get index of viewPrefab in views list
                int index = views.IndexOf(view);
                
                // Inform each group about viewcount change
                foreach (DynamicEntitiesGroup group in dynamicEntitiesGroups)
                {
                    group.UpdateViewsCount(views.Count);
                }
                
                foreach (StaticEntitiesGroup group in staticEntitiesGroups)
                {
                    group.UpdateViewsCount(views.Count);
                }
                
                // If it's not null, inform the location script about the view count change
                if (mainScene.currentLocationScript != null)
                {
                    mainScene.currentLocationScript.UpdateViewsCount(views.Count);
                }

                UpdateViewsLabel();
            }
            finally
            {
                MainScene.SetReadyState(true);
            }
        }

        private void RemoveView()
        {
            MainScene.SetReadyState(false);
            try
            {
                // Debug
                Debug.Log("[MainScene] RemoveView");

                // If min views reached, return
                if (views.Count <= 1)
                {
                    return;
                }

                // Erase the last view from views list
                views.RemoveAt(views.Count - 1);

                // Inform each group about viewcount change
                foreach (DynamicEntitiesGroup group in dynamicEntitiesGroups)
                {
                    group.UpdateViewsCount(views.Count);
                }
                
                foreach (StaticEntitiesGroup group in staticEntitiesGroups)
                {
                    group.UpdateViewsCount(views.Count);
                }
                
                // If it's not null, inform the location script about the view count change
                if (mainScene.currentLocationScript != null)
                {
                    mainScene.currentLocationScript.UpdateViewsCount(views.Count);
                }

                UpdateViewsLabel();
            }
            finally
            {
                MainScene.SetReadyState(true);
            }
        }

        void UpdateViewsLabel()
        {
            // Debug
            Debug.Log("[MainScene] Views: " + views.Count);
        }

        public void ActivateSwimMode()
        {
            cameraRig.GetComponent<SimulationModeCameraRig>().Activate();
            UpdateHudVisibility();
        }

        public void DectivateSwimMode()
        {
            cameraRig.GetComponent<SimulationModeCameraRig>().Deactivate();
            UpdateHudVisibility();
        }
        
        public void ActivateAutomaticCameraMode()
        {
            isAutomaticCameraModeActive = true;
            mainScene.mainCamera.transform.parent = mainScene.currentLocationScript.dollyCart.transform;
            mainScene.mainCamera.transform.localPosition = Vector3.zero;
            mainScene.mainCamera.transform.localRotation = Quaternion.identity;
            UpdateHudVisibility();
            UnityEngine.Cursor.visible = false;
            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        }
        
        public void DectivateAutomaticCameraMode()
        {
            isAutomaticCameraModeActive = false;
            mainScene.mainCamera.transform.parent = cameraRig.transform;
            mainScene.mainCamera.transform.localPosition = Vector3.zero;
            mainScene.mainCamera.transform.localRotation = Quaternion.identity;
            UpdateHudVisibility();
            UnityEngine.Cursor.visible = true;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
        }

        public void LoadLocationAndUpdateGUIState(string locationName)
        {
            if (cameraRig != null)
            {
                var cameraRigController = cameraRig.GetComponent<SimulationModeCameraRig>();
                if (cameraRigController != null)
                {
                    cameraRigController.ResetPositionAndRotation();
                }
            }
            // Temporarily unregister the callback using the stored reference
            locationsDropdownField.UnregisterCallback(locationChangeCallback);
            
            // Update the dropdown value
            locationsDropdownField.value = locationName;
            
            // Re-register the callback using the stored reference
            locationsDropdownField.RegisterCallback(locationChangeCallback);
            
            mainScene.LoadLocation(locationName);
        }

        public override bool OnEscapePressed()
        {
            if (cameraRig != null && cameraRig.GetComponent<SimulationModeCameraRig>().isActive)
            {
                DectivateSwimMode();
                return true;
            }
            return false;
        }
        
        public override void SetHudless(bool hudless)
        {
            isHudless = hudless;
            UpdateHudVisibility();
        }

        private void UpdateHudVisibility()
        {
            bool hideHud = false;
            
            if (isHudless)
            {
                hideHud = true;
            }

            if (cameraRig != null)
            {
                var rig = cameraRig.GetComponent<SimulationModeCameraRig>();
                if (rig != null)
                {
                    if (rig.isActive)
                    {
                        hideHud = true;
                    }
                }
            }

            if (isAutomaticCameraModeActive)
            {
                hideHud = true;
            }

            if (mainGui != null)
            {
                if (hideHud)
                {
                    mainGui.style.opacity = 0;
                }
                else
                {
                    mainGui.style.opacity = 1;
                }
            }
        }

        public StaticEntitiesGroupComponent GetStaticEntitiesGroupComponent(string groupName)
        {
            var group = staticEntitiesGroups.Find(g => g.name == groupName);
            if (group != null && group.staticEntitiesGroupStructs.Count > 0)
            {
                var entity = group.staticEntitiesGroupStructs[0].StaticEntitiesGroupEntity;
                if (entityManager.Exists(entity) && entityManager.HasComponent<StaticEntitiesGroupComponent>(entity))
                {
                    return entityManager.GetComponentData<StaticEntitiesGroupComponent>(entity);
                }
            }
            // Return a default or throw an exception if not found
            Debug.LogWarning($"[SimulationModeManager] Static entity group component for '{groupName}' not found.");
            return default;
        }
    }
} 