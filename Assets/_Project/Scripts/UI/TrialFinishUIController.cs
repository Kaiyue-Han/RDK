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

    [Header("Legacy Experimenter UI")]
    [Tooltip("Optional old single result text. It will show a compact summary if assigned.")]
    [SerializeField] private TMP_Text resultText;

    [Header("Result Page UI")]
    [SerializeField] private TMP_Text conditionText;
    [SerializeField] private TMP_Text estimatedThresholdText;
    [SerializeField] private TMP_Text resultStatusText;
    [SerializeField] private TMP_Text reversalsText;
    [SerializeField] private TMP_Text validTrialsText;
    [SerializeField] private TMP_Text stopReasonText;

    [Header("Catch Control UI (Experimenter Only)")]
    [Tooltip("Optional. Displays zero-catch false alarms, e.g. 1 / 2 (50%).")]
    [SerializeField] private TMP_Text zeroCatchText;
    [Tooltip("Optional. Displays high-catch detections, e.g. 1 / 1 (100%).")]
    [SerializeField] private TMP_Text highCatchText;

    [Header("Refresh")]
    [SerializeField] private bool autoRefreshResultPage = true;
    [SerializeField] private float refreshIntervalSec = 0.25f;

    private bool shownFinished = false;
    private float nextRefreshTime = 0f;

    private void Start()
    {
        if (trialCompletePanel != null)
            trialCompletePanel.SetActive(false);

        RefreshResultPage();
    }

    private void Update()
    {
        // If experimenter UI is opened, automatically hide participant finish panel.
        if (experimenterPanelRoot != null &&
            experimenterPanelRoot.activeSelf &&
            trialCompletePanel != null &&
            trialCompletePanel.activeSelf)
        {
            trialCompletePanel.SetActive(false);
        }

        if (trialController != null && !shownFinished && trialController.TrialFinished)
        {
            shownFinished = true;

            if (trialCompletePanel != null)
                trialCompletePanel.SetActive(true);

            if (completeText != null)
                completeText.text = "Trial Complete\nPlease wait for the experimenter.";

            RefreshResultPage();
        }

        if (autoRefreshResultPage && Time.time >= nextRefreshTime)
        {
            nextRefreshTime = Time.time + Mathf.Max(0.05f, refreshIntervalSec);
            RefreshResultPage();
        }
    }

    public void ResetUIState()
    {
        shownFinished = false;

        if (trialCompletePanel != null)
            trialCompletePanel.SetActive(false);

        RefreshResultPage();
    }

    public void RefreshResultPage()
    {
        string condition = GetConditionText();
        string status = GetResultStatus();
        string threshold = GetThresholdText(status);
        string reversals = GetReversalsText();
        string validTrials = GetValidTrialsText();
        string stopReason = GetStopReasonText(status);
        string catchFalseAlarms = GetCatchFalseAlarmText();
        string catchHitRate = GetCatchHitRateText();

        if (conditionText != null)
            conditionText.text = $"Condition: {condition}";

        if (estimatedThresholdText != null)
            estimatedThresholdText.text = $"Estimated Threshold: {threshold}";

        if (resultStatusText != null)
            resultStatusText.text = $"Result Status: {status}";

        if (reversalsText != null)
            reversalsText.text = $"Reversals: {reversals}";

        if (validTrialsText != null)
            validTrialsText.text = $"Valid Trials: {validTrials}";

        if (stopReasonText != null)
            stopReasonText.text = $"Stop Reason: {stopReason}";

        // These fields belong to the experimenter result page only.
        // Nothing is shown on the participant trial-complete panel.
        if (zeroCatchText != null)
            zeroCatchText.text = $"Zero Catch: {catchFalseAlarms} false alarms";

        if (highCatchText != null)
            highCatchText.text = $"High Catch: {catchHitRate} detected";

        if (resultText != null)
        {
            resultText.text =
                $"Condition: {condition}\n" +
                $"Estimated Threshold: {threshold}\n" +
                $"Result Status: {status}\n" +
                $"Reversals: {reversals}\n" +
                $"Valid Trials: {validTrials}\n" +
                $"Zero Catch: {catchFalseAlarms} false alarms\n" +
                $"High Catch: {catchHitRate} detected\n" +
                $"Stop Reason: {stopReason}";
        }
    }

    private string GetConditionText()
    {
        if (trialController != null && !string.IsNullOrEmpty(trialController.CurrentConditionName) && trialController.CurrentConditionName != "Unknown")
            return trialController.CurrentConditionName;

        return "-";
    }

    private string GetResultStatus()
    {
        if (searchFlowController == null)
            return "Unavailable";

        if (trialController != null)
        {
            if (trialController.TrialPaused)
                return "Paused";

            if (trialController.TrialRunning && !trialController.TrialFinished)
                return "Running";

            if (trialController.TrialFinished)
            {
                if (IsAbortStopReason(searchFlowController.StopReason))
                    return "Aborted";

                return searchFlowController.HasEstimatedThreshold && searchFlowController.ThresholdReliable
                    ? "Complete"
                    : "Incomplete";
            }
        }

        if (searchFlowController.Phase == GainSearchFlowController.SearchPhase.Staircase)
            return "Running";

        if (searchFlowController.Phase == GainSearchFlowController.SearchPhase.Finished)
        {
            if (IsAbortStopReason(searchFlowController.StopReason))
                return "Aborted";

            return searchFlowController.HasEstimatedThreshold && searchFlowController.ThresholdReliable
                ? "Complete"
                : "Incomplete";
        }

        return "Pending";
    }

    private string GetThresholdText(string status)
    {
        if (searchFlowController != null && searchFlowController.HasEstimatedThreshold)
            return $"{searchFlowController.EstimatedThresholdDeg:F1}°";

        if (status == "Running" || status == "Paused")
            return "Pending";

        if (status == "Incomplete" || status == "Aborted")
            return "Not available";

        return "-";
    }

    private string GetReversalsText()
    {
        if (searchFlowController == null)
            return "-";

        return $"{searchFlowController.ReversalCount} / {searchFlowController.TargetReversalCount}";
    }

    private string GetValidTrialsText()
    {
        if (searchFlowController == null)
            return "-";

        return $"{searchFlowController.ValidTrialCount} / {searchFlowController.MaxValidTrials}";
    }

    private string GetCatchFalseAlarmText()
    {
        if (searchFlowController == null)
            return "-";

        if (!searchFlowController.CatchEventsEnabled)
            return "Disabled";

        int total = searchFlowController.ZeroCatchCount;
        int falseAlarms = searchFlowController.ZeroCatchFalseAlarmCount;

        if (total <= 0)
            return "0 / 0 (-)";

        return $"{falseAlarms} / {total} ({searchFlowController.ZeroCatchFalseAlarmRate:P0})";
    }

    private string GetCatchHitRateText()
    {
        if (searchFlowController == null)
            return "-";

        if (!searchFlowController.CatchEventsEnabled)
            return "Disabled";

        int total = searchFlowController.HighCatchCount;
        int hits = searchFlowController.HighCatchHitCount;

        if (total <= 0)
            return "0 / 0 (-)";

        return $"{hits} / {total} ({searchFlowController.HighCatchHitRate:P0})";
    }

    private string GetStopReasonText(string status)
    {
        if (searchFlowController == null)
            return "-";

        if (status == "Running" || status == "Paused" || status == "Pending")
            return "-";

        return FormatStopReason(searchFlowController.StopReason);
    }

    private static bool IsAbortStopReason(string reason)
    {
        return reason == "ABORTED" || reason == "STOP_SEARCH" || reason == "TRIAL_ABORT";
    }

    private static string FormatStopReason(string reason)
    {
        if (string.IsNullOrEmpty(reason) || reason == "NONE")
            return "-";

        switch (reason)
        {
            case "RUNNING":
                return "-";
            case "TARGET_REVERSALS_REACHED":
                return "Target reversals reached";
            case "MAX_VALID_TRIALS_REACHED":
                return "Max valid trials reached";
            case "ABORTED":
            case "STOP_SEARCH":
            case "TRIAL_ABORT":
                return "Manual abort";
            case "RESET":
                return "Reset";
            default:
                return reason.Replace('_', ' ');
        }
    }
}
