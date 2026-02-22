using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Audio;

public class SettingsMenuUI : MonoBehaviour
{
    [Header("Sliders")]
    public Slider masterSlider;
    public Slider musicSlider;
    public Slider sfxSlider;

    [Header("Toggle")]
    public Toggle fullscreenToggle;

    [Header("Audio")]
    public AudioMixer mixer;
    [Header("Menu Panels")]
    public GameObject settingsPanel;
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
        if (settingsPanel != null)
            settingsPanel.SetActive(false);

        if (mainButtonsPanel != null)
           menuStateController.ReturnToMain();
    }
    private void Start()
    {
        LoadSettings();

       // masterSlider.onValueChanged.AddListener(SetMasterVolume);
        //musicSlider.onValueChanged.AddListener(SetMusicVolume);
        //sfxSlider.onValueChanged.AddListener(SetSFXVolume);
        //fullscreenToggle.onValueChanged.AddListener(SetFullscreen);
        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);
    }

    public void SetMasterVolume(float value)
    {
        mixer.SetFloat("MasterVolume", Mathf.Log10(value) * 20);
        PlayerPrefs.SetFloat("MasterVolume", value);
    }

    public void SetMusicVolume(float value)
    {
        mixer.SetFloat("MusicVolume", Mathf.Log10(value) * 20);
        PlayerPrefs.SetFloat("MusicVolume", value);
    }

    public void SetSFXVolume(float value)
    {
        mixer.SetFloat("SFXVolume", Mathf.Log10(value) * 20);
        PlayerPrefs.SetFloat("SFXVolume", value);
    }

    public void SetFullscreen(bool isFullscreen)
    {
        Screen.fullScreen = isFullscreen;
        PlayerPrefs.SetInt("Fullscreen", isFullscreen ? 1 : 0);
    }

    private void LoadSettings()
    {
        float master = PlayerPrefs.GetFloat("MasterVolume", 1f);
        float music = PlayerPrefs.GetFloat("MusicVolume", 1f);
        float sfx = PlayerPrefs.GetFloat("SFXVolume", 1f);
        bool fullscreen = PlayerPrefs.GetInt("Fullscreen", 1) == 1;

        masterSlider.value = master;
        musicSlider.value = music;
        sfxSlider.value = sfx;
        fullscreenToggle.isOn = fullscreen;

        SetMasterVolume(master);
        SetMusicVolume(music);
        SetSFXVolume(sfx);
        SetFullscreen(fullscreen);
    }

    public void CloseSettings()
    {
        gameObject.SetActive(false);
    }
}