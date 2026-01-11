using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class PlayModeSceneSetter : EditorWindow
{
    private SceneAsset startScene;

    [MenuItem("Tools/Set Play Mode Start Scene")]
    public static void ShowWindow()
    {
        GetWindow<PlayModeSceneSetter>("Set Start Scene");
    }

    private void OnGUI()
    {
        startScene = (SceneAsset)EditorGUILayout.ObjectField("Start Scene", startScene, typeof(SceneAsset), false);

        if (GUILayout.Button("Set as Play Mode Start Scene") && startScene != null)
        {
            EditorSceneManager.playModeStartScene = startScene;
            Debug.Log("Play Mode Start Scene set to: " + startScene.name);
        }
    }
}
