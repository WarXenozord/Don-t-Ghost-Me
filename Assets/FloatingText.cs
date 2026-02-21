using UnityEngine;
using TMPro;

public class FloatingText : MonoBehaviour
{
    public TMP_Text text;

    [Header("Animation Settings")]
    public float cycleDuration = 3f;
    public float floatHeight = 15f;
    public bool isUI = true;

    private Vector3 startPos;
    private Color colorA;
    private Color colorB;

    private bool animate = false;
    private float timer = 0f;

    void Start()
    {
        if (text == null)
            text = GetComponent<TMP_Text>();

        startPos = transform.localPosition;

        // Disable animation visually at start
        enabled = false;
    }

    void Update()
    {
        if (!animate) return;

        timer += Time.deltaTime;

        float t = Mathf.PingPong(timer / cycleDuration, 1f);
        float smooth = Mathf.SmoothStep(0f, 1f, t);

        // Color interpolation
        text.color = Color.Lerp(colorA, colorB, smooth);

        // Floating motion (sin-based for smooth loop)
        float offset = Mathf.Sin(timer * Mathf.PI * 2f / cycleDuration) * floatHeight;

        if (isUI)
            transform.localPosition = startPos + new Vector3(0f, offset, 0f);
        else
            transform.localPosition = startPos + new Vector3(0f, offset * 0.01f, 0f);
    }

    // Call this AFTER second UFO transition
    public void StartFloatingEffect()
    {
        GenerateRandomColors();

        animate = true;
        enabled = true;
    }

    void GenerateRandomColors()
    {
        colorA = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.7f, 1f);
        colorB = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.7f, 1f);
    }
}