using TMPro;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ExperimentLauncherController : MonoBehaviour
{
    [Header("Route selection")]
    public Toggle complexRouteToggle;
    public Toggle straightRouteToggle;
    public Toggle trainingToggle;

    [Header("Scene names")]
    public string launcherSceneName = "ExperimentLauncher";
    public string complexRouteSceneName = "BambergExperiment";
    public string straightRouteSceneName = "StraightLineExperiment";
    public string trainingSceneName = "Training";

    [Header("Formal identifiers")]
    [Tooltip("Anonymous participant ID. Wire a TMP input field in the launcher for formal data collection.")]
    [SerializeField] private TMP_InputField participantIdInput;

    [Tooltip("Optional session ID. If blank, a UTC timestamp is generated once and persists across scene loads.")]
    [SerializeField] private TMP_InputField sessionIdInput;

    [Tooltip("Inspector fallback for development builds without launcher input fields. Must be set explicitly; no anonymous default is generated.")]
    [SerializeField] private string participantIdFallback = "";

    [SerializeField] private string sessionIdFallback = "";

    [Header("Quest participant ID selector")]
    [Tooltip("Use controller-operated increment/decrement buttons instead of relying on a Quest virtual keyboard.")]
    [SerializeField] private bool useParticipantNumberSelector = true;

    [Min(1)]
    [SerializeField] private int participantNumber = 1;

    [SerializeField] private string participantIdPrefix = "P";

    [Min(1)]
    [SerializeField] private int participantNumberDigits = 3;

    private void Awake()
    {
        // In experiment scenes, the route toggles can stay empty.
        if (
            complexRouteToggle == null &&
            straightRouteToggle == null &&
            trainingToggle == null
        )
            return;

        // Keep one valid default selection in the launcher scene.
        bool hasSelection =
            (complexRouteToggle != null && complexRouteToggle.isOn) ||
            (straightRouteToggle != null && straightRouteToggle.isOn) ||
            (trainingToggle != null && trainingToggle.isOn);

        if (!hasSelection)
        {
            if (complexRouteToggle != null)
                complexRouteToggle.isOn = true;
            else if (straightRouteToggle != null)
                straightRouteToggle.isOn = true;
            else if (trainingToggle != null)
                trainingToggle.isOn = true;
        }

        if (FormalExperimentContext.IsSessionConfigured)
        {
            if (
                participantIdInput != null &&
                (
                    string.IsNullOrWhiteSpace(participantIdInput.text) ||
                    IsLegacyFieldLabel(participantIdInput.text, "Participant ID")
                )
            )
                participantIdInput.text = FormalExperimentContext.ParticipantId;
            if (
                sessionIdInput != null &&
                (
                    string.IsNullOrWhiteSpace(sessionIdInput.text) ||
                    IsLegacyFieldLabel(sessionIdInput.text, "Session ID")
                )
            )
                sessionIdInput.text = FormalExperimentContext.SessionId;
        }

        InitializeQuestIdentifierEntry();
    }

    public void SelectPreviousParticipant()
    {
        ChangeParticipantNumber(-1);
    }

    public void SelectNextParticipant()
    {
        ChangeParticipantNumber(1);
    }

    public void LoadSelectedExperiment()
    {
        // Training is deliberately outside the formal participant/session scope.
        if (trainingToggle != null && trainingToggle.isOn)
        {
            LoadScene(trainingSceneName);
            return;
        }

        if (!EnsureFormalIdentifiers())
            return;

        if (complexRouteToggle != null && complexRouteToggle.isOn)
        {
            LoadScene(complexRouteSceneName);
            return;
        }

        if (straightRouteToggle != null && straightRouteToggle.isOn)
        {
            LoadScene(straightRouteSceneName);
            return;
        }

        Debug.LogWarning("[ExperimentLauncher] No route selected.");
    }

    private bool EnsureFormalIdentifiers()
    {
        string participantId = participantIdInput != null
            ? participantIdInput.text
            : participantIdFallback;
        string sessionId = sessionIdInput != null
            ? sessionIdInput.text
            : sessionIdFallback;

        if (IsLegacyFieldLabel(participantId, "Participant ID"))
            participantId = "";
        if (IsLegacyFieldLabel(sessionId, "Session ID"))
            sessionId = "";

        if (
            FormalExperimentContext.IsSessionConfigured &&
            !string.Equals(
                participantId?.Trim(),
                FormalExperimentContext.ParticipantId,
                StringComparison.OrdinalIgnoreCase
            ) &&
            string.Equals(
                sessionId?.Trim(),
                FormalExperimentContext.SessionId,
                StringComparison.Ordinal
            )
        )
        {
            // A newly selected participant must receive a new automatically
            // generated session instead of inheriting the previous person's ID.
            sessionId = "";
            if (sessionIdInput != null)
                sessionIdInput.text = "";
        }

        if (
            string.IsNullOrWhiteSpace(participantId) &&
            FormalExperimentContext.IsSessionConfigured
        )
        {
            return true;
        }

        if (!FormalExperimentContext.TryConfigureSession(participantId, sessionId, out string error))
        {
            Debug.LogError($"[ExperimentLauncher] {error}", this);
            return false;
        }

        return true;
    }

    private void InitializeQuestIdentifierEntry()
    {
        if (participantIdInput == null)
            return;

        if (IsLegacyFieldLabel(participantIdInput.text, "Participant ID"))
            participantIdInput.text = "";
        if (sessionIdInput != null && IsLegacyFieldLabel(sessionIdInput.text, "Session ID"))
            sessionIdInput.text = "";

        if (!useParticipantNumberSelector)
            return;

        participantIdInput.readOnly = true;

        if (TryParseParticipantNumber(participantIdInput.text, out int existingNumber))
            participantNumber = existingNumber;
        else
            WriteParticipantId();
    }

    private void ChangeParticipantNumber(int delta)
    {
        if (!useParticipantNumberSelector)
            return;

        participantNumber = Mathf.Max(1, participantNumber + delta);
        WriteParticipantId();

        // Leaving this blank tells FormalExperimentContext to create a fresh
        // UTC session ID when this participant starts a formal scene.
        if (sessionIdInput != null)
            sessionIdInput.text = "";
        sessionIdFallback = "";
    }

    private void WriteParticipantId()
    {
        string prefix = string.IsNullOrWhiteSpace(participantIdPrefix)
            ? "P"
            : participantIdPrefix.Trim();
        string formattedId = prefix + participantNumber.ToString(
            new string('0', Mathf.Max(1, participantNumberDigits))
        );

        if (participantIdInput != null)
            participantIdInput.SetTextWithoutNotify(formattedId);
        participantIdFallback = formattedId;
    }

    private bool TryParseParticipantNumber(string participantId, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(participantId))
            return false;

        string prefix = string.IsNullOrWhiteSpace(participantIdPrefix)
            ? "P"
            : participantIdPrefix.Trim();
        string trimmed = participantId.Trim();
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string numericPart = trimmed.Substring(prefix.Length);
        return int.TryParse(numericPart, out number) && number >= 1;
    }

    private static bool IsLegacyFieldLabel(string value, string label)
    {
        return string.Equals(value?.Trim(), label, StringComparison.OrdinalIgnoreCase);
    }

    public void ReturnToLauncher()
    {
        LoadScene(launcherSceneName);
    }

    private static void LoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("[ExperimentLauncher] Scene name is empty.");
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError(
                $"[ExperimentLauncher] Scene '{sceneName}' is not available. " +
                "Check the scene name and add it to Build Profiles / Build Settings."
            );
            return;
        }

        SceneManager.LoadScene(sceneName);
    }
}
