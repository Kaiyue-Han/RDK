using UnityEngine;
using TMPro;

public class TrialFinishUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ExperimentTrialController trialController;
    [SerializeField] private GainSearchFlowController searchFlowController;

    [Header("Experimenter Main UI")]
    [SerializeField] private GameObject experimenterPanelRoot;

    [Header("Participant UI")]
    [SerializeField] private GameObject trialCompletePanel;
    [SerializeField] private TMP_Text completeText;

    [Header("Experimenter UI")]
    [SerializeField] private TMP_Text resultText;

    private bool shownFinished = false;

    private void Start()
    {
        if (trialCompletePanel != null)
            trialCompletePanel.SetActive(false);

        RefreshResultText(false);
    }

    private void Update()
    {
        // If experimenter UI is opened, automatically hide participant finish panel
        if (experimenterPanelRoot != null &&
            experimenterPanelRoot.activeSelf &&
            trialCompletePanel != null &&
            trialCompletePanel.activeSelf)
        {
            trialCompletePanel.SetActive(false);
        }

        if (trialController == null) return;

        if (!shownFinished && trialController.TrialFinished)
        {
            shownFinished = true;

            if (trialCompletePanel != null)
                trialCompletePanel.SetActive(true);

            if (completeText != null)
                completeText.text = "Trial Complete\nPlease wait for the experimenter.";

            RefreshResultText(true);
        }
    }

    public void ResetUIState()
    {
        shownFinished = false;

        if (trialCompletePanel != null)
            trialCompletePanel.SetActive(false);

        RefreshResultText(false);
    }

    private void RefreshResultText(bool finished)
    {
        if (resultText == null)
            return;

        if (!finished)
        {
            resultText.text = "Confirmed Theta: -";
            return;
        }

        float theta = -1f;

        if (searchFlowController != null && searchFlowController.HasConfirmedUpperAcceptableTheta)
        {
            theta = searchFlowController.ConfirmedUpperAcceptableThetaDeg;
        }
        else if (trialController != null && trialController.FinalConfirmedThetaDeg > 0f)
        {
            theta = trialController.FinalConfirmedThetaDeg;
        }

        resultText.text = theta >= 0f
            ? $"Confirmed Theta: {theta:F1}°"
            : "Confirmed Theta: -";
    }
}