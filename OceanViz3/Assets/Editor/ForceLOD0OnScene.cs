#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class ForceLOD0OnScene
{
    [MenuItem("Tools/LOD/Force Highest LOD")]
    static void ForceHighest()
    {
        foreach (var l in Object.FindObjectsOfType<LODGroup>())
            l.ForceLOD(0); // editor-only
    }

    [MenuItem("Tools/LOD/Restore LODs")]
    static void Restore()
    {
        foreach (var l in Object.FindObjectsOfType<LODGroup>())
            l.ForceLOD(-1);
    }
}
#endif
