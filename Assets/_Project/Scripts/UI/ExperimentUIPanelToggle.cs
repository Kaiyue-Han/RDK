using UnityEngine;

public class ExperimentUIPanelToggle : MonoBehaviour
{
    [Header("UI Root")]
    [SerializeField] private GameObject panelRoot;

    [Tooltip("Input provider that implements IButtonInput.")]
    [SerializeField] private MonoBehaviour inputBehaviour;

    [Header("Participant concealment")]
    [Tooltip("Keep the experimenter panel hidden while a formal condition is running or paused.")]
    [SerializeField] private bool hideDuringActiveTrial = true;

    [Tooltip("Optional. Automatically found in the scene when left empty.")]
    [SerializeField] private ExperimentTrialController trialController;

    private IButtonInput input;

    void Awake()
    {
        if (trialController == null)
            trialController = FindFirstObjectByType<ExperimentTrialController>();

        if (inputBehaviour != null)
        {
            input = inputBehaviour as IButtonInput;
            if (input == null)
            {
                Debug.LogWarning("[ExperimentUI] inputBehaviour does not implement IButtonInput.");
            }
        }
        else
        {
            Debug.LogWarning("[ExperimentUI] inputBehaviour is not assigned.");
        }

        if (panelRoot == null)
        {
            Debug.LogWarning("[ExperimentUI] panelRoot is not assigned.");
        }
    }

    void Start()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(true); // 启动默认显示
        }
    }

    void Update()
    {
        if (panelRoot == null)
            return;

        if (ShouldConcealExperimenterPanel())
        {
            if (panelRoot.activeSelf)
                panelRoot.SetActive(false);
            return;
        }

        if (input == null)
            return;

        if (input.PressedThisFrame())
        {
            TogglePanel();
        }
    }

    private bool ShouldConcealExperimenterPanel()
    {
        return hideDuringActiveTrial &&
               trialController != null &&
               (trialController.TrialRunning || trialController.TrialPaused) &&
               !trialController.TrialFinished;
    }

    private void TogglePanel()
    {
        bool active = !panelRoot.activeSelf;
        panelRoot.SetActive(active);
        Debug.Log($"[ExperimentUI] Panel {(active ? "Shown" : "Hidden")}");
    }
}
