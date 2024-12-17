using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates Perlin noise textures with customizable parameters.
/// </summary>
public class NoiseGenerator : MonoBehaviour
{
    /// <summary>
    /// Generates a grayscale noise texture using Perlin noise.
    /// </summary>
    /// <param name="width">The width of the texture in pixels.</param>
    /// <param name="height">The height of the texture in pixels.</param>
    /// <param name="offsetX">The X offset for the noise pattern.</param>
    /// <param name="offsetY">The Y offset for the noise pattern.</param>
    /// <param name="scale">The scale factor for the noise. Higher values create larger patterns.</param>
    /// <param name="power">Optional power value to adjust the contrast of the noise. Default is 1.0f.</param>
    /// <returns>A new Texture2D containing the generated noise pattern.</returns>
    public static Texture2D GenerateNoiseTexture(int width, int height, float offsetX, float offsetY, int scale, float power = 1f)
    {
        // Create a new texture
        Texture2D noiseTexture = new Texture2D(width, height);

        // Generate a random seed
        float randomSeed = Random.Range(0f, 1000f);

        // For each pixel in the texture
        for (int x = 0; x < noiseTexture.width; x++)
        {
            for (int y = 0; y < noiseTexture.height; y++)
            {
                // Calculate the noise value with the random seed
                float noiseValue = Mathf.PerlinNoise((x + offsetX + randomSeed) / scale, (y + offsetY + randomSeed) / scale);
                noiseValue = Mathf.Pow(noiseValue, power);

                // Set the pixel color according to the noise value
                noiseTexture.SetPixel(x, y, new Color(noiseValue, noiseValue, noiseValue));
            }
        }

        // Apply the changes to the texture
        noiseTexture.Apply();

        // Return the texture
        return noiseTexture;
    }
}
