using UnityEngine;
using UnityEngine.UI;

public class ExperimentUIController : MonoBehaviour
{
    public enum PlayAreaSize { Size3x3, Size6x6 }
    public enum OccluderType { Static, Dynamic }
    public enum OcclusionSize { Small, Large }

    [Header("UI Toggles")]
    [SerializeField] private Toggle space3x3;
    [SerializeField] private Toggle space6x6;

    [SerializeField] private Toggle typeStatic;
    [SerializeField] private Toggle typeDynamic;

    [SerializeField] private Toggle sizeSmall;
    [SerializeField] private Toggle sizeLarge;

    [Header("Apply Button")]
    [SerializeField] private Button applyButton;

    [Header("Core References")]
    [SerializeField] private MaskingEventManager maskingEventManager;

    [Tooltip("Drag the component that implements IOccluder for Static condition (e.g., UIOccluder).")]
    [SerializeField] private MonoBehaviour staticOccluderBehaviour;

    [Tooltip("Drag the component that implements IOccluder for Dynamic condition (e.g., UmbrellaOccluder).")]
    [SerializeField] private MonoBehaviour DynamicOccluderBehaviour;

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

    [Tooltip("Lock UI interactions after Apply() (toggles/buttons become non-interactable).")]
    [SerializeField] private bool lockUIAfterApply = false;

    private void Awake()
    {
        if (applyButton) applyButton.onClick.AddListener(Apply);

        // 保底：确保每组一定是二选一（即使你没配 ToggleGroup）
        BindMutualExclusive(space3x3, space6x6);
        BindMutualExclusive(typeStatic, typeDynamic);
        BindMutualExclusive(sizeSmall, sizeLarge);
    }

    private void Start()
    {
        EnsureDefaults();
        if (applyOnStart) Apply();
    }

    private void BindMutualExclusive(Toggle a, Toggle b)
    {
        if (!a || !b) return;
        a.onValueChanged.AddListener(on => { if (on) b.isOn = false; });
        b.onValueChanged.AddListener(on => { if (on) a.isOn = false; });
    }

    private void EnsureDefaults()
    {
        // Space Size
        if (space3x3 && space6x6)
        {
            if (space3x3.isOn == space6x6.isOn) // 两个都开 或 两个都关
            {
                space3x3.isOn = false;
                space6x6.isOn = true; // 默认 6x6
            }
        }

        // Occluder Type
        if (typeStatic && typeDynamic)
        {
            if (typeStatic.isOn == typeDynamic.isOn)
            {
                typeDynamic.isOn = false;
                typeStatic.isOn = true; // 默认 Static
            }
        }

        // Occlusion Size
        if (sizeSmall && sizeLarge)
        {
            if (sizeSmall.isOn == sizeLarge.isOn)
            {
                sizeLarge.isOn = false;
                sizeSmall.isOn = true; // 默认 Small
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

        // 0) Clean boundary: Apply should happen at a "trial boundary"
        maskingEventManager.AbortAndResetToIdle();

        var type = ReadOccluderType();
        var occSize = ReadOcclusionSize();
        var area = ReadPlayArea(); // 目前不接 coin manager，但保留
        int ratio = (occSize == OcclusionSize.Small) ? smallOcclusionRatio : largeOcclusionRatio;

        // 1) ratio
        maskingEventManager.SetOcclusionRatio(ratio);

        // 2) occluder type (swap behaviour + rebind IOccluder)
        MonoBehaviour chosenOccluder = (type == OccluderType.Static) ? staticOccluderBehaviour : DynamicOccluderBehaviour;
        if (!chosenOccluder)
        {
            Debug.LogError($"[ExperimentUI] OccluderBehaviour for {type} not assigned.");
            return;
        }

        if (!(chosenOccluder is IOccluder))
        {
            Debug.LogError($"[ExperimentUI] {chosenOccluder.name} does NOT implement IOccluder.");
            return;
        }

        maskingEventManager.SetOccluderBehaviour(chosenOccluder);

        Debug.Log($"[ExperimentUI] Apply: Area={area}, Type={type}, Ratio={ratio}");

        // 3) Optional: lock UI (prevent accidental changes during trial)
        if (lockUIAfterApply)
            SetUIInteractable(false);

        // 4) Optional: close panel after apply (recommended for participant mode)
        if (closePanelAfterApply && panelRoot)
            panelRoot.SetActive(false);
    }

    public void SetUIInteractable(bool interactable)
    {
        if (space3x3) space3x3.interactable = interactable;
        if (space6x6) space6x6.interactable = interactable;

        if (typeStatic) typeStatic.interactable = interactable;
        if (typeDynamic) typeDynamic.interactable = interactable;

        if (sizeSmall) sizeSmall.interactable = interactable;
        if (sizeLarge) sizeLarge.interactable = interactable;

        if (applyButton) applyButton.interactable = interactable;
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

    private PlayAreaSize ReadPlayArea()
    {
        if (space3x3 && space3x3.isOn) return PlayAreaSize.Size3x3;
        return PlayAreaSize.Size6x6;
    }
}
