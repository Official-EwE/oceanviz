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

	public static void SetReadyState(bool state)
	{
		IsReady = state;
	}

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
	private int nextStaticEntityGroupId = 0;
	
	/// <summary>
	/// List of active static entity groups (e.g., coral, seaweed) in the scene.
	/// </summary>
	[SerializeField]
	public List<StaticEntitiesGroup> staticEntitiesGroups = new List<StaticEntitiesGroup>();

	//// UI
	public GameObject mainMenuUIDocument;
	private VisualElement mainMenuRoot;
	private bool isMainMenuVisible = false;
	
	//// Game Objects
	public GameObject templateTerrain;
	public GameObject mainCamera;
	public GameObject currentLocationGameObject;
	
	//// Rendering
	/// <summary>
	/// Universal Render Pipeline (URP) asset configuration. Used to enable/disable renderer features.
	/// </summary>
	public UniversalRendererData urpAsset;

	//// Entity Component System
	private World world;
	private EntityManager entityManager;
	
	private NoiseTextureManager noiseTextureManager;

	private float lastEntitiesGraphicsStatsLogTime = 0f;

	// App Mode Management
	public AppMode currentMode { get; private set; }
	public SimulationModeManager simulationModeManager;
	public AssetBrowserModeManager assetBrowserModeManager;
	private AppModeManager currentModeManager;
	private bool isHudless;

	private void Awake()
	{
		world = World.DefaultGameObjectInjectionWorld;
		entityManager = world.EntityManager;

		// --- Ensure SceneData singleton entity exists ---
		var sceneDataQuery = entityManager.CreateEntityQuery(typeof(SceneData));
		if (sceneDataQuery.CalculateEntityCount() == 0)
		{
			// Use mainCamera position if available, otherwise default to zero
			float3 cameraPos = float3.zero;
			if (mainCamera != null)
			{
				cameraPos = mainCamera.transform.position;
			}
			Entity sceneDataEntity = entityManager.CreateEntity(typeof(SceneData));
			entityManager.SetComponentData(sceneDataEntity, new SceneData { CameraPosition = cameraPos });
		}
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

		// Initialize NoiseTextureManager
		noiseTextureManager = NoiseTextureManager.Instance;
		if (noiseTextureManager == null)
		{
			Debug.LogError("[MainScene] Failed to initialize NoiseTextureManager");
			return;
		}
		
		//// Locations
		// Read StreamingAssets/Locations folder to populate the locationNames list
		UpdateLocationPresets();
		
		//// Presets
		// Read StreamingAssets/Locations folder to populate the presets lists
		GroupPresetsManager.Instance.UpdatePresets();

		// Set current location name before setting up mode managers
		var firstLocation = locationPresets.First.Value.Key;
		currentLocationName = firstLocation;

		// Setup mode managers
		simulationModeManager.Setup(this);
		assetBrowserModeManager.Setup(this);

		//// UI
		// Hide main menu initially
		mainMenuUIDocument.SetActive(false);

		// Register main menu button callbacks
		
		// Load the first location scene
		SceneManager.LoadScene(firstLocation, LoadSceneMode.Additive);
		
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
		
		// Start in Simulation mode
		SwitchMode(AppMode.Simulation);
    }
	
    private struct TempLocationPreset
    {
        public string name;
        public int wind_turbine_pylon_amount;
    }
	
	/// <summary>
	/// Updates the collection of available location presets from the StreamingAssets folder.
	/// </summary>
	private void UpdateLocationPresets()
	{
		IsReady = false;
		
		locationPresets = new LinkedList<KeyValuePair<string, LocationPreset>>();
		locationNames.Clear();
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

				LocationPreset locationPreset = new LocationPreset
				{
                    name = tempPreset.name,
                    wind_turbine_pylon_amount = tempPreset.wind_turbine_pylon_amount
				};

				locationPresets.AddLast(new KeyValuePair<string, LocationPreset>(tempPreset.name, locationPreset));
				locationNames.Add(tempPreset.name);
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
    // Removed color parsing; turbidity color settings are no longer used

	public void UnloadLocation()
	{
		IsReady = false;
		Debug.Log("[MainScene] Unloading location: " + currentLocationName);
		if (SceneManager.GetSceneByName(currentLocationName).isLoaded)
		{
			SceneManager.UnloadSceneAsync(currentLocationName);
		}
	}
	
	public void LoadLocationAndUpdateGUIState(string locationName)
	{
		simulationModeManager.LoadLocationAndUpdateGUIState(locationName);
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
		obstacleEntities.Dispose();

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
		while (currentLocationScript == null || !currentLocationScript.isActiveAndEnabled || !LocationScript.IsReady)
		{
			yield return null;
		}

		Debug.Log($"[MainScene] Location {locationName} is ready, notifying current mode manager.");
		
		currentModeManager?.OnLocationReady();
		
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
		
		currentLocationScript.Setup(simulationModeManager.mainGui.Q<VisualElement>("TurbidityRow"), currentPreset, simulationModeManager.turbidityPerView, simulationModeManager.views.Count, this);
		
		//IsReady = true; // This will be set in WaitForLocationSetup
	}
	
	public void SpawnDynamicPreset(string name)
	{
		simulationModeManager.SpawnDynamicPreset(name);
	}

	/// <summary>
	/// Spawns a dynamic preset with a custom group display name for this session.
	/// </summary>
	/// <param name="presetName">Name of the dynamic preset</param>
	/// <param name="groupName">Display name for the group</param>
	public void SpawnDynamicPreset(string presetName, string groupName)
	{
		simulationModeManager.SpawnDynamicPreset(presetName, groupName);
	}
	
	/// <summary>
	/// Creates and spawns a new static entity group based on the specified preset.
	/// </summary>
	/// <param name="presetName">Name of the preset to use</param>
	/// <param name="groupName">Name for the new group instance</param>
	public async Task SpawnStaticPreset(string presetName, string groupName)
	{
		await simulationModeManager.SpawnStaticPreset(presetName, groupName);
	}

    private void Update()
	{
		// Add main menu toggle to existing Update method
		if (Input.GetKeyDown(KeyCode.Escape))
		{
			if (currentModeManager == null || !currentModeManager.OnEscapePressed())
			{
				ToggleMainMenu();
			}
		}

		if (Input.GetKeyDown(KeyCode.F11))
		{
			isHudless = !isHudless;
			if (currentModeManager != null)
			{
				currentModeManager.SetHudless(isHudless);
			}
		}

		currentModeManager?.OnUpdate();
	}
	
	/// <summary>
	/// Updates the number of active views and adjusts the UI accordingly.
	/// </summary>
	/// <param name="viewCount">Desired number of views (1-4)</param>
	public void SetViewCountAndUpdateGUIState(int viewCount)
	{
		simulationModeManager.SetViewCountAndUpdateGUIState(viewCount);
	}

	/// <summary>
	/// Toggles the visibility of the main menu and main GUI
	/// </summary>
	public void ToggleMainMenu()
	{
		isMainMenuVisible = !isMainMenuVisible;
		
		if (isMainMenuVisible)
		{
			// Instead of hiding the whole mainGUI, we now just hide the specific mode's GUI
			if (currentModeManager != null)
			{
				currentModeManager.EnterMenu();
			}

			mainMenuUIDocument.SetActive(true);

			// Register close button callback after activating the document
			mainMenuRoot = mainMenuUIDocument.GetComponent<UIDocument>().rootVisualElement;
			
			var closeButton = mainMenuRoot.Q<Button>("CloseMenuButton");
			if (closeButton != null)
			{
				closeButton.RegisterCallback<ClickEvent>((evt) => CloseMainMenu());
			}
			else
			{
				Debug.LogError("[MainScene] CloseMenuButton not found in mainMenuRoot");
			}

			var assetBrowserButton = mainMenuRoot.Q<Button>("AssetBrowserButton");
			if (assetBrowserButton != null)
			{
				assetBrowserButton.RegisterCallback<ClickEvent>((evt) =>
				{
					SwitchMode(AppMode.AssetBrowser);
					CloseMainMenu();
				});
			}
			else
			{
				Debug.LogError("[MainScene] AssetBrowserButton not found in mainMenuRoot");
			}

			var simulationModeButton = mainMenuRoot.Q<Button>("SimulationModeButton");
			if (simulationModeButton != null)
			{
				simulationModeButton.RegisterCallback<ClickEvent>((evt) =>
				{
					SwitchMode(AppMode.Simulation);
					CloseMainMenu();
				});
			}
			else
			{
				Debug.LogError("[MainScene] SimulationModeButton not found in mainMenuRoot");
			}

			// Set button visibility based on current mode
			if (currentMode == AppMode.Simulation)
			{
				if (simulationModeButton != null)
				{
					simulationModeButton.style.display = DisplayStyle.None;
				}
				if (assetBrowserButton != null)
				{
					assetBrowserButton.style.display = DisplayStyle.Flex;
				}
			}
			else if (currentMode == AppMode.AssetBrowser)
			{
				if (simulationModeButton != null)
				{
					simulationModeButton.style.display = DisplayStyle.Flex;
				}
				if (assetBrowserButton != null)
				{
					assetBrowserButton.style.display = DisplayStyle.None;
				}
			}

			var closeAppButton = mainMenuRoot.Q<Button>("CloseAppButton");
			if (closeAppButton != null)
			{
				closeAppButton.RegisterCallback<ClickEvent>((evt) => Application.Quit());
			}
			else
			{
				Debug.LogError("[MainScene] CloseAppButton not found in mainMenuRoot");
			}
		}
		else
		{
			mainMenuUIDocument.SetActive(false);

			// Re-enter the current mode to show its GUI
			if (currentModeManager != null)
			{
				currentModeManager.ExitMenu();
			}
		}
	}

	/// <summary>
	/// Closes the main menu and returns to the main GUI
	/// </summary>
	private void CloseMainMenu()
	{
		isMainMenuVisible = false;
		mainMenuUIDocument.SetActive(false);
		
		// Re-enter the current mode to show its GUI
		if (currentModeManager != null)
		{
			currentModeManager.ExitMenu();
		}
	}

	public void SwitchMode(AppMode newMode)
	{
		if (currentModeManager != null)
		{
			currentModeManager.ExitMode();
		}

		switch (newMode)
		{
			case AppMode.Simulation:
				currentModeManager = simulationModeManager;
				break;
			case AppMode.AssetBrowser:
				currentModeManager = assetBrowserModeManager;
				break;
		}

		currentMode = newMode;
		currentModeManager.EnterMode();
		currentModeManager.SetHudless(isHudless);
	}

	public void SwitchLocation(string locationName)
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
}
}
