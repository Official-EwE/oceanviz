using System;
using System.IO;
using UnityEngine;

namespace OceanViz3
{
    /// <summary>
    /// Platform-specific file dialogs for saving/loading scene setup files.
    /// In the editor this uses Unity's EditorUtility panels; in runtime builds this uses UnityStandaloneFileBrowser.
    /// </summary>
    public static class SceneSetupFileDialogs
    {
        public const string SceneSetupExtension = "ov3scene";

        public static bool TryGetSavePath(string initialDirectory, out string savePath)
        {
            savePath = null;

#if UNITY_EDITOR
            // In-editor, use Unity's built-in file panels.
            string directory = initialDirectory;
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                directory = Application.dataPath;
            }

            string path = UnityEditor.EditorUtility.SaveFilePanel(
                "Save OceanViz3 Scene Setup",
                directory,
                "scene_setup",
                SceneSetupExtension);

            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            savePath = path;
            return true;
#else
            string directory = initialDirectory;
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                directory = Application.persistentDataPath;
            }

            var extensionList = new[]
            {
                new SFB.ExtensionFilter("OceanViz3 Scene Setup", SceneSetupExtension),
                new SFB.ExtensionFilter("All Files", "*")
            };

            string path = SFB.StandaloneFileBrowser.SaveFilePanel(
                "Save OceanViz3 Scene Setup",
                directory,
                "scene_setup",
                extensionList);

            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            savePath = path;
            return true;
#endif
        }

        public static bool TryGetOpenPath(string initialDirectory, out string openPath)
        {
            openPath = null;

#if UNITY_EDITOR
            // In-editor, use Unity's built-in file panels.
            string directory = initialDirectory;
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                directory = Application.dataPath;
            }

            string path = UnityEditor.EditorUtility.OpenFilePanel(
                "Load OceanViz3 Scene Setup",
                directory,
                SceneSetupExtension);

            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            openPath = path;
            return true;
#else
            string directory = initialDirectory;
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                directory = Application.persistentDataPath;
            }

            var extensionList = new[]
            {
                new SFB.ExtensionFilter("OceanViz3 Scene Setup", SceneSetupExtension),
                new SFB.ExtensionFilter("All Files", "*")
            };

            string[] paths = SFB.StandaloneFileBrowser.OpenFilePanel(
                "Load OceanViz3 Scene Setup",
                directory,
                extensionList,
                false);

            if (paths == null || paths.Length == 0)
            {
                return false;
            }

            string path = paths[0];
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            openPath = path;
            return true;
#endif
        }
    }
}



