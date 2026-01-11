#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Ensures that the main scene at Assets/Scenes/Main/Main.unity
/// is always used as the Play Mode start scene after the editor reloads.
/// </summary>
[InitializeOnLoad]
public static class DefaultSceneLoader
{
    private const string MainScenePath = "Assets/Scenes/Main/Main.unity";

    static DefaultSceneLoader()
    {
        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScenePath);

        UnityEngine.Debug.Assert(
            sceneAsset != null,
            "DefaultSceneLoader: Could not find main scene at path: " + MainScenePath);

        if (sceneAsset == null)
        {
            return;
        }

        EditorSceneManager.playModeStartScene = sceneAsset;
    }
}
#endif
