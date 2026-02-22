using UnityEngine;

public class GameOverCursorSetup : MonoBehaviour
{
    void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 1f; // in case game was paused
    }
}