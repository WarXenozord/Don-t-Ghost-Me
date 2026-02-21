using UnityEngine;

public class MediumFlashlightToggle : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private KeyCode toggleKey = KeyCode.T;

    [Header("Flashlight")]
    [SerializeField] private Light flashlight;
    [SerializeField] private bool autoFindSpotlightInChildren = true;
    [SerializeField] private bool startEnabled = true;

    void Awake()
    {
        if (flashlight == null && autoFindSpotlightInChildren)
        {
            var lights = GetComponentsInChildren<Light>(true);
            for (var i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Spot)
                {
                    flashlight = lights[i];
                    break;
                }
            }
        }

        if (flashlight != null)
        {
            flashlight.enabled = startEnabled;
        }
    }

    void Update()
    {
        if (flashlight == null) return;
        if (ChatUI.IsChatFocused) return;
        if (!Input.GetKeyDown(toggleKey)) return;

        flashlight.enabled = !flashlight.enabled;
    }
}
