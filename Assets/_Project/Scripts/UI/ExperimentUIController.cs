using UnityEngine;
using UnityEngine.UI;

public class ExperimentUIController : MonoBehaviour
{
    public enum AnchorMode { FollowHMD, WorldFixed }
    public enum OccluderType { Static, Dynamic }
    public enum OcclusionSize { Small, Large }

    [Header("Anchor Mode Toggles")]
    [SerializeField] private Toggle anchorFollowHmd;
    [SerializeField] private Toggle anchorWorldFixed;

    [Header("Occluder Type Toggles")]
    [SerializeField] private Toggle typeStatic;
    [SerializeField] private Toggle typeDynamic;

    [Header("Occlusion Size Toggles")]
    [SerializeField] private Toggle sizeSmall;
    [SerializeField] private Toggle sizeLarge;

    [Header("No Occluder Baseline")]
    [Tooltip("When selected, no visual occluder is shown. The same injection and staircase flow still run.")]
    [SerializeField] private Toggle noOccluder;

    [Header("Apply Button")]
    [SerializeField] private Button applyButton;

    [Header("Core References")]
    [SerializeField] private MaskingEventManager maskingEventManager;
    [SerializeField] private ExperimentTrialController experimentTrialController;

    [Header("Mapping (match paper)")]
    [SerializeField] private int smallOcclusionRatio = 40;
    [SerializeField] private int largeOcclusionRatio = 70;

    [Header("Optional - Behavior")]
    [Tooltip("Apply once on Start() after defaults are ensured.")]
    [SerializeField] private bool applyOnStart = false;

    [Tooltip("If assigned, panel will be hidden after Apply(). Drag your Panel here.")]
    [SerializeField] private GameObject panelRoot;

    [Tooltip("Hide panel after Apply(). Recommended for participant mode.")]
    [SerializeField] private bool closePanelAfterApply = true;

    [Tooltip("If true, UI options are locked after Apply(). If false, they stay editable even after the trial controller starts.")]
    [SerializeField] private bool lockUIAfterApply = false;

    private bool uiGloballyInteractable = true;

    private void Awake()
    {
        if (applyButton) applyButton.onClick.AddListener(Apply);

        BindMutualExclusive(anchorFollowHmd, anchorWorldFixed);
        BindMutualExclusive(typeStatic, typeDynamic);
        BindMutualExclusive(sizeSmall, sizeLarge);

        if (noOccluder)
            noOccluder.onValueChanged.AddListener(_ => RefreshInteractability());
    }

    private void OnEnable()
    {
        // Important: ExperimentTrialController may lock this UI when a trial starts.
        // If the user chose not to lock after Apply, re-opening the panel should make it editable again.
        if (!lockUIAfterApply)
            SetUIInteractable(true);
    }

    private void Start()
    {
        EnsureDefaults();
        SetUIInteractable(!lockUIAfterApply || !applyOnStart);

        if (applyOnStart)
            Apply();
    }

    private void BindMutualExclusive(Toggle a, Toggle b)
    {
        if (!a || !b) return;
        a.onValueChanged.AddListener(on => { if (on) b.isOn = false; });
        b.onValueChanged.AddListener(on => { if (on) a.isOn = false; });
    }

    private void EnsureDefaults()
    {
        if (anchorFollowHmd && anchorWorldFixed)
        {
            if (anchorFollowHmd.isOn == anchorWorldFixed.isOn)
            {
                anchorWorldFixed.isOn = false;
                anchorFollowHmd.isOn = true; // default: old behavior
            }
        }

        if (typeStatic && typeDynamic)
        {
            if (typeStatic.isOn == typeDynamic.isOn)
            {
                typeDynamic.isOn = false;
                typeStatic.isOn = true; // default: static
            }
        }

        if (sizeSmall && sizeLarge)
        {
            if (sizeSmall.isOn == sizeLarge.isOn)
            {
                sizeLarge.isOn = false;
                sizeSmall.isOn = true; // default 40
            }
        }
    }

    public void Apply()
    {
        if (!maskingEventManager)
        {
            Debug.LogError("[ExperimentUI] MaskingEventManager not assigned.");
            return;
        }

        EnsureDefaults();

        bool useNoOccluder = noOccluder && noOccluder.isOn;

        AnchorMode anchor = ReadAnchorMode();
        OccluderType type = ReadOccluderType();
        OcclusionSize occSize = ReadOcclusionSize();

        int ratio = useNoOccluder
            ? 0
            : (occSize == OcclusionSize.Small ? smallOcclusionRatio : largeOcclusionRatio);

        MaskingEventManager.TrialOccluderType trialType = useNoOccluder
            ? MaskingEventManager.TrialOccluderType.NoOccluder
            : ConvertToTrialOccluder(anchor, type);

        maskingEventManager.ConfigureTrial(trialType, ratio);

        Debug.Log(
            $"[ExperimentUI] Apply: NoOccluder={useNoOccluder}, " +
            $"Anchor={anchor}, Type={type}, Ratio={ratio}, " +
            $"TrialOccluder={trialType}, LockUIAfterApply={lockUIAfterApply}"
        );

        if (experimentTrialController != null)
            experimentTrialController.StartCurrentConfiguredTrial();

        // This line is the actual fix:
        // ExperimentTrialController.StartCurrentConfiguredTrial() may force-lock the UI.
        // We apply the user's chosen setting AFTER that call so the Inspector option really works.
        SetUIInteractable(!lockUIAfterApply);

        if (closePanelAfterApply && panelRoot)
            panelRoot.SetActive(false);
    }

    public void SetUIInteractable(bool interactable)
    {
        uiGloballyInteractable = interactable;
        RefreshInteractability();
    }

    private void RefreshInteractability()
    {
        bool useNoOccluder = noOccluder && noOccluder.isOn;
        bool regularOptionsInteractable = uiGloballyInteractable && !useNoOccluder;

        if (anchorFollowHmd) anchorFollowHmd.interactable = regularOptionsInteractable;
        if (anchorWorldFixed) anchorWorldFixed.interactable = regularOptionsInteractable;

        if (typeStatic) typeStatic.interactable = regularOptionsInteractable;
        if (typeDynamic) typeDynamic.interactable = regularOptionsInteractable;

        if (sizeSmall) sizeSmall.interactable = regularOptionsInteractable;
        if (sizeLarge) sizeLarge.interactable = regularOptionsInteractable;

        if (noOccluder) noOccluder.interactable = uiGloballyInteractable;
        if (applyButton) applyButton.interactable = uiGloballyInteractable;
    }

    [ContextMenu("DEBUG Unlock UI")]
    public void DebugUnlockUI()
    {
        SetUIInteractable(true);
        Debug.Log("[ExperimentUI] DEBUG Unlock UI.");
    }

    [ContextMenu("DEBUG Lock UI")]
    public void DebugLockUI()
    {
        SetUIInteractable(false);
        Debug.Log("[ExperimentUI] DEBUG Lock UI.");
    }

    private AnchorMode ReadAnchorMode()
    {
        if (anchorWorldFixed && anchorWorldFixed.isOn) return AnchorMode.WorldFixed;
        return AnchorMode.FollowHMD;
    }

    private OccluderType ReadOccluderType()
    {
        if (typeDynamic && typeDynamic.isOn) return OccluderType.Dynamic;
        return OccluderType.Static;
    }

    private OcclusionSize ReadOcclusionSize()
    {
        if (sizeLarge && sizeLarge.isOn) return OcclusionSize.Large;
        return OcclusionSize.Small;
    }

    private MaskingEventManager.TrialOccluderType ConvertToTrialOccluder(AnchorMode anchor, OccluderType type)
    {
        if (anchor == AnchorMode.FollowHMD)
        {
            return type == OccluderType.Static
                ? MaskingEventManager.TrialOccluderType.Newspaper
                : MaskingEventManager.TrialOccluderType.Pigeon;
        }

        return type == OccluderType.Static
            ? MaskingEventManager.TrialOccluderType.Sign
            : MaskingEventManager.TrialOccluderType.Butterfly;
    }

    public int GetCurrentOcclusionRatio()
    {
        if (noOccluder && noOccluder.isOn)
            return 0;

        OcclusionSize occSize = ReadOcclusionSize();
        return (occSize == OcclusionSize.Small) ? smallOcclusionRatio : largeOcclusionRatio;
    }
}
