using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class MenuSequenceController : MonoBehaviour
{
    [Header("References")]
    public GameObject ufo3D;
    public float startScale;
    public float endScale;
    public Transform bookTransform;
    public CanvasGroup titleGroup;
    public float titleDelay = 1f;
    public CanvasGroup buttonsGroup;
    public float buttonsDelay = 1f;
    public CanvasGroup aliensGroup;
    public float aliensDelay = 1f;
    public FloatingText floatingText;
    public CanvasGroup finalUFOGif;
    public float finalGifDelay = 1f;

public AudioSource musicSource;
public AudioClip ufoMusic;
    [Header("Settings")]
    public float flyDuration = 3f;
    public float secondFlyDuration = 2f;
public void StartSequence()
{
    Debug.Log("SEQUENCE STARTED");
    StartCoroutine(SequenceRoutine());
}
void Awake()
{
    // Hide title and buttons at start
    SetCanvasGroup(titleGroup, 0f, false);
    SetCanvasGroup(buttonsGroup, 0f, false);
    SetCanvasGroup(aliensGroup, 0f, false);
    SetCanvasGroup(finalUFOGif, 0f, false);

    ufo3D.SetActive(false);
}

    IEnumerator SequenceRoutine()
    {
        ufo3D.SetActive(true);
        float bookDistance = GetDistanceFromCamera(bookTransform);

// Slightly closer to camera than book
float ufoDistance = bookDistance - 0.5f;
        // FIRST PASS (big, left to right)
        Vector3 start = Camera.main.ViewportToWorldPoint(
    new Vector3(-0.2f, 0.5f, ufoDistance)
);

Vector3 end = Camera.main.ViewportToWorldPoint(
    new Vector3(1.2f, 0.5f, ufoDistance)
);

        StartCoroutine(FlyUFO(start, end, flyDuration, startScale, endScale));
        musicSource.clip = ufoMusic;
        musicSource.Play();

        // Fade in title + buttons while UFO is offscreen
        
        yield return new WaitForSeconds(buttonsDelay);
        StartCoroutine(FadeCanvasGroup(buttonsGroup, 1f, 0f));
        yield return new WaitForSeconds(titleDelay);
        StartCoroutine(FadeCanvasGroup(titleGroup, 1f, 1.5f));
        
        yield return new WaitForSeconds(aliensDelay);
        StartCoroutine(FadeCanvasGroup(aliensGroup, 1f, 0f));

        yield return new WaitForSeconds(1f);

        // SECOND PASS (small, right to left)
        Vector3 end2 = Camera.main.ViewportToWorldPoint(
    new Vector3(0.4f, 0.9f, ufoDistance)
);

Vector3 start2 = Camera.main.ViewportToWorldPoint(
    new Vector3(1.2f, 0.9f, ufoDistance)
);

        yield return FlyUFO(start2, end2, secondFlyDuration, endScale*0.25f, endScale * 0.125f);

        // Stop above title
        ufo3D.transform.position = new Vector3(
            titleGroup.transform.position.x,
            titleGroup.transform.position.y + 200f,
            0
        );

        // Fade out 3D UFO
        StartCoroutine(FadeOut3DUFO());

        // Fade in GIF
        StartCoroutine(FadeCanvasGroup(finalUFOGif, 1f, 1f));
        floatingText.StartFloatingEffect();

        ufo3D.SetActive(false);
    }

    IEnumerator FlyUFO(Vector3 start, Vector3 end, float duration, float startScale, float endScale)
    {
        float t = 0f;
        ufo3D.transform.position = start;

        while (t < duration)
        {
            t += Time.deltaTime;
            float normalized = t / duration;

            ufo3D.transform.position = Vector3.Lerp(start, end, normalized);
            ufo3D.transform.localScale = Vector3.Lerp(
                Vector3.one * startScale,
                Vector3.one * endScale,
                normalized
            );

            ufo3D.transform.Rotate(0f, 120f * Time.deltaTime, 0f);

            yield return null;
        }
    }

    IEnumerator FadeCanvasGroup(CanvasGroup group, float target, float duration)
{
    float start = group.alpha;
    float t = 0f;

    while (t < duration)
    {
        t += Time.deltaTime;
        group.alpha = Mathf.Lerp(start, target, t / duration);
        yield return null;
    }

    group.alpha = target;

    if (target == 1f)
    {
        group.interactable = true;
        group.blocksRaycasts = true;
    }
}

    IEnumerator FadeOut3DUFO()
    {
        Renderer[] renderers = ufo3D.GetComponentsInChildren<Renderer>();
        float duration = 1f;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float alpha = 1f - (t / duration);

            foreach (var r in renderers)
            {
                foreach (var mat in r.materials)
                {
                    if (mat.HasProperty("_Color"))
                    {
                        Color c = mat.color;
                        c.a = alpha;
                        mat.color = c;
                    }
                }
            }

            yield return null;
        }
    }
    Vector3 GetOffScreenPosition(bool leftSide, float distanceFromCamera)
{
    Camera cam = Camera.main;

    // x = -0.2 = off left
    // x = 1.2 = off right
    float viewportX = leftSide ? -0.2f : 1.2f;
    float viewportY = 0.5f; // middle vertically

    Vector3 viewportPos = new Vector3(viewportX, viewportY, distanceFromCamera);
    return cam.ViewportToWorldPoint(viewportPos);
}
void SetCanvasGroup(CanvasGroup group, float alpha, bool interactable)
{
    group.alpha = alpha;
    group.interactable = interactable;
    group.blocksRaycasts = interactable;
}
float GetDistanceFromCamera(Transform target)
{
    return Vector3.Distance(Camera.main.transform.position, target.position);
}
}