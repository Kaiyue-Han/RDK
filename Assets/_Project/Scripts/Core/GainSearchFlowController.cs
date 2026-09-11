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

    public enum EvaluationTrialType
    {
        Normal,
        CatchZero,
        CatchHigh
    }

    [Header("References")]
    [SerializeField] private ParticipantFeedbackController participantFeedback;
    [SerializeField] private RotationInjectionController injectionController;
    [SerializeField] private GainRecordTriggerController recordTriggerController;
    [SerializeField] private MaskingEventManager maskingEventManager;
    [SerializeField] private EventLogger eventLogger;

    [Header("Staircase Start")]
    [SerializeField] private StaircaseStartMode startMode = StaircaseStartMode.HighStart;
    [SerializeField] private float highStartThetaDeg = 4f;
    [SerializeField] private float lowStartThetaDeg = 4f;
    [SerializeField] private float customStartThetaDeg = 4f;

    [Header("Staircase Parameters")]
    [SerializeField] private float initialStepDeg = 2f;
    [SerializeField] private float finalStepDeg = 1f;
    [SerializeField] private int stepReductionAfterReversals = 2;
    [SerializeField] private int targetReversalCount = 12;
    [SerializeField] private int ignoreFirstReversals = 2;
    [Tooltip("Minimum reversals required to report an estimate when the valid-trial cap is reached before the target reversal count.")]
    [SerializeField] private int minimumUsableReversals = 1;
    [SerializeField] private int maxValidTrials = 30;
    [SerializeField] private float minThetaDeg = 0f;
    [SerializeField] private float maxThetaDeg = 15f;
    [Tooltip("Mark the estimate as boundary-limited after this many consecutive valid normal evaluations attempt to move beyond the same theta boundary.")]
    [Min(1)]
    [SerializeField] private int boundaryLimitedAfterConsecutiveClampedTrials = 3;

    [Header("Catch Events")]
    [Tooltip("Insert control trials that do not update the staircase.")]
    [SerializeField] private bool enableCatchEvents = true;

    [Tooltip("Minimum number of VALID normal staircase trials between catch events.")]
    [Min(1)]
    [SerializeField] private int minNormalTrialsBetweenCatch = 7;

    [Tooltip("Maximum number of VALID normal staircase trials between catch events. The actual interval is randomized inclusively.")]
    [Min(1)]
    [SerializeField] private int maxNormalTrialsBetweenCatch = 9;

    [Tooltip("Extra event theta used by the high catch. It is clamped to the injection hard maximum. Zero catches always use 0 degrees.")]
    [Min(0f)]
    [SerializeField] private float highCatchThetaDeg = 15f;

    [Header("Formal Response Window")]
    [Tooltip("Deadline is actual injection start plus this duration. The visual event may end before the response window.")]
    [Min(0.1f)]
    [SerializeField] private float responseWindowSec = 2.0f;

    public float ResponseWindowSec => Mathf.Max(0.1f, responseWindowSec);

    [Header("Event-related walking-speed summary")]
    [Min(0.1f)]
    [SerializeField] private float preEventSpeedWindowSec = 1.0f;
    [Min(0.1f)]
    [SerializeField] private float postEventSpeedWindowSec = 1.0f;

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
    public int MinimumUsableReversals => Mathf.Max(ignoreFirstReversals + 1, minimumUsableReversals);
    public int MaxValidTrials => Mathf.Max(1, maxValidTrials);
    public float EstimatedThresholdDeg => estimatedThresholdDeg;
    public bool HasEstimatedThreshold => hasEstimatedThreshold;
    public bool ThresholdReliable => thresholdReliable;
    public bool BoundaryLimitedEstimate => boundaryLimitedEstimate;
    public string StopReason => stopReason;

    public EvaluationTrialType CurrentEvaluationTrialType => currentEvaluationTrialType;
    public bool CurrentEvaluationIsCatch => currentEvaluationTrialType != EvaluationTrialType.Normal;
    public float CurrentPlannedThetaDeg => currentPlannedThetaDeg;

    // Catch-event summary values exposed to the experimenter result UI.
    // Catch trials remain excluded from staircase updates.
    public bool CatchEventsEnabled => enableCatchEvents;
    public int ZeroCatchCount => zeroCatchCount;
    public int ZeroCatchFalseAlarmCount => zeroCatchFalseAlarmCount;
    public float ZeroCatchFalseAlarmRate => zeroCatchCount > 0
        ? (float)zeroCatchFalseAlarmCount / zeroCatchCount
        : 0f;
    public int HighCatchCount => highCatchCount;
    public int HighCatchHitCount => highCatchHitCount;
    public float HighCatchHitRate => highCatchCount > 0
        ? (float)highCatchHitCount / highCatchCount
        : 0f;

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
    private bool boundaryLimitedEstimate = false;
    private int consecutiveBoundaryClampedTrials = 0;
    private string stopReason = "NONE";

    private bool noticedDuringCurrentEvaluation = false;

    private bool evaluationPending = false;
    private bool evaluationInProgress = false;

    private bool responseWindowOpen;
    private bool responseAccepted;
    private float responseTime = float.NaN;
    private float responseDeadline = float.NaN;

    // Catch-event runtime state. Catch trials are interleaved with the staircase
    // but never change theta, reversal count, or validTrialCount.
    private EvaluationTrialType currentEvaluationTrialType = EvaluationTrialType.Normal;
    private float currentPlannedThetaDeg = 0f;
    private int normalTrialsSinceLastCatch = 0;
    private int nextCatchAfterNormalTrials = 7;
    private int catchSequenceIndex = 0;

    private int zeroCatchCount = 0;
    private int zeroCatchFalseAlarmCount = 0;
    private int highCatchCount = 0;
    private int highCatchHitCount = 0;

    private struct SpeedSample
    {
        public float time;
        public float speed;

        public SpeedSample(float time, float speed)
        {
            this.time = time;
            this.speed = speed;
        }
    }

    private struct SpeedAccumulator
    {
        public float weightedSum;
        public float duration;
        public float minimum;
        public bool hasSample;

        public void Add(float speed, float seconds)
        {
            if (seconds <= 0f)
                return;

            weightedSum += Mathf.Max(0f, speed) * seconds;
            duration += seconds;
            minimum = hasSample ? Mathf.Min(minimum, speed) : Mathf.Max(0f, speed);
            hasSample = true;
        }

        public float Mean => duration > 1e-5f ? weightedSum / duration : 0f;
        public float Min => hasSample ? minimum : 0f;
    }

    private readonly List<SpeedSample> recentSpeedSamples = new List<SpeedSample>(128);
    private bool walkingSummaryActive;
    private float walkingSummaryEventStart;
    private float walkingSummaryEventEnd;
    private float walkingSummaryPostEnd;
    private SpeedAccumulator preEventSpeed;
    private SpeedAccumulator duringEventSpeed;
    private SpeedAccumulator postEventSpeed;
    private EventLogger.EvaluationSnapshot walkingSummaryEvaluationSnapshot;

    private void OnEnable()
    {
        if (maskingEventManager != null)
        {
            maskingEventManager.OnOcclusionEnded += HandleOcclusionEnded;
            maskingEventManager.OnFormalEventStarted += HandleFormalEventStartedForWalkingSummary;
        }

        if (injectionController != null)
        {
            injectionController.OnInjectionStarted += HandleInjectionStarted;
            injectionController.OnInjectionCompleted += HandleInjectionCompleted;
            injectionController.OnEvaluationInvalidated += HandleEvaluationInvalidated;
        }
    }

    private void OnDisable()
    {
        if (maskingEventManager != null)
        {
            maskingEventManager.OnOcclusionEnded -= HandleOcclusionEnded;
            maskingEventManager.OnFormalEventStarted -= HandleFormalEventStartedForWalkingSummary;
        }

        if (injectionController != null)
        {
            injectionController.OnInjectionStarted -= HandleInjectionStarted;
            injectionController.OnInjectionCompleted -= HandleInjectionCompleted;
            injectionController.OnEvaluationInvalidated -= HandleEvaluationInvalidated;
        }
    }

    private void Start()
    {
        Debug.Log("[GainSearchFlowController] Start called.", this);

        currentTestThetaDeg = GetConfiguredStartTheta();
        currentStepDeg = Mathf.Max(0.001f, initialStepDeg);
        ResetCatchState();
        PlanNextEvaluation();
        ApplyCurrentPlannedThetaToInjection();
    }

    private void Update()
    {
        UpdateWalkingSpeedSummary();

        if (enableDebugKeyboard)
        {
            if (Input.GetKeyDown(startSearchKey))
                StartSearch();

            if (Input.GetKeyDown(finishEvaluationKey))
                NotifyCurrentEvaluationFinished();

            if (Input.GetKeyDown(resetSearchKey))
                ResetSearch();
        }
    }

    private void LateUpdate()
    {
        // ParticipantFeedbackController samples XR input in Update. Processing in
        // LateUpdate makes a button report from the deadline frame available before
        // the no-response decision is made, independent of Update execution order.
        if (participantFeedback == null)
            return;

        if (
            responseWindowOpen &&
            !responseAccepted &&
            participantFeedback.TryConsumeFeedback(out float feedbackTime)
        )
        {
            if (feedbackTime <= responseDeadline)
            {
                AcceptResponse(feedbackTime);
            }
        }

        if (responseWindowOpen && !responseAccepted && Time.time >= responseDeadline)
        {
            responseWindowOpen = false;
            noticedDuringCurrentEvaluation = false;
            FormalExperimentContext.RecordNoResponse();
            participantFeedback.DisableListening();

            if (eventLogger != null && maskingEventManager != null)
                eventLogger.Mark("RESPONSE_TIMEOUT", maskingEventManager);

            TryFinishEvaluationAfterResponse();
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
        boundaryLimitedEstimate = false;
        consecutiveBoundaryClampedTrials = 0;
        stopReason = "RUNNING";

        noticedDuringCurrentEvaluation = false;

        evaluationPending = true;
        evaluationInProgress = false;

        ResetResponseState();

        if (participantFeedback != null)
            participantFeedback.ResetState(false);

        if (injectionController != null)
            injectionController.ResetDirectionCache();

        ResetCatchState();
        PlanNextEvaluation();
        ApplyCurrentPlannedThetaToInjection();

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
            $"minimumUsableReversals={MinimumUsableReversals};" +
            $"maxValidTrials={Mathf.Max(1, maxValidTrials)};" +
            $"minThetaDeg={FormatFloat(minThetaDeg)};" +
            $"maxThetaDeg={FormatFloat(maxThetaDeg)};" +
            $"boundaryLimitedAfterConsecutiveClampedTrials={Mathf.Max(1, boundaryLimitedAfterConsecutiveClampedTrials)};" +
            $"catchEventsEnabled={enableCatchEvents.ToString().ToLowerInvariant()};" +
            $"catchIntervalValidNormalTrials={Mathf.Max(1, minNormalTrialsBetweenCatch)}-{Mathf.Max(Mathf.Max(1, minNormalTrialsBetweenCatch), maxNormalTrialsBetweenCatch)};" +
            $"zeroCatchThetaDeg=0.000;" +
            $"highCatchThetaDeg={FormatFloat(GetClampedHighCatchThetaDeg())};" +
            $"catchTypePattern=ZERO-HIGH-ALTERNATING";
    }

    public bool NotifyEvaluationTriggered(float walkingSpeedAtTriggerMps)
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
            return false;
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
            return false;
        }

        string trialType = GetTrialTypeLogName(currentEvaluationTrialType);
        if (!FormalExperimentContext.BeginEvaluation(
                trialType,
                currentPlannedThetaDeg,
                walkingSpeedAtTriggerMps
            ))
        {
            Debug.LogError(
                "[GainSearchFlowController] Formal run identifiers are not active.",
                this
            );
            return false;
        }

        evaluationPending = false;
        evaluationInProgress = true;
        noticedDuringCurrentEvaluation = false;

        if (injectionController != null)
            injectionController.BeginEvaluationInjectionTracking();

        ResetResponseState();

        if (participantFeedback != null)
            participantFeedback.ResetState(false);

        if (eventLogger != null && maskingEventManager != null)
        {
            if (currentEvaluationTrialType == EvaluationTrialType.Normal)
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
                    reversalThetas.Count,
                    "",
                    "trialType=NORMAL"
                );
            }
            else
            {
                eventLogger.LogCatchEvaluation(
                    "CATCH_EVAL_START",
                    maskingEventManager,
                    GetTrialTypeLogName(currentEvaluationTrialType),
                    currentPlannedThetaDeg,
                    null,
                    null,
                    "",
                    currentTestThetaDeg,
                    "",
                    BuildCatchRuntimeExtra()
                );
            }
        }

        if (debugLog)
        {
            Debug.Log(
                $"[GainSearchFlowController] Evaluation started. " +
                $"TrialType={currentEvaluationTrialType}, PlannedTheta={currentPlannedThetaDeg:F2}, " +
                $"HeldStaircaseTheta={currentTestThetaDeg:F2}, Step={currentStepDeg}, " +
                $"Pending={evaluationPending}, InProgress={evaluationInProgress}",
                this
            );
        }

        return true;
    }

    public void NotifyEvaluationTriggerFailed()
    {
        if (!evaluationInProgress)
            return;

        evaluationInProgress = false;
        evaluationPending = true;
        ResetResponseState();

        if (participantFeedback != null)
            participantFeedback.ResetState(false);

        if (injectionController != null)
            injectionController.CancelActiveInjection("TRIGGER_FAILED", false);
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

        EvaluationTrialType trialTypeBeforeFinish = currentEvaluationTrialType;
        float plannedThetaBeforeFinish = currentPlannedThetaDeg;
        float staircaseThetaBeforeFinish = currentTestThetaDeg;
        bool noticedBeforeFinish = noticedDuringCurrentEvaluation;

        RefreshWalkingSummaryEvaluationSnapshot();

        if (responseWindowOpen && !responseAccepted)
        {
            if (debugLog)
                Debug.LogWarning("[GainSearchFlowController] Finish ignored while response window is open.", this);
            return;
        }

        bool injectionActuallyCompleted =
            injectionController != null &&
            injectionController.CurrentEvaluationInjectionCompleted;
        bool injectionConcludedByAcceptedResponse =
            responseAccepted &&
            injectionController != null &&
            injectionController.CurrentEvaluationConcludedByAcceptedResponse;

        if (!injectionActuallyCompleted && !injectionConcludedByAcceptedResponse)
        {
            string outcome = injectionController != null
                ? injectionController.CurrentEvaluationInjectionOutcome
                : "NO_INJECTION_CONTROLLER";

            if (eventLogger != null && maskingEventManager != null)
            {
                if (trialTypeBeforeFinish == EvaluationTrialType.Normal)
                {
                    eventLogger.LogStaircaseEvaluation(
                        "STAIRCASE_EVAL_FINISH",
                        maskingEventManager,
                        staircaseThetaBeforeFinish,
                        noticedBeforeFinish,
                        false,
                        currentStepDeg,
                        0f,
                        currentTestThetaDeg,
                        false,
                        0,
                        reversalThetas.Count,
                        "INJECTION_NOT_COMPLETED",
                        $"trialType=NORMAL;injectionOutcome={outcome};retrySameTheta=true"
                    );
                }
                else
                {
                    eventLogger.LogCatchEvaluation(
                        "CATCH_EVAL_FINISH",
                        maskingEventManager,
                        GetTrialTypeLogName(trialTypeBeforeFinish),
                        plannedThetaBeforeFinish,
                        noticedBeforeFinish,
                        false,
                        "INVALID_RETRY",
                        staircaseThetaBeforeFinish,
                        "INJECTION_NOT_COMPLETED",
                        $"injectionOutcome={outcome};retrySameCatch=true;{BuildCatchRuntimeExtra()}"
                    );
                }
            }

            noticedDuringCurrentEvaluation = false;
            evaluationInProgress = false;
            evaluationPending = true;

            ResetResponseState();

            if (participantFeedback != null)
                participantFeedback.ResetState(false);

            // Keep exactly the same planned normal/catch trial after an invalid event.
            ApplyCurrentPlannedThetaToInjection();

            if (debugLog)
            {
                Debug.LogWarning(
                    $"[GainSearchFlowController] Evaluation invalid: injection did not complete. " +
                    $"TrialType={trialTypeBeforeFinish}, Outcome={outcome}. " +
                    $"Staircase and catch schedule not updated.",
                    this
                );
            }

            return;
        }

        if (trialTypeBeforeFinish == EvaluationTrialType.Normal)
        {
            ProcessValidStaircaseEvaluation(staircaseThetaBeforeFinish, noticedBeforeFinish);
        }
        else
        {
            ProcessValidCatchEvaluation(
                trialTypeBeforeFinish,
                plannedThetaBeforeFinish,
                staircaseThetaBeforeFinish,
                noticedBeforeFinish
            );
        }

        FormalExperimentContext.CompleteTrial();

        noticedDuringCurrentEvaluation = false;
        evaluationInProgress = false;

        ResetResponseState();

        if (phase != SearchPhase.Idle && phase != SearchPhase.Finished)
        {
            evaluationPending = true;
            PlanNextEvaluation();
        }
        else
        {
            evaluationPending = false;
        }

        ApplyCurrentPlannedThetaToInjection();

        if (participantFeedback != null)
            participantFeedback.ResetState(false);

        if (debugLog)
        {
            Debug.Log(
                $"[GainSearchFlowController] Evaluation finished. " +
                $"CompletedType={trialTypeBeforeFinish}, Phase={phase}, " +
                $"NextType={currentEvaluationTrialType}, NextPlannedTheta={currentPlannedThetaDeg:F2}, " +
                $"StaircaseTheta={currentTestThetaDeg:F2}, Step={currentStepDeg}, " +
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
        walkingSummaryActive = false;
        walkingSummaryEvaluationSnapshot = default;

        ResetResponseState();

        if (participantFeedback != null)
            participantFeedback.ResetState(false);

        if (injectionController != null)
            injectionController.CancelActiveInjection(stopReason, true);

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
        boundaryLimitedEstimate = false;
        consecutiveBoundaryClampedTrials = 0;
        stopReason = "RESET";

        noticedDuringCurrentEvaluation = false;

        evaluationPending = false;
        evaluationInProgress = false;
        walkingSummaryActive = false;
        walkingSummaryEvaluationSnapshot = default;

        ResetResponseState();

        ResetCatchState();
        PlanNextEvaluation();
        ApplyCurrentPlannedThetaToInjection();

        if (participantFeedback != null)
            participantFeedback.ResetState(false);

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
        if (!evaluationInProgress)
            return;

        // A valid response window may intentionally continue after the 0.85 s
        // visual event. If injection failed or was incomplete, no response can
        // make the evaluation valid, so retry the same planned trial immediately.
        if (
            injectionController == null ||
            !injectionController.CurrentEvaluationInjectionCompleted
        )
        {
            ResetResponseState();
            if (participantFeedback != null)
                participantFeedback.ResetState(false);
            NotifyCurrentEvaluationFinished();
        }
    }

    private void HandleInjectionStarted(float actualInjectionStartTime)
    {
        if (!evaluationInProgress || responseAccepted)
            return;

        responseTime = float.NaN;
        responseDeadline = actualInjectionStartTime + ResponseWindowSec;
        responseWindowOpen = true;

        if (participantFeedback != null)
            participantFeedback.ResetState(true);

        FormalExperimentContext.RecordInjectionStart(
            actualInjectionStartTime,
            responseDeadline,
            injectionController != null
                ? injectionController.WalkingSpeedAtInjectionMps
                : 0f,
            injectionController != null
                ? injectionController.DebugMovementCongruenceState
                : "UNKNOWN"
        );

        if (eventLogger != null && maskingEventManager != null)
        {
            eventLogger.Mark(
                "RESPONSE_WINDOW_OPEN",
                maskingEventManager,
                -1f,
                $"deadline={FormatFloat(responseDeadline)};" +
                $"windowSec={FormatFloat(responseWindowSec)}"
            );
        }

        RefreshWalkingSummaryEvaluationSnapshot();
    }

    private void HandleFormalEventStartedForWalkingSummary(
        MaskingEventManager.FormalEventTiming timing
    )
    {
        if (!evaluationInProgress)
            return;

        walkingSummaryEventStart = timing.EventStartTime;
        walkingSummaryEventEnd = timing.EventEndTime;
        walkingSummaryPostEnd = timing.EventEndTime + Mathf.Max(0.1f, postEventSpeedWindowSec);
        preEventSpeed = CalculateRecentSpeedWindow(
            timing.EventStartTime - Mathf.Max(0.1f, preEventSpeedWindowSec),
            timing.EventStartTime
        );
        duringEventSpeed = default;
        postEventSpeed = default;
        RefreshWalkingSummaryEvaluationSnapshot();
        walkingSummaryActive = true;
    }

    private void UpdateWalkingSpeedSummary()
    {
        float now = Time.time;
        float speed = injectionController != null
            ? injectionController.CurrentWalkingSpeedMps
            : 0f;

        recentSpeedSamples.Add(new SpeedSample(now, speed));
        float oldestNeeded = now - Mathf.Max(0.1f, preEventSpeedWindowSec) - 0.25f;
        while (recentSpeedSamples.Count > 1 && recentSpeedSamples[1].time < oldestNeeded)
            recentSpeedSamples.RemoveAt(0);

        if (!walkingSummaryActive)
            return;

        float frameStart = Mathf.Max(0f, now - Mathf.Max(Time.deltaTime, 0f));
        AddOverlappingSpeed(
            ref duringEventSpeed,
            speed,
            frameStart,
            now,
            walkingSummaryEventStart,
            walkingSummaryEventEnd
        );
        AddOverlappingSpeed(
            ref postEventSpeed,
            speed,
            frameStart,
            now,
            walkingSummaryEventEnd,
            walkingSummaryPostEnd
        );

        if (now < walkingSummaryPostEnd)
            return;

        if (eventLogger != null && maskingEventManager != null)
        {
            eventLogger.LogWalkingSpeedSummary(
                maskingEventManager,
                walkingSummaryEvaluationSnapshot,
                preEventSpeed.Mean,
                preEventSpeed.Min,
                duringEventSpeed.Mean,
                duringEventSpeed.Min,
                postEventSpeed.Mean,
                postEventSpeed.Min,
                Mathf.Max(0.1f, preEventSpeedWindowSec),
                Mathf.Max(0f, walkingSummaryEventEnd - walkingSummaryEventStart),
                Mathf.Max(0.1f, postEventSpeedWindowSec)
            );
        }

        walkingSummaryActive = false;
        walkingSummaryEvaluationSnapshot = default;
    }

    private void RefreshWalkingSummaryEvaluationSnapshot()
    {
        if (!walkingSummaryActive && !evaluationInProgress)
            return;

        walkingSummaryEvaluationSnapshot =
            new EventLogger.EvaluationSnapshot(maskingEventManager);
    }

    private SpeedAccumulator CalculateRecentSpeedWindow(float startTime, float endTime)
    {
        SpeedAccumulator result = default;

        for (int i = 0; i < recentSpeedSamples.Count; i++)
        {
            float segmentStart = Mathf.Max(startTime, recentSpeedSamples[i].time);
            float segmentEnd = i + 1 < recentSpeedSamples.Count
                ? Mathf.Min(endTime, recentSpeedSamples[i + 1].time)
                : endTime;
            result.Add(recentSpeedSamples[i].speed, Mathf.Max(0f, segmentEnd - segmentStart));
        }

        return result;
    }

    private static void AddOverlappingSpeed(
        ref SpeedAccumulator accumulator,
        float speed,
        float frameStart,
        float frameEnd,
        float windowStart,
        float windowEnd
    )
    {
        float overlapStart = Mathf.Max(frameStart, windowStart);
        float overlapEnd = Mathf.Min(frameEnd, windowEnd);
        accumulator.Add(speed, Mathf.Max(0f, overlapEnd - overlapStart));
    }

    private void HandleInjectionCompleted(float completionTime)
    {
        RefreshWalkingSummaryEvaluationSnapshot();
        TryFinishEvaluationAfterResponse();
    }

    private void HandleEvaluationInvalidated(string reason)
    {
        if (!evaluationInProgress)
            return;

        ResetResponseState();

        if (participantFeedback != null)
            participantFeedback.ResetState(false);

        NotifyCurrentEvaluationFinished();
    }

    private void AcceptResponse(float acceptedResponseTime)
    {
        if (!evaluationInProgress || !responseWindowOpen || responseAccepted)
            return;

        if (
            injectionController == null ||
            !injectionController.TryStopAfterAcceptedResponse(
                out float appliedThetaAtResponseDeg
            )
        )
        {
            // Invalidation raises a synchronous notification and may already
            // have completed the invalid-evaluation retry path.
            if (!evaluationInProgress)
                return;

            // An invalidating state that began before the response retains
            // priority. The existing invalid-evaluation path retries the same
            // planned trial without updating staircase or catch state.
            ResetResponseState();
            if (participantFeedback != null)
                participantFeedback.ResetState(false);
            NotifyCurrentEvaluationFinished();
            return;
        }

        responseAccepted = true;
        responseWindowOpen = false;
        responseTime = acceptedResponseTime;
        noticedDuringCurrentEvaluation = true;

        if (participantFeedback != null)
            participantFeedback.DisableListening();

        FormalExperimentContext.RecordResponse(responseTime, true);
        FormalExperimentContext.RecordAppliedThetaAtResponse(
            appliedThetaAtResponseDeg
        );

        if (eventLogger != null && maskingEventManager != null)
        {
            eventLogger.LogParticipantResponse(
                maskingEventManager,
                true,
                responseTime,
                FormalExperimentContext.ResponseRtSec
            );
        }

        TryFinishEvaluationAfterResponse();
    }

    private void TryFinishEvaluationAfterResponse()
    {
        if (!evaluationInProgress)
            return;

        bool responseResolved = responseAccepted || !responseWindowOpen;
        bool injectionCompleted =
            injectionController != null &&
            injectionController.CurrentEvaluationInjectionCompleted;
        bool injectionConcludedByAcceptedResponse =
            responseAccepted &&
            injectionController != null &&
            injectionController.CurrentEvaluationConcludedByAcceptedResponse;

        if (
            responseResolved &&
            (injectionCompleted || injectionConcludedByAcceptedResponse)
        )
            NotifyCurrentEvaluationFinished();
    }

    private void ResetResponseState()
    {
        responseWindowOpen = false;
        responseAccepted = false;
        responseTime = float.NaN;
        responseDeadline = float.NaN;
    }

    private void ProcessValidStaircaseEvaluation(float testThetaBeforeFinish, bool noticedBeforeFinish)
    {
        validTrialCount++;
        normalTrialsSinceLastCatch++;

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

        bool clampedAtLowerBoundary =
            Mathf.Approximately(testThetaBeforeFinish, minThetaDeg) &&
            staircaseDeltaDeg < 0f &&
            Mathf.Approximately(nextThetaDeg, testThetaBeforeFinish);
        bool clampedAtUpperBoundary =
            Mathf.Approximately(testThetaBeforeFinish, maxThetaDeg) &&
            staircaseDeltaDeg > 0f &&
            Mathf.Approximately(nextThetaDeg, testThetaBeforeFinish);

        if (clampedAtLowerBoundary || clampedAtUpperBoundary)
        {
            consecutiveBoundaryClampedTrials++;
            if (consecutiveBoundaryClampedTrials >= Mathf.Max(1, boundaryLimitedAfterConsecutiveClampedTrials))
                boundaryLimitedEstimate = true;
        }
        else
        {
            consecutiveBoundaryClampedTrials = 0;
        }

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
                reversalThetas.Count,
                "",
                "trialType=NORMAL"
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
                    reversalThetas.Count,
                    "",
                    "trialType=NORMAL"
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
                $"thresholdReliable={thresholdReliable.ToString().ToLowerInvariant()};" +
                $"hasEstimatedThreshold={hasEstimatedThreshold.ToString().ToLowerInvariant()};" +
                $"boundaryLimitedEstimate={boundaryLimitedEstimate.ToString().ToLowerInvariant()};" +
                $"thresholdEstimateStatus={GetThresholdEstimateStatus()};" +
                BuildCatchSummaryExtra()
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

        if (reversalThetas.Count < MinimumUsableReversals)
            return false;

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

    public string GetThresholdEstimateStatus()
    {
        if (!hasEstimatedThreshold)
            return "INSUFFICIENT_STAIRCASE";

        if (boundaryLimitedEstimate)
            return "BOUNDARY_LIMITED_ESTIMATE";

        return thresholdReliable
            ? "COMPLETE_STAIRCASE"
            : "INCOMPLETE_STAIRCASE";
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

    private void ResetCatchState()
    {
        currentEvaluationTrialType = EvaluationTrialType.Normal;
        currentPlannedThetaDeg = currentTestThetaDeg;

        normalTrialsSinceLastCatch = 0;
        catchSequenceIndex = 0;

        zeroCatchCount = 0;
        zeroCatchFalseAlarmCount = 0;
        highCatchCount = 0;
        highCatchHitCount = 0;

        ScheduleNextCatchInterval();
    }

    private void PlanNextEvaluation()
    {
        currentEvaluationTrialType = EvaluationTrialType.Normal;
        currentPlannedThetaDeg = currentTestThetaDeg;

        if (!enableCatchEvents)
            return;

        if (normalTrialsSinceLastCatch < nextCatchAfterNormalTrials)
            return;

        currentEvaluationTrialType = GetNextCatchType();
        currentPlannedThetaDeg = currentEvaluationTrialType == EvaluationTrialType.CatchZero
            ? 0f
            : GetClampedHighCatchThetaDeg();

        if (debugLog)
        {
            Debug.Log(
                $"[GainSearchFlowController] Catch planned. " +
                $"Type={currentEvaluationTrialType}, CatchTheta={currentPlannedThetaDeg:F2}, " +
                $"HeldStaircaseTheta={currentTestThetaDeg:F2}, " +
                $"ValidNormalSinceLastCatch={normalTrialsSinceLastCatch}, " +
                $"ScheduledAfter={nextCatchAfterNormalTrials}",
                this
            );
        }
    }

    private void ProcessValidCatchEvaluation(
        EvaluationTrialType trialType,
        float catchThetaDeg,
        float heldStaircaseThetaDeg,
        bool noticed
    )
    {
        string catchOutcome;

        if (trialType == EvaluationTrialType.CatchZero)
        {
            zeroCatchCount++;
            if (noticed)
            {
                zeroCatchFalseAlarmCount++;
                catchOutcome = "FALSE_ALARM";
            }
            else
            {
                catchOutcome = "CORRECT_REJECTION";
            }
        }
        else
        {
            highCatchCount++;
            if (noticed)
            {
                highCatchHitCount++;
                catchOutcome = "HIT";
            }
            else
            {
                catchOutcome = "MISS";
            }
        }

        if (eventLogger != null && maskingEventManager != null)
        {
            eventLogger.LogCatchEvaluation(
                "CATCH_EVAL_FINISH",
                maskingEventManager,
                GetTrialTypeLogName(trialType),
                catchThetaDeg,
                noticed,
                true,
                catchOutcome,
                heldStaircaseThetaDeg,
                "",
                BuildCatchRuntimeExtra()
            );
        }

        if (debugLog)
        {
            Debug.Log(
                $"[GainSearchFlowController] Catch completed. " +
                $"Type={trialType}, Theta={catchThetaDeg:F2}, Noticed={noticed}, " +
                $"Outcome={catchOutcome}. Staircase held at {heldStaircaseThetaDeg:F2}deg.",
                this
            );
        }

        // A valid catch consumes the catch slot but does NOT count as a valid
        // staircase trial and does NOT change reversal state or staircase theta.
        normalTrialsSinceLastCatch = 0;
        catchSequenceIndex = (catchSequenceIndex + 1) % 2;
        ScheduleNextCatchInterval();
    }

    private EvaluationTrialType GetNextCatchType()
    {
        return catchSequenceIndex % 2 == 0
            ? EvaluationTrialType.CatchZero
            : EvaluationTrialType.CatchHigh;
    }

    private void ScheduleNextCatchInterval()
    {
        int minInterval = Mathf.Max(1, minNormalTrialsBetweenCatch);
        int maxInterval = Mathf.Max(minInterval, maxNormalTrialsBetweenCatch);
        nextCatchAfterNormalTrials = Random.Range(minInterval, maxInterval + 1);
    }

    private float GetClampedHighCatchThetaDeg()
    {
        float upper = Mathf.Max(0f, maxThetaDeg);
        if (injectionController != null)
            upper = Mathf.Min(upper, Mathf.Max(0f, injectionController.thetaHardMax));

        return Mathf.Clamp(highCatchThetaDeg, 0f, upper);
    }

    private string GetTrialTypeLogName(EvaluationTrialType trialType)
    {
        switch (trialType)
        {
            case EvaluationTrialType.CatchZero:
                return "CATCH_ZERO";
            case EvaluationTrialType.CatchHigh:
                return "CATCH_HIGH";
            case EvaluationTrialType.Normal:
            default:
                return "NORMAL";
        }
    }

    private string BuildCatchRuntimeExtra()
    {
        return
            $"normalTrialsSinceLastCatch={normalTrialsSinceLastCatch};" +
            $"nextCatchAfterNormalTrials={nextCatchAfterNormalTrials};" +
            $"zeroCatchCount={zeroCatchCount};" +
            $"zeroCatchFalseAlarmCount={zeroCatchFalseAlarmCount};" +
            $"highCatchCount={highCatchCount};" +
            $"highCatchHitCount={highCatchHitCount}";
    }

    private string BuildCatchSummaryExtra()
    {
        float falseAlarmRate = zeroCatchCount > 0
            ? (float)zeroCatchFalseAlarmCount / zeroCatchCount
            : 0f;

        float highHitRate = highCatchCount > 0
            ? (float)highCatchHitCount / highCatchCount
            : 0f;

        return
            $"catchEventsEnabled={enableCatchEvents.ToString().ToLowerInvariant()};" +
            $"zeroCatchCount={zeroCatchCount};" +
            $"zeroCatchFalseAlarmCount={zeroCatchFalseAlarmCount};" +
            $"zeroCatchFalseAlarmRate={FormatFloat(falseAlarmRate)};" +
            $"highCatchCount={highCatchCount};" +
            $"highCatchHitCount={highCatchHitCount};" +
            $"highCatchHitRate={FormatFloat(highHitRate)}";
    }

    private void ApplyCurrentPlannedThetaToInjection()
    {
        if (injectionController == null)
            return;

        injectionController.SetCurrentEventThetaDeg(currentPlannedThetaDeg);

        if (debugLog)
        {
            Debug.Log(
                $"[GainSearchFlowController] Applied planned theta to injection controller: " +
                $"TrialType={currentEvaluationTrialType}, PlannedTheta={currentPlannedThetaDeg:F2}, " +
                $"HeldStaircaseTheta={currentTestThetaDeg:F2}",
                this
            );
        }
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }
}
