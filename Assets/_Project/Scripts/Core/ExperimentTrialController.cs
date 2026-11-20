using UnityEngine;

public class ExperimentTrialController : MonoBehaviour
{
    [Header("Core References")]
    [SerializeField] private MaskingEventManager maskingEventManager;
    [SerializeField] private GainSearchFlowController searchFlowController;
    [SerializeField] private EventLogger eventLogger;
    [SerializeField] private WorldRotator worldRotator;

    [Header("Optional UI Lock")]
    [SerializeField] private ExperimentUIController experimentUIController;

    [Header("XR Inputs")]
    [Tooltip("Use left X for pause/resume.")]
    [SerializeField] private MonoBehaviour pauseResumeInputBehaviour;

    [Tooltip("Use left Y for abort (hold). Must be XRButtonInput.")]
    [SerializeField] private MonoBehaviour abortInputBehaviour;

    [Header("Abort Hold")]
    [SerializeField] private float abortHoldSec = 3f;

    [Header("Formal Run")]
    [Tooltip("Formal runs disable Pause/Resume. Use Abort when a participant cannot continue.")]
    [SerializeField] private bool formalRunMode = true;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    [Header("Debug Keyboard")]
    [SerializeField] private bool enableDebugKeyboard = true;
    [SerializeField] private KeyCode startTrialKey = KeyCode.F8;
    [SerializeField] private KeyCode resetTrialKey = KeyCode.F9;
    [SerializeField] private KeyCode abortTrialKey = KeyCode.F10;
    [SerializeField] private KeyCode pauseTrialKey = KeyCode.F11;
    [SerializeField] private KeyCode resumeTrialKey = KeyCode.F12;

    [Header("Runtime State")]
    [SerializeField] private bool trialRunning = false;
    [SerializeField] private bool trialFinished = false;
    [SerializeField] private bool trialPaused = false;
    [SerializeField] private bool resultLogged = false;

    [SerializeField] private string currentConditionName = "Unknown";
    [SerializeField] private float finalConfirmedThetaDeg = 0f;
    [SerializeField] private bool finalSuccess = false;

    [SerializeField] private bool abortHoldActive = false;
    [SerializeField] private float abortHoldStartTime = -1f;

    private IButtonInput pauseResumeInput;
    private XRButtonInput abortInput;

    public bool TrialRunning => trialRunning;
    public bool TrialFinished => trialFinished;
    public bool TrialPaused => trialPaused;
    public string CurrentConditionName => currentConditionName;
    public float FinalEstimatedThresholdDeg => finalConfirmedThetaDeg;
    public float FinalConfirmedThetaDeg => finalConfirmedThetaDeg; // Backward-compatible UI alias.
    public bool FinalSuccess => finalSuccess;

    private void Awake()
    {
        pauseResumeInput = pauseResumeInputBehaviour as IButtonInput;
        abortInput = abortInputBehaviour as XRButtonInput;

        if (pauseResumeInputBehaviour != null && pauseResumeInput == null)
        {
            Debug.LogError("[ExperimentTrialController] pauseResumeInputBehaviour must implement IButtonInput.", this);
        }

        if (abortInputBehaviour != null && abortInput == null)
        {
            Debug.LogError("[ExperimentTrialController] abortInputBehaviour must be XRButtonInput.", this);
        }
    }

    private void Update()
    {
        HandleXRInputs();

        if (enableDebugKeyboard)
        {
            if (Input.GetKeyDown(startTrialKey))
                StartCurrentConfiguredTrial();

            if (Input.GetKeyDown(resetTrialKey))
                ResetTrialState();

            if (Input.GetKeyDown(abortTrialKey))
                AbortCurrentTrial();

            if (Input.GetKeyDown(pauseTrialKey))
                PauseCurrentTrial();

            if (Input.GetKeyDown(resumeTrialKey))
                ResumeCurrentTrial();
        }

        if (!trialRunning || trialFinished || trialPaused)
            return;

        if (searchFlowController == null)
            return;

        if (searchFlowController.Phase != GainSearchFlowController.SearchPhase.Finished)
            return;

        FinishCurrentTrial();
    }

    private void HandleXRInputs()
    {
        // X button: toggle pause/resume
        if (
            !formalRunMode &&
            pauseResumeInput != null &&
            pauseResumeInput.PressedThisFrame()
        )
        {
            if (trialPaused)
                ResumeCurrentTrial();
            else
                PauseCurrentTrial();
        }

        // Y button: hold to abort
        if (abortInput != null)
        {
            bool pressedNow = abortInput.IsPressedNow();

            if (pressedNow)
            {
                if (!abortHoldActive)
                {
                    abortHoldActive = true;
                    abortHoldStartTime = Time.time;

                    if (debugLog)
                    {
                        Debug.Log("[ExperimentTrialController] Abort hold started.", this);
                    }
                }
                else
                {
                    if ((Time.time - abortHoldStartTime) >= abortHoldSec)
                    {
                        abortHoldActive = false;
                        abortHoldStartTime = -1f;
                        AbortCurrentTrial();
                    }
                }
            }
            else
            {
                if (abortHoldActive)
                {
                    abortHoldActive = false;
                    abortHoldStartTime = -1f;

                    if (debugLog)
                    {
                        Debug.Log("[ExperimentTrialController] Abort hold cancelled.", this);
                    }
                }
            }
        }
    }

    public void StartCurrentConfiguredTrial()
    {
        if (maskingEventManager == null || searchFlowController == null)
        {
            Debug.LogError("[ExperimentTrialController] Missing core references.");
            return;
        }

        if (!maskingEventManager.IsTrialConfigured)
        {
            Debug.LogWarning("[ExperimentTrialController] Cannot start trial: current condition is not configured.");
            return;
        }

        if (trialRunning || trialPaused)
        {
            Debug.LogWarning("[ExperimentTrialController] A formal run is already active.", this);
            return;
        }

        if (!FormalExperimentContext.BeginRun())
        {
            Debug.LogError(
                "[ExperimentTrialController] Cannot start formal run: participant/session IDs are not configured in the launcher.",
                this
            );
            return;
        }

        trialRunning = true;
        trialFinished = false;
        trialPaused = false;
        resultLogged = false;

        abortHoldActive = false;
        abortHoldStartTime = -1f;

        finalConfirmedThetaDeg = 0f;
        finalSuccess = false;

        currentConditionName = maskingEventManager.CurrentConditionKey;

        if (debugLog)
        {
            Debug.Log(
                $"[ExperimentTrialController] Trial started. Condition={currentConditionName}",
                this
            );
        }

        if (eventLogger != null)
        {
            eventLogger.Mark(
                "TRIAL_START",
                maskingEventManager,
                -1f,
                searchFlowController.BuildStaircaseStartExtra()
            );
        }

        searchFlowController.StartSearch();

        if (experimentUIController != null)
        {
            experimentUIController.SetUIInteractable(false);
        }
    }

    public void AbortCurrentTrial()
    {
        if (!trialRunning && !trialPaused)
        {
            if (debugLog)
            {
                Debug.Log("[ExperimentTrialController] Abort ignored: no active/paused trial.", this);
            }
            return;
        }

        if (debugLog)
        {
            Debug.Log(
                $"[ExperimentTrialController] Trial aborted. Condition={currentConditionName}",
                this
            );
        }

        if (eventLogger != null && maskingEventManager != null)
        {
            eventLogger.Mark(
                "TRIAL_ABORT",
                maskingEventManager,
                -1f,
                "abortReason=AbortCurrentTrial"
            );
        }

        if (worldRotator != null)
            worldRotator.AbortCurrentInjection("ABORTED");

        if (searchFlowController != null)
            searchFlowController.StopSearch("ABORTED");

        if (maskingEventManager != null)
            maskingEventManager.AbortAndResetToIdle();

        trialRunning = false;
        trialFinished = true;
        trialPaused = false;
        resultLogged = false;

        abortHoldActive = false;
        abortHoldStartTime = -1f;

        finalConfirmedThetaDeg = 0f;
        finalSuccess = false;

        if (experimentUIController != null)
        {
            experimentUIController.SetUIInteractable(true);
        }

        FormalExperimentContext.EndRun();
    }

    public void PauseCurrentTrial()
    {
        if (formalRunMode)
        {
            if (debugLog)
                Debug.Log("[ExperimentTrialController] Pause is disabled in formal mode.", this);
            return;
        }

        if (!trialRunning || trialFinished)
        {
            if (debugLog)
            {
                Debug.Log("[ExperimentTrialController] Pause ignored: trial is not running.", this);
            }
            return;
        }

        if (trialPaused)
        {
            if (debugLog)
            {
                Debug.Log("[ExperimentTrialController] Pause ignored: trial already paused.", this);
            }
            return;
        }

        trialPaused = true;

        if (debugLog)
        {
            Debug.Log(
                $"[ExperimentTrialController] Trial paused. Condition={currentConditionName}",
                this
            );
        }

        if (experimentUIController != null)
        {
            experimentUIController.SetUIInteractable(true);
        }
    }

    public void ResumeCurrentTrial()
    {
        if (formalRunMode)
        {
            if (debugLog)
                Debug.Log("[ExperimentTrialController] Resume is disabled in formal mode.", this);
            return;
        }

        if (!trialPaused)
        {
            if (debugLog)
            {
                Debug.Log("[ExperimentTrialController] Resume ignored: trial is not paused.", this);
            }
            return;
        }

        trialPaused = false;
        trialRunning = true;

        if (debugLog)
        {
            Debug.Log(
                $"[ExperimentTrialController] Trial resumed. Condition={currentConditionName}",
                this
            );
        }

        if (experimentUIController != null)
        {
            experimentUIController.SetUIInteractable(false);
        }
    }

    public void ResetTrialState()
    {
        ResetTrialState("ManualReset", true);
    }

    public void ResetTrialState(string resetReason, bool logReset)
    {
        bool wasActive = trialRunning || trialPaused || trialFinished;

        if (logReset && eventLogger != null && maskingEventManager != null)
        {
            eventLogger.LogResetEvent(
                "TRIAL_RESET",
                maskingEventManager,
                string.IsNullOrEmpty(resetReason) ? "ManualReset" : resetReason,
                $"wasActive={wasActive.ToString().ToLowerInvariant()};wasRunning={trialRunning.ToString().ToLowerInvariant()};wasFinished={trialFinished.ToString().ToLowerInvariant()};wasPaused={trialPaused.ToString().ToLowerInvariant()}"
            );
        }

        trialRunning = false;
        trialFinished = false;
        trialPaused = false;
        resultLogged = false;

        abortHoldActive = false;
        abortHoldStartTime = -1f;

        currentConditionName = "Unknown";
        finalConfirmedThetaDeg = 0f;
        finalSuccess = false;

        if (experimentUIController != null)
        {
            experimentUIController.SetUIInteractable(true);
        }

        if (debugLog)
        {
            Debug.Log($"[ExperimentTrialController] Trial state reset. Reason={resetReason}", this);
        }

        FormalExperimentContext.EndRun();
    }

    private void FinishCurrentTrial()
    {
        if (searchFlowController == null || maskingEventManager == null)
            return;

        trialRunning = false;
        trialFinished = true;
        trialPaused = false;

        abortHoldActive = false;
        abortHoldStartTime = -1f;

        finalSuccess = searchFlowController.HasEstimatedThreshold && searchFlowController.ThresholdReliable;
        finalConfirmedThetaDeg = searchFlowController.EstimatedThresholdDeg;

        if (debugLog)
        {
            Debug.Log(
                $"[ExperimentTrialController] Trial finished. " +
                $"Condition={currentConditionName}, Success={finalSuccess}, EstimatedThreshold={finalConfirmedThetaDeg:F2}, " +
                $"StopReason={searchFlowController.StopReason}",
                this
            );
        }

        if (!resultLogged && eventLogger != null)
        {
            eventLogger.Mark(
                "TRIAL_FINISH",
                maskingEventManager,
                -1f,
                $"stopReason={searchFlowController.StopReason};" +
                $"thresholdReliable={searchFlowController.ThresholdReliable.ToString().ToLowerInvariant()};" +
                $"thresholdEstimateStatus={searchFlowController.GetThresholdEstimateStatus()}"
            );

            resultLogged = true;
        }

        if (experimentUIController != null)
        {
            experimentUIController.SetUIInteractable(true);
        }

        FormalExperimentContext.EndRun();
    }

}
