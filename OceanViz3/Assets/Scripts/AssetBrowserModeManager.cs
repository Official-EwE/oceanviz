using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using System.Linq;
using System.Threading.Tasks;
using System.Collections;

namespace OceanViz3
{
    public class AssetBrowserModeManager : AppModeManager
    {
        [Header("UI")]
        public GameObject mainGUIUIDocument;

        [Header("Scene Objects")]
        public GameObject cameraRig;

        private bool menuButtonInitialized = false;
        private bool reloadButtonInitialized = false;
        private bool nextButtonInitialized = false;
        private bool previousButtonInitialized = false;
        private bool locationsDropdownInitialized = false;

        private DynamicEntitiesGroup currentDynamicGroup;
        private StaticEntitiesGroup currentStaticGroup;
        private EventCallback<ChangeEvent<string>> assetPickerCallback;
        private DropdownField locationsDropdownField;
        private EventCallback<ChangeEvent<string>> locationChangeCallback;

        private VisualElement root;
        private bool isHudless;

        private const int ASSET_BROWSER_DYNAMIC_ENTITY_ID = -100;
        private const int ASSET_BROWSER_STATIC_ENTITIES_GROUP_ID = -101;

        private EntityManager entityManager;
        private Entity trackedEntity = Entity.Null;
        private EntityQuery boidQuery;
        private EntityQuery staticEntityQuery;

        public override void Setup(MainScene mainScene)
        {
            base.Setup(mainScene);
            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            boidQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<LocalToWorld>(),
                ComponentType.ReadOnly<BoidShared>()
            );
            staticEntityQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<LocalToWorld>(),
                ComponentType.ReadOnly<StaticEntityShared>()
            );

            // Ensure Asset-Browser camera is disabled on application start (we start in Simulation mode)
            if (cameraRig != null)
            {
                cameraRig.SetActive(false);
                var cam = cameraRig.GetComponentInChildren<Camera>(true);
                if (cam != null) cam.enabled = false;
                var al = cam != null ? cam.GetComponent<AudioListener>() : null;
                if (al != null) al.enabled = false;

                // Pass UIDocument to camera rig for UI hit testing
                var rig = cameraRig.GetComponent<AssetBrowserCameraRig>();
                if (rig != null && mainGUIUIDocument != null)
                {
                    var uiDoc = mainGUIUIDocument.GetComponent<UIDocument>();
                    rig.SetUIDocument(uiDoc);
                }
            }

            // Hide Asset-Browser GUI until the mode is entered
            if (mainGUIUIDocument != null)
            {
                mainGUIUIDocument.SetActive(false);
                TryEnsureRoot();
            }
        }

        private bool TryEnsureRoot()
        {
            if (root != null)
            {
                return true;
            }

            if (mainGUIUIDocument == null)
            {
                return false;
            }

            var uiDoc = mainGUIUIDocument.GetComponent<UIDocument>();
            if (uiDoc == null)
            {
                return false;
            }

            root = uiDoc.rootVisualElement;
            return root != null;
        }

        public override void OnUpdate()
        {
            // If our tracked entity was destroyed, we should clear our reference to it.
            if (trackedEntity != Entity.Null && !entityManager.Exists(trackedEntity))
            {
                trackedEntity = Entity.Null;
            }
        
            // If we have a valid tracked entity, update the camera rig's position.
            if (trackedEntity != Entity.Null)
            {
                var position = entityManager.GetComponentData<LocalToWorld>(trackedEntity).Position;
                if (cameraRig != null)
                {
                    cameraRig.transform.position = position;
                }
            }
        }

        private void FindTrackedDynamicEntity()
        {
            entityManager.GetAllUniqueSharedComponents(out NativeList<BoidShared> uniqueBoidTypes, Allocator.Temp);
            try
            {
                BoidShared targetBoidShared = default;
                bool found = false;
                foreach (var boidShared in uniqueBoidTypes)
                {
                    if (boidShared.DynamicEntityId == ASSET_BROWSER_DYNAMIC_ENTITY_ID)
                    {
                        targetBoidShared = boidShared;
                        found = true;
                        break;
                    }
                }
        
                if (found)
                {
                    boidQuery.SetSharedComponentFilter(targetBoidShared);
                    using (var entities = boidQuery.ToEntityArray(Allocator.Temp))
                    {
                        if (entities.Length > 0)
                        {
                            trackedEntity = entities[0];
                            Debug.Log($"[AssetBrowserModeManager] Started tracking dynamic entity.");
                        }
                    }
                    boidQuery.ResetFilter();
                }
            }
            finally
            {
                uniqueBoidTypes.Dispose();
            }
        }
        
        private void FindTrackedStaticEntity()
        {
            var uniqueStaticTypes = new NativeList<StaticEntityShared>(Allocator.TempJob);
            try
            {
                entityManager.GetAllUniqueSharedComponents(out uniqueStaticTypes, Allocator.TempJob);
                StaticEntityShared targetStaticShared = default;
                bool found = false;
                foreach (var staticShared in uniqueStaticTypes)
                {
                    if (staticShared.StaticEntitiesGroupId == ASSET_BROWSER_STATIC_ENTITIES_GROUP_ID)
                    {
                        targetStaticShared = staticShared;
                        found = true;
                        break;
                    }
                }
        
                if (found)
                {
                    staticEntityQuery.SetSharedComponentFilter(targetStaticShared);
                    using (var entities = staticEntityQuery.ToEntityArray(Allocator.TempJob))
                    {
                        if (entities.Length > 0)
                        {
                            trackedEntity = entities[0];
                            Debug.Log($"[AssetBrowserModeManager] Started tracking static entity.");
                        }
                    }
                    staticEntityQuery.ResetFilter();
                }
            }
            finally
            {
                uniqueStaticTypes.Dispose();
            }
        }

        private async Task SpawnEntityGroup(string presetName)
        {
            // First, try to get it as a dynamic preset
            var dynamicPreset = GroupPresetsManager.Instance.GetDynamicPresetByName(presetName);
            if (dynamicPreset != null)
            {
                await SpawnDynamicEntity(presetName);
                return;
            }

            // If not found, try as a static preset
            var staticPreset = GroupPresetsManager.Instance.GetStaticPresetByName(presetName);
            if (staticPreset != null)
            {
                await SpawnStaticEntity(presetName);
                return;
            }

            Debug.LogError($"[AssetBrowserModeManager] Preset '{presetName}' not found as dynamic or static.");
        }

        private async Task SpawnDynamicEntity(string name)
        {
            var selectedPreset = GroupPresetsManager.Instance.GetDynamicPresetByName(name);
            if (selectedPreset == null) return;

            // This logic is copied from SimulationModeManager, but without creating a UI DataRow.
            // Get boid bounds for all habitats
            List<GameObject> filteredBoidBounds = new List<GameObject>();
            if (mainScene.currentLocationScript != null)
            {
                foreach (string habitat in selectedPreset.habitats)
                {
                    if (string.IsNullOrEmpty(habitat)) continue;

                    var habitatBounds = mainScene.currentLocationScript.GetBoidBoundsByBiomeName(habitat);
                    if (habitatBounds != null && habitatBounds.Count > 0)
                    {
                        filteredBoidBounds.AddRange(habitatBounds);
                    }
                }
            }
            
            DynamicEntitiesGroup dynamicEntitiesGroup = new DynamicEntitiesGroup();
            // Passing null for dataRow as we don't want UI for this group.
            // Assuming the group handles a null dataRow gracefully.
            dynamicEntitiesGroup.Setup(
                name: selectedPreset.name,
                dynamicEntityId: ASSET_BROWSER_DYNAMIC_ENTITY_ID, // ID is not critical here as we only have one group
                dynamicEntityPreset: selectedPreset,
                dataRow: null, 
                viewsCount: 1,
                boidBounds: filteredBoidBounds
            );

            await dynamicEntitiesGroup.LoadAndSpawnGroup();
            dynamicEntitiesGroup.SetPopulationAndUpdateGUIState(1); // Set population to 1

            currentDynamicGroup = dynamicEntitiesGroup;

            // We need to add this group to the main list in SimulationModeManager so it gets updated
            mainScene.simulationModeManager.dynamicEntitiesGroups.Add(currentDynamicGroup);
            currentDynamicGroup.OnDeleteRequest += (group) => mainScene.simulationModeManager.dynamicEntitiesGroups.Remove(group);
        }

        private async Task SpawnStaticEntity(string name)
        {
            var selectedPreset = GroupPresetsManager.Instance.GetStaticPresetByName(name);
            if (selectedPreset == null) return;
            
            StaticEntitiesGroup staticEntitiesGroup = new StaticEntitiesGroup();
            // Passing null for dataRow as we don't want UI for this group.
            // Assuming the group handles a null dataRow gracefully.
            staticEntitiesGroup.Setup(
                name: selectedPreset.name,
                dataRow: null, 
                viewsCount: 1,
                staticEntitiesGroupId: ASSET_BROWSER_STATIC_ENTITIES_GROUP_ID,
                staticEntityPreset: selectedPreset
            );

            await staticEntitiesGroup.LoadAndSpawnGroup();
            // For static entities, "population 1" is best represented by a low count.
            // A group with a count > 0 will spawn entities.
            staticEntitiesGroup.SetPopulationAndUpdateGUIState(1);

            currentStaticGroup = staticEntitiesGroup;

            // We need to add this group to the main list in SimulationModeManager so it gets updated
            mainScene.simulationModeManager.staticEntitiesGroups.Add(currentStaticGroup);
            currentStaticGroup.OnDeleteRequest += (group) => mainScene.simulationModeManager.staticEntitiesGroups.Remove(group);
        }

        private async Task RemoveCurrentEntityGroup()
        {
            Entity entityToTrack = Entity.Null;
            if (currentDynamicGroup != null)
            {
                if(currentDynamicGroup.boidSchoolStructs.Any())
                    entityToTrack = currentDynamicGroup.boidSchoolStructs[0].boidSchoolEntity;
                
                currentDynamicGroup.DeleteGroup();
                currentDynamicGroup = null;
            }
            else if (currentStaticGroup != null)
            {
                if(currentStaticGroup.staticEntitiesGroupStructs.Any())
                    entityToTrack = currentStaticGroup.staticEntitiesGroupStructs[0].StaticEntitiesGroupEntity;
                
                currentStaticGroup.DeleteGroup();
                currentStaticGroup = null;
            }
            
            trackedEntity = Entity.Null;

            if (entityToTrack != Entity.Null)
            {
                while (entityManager.Exists(entityToTrack))
                {
                    await Task.Yield();
                }
            }
        }

        public StaticEntitiesGroup GetCurrentStaticGroup()
        {
            return currentStaticGroup;
        }

        private void ReloadCurrentEntityGroup(ClickEvent evt)
        {
            if (currentDynamicGroup != null)
            {
                Debug.Log("[AssetBrowserModeManager] Reloading dynamic entity group.");
                currentDynamicGroup.ReloadGroup(evt);
            }
            else if (currentStaticGroup != null)
            {
                Debug.Log("[AssetBrowserModeManager] Reloading static entity group.");
                currentStaticGroup.ReloadGroup(evt);
            }
            else
            {
                Debug.LogWarning("[AssetBrowserModeManager] Reload button clicked, but no entity group is currently loaded.");
            }
        }

        private void GoToNextAsset(ClickEvent evt)
        {
            var root = mainGUIUIDocument.GetComponent<UIDocument>().rootVisualElement;
            var assetPickerDropdown = root.Q<DropdownField>("AssetPickerDropdownField");

            if (assetPickerDropdown == null || assetPickerDropdown.choices.Count == 0) return;

            int currentIndex = assetPickerDropdown.choices.IndexOf(assetPickerDropdown.value);
            int nextIndex = (currentIndex + 1) % assetPickerDropdown.choices.Count;
            
            assetPickerDropdown.value = assetPickerDropdown.choices[nextIndex];
        }

        private void GoToPreviousAsset(ClickEvent evt)
        {
            var root = mainGUIUIDocument.GetComponent<UIDocument>().rootVisualElement;
            var assetPickerDropdown = root.Q<DropdownField>("AssetPickerDropdownField");

            if (assetPickerDropdown == null || assetPickerDropdown.choices.Count == 0) return;

            int currentIndex = assetPickerDropdown.choices.IndexOf(assetPickerDropdown.value);
            int previousIndex = (currentIndex - 1 + assetPickerDropdown.choices.Count) % assetPickerDropdown.choices.Count;

            assetPickerDropdown.value = assetPickerDropdown.choices[previousIndex];
        }

        private async void OnAssetSelected(string newAssetName)
        {
            await RemoveCurrentEntityGroup();
            await SpawnEntityGroup(newAssetName);
            
            StopAllCoroutines(); // Stop any previous search
            StartCoroutine(FindTrackedEntityAfterSpawn());
        }

        private IEnumerator FindTrackedEntityAfterSpawn()
        {
            float timeout = 5f;
            float timer = 0f;
            while (trackedEntity == Entity.Null && timer < timeout)
            {
                if (currentDynamicGroup != null)
                {
                    FindTrackedDynamicEntity();
                }
                else if (currentStaticGroup != null)
                {
                    FindTrackedStaticEntity();
                }
                
                if (trackedEntity != Entity.Null)
                {
                    Debug.Log("[AssetBrowserModeManager] Found tracked entity via coroutine.");
                    yield break; // Exit coroutine
                }

                timer += Time.deltaTime;
                yield return null;
            }
            if (trackedEntity == Entity.Null)
            {
                Debug.LogWarning("[AssetBrowserModeManager] Timed out finding tracked entity after spawn.");
            }
        }

        private void PopulateAssetPickerDropdown()
        {
            var root = mainGUIUIDocument.GetComponent<UIDocument>().rootVisualElement;
            var assetPickerDropdown = root.Q<DropdownField>("AssetPickerDropdownField");

            if (assetPickerDropdown == null)
            {
                Debug.LogError("[AssetBrowserModeManager] AssetPickerDropdownField not found in UI.");
                return;
            }

            if (assetPickerCallback != null)
            {
                assetPickerDropdown.UnregisterCallback(assetPickerCallback);
            }

            assetPickerDropdown.choices.Clear();

            // Populate with dynamic entities
            foreach (var dynamicPreset in GroupPresetsManager.Instance.dynamicEntitiesPresetsList)
            {
                assetPickerDropdown.choices.Add($"{dynamicPreset.name}");
            }

            // Populate with static entities
            foreach (var staticPreset in GroupPresetsManager.Instance.staticEntitiesPresetsList)
            {
                assetPickerDropdown.choices.Add($"{staticPreset.name}");
            }

            if (assetPickerDropdown.choices.Count > 0)
            {
                assetPickerDropdown.value = assetPickerDropdown.choices[0];
                OnAssetSelected(assetPickerDropdown.value);
            }

            assetPickerCallback = (evt) => OnAssetSelected(evt.newValue);
            assetPickerDropdown.RegisterCallback(assetPickerCallback);
        }

        public override void EnterMode()
        {
            // Set view count to 1
            mainScene.simulationModeManager.SetViewCountAndUpdateGUIState(1);

            // Delete all dynamic entity groups from simulation mode
            foreach (var group in mainScene.simulationModeManager.dynamicEntitiesGroups.ToList())
            {
                group.DeleteGroup();
            }

            // Delete all static entity groups from simulation mode
            foreach (var group in mainScene.simulationModeManager.staticEntitiesGroups.ToList())
            {
                group.DeleteGroup();
            }

            // Disable Simulation camera to avoid duplicate AudioListener
            if (mainScene.mainCamera != null)
            {
                mainScene.mainCamera.SetActive(false);
            }
            
            mainGUIUIDocument.SetActive(true);
            SetHudless(isHudless);
            
            PopulateAssetPickerDropdown();

            if (!menuButtonInitialized && mainGUIUIDocument != null)
            {
                var root = mainGUIUIDocument.GetComponent<UIDocument>().rootVisualElement;
                var menuButton = root.Q<Button>("MainMenuButton");
                if (menuButton != null)
                {
                    menuButton.RegisterCallback<ClickEvent>((evt) => this.mainScene.ToggleMainMenu());
                    menuButtonInitialized = true;
                }
            }
            
            if (!reloadButtonInitialized && mainGUIUIDocument != null)
            {
                var root = mainGUIUIDocument.GetComponent<UIDocument>().rootVisualElement;
                var reloadButton = root.Q<Button>("ReloadButton");
                if (reloadButton != null)
                {
                    reloadButton.RegisterCallback<ClickEvent>(ReloadCurrentEntityGroup);
                    reloadButtonInitialized = true;
                }
            }
            
            if (!nextButtonInitialized && mainGUIUIDocument != null)
            {
                var root = mainGUIUIDocument.GetComponent<UIDocument>().rootVisualElement;
                var nextButton = root.Q<Button>("NextButton");
                if (nextButton != null)
                {
                    nextButton.RegisterCallback<ClickEvent>(GoToNextAsset);
                    nextButtonInitialized = true;
                }
            }
            
            if (!previousButtonInitialized && mainGUIUIDocument != null)
            {
                var root = mainGUIUIDocument.GetComponent<UIDocument>().rootVisualElement;
                var previousButton = root.Q<Button>("PreviousButton");
                if (previousButton != null)
                {
                    previousButton.RegisterCallback<ClickEvent>(GoToPreviousAsset);
                    previousButtonInitialized = true;
                }
            }
            
            if (!locationsDropdownInitialized && mainGUIUIDocument != null)
            {
                var root = mainGUIUIDocument.GetComponent<UIDocument>().rootVisualElement;
                locationsDropdownField = root.Q<DropdownField>("LocationsDropdownField");
                if (locationsDropdownField != null)
                {
                    locationsDropdownField.choices.Clear();
                    foreach (var name in mainScene.locationNames)
                    {
                        locationsDropdownField.choices.Add(name);
                    }

                    if (mainScene.locationNames.Count > 0)
                    {
                        locationsDropdownField.value = mainScene.currentLocationName;
                    }

                    locationChangeCallback = (evt) => OnLocationDropdownFieldChanged(evt.newValue);
                    locationsDropdownField.RegisterCallback(locationChangeCallback);
                    locationsDropdownInitialized = true;
                }
            }
            
            if (cameraRig != null)
            {
                cameraRig.SetActive(true);
                // Ensure camera and AudioListener are enabled
                var cam = cameraRig.GetComponentInChildren<Camera>(true);
                if (cam != null) cam.enabled = true;
                var al = cam != null ? cam.GetComponent<AudioListener>() : null;
                if (al != null) al.enabled = true;
            }
        }

        public override void ExitMode()
        {
            RemoveCurrentEntityGroup();
            mainGUIUIDocument.SetActive(false);
            if (cameraRig != null)
            {
                // Disable AudioListener to prevent duplicates
                var cam = cameraRig.GetComponentInChildren<Camera>(true);
                if (cam != null) cam.enabled = false;
                var al = cam != null ? cam.GetComponent<AudioListener>() : null;
                if (al != null) al.enabled = false;
                cameraRig.SetActive(false);
            }
            // Re-enable main simulation camera
            if (mainScene.mainCamera != null)
            {
                mainScene.mainCamera.SetActive(true);
            }
        }

        public override void EnterMenu()
        {
            mainGUIUIDocument.SetActive(false);
        }

        public override void ExitMenu()
        {
            mainGUIUIDocument.SetActive(true);
            SetHudless(isHudless);
        }

        private void OnLocationDropdownFieldChanged(string locationName)
        {
            mainScene.SwitchLocation(locationName);
            if (locationsDropdownField != null)
            {
                locationsDropdownField.value = locationName;
            }
        }

        public override void OnLocationReady()
        {
            // When the location is ready, we need to respawn the current asset
            // to ensure it uses the new location's boid bounds, etc.
            var uiRoot = mainGUIUIDocument.GetComponent<UIDocument>().rootVisualElement;
            if (uiRoot == null) return;
            
            var assetPickerDropdown = uiRoot.Q<DropdownField>("AssetPickerDropdownField");
            if (assetPickerDropdown != null && !string.IsNullOrEmpty(assetPickerDropdown.value))
            {
                // OnAssetSelected will handle removing the old and spawning the new.
                OnAssetSelected(assetPickerDropdown.value);
            }
        }

        public override void SetHudless(bool hudless)
        {
            isHudless = hudless;

            if (!TryEnsureRoot())
            {
                Debug.Assert(false, "[AssetBrowserModeManager] UIDocument rootVisualElement is not available in SetHudless.");
                return;
            }

            if (isHudless)
            {
                root.style.opacity = 0;
            }
            else
            {
                root.style.opacity = 1;
            }
        }
    }
} 