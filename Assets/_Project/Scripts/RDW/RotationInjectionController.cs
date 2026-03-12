using UnityEngine;

public class RotationInjectionController : MonoBehaviour
{
    [Header("Dependencies")]
    public PlayAreaRectProvider playArea;
    public MaskingEventManager maskingEventManager;
    public WalkingDetector walkingDetector;

    [Header("Injection settings")]
    public bool enableInjection = true;
    public float defaultThetaEventDeg = 10f;
    public float thetaHardMax = 25f;

    [Header("Distance-based clamp")]
    public float dMinNoEvent = 0.4f;
    public float dSafeForMax = 1.2f;
    public float thetaMinAtDmin = 4f;
    public float thetaMaxAtDsafe = 18f;

    [Header("Event timing")]
    public float eventDuration = 0.9f;
    [Range(0f, 1f)] public float injectAt = 0.5f;
    public float injectWindowRatio = 0.55f;

    [Header("Debug")]
    public bool logDebug = true;

    bool active;
    float startTime;
    float appliedAngle;
    float signedTheta;
    float cachedBaseYawRate;

    void Awake()
    {
        if (maskingEventManager != null)
        {
            maskingEventManager.OnInjectPoint += OnInjectPoint;
            eventDuration = maskingEventManager.occlusionSec;
            injectAt = maskingEventManager.injectAt;
        }
    }

    void OnDestroy()
    {
        if (maskingEventManager != null)
            maskingEventManager.OnInjectPoint -= OnInjectPoint;
    }

    public void SetBaseYawRateForThisFrame(float baseYawRate)
    {
        cachedBaseYawRate = baseYawRate;
    }

    void OnInjectPoint()
    {
        if (!enableInjection) return;
        if (playArea == null) return;

        if (walkingDetector != null && !walkingDetector.IsWalking)
        {
            if (logDebug) Debug.Log("[Inject] walkingDetector=false, skip");
            return;
        }

        if (Mathf.Abs(cachedBaseYawRate) < 1.0f)
        {
            if (logDebug) Debug.Log("[Inject] |base| too small, skip");
            return;
        }

        float baseSign = Mathf.Sign(cachedBaseYawRate);

        float d = playArea.DistanceToBoundary(playArea.GetHmdXZ());
        if (d < dMinNoEvent)
        {
            if (logDebug) Debug.Log($"[Inject] d={d:F2} < dMinNoEvent, skip");
            return;
        }

        float theta = Mathf.Clamp(defaultThetaEventDeg, 0f, thetaHardMax);
        float thetaMax = ThetaMaxByDistance(d);
        theta = Mathf.Min(theta, thetaMax);

        signedTheta = theta * baseSign;

        active = true;
        startTime = Time.time;
        appliedAngle = 0f;

        if (logDebug)
            Debug.Log($"[Inject] d={d:F2} thetaMax={thetaMax:F1} thetaSigned={signedTheta:F1}");
    }

    public float ComputeEventYawRate(float dt)
    {
        if (!enableInjection || !active) return 0f;

        float t = Time.time - startTime;
        if (t >= eventDuration)
        {
            active = false;
            return 0f;
        }

        float win = eventDuration * injectWindowRatio;
        float mid = eventDuration * injectAt;
        float t0 = Mathf.Clamp(mid - win * 0.5f, 0f, eventDuration);
        float t1 = Mathf.Clamp(mid + win * 0.5f, 0f, eventDuration);

        float targetApplied;
        if (t <= t0) targetApplied = 0f;
        else if (t >= t1) targetApplied = signedTheta;
        else
        {
            float u = (t - t0) / Mathf.Max(t1 - t0, 1e-4f);
            u = SmoothStep01(u);
            targetApplied = signedTheta * u;
        }

        float delta = targetApplied - appliedAngle;
        appliedAngle = targetApplied;

        return delta / Mathf.Max(dt, 1e-4f);
    }

    float ThetaMaxByDistance(float d)
    {
        float s = Mathf.Clamp01((d - dMinNoEvent) / Mathf.Max(dSafeForMax - dMinNoEvent, 1e-4f));
        return Mathf.Lerp(thetaMinAtDmin, thetaMaxAtDsafe, SmoothStep01(s));
    }

    static float SmoothStep01(float x) => x * x * (3f - 2f * x);
}