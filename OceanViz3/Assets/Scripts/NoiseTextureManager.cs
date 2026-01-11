using UnityEngine;

namespace OceanViz3
{
    public class NoiseTextureManager : MonoBehaviour
    {
        private static NoiseTextureManager instance;
        public static NoiseTextureManager Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("NoiseTextureManager");
                    instance = go.AddComponent<NoiseTextureManager>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        [SerializeField]
        private Texture2D sharedNoiseTexture;
        private int noiseTextureSize = 1024; // Make this larger than your detail map size

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                GenerateSharedNoise();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void GenerateSharedNoise()
        {
            // Use the simplified NoiseGenerator method with basic parameters
            sharedNoiseTexture = NoiseGenerator.GenerateNoiseTexture(
                noiseTextureSize,    // width
                noiseTextureSize,    // height
                0,                   // offsetX
                0,                   // offsetY
                50.0f               // scale - higher = less detail
            );
        }

        /// <summary>
        /// Samples the generated noise texture at given world coordinates using a specific scale.
        /// </summary>
        /// <param name="worldX">World X coordinate.</param>
        /// <param name="worldY">World Y coordinate (used for Z in noise sampling).</param>
        /// <param name="offset">2D offset applied before scaling.</param>
        public float SampleNoise(float x, float y, Vector2 offset)
        {
            // Apply offset and scale the world coordinates.
            // Dividing by scale maps world units to the noise pattern's frequency.
            float scaledX = (x + offset.x);
            float scaledY = (y + offset.y);

            // Sample the texture using normalized UV coordinates with bilinear filtering for smoothness.
            return sharedNoiseTexture.GetPixelBilinear(scaledX, scaledY).r; // Sample red channel
        }
    }
} 