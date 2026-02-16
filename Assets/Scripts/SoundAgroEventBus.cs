using System;
using UnityEngine;

public static class SoundAgroEventBus
{
    public static event Action<SoundAgroEvent> OnSoundAgroEvent;

    public static void Emit(Vector3 worldPosition, float intensity, GameObject source = null)
    {
        if (intensity <= 0f) return;

        var evt = new SoundAgroEvent
        {
            worldPosition = worldPosition,
            intensity = intensity,
            source = source,
            emittedAt = Time.time
        };

        OnSoundAgroEvent?.Invoke(evt);
    }

    public static float GetPerceivedIntensity(SoundAgroEvent evt, Vector3 listenerPosition, float distanceFalloff = 1f)
    {
        var dist = Vector3.Distance(listenerPosition, evt.worldPosition);
        return evt.intensity / (1f + Mathf.Max(0f, distanceFalloff) * dist * dist);
    }
}

[Serializable]
public struct SoundAgroEvent
{
    public Vector3 worldPosition;
    public float intensity;
    public float emittedAt;
    public GameObject source;
}
