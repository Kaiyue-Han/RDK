using UnityEngine;

public class OcclusionTriggerController : MonoBehaviour
{
    [Header("Core References")]
    [SerializeField] private MaskingEventManager maskingEventManager;
    [SerializeField] private WalkingDetector walkingDetector;
    [SerializeField] private PhysicalPositionTracker physicalTracker;
    [SerializeField] private PlayAreaRectProvider playAreaProvider;

    [Header("Trigger Conditions")]
    [Tooltip("Distance to nearest boundary (meters) below which occlusion may trigger.")]
    [SerializeField] private float nearBoundaryThreshold = 0.8f;

    [Tooltip("Minimum time between two occlusion triggers.")]
    [SerializeField] private float triggerCooldownSec = 4f;

    [Tooltip("If true, only trigger while WalkingDetector says the user is walking.")]
    [SerializeField] private bool requireWalking = true;

    [Header("Debug")]
    [SerializeField] private bool logDebug = false;

    private float lastTriggerTime = -999f;

    private void Update()
    {
        if (!CanEvaluate())
            return;

        if (!PassWalkingGate())
            return;

        Vector2 physicalXZ = physicalTracker.PhysicalPositionXZ;
        float distToBoundary = playAreaProvider.DistanceToBoundary(physicalXZ);

        if (logDebug)
        {
            Debug.Log(
                $"[OcclusionTriggerController] physical={physicalXZ}, " +
                $"distToBoundary={distToBoundary:F3}, " +
                $"threshold={nearBoundaryThreshold:F3}, " +
                $"isWalking={walkingDetector.IsWalking}");
        }

        if (distToBoundary > nearBoundaryThreshold)
            return;

        if (Time.time < lastTriggerTime + triggerCooldownSec)
            return;

        bool triggered = maskingEventManager.TriggerCurrentOcclusion();
        if (!triggered)
            return;

        lastTriggerTime = Time.time;

        if (logDebug)
        {
            Debug.Log(
                $"[OcclusionTriggerController] Triggered. " +
                $"Type={maskingEventManager.CurrentOccluderType}, " +
                $"Ratio={maskingEventManager.CurrentOcclusionRatio}, " +
                $"distToBoundary={distToBoundary:F3}");
        }
    }

    private bool CanEvaluate()
    {
        if (maskingEventManager == null)
        {
            if (logDebug) Debug.LogWarning("[OcclusionTriggerController] maskingEventManager missing.");
            return false;
        }

        if (walkingDetector == null)
        {
            if (logDebug) Debug.LogWarning("[OcclusionTriggerController] walkingDetector missing.");
            return false;
        }

        if (physicalTracker == null)
        {
            if (logDebug) Debug.LogWarning("[OcclusionTriggerController] physicalTracker missing.");
            return false;
        }

        if (playAreaProvider == null)
        {
            if (logDebug) Debug.LogWarning("[OcclusionTriggerController] playAreaProvider missing.");
            return false;
        }

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

        return walkingDetector.IsWalking;
    }

    public void ResetCooldown()
    {
        lastTriggerTime = -999f;
    }
}