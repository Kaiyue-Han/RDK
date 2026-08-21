using UnityEngine;

public sealed class TrainingDemoController : MonoBehaviour
{
    public enum TrainingExample
    {
        None,
        Example1,
        Example2
    }

    private const int TrainingOcclusionRatio = 70;
    private const float Example1ThetaDeg = 0f;
    private const float Example2ThetaDeg = 20f;

    [Header("Shared event components")]
    [SerializeField] private MaskingEventManager maskingEventManager;
    [SerializeField] private RotationInjectionController injectionController;
    [SerializeField] private TrainingEventTrigger eventTrigger;

    [Header("Reset")]
    [Tooltip("Optional. Reuse ExperimentResetController for XR rig, WorldRoot, and coin reset.")]
    [SerializeField] private ExperimentResetController resetController;

    [Header("UI")]
    [Tooltip("Training UI panel hidden only after the selected example is armed successfully.")]
    [SerializeField] private GameObject experimentPanelRoot;

    [Header("Fixed Example 2 direction")]
    [Tooltip("Positive uses Unity positive world yaw. Turn this off to use -20 degrees.")]
    [SerializeField] private bool example2UsesPositiveYaw = true;

    [Header("Runtime state")]
    [SerializeField] private TrainingExample selectedExample = TrainingExample.None;
    [SerializeField] private bool started;
    [SerializeField] private bool eventTriggered;

    public TrainingExample SelectedExample => selectedExample;
    public bool IsStarted => started;
    public bool HasTriggered => eventTriggered;

    public void SelectExample1()
    {
        SelectExample(TrainingExample.Example1);
    }

    public void SelectExample2()
    {
        SelectExample(TrainingExample.Example2);
    }

    public void SetExample1Selected(bool isOn)
    {
        if (isOn)
            SelectExample1();
    }

    public void SetExample2Selected(bool isOn)
    {
        if (isOn)
            SelectExample2();
    }

    public void StartSelectedExample()
    {
        if (!ValidateRequiredReferences())
            return;

        if (selectedExample == TrainingExample.None)
        {
            Debug.LogWarning("[Training] Select Example 1 or Example 2 before Start.", this);
            return;
        }

        if (started)
        {
            Debug.LogWarning("[Training] This demonstration is already started. Reset first.", this);
            return;
        }

        maskingEventManager.ConfigureTrainingDemo(
            MaskingEventManager.TrialOccluderType.StaticButterfly,
            TrainingOcclusionRatio
        );

        float signedTheta = selectedExample == TrainingExample.Example1
            ? Example1ThetaDeg
            : (example2UsesPositiveYaw ? Example2ThetaDeg : -Example2ThetaDeg);
        injectionController.ArmTrainingInjection(signedTheta);

        if (!eventTrigger.Arm())
        {
            injectionController.ClearTrainingInjection("TRAINING_ARM_FAILED");
            maskingEventManager.ClearTrial("TRAINING_ARM_FAILED", false);
            return;
        }

        started = true;
        eventTriggered = false;

        if (experimentPanelRoot != null)
            experimentPanelRoot.SetActive(false);

        Debug.Log($"[Training] {selectedExample} armed.", this);
    }

    public bool PlaySelectedExample()
    {
        if (!started || eventTriggered)
            return false;

        bool startedOcclusion = maskingEventManager.TriggerTrainingOcclusion();
        if (!startedOcclusion)
            return false;

        eventTriggered = true;
        return true;
    }

    public void ResetTraining()
    {
        eventTrigger?.ResetTrigger();
        injectionController?.ClearTrainingInjection();
        maskingEventManager?.ClearTrial("TRAINING_RESET", false);

        if (resetController != null)
            resetController.ResetExperiment("TRAINING_RESET");

        started = false;
        eventTriggered = false;
        Debug.Log("[Training] Reset complete.", this);
    }

    private void SelectExample(TrainingExample example)
    {
        if (started)
        {
            Debug.LogWarning("[Training] Reset before changing the selected example.", this);
            return;
        }

        selectedExample = example;
    }

    private bool ValidateRequiredReferences()
    {
        if (
            maskingEventManager != null &&
            injectionController != null &&
            eventTrigger != null
        )
        {
            return true;
        }

        Debug.LogError(
            "[Training] MaskingEventManager, RotationInjectionController, and " +
            "TrainingEventTrigger must be assigned.",
            this
        );
        return false;
    }
}
