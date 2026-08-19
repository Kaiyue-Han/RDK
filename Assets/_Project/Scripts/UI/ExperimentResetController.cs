using UnityEngine;
using UnityEngine.UI;

public class ExperimentResetController : MonoBehaviour
{
    public enum ResetSource
    {
        UI,
        Keyboard,
        Script
    }

    [Header("Scene Transform References")]
    [Tooltip("Usually the XR Origin / VR Player root. This object is moved so the HMD returns to the recorded start position/yaw.")]
    [SerializeField] private Transform playerRigRoot;

    [Tooltip("Usually XR Origin / Camera Offset / Main Camera. Used to align the view back to the recorded start pose.")]
    [SerializeField] private Transform hmd;

    [Tooltip("The root that RDW rotates, usually WorldRoot. Resetting this removes accumulated redirected-walking world rotation.")]
    [SerializeField] private Transform worldRoot;

    [Tooltip("Optional WorldRotator. Its internal smoothed yaw state is cleared during reset.")]
    [SerializeField] private WorldRotator worldRotator;

    [Header("Experiment References")]
    [SerializeField] private ExperimentTrialController trialController;
    [SerializeField] private GainSearchFlowController searchFlowController;
    [SerializeField] private MaskingEventManager maskingEventManager;
    [SerializeField] private CoinSequenceManager coinSequenceManager;
    [SerializeField] private ExperimentUIController experimentUIController;
    [SerializeField] private ExperimentUITabController tabController;
    [SerializeField] private TrialFinishUIController resultUIController;
    [SerializeField] private EventLogger eventLogger;

    [Header("Physical / Transient State")]
    [SerializeField] private WalkingDetector walkingDetector;
    [SerializeField] private PhysicalPositionTracker physicalPositionTracker;
    [SerializeField] private PlayAreaRectProvider playAreaRectProvider;
    [SerializeField] private RotationInjectionController injectionController;
    [SerializeField] private ParticipantFeedbackController participantFeedback;

    [Header("Optional UI")]
    [SerializeField] private Button resetButton;
    [Tooltip("After reset, switch the experimenter UI back to the Settings tab.")]
    [SerializeField] private bool showSettingsTabAfterReset = true;

    [Header("Reset Behaviour")]
    [Tooltip("Clear the configured trial so the next run requires pressing Apply again, like entering the scene fresh.")]
    [SerializeField] private bool clearTrialConfiguration = true;

    [Tooltip("Also restore the player rig transform saved at Start before aligning the HMD. Keep this on unless you have a custom XR origin workflow.")]
    [SerializeField] private bool restorePlayerRigTransform = true;

    [Tooltip("Move/rotate the player rig so the HMD returns to the initial world XZ position and yaw.")]
    [SerializeField] private bool alignHmdToInitialPose = true;

    [Tooltip("Do not force the HMD height to the initial height. This avoids odd vertical jumps for different users.")]
    [SerializeField] private bool keepCurrentHmdHeight = true;

    [Tooltip("Write one reset row to events.csv.")]
    [SerializeField] private bool logFullReset = true;

    [Header("Debug Keyboard")]
    [SerializeField] private bool enableKeyboardReset = true;
    [SerializeField] private KeyCode resetKey = KeyCode.R;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    private TransformSnapshot initialPlayerRig;
    private TransformSnapshot initialWorldRoot;
    private Vector3 initialHmdWorldPosition;
    private float initialHmdWorldYaw;
    private bool initialPoseCaptured = false;

    private struct TransformSnapshot
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 localScale;

        public TransformSnapshot(Transform transform)
        {
            position = transform != null ? transform.position : Vector3.zero;
            rotation = transform != null ? transform.rotation : Quaternion.identity;
            localScale = transform != null ? transform.localScale : Vector3.one;
        }

        public void Restore(Transform transform)
        {
            if (transform == null)
                return;

            transform.position = position;
            transform.rotation = rotation;
            transform.localScale = localScale;
        }
    }

    private void Awake()
    {
        if (resetButton != null)
            resetButton.onClick.AddListener(ResetExperimentFromUI);
    }

    private void Start()
    {
        CaptureInitialPose();
    }

    private void Update()
    {
        if (enableKeyboardReset && Input.GetKeyDown(resetKey))
            ResetExperiment("KEYBOARD_RESET", ResetSource.Keyboard);
    }

    [ContextMenu("Capture Initial Pose Now")]
    public void CaptureInitialPose()
    {
        initialPlayerRig = new TransformSnapshot(playerRigRoot);
        initialWorldRoot = new TransformSnapshot(worldRoot);

        if (hmd != null)
        {
            initialHmdWorldPosition = hmd.position;
            initialHmdWorldYaw = YawOf(hmd.rotation);
        }
        else
        {
            initialHmdWorldPosition = Vector3.zero;
            initialHmdWorldYaw = 0f;
        }

        initialPoseCaptured = true;

        if (debugLog)
        {
            Debug.Log(
                $"[ExperimentResetController] Initial pose captured. " +
                $"HMDPos={initialHmdWorldPosition}, HMDYaw={initialHmdWorldYaw:F1}",
                this
            );
        }
    }

    public void ResetExperimentFromUI()
    {
        ResetExperiment("UI_RESET", ResetSource.UI);
    }

    public void ResetExperiment(string reason)
    {
        ResetExperiment(reason, ResetSource.Script);
    }

    public void ResetExperiment(string reason, ResetSource source)
    {
        if (!initialPoseCaptured)
            CaptureInitialPose();

        string state = GetCurrentResetState();
        string resetReason = string.IsNullOrEmpty(reason) ? source.ToString() : reason;
        string combinedReason = $"{resetReason}_{state}";

        if (debugLog)
        {
            Debug.Log($"[ExperimentResetController] Full reset started. Reason={combinedReason}", this);
        }

        if (logFullReset && eventLogger != null && maskingEventManager != null)
        {
            eventLogger.LogResetEvent(
                "FULL_RESET",
                maskingEventManager,
                combinedReason,
                $"source={source};stateBeforeReset={state}"
            );
        }

        // Stop experiment logic first so no late evaluation/occlusion callback updates state after reset.
        if (trialController != null)
            trialController.ResetTrialState(combinedReason, false);

        if (searchFlowController != null)
            searchFlowController.ResetSearch(combinedReason, false);

        if (maskingEventManager != null)
        {
            if (clearTrialConfiguration)
                maskingEventManager.ClearTrial(combinedReason, false);
            else
                maskingEventManager.AbortAndResetToIdle(combinedReason, false);
        }

        if (coinSequenceManager != null)
            coinSequenceManager.ResetSequence();

        RestoreSceneTransforms();

        // The participant physically returns to the designated origin before a
        // full reset. Rebuild all physical baselines from that pose so the new run
        // cannot inherit displacement, speed smoothing, or turn history.
        if (physicalPositionTracker != null)
            physicalPositionTracker.ResetTracker();

        if (playAreaRectProvider != null)
            playAreaRectProvider.ResetOrigin();

        if (walkingDetector != null)
            walkingDetector.ResetState();

        if (injectionController != null)
            injectionController.ResetDirectionCache();

        if (participantFeedback != null)
            participantFeedback.ResetState(false);

        if (worldRotator != null)
            worldRotator.ResetRuntimeState();

        if (experimentUIController != null)
            experimentUIController.SetUIInteractable(true);

        if (resultUIController != null)
            resultUIController.ResetUIState();

        if (showSettingsTabAfterReset && tabController != null)
            tabController.ShowSettings();

        if (debugLog)
        {
            Debug.Log($"[ExperimentResetController] Full reset finished. Reason={combinedReason}", this);
        }
    }

    private string GetCurrentResetState()
    {
        if (trialController != null)
        {
            if (trialController.TrialRunning || trialController.TrialPaused)
                return "RUNNING";

            if (trialController.TrialFinished)
                return "FINISHED";
        }

        if (searchFlowController != null)
        {
            if (searchFlowController.Phase == GainSearchFlowController.SearchPhase.Staircase)
                return "RUNNING";

            if (searchFlowController.Phase == GainSearchFlowController.SearchPhase.Finished)
                return "FINISHED";
        }

        return "IDLE";
    }

    private void RestoreSceneTransforms()
    {
        // Restore the RDW-rotated virtual world first.
        initialWorldRoot.Restore(worldRoot);

        if (restorePlayerRigTransform)
            initialPlayerRig.Restore(playerRigRoot);

        if (alignHmdToInitialPose)
            AlignHmdToInitialPose();
    }

    private void AlignHmdToInitialPose()
    {
        if (playerRigRoot == null || hmd == null)
            return;

        float currentYaw = YawOf(hmd.rotation);
        float yawDelta = Mathf.DeltaAngle(currentYaw, initialHmdWorldYaw);

        Vector3 pivot = hmd.position;
        playerRigRoot.RotateAround(pivot, Vector3.up, yawDelta);

        Vector3 currentHmdPosition = hmd.position;
        Vector3 delta = initialHmdWorldPosition - currentHmdPosition;

        if (keepCurrentHmdHeight)
            delta.y = 0f;

        playerRigRoot.position += delta;
    }

    private static float YawOf(Quaternion rotation)
    {
        return rotation.eulerAngles.y;
    }
}
