using UnityEngine;
using UnityEngine.InputSystem;

public class RDWControllerInputGate : MonoBehaviour
{
    [Header("RDW Executor")]
    [SerializeField] private Behaviour rdwController;

    [Header("Controller Locomotion Inputs")]
    [SerializeField] private InputActionReference continuousLocomotionInput;
    [SerializeField] private InputActionReference teleportLocomotionInput;
    [SerializeField] private InputActionReference turnLocomotionInput;
    [SerializeField] private float moveDeadzone = 0.2f;

    public bool IsControllerLocomotionActive { get; private set; }

    private void OnEnable()
    {
        if (continuousLocomotionInput && continuousLocomotionInput.action != null)
            continuousLocomotionInput.action.Enable();

        if (teleportLocomotionInput && teleportLocomotionInput.action != null)
            teleportLocomotionInput.action.Enable();

        if (turnLocomotionInput && turnLocomotionInput.action != null)
            turnLocomotionInput.action.Enable();
    }

    private void OnDisable()
    {
        if (continuousLocomotionInput && continuousLocomotionInput.action != null)
            continuousLocomotionInput.action.Disable();

        if (teleportLocomotionInput && teleportLocomotionInput.action != null)
            teleportLocomotionInput.action.Disable();

        if (turnLocomotionInput && turnLocomotionInput.action != null)
            turnLocomotionInput.action.Disable();
    }

    private void Update()
    {
        if (!rdwController) return;

        IsControllerLocomotionActive =
            IsContinuousLocomotionActive() ||
            IsTeleportLocomotionActive() ||
            IsTurnLocomotionActive();

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

        Vector2 v = teleportLocomotionInput.action.ReadValue<Vector2>();
        return v.magnitude > moveDeadzone;
    }

    private bool IsTurnLocomotionActive()
    {
        if (!turnLocomotionInput || turnLocomotionInput.action == null)
            return false;

        Vector2 v = turnLocomotionInput.action.ReadValue<Vector2>();
        return v.magnitude > moveDeadzone;
    }
}
