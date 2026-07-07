using UnityEngine;

public class ExperimentUIPanelToggle : MonoBehaviour
{
    [Header("UI Root")]
    [SerializeField] private GameObject panelRoot;

    [Tooltip("Input provider that implements IButtonInput.")]
    [SerializeField] private MonoBehaviour inputBehaviour;

    private IButtonInput input;

    void Awake()
    {
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
        if (panelRoot == null || input == null) return;

        if (input.PressedThisFrame())
        {
            TogglePanel();
        }
    }

    private void TogglePanel()
    {
        bool active = !panelRoot.activeSelf;
        panelRoot.SetActive(active);
        Debug.Log($"[ExperimentUI] Panel {(active ? "Shown" : "Hidden")}");
    }
}