using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// For editor only. Manages the cleanup of terrain detail prototypes when exiting play mode.
/// </summary>
public class TerrainDetailCleaner : MonoBehaviour
{
    [SerializeField]
    private bool cleanOnDestroy = true;

    private Terrain terrain;

    private void Awake()
    {
        terrain = GetComponent<Terrain>();
        if (terrain == null)
        {
            Debug.LogError("TerrainDetailCleaner must be attached to a GameObject with a Terrain component.");
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        if (terrain != null && cleanOnDestroy)
        {
            #if UNITY_EDITOR
                RemoveProceduralDetailPrototypes();
            #endif
        }
    }

    public void CleanDetails()
    {
        if (terrain != null)
        {
            #if UNITY_EDITOR
                RemoveProceduralDetailPrototypes();
            #else
                Debug.LogWarning("[TerrainDetailCleaner] Cleaning terrain details is only supported in the Unity Editor");
            #endif
        }
    }

    private void RemoveProceduralDetailPrototypes()
    {
        #if UNITY_EDITOR
            TerrainData terrainData = terrain.terrainData;
            DetailPrototype[] existingPrototypes = terrainData.detailPrototypes;
            List<DetailPrototype> newPrototypes = new List<DetailPrototype>();
            List<int> indicesToRemove = new List<int>();

            // Identify prototypes to keep and indices to remove
            for (int i = 0; i < existingPrototypes.Length; i++)
            {
                if (existingPrototypes[i].prototype != null && 
                    !existingPrototypes[i].prototype.name.StartsWith("PROC_"))
                {
                    newPrototypes.Add(existingPrototypes[i]);
                }
                else
                {
                    indicesToRemove.Add(i);
                }
            }

            int detailWidth = terrainData.detailWidth;
            int detailHeight = terrainData.detailHeight;

            // Create a new array to hold all layers
            List<int[,]> allLayers = new List<int[,]>();

            // Collect all layer data, skipping the ones to be removed
            for (int i = 0; i < existingPrototypes.Length; i++)
            {
                if (!indicesToRemove.Contains(i))
                {
                    allLayers.Add(terrainData.GetDetailLayer(0, 0, detailWidth, detailHeight, i));
                }
            }

            // Update terrain detail prototypes
            terrainData.detailPrototypes = newPrototypes.ToArray();

            // Set the remaining layers back to the terrain
            for (int i = 0; i < allLayers.Count; i++)
            {
                terrainData.SetDetailLayer(0, 0, i, allLayers[i]);
            }

            Debug.Log($"Removed {indicesToRemove.Count} procedural detail prototypes and their corresponding layers.");
        #endif
    }
}