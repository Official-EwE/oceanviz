using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DeleteMeOnStart : MonoBehaviour
{
    /// <summary>
    /// Utility class. Deactivates and destroys the game object on start.
    /// </summary>
    void Start()
    {
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
