using UnityEngine;
using UnityEngine.UI;

public class ExperimentUITabController : MonoBehaviour
{
    public enum UITab
    {
        Settings,
        Result
    }

    [Header("Panels")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private GameObject resultPanel;

    [Header("Tab Buttons")]
    [SerializeField] private Button settingsTabButton;
    [SerializeField] private Button resultTabButton;

    [Header("Optional References")]
    [SerializeField] private TrialFinishUIController resultUIController;

    [Header("Optional Tab Visuals")]
    [Tooltip("If empty, the Image on Settings Tab Button will be used.")]
    [SerializeField] private Graphic settingsTabBackground;

    [Tooltip("If empty, the Image on Result Tab Button will be used.")]
    [SerializeField] private Graphic resultTabBackground;

    [Tooltip("Keep this OFF for tab-style UI. Turning it on makes the active tab not clickable.")]
    [SerializeField] private bool disableActiveTabButton = false;

    [Tooltip("Selected tab background alpha. 0 means the selected tab background is invisible but still clickable.")]
    [Range(0f, 1f)]
    [SerializeField] private float selectedBackgroundAlpha = 0f;

    [Tooltip("Unselected tab background alpha. 1 means the unselected tab background is fully visible.")]
    [Range(0f, 1f)]
    [SerializeField] private float unselectedBackgroundAlpha = 1f;

    [Header("Startup")]
    [SerializeField] private UITab defaultTab = UITab.Settings;

    public UITab CurrentTab => currentTab;

    private UITab currentTab = UITab.Settings;

    private void Awake()
    {
        AutoAssignTabBackgrounds();

        if (settingsTabButton != null)
            settingsTabButton.onClick.AddListener(ShowSettings);

        if (resultTabButton != null)
            resultTabButton.onClick.AddListener(ShowResult);
    }

    private void Start()
    {
        ShowTab(defaultTab);
    }

    private void OnValidate()
    {
        AutoAssignTabBackgrounds();
        ApplyTabVisuals(currentTab);
    }

    public void ShowSettings()
    {
        ShowTab(UITab.Settings);
    }

    public void ShowResult()
    {
        ShowTab(UITab.Result);
    }

    public void ShowTab(UITab tab)
    {
        currentTab = tab;

        bool showSettings = tab == UITab.Settings;
        bool showResult = tab == UITab.Result;

        if (settingsPanel != null)
            settingsPanel.SetActive(showSettings);

        if (resultPanel != null)
            resultPanel.SetActive(showResult);

        if (settingsTabButton != null)
            settingsTabButton.interactable = !disableActiveTabButton || !showSettings;

        if (resultTabButton != null)
            resultTabButton.interactable = !disableActiveTabButton || !showResult;

        ApplyTabVisuals(tab);

        if (showResult && resultUIController != null)
            resultUIController.RefreshResultPage();
    }

    private void AutoAssignTabBackgrounds()
    {
        if (settingsTabBackground == null && settingsTabButton != null)
            settingsTabBackground = settingsTabButton.GetComponent<Graphic>();

        if (resultTabBackground == null && resultTabButton != null)
            resultTabBackground = resultTabButton.GetComponent<Graphic>();
    }

    private void ApplyTabVisuals(UITab selectedTab)
    {
        SetGraphicAlpha(settingsTabBackground, selectedTab == UITab.Settings ? selectedBackgroundAlpha : unselectedBackgroundAlpha);
        SetGraphicAlpha(resultTabBackground, selectedTab == UITab.Result ? selectedBackgroundAlpha : unselectedBackgroundAlpha);
    }

    private static void SetGraphicAlpha(Graphic graphic, float alpha)
    {
        if (graphic == null)
            return;

        Color color = graphic.color;
        color.a = Mathf.Clamp01(alpha);
        graphic.color = color;

        // Keep the Graphic component enabled so the Button can still receive pointer/ray clicks.
        graphic.raycastTarget = true;
    }
}
