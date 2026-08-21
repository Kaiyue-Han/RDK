using UnityEngine;

public sealed class TrainingEventTrigger : MonoBehaviour
{
    [Header("Exact participant collider")]
    [Tooltip("Assign VR Player/Camera Offset/Main Camera/PlayerTrigger CapsuleCollider.")]
    [SerializeField] private Collider participantCollider;

    [Header("Training controller")]
    [SerializeField] private TrainingDemoController demoController;

    [Header("Runtime state")]
    [SerializeField] private bool armed;
    [SerializeField] private bool triggered;
    [SerializeField] private bool participantInside;

    public bool IsArmed => armed;
    public bool HasTriggered => triggered;

    private void Awake()
    {
        ResetTrigger();
    }

    public bool Arm()
    {
        if (participantCollider == null || demoController == null)
        {
            Debug.LogError(
                "[TrainingEventTrigger] Participant collider and demo controller must be assigned.",
                this
            );
            return false;
        }

        if (participantInside)
        {
            Debug.LogWarning(
                "[TrainingEventTrigger] Cannot arm while the participant is inside the trigger. " +
                "Reset and return to the start position first.",
                this
            );
            return false;
        }

        triggered = false;
        armed = true;
        return true;
    }

    public void ResetTrigger()
    {
        armed = false;
        triggered = false;
        participantInside = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other != participantCollider)
            return;

        participantInside = true;

        if (!armed || triggered)
            return;

        // Close the one-shot gate before invoking any downstream event callbacks.
        armed = false;
        triggered = true;

        if (!demoController.PlaySelectedExample())
        {
            Debug.LogError("[TrainingEventTrigger] The selected demonstration could not start.", this);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == participantCollider)
            participantInside = false;
    }
}
