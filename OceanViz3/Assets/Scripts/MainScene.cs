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

namespace OceanViz3 {

/// <summary>
/// Main controller class for the ocean visualization scene. Handles initialization, UI management,
/// entity group management, location management, and camera controls.
/// </summary>
public class MainScene : MonoBehaviour
{
	/// <summary>
	/// Indicates whether the MainScene is fully initialized and ready for operations. Used by the SimulationAPI to check if the scene is ready to receive state updates.
	/// </summary>
	public static bool IsReady { get; private set; }
	/// <summary>
	/// Maximum number of simultaneous views supported by the application.
	/// </summary>
	private const int MaxViews = 4;
	public SimulationAPI simulationAPI;
	public LocationScript currentLocationScript;
	public String currentLocationName;
	public List<String> locationNames = new List<String>();
	/// <summary>
	/// List of active views in the scene.
	/// </summary>
	public List<View> views = new List<View>();
	
	/// <summary>
	/// Collection of location presets containing environment-specific settings like water turbidity colors.
	/// </summary>
	public LinkedList<KeyValuePair<string, LocationPreset>> locationPresets;

	/// <summary>
	/// List of active dynamic entity groups (e.g., fish schools) in the scene.
	/// </summary>
	[SerializeField]
	public List<DynamicEntitiesGroup> dynamicEntitiesGroups = new List<DynamicEntitiesGroup>();
	
	/// <summary>
	/// Unique identifier counter for dynamic entity groups.
	/// </summary>
	private int nextDynamicEntityGroupId = 0;
	
	/// <summary>
	/// List of active static entity groups (e.g., coral, seaweed) in the scene.
	/// </summary>
	[SerializeField]
	public List<StaticEntitiesGroup> staticEntitiesGroups = new List<StaticEntitiesGroup>();

	//// UI
	public GameObject UIDocument;
	private VisualElement gui;
	private DropdownField addDynamicRowDropdownField;
	private DropdownField addStaticRowDropdownField;
	private DropdownField locationsDropdownField;
	private VisualElement addDynamicRowButton;
	private VisualElement addStaticRowButton;
	public VisualTreeAsset DataRow;
	public VisualTreeAsset StaticGroupDataRow;
	
	//// Game Objects
	public GameObject templateTerrain;
	public GameObject mainCamera;
	public GameObject cameraRig;
	public GameObject currentLocationGameObject;
	
	//// Rendering
	/// <summary>
	/// Universal Render Pipeline (URP) asset configuration. Used to enable/disable renderer features.
	/// </summary>
	public UniversalRendererData urpAsset;

	//// Entity Component System
	private World world;
	private EntityManager entityManager;
	
	//// Events
	private EventCallback<ChangeEvent<string>> locationChangeCallback;

	private void Awake()
	{
		world = World.DefaultGameObjectInjectionWorld;
		entityManager = world.EntityManager;
	}
	
	private void Start()
	{
		Debug.Log("[MainScene] Start");
		
		IsReady = false;

		//// Rendering
		// Enable each Renderer Feature
		foreach (var feature in urpAsset.rendererFeatures)
		{
			feature.SetActive(true);
		}

		//// UI
		gui = UIDocument.GetComponent<UIDocument>().rootVisualElement;
		
		//// Locations
		// Read StreamingAssets/Locations folder to populate the locationNames list
		UpdateLocationPresets();
		
		// LocationsDropdownField
		locationsDropdownField = gui.Q<DropdownField>("LocationsDropdownField");
		for (int i = 0; i < locationNames.Count; i++)
		{
			locationsDropdownField.choices.Add(locationNames[i]);
		}
		
		// Set the default value of the dropdown to the first element of the choices list
		locationsDropdownField.value = locationsDropdownField.choices[0];
		
		// Load the first location scene
		SceneManager.LoadScene(locationsDropdownField.choices[0], LoadSceneMode.Additive);
		currentLocationName = locationsDropdownField.choices[0];
		
		// Store the callback
		locationChangeCallback = (evt) => OnLocationLocationDropdownFieldChanged(evt.newValue);
		locationsDropdownField.RegisterCallback(locationChangeCallback);
		
		//// Presets
		if (DataRow == null){Debug.LogError("[MainScene] DataRow is null");}
		if (StaticGroupDataRow == null){Debug.LogError("[MainScene] DataRow is null");}

		// Read StreamingAssets folder to populate the presets lists
		GroupPresetsManager.Instance.UpdatePresets();
		
		addDynamicRowDropdownField = gui.Q<DropdownField>("AddDynamicRowDropdownField");
		addStaticRowDropdownField = gui.Q<DropdownField>("AddStaticRowDropdownField");

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
		addDynamicRowButton = gui.Q<Button>("AddDynamicRowButton");
		addDynamicRowButton.RegisterCallback<ClickEvent>((evt) => SpawnSelectedDynamicPreset());
		addStaticRowButton = gui.Q<Button>("AddStaticRowButton");
		addStaticRowButton.RegisterCallback<ClickEvent>((evt) => SpawnSelectedStaticPreset());

		// ViewCount buttons callbacks
		gui.Q<Button>("SetViews1").RegisterCallback<ClickEvent>((evt) => SetViewCountAndUpdateGUIState(1));
		gui.Q<Button>("SetViews2").RegisterCallback<ClickEvent>((evt) => SetViewCountAndUpdateGUIState(2));
		gui.Q<Button>("SetViews3").RegisterCallback<ClickEvent>((evt) => SetViewCountAndUpdateGUIState(3));
		gui.Q<Button>("SetViews4").RegisterCallback<ClickEvent>((evt) => SetViewCountAndUpdateGUIState(4));

		// Add first view
		SetViewCountAndUpdateGUIState(1);

		// Swim mode
		gui.Q<Button>("ActivateSwimModeButton").RegisterCallback<ClickEvent>((evt) => ActivateSwimMode());
		
		// Automatic camera mode
		gui.Q<Button>("ActivateAutomaticCameraModeButton").RegisterCallback<ClickEvent>((evt) => ActivateAutomaticCameraMode());
		
		StartCoroutine(WaitForECSInitialization());
    }

    /// <summary>
    /// Initializes ECS components and waits for the world to be ready.
    /// </summary>
    private IEnumerator WaitForECSInitialization()
    {
        // Wait for the DefaultWorld to be created
        while (World.DefaultGameObjectInjectionWorld == null)
        {
            yield return null;
        }

        world = World.DefaultGameObjectInjectionWorld;

        // Wait for the EntityManager to be available
        while (world.EntityManager == null)
        {
            yield return null;
        }

        entityManager = world.EntityManager;

        // Wait for any systems to complete their initialization
        yield return null;

        // ECS world is now initialized
        IsReady = true;
        Debug.Log("[MainScene] ECS World initialized. MainScene is now ready.");

        simulationAPI.Setup(this);
    }
	
	private struct TempLocationPreset
	{
		public string name;
		public string turbidity_low_color;
		public string turbidity_high_color;
	}
	
	/// <summary>
	/// Updates the collection of available location presets from the StreamingAssets folder.
	/// </summary>
	private void UpdateLocationPresets()
	{
		IsReady = false;
		
		locationPresets = new LinkedList<KeyValuePair<string, LocationPreset>>();
		string[] locationFolders = Directory.GetDirectories(Path.Combine(Application.streamingAssetsPath, "Locations"));
		foreach (string folder in locationFolders)
		{
			var jsonPath = Path.Combine(Application.streamingAssetsPath, "Locations", folder, "location_properties.json");
			if (!File.Exists(jsonPath))
			{
				Debug.LogError($"[MainScene] location_properties.json not found in {jsonPath}");
				continue;
			}
			string json = File.ReadAllText(jsonPath);
			try
			{
				TempLocationPreset tempPreset = JsonUtility.FromJson<TempLocationPreset>(json);

				// Convert hex strings to Color
				LocationPreset locationPreset = new LocationPreset
				{
					name = tempPreset.name,
					turbidity_low_color = HexToColor(tempPreset.turbidity_low_color),
					turbidity_high_color = HexToColor(tempPreset.turbidity_high_color)
				};

				locationPresets.AddLast(new KeyValuePair<string, LocationPreset>(tempPreset.name, locationPreset));
			}
			catch (Exception e)
			{
				Debug.LogError($"[MainScene] Invalid JSON in {jsonPath}: {e.Message}");
			}
		}
		
		IsReady = true;
	}

	/// <summary>
	/// Converts a hexadecimal color string to a Unity Color object.
	/// </summary>
	/// <param name="hex">Hexadecimal color string (format: "0xRRGGBB" or "#RRGGBB")</param>
	/// <returns>Unity Color object</returns>
	private Color HexToColor(string hex)
	{
		hex = hex.Replace("0x", "").Replace("#", "");
		byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
		byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
		byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
		return new Color32(r, g, b, 255);
	}

	private void OnLocationLocationDropdownFieldChanged(string locationName)
	{
		IsReady = false;
		try
		{
			UnloadLocation();
			LoadLocation(locationName);
		}
		finally
		{
			IsReady = true;
		}
	}
	
	public void UnloadLocation()
	{
		IsReady = false;
		Debug.Log("[MainScene] Unloading location: " + currentLocationName);
		SceneManager.UnloadSceneAsync(currentLocationName);
	}
	
	public void LoadLocationAndUpdateGUIState(string locationName)
	{
		// Temporarily unregister the callback using the stored reference
		locationsDropdownField.UnregisterCallback(locationChangeCallback);
		
		// Update the dropdown value
		locationsDropdownField.value = locationName;
		
		// Re-register the callback using the stored reference
		locationsDropdownField.RegisterCallback(locationChangeCallback);
		
		LoadLocation(locationName);
	}

	/// <summary>
	/// Loads a new location and updates all entity groups to work with the new environment.
	/// </summary>
	/// <param name="locationName">Name of the location to load</param>
	public void LoadLocation(string locationName)
	{
		IsReady = false;
		
		Debug.Log("[MainScene] Location changing to: " + locationName);

		// Delete all the entities belonging to the previous location
		// - Obstacle entities
		EntityQuery obstacleQuery = entityManager.CreateEntityQuery(typeof(BoidObstacle));
		NativeArray<Entity> obstacleEntities = obstacleQuery.ToEntityArray(Allocator.Temp);
		foreach (Entity obstacleEntity in obstacleEntities)
		{
			entityManager.DestroyEntity(obstacleEntity);
		}

		// Load the new location scene
		var loadOperation = SceneManager.LoadSceneAsync(locationName, LoadSceneMode.Additive);
		loadOperation.completed += (asyncOperation) => {
			currentLocationName = locationName;
			
			// Wait for OnLocationLoaded to complete
			StartCoroutine(WaitForLocationSetup(locationName));
		};
	}

	private IEnumerator WaitForLocationSetup(string locationName)
	{
		// Wait until currentLocationScript is set and initialized
		while (currentLocationScript == null || !currentLocationScript.isActiveAndEnabled)
		{
			yield return null;
		}

		Debug.Log($"[MainScene] Location {locationName} is ready, updating entity groups");
		
		// After the new location is loaded, update all dynamic entities groups
		foreach (DynamicEntitiesGroup group in dynamicEntitiesGroups)
		{
			// Get new boid bounds for the group's habitat
			List<GameObject> newBoidBounds = currentLocationScript.GetBoidBoundsByBiomeName(group.dynamicEntityPreset.habitat);
			if (newBoidBounds == null || newBoidBounds.Count == 0)
			{
				Debug.LogWarning($"[MainScene] No boid bounds found for habitat: {group.dynamicEntityPreset.habitat} in new location");
				continue;
			}
			
			// Update the group with new bounds
			group.UpdateBoidBounds(newBoidBounds);
		}

		// Update all static entities groups
		foreach (StaticEntitiesGroup group in staticEntitiesGroups)
		{
			// Get new terrain and splatmap for the group
			Terrain newTerrain = currentLocationScript.GetTerrain();
			if (newTerrain == null)
			{
				Debug.LogError($"[MainScene] No terrain found in location {locationName}. Terrain reference might be missing in LocationScript.");
				continue;
			}
			
			Texture2D newBiomeSplatmap = currentLocationScript.GetFloraBiomeSplatmap(group.staticEntityPreset.habitats[0]); // TODO: Handle multiple habitats
			if (newBiomeSplatmap == null)
			{
				Debug.LogWarning($"[MainScene] No splatmap found for habitat: {group.staticEntityPreset.habitats[0]} in new location");
				continue;
			}

			// Update the group with new terrain and splatmap
			group.UpdateTerrainAndSplatmap(newTerrain, newBiomeSplatmap);
		}
		
		IsReady = true;
	}

	public void OnLocationLoaded(LocationScript loadedLocationScript)
	{
		Debug.Log("[MainScene] Location loaded: " + loadedLocationScript.gameObject.scene.name);
		
		currentLocationScript = loadedLocationScript;
		currentLocationGameObject = loadedLocationScript.gameObject;
		
		// Find the LocationPreset for the current location
		var presetPair = locationPresets.FirstOrDefault(kvp => kvp.Key == currentLocationName);
		if (presetPair.Equals(default(KeyValuePair<string, LocationPreset>)))
		{
			Debug.LogError($"[MainScene] LocationPreset not found for {currentLocationName}. Setup aborted.");
			return;
		}
		
		LocationPreset currentPreset = presetPair.Value;
		currentLocationScript.Setup(gui.Q<VisualElement>("TurbidityRow"), currentPreset);
		
		IsReady = true;
	}
	
	public void SpawnSelectedDynamicPreset()
	{
		SpawnDynamicPreset(addDynamicRowDropdownField.value);
	}
	
	/// <summary>
	/// Creates and spawns a new dynamic entity group (e.g., fish school) based on the specified preset.
	/// </summary>
	/// <param name="name">Name of the preset to spawn</param>
	public async void SpawnDynamicPreset(string name)
	{
		IsReady = false;
		if (GroupPresetsManager.Instance == null)
		{
			Debug.LogError("[MainScene] GroupPresetsManager.Instance is null. Make sure it's properly initialized.");
			return;
		}
		
		Debug.Log($"[MainScene] Attempting to spawn dynamic preset: {name}");
		DynamicEntityPreset selectedPreset = GroupPresetsManager.Instance.dynamicEntitiesPresetsList.Find(flockPreset => flockPreset.name == name);
		
		if (selectedPreset == null)
		{
			Debug.LogError($"[MainScene] Error: Dynamic preset '{name}' not found in the presets list.");
			return;
		}

		VisualElement dataRow = DataRow.CloneTree();
		gui.Q<VisualElement>("DataRows").Add(dataRow);
		
		List<GameObject> filteredBoidBounds = currentLocationScript.GetBoidBoundsByBiomeName(selectedPreset.habitat);
		if (filteredBoidBounds == null || filteredBoidBounds.Count == 0)
		{
			Debug.LogWarning($"[MainScene] No boid bounds found for habitat: {selectedPreset.habitat}");
		}
		else
		{
			Debug.Log($"[MainScene] Found {filteredBoidBounds.Count} boid bounds for habitat: {selectedPreset.habitat}");
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
			gui.Q<VisualElement>("DataRows").Remove(dataRow);
			return;
		}
		finally
		{
			IsReady = true;
			Debug.Log("[MainScene] SpawnDynamicPreset completed, IsReady set to true");
		}
	}
	
	public void SpawnSelectedStaticPreset()
	{
		SpawnStaticPreset(addStaticRowDropdownField.value, addStaticRowDropdownField.value);
	}
	
	/// <summary>
	/// Creates and spawns a new static entity group (e.g., coral formation) based on the specified preset.
	/// </summary>
	/// <param name="presetName">Name of the preset to use</param>
	/// <param name="groupName">Name for the new group instance</param>
	public void SpawnStaticPreset(string presetName, string groupName)
	{
		IsReady = false;
		try
		{
			StaticEntityPreset? staticEntityPreset = null;
			bool presetFound = false;

			// Find the selected preset
			foreach (var preset in GroupPresetsManager.Instance.staticEntitiesPresetsList)
			{
				if (preset.name == presetName)
				{
					staticEntityPreset = preset;
					presetFound = true;
					break;
				}
			}

			if (!presetFound)
			{
				string errorMessage = $"[MainScene] Error: StaticEntitiesGroup preset '{addStaticRowDropdownField.value}' not found in the floraPresets list.";
				Debug.LogError(errorMessage);
				return;
			}
			
			Debug.Log("[MainScene] Adding staticEntitiesGroup: " + staticEntityPreset.name);

			// Add a new DataRow to the UIDocument
			VisualElement dataRow = StaticGroupDataRow.CloneTree();
			gui.Q<VisualElement>("StaticDataRows").Add(dataRow);
			dataRow.Q<Slider>("Slider").label = groupName;
			
			// Get terrain from the current location
			Terrain terrain = currentLocationScript.GetTerrain();
			
			// Get splatmap texture from the current location
			Texture2D biomeSplatmap = currentLocationScript.GetFloraBiomeSplatmap(staticEntityPreset.habitats[0]); // TODO: Handle multiple habitats
			if (biomeSplatmap == null)
			{
				Debug.LogError("[MainScene] Error: Splatmap not found for habitat " + staticEntityPreset.habitats[0]);
				return;
			}
			
			// StaticEntitiesGroup
			StaticEntitiesGroup staticEntitiesGroup = new StaticEntitiesGroup();
			staticEntitiesGroup.Setup(
				groupName: groupName,
				presetName: presetName,
				staticEntityPreset: staticEntityPreset,
				dataRow: dataRow,
				viewsCount: views.Count,
				terrain: terrain,
				splatmap: biomeSplatmap);
			
			try
			{
				staticEntitiesGroup.LoadAndSpawnStaticGroup();
				staticEntitiesGroups.Add(staticEntitiesGroup);
				staticEntitiesGroup.UpdateViewsCount(views.Count);
				staticEntitiesGroup.OnDeleteRequested += HandleStaticGroupDeleteRequest;
			}
			catch (Exception e)
			{
				Debug.LogError($"[MainScene] Error loading and spawning staticEntitiesGroup: {e.Message}");
				gui.Q<VisualElement>("StaticDataRows").Remove(dataRow);
			}
		}
		finally
		{
			IsReady = true;
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

    void Update()
	{
		// Pressing RMB or Escape button if the camera swim mode is active deactivates it
		if ((Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)))
		{
			// If the camera swim mode is active, deactivate it
			if (cameraRig.GetComponent<CameraRig>().isActive)
			{
				DectivateSwimMode();
			}
				
			// Check if the camera is parented to the dolly cart
			if (mainCamera.transform.parent == currentLocationScript.dollyCart.transform)
			{
				// If it is, deactivate the automatic camera mode
				DectivateAutomaticCameraMode();
			}
		}
	}
	
	/// <summary>
	/// Updates the number of active views and adjusts the UI accordingly.
	/// </summary>
	/// <param name="viewCount">Desired number of views (1-4)</param>
	public void SetViewCountAndUpdateGUIState(int viewCount)
	{
		IsReady = false;
		try
		{
			// Dark gray color
			StyleColor defaultColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));

			// Selected cyan color
			StyleColor selectedColor = new StyleColor(new Color(0.0f, 0.8f, 1.0f));

			// Reset the background color of the view count buttons to default
			gui.Q<Button>("SetViews1").style.backgroundColor = defaultColor;
			gui.Q<Button>("SetViews2").style.backgroundColor = defaultColor;
			gui.Q<Button>("SetViews3").style.backgroundColor = defaultColor;
			gui.Q<Button>("SetViews4").style.backgroundColor = defaultColor;

			// Change the background color of the correct view count button to blue to indicate the current view count
			if (viewCount == 1)
			{
				gui.Q<Button>("SetViews1").style.backgroundColor = selectedColor;
			}
			else if (viewCount == 2)
			{
				gui.Q<Button>("SetViews2").style.backgroundColor = selectedColor;
			}
			else if (viewCount == 3)
			{
				gui.Q<Button>("SetViews3").style.backgroundColor = selectedColor;
			}
			else if (viewCount == 4)
			{
				gui.Q<Button>("SetViews4").style.backgroundColor = selectedColor;
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
			IsReady = true;
		}
	}

	private void AddView()
	{
		IsReady = false;
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
			if (currentLocationScript != null)
			{
				currentLocationScript.UpdateViewsCount(views.Count);
			}

			UpdateViewsLabel();
		}
		finally
		{
			IsReady = true;
		}
	}

	private void RemoveView()
	{
		IsReady = false;
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
			if (currentLocationScript != null)
			{
				currentLocationScript.UpdateViewsCount(views.Count);
			}

			UpdateViewsLabel();
		}
		finally
		{
			IsReady = true;
		}
	}

	void UpdateViewsLabel()
	{
		// Debug
		Debug.Log("[MainScene] Views: " + views.Count);
	}

	/// <summary>
	/// Activates swim mode, allowing free camera movement and hiding the UI.
	/// </summary>
	public void ActivateSwimMode()
	{
		cameraRig.GetComponent<CameraRig>().Activate();

		// Set the GUI opacity to 0
		gui.style.opacity = 0;
	}

	/// <summary>
	/// Deactivates swim mode, restoring the UI and restricting camera movement.
	/// </summary>
	public void DectivateSwimMode()
	{
		cameraRig.GetComponent<CameraRig>().Deactivate();

		// Set the GUI opacity to 1
		gui.style.opacity = 1;
	}
	
	/// <summary>
	/// Activates automatic camera mode, attaching the camera to a predefined path.
	/// </summary>
	public void ActivateAutomaticCameraMode()
	{
		// Parent the main camera to the dolly cart
		mainCamera.transform.parent = currentLocationScript.dollyCart.transform;
		
		// Reset the main camera's position and rotation so it's centered on the dolly cart and looking forward
		mainCamera.transform.localPosition = Vector3.zero;
		mainCamera.transform.localRotation = Quaternion.identity;

		// Set the GUI opacity to 0
		gui.style.opacity = 0;
		
		// Hide the cursor and lock it
		UnityEngine.Cursor.visible = false;
      UnityEngine.Cursor.lockState = CursorLockMode.Locked;
	}
	
	/// <summary>
	/// Deactivates automatic camera mode, returning the camera to the camera rig.
	/// </summary>
	public void DectivateAutomaticCameraMode()
	{
		// Return the main camera to the camera rig
		mainCamera.transform.parent = cameraRig.transform;
		
		// Reset the main camera's position and rotation so it's centered on the camera rig and looking forward
		mainCamera.transform.localPosition = Vector3.zero;
		mainCamera.transform.localRotation = Quaternion.identity;

		// Set the GUI opacity to 1
		gui.style.opacity = 1;
		
		// Show the cursor and unlock it
		UnityEngine.Cursor.visible = true;
		UnityEngine.Cursor.lockState = CursorLockMode.None;
	}
}
}
