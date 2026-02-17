using UnityEngine;

public abstract class SoundAgroListener : MonoBehaviour
{
    [Header("Hearing")]
    [Min(0f)] public float minPerceivedIntensity = 0.2f;
    [Min(0f)] public float distanceFalloff = 1f;

    protected virtual void OnEnable()
    {
        SoundAgroEventBus.OnSoundAgroEvent += HandleSoundEvent;
    }

    protected virtual void OnDisable()
    {
        SoundAgroEventBus.OnSoundAgroEvent -= HandleSoundEvent;
    }

    private void HandleSoundEvent(SoundAgroEvent evt)
    {
        var perceived = SoundAgroEventBus.GetPerceivedIntensity(evt, transform.position, distanceFalloff);
        if (perceived < minPerceivedIntensity) return;
        OnSoundAgroHeard(evt, perceived);
    }

    protected abstract void OnSoundAgroHeard(SoundAgroEvent evt, float perceivedIntensity);
}
