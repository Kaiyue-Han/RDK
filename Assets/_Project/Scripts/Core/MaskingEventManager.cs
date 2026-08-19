using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MaskingEventManager : MonoBehaviour
{
    public enum TrialOccluderType
    {
        NoOccluder,
        StaticButterfly,
        DynamicButterfly
    }

    public readonly struct FormalEventTiming
    {
        public readonly float EventStartTime;
        public readonly float EventEndTime;
        public readonly float InjectionWindowStartTime;
        public readonly float InjectionWindowEndTime;

        public float EventDuration => EventEndTime - EventStartTime;
        public float InjectionWindowDuration =>
            InjectionWindowEndTime - InjectionWindowStartTime;

        public FormalEventTiming(
            float eventStartTime,
            float eventEndTime,
            float injectionWindowStartTime,
            float injectionWindowEndTime
        )
        {
            EventStartTime = eventStartTime;
            EventEndTime = eventEndTime;
            InjectionWindowStartTime = injectionWindowStartTime;
            InjectionWindowEndTime = injectionWindowEndTime;
        }
    }

    [Header("Formal Butterfly Occluder")]
    [Tooltip("The same butterfly object/layout is used for both Static and Dynamic formal conditions.")]
    [SerializeField] private MonoBehaviour butterflyOccluderBehaviour;

    [Header("Logging")]
    [SerializeField] private EventLogger logger;

    [Header("Authoritative Formal Event Timing")]
    [Min(0.05f)]
    [SerializeField] private float formalEventDurationSec = 0.85f;

    [Tooltip("Center of the injection window as a fraction of the event duration.")]
    [Range(0f, 1f)]
    [SerializeField] private float injectAt = 0.50f;

    [Tooltip("Injection-window duration as a fraction of the event duration.")]
    [Range(0.01f, 1f)]
    [SerializeField] private float injectWindowRatio = 0.55f;

    [Header("Current Locked Trial Condition")]
    [SerializeField] private bool isTrialConfigured;
    [SerializeField] private bool isTrialRunning;
    [SerializeField] private TrialOccluderType currentOccluderType = TrialOccluderType.NoOccluder;
    [SerializeField] private int currentOcclusionRatio;

    [Header("Runtime State")]
    [SerializeField] private bool isOcclusionActive;

    [Header("Debug Manual Trigger")]
    [Tooltip("Editor/debug only. Keep disabled in the formal StraightLine scene.")]
    [SerializeField] private bool enableDebugKeyboardTrigger;

#if ENABLE_INPUT_SYSTEM
    [SerializeField] private Key debugTriggerKey = Key.F9;
#endif

    [SerializeField] private bool debugBypassTrialState;

    public event Action<FormalEventTiming> OnFormalEventStarted;
    public event Action OnFormalEventEnding;
    public event Action OnOcclusionEnded;

    public bool IsTrialConfigured => isTrialConfigured;
    public bool IsTrialRunning => isTrialRunning;
    public bool IsOcclusionActive => isOcclusionActive;
    public TrialOccluderType CurrentOccluderType => currentOccluderType;
    public bool IsNoOccluderCondition => currentOccluderType == TrialOccluderType.NoOccluder;
    public int CurrentOcclusionRatio => IsNoOccluderCondition ? 0 : currentOcclusionRatio;
    public string CurrentAnchorModeName => IsNoOccluderCondition ? "None" : "WorldFixed";
    public string CurrentMotionModeName => IsNoOccluderCondition
        ? "None"
        : currentOccluderType == TrialOccluderType.StaticButterfly ? "Static" : "Dynamic";
    public string CurrentVisualName => IsNoOccluderCondition ? "None" : "Butterfly";
    public string CurrentConditionKey => BuildCurrentConditionName();
    public string CurrentConditionName => BuildCurrentConditionName();
    public float FormalEventDurationSec => Mathf.Max(0.05f, formalEventDurationSec);
    public float InjectAt => Mathf.Clamp01(injectAt);
    public float InjectWindowRatio => Mathf.Clamp(injectWindowRatio, 0.01f, 1f);
    public FormalEventTiming CurrentEventTiming { get; private set; }

    private IOccluder butterflyOccluder;
    private WorldFixedOccluder formalButterflyOccluder;
    private IOccluder activeOccluder;

    private void Awake()
    {
        RebindOccluder();

        if (logger == null)
            Debug.LogWarning("[MaskingEventManager] logger not assigned (no csv output).", this);
    }

    private void Start()
    {
        butterflyOccluder?.Hide();
        activeOccluder = null;
        isOcclusionActive = false;
    }

    private void Update()
    {
        if (enableDebugKeyboardTrigger && WasDebugTriggerPressed())
            DebugTriggerCurrentOcclusion();

        if (isOcclusionActive && Time.time >= CurrentEventTiming.EventEndTime)
            EndOcclusion();
    }

    public void ConfigureTrial(TrialOccluderType occluderType, int ratio)
    {
        AbortAndResetToIdle("ConfigureTrial");

        currentOccluderType = occluderType;
        currentOcclusionRatio = IsNoOccluderCondition ? 0 : NormalizeFormalRatio(ratio);
        isTrialConfigured = true;
        isTrialRunning = true;

        Debug.Log(
            $"[MaskingEventManager] Formal condition configured: {BuildCurrentConditionName()}",
            this
        );
    }

    public void StopTrial()
    {
        AbortAndResetToIdle("StopTrial");
        isTrialRunning = false;
    }

    public void ClearTrial()
    {
        ClearTrial("ClearTrial", true);
    }

    public void ClearTrial(string resetReason, bool logReset)
    {
        AbortAndResetToIdle(
            string.IsNullOrEmpty(resetReason) ? "ClearTrial" : resetReason,
            logReset
        );

        isTrialConfigured = false;
        isTrialRunning = false;
        currentOccluderType = TrialOccluderType.NoOccluder;
        currentOcclusionRatio = 0;
    }

    public bool DebugTriggerCurrentOcclusion()
    {
        if (isOcclusionActive)
            AbortAndResetToIdle("DebugRestartOcclusion");

        if (debugBypassTrialState && (!isTrialConfigured || !isTrialRunning))
        {
            isTrialConfigured = true;
            isTrialRunning = true;
        }

        return TriggerCurrentOcclusion();
    }

    public bool TriggerCurrentOcclusion()
    {
        if (!isTrialConfigured || !isTrialRunning || isOcclusionActive)
        {
            Debug.LogWarning(
                $"[MaskingEventManager] Trigger rejected. configured={isTrialConfigured}, " +
                $"running={isTrialRunning}, active={isOcclusionActive}",
                this
            );
            return false;
        }

        activeOccluder = IsNoOccluderCondition ? null : butterflyOccluder;
        if (!IsNoOccluderCondition && activeOccluder == null)
        {
            Debug.LogError("[MaskingEventManager] Formal butterfly occluder is not assigned.", this);
            return false;
        }

        float eventStart = Time.time;
        CurrentEventTiming = CalculateTiming(eventStart);
        FormalExperimentContext.RecordEventStart(eventStart);

        if (formalButterflyOccluder != null)
        {
            formalButterflyOccluder.SetSpatialMotionEnabled(
                currentOccluderType == TrialOccluderType.DynamicButterfly
            );
        }

        if (activeOccluder != null)
            activeOccluder.Show(CurrentOcclusionRatio, FormalEventDurationSec);

        isOcclusionActive = true;

        logger?.Mark(
            "OCCLUSION_START",
            this,
            -1f,
            $"eventDurationSec={FormalEventDurationSec:F3};" +
            $"injectionWindowStart={CurrentEventTiming.InjectionWindowStartTime:F3};" +
            $"injectionWindowEnd={CurrentEventTiming.InjectionWindowEndTime:F3}"
        );

        OnFormalEventStarted?.Invoke(CurrentEventTiming);
        return true;
    }

    public void AbortAndResetToIdle(string resetReason = "AbortAndResetToIdle")
    {
        AbortAndResetToIdle(resetReason, true);
    }

    public void AbortAndResetToIdle(string resetReason, bool logReset)
    {
        if (isOcclusionActive)
            OnFormalEventEnding?.Invoke();

        activeOccluder?.Hide();
        butterflyOccluder?.Hide();
        activeOccluder = null;
        isOcclusionActive = false;
        CurrentEventTiming = default;

        if (logReset)
            logger?.LogResetEvent("RESET_TO_IDLE", this, resetReason);
    }

    public void SetOcclusionRatio(int ratio)
    {
        currentOcclusionRatio = IsNoOccluderCondition ? 0 : NormalizeFormalRatio(ratio);
    }

    public string BuildCurrentConditionName()
    {
        if (IsNoOccluderCondition)
            return "NoOccluder";

        string motion = currentOccluderType == TrialOccluderType.StaticButterfly
            ? "StaticButterfly"
            : "DynamicButterfly";
        return $"{motion}{CurrentOcclusionRatio}";
    }

    private FormalEventTiming CalculateTiming(float eventStart)
    {
        float duration = FormalEventDurationSec;
        float windowDuration = duration * InjectWindowRatio;
        float midpoint = duration * InjectAt;
        float startOffset = Mathf.Clamp(midpoint - windowDuration * 0.5f, 0f, duration);
        float endOffset = Mathf.Clamp(midpoint + windowDuration * 0.5f, 0f, duration);

        return new FormalEventTiming(
            eventStart,
            eventStart + duration,
            eventStart + startOffset,
            eventStart + endOffset
        );
    }

    private void EndOcclusion()
    {
        OnFormalEventEnding?.Invoke();

        activeOccluder?.Hide();
        activeOccluder = null;
        isOcclusionActive = false;

        logger?.Mark("OCCLUSION_END", this);
        OnOcclusionEnded?.Invoke();
    }

    private void RebindOccluder()
    {
        butterflyOccluder = butterflyOccluderBehaviour as IOccluder;
        formalButterflyOccluder = butterflyOccluderBehaviour as WorldFixedOccluder;

        if (butterflyOccluderBehaviour != null && butterflyOccluder == null)
        {
            Debug.LogError(
                "[MaskingEventManager] butterflyOccluderBehaviour must implement IOccluder.",
                this
            );
        }
    }

    private static int NormalizeFormalRatio(int ratio)
    {
        if (ratio == 40 || ratio == 70)
            return ratio;

        int normalized = ratio < 55 ? 40 : 70;
        Debug.LogWarning(
            $"[MaskingEventManager] Formal coverage must be 40 or 70. " +
            $"Received {ratio}; using {normalized}."
        );
        return normalized;
    }

    private bool WasDebugTriggerPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current[debugTriggerKey].wasPressedThisFrame;
#else
        return false;
#endif
    }
}
