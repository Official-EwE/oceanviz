using UnityEngine;

/// <summary>
/// Controls a free-flying camera rig with first-person controls.
/// Handles mouse look and WASD movement with additional vertical movement using Q/E keys.
/// </summary>
public class CameraRig : MonoBehaviour
{
    /// <summary>Base movement speed in units per second</summary>
    public float mainSpeed = 10f;
    
    /// <summary>Speed multiplier when holding Shift key</summary>
    public float shiftMultiplier = 4f; // Quadruple speed when pressing Shift
    public float camSens = 0.25f;
    public float verticalSpeed = 5f; // Speed for Q/E vertical movement

    private CharacterController controller;
    private Camera playerCamera;
    [HideInInspector] public bool isActive = false;

    private float rotationX = 0f;
    private float rotationY = 0f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        playerCamera = GetComponentInChildren<Camera>();
        if (playerCamera == null)
        {
            Debug.LogError("CameraRig: No camera found in children of the player object!");
        }
    }

    void Update()
    {
        if (!isActive) return;

        // Mouse look (camera rotation)
        rotationX += Input.GetAxis("Mouse X") * camSens;
        rotationY -= Input.GetAxis("Mouse Y") * camSens; // Inverted Y-axis
        rotationY = Mathf.Clamp(rotationY, -90, 90);

        transform.rotation = Quaternion.Euler(0, rotationX, 0); // Rotate the entire rig horizontally
        playerCamera.transform.localRotation = Quaternion.Euler(rotationY, 0, 0); // Rotate only the camera vertically

        // Keyboard movement (in camera look direction)
        Vector3 moveDirection = GetBaseInput();
        float currentSpeed = mainSpeed;

        if (Input.GetKey(KeyCode.LeftShift))
        {
            currentSpeed *= shiftMultiplier;
        }

        moveDirection *= currentSpeed * Time.deltaTime;
        moveDirection = playerCamera.transform.TransformDirection(moveDirection);

        // Apply vertical movement from Q and E
        float verticalMovement = 0f;
        if (Input.GetKey(KeyCode.Q))
        {
            verticalMovement -= verticalSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.E))
        {
            verticalMovement += verticalSpeed * Time.deltaTime;
        }

        moveDirection.y += verticalMovement;

        controller.Move(moveDirection);
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
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }
}