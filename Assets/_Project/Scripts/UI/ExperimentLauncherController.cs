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

    private void Awake()
    {
        // In experiment scenes, the route toggles can stay empty.
        if (complexRouteToggle == null || straightRouteToggle == null)
            return;

        // Keep one valid default selection in the launcher scene.
        if (!complexRouteToggle.isOn && !straightRouteToggle.isOn)
            complexRouteToggle.isOn = true;
    }

    public void LoadSelectedExperiment()
    {
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