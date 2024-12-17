using UnityEngine;

/// <summary>
/// Makes a GameObject follow the main camera's horizontal position while maintaining its own vertical position.
/// This is used by the water surface to keep hight mesh tesselation close to the camera.
/// </summary>
public class FollowMainCameraHorizontal : MonoBehaviour
{
    private Camera mainCamera;
    
    private void Start()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("No main camera found in the scene!");
        }
    }
    
    /// <summary>
    /// Updates the GameObject's position each frame to match the main camera's horizontal position.
    /// The Y (vertical) position of the GameObject remains unchanged.
    /// </summary>
    private void Update()
    {
        if (mainCamera != null)
        {
            // Copy only the X and Z positions from the active camera
            Vector3 newPosition = new Vector3(
                mainCamera.transform.position.x,
                transform.position.y, // Keep the original Y position
                mainCamera.transform.position.z
            );
            
            // Apply the new position to this GameObject
            transform.position = newPosition;
        }
    }
}