namespace OceanViz3
{
    using UnityEngine;
    using UnityEngine.EventSystems;
    using Unity.Entities;
    using Unity.Mathematics;
    using UnityEngine.UIElements;

    /// <summary>
    /// Provides an orbit-style camera rig for the Asset Browser mode.
    /// The camera orbits around the rig's position when the user holds the left mouse button (LMB)
    /// as long as the pointer is not over a UI element. While orbiting the cursor is hidden and
    /// locked, restoring when the button is released.
    /// </summary>
    public class AssetBrowserCameraRig : MonoBehaviour
    {
        // --- Inspector parameters ---

        [Header("Movement Speeds")]
        [Tooltip("Degrees per pixel mouse movement.")]
        public float rotationSpeed = 1.2f;

        [Tooltip("Zoom speed using mouse scroll wheel.")]
        public float zoomSpeed = 80f;

        [Header("Zoom Limits")]
        public float minDistance = 0.001f;
        public float maxDistance = 500f;

        [Header("Pitch Limits")]
        public float minPitch = -80f;
        public float maxPitch = 80f;

        // Reference to the active UIDocument for more accurate UI hit detection
        [SerializeField] private UIDocument uiDocument;

        // --- Private state ---
        private Camera orbitCamera;
        private bool isOrbiting = false;
        private float yaw;
        private float pitch;
        private float distance;

        // ECS integration (optional – mirrors SimulationModeCameraRig behaviour)
        private EntityManager entityManager;
        private Entity sceneDataEntity;

        private void Start()
        {
            orbitCamera = GetComponentInChildren<Camera>();
            if (orbitCamera == null)
            {
                Debug.LogError("[AssetBrowserCameraRig] No Camera found in children.");
                enabled = false;
                return;
            }

            // Disable AudioListener on this camera if duplicates detected
            var al = orbitCamera.GetComponent<AudioListener>();
            if (al != null)
            {
                var audioListeners = FindObjectsOfType<AudioListener>();
                if (audioListeners.Length > 1)
                {
                    al.enabled = false;
                    // Intentionally silent to reduce console spam
                }
            }
            
            // Initialise spherical coordinates based on current camera transform.
            Vector3 offset = orbitCamera.transform.position - transform.position;
            distance = offset.magnitude;
            if (distance < 0.0001f) distance = 5f; // Default fallback

            // Convert offset to yaw / pitch (in degrees)
            yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(offset.y / distance) * Mathf.Rad2Deg;

            // Initialization complete

            // ECS setup (optional)
            if (World.DefaultGameObjectInjectionWorld != null)
            {
                entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
                var query = entityManager.CreateEntityQuery(typeof(SceneData));
                if (query.CalculateEntityCount() > 0)
                {
                    sceneDataEntity = query.GetSingletonEntity();
                }
            }
        }

        private void Update()
        {
            HandleInput();
            UpdateCameraTransform();
            UpdateSceneData();
        }

        private void HandleInput()
        {
            // Begin orbiting when LMB is pressed and pointer not over UI.
            if (Input.GetMouseButtonDown(0))
            {
                if (IsPointerOver3DInteractionArea(Input.mousePosition))
                {
                    isOrbiting = true;
                    UnityEngine.Cursor.visible = false;
                    UnityEngine.Cursor.lockState = CursorLockMode.Locked;
                }
            }
            // End orbiting on release.
            if (Input.GetMouseButtonUp(0) && isOrbiting)
            {
                isOrbiting = false;
                UnityEngine.Cursor.visible = true;
                UnityEngine.Cursor.lockState = CursorLockMode.None;
            }

            // While orbiting, update angles based on mouse movement.
            if (isOrbiting)
            {
                float deltaYaw = Input.GetAxis("Mouse X") * rotationSpeed * 100f * Time.deltaTime;
                float deltaPitch = -Input.GetAxis("Mouse Y") * rotationSpeed * 100f * Time.deltaTime;
                yaw += deltaYaw;
                pitch += deltaPitch; // deltaPitch already negated above
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            }

            // Zoom with scroll wheel only when over the 3D interaction area.
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && IsPointerOver3DInteractionArea(Input.mousePosition))
            {
                float zoomDelta = -scroll * zoomSpeed * Time.deltaTime;
                distance = Mathf.Clamp(distance + zoomDelta, minDistance, maxDistance);
            }
        }

        private void UpdateCameraTransform()
        {
            // Calculate new camera position from spherical coordinates.
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 newPos = transform.position + rot * (Vector3.back * distance);
            orbitCamera.transform.position = newPos;
            orbitCamera.transform.LookAt(transform.position);
        }

        private void UpdateSceneData()
        {
            if (entityManager != null && entityManager.Exists(sceneDataEntity))
            {
                var sceneData = entityManager.GetComponentData<SceneData>(sceneDataEntity);
                sceneData.CameraPosition = orbitCamera.transform.position;
                entityManager.SetComponentData(sceneDataEntity, sceneData);
            }
        }

        /// <summary>
        /// Checks if the pointer is currently hovering over a visible, pickable UI Toolkit element.
        /// Falls back to EventSystem check if UIDocument not set.
        /// </summary>
        private bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (uiDocument != null && uiDocument.rootVisualElement != null && uiDocument.rootVisualElement.panel != null)
            {
                Vector2 panelPos = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
                var picked = uiDocument.rootVisualElement.panel.Pick(panelPos);
                if (picked != null)
                {
                    VisualElement current = picked;
                    while (current != null && current != uiDocument.rootVisualElement)
                    {
                        if (current.pickingMode != PickingMode.Ignore && IsInteractiveElement(current))
                        {
                            return true;
                        }
                        current = current.parent;
                    }
                }
                return false; // No interactive element under pointer
            }

            // Fallback to EventSystem
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private bool IsPointerOver3DInteractionArea(Vector2 screenPosition)
        {
            if (uiDocument == null || uiDocument.rootVisualElement == null || uiDocument.rootVisualElement.panel == null)
            {
                return false;
            }

            Vector2 panelPos = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            var picked = uiDocument.rootVisualElement.panel.Pick(panelPos);

            if (picked != null)
            {
                // Check if the picked element is the 3DInteractionArea
                if (picked.name == "3DInteractionArea")
                {
                    return true;
                }
            }

            return false;
        }

        // Determines if a VisualElement is considered interactive (blocks orbiting)
        private bool IsInteractiveElement(VisualElement ve)
        {
            if (ve == null) return false;
            if (ve.focusable) return true;

            return ve is Button || ve is Slider || ve is DropdownField || ve is Toggle || ve is TextField;
        }

        public void SetUIDocument(UIDocument doc)
        {
            uiDocument = doc;
        }
    }
} 