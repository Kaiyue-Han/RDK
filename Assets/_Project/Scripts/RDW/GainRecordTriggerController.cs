using UnityEngine;

public class GainRecordTriggerController : MonoBehaviour
{
    [Header("Core References")]
    [SerializeField] private MaskingEventManager maskingEventManager;
    [SerializeField] private RotationInjectionController injectionController;
    [SerializeField] private WalkingDetector walkingDetector;
    [SerializeField] private GainSearchFlowController searchFlowController;

    [Header("Other Conditions")]
    [SerializeField] private float triggerCooldownSec = 4f;
    [SerializeField] private bool requireWalking = true;

    [Header("Runtime State")]
    [SerializeField] private float lastCandidateThetaDeg = 0f;
    [SerializeField] private float lastTriggerTime = -999f;

    [Header("Debug")]
    [SerializeField] private bool logDebug = true;

    private void Update()
    {
        if (!CanEvaluate())
            return;

        if (!PassWalkingGate())
            return;

        if (!NeedsEvaluationNow())
            return;

        if (searchFlowController == null || injectionController == null)
            return;

        if (Time.time < lastTriggerTime + triggerCooldownSec)
            return;

        float candidateThetaDeg = searchFlowController.CurrentTestThetaDeg;

        // Push the current test theta into the injection controller before checking
        // whether this frame has a usable injection direction.
        injectionController.SetCurrentEventThetaDeg(candidateThetaDeg);

        // Important: do not start an occlusion/evaluation if the injection controller
        // already knows there is no valid signed theta candidate on this frame.
        // The final safety check still happens in GainSearchFlowController after the event.
        if (!injectionController.TryGetCurrentCandidateSignedThetaDeg(out float candidateSignedThetaDeg))
        {
            if (logDebug)
            {
                Debug.Log(
                    $"[GainRecordTrigger] Evaluation not triggered: no valid injection candidate. " +
                    $"candidateTheta={candidateThetaDeg:F2}"
                );
            }

            return;
        }

        lastCandidateThetaDeg = Mathf.Abs(candidateSignedThetaDeg);

        bool triggered = maskingEventManager.TriggerCurrentOcclusion();
        if (!triggered)
            return;

        lastTriggerTime = Time.time;

        // Tell the search flow that one evaluation is now running.
        searchFlowController.NotifyEvaluationTriggered();

        if (logDebug)
        {
            Debug.Log(
                $"[GainRecordTrigger] Triggered evaluation event. " +
                $"candidateTheta={candidateThetaDeg:F2}, signedTheta={candidateSignedThetaDeg:F2}, " +
                $"next cooldown until t={lastTriggerTime + triggerCooldownSec:F2}"
            );
        }
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
        lastCandidateThetaDeg = 0f;
        lastTriggerTime = -999f;

        if (logDebug)
        {
            Debug.Log("[GainRecordTrigger] Trigger state reset.");
        }
    }

    public float GetCurrentCandidateThetaDeg()
    {
        if (searchFlowController == null)
            return 0f;

        return searchFlowController.CurrentTestThetaDeg;
    }

    public float GetLastCandidateThetaDeg()
    {
        return lastCandidateThetaDeg;
    }
}
