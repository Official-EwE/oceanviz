using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

namespace OceanViz3
{
    /// <summary>
    /// Provides deterministic random number generation functionality using Unity.Mathematics.
    /// </summary>
    public static class RandomGenerator
    {
        /// <summary>
        /// Random number generator instance with a fixed seed for deterministic results.
        /// </summary>
        private static Unity.Mathematics.Random random = new Unity.Mathematics.Random(12345);

        public static float GetRandomFloat(float min, float max)
        {
            return random.NextFloat(min, max);
        }
        
        public static int GetRandomInt(int min, int max)
        {
            return random.NextInt(min, max);
        }
        
        public static float3 GetRandomFloat3(float min, float max)
        {
            return new float3(GetRandomFloat(min, max), GetRandomFloat(min, max), GetRandomFloat(min, max));
        }
    }
}
