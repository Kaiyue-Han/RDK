using UnityEngine;

public class CoinPulseVisual : MonoBehaviour
{
    [Header("Renderer")]
    [SerializeField] private Renderer targetRenderer;

    [Header("Colors")]
    [SerializeField] private Color inactiveColor = new Color(0.75f, 0.65f, 0.35f);
    [SerializeField] private Color activeColor = new Color(1.00f, 0.78f, 0.20f);

    [Header("Pulse Settings")]
    [SerializeField] private float pulseSpeed = 8f;
    [SerializeField] private float minIntensity = 0.15f;
    [SerializeField] private float maxIntensity = 0.7f;

    private bool isActivePulse = false;
    private MaterialPropertyBlock mpb;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponentInChildren<Renderer>();
        }

        mpb = new MaterialPropertyBlock();
        ApplyInactive();
    }

    public void SetActivePulse(bool active)
    {
        isActivePulse = active;

        if (!isActivePulse)
            ApplyInactive();
        else
            ApplyActiveImmediate();
    }

    private void Update()
    {
        if (targetRenderer == null) return;
        if (!isActivePulse) return;

        float t = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed);
        float intensity = Mathf.Lerp(minIntensity, maxIntensity, t);

        targetRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColorId, activeColor);
        mpb.SetColor(ColorId, activeColor);
        mpb.SetColor(EmissionColorId, activeColor * intensity);
        targetRenderer.SetPropertyBlock(mpb);
    }

    private void ApplyInactive()
    {
        if (targetRenderer == null) return;

        targetRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColorId, inactiveColor);
        mpb.SetColor(ColorId, inactiveColor);
        mpb.SetColor(EmissionColorId, Color.black);
        targetRenderer.SetPropertyBlock(mpb);
    }

    private void ApplyActiveImmediate()
    {
        if (targetRenderer == null) return;

        targetRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColorId, activeColor);
        mpb.SetColor(ColorId, activeColor);
        mpb.SetColor(EmissionColorId, activeColor * minIntensity);
        targetRenderer.SetPropertyBlock(mpb);
    }
}