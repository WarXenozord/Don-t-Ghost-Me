using UnityEngine;

public class SoundAgroEmitter : MonoBehaviour
{
    [Min(0f)] public float defaultIntensity = 2f;

    public void Emit()
    {
        Emit(defaultIntensity);
    }

    public void Emit(float intensity)
    {
        SoundAgroEventBus.Emit(transform.position, intensity, gameObject);
    }

    public void EmitAt(Vector3 worldPosition, float intensity)
    {
        SoundAgroEventBus.Emit(worldPosition, intensity, gameObject);
    }
}
