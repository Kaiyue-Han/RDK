using UnityEngine;
using UnityEngine.InputSystem;

public class RDWControllerInputGate : MonoBehaviour
{
    [Header("RDW Executor")]
    [SerializeField] private Behaviour rdwController;

    [Header("Controller Locomotion Inputs")]
    [SerializeField] private InputActionReference continuousLocomotionInput;
    [SerializeField] private InputActionReference teleportLocomotionInput;
    [SerializeField] private float moveDeadzone = 0.2f;

    public bool IsControllerLocomotionActive { get; private set; }

    private void OnEnable()
    {
        if (continuousLocomotionInput && continuousLocomotionInput.action != null)
            continuousLocomotionInput.action.Enable();

        if (teleportLocomotionInput && teleportLocomotionInput.action != null)
            teleportLocomotionInput.action.Enable();
    }

    private void OnDisable()
    {
        if (continuousLocomotionInput && continuousLocomotionInput.action != null)
            continuousLocomotionInput.action.Disable();

        if (teleportLocomotionInput && teleportLocomotionInput.action != null)
            teleportLocomotionInput.action.Disable();
    }

    private void Update()
    {
        if (!rdwController) return;

        IsControllerLocomotionActive =
            IsContinuousLocomotionActive() || IsTeleportLocomotionActive();

        rdwController.enabled = !IsControllerLocomotionActive;
    }

    private bool IsContinuousLocomotionActive()
    {
        if (!continuousLocomotionInput || continuousLocomotionInput.action == null)
            return false;

        Vector2 v = continuousLocomotionInput.action.ReadValue<Vector2>();
        return v.magnitude > moveDeadzone;
    }

    private bool IsTeleportLocomotionActive()
    {
        if (!teleportLocomotionInput || teleportLocomotionInput.action == null)
            return false;

        return teleportLocomotionInput.action.IsPressed();
    }
}