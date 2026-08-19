using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ExperimentLauncherController : MonoBehaviour
{
    [Header("Route selection")]
    public Toggle complexRouteToggle;
    public Toggle straightRouteToggle;

    [Header("Scene names")]
    public string launcherSceneName = "ExperimentLauncher";
    public string complexRouteSceneName = "BambergExperiment";
    public string straightRouteSceneName = "StraightLineExperiment";

    [Header("Formal identifiers")]
    [Tooltip("Anonymous participant ID. Wire a TMP input field in the launcher for formal data collection.")]
    [SerializeField] private TMP_InputField participantIdInput;

    [Tooltip("Optional session ID. If blank, a UTC timestamp is generated once and persists across scene loads.")]
    [SerializeField] private TMP_InputField sessionIdInput;

    [Tooltip("Inspector fallback for development builds without launcher input fields. Must be set explicitly; no anonymous default is generated.")]
    [SerializeField] private string participantIdFallback = "";

    [SerializeField] private string sessionIdFallback = "";

    private void Awake()
    {
        // In experiment scenes, the route toggles can stay empty.
        if (complexRouteToggle == null || straightRouteToggle == null)
            return;

        // Keep one valid default selection in the launcher scene.
        if (!complexRouteToggle.isOn && !straightRouteToggle.isOn)
            complexRouteToggle.isOn = true;

        if (FormalExperimentContext.IsSessionConfigured)
        {
            if (participantIdInput != null && string.IsNullOrWhiteSpace(participantIdInput.text))
                participantIdInput.text = FormalExperimentContext.ParticipantId;
            if (sessionIdInput != null && string.IsNullOrWhiteSpace(sessionIdInput.text))
                sessionIdInput.text = FormalExperimentContext.SessionId;
        }
    }

    public void LoadSelectedExperiment()
    {
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
