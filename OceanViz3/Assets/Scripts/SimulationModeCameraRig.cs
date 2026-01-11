using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Controls a free-flying camera rig with first-person controls.
/// Handles mouse look and WASD movement with additional vertical movement using Q/E keys.
/// Supports configurable movement inertia to slow acceleration when starting to move and
/// to keep a bit of momentum after releasing movement keys until the rig stops.
/// </summary>
namespace OceanViz3
{
public class SimulationModeCameraRig : MonoBehaviour
{
    /// <summary>Base movement speed in units per second</summary>
    public float mainSpeed = 10f;
    
    /// <summary>Speed multiplier when holding Shift key</summary>
    public float shiftMultiplier = 4f; // Quadruple speed when pressing Shift
    public float camSens = 0.25f;
    public float verticalSpeed = 5f; // Speed for Q/E vertical movement
    /// <summary>Movement inertia in range [0, 1]. 0 = instant response, 1 = heavy inertia.</summary>
    public float inertia = 0.25f;

    private CharacterController controller;
    private Camera playerCamera;
    private Light spotLight;
    [HideInInspector] public bool isActive = false;

    private float rotationX = 0f;
    private float rotationY = 0f;

    private EntityManager entityManager;
    private Entity sceneDataEntity;
    // Smoothed velocity (world space, units per second)
    private Vector3 currentVelocity = Vector3.zero;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        playerCamera = GetComponentInChildren<Camera>();
        if (playerCamera == null)
        {
            Debug.LogError("SimulationModeCameraRig: No camera found in children of the player object!");
        }
        else
        {
            Transform spotLightTransform = playerCamera.transform.Find("SpotLight");
            if (spotLightTransform != null)
            {
                spotLight = spotLightTransform.GetComponent<Light>();
                if (spotLight == null)
                {
                    Debug.LogError("SimulationModeCameraRig: No Light component found on SpotLight object!");
                }
            }
            else
            {
                Debug.LogError("SimulationModeCameraRig: No child object named 'SpotLight' found on the camera!");
            }
        }

        // Initialize ECS components
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        var query = entityManager.CreateEntityQuery(typeof(SceneData));
        sceneDataEntity = query.GetSingletonEntity();

        // Initialize yaw/pitch from current Transform and Camera so editor-set rotation is respected
        // Yaw comes from the rig's world yaw, pitch from the camera's world pitch (clamped)
        float initialYawDegrees = transform.eulerAngles.y;
        float initialPitchDegrees = 0f;
        if (playerCamera != null)
        {
            initialPitchDegrees = playerCamera.transform.rotation.eulerAngles.x;
        }

        // Convert pitch to signed range [-180, 180] then clamp to [-90, 90]
        if (initialPitchDegrees > 180f)
        {
            initialPitchDegrees -= 360f;
        }
        initialPitchDegrees = Mathf.Clamp(initialPitchDegrees, -90f, 90f);

        rotationX = initialYawDegrees;
        rotationY = initialPitchDegrees;

        // Apply the orientation immediately to avoid a one-frame snap in Update
        transform.rotation = Quaternion.Euler(0f, rotationX, 0f);
        if (playerCamera != null)
        {
            playerCamera.transform.localRotation = Quaternion.Euler(rotationY, 0f, 0f);
        }
    }

    void Update()
    {
        if (playerCamera != null && spotLight != null)
        {
            float cameraY = playerCamera.transform.position.y;
            float t = Mathf.InverseLerp(-50, -200, cameraY);
            spotLight.intensity = Mathf.Lerp(0, 270, t);
        }
        
        if (isActive)
        {
            // Mouse look (camera rotation)
            rotationX += Input.GetAxis("Mouse X") * camSens;
            rotationY -= Input.GetAxis("Mouse Y") * camSens; // Inverted Y-axis
            rotationY = Mathf.Clamp(rotationY, -90, 90);
        }

        // Always apply rotation to prevent other components from overriding it
        transform.rotation = Quaternion.Euler(0, rotationX, 0); // Rotate the entire rig horizontally
        if (playerCamera != null)
        {
            playerCamera.transform.localRotation = Quaternion.Euler(rotationY, 0, 0); // Rotate only the camera vertically
        }

        if (!isActive)
        {
            // Stop any residual momentum when controls are inactive
            currentVelocity = Vector3.zero;

            // Ensure ECS systems always receive the current camera position even when controls are inactive
            if (entityManager != null && entityManager.Exists(sceneDataEntity))
            {
                var sceneData = entityManager.GetComponentData<SceneData>(sceneDataEntity);
                sceneData.CameraPosition = transform.position;
                entityManager.SetComponentData(sceneDataEntity, sceneData);
            }
            else
            {
                Debug.LogError("[SimulationModeCameraRig] SceneData entity not found");
            }

            return;
        }

        // Keyboard movement (in camera look direction)
        Vector3 inputDirection = GetBaseInput();
        float currentSpeed = mainSpeed;

        if (Input.GetKey(KeyCode.LeftShift))
        {
            currentSpeed *= shiftMultiplier;
        }

        // Desired velocity in world space
        Vector3 desiredVelocity = inputDirection * currentSpeed;
        if (playerCamera != null)
        {
            desiredVelocity = playerCamera.transform.TransformDirection(desiredVelocity);
        }


        // Apply vertical movement from Q and E
        float verticalMovement = 0f;
        if (Input.GetKey(KeyCode.Q))
        {
            verticalMovement -= verticalSpeed;
        }
        if (Input.GetKey(KeyCode.E))
        {
            verticalMovement += verticalSpeed;
        }

        desiredVelocity.y += verticalMovement;

        // Smooth acceleration/deceleration using inertia (frame-rate independent)
        float inertiaClamped = Mathf.Clamp01(inertia);
        float responsePerSecond = Mathf.Lerp(20f, 2f, inertiaClamped);
        float smoothingFactor = 1f - Mathf.Exp(-responsePerSecond * Time.deltaTime);
        currentVelocity = Vector3.Lerp(currentVelocity, desiredVelocity, smoothingFactor);

        if (controller != null)
        {
            controller.Move(currentVelocity * Time.deltaTime);
        }


        // Update SceneData camera position
        if (entityManager != null && entityManager.Exists(sceneDataEntity))
        {
            var sceneData = entityManager.GetComponentData<SceneData>(sceneDataEntity);
            sceneData.CameraPosition = transform.position;
            entityManager.SetComponentData(sceneDataEntity, sceneData);
        }
        else
        {
            Debug.LogError("[SimulationModeCameraRig] SceneData entity not found");
        }
    }

    /// <summary>
    /// Gets the base movement input vector from WASD keys.
    /// </summary>
    /// <returns>Normalized direction vector based on keyboard input</returns>
    private Vector3 GetBaseInput()
    {
        Vector3 p_Velocity = new Vector3();
        if (Input.GetKey(KeyCode.W)) p_Velocity += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) p_Velocity += Vector3.back;
        if (Input.GetKey(KeyCode.A)) p_Velocity += Vector3.left;
        if (Input.GetKey(KeyCode.D)) p_Velocity += Vector3.right;
        return p_Velocity.normalized; // Normalize to ensure consistent speed in all directions
    }

    /// <summary>
    /// Resets the camera rig to its default starting position and rotation.
    /// </summary>
    public void ResetPositionAndRotation()
    {
        Vector3 newPosition = new Vector3(0, -12, -6);

        if (controller != null)
        {
            // Disabling and re-enabling the controller is a reliable way to teleport it.
            controller.enabled = false;
            transform.position = newPosition;
            controller.enabled = true;
        }
        else
        {
            transform.position = newPosition;
        }
        
        // Reset rotation variables
        rotationX = 0f;
        rotationY = 0f;
        currentVelocity = Vector3.zero;
        
        // Apply rotation reset
        transform.rotation = Quaternion.Euler(0, rotationX, 0);
        if (playerCamera != null)
        {
            playerCamera.transform.localRotation = Quaternion.Euler(rotationY, 0, 0);
        }

        // Update SceneData camera position
        if (entityManager != null && entityManager.Exists(sceneDataEntity))
        {
            var sceneData = entityManager.GetComponentData<SceneData>(sceneDataEntity);
            sceneData.CameraPosition = transform.position;
            entityManager.SetComponentData(sceneDataEntity, sceneData);
        }
    }

    /// <summary>
    /// Activates the camera rig controls and hides/locks the cursor.
    /// </summary>
    public void Activate()
    {
        isActive = true;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    /// <summary>
    /// Deactivates the camera rig controls and shows/unlocks the cursor.
    /// </summary>
    public void Deactivate()
    {
        isActive = false;
        currentVelocity = Vector3.zero;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    /// <summary>
    /// Instantly snaps the rig position and orientation to match a source transform.
    /// Keeps internal yaw/pitch state in sync so subsequent updates are stable.
    /// </summary>
    /// <param name="source">Transform to copy pose from</param>
    public void SnapToTransform(Transform source)
    {
        if (source == null)
        {
            return;
        }

        // Position
        transform.position = source.position;

        // Extract yaw and pitch from world rotation
        Vector3 euler = source.rotation.eulerAngles;
        float newYaw = euler.y;
        float newPitch = euler.x;
        if (newPitch > 180f)
        {
            newPitch -= 360f;
        }
        newPitch = Mathf.Clamp(newPitch, -90f, 90f);

        rotationX = newYaw;
        rotationY = newPitch;
        currentVelocity = Vector3.zero;

        // Apply immediately (match class Update behavior)
        transform.rotation = Quaternion.Euler(0f, rotationX, 0f);
        // Intentionally do not touch playerCamera local rotation here to allow camera-side
        // effects (e.g., UnderwaterCameraEffect/HandheldEffect) to layer their offsets.
    }
}
}