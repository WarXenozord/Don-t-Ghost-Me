using UnityEngine;
using System.Collections;

public class MenuStateController : MonoBehaviour
{
    public MenuCameraController cameraController;

    public Transform sideTarget;
    public Transform mainTarget;

    public GameObject mainButtonsPanel;
    public GameObject playPanel;
    public GameObject settingsPanel;
    public GameObject creditsPanel;

    
    public void OpenPlay()
    {
        StartCoroutine(ChangeState(sideTarget, playPanel));
    }
public void ReturnToMain()
{
    
    StartCoroutine(ReturnRoutine());
}

IEnumerator ReturnRoutine()
{
    yield return StartCoroutine(cameraController.MoveToRoutine(mainTarget));

    mainButtonsPanel.SetActive(true);
}
    public void OpenSettings()
    {
        StartCoroutine(ChangeState(sideTarget, settingsPanel));
    }

    public void OpenCredits()
    {
        StartCoroutine(ChangeState(sideTarget, creditsPanel));
    }

    IEnumerator ChangeState(Transform camTarget, GameObject panelToOpen)
    {
        // Hide current UI
        mainButtonsPanel.SetActive(false);
        playPanel.SetActive(false);
        settingsPanel.SetActive(false);
        creditsPanel.SetActive(false);

        // WAIT for camera to finish
        yield return StartCoroutine(cameraController.MoveToRoutine(camTarget));

        // Activate only after movement is done
        panelToOpen.SetActive(true);
    }
}