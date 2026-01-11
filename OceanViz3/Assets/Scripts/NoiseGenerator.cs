using UnityEngine;

/// <summary>
/// Generates Perlin noise textures.
/// </summary>
public static class NoiseGenerator
{
    /// <summary>
    /// Generates a grayscale noise texture using Perlin noise.
    /// </summary>
    /// <param name="width">Texture width.</param>
    /// <param name="height">Texture height.</param>
    /// <param name="offsetX">Global X offset for the noise pattern.</param>
    /// <param name="offsetY">Global Y offset for the noise pattern.</param>
    /// <param name="scale">Scale (frequency) of the noise. Lower values = larger features.</param>
    /// <returns>A Texture2D containing the generated Perlin noise.</returns>
    public static Texture2D GenerateNoiseTexture(int width, int height, float offsetX, float offsetY, float scale)
    {
        if (scale <= 0) scale = 0.0001f; // Prevent division by zero

        Texture2D noiseTexture = new Texture2D(width, height, TextureFormat.R8, false);
        noiseTexture.filterMode = FilterMode.Bilinear; // Use bilinear for smoother sampling

        Color32[] pixels = new Color32[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Sample Perlin noise. Output range is 0 to 1
                float noiseValue = Mathf.PerlinNoise((x + offsetX) / scale, (y + offsetY) / scale);
                
                // Convert noise value to grayscale color
                byte colorValue = (byte)(noiseValue * 255);
                pixels[y * width + x] = new Color32(colorValue, colorValue, colorValue, 255);
            }
        }

        noiseTexture.SetPixels32(pixels);
        noiseTexture.Apply(false); // Apply without mipmaps

        return noiseTexture;
    }
}
