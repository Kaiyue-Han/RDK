using UnityEngine;

public class ExperimentUIPanelToggle : MonoBehaviour
{
    [Header("UI Root")]
    public GameObject panelRoot;

    [Header("Toggle Input")]
    public KeyCode keyboardToggleKey = KeyCode.Tab;

    [Tooltip("Optional XR input (must implement IButtonInput)")]
    public MonoBehaviour inputBehaviour;

    private IButtonInput input;

    void Awake()
    {
        if (inputBehaviour)
            input = inputBehaviour as IButtonInput;

        if (!panelRoot)
            Debug.LogWarning("[ExperimentUI] panelRoot is not assigned.");
    }

    void Start()
    {
        if (panelRoot != null)
            panelRoot.SetActive(true); // 启动默认显示
    }

    void Update()
    {
        if (panelRoot == null) return;

        if (Input.GetKeyDown(keyboardToggleKey))
            TogglePanel();

        if (input != null && input.PressedThisFrame())
            TogglePanel();
    }

    void TogglePanel()
    {
        bool active = !panelRoot.activeSelf;
        panelRoot.SetActive(active);
        Debug.Log($"[ExperimentUI] Panel {(active ? "Shown" : "Hidden")}");
    }
}