using System;
using UnityEngine;

public class ParticipantFeedbackController : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Component that implements IButtonInput, usually XRButtonInput.")]
    [SerializeField] private MonoBehaviour feedbackInputBehaviour;

    [Header("Behaviour")]
    [Tooltip("If true, feedback is only accepted while listening is enabled.")]
    [SerializeField] private bool requireListeningEnabled = true;

    [Tooltip("Optional cooldown to prevent accidental repeated reports.")]
    [SerializeField] private float feedbackCooldownSec = 0.3f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    /// <summary>
    /// Fired once whenever the participant reports noticing something.
    /// </summary>
    public event Action OnFeedbackReported;

    /// <summary>
    /// True if at least one unconsumed feedback report exists.
    /// </summary>
    public bool HasPendingFeedback => pendingFeedbackCount > 0;

    /// <summary>
    /// Number of unconsumed feedback reports.
    /// </summary>
    public int PendingFeedbackCount => pendingFeedbackCount;

    /// <summary>
    /// Total number of feedback reports since last reset.
    /// </summary>
    public int TotalFeedbackCount => totalFeedbackCount;

    /// <summary>
    /// Whether this controller is currently accepting feedback.
    /// </summary>
    public bool IsListeningEnabled => listeningEnabled;

    private IButtonInput feedbackInput;
    private bool listeningEnabled = true;
    private int pendingFeedbackCount = 0;
    private int totalFeedbackCount = 0;
    private float lastFeedbackTime = -999f;
    private void Start()
    {
        Debug.Log("[ParticipantFeedbackController] Start called.", this);
    }
    private void Awake()
    {
        feedbackInput = feedbackInputBehaviour as IButtonInput;

        if (feedbackInputBehaviour != null && feedbackInput == null)
        {
            Debug.LogError(
                "[ParticipantFeedbackController] Assigned feedbackInputBehaviour does not implement IButtonInput.",
                this
            );
        }
    }

    private void Update()
    {
        if (feedbackInput == null)
            return;

        if (requireListeningEnabled && !listeningEnabled)
            return;

        if (!feedbackInput.PressedThisFrame())
            return;

        if (Time.time - lastFeedbackTime < feedbackCooldownSec)
            return;

        lastFeedbackTime = Time.time;
        pendingFeedbackCount++;
        totalFeedbackCount++;

        if (debugLog)
        {
            Debug.Log(
                $"[ParticipantFeedbackController] Feedback reported. " +
                $"Total={totalFeedbackCount}, Pending={pendingFeedbackCount}",
                this
            );
        }

        OnFeedbackReported?.Invoke();
    }

    public void EnableListening()
    {
        listeningEnabled = true;

        if (debugLog)
            Debug.Log("[ParticipantFeedbackController] Listening enabled.", this);
    }

    public void DisableListening()
    {
        listeningEnabled = false;

        if (debugLog)
            Debug.Log("[ParticipantFeedbackController] Listening disabled.", this);
    }

    public bool ConsumeFeedback()
    {
        if (pendingFeedbackCount <= 0)
            return false;

        pendingFeedbackCount--;

        if (debugLog)
        {
            Debug.Log(
                $"[ParticipantFeedbackController] Consumed one feedback. Pending={pendingFeedbackCount}",
                this
            );
        }

        return true;
    }

    public void ClearPendingFeedback()
    {
        pendingFeedbackCount = 0;

        if (debugLog)
            Debug.Log("[ParticipantFeedbackController] Pending feedback cleared.", this);
    }

    public void ResetState(bool enableListeningAfterReset = true)
    {
        pendingFeedbackCount = 0;
        totalFeedbackCount = 0;
        lastFeedbackTime = -999f;
        listeningEnabled = enableListeningAfterReset;

        if (debugLog)
            Debug.Log("[ParticipantFeedbackController] State reset.", this);
    }
}