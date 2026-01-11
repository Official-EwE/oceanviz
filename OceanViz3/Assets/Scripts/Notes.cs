#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// Editor-only component that holds free-form text notes on a GameObject.
/// Intended for documentation and editor usage only; excluded from builds.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tools/Notes (Editor Only)")]
public class Notes : MonoBehaviour
{
    [SerializeField]
    [TextArea(3, 20)]
    private string text = string.Empty;

    public string Text
    {
        get { return text; }
        set
        {
            Debug.Assert(value != null, "[Notes] Text cannot be null.");
            text = value == null ? string.Empty : value;
        }
    }

    private void Reset()
    {
        hideFlags |= HideFlags.DontSaveInBuild;
    }
}
#endif







