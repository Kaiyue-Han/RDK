using UnityEngine;

public class GainSearchFlowController : MonoBehaviour
{
    public enum SearchPhase
    {
        Idle,
        Coarse,
        Fine,
        Confirm,
        Finished
    }

    [Header("References")]
    [SerializeField] private ParticipantFeedbackController participantFeedback;
    [SerializeField] private RotationInjectionController injectionController;
    [SerializeField] private GainRecordTriggerController recordTriggerController;
    [SerializeField] private MaskingEventManager maskingEventManager;
    [SerializeField] private EventLogger eventLogger;

    [Header("Search Parameters")]
    [SerializeField] private float startThetaDeg = 10f;
    [SerializeField] private float coarseStepDeg = 2f;
    [SerializeField] private float fineStepDeg = 1f;

    [Header("Auto Finish Evaluation")]
    [SerializeField] private bool autoFinishAfterOcclusionEnd = true;
    [SerializeField] private float autoFinishDelaySec = 0.15f;

    [Header("Debug Keyboard")]
    [SerializeField] private bool enableDebugKeyboard = true;
    [SerializeField] private KeyCode startSearchKey = KeyCode.F5;
    [SerializeField] private KeyCode finishEvaluationKey = KeyCode.F6;
    [SerializeField] private KeyCode resetSearchKey = KeyCode.F7;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    public SearchPhase Phase => phase;
    public float CurrentSafeThetaDeg => currentSafeThetaDeg;
    public float CurrentTestThetaDeg => currentTestThetaDeg;
    public float ConfirmedUpperAcceptableThetaDeg => confirmedUpperAcceptableThetaDeg;
    public bool HasConfirmedUpperAcceptableTheta => hasConfirmedUpperAcceptableTheta;

    public bool NeedsEvaluationTrigger => evaluationPending && !evaluationInProgress;
    public bool IsEvaluationInProgress => evaluationInProgress;

    private SearchPhase phase = SearchPhase.Idle;

    private float currentSafeThetaDeg;
    private float currentTestThetaDeg;

    private float confirmedUpperAcceptableThetaDeg;
    private bool hasConfirmedUpperAcceptableTheta = false;

    private bool noticedDuringCurrentEvaluation = false;

    private bool evaluationPending = false;
    private bool evaluationInProgress = false;

    private bool autoFinishScheduled = false;
    private float autoFinishTime = -1f;

    private void OnEnable()
    {
        if (maskingEventManager != null)
            maskingEventManager.OnOcclusionEnded += HandleOcclusionEnded;
    }

    private void OnDisable()
    {
        if (maskingEventManager != null)
            maskingEventManager.OnOcclusionEnded -= HandleOcclusionEnded;
    }

    private void Start()
    {
        Debug.Log("[GainSearchFlowController] Start called.", this);

        currentSafeThetaDeg = startThetaDeg;
        currentTestThetaDeg = startThetaDeg;
        ApplyCurrentTestThetaToInjection();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5))
        {
            Debug.Log("[GainSearchFlowController] Raw F5 detected.", this);
        }

        if (enableDebugKeyboard)
        {
            if (Input.GetKeyDown(startSearchKey))
                StartSearch();

            if (Input.GetKeyDown(finishEvaluationKey))
                NotifyCurrentEvaluationFinished();

            if (Input.GetKeyDown(resetSearchKey))
                ResetSearch();
        }

        if (autoFinishScheduled && Time.time >= autoFinishTime)
        {
            autoFinishScheduled = false;
            NotifyCurrentEvaluationFinished();
        }

        if (participantFeedback == null)
            return;

        if (participantFeedback.ConsumeFeedback())
        {
            if (evaluationInProgress)
            {
                noticedDuringCurrentEvaluation = true;

                if (debugLog)
                {
                    Debug.Log(
                        $"[GainSearchFlowController] Feedback noticed during current evaluation. Phase={phase}",
                        this
                    );
                }
            }
            else
            {
                if (debugLog)
                {
                    Debug.Log(
                        $"[GainSearchFlowController] Feedback ignored because no evaluation is in progress. " +
                        $"Phase={phase}, Pending={evaluationPending}, InProgress={evaluationInProgress}",
                        this
                    );
                }
            }
        }
    }

    public void StartSearch()
    {
        phase = SearchPhase.Coarse;

        currentSafeThetaDeg = startThetaDeg;
        currentTestThetaDeg = startThetaDeg;

        confirmedUpperAcceptableThetaDeg = 0f;
        hasConfirmedUpperAcceptableTheta = false;
        noticedDuringCurrentEvaluation = false;

        evaluationPending = true;
        evaluationInProgress = false;

        autoFinishScheduled = false;
        autoFinishTime = -1f;

        if (participantFeedback != null)
            participantFeedback.ResetState(true);

        if (injectionController != null)
            injectionController.ResetDirectionCache();

        ApplyCurrentTestThetaToInjection();

        if (recordTriggerController != null)
            recordTriggerController.ResetTriggerState();

        if (eventLogger != null && maskingEventManager != null)
        {
            eventLogger.LogSearchEvent(
                "SEARCH_START",
                BuildCurrentConditionName(),
                maskingEventManager.CurrentOcclusionRatio,
                phase.ToString(),
                currentTestThetaDeg,
                currentSafeThetaDeg
            );
        }

        if (debugLog)
        {
            Debug.Log(
                $"[GainSearchFlowController] Search started. " +
                $"Phase={phase}, Safe={currentSafeThetaDeg}, Test={currentTestThetaDeg}, Pending={evaluationPending}",
                this
            );
        }
    }

    public void NotifyEvaluationTriggered()
    {
        if (phase == SearchPhase.Idle || phase == SearchPhase.Finished)
        {
            if (debugLog)
            {
                Debug.Log(
                    $"[GainSearchFlowController] NotifyEvaluationTriggered ignored. Current phase={phase}",
                    this
                );
            }
            return;
        }

        if (!evaluationPending)
        {
            if (debugLog)
            {
                Debug.Log(
                    $"[GainSearchFlowController] NotifyEvaluationTriggered ignored. " +
                    $"Pending={evaluationPending}, InProgress={evaluationInProgress}",
                    this
                );
            }
            return;
        }

        evaluationPending = false;
        evaluationInProgress = true;
        noticedDuringCurrentEvaluation = false;

        if (injectionController != null)
            injectionController.BeginEvaluationInjectionTracking();

        autoFinishScheduled = false;
        autoFinishTime = -1f;

        if (participantFeedback != null)
            participantFeedback.ResetState(true);

        if (eventLogger != null && maskingEventManager != null)
        {
            eventLogger.LogEvaluation(
                "EVAL_START",
                BuildCurrentConditionName(),
                maskingEventManager.CurrentOcclusionRatio,
                phase.ToString(),
                currentTestThetaDeg,
                currentSafeThetaDeg,
                false
            );

            eventLogger.LogSearchEvent(
                "EVAL_TRIGGERED",
                BuildCurrentConditionName(),
                maskingEventManager.CurrentOcclusionRatio,
                phase.ToString(),
                currentTestThetaDeg,
                currentSafeThetaDeg
            );
        }

        if (debugLog)
        {
            Debug.Log(
                $"[GainSearchFlowController] Evaluation triggered. " +
                $"Phase={phase}, Test={currentTestThetaDeg}, Pending={evaluationPending}, InProgress={evaluationInProgress}",
                this
            );
        }
    }

    public void NotifyCurrentEvaluationFinished()
    {
        if (phase == SearchPhase.Idle || phase == SearchPhase.Finished)
        {
            if (debugLog)
            {
                Debug.Log(
                    $"[GainSearchFlowController] NotifyCurrentEvaluationFinished ignored. Current phase={phase}",
                    this
                );
            }
            return;
        }

        if (!evaluationInProgress)
        {
            if (debugLog)
            {
                Debug.Log(
                    $"[GainSearchFlowController] NotifyCurrentEvaluationFinished ignored. " +
                    $"No evaluation in progress. Phase={phase}, Pending={evaluationPending}",
                    this
                );
            }
            return;
        }

        string phaseBeforeFinish = phase.ToString();
        float testThetaBeforeFinish = currentTestThetaDeg;
        float safeThetaBeforeFinish = currentSafeThetaDeg;
        bool noticedBeforeFinish = noticedDuringCurrentEvaluation;

        bool injectionActuallyStarted =
            injectionController != null &&
            injectionController.CurrentEvaluationInjectionStarted;

        if (!injectionActuallyStarted)
        {
            if (eventLogger != null && maskingEventManager != null)
            {
                eventLogger.LogEvaluation(
                    "EVAL_FINISH_INVALID_NO_INJECTION",
                    BuildCurrentConditionName(),
                    maskingEventManager.CurrentOcclusionRatio,
                    phaseBeforeFinish,
                    testThetaBeforeFinish,
                    safeThetaBeforeFinish,
                    noticedBeforeFinish
                );

                string outcome = injectionController != null
                    ? injectionController.CurrentEvaluationInjectionOutcome
                    : "NO_INJECTION_CONTROLLER";

                eventLogger.LogSearchEvent(
                    "EVAL_RETRY_NO_INJECTION",
                    BuildCurrentConditionName(),
                    maskingEventManager.CurrentOcclusionRatio,
                    phaseBeforeFinish,
                    testThetaBeforeFinish,
                    safeThetaBeforeFinish,
                    $"outcome={outcome}"
                );
            }

            noticedDuringCurrentEvaluation = false;
            evaluationInProgress = false;
            evaluationPending = true;

            autoFinishScheduled = false;
            autoFinishTime = -1f;

            if (participantFeedback != null)
                participantFeedback.ResetState(true);

            if (debugLog)
            {
                string outcome = injectionController != null
                    ? injectionController.CurrentEvaluationInjectionOutcome
                    : "NO_INJECTION_CONTROLLER";

                Debug.LogWarning(
                    $"[GainSearchFlowController] Evaluation invalid: no injection started. " +
                    $"Outcome={outcome}. Theta not updated.",
                    this
                );
            }

            return;
        }

        switch (phase)
        {
            case SearchPhase.Coarse:
                HandleCoarseEvaluationFinished();
                break;

            case SearchPhase.Fine:
                HandleFineEvaluationFinished();
                break;

            case SearchPhase.Confirm:
                HandleConfirmEvaluationFinished();
                break;
        }

        if (eventLogger != null && maskingEventManager != null)
        {
            eventLogger.LogEvaluation(
                "EVAL_FINISH",
                BuildCurrentConditionName(),
                maskingEventManager.CurrentOcclusionRatio,
                phaseBeforeFinish,
                testThetaBeforeFinish,
                safeThetaBeforeFinish,
                noticedBeforeFinish
            );
        }

        noticedDuringCurrentEvaluation = false;
        evaluationInProgress = false;

        autoFinishScheduled = false;
        autoFinishTime = -1f;

        if (phase != SearchPhase.Idle && phase != SearchPhase.Finished)
            evaluationPending = true;
        else
            evaluationPending = false;

        ApplyCurrentTestThetaToInjection();

        if (participantFeedback != null)
            participantFeedback.ResetState(true);

        if (debugLog)
        {
            Debug.Log(
                $"[GainSearchFlowController] Evaluation finished. " +
                $"Phase={phase}, Safe={currentSafeThetaDeg}, Test={currentTestThetaDeg}, " +
                $"Pending={evaluationPending}, InProgress={evaluationInProgress}",
                this
            );
        }
    }

    public void StopSearch()
    {
        if (eventLogger != null && maskingEventManager != null)
        {
            eventLogger.LogSearchEvent(
                "SEARCH_STOP",
                BuildCurrentConditionName(),
                maskingEventManager.CurrentOcclusionRatio,
                phase.ToString(),
                currentTestThetaDeg,
                currentSafeThetaDeg
            );
        }

        phase = SearchPhase.Finished;
        evaluationPending = false;
        evaluationInProgress = false;
        noticedDuringCurrentEvaluation = false;

        autoFinishScheduled = false;
        autoFinishTime = -1f;

        if (participantFeedback != null)
            participantFeedback.ResetState(true);

        if (recordTriggerController != null)
            recordTriggerController.ResetTriggerState();

        if (debugLog)
        {
            Debug.Log("[GainSearchFlowController] Search stopped manually.", this);
        }
    }

    public void ResetSearch()
    {
        if (eventLogger != null && maskingEventManager != null)
        {
            eventLogger.LogSearchEvent(
                "SEARCH_RESET",
                BuildCurrentConditionName(),
                maskingEventManager.CurrentOcclusionRatio,
                phase.ToString(),
                currentTestThetaDeg,
                currentSafeThetaDeg
            );
        }

        phase = SearchPhase.Idle;
        currentSafeThetaDeg = startThetaDeg;
        currentTestThetaDeg = startThetaDeg;
        confirmedUpperAcceptableThetaDeg = 0f;
        hasConfirmedUpperAcceptableTheta = false;
        noticedDuringCurrentEvaluation = false;

        evaluationPending = false;
        evaluationInProgress = false;

        autoFinishScheduled = false;
        autoFinishTime = -1f;

        ApplyCurrentTestThetaToInjection();

        if (participantFeedback != null)
            participantFeedback.ResetState(true);

        if (injectionController != null)
            injectionController.ResetDirectionCache();

        if (recordTriggerController != null)
            recordTriggerController.ResetTriggerState();

        if (debugLog)
        {
            Debug.Log(
                $"[GainSearchFlowController] Search reset. " +
                $"Phase={phase}, Safe={currentSafeThetaDeg}, Test={currentTestThetaDeg}",
                this
            );
        }
    }

    private void HandleOcclusionEnded()
    {
        if (!autoFinishAfterOcclusionEnd)
            return;

        if (!evaluationInProgress)
            return;

        autoFinishScheduled = true;
        autoFinishTime = Time.time + autoFinishDelaySec;

        if (debugLog)
        {
            Debug.Log(
                $"[GainSearchFlowController] Occlusion ended -> auto finish scheduled at t={autoFinishTime:F2}",
                this
            );
        }
    }

    private void HandleCoarseEvaluationFinished()
    {
        if (!noticedDuringCurrentEvaluation)
        {
            currentSafeThetaDeg = currentTestThetaDeg;
            currentTestThetaDeg += coarseStepDeg;

            if (debugLog)
            {
                Debug.Log(
                    $"[GainSearchFlowController] Coarse accepted. " +
                    $"New Safe={currentSafeThetaDeg}, Next Test={currentTestThetaDeg}",
                    this
                );
            }
        }
        else
        {
            if (eventLogger != null && maskingEventManager != null)
            {
                eventLogger.LogSearchEvent(
                    "PHASE_CHANGE_COARSE_TO_FINE",
                    BuildCurrentConditionName(),
                    maskingEventManager.CurrentOcclusionRatio,
                    phase.ToString(),
                    currentTestThetaDeg,
                    currentSafeThetaDeg
                );
            }

            phase = SearchPhase.Fine;
            currentTestThetaDeg = currentSafeThetaDeg + fineStepDeg;

            if (debugLog)
            {
                Debug.Log(
                    $"[GainSearchFlowController] Coarse noticed -> switch to Fine. " +
                    $"Safe={currentSafeThetaDeg}, Next Test={currentTestThetaDeg}",
                    this
                );
            }
        }
    }

    private void HandleFineEvaluationFinished()
    {
        if (!noticedDuringCurrentEvaluation)
        {
            currentSafeThetaDeg = currentTestThetaDeg;
            currentTestThetaDeg += fineStepDeg;

            if (debugLog)
            {
                Debug.Log(
                    $"[GainSearchFlowController] Fine accepted. " +
                    $"New Safe={currentSafeThetaDeg}, Next Test={currentTestThetaDeg}",
                    this
                );
            }
        }
        else
        {
            if (eventLogger != null && maskingEventManager != null)
            {
                eventLogger.LogSearchEvent(
                    "PHASE_CHANGE_FINE_TO_CONFIRM",
                    BuildCurrentConditionName(),
                    maskingEventManager.CurrentOcclusionRatio,
                    phase.ToString(),
                    currentTestThetaDeg,
                    currentSafeThetaDeg
                );
            }

            phase = SearchPhase.Confirm;
            currentTestThetaDeg = currentSafeThetaDeg;

            if (debugLog)
            {
                Debug.Log(
                    $"[GainSearchFlowController] Fine noticed -> switch to Confirm. " +
                    $"Confirm Test={currentTestThetaDeg}",
                    this
                );
            }
        }
    }

    private void HandleConfirmEvaluationFinished()
    {
        if (!noticedDuringCurrentEvaluation)
        {
            confirmedUpperAcceptableThetaDeg = currentTestThetaDeg;
            hasConfirmedUpperAcceptableTheta = true;

            if (eventLogger != null && maskingEventManager != null)
            {
                eventLogger.LogSearchEvent(
                    "CONFIRM_ACCEPTED",
                    BuildCurrentConditionName(),
                    maskingEventManager.CurrentOcclusionRatio,
                    phase.ToString(),
                    currentTestThetaDeg,
                    currentSafeThetaDeg,
                    $"confirmed={confirmedUpperAcceptableThetaDeg:F3}"
                );
            }

            phase = SearchPhase.Finished;

            if (debugLog)
            {
                Debug.Log(
                    $"[GainSearchFlowController] Confirm accepted. " +
                    $"Confirmed upper acceptable theta = {confirmedUpperAcceptableThetaDeg}",
                    this
                );
            }
        }
        else
        {
            confirmedUpperAcceptableThetaDeg = Mathf.Max(0f, currentTestThetaDeg - fineStepDeg);
            hasConfirmedUpperAcceptableTheta = true;

            currentSafeThetaDeg = confirmedUpperAcceptableThetaDeg;
            currentTestThetaDeg = confirmedUpperAcceptableThetaDeg;

            if (eventLogger != null && maskingEventManager != null)
            {
                eventLogger.LogSearchEvent(
                    "CONFIRM_FALLBACK",
                    BuildCurrentConditionName(),
                    maskingEventManager.CurrentOcclusionRatio,
                    phase.ToString(),
                    currentTestThetaDeg,
                    currentSafeThetaDeg,
                    $"confirmed={confirmedUpperAcceptableThetaDeg:F3}"
                );
            }

            phase = SearchPhase.Finished;

            if (debugLog)
            {
                Debug.LogWarning(
                    $"[GainSearchFlowController] Confirm noticed. Conservative fallback -> " +
                    $"Confirmed upper acceptable theta = {confirmedUpperAcceptableThetaDeg}",
                    this
                );
            }
        }
    }

    private void ApplyCurrentTestThetaToInjection()
    {
        if (injectionController == null)
            return;

        injectionController.SetCurrentEventThetaDeg(currentTestThetaDeg);

        if (debugLog)
        {
            Debug.Log(
                $"[GainSearchFlowController] Applied test theta to injection controller: {currentTestThetaDeg}",
                this
            );
        }
    }

    private string BuildCurrentConditionName()
    {
        if (maskingEventManager == null)
            return "Unknown";

        string typeName = (maskingEventManager.CurrentOccluderType == MaskingEventManager.TrialOccluderType.Newspaper)
            ? "Newspaper"
            : "Pigeon";

        return $"{typeName}{maskingEventManager.CurrentOcclusionRatio}";
    }
}