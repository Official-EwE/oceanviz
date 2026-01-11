using System;

namespace OceanViz3
{
    /// <summary>
    /// Serializable representation of a scene setup that can be saved to or loaded from JSON.
    /// This is written/read via the Simulation Mode UI's Save Scene / Load Scene buttons using the
    /// UnityStandaloneFileBrowser file dialogs, typically as a <c>.ov3scene</c> file under the
    /// project's <c>SavedScenes</c> folder (next to the executable in builds).
    /// Captures location, views, turbidity, and entity group configuration.
    /// </summary>
    [Serializable]
    public class SceneSetupFileV1
    {
        public int version = 1;
        public string locationName;
        public int viewsCount;
        public TurbidityEntry[] turbidities;
        public GroupEntry[] groups;
    }

    /// <summary>
    /// Per-view turbidity entry for the scene setup file.
    /// </summary>
    [Serializable]
    public class TurbidityEntry
    {
        public int viewIndex;
        public float turbidity;
    }

    /// <summary>
    /// Entity group entry (dynamic or static) for the scene setup file.
    /// </summary>
    [Serializable]
    public class GroupEntry
    {
        public string presetName;
        public string groupName;
        public float population;
        public string[] overrideHabitats;
        public VisibilityEntry[] visibilities;
    }

    /// <summary>
    /// Per-view visibility entry (0-1 fraction) for a group.
    /// </summary>
    [Serializable]
    public class VisibilityEntry
    {
        public int viewIndex;
        public float visibility;
    }
}


