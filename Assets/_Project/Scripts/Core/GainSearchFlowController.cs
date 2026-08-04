using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

public class GainSearchFlowController : MonoBehaviour
{
    public enum SearchPhase
    {
        Idle,
        Staircase,
        Finished
    }

    public enum StaircaseStartMode
    {
        HighStart,
        LowStart,
        Custom
    }

    [Header("References")]
    [SerializeField] private ParticipantFeedbackController participantFeedback;
    [SerializeField] private RotationInjectionController injectionController;
    [SerializeField] private GainRecordTriggerController recordTriggerController;
    [SerializeField] private MaskingEventManager maskingEventManager;
    [SerializeField] private EventLogger eventLogger;

    [Header("Staircase Start")]
    [SerializeField] private StaircaseStartMode startMode = StaircaseStartMode.HighStart;
    [SerializeField] private float highStartThetaDeg = 20f;
    [SerializeField] private float lowStartThetaDeg = 10f;
    [SerializeField] private float customStartThetaDeg = 20f;

    [Header("Staircase Parameters")]
    [SerializeField] private float initialStepDeg = 2f;
    [SerializeField] private float finalStepDeg = 1f;
    [SerializeField] private int stepReductionAfterReversals = 2;
    [SerializeField] private int targetReversalCount = 8;
    [SerializeField] private int ignoreFirstReversals = 2;
    [SerializeField] private int maxValidTrials = 30;
    [SerializeField] private float minThetaDeg = 0f;
    [SerializeField] private float maxThetaDeg = 25f;

    [Header("Auto Finish Evaluation")]
    [Tooltip("Evaluation is automatically finished this many seconds after occlusion ends. Feedback is only counted after injection starts.")]
    [SerializeField] private bool autoFinishAfterOcclusionEnd = true;
    [SerializeField] private float autoFinishDelaySec = 1.0f;

    [Header("Debug Keyboard")]
    [SerializeField] private bool enableDebugKeyboard = true;
    [SerializeField] private KeyCode startSearchKey = KeyCode.F5;
    [SerializeField] private KeyCode finishEvaluationKey = KeyCode.F6;
    [SerializeField] private KeyCode resetSearchKey = KeyCode.F7;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    public SearchPhase Phase => phase;
    public StaircaseStartMode StartMode => startMode;
    public float CurrentTestThetaDeg => currentTestThetaDeg;
    public float CurrentStepDeg => currentStepDeg;
    public int ValidTrialCount => validTrialCount;
    public int ReversalCount => reversalThetas.Count;
    public int TargetReversalCount => targetReversalCount;
    public int MaxValidTrials => Mathf.Max(1, maxValidTrials);
    public float EstimatedThresholdDeg => estimatedThresholdDeg;
    public bool HasEstimatedThreshold => hasEstimatedThreshold;
    public bool ThresholdReliable => thresholdReliable;
    public string StopReason => stopReason;

    // Backward-compatible names used by old UI/controller code.
    // After the staircase refactor, these represent the estimated staircase threshold.
    public float CurrentSafeThetaDeg => hasEstimatedThreshold ? estimatedThresholdDeg : 0f;
    public float ConfirmedUpperAcceptableThetaDeg => estimatedThresholdDeg;
    public bool HasConfirmedUpperAcceptableTheta => hasEstimatedThreshold;

    public bool NeedsEvaluationTrigger => evaluationPending && !evaluationInProgress;
    public bool IsEvaluationInProgress => evaluationInProgress;

    private SearchPhase phase = SearchPhase.Idle;

    private float currentTestThetaDeg;
    private float currentStepDeg;
    private float lastStaircaseDeltaDeg = 0f;

    private readonly List<float> reversalThetas = new List<float>();
    private int validTrialCount = 0;

    private float estimatedThresholdDeg = 0f;
    private bool hasEstimatedThreshold = false;
    private bool thresholdReliable = false;
    private string stopReason = "NONE";

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

        currentTestThetaDeg = GetConfiguredStartTheta();
        currentStepDeg = Mathf.Max(0.001f, initialStepDeg);
        ApplyCurrentTestThetaToInjection();
    }

    private void Update()
    {
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
            if (!evaluationInProgress)
            {
                if (debugLog)
                {
                    Debug.Log(
                        $"[GainSearchFlowController] Feedback ignored because no evaluation is in progress. " +
                        $"Phase={phase}, Pending={evaluationPending}, InProgress={evaluationInProgress}",
                        this
                    );
                }

                return;
            }

            // Backend feedback window: feedback is counted only after the injection has actually started.
            // This avoids counting reactions to the occluder itself before the redirection event begins.
            if (injectionController == null || !injectionController.CurrentEvaluationInjectionStarted)
            {
                if (debugLog)
                {
                    string outcome = injectionController != null
                        ? injectionController.CurrentEvaluationInjectionOutcome
                        : "NO_INJECTION_CONTROLLER";

                    Debug.Log(
                        $"[GainSearchFlowController] Feedback ignored before injection start. Outcome={outcome}",
                        this
                    );
                }

                return;
            }

            noticedDuringCurrentEvaluation = true;

            if (debugLog)
            {
                Debug.Log(
                    $"[GainSearchFlowController] Feedback noticed during current evaluation. Phase={phase}",
                    this
                );
            }
        }
    }

    public void StartSearch()
    {
        phase = SearchPhase.Staircase;

        currentTestThetaDeg = GetConfiguredStartTheta();
        currentStepDeg = Mathf.Max(0.001f, initialStepDeg);
        lastStaircaseDeltaDeg = 0f;

        reversalThetas.Clear();
        validTrialCount = 0;

        estimatedThresholdDeg = 0f;
        hasEstimatedThreshold = false;
        thresholdReliable = false;
        stopReason = "RUNNING";

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

        if (debugLog)
        {
            Debug.Log(
                $"[GainSearchFlowController] Staircase started. " +
                $"StartMode={startMode}, Test={currentTestThetaDeg}, Step={currentStepDeg}, " +
                $"TargetReversals={targetReversalCount}, MaxValidTrials={Mathf.Max(1, maxValidTrials)}, Pending={evaluationPending}",
                this
            );
        }
    }

    public string BuildStaircaseStartExtra()
    {
        return
            $"searchMethod=Staircase;" +
            $"startMode={startMode};" +
            $"startThetaDeg={FormatFloat(GetConfiguredStartTheta())};" +
            $"initialStepDeg={FormatFloat(initialStepDeg)};" +
            $"finalStepDeg={FormatFloat(finalStepDeg)};" +
            $"stepReductionAfterReversals={stepReductionAfterReversals};" +
            $"targetReversalCount={targetReversalCount};" +
            $"ignoreFirstReversals={ignoreFirstReversals};" +
            $"maxValidTrials={Mathf.Max(1, maxValidTrials)};" +
            $"minThetaDeg={FormatFloat(minThetaDeg)};" +
            $"maxThetaDeg={FormatFloat(maxThetaDeg)}";
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
            eventLogger.LogStaircaseEvaluation(
                "STAIRCASE_EVAL_START",
                maskingEventManager,
                currentTestThetaDeg,
                false,
                null,
                currentStepDeg,
                0f,
                currentTestThetaDeg,
                false,
                0,
                reversalThetas.Count
            );
        }

        if (debugLog)
        {
            Debug.Log(
                $"[GainSearchFlowController] Staircase evaluation started. " +
                $"Test={currentTestThetaDeg}, Step={currentStepDeg}, Pending={evaluationPending}, InProgress={evaluationInProgress}",
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

        float testThetaBeforeFinish = currentTestThetaDeg;
        bool noticedBeforeFinish = noticedDuringCurrentEvaluation;

        bool injectionActuallyStarted =
            injectionController != null &&
            injectionController.CurrentEvaluationInjectionStarted;

        if (!injectionActuallyStarted)
        {
            string outcome = injectionController != null
                ? injectionController.CurrentEvaluationInjectionOutcome
                : "NO_INJECTION_CONTROLLER";

            if (eventLogger != null && maskingEventManager != null)
            {
                eventLogger.LogStaircaseEvaluation(
                    "STAIRCASE_EVAL_FINISH",
                    maskingEventManager,
                    testThetaBeforeFinish,
                    noticedBeforeFinish,
                    false,
                    currentStepDeg,
                    0f,
                    currentTestThetaDeg,
                    false,
                    0,
                    reversalThetas.Count,
                    "NO_INJECTION_STARTED",
                    $"injectionOutcome={outcome};retrySameTheta=true"
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
                Debug.LogWarning(
                    $"[GainSearchFlowController] Evaluation invalid: no injection started. " +
                    $"Outcome={outcome}. Theta not updated.",
                    this
                );
            }

            return;
        }

        ProcessValidStaircaseEvaluation(testThetaBeforeFinish, noticedBeforeFinish);

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
                $"[GainSearchFlowController] Staircase evaluation finished. " +
                $"Phase={phase}, Test={currentTestThetaDeg}, Step={currentStepDeg}, " +
                $"ValidTrials={validTrialCount}, Reversals={reversalThetas.Count}, " +
                $"Pending={evaluationPending}, InProgress={evaluationInProgress}",
                this
            );
        }
    }

    public void StopSearch(string reason = "STOP_SEARCH")
    {
        phase = SearchPhase.Finished;
        stopReason = string.IsNullOrEmpty(reason) ? "STOP_SEARCH" : reason;
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
            Debug.Log($"[GainSearchFlowController] Search stopped manually. Reason={stopReason}", this);
        }
    }

    public void ResetSearch()
    {
        ResetSearch("SearchReset", true);
    }

    public void ResetSearch(string resetReason, bool logReset)
    {
        if (logReset && eventLogger != null && maskingEventManager != null)
        {
            eventLogger.LogResetEvent(
                "MANUAL_RESET",
                maskingEventManager,
                string.IsNullOrEmpty(resetReason) ? "SearchReset" : resetReason
            );
        }

        phase = SearchPhase.Idle;
        currentTestThetaDeg = GetConfiguredStartTheta();
        currentStepDeg = Mathf.Max(0.001f, initialStepDeg);
        lastStaircaseDeltaDeg = 0f;

        reversalThetas.Clear();
        validTrialCount = 0;

        estimatedThresholdDeg = 0f;
        hasEstimatedThreshold = false;
        thresholdReliable = false;
        stopReason = "RESET";

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
                $"[GainSearchFlowController] Search reset. Reason={resetReason}. " +
                $"Phase={phase}, Test={currentTestThetaDeg}, Step={currentStepDeg}",
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

    private void ProcessValidStaircaseEvaluation(float testThetaBeforeFinish, bool noticedBeforeFinish)
    {
        validTrialCount++;

        int deltaSign = noticedBeforeFinish ? -1 : 1;
        bool isReversal = Mathf.Abs(lastStaircaseDeltaDeg) > 0.0001f &&
                          Mathf.Sign(lastStaircaseDeltaDeg) != deltaSign;

        int reversalIndex = 0;
        if (isReversal)
        {
            reversalThetas.Add(testThetaBeforeFinish);
            reversalIndex = reversalThetas.Count;
        }

        currentStepDeg = reversalThetas.Count >= Mathf.Max(0, stepReductionAfterReversals)
            ? Mathf.Max(0.001f, finalStepDeg)
            : Mathf.Max(0.001f, initialStepDeg);

        float staircaseDeltaDeg = deltaSign * currentStepDeg;
        float nextThetaDeg = Mathf.Clamp(testThetaBeforeFinish + staircaseDeltaDeg, minThetaDeg, maxThetaDeg);

        if (eventLogger != null && maskingEventManager != null)
        {
            eventLogger.LogStaircaseEvaluation(
                "STAIRCASE_EVAL_FINISH",
                maskingEventManager,
                testThetaBeforeFinish,
                noticedBeforeFinish,
                true,
                currentStepDeg,
                staircaseDeltaDeg,
                nextThetaDeg,
                isReversal,
                reversalIndex,
                reversalThetas.Count
            );

            if (isReversal)
            {
                eventLogger.LogStaircaseEvaluation(
                    "STAIRCASE_REVERSAL",
                    maskingEventManager,
                    testThetaBeforeFinish,
                    noticedBeforeFinish,
                    true,
                    currentStepDeg,
                    staircaseDeltaDeg,
                    nextThetaDeg,
                    true,
                    reversalIndex,
                    reversalThetas.Count
                );
            }
        }

        lastStaircaseDeltaDeg = staircaseDeltaDeg;
        currentTestThetaDeg = nextThetaDeg;

        if (reversalThetas.Count >= targetReversalCount)
        {
            FinishStaircase("TARGET_REVERSALS_REACHED", true);
            return;
        }

        int effectiveMaxValidTrials = Mathf.Max(1, maxValidTrials);
        if (validTrialCount >= effectiveMaxValidTrials)
        {
            FinishStaircase("MAX_VALID_TRIALS_REACHED", false);
        }
    }

    private void FinishStaircase(string reason, bool reliable)
    {
        stopReason = reason;
        thresholdReliable = reliable;

        hasEstimatedThreshold = TryComputeEstimatedThreshold(out estimatedThresholdDeg);

        if (!hasEstimatedThreshold)
        {
            estimatedThresholdDeg = 0f;
            thresholdReliable = false;
        }

        phase = SearchPhase.Finished;
        evaluationPending = false;
        evaluationInProgress = false;

        if (eventLogger != null && maskingEventManager != null)
        {
            eventLogger.LogStaircaseResult(
                "STAIRCASE_RESULT",
                maskingEventManager,
                estimatedThresholdDeg,
                BuildUsedReversalsString(),
                BuildAllReversalsString(),
                validTrialCount,
                reversalThetas.Count,
                hasEstimatedThreshold && thresholdReliable,
                stopReason,
                $"thresholdReliable={thresholdReliable.ToString().ToLowerInvariant()};hasEstimatedThreshold={hasEstimatedThreshold.ToString().ToLowerInvariant()}"
            );
        }

        if (debugLog)
        {
            Debug.Log(
                $"[GainSearchFlowController] Staircase finished. " +
                $"Reason={stopReason}, HasThreshold={hasEstimatedThreshold}, Reliable={thresholdReliable}, " +
                $"Threshold={estimatedThresholdDeg:F3}, ValidTrials={validTrialCount}, Reversals={reversalThetas.Count}",
                this
            );
        }
    }

    private bool TryComputeEstimatedThreshold(out float threshold)
    {
        threshold = 0f;

        int startIndex = Mathf.Clamp(ignoreFirstReversals, 0, reversalThetas.Count);
        int usedCount = reversalThetas.Count - startIndex;
        if (usedCount <= 0)
            return false;

        float sum = 0f;
        for (int i = startIndex; i < reversalThetas.Count; i++)
            sum += reversalThetas[i];

        threshold = sum / usedCount;
        return true;
    }

    private string BuildUsedReversalsString()
    {
        int startIndex = Mathf.Clamp(ignoreFirstReversals, 0, reversalThetas.Count);
        return BuildReversalString(startIndex, reversalThetas.Count);
    }

    private string BuildAllReversalsString()
    {
        return BuildReversalString(0, reversalThetas.Count);
    }

    private string BuildReversalString(int startInclusive, int endExclusive)
    {
        if (reversalThetas.Count == 0 || startInclusive >= endExclusive)
            return "";

        StringBuilder builder = new StringBuilder();
        for (int i = startInclusive; i < endExclusive; i++)
        {
            if (builder.Length > 0)
                builder.Append(';');
            builder.Append(FormatFloat(reversalThetas[i]));
        }

        return builder.ToString();
    }

    private float GetConfiguredStartTheta()
    {
        float theta;
        switch (startMode)
        {
            case StaircaseStartMode.LowStart:
                theta = lowStartThetaDeg;
                break;
            case StaircaseStartMode.Custom:
                theta = customStartThetaDeg;
                break;
            case StaircaseStartMode.HighStart:
            default:
                theta = highStartThetaDeg;
                break;
        }

        return Mathf.Clamp(theta, minThetaDeg, maxThetaDeg);
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

    private static string FormatFloat(float value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }
}
