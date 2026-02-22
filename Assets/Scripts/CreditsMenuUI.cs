using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Audio;

public class CreditsMenuUI : MonoBehaviour
{
    
    [Header("Menu Panels")]
    public GameObject creditsPanel;
    public GameObject mainButtonsPanel;
    public MenuStateController menuStateController;
    public Button backButton;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return) )
        {
            OnBackClicked();
        }
    }
     public void OnBackClicked()
    {
        if (creditsPanel != null)
            creditsPanel.SetActive(false);

        if (mainButtonsPanel != null)
           menuStateController.ReturnToMain();
    }
    private void Start()
    {
        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);
    }

    public void CloseCredits()
    {
        gameObject.SetActive(false);
    }
}
