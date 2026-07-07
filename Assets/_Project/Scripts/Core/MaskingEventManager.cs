using System;
using UnityEngine;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MaskingEventManager : MonoBehaviour
{
    public enum TrialOccluderType
    {
        Newspaper,
        Pigeon,
        Sign,
        Butterfly
    }

    [Header("Formal Trial Modules - Follow HMD")]
    [SerializeField] private MonoBehaviour newspaperOccluderBehaviour;
    [SerializeField] private MonoBehaviour pigeonOccluderBehaviour;

    [Header("Formal Trial Modules - World Fixed")]
    [SerializeField] private MonoBehaviour signOccluderBehaviour;
    [SerializeField] private MonoBehaviour butterflyOccluderBehaviour;

    [Header("Logging")]
    [SerializeField] private EventLogger logger;

    [Header("Occlusion Timing")]
    [Tooltip("Used by static occluders: Newspaper and Sign.")]
    [FormerlySerializedAs("newspaperOcclusionSec")]
    [SerializeField] private float staticOcclusionSec = 0.7f;

    [Tooltip("Used by dynamic occluders: Pigeon and Butterfly.")]
    [FormerlySerializedAs("pigeonOcclusionSec")]
    [SerializeField] private float dynamicOcclusionSec = 0.85f;

    [Range(0f, 1f)]
    public float injectAt = 0.5f;

    // compatibility for old RDW controller access
    public float occlusionSec => GetCurrentOcclusionDuration();

    [Header("Current Locked Trial Condition")]
    [SerializeField] private bool isTrialConfigured = false;
    [SerializeField] private bool isTrialRunning = false;
    [SerializeField] private TrialOccluderType currentOccluderType = TrialOccluderType.Newspaper;
    [SerializeField] private int currentOcclusionRatio = 40;

    [Header("Runtime State")]
    [SerializeField] private bool isOcclusionActive = false;

    [Header("Debug Manual Trigger")]
    [Tooltip("Editor/debug only: press the selected key to trigger the currently configured occluder immediately. This bypasses walking/cooldown/search checks.")]
    [SerializeField] private bool enableDebugKeyboardTrigger = true;

#if ENABLE_INPUT_SYSTEM
    [Tooltip("New Input System debug trigger key. Change this in the Inspector, for example F9, F10, Backquote, etc.")]
    [SerializeField] private Key debugTriggerKey = Key.F9;
#endif


    [Tooltip("If true, the debug key can trigger even before a formal trial is running. It uses the current Inspector/UI condition.")]
    [SerializeField] private bool debugBypassTrialState = true;

    public event Action OnInjectPoint;
    public event Action OnOcclusionEnded;

    public bool IsTrialConfigured => isTrialConfigured;
    public bool IsTrialRunning => isTrialRunning;
    public bool IsOcclusionActive => isOcclusionActive;

    public TrialOccluderType CurrentOccluderType => currentOccluderType;
    public int CurrentOcclusionRatio => currentOcclusionRatio;

    private IOccluder newspaperOccluder;
    private IOccluder pigeonOccluder;
    private IOccluder signOccluder;
    private IOccluder butterflyOccluder;
    private IOccluder activeOccluder;

    private float occlusionStartTime;
    private float currentOcclusionDuration;
    private bool injectFired;

    private void Awake()
    {
        RebindOccluders();

        if (logger == null)
            Debug.LogWarning("[MaskingEventManager] logger not assigned (no csv output).");
    }

    private void Start()
    {
        newspaperOccluder?.Hide();
        pigeonOccluder?.Hide();
        signOccluder?.Hide();
        butterflyOccluder?.Hide();

        activeOccluder = null;
        isOcclusionActive = false;
    }

    private void Update()
    {
        if (enableDebugKeyboardTrigger && WasDebugTriggerPressed())
        {
            DebugTriggerCurrentOcclusion();
        }

        if (!isOcclusionActive)
            return;

        RunOcclusionWindow();
    }

    public void ConfigureTrial(TrialOccluderType occluderType, int ratio)
    {
        AbortAndResetToIdle();

        currentOccluderType = occluderType;
        currentOcclusionRatio = ratio;

        isTrialConfigured = true;
        isTrialRunning = true;

        Debug.Log($"[MaskingEventManager] Trial configured: Type={currentOccluderType}, Ratio={currentOcclusionRatio}, Condition={BuildCurrentConditionName()}");
    }

    public void StopTrial()
    {
        AbortAndResetToIdle();
        isTrialRunning = false;
        Debug.Log("[MaskingEventManager] Trial stopped.");
    }

    public void ClearTrial()
    {
        AbortAndResetToIdle();

        isTrialConfigured = false;
        isTrialRunning = false;
        currentOccluderType = TrialOccluderType.Newspaper;
        currentOcclusionRatio = 40;

        Debug.Log("[MaskingEventManager] Trial cleared.");
    }

    public bool DebugTriggerCurrentOcclusion()
    {
        if (isOcclusionActive)
        {
            Debug.Log("[MaskingEventManager] Debug trigger pressed while occlusion is active. Restarting occlusion for position test.");
            AbortAndResetToIdle();
        }

        if (debugBypassTrialState)
        {
            if (!isTrialConfigured || !isTrialRunning)
            {
                Debug.LogWarning("[MaskingEventManager] Debug trigger is bypassing trial state. Current Inspector/UI condition will be used.");
                isTrialConfigured = true;
                isTrialRunning = true;
            }
        }

        Debug.Log($"[MaskingEventManager] DEBUG trigger ({GetDebugTriggerKeyName()}): Type={currentOccluderType}, Ratio={currentOcclusionRatio}, Condition={BuildCurrentConditionName()}");
        return TriggerCurrentOcclusion();
    }

    public bool TriggerCurrentOcclusion()
    {
        if (!isTrialConfigured)
        {
            Debug.LogWarning("[MaskingEventManager] Trigger ignored: trial not configured.");
            return false;
        }

        if (!isTrialRunning)
        {
            Debug.LogWarning("[MaskingEventManager] Trigger ignored: trial not running.");
            return false;
        }

        if (isOcclusionActive)
        {
            Debug.LogWarning("[MaskingEventManager] Trigger ignored: occlusion already active.");
            return false;
        }

        activeOccluder = GetCurrentOccluder();
        if (activeOccluder == null)
        {
            Debug.LogError($"[MaskingEventManager] Trigger failed: current occluder is null. Type={currentOccluderType}");
            return false;
        }

        currentOcclusionDuration = GetCurrentOcclusionDuration();
        occlusionStartTime = Time.time;
        injectFired = false;
        isOcclusionActive = true;

        logger?.Mark(
            "OCCLUSION_START",
            BuildCurrentConditionName(),
            currentOcclusionRatio
        );

        activeOccluder.Show(currentOcclusionRatio, currentOcclusionDuration);

        Debug.Log($"[MaskingEventManager] TriggerCurrentOcclusion: Type={currentOccluderType}, Ratio={currentOcclusionRatio}, Duration={currentOcclusionDuration}, Condition={BuildCurrentConditionName()}");
        return true;
    }

    public void AbortAndResetToIdle()
    {
        activeOccluder?.Hide();
        newspaperOccluder?.Hide();
        pigeonOccluder?.Hide();
        signOccluder?.Hide();
        butterflyOccluder?.Hide();

        activeOccluder = null;
        isOcclusionActive = false;
        injectFired = false;
        currentOcclusionDuration = 0f;

        logger?.Mark(
            "ABORT_RESET",
            BuildCurrentConditionName(),
            currentOcclusionRatio
        );
    }

    public void SetOcclusionRatio(int ratio)
    {
        currentOcclusionRatio = ratio;
    }

    private bool WasDebugTriggerPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current[debugTriggerKey].wasPressedThisFrame;
#else
        return false;
#endif
    }

    private string GetDebugTriggerKeyName()
    {
#if ENABLE_INPUT_SYSTEM
        return debugTriggerKey.ToString();
#else
        return "New Input System not enabled";
#endif
    }

    private void RunOcclusionWindow()
    {
        float elapsed = Time.time - occlusionStartTime;

        if (!injectFired && elapsed >= currentOcclusionDuration * injectAt)
        {
            injectFired = true;

            logger?.Mark(
                "INJECT_POINT",
                BuildCurrentConditionName(),
                currentOcclusionRatio
            );

            OnInjectPoint?.Invoke();
        }

        if (elapsed >= currentOcclusionDuration)
        {
            EndOcclusion();
        }
    }

    private void EndOcclusion()
    {
        activeOccluder?.Hide();
        activeOccluder = null;

        isOcclusionActive = false;
        injectFired = false;

        logger?.Mark(
            "EVENT_END",
            BuildCurrentConditionName(),
            currentOcclusionRatio
        );

        Debug.Log("[MaskingEventManager] Occlusion ended.");

        OnOcclusionEnded?.Invoke();
    }

    private void RebindOccluders()
    {
        newspaperOccluder = newspaperOccluderBehaviour as IOccluder;
        pigeonOccluder = pigeonOccluderBehaviour as IOccluder;
        signOccluder = signOccluderBehaviour as IOccluder;
        butterflyOccluder = butterflyOccluderBehaviour as IOccluder;

        ValidateOccluder(newspaperOccluderBehaviour, newspaperOccluder, "newspaperOccluderBehaviour");
        ValidateOccluder(pigeonOccluderBehaviour, pigeonOccluder, "pigeonOccluderBehaviour");
        ValidateOccluder(signOccluderBehaviour, signOccluder, "signOccluderBehaviour");
        ValidateOccluder(butterflyOccluderBehaviour, butterflyOccluder, "butterflyOccluderBehaviour");
    }

    private void ValidateOccluder(MonoBehaviour behaviour, IOccluder occluder, string fieldName)
    {
        if (behaviour != null && occluder == null)
            Debug.LogError($"[MaskingEventManager] {fieldName} must implement IOccluder.");
    }

    private IOccluder GetCurrentOccluder()
    {
        switch (currentOccluderType)
        {
            case TrialOccluderType.Newspaper:
                return newspaperOccluder;
            case TrialOccluderType.Pigeon:
                return pigeonOccluder;
            case TrialOccluderType.Sign:
                return signOccluder;
            case TrialOccluderType.Butterfly:
                return butterflyOccluder;
            default:
                return null;
        }
    }

    private float GetCurrentOcclusionDuration()
    {
        return IsStaticOccluder(currentOccluderType)
            ? staticOcclusionSec
            : dynamicOcclusionSec;
    }

    private bool IsStaticOccluder(TrialOccluderType type)
    {
        return type == TrialOccluderType.Newspaper || type == TrialOccluderType.Sign;
    }

    private string BuildCurrentConditionName()
    {
        string motionName = IsStaticOccluder(currentOccluderType) ? "Static" : "Dynamic";
        string anchorName = IsWorldFixedOccluder(currentOccluderType) ? "WorldFixed" : "FollowHMD";
        string visualName = GetVisualName(currentOccluderType);

        return $"{motionName}_{currentOcclusionRatio}_{anchorName}_{visualName}";
    }

    private bool IsWorldFixedOccluder(TrialOccluderType type)
    {
        return type == TrialOccluderType.Sign || type == TrialOccluderType.Butterfly;
    }

    private string GetVisualName(TrialOccluderType type)
    {
        switch (type)
        {
            case TrialOccluderType.Newspaper:
                return "Paper";
            case TrialOccluderType.Pigeon:
                return "Pigeon";
            case TrialOccluderType.Sign:
                return "Sign";
            case TrialOccluderType.Butterfly:
                return "Butterfly";
            default:
                return "Unknown";
        }
    }
}
