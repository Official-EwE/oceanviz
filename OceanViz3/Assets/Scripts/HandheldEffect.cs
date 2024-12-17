using UnityEngine;

public class UnderwaterCameraEffect : MonoBehaviour
{
    public float amplitude = 1.0f; // The amplitude of the noise
    public float frequency = 1.0f; // The frequency of the noise
    public float noiseSpeed = 1.0f; // Speed of the noise over time

    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;

    void Start()
    {
        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;
    }

    void Update()
    {

        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;

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

        // Apply noise to position and rotation on top of user input
        transform.localPosition = initialLocalPosition + noisePositionOffset;
        transform.localRotation = initialLocalRotation * Quaternion.Euler(noiseRotationOffset);
    }
}
