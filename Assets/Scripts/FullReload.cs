using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class FullReload : MonoBehaviour
{
    [Header("Restart")]
    [SerializeField] private int bootSceneBuildIndex = 0;
    [SerializeField] private bool clearPlayerPrefs = true;
    [SerializeField] private bool destroyDontDestroyObjects = true;
    [SerializeField] private float socketCloseTimeoutSeconds = 1f;

    private bool _restarting;

    /// <summary>
    /// Hard restart: clears persisted data and rebuilds runtime from boot scene.
    /// </summary>
    public void RestartGame()
    {
        if (_restarting) return;
        StartCoroutine(RestartRoutine());
    }

    private IEnumerator RestartRoutine()
    {
        _restarting = true;

        // Restore normal runtime state before reloading.
        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (clearPlayerPrefs)
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
        }

        yield return CloseNakamaSocketIfAny();

        if (destroyDontDestroyObjects)
        {
            DestroyDontDestroyRoots();
            // Let Destroy() queue flush one frame before loading fresh scene.
            yield return null;
        }

        SceneManager.LoadScene(bootSceneBuildIndex, LoadSceneMode.Single);
    }

    private IEnumerator CloseNakamaSocketIfAny()
    {
        var conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        if (conn == null || conn.Socket == null) yield break;

        var closeTask = conn.Socket.CloseAsync();
        var timeoutAt = Time.realtimeSinceStartup + Mathf.Max(0.1f, socketCloseTimeoutSeconds);
        while (!closeTask.IsCompleted && Time.realtimeSinceStartup < timeoutAt)
        {
            yield return null;
        }
    }

    private void DestroyDontDestroyRoots()
    {
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        for (var i = 0; i < all.Length; i++)
        {
            var go = all[i];
            if (go == null) continue;
            if (go == gameObject) continue;
            if (go.scene.name != "DontDestroyOnLoad") continue;
            if (go.transform.parent != null) continue;

            Destroy(go);
        }
    }
}
