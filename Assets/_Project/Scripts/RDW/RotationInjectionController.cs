using UnityEngine;

public class RotationInjectionController : MonoBehaviour
{
    [Header("Dependencies")]
    public PlayAreaRectProvider playArea;
    public MaskingEventManager maskingEventManager;
    public WalkingDetector walkingDetector;

    [Header("Logging")]
    public EventLogger eventLogger;

    [Header("Injection settings")]
    public bool enableInjection = true;

    [Tooltip("Current experiment-driven event theta (deg). No longer derived from boundary distance.")]
    public float currentEventThetaDeg = 10f;

    public float thetaHardMax = 25f;

    [Header("Base direction gate")]
    [Tooltip("Preferred minimum base yaw rate magnitude for using current-frame direction.")]
    public float minBaseYawRateForInjection = 1.0f;

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

    // Cache the most recent valid base direction
    float lastValidBaseSign = 1f;
    bool hasValidBaseSign = false;

    // This is the key state used by GainSearchFlowController.
    // Only an evaluation that reaches INJECTION_START should be allowed to update theta.
    public bool CurrentEvaluationInjectionStarted { get; private set; } = false;
    public string CurrentEvaluationInjectionOutcome { get; private set; } = "NONE";
    public float CachedBaseYawRate => cachedBaseYawRate;
    public float LastSignedThetaDeg => signedTheta;
    public float LastInjectionSign => Mathf.Abs(signedTheta) > 0.0001f ? Mathf.Sign(signedTheta) : 0f;

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

    public void BeginEvaluationInjectionTracking()
    {
        CurrentEvaluationInjectionStarted = false;
        CurrentEvaluationInjectionOutcome = "WAITING_FOR_INJECT_POINT";
    }

    public void SetBaseYawRateForThisFrame(float baseYawRate)
    {
        cachedBaseYawRate = baseYawRate;

        if (Mathf.Abs(baseYawRate) >= minBaseYawRateForInjection)
        {
            lastValidBaseSign = Mathf.Sign(baseYawRate);
            hasValidBaseSign = true;
        }
    }

    public void SetCurrentEventThetaDeg(float thetaDeg)
    {
        currentEventThetaDeg = Mathf.Clamp(thetaDeg, 0f, thetaHardMax);

        if (logDebug)
            Debug.Log($"[Injection] currentEventThetaDeg set to {currentEventThetaDeg:F2}");
    }

    public float GetCurrentEventThetaDeg()
    {
        return Mathf.Clamp(currentEventThetaDeg, 0f, thetaHardMax);
    }

    public bool TryGetCurrentCandidateSignedThetaDeg(out float candidateSignedThetaDeg)
    {
        candidateSignedThetaDeg = 0f;

        if (!enableInjection)
            return false;

        if (walkingDetector != null && !walkingDetector.IsWalking)
            return false;

        float chosenSign = 0f;

        // Prefer current-frame base direction if strong enough
        if (Mathf.Abs(cachedBaseYawRate) >= minBaseYawRateForInjection)
        {
            chosenSign = Mathf.Sign(cachedBaseYawRate);
        }
        // Otherwise fall back to the most recent valid direction
        else if (hasValidBaseSign)
        {
            chosenSign = lastValidBaseSign;
        }
        else
        {
            return false;
        }

        float theta = Mathf.Clamp(currentEventThetaDeg, 0f, thetaHardMax);
        candidateSignedThetaDeg = theta * chosenSign;
        return true;
    }

    public bool TryGetCurrentCandidateAbsThetaDeg(out float candidateAbsThetaDeg)
    {
        candidateAbsThetaDeg = 0f;

        if (!TryGetCurrentCandidateSignedThetaDeg(out float signedThetaDeg))
            return false;

        candidateAbsThetaDeg = Mathf.Abs(signedThetaDeg);
        return true;
    }

    void OnInjectPoint()
    {
        CurrentEvaluationInjectionStarted = false;
        CurrentEvaluationInjectionOutcome = "AT_INJECT_POINT";

        if (maskingEventManager != null)
        {
            eventDuration = maskingEventManager.occlusionSec;
            injectAt = maskingEventManager.injectAt;
        }

        // 1) Injection disabled
        if (!enableInjection)
        {
            CurrentEvaluationInjectionOutcome = "SKIPPED_DISABLED";
            LogInjectionEvent("INJECTION_SKIPPED_DISABLED", 0f);

            if (logDebug)
                Debug.Log("[Inject] skipped: injection disabled");

            return;
        }

        // 2) Not walking at inject point
        if (walkingDetector != null && !walkingDetector.IsWalking)
        {
            CurrentEvaluationInjectionOutcome = "SKIPPED_NOT_WALKING";
            LogInjectionEvent("INJECTION_SKIPPED_NOT_WALKING", 0f);

            if (logDebug)
                Debug.Log("[Inject] skipped: not walking at inject point");

            return;
        }

        float chosenSign = 0f;

        // 3) Prefer current-frame base direction if strong enough
        if (Mathf.Abs(cachedBaseYawRate) >= minBaseYawRateForInjection)
        {
            chosenSign = Mathf.Sign(cachedBaseYawRate);
        }
        // 4) Otherwise use cached valid direction
        else if (hasValidBaseSign)
        {
            chosenSign = lastValidBaseSign;
        }
        else
        {
            CurrentEvaluationInjectionOutcome = "SKIPPED_NO_VALID_DIRECTION";
            LogInjectionEvent("INJECTION_SKIPPED_NO_VALID_DIRECTION", 0f);

            if (logDebug)
            {
                Debug.Log(
                    $"[Inject] skipped: no valid direction. " +
                    $"cachedBaseYawRate={cachedBaseYawRate:F2}, hasValidBaseSign={hasValidBaseSign}"
                );
            }

            return;
        }

        float theta = Mathf.Clamp(currentEventThetaDeg, 0f, thetaHardMax);
        float candidateSignedTheta = theta * chosenSign;

        signedTheta = candidateSignedTheta;
        active = true;
        startTime = Time.time;
        appliedAngle = 0f;

        CurrentEvaluationInjectionStarted = true;
        CurrentEvaluationInjectionOutcome = "STARTED";

        LogInjectionEvent("INJECTION_START", signedTheta);

        if (logDebug)
        {
            Debug.Log(
                $"[Inject] thetaSigned={signedTheta:F1}, " +
                $"cachedBaseYawRate={cachedBaseYawRate:F2}, " +
                $"lastValidBaseSign={(hasValidBaseSign ? lastValidBaseSign.ToString("F0") : "none")}"
            );
        }
    }

    public float ComputeEventYawRate(float dt)
    {
        if (!enableInjection || !active)
            return 0f;

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
        if (t <= t0)
        {
            targetApplied = 0f;
        }
        else if (t >= t1)
        {
            targetApplied = signedTheta;
        }
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

    static float SmoothStep01(float x) => x * x * (3f - 2f * x);

    public void ResetDirectionCache()
    {
        cachedBaseYawRate = 0f;
        hasValidBaseSign = false;
        lastValidBaseSign = 1f;

        active = false;
        appliedAngle = 0f;
        signedTheta = 0f;

        CurrentEvaluationInjectionStarted = false;
        CurrentEvaluationInjectionOutcome = "RESET";

        if (logDebug)
            Debug.Log("[Injection] Direction cache reset.");
    }

    private void LogInjectionEvent(string mark, float value)
    {
        if (eventLogger == null || maskingEventManager == null)
            return;

        float injectionSign = Mathf.Abs(value) > 0.0001f ? Mathf.Sign(value) : 0f;

        eventLogger.LogInjectionEvent(
            mark,
            maskingEventManager,
            value,
            cachedBaseYawRate,
            injectionSign,
            value,
            CurrentEvaluationInjectionOutcome
        );
    }
}
