using UnityEngine;

public class CandleFlicker : MonoBehaviour
{
    public Light candleLight;

    [Header("Intensity")]
    public float baseIntensity = 2f;
    public float flickerAmount = 0.5f;
    public float flickerSpeed = 8f;

    [Header("Range")]
    public float baseRange = 3f;
    public float rangeFlicker = 0.2f;

    private float noiseOffset;

    void Start()
    {
        if (candleLight == null)
            candleLight = GetComponentInChildren<Light>();

        noiseOffset = Random.Range(0f, 100f);
    }

    void Update()
    {
        float noise = Mathf.PerlinNoise(Time.time * flickerSpeed, noiseOffset);

        float intensity = baseIntensity + (noise - 0.5f) * flickerAmount;
        float range = baseRange + (noise - 0.5f) * rangeFlicker;

        candleLight.intensity = intensity;
        candleLight.range = range;
    }
}