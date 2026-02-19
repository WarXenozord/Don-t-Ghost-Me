using UnityEngine;

[ExecuteInEditMode]
public class HalfLifeEffect : MonoBehaviour
{
    public Shader effectShader;
    private Material _material;

    [Range(0, 5)]
    public float blurSize = 1.5f;

    [Range(0, 1)]
    public float grayscaleAmount = 1f;

    public bool isHalfLife = false;

    void Start()
    {
        if (effectShader != null)
            _material = new Material(effectShader);
    }

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (!isHalfLife || _material == null)
        {
            Graphics.Blit(src, dest);
            return;
        }

        _material.SetFloat("_BlurSize", blurSize);
        _material.SetFloat("_GrayscaleAmount", grayscaleAmount);

        Graphics.Blit(src, dest, _material);
    }
}