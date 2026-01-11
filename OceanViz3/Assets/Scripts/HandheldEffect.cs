using UnityEngine;
using OceanViz3;

public class UnderwaterCameraEffect : MonoBehaviour
{
    public float amplitude = 1.0f; // The amplitude of the noise
    public float frequency = 1.0f; // The frequency of the noise
    public float noiseSpeed = 1.0f; // Speed of the noise over time
    public float returnSpeed = 0.5f; // How quickly to return to the starting position

    private Vector3 startingLocalPosition;
    private Vector3 currentBasePosition;
    private Quaternion currentBaseRotation;
    private SimulationModeCameraRig cameraRig;

    void Start()
    {
        startingLocalPosition = transform.localPosition;
        currentBasePosition = startingLocalPosition;
        currentBaseRotation = transform.localRotation;
        
        // Find the SimulationModeCameraRig component in the parent hierarchy
        cameraRig = GetComponentInParent<SimulationModeCameraRig>();
        if (cameraRig == null)
        {
            Debug.LogWarning("UnderwaterCameraEffect: SimulationModeCameraRig not found in parent hierarchy. Swim mode detection will not work.");
        }
    }

    void Update()
    {
        // Check if swim mode is active
        bool swimModeActive = cameraRig != null && cameraRig.isActive;
        
        if (swimModeActive)
        {
            // When swim mode is active, update our base transform to the current one
            // so the effect is relative to active camera control, without snapping later.
            currentBaseRotation = transform.localRotation;
            currentBasePosition = transform.localPosition;
        }
        else
        {
            // Smoothly return to starting position only when not in swim mode
            currentBasePosition = Vector3.Lerp(transform.localPosition, startingLocalPosition, returnSpeed * Time.deltaTime);
        }

        float time = Time.time * noiseSpeed * 0.2f;

        // Compute noise for position
        float noiseX = Mathf.PerlinNoise(time, 0.0f) * 2.0f - 1.0f;
        float noiseY = Mathf.PerlinNoise(0.0f, time) * 2.0f - 1.0f;
        float noiseZ = Mathf.PerlinNoise(time, time) * 2.0f - 1.0f;

        Vector3 noisePositionOffset = new Vector3(noiseX, noiseY, noiseZ) * amplitude * 0.001f;

        // Compute noise for rotation
        float noiseRotX = Mathf.PerlinNoise(time + 1.0f, 0.0f) * 2.0f - 1.0f;
        float noiseRotY = Mathf.PerlinNoise(0.0f, time + 1.0f) * 2.0f - 1.0f;
        float noiseRotZ = Mathf.PerlinNoise(time + 1.0f, time + 1.0f) * 2.0f - 1.0f;

        Vector3 noiseRotationOffset = new Vector3(noiseRotX, noiseRotY, noiseRotZ * 0.0f) * amplitude * 0.01f;

        // Apply noise to position and rotation
        transform.localPosition = currentBasePosition + noisePositionOffset;
        transform.localRotation = currentBaseRotation * Quaternion.Euler(noiseRotationOffset);
    }
}
