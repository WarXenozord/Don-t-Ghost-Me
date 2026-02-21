using UnityEngine;

public class UIButtonSFX : MonoBehaviour
{
    public AudioClip clickClip;
    public AudioSource audioSource;

    public void PlayClick()
    {
        if (clickClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clickClip);
        }
    }
}