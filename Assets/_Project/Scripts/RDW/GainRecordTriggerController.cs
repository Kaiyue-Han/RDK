using UnityEngine;

public class GainRecordTriggerController : MonoBehaviour
{
    private enum PreEventState
    {
        WaitingForStableBase,
        BaselineLocked,
        EventActive
    }

    [Header("Core References")]
    [SerializeField] private MaskingEventManager maskingEventManager;
    [SerializeField] private RotationInjectionController injectionController;
    [SerializeField] private WalkingDetector walkingDetector;
    [SerializeField] private GainSearchFlowController searchFlowController;
    [SerializeField] private WorldRotator worldRotator;

    [Header("Other Conditions")]
    [Tooltip("Minimum time between the starts of two formal evaluation events.")]
    [SerializeField] private float triggerCooldownSec = 6f;

    [SerializeField] private bool requireWalking = true;

    [Header("Pre-Event Base Control")]
    [Tooltip("How long the already-smoothed base yaw rate must remain stable before it can be frozen (seconds).")]
    [Min(0f)]
    [SerializeField] private float baseStableDurationSec = 1.0f;

    [Tooltip("Maximum allowed variation of the smoothed base yaw rate during the stability window (deg/s).")]
    [Min(0f)]
    [SerializeField] private float baseStabilityToleranceDegPerSec = 1.0f;

    [Tooltip("After the base is frozen, keep the same base with no occluder for this long before the event starts (seconds).")]
    [Min(0f)]
    [SerializeField] private float preEventBaselineSec = 1.0f;

    [Header("Runtime State")]
    [SerializeField] private float lastCandidateThetaDeg = 0f;
    [SerializeField] private float lastTriggerTime = -999f;
    [SerializeField] private PreEventState preEventState = PreEventState.WaitingForStableBase;
    [SerializeField] private float stableBaseElapsedSec = 0f;
    [SerializeField] private float stableBaseMin = 0f;
    [SerializeField] private float stableBaseMax = 0f;
    [SerializeField] private float baselineElapsedSec = 0f;
    [SerializeField] private float frozenBaseYawRate = 0f;

    [Header("Debug")]
    [SerializeField] private bool logDebug = true;

    private bool stabilityTrackingActive = false;
    private float stabilityStartTime = 0f;
    private float stabilitySign = 0f;
    private float baselineStartTime = 0f;

    private void Awake()
    {
        if (worldRotator == null)
            worldRotator = FindObjectOfType<WorldRotator>();
    }

    private void OnEnable()
    {
        if (maskingEventManager != null)
            maskingEventManager.OnOcclusionEnded += HandleOcclusionEnded;
    }

    private void OnDisable()
    {
        if (maskingEventManager != null)
            maskingEventManager.OnOcclusionEnded -= HandleOcclusionEnded;

        CancelPreparation(false);
    }

    private void Update()
    {
        // Once the formal event has started, keep the base frozen until
        // MaskingEventManager reports that the event window has ended.
        if (preEventState == PreEventState.EventActive)
            return;

        if (!CanEvaluate())
        {
            CancelPreparation(false);
            ResetStabilityTracking();
            return;
        }

        if (!PassWalkingGate())
        {
            CancelPreparation(false);
            ResetStabilityTracking();
            return;
        }

        if (!NeedsEvaluationNow())
        {
            CancelPreparation(false);
            ResetStabilityTracking();
            return;
        }

        if (searchFlowController == null || injectionController == null || worldRotator == null)
        {
            CancelPreparation(false);
            ResetStabilityTracking();
            return;
        }

        // Cooldown is measured between actual event starts.
        if (Time.time < lastTriggerTime + triggerCooldownSec)
        {
            CancelPreparation(false);
            ResetStabilityTracking();
            return;
        }

        float candidateThetaDeg = searchFlowController.CurrentPlannedThetaDeg;
        injectionController.SetCurrentEventThetaDeg(candidateThetaDeg);

        if (preEventState == PreEventState.BaselineLocked)
        {
            RunLockedBaseline(candidateThetaDeg);
            return;
        }

        // Before the stability timer can progress, all original injection gates
        // must already be valid: walking, current base direction, and user movement
        // congruence.
        if (!injectionController.TryGetCurrentCandidateSignedThetaDeg(out float candidateSignedThetaDeg))
        {
            ResetStabilityTracking();
            return;
        }

        lastCandidateThetaDeg = Mathf.Abs(candidateSignedThetaDeg);

        float currentSmoothedBase = worldRotator.CurrentSmoothedBaseYawRate;

        if (!UpdateBaseStability(currentSmoothedBase))
            return;

        // The user has now experienced a stable smoothed base for the requested
        // duration. Freeze exactly that experienced base value.
        worldRotator.FreezeBase();
        frozenBaseYawRate = worldRotator.FrozenBaseYawRate;

        preEventState = PreEventState.BaselineLocked;
        baselineStartTime = Time.time;
        baselineElapsedSec = 0f;

        if (logDebug)
        {
            Debug.Log(
                $"[GainRecordTrigger] Base frozen after stable window. " +
                $"frozenBase={frozenBaseYawRate:F2}deg/s, " +
                $"stableDuration={baseStableDurationSec:F2}s, " +
                $"baselineDuration={preEventBaselineSec:F2}s"
            );
        }
    }

    private void RunLockedBaseline(float candidateThetaDeg)
    {
        baselineElapsedSec = Time.time - baselineStartTime;

        // During the one-second no-occluder baseline, all important gates must
        // remain valid. If they stop being valid, cancel this preparation,
        // release the base, and wait for a new stable opportunity.
        if (!injectionController.TryGetCurrentCandidateSignedThetaDeg(out float candidateSignedThetaDeg))
        {
            CancelPreparation(true);
            ResetStabilityTracking();
            return;
        }

        lastCandidateThetaDeg = Mathf.Abs(candidateSignedThetaDeg);

        if (baselineElapsedSec < preEventBaselineSec)
            return;

        float triggerSpeedMps = walkingDetector != null
            ? Mathf.Max(0f, walkingDetector.SmoothedSpeed)
            : 0f;

        // Allocate trial/evaluation identifiers and reset injection/response state
        // before OCCLUSION_START is logged so every event row is joinable.
        bool evaluationPrepared = searchFlowController.NotifyEvaluationTriggered(
            triggerSpeedMps
        );
        if (!evaluationPrepared)
        {
            CancelPreparation(true);
            ResetStabilityTracking();
            return;
        }

        bool triggered = maskingEventManager.TriggerCurrentOcclusion();
        if (!triggered)
        {
            searchFlowController.NotifyEvaluationTriggerFailed();
            CancelPreparation(true);
            ResetStabilityTracking();
            return;
        }

        lastTriggerTime = Time.time;
        preEventState = PreEventState.EventActive;

        if (logDebug)
        {
            Debug.Log(
                $"[GainRecordTrigger] Triggered evaluation after stable base + baseline. " +
                $"trialType={searchFlowController.CurrentEvaluationTrialType}, " +
                $"candidateTheta={candidateThetaDeg:F2}, " +
                $"signedTheta={candidateSignedThetaDeg:F2}, " +
                $"frozenBase={frozenBaseYawRate:F2}deg/s, " +
                $"next cooldown until t={lastTriggerTime + triggerCooldownSec:F2}"
            );
        }
    }

    private bool UpdateBaseStability(float currentBaseYawRate)
    {
        float currentSign = Mathf.Abs(currentBaseYawRate) > 0.0001f
            ? Mathf.Sign(currentBaseYawRate)
            : 0f;

        if (!stabilityTrackingActive)
        {
            StartStabilityTracking(currentBaseYawRate, currentSign);
            return baseStableDurationSec <= 0f;
        }

        // A direction change means this is not one stable background condition.
        if (currentSign == 0f || currentSign != stabilitySign)
        {
            StartStabilityTracking(currentBaseYawRate, currentSign);
            return false;
        }

        stableBaseMin = Mathf.Min(stableBaseMin, currentBaseYawRate);
        stableBaseMax = Mathf.Max(stableBaseMax, currentBaseYawRate);

        float range = stableBaseMax - stableBaseMin;

        // If the base changes too much, begin a fresh stability window from here.
        if (range > baseStabilityToleranceDegPerSec)
        {
            StartStabilityTracking(currentBaseYawRate, currentSign);
            return false;
        }

        stableBaseElapsedSec = Time.time - stabilityStartTime;
        return stableBaseElapsedSec >= baseStableDurationSec;
    }

    private void StartStabilityTracking(float currentBaseYawRate, float currentSign)
    {
        stabilityTrackingActive = true;
        stabilityStartTime = Time.time;
        stabilitySign = currentSign;
        stableBaseMin = currentBaseYawRate;
        stableBaseMax = currentBaseYawRate;
        stableBaseElapsedSec = 0f;
    }

    private void ResetStabilityTracking()
    {
        stabilityTrackingActive = false;
        stabilityStartTime = 0f;
        stabilitySign = 0f;
        stableBaseElapsedSec = 0f;
        stableBaseMin = 0f;
        stableBaseMax = 0f;
    }

    private void HandleOcclusionEnded()
    {
        // Keep the background base identical through the complete event window,
        // then release it. WorldRotator resumes smoothing from the frozen value.
        if (worldRotator != null)
            worldRotator.UnfreezeBase();

        if (logDebug && preEventState == PreEventState.EventActive)
        {
            Debug.Log(
                $"[GainRecordTrigger] Event ended. Base unfrozen from " +
                $"{frozenBaseYawRate:F2}deg/s."
            );
        }

        frozenBaseYawRate = 0f;
        baselineElapsedSec = 0f;
        preEventState = PreEventState.WaitingForStableBase;
        ResetStabilityTracking();
    }

    private void CancelPreparation(bool logCancellation)
    {
        bool hadPreparation =
            preEventState == PreEventState.BaselineLocked ||
            (worldRotator != null && worldRotator.IsBaseFrozen);

        if (worldRotator != null && worldRotator.IsBaseFrozen)
            worldRotator.UnfreezeBase();

        if (logCancellation && logDebug && hadPreparation)
        {
            Debug.Log(
                "[GainRecordTrigger] Pre-event preparation cancelled. " +
                "Base released; waiting for a new stable opportunity."
            );
        }

        frozenBaseYawRate = 0f;
        baselineElapsedSec = 0f;

        if (preEventState != PreEventState.EventActive)
            preEventState = PreEventState.WaitingForStableBase;
    }

    private bool CanEvaluate()
    {
        if (maskingEventManager == null || injectionController == null || searchFlowController == null)
            return false;

        if (!maskingEventManager.IsTrialConfigured)
            return false;

        if (!maskingEventManager.IsTrialRunning)
            return false;

        if (maskingEventManager.IsOcclusionActive)
            return false;

        return true;
    }

    private bool PassWalkingGate()
    {
        if (!requireWalking)
            return true;

        if (walkingDetector == null)
            return true;

        return walkingDetector.IsWalking;
    }

    private bool NeedsEvaluationNow()
    {
        if (searchFlowController == null)
            return false;

        if (searchFlowController.Phase == GainSearchFlowController.SearchPhase.Idle)
            return false;

        if (searchFlowController.Phase == GainSearchFlowController.SearchPhase.Finished)
            return false;

        return searchFlowController.NeedsEvaluationTrigger;
    }

    public void ResetTriggerState()
    {
        if (worldRotator != null)
            worldRotator.UnfreezeBase();

        lastCandidateThetaDeg = 0f;
        lastTriggerTime = -999f;
        frozenBaseYawRate = 0f;
        baselineElapsedSec = 0f;
        preEventState = PreEventState.WaitingForStableBase;

        ResetStabilityTracking();

        if (logDebug)
        {
            Debug.Log("[GainRecordTrigger] Trigger state reset.");
        }
    }

    public float GetCurrentCandidateThetaDeg()
    {
        if (searchFlowController == null)
            return 0f;

        return searchFlowController.CurrentPlannedThetaDeg;
    }

    public float GetLastCandidateThetaDeg()
    {
        return lastCandidateThetaDeg;
    }
}
