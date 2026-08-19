using System;
using System.Collections.Generic;
using UnityEngine;

public class RotationInjectionController : MonoBehaviour
{
    [Header("Dependencies")]
    public PlayAreaRectProvider playArea;
    public MaskingEventManager maskingEventManager;
    public WalkingDetector walkingDetector;

    [Header("Logging")]
    public EventLogger eventLogger;

    [Header("Injection settings")]
    public bool enableInjection = true;

    [Tooltip("Current experiment-driven event theta (deg). No longer derived from boundary distance.")]
    public float currentEventThetaDeg = 10f;

    public float thetaHardMax = 25f;

    [Header("Current base direction gate")]
    [Tooltip("Event injection is allowed only when the CURRENT base yaw-rate magnitude reaches this value.")]
    public float minBaseYawRateForInjection = 1.0f;

    [Header("User movement congruence gate")]
    [Tooltip("If true, event injection starts only when the user's recent physical trajectory is straight or turns in the same direction as the current base steering.")]
    public bool requireUserMovementCongruence = true;

    [Tooltip("Recent time window used to estimate the user's physical turning trend (seconds).")]
    public float movementTrendWindowSec = 0.6f;

    [Tooltip("Minimum physical displacement required in each half of the trend window (meters).")]
    public float minMovementSegmentDistance = 0.06f;

    [Tooltip("Absolute signed turn angle below this value is treated as straight walking (degrees).")]
    public float straightAngleToleranceDeg = 8f;

    [Header("Debug")]
    public bool logDebug = true;

    [SerializeField] private float debugUserTurnSign = 0f;
    [SerializeField] private float debugUserTurnAngleDeg = 0f;
    [SerializeField] private string debugMovementCongruenceState = "NO_DATA";

    private bool active;
    private bool pendingWindowStart;
    private MaskingEventManager.FormalEventTiming currentEventTiming;
    private float actualInjectionStartTime = float.NaN;
    private float appliedAngle;
    private float signedTheta;
    private float cachedBaseYawRate;

    private struct MovementSample
    {
        public float time;
        public Vector2 position;

        public MovementSample(float time, Vector2 position)
        {
            this.time = time;
            this.position = position;
        }
    }

    private readonly List<MovementSample> movementSamples = new List<MovementSample>(64);

    // GainSearchFlowController uses these states to decide whether the staircase
    // evaluation was valid. A skipped injection must not update theta.
    public bool CurrentEvaluationInjectionStarted { get; private set; } = false;
    public bool CurrentEvaluationInjectionCompleted { get; private set; } = false;
    public string CurrentEvaluationInjectionOutcome { get; private set; } = "NONE";

    public event Action<float> OnInjectionStarted;
    public event Action<float> OnInjectionCompleted;
    public event Action<float> OnIncompleteInjectionCancelled;

    public float CachedBaseYawRate => cachedBaseYawRate;
    public float LastSignedThetaDeg => signedTheta;
    public float LastInjectionSign => Mathf.Abs(signedTheta) > 0.0001f ? Mathf.Sign(signedTheta) : 0f;
    public float DebugUserTurnSign => debugUserTurnSign;
    public float DebugUserTurnAngleDeg => debugUserTurnAngleDeg;
    public string DebugMovementCongruenceState => debugMovementCongruenceState;
    public float ActualInjectionStartTime => actualInjectionStartTime;
    public float WalkingSpeedAtInjectionMps { get; private set; }
    public float AppliedAngleDeg => appliedAngle;

    private void Awake()
    {
        if (maskingEventManager != null)
        {
            maskingEventManager.OnFormalEventStarted += HandleFormalEventStarted;
            maskingEventManager.OnFormalEventEnding += HandleFormalEventEnding;
        }
    }

    private void OnDestroy()
    {
        if (maskingEventManager != null)
        {
            maskingEventManager.OnFormalEventStarted -= HandleFormalEventStarted;
            maskingEventManager.OnFormalEventEnding -= HandleFormalEventEnding;
        }
    }

    private void Update()
    {
        SamplePhysicalMovement();

        if (
            (active || pendingWindowStart) &&
            walkingDetector != null &&
            walkingDetector.IsControllerLocomotionActive
        )
        {
            CancelActiveInjection("CANCELLED_ARTIFICIAL_LOCOMOTION", true);
        }
    }

    public void BeginEvaluationInjectionTracking()
    {
        CancelActiveInjection("NEW_EVALUATION", false);
        CurrentEvaluationInjectionStarted = false;
        CurrentEvaluationInjectionCompleted = false;
        CurrentEvaluationInjectionOutcome = "WAITING_FOR_EVENT";
        actualInjectionStartTime = float.NaN;
        WalkingSpeedAtInjectionMps = 0f;
    }

    /// <summary>
    /// Called every frame by WorldRotator. Only the current frame's base yaw rate
    /// is stored. No previous-direction fallback is used.
    /// </summary>
    public void SetBaseYawRateForThisFrame(float baseYawRate)
    {
        cachedBaseYawRate = baseYawRate;
    }

    public void SetCurrentEventThetaDeg(float thetaDeg)
    {
        currentEventThetaDeg = Mathf.Clamp(thetaDeg, 0f, thetaHardMax);

        if (logDebug)
            Debug.Log($"[Injection] currentEventThetaDeg set to {currentEventThetaDeg:F2}");
    }

    public float GetCurrentEventThetaDeg()
    {
        return Mathf.Clamp(currentEventThetaDeg, 0f, thetaHardMax);
    }

    /// <summary>
    /// Used before triggering an occlusion event. It prevents an event from starting
    /// when there is no current base steering or the user's movement is not congruent.
    /// The same checks are repeated at the actual injection point.
    /// </summary>
    public bool TryGetCurrentCandidateSignedThetaDeg(out float candidateSignedThetaDeg)
    {
        candidateSignedThetaDeg = 0f;

        if (!enableInjection)
            return false;

        if (walkingDetector != null && !walkingDetector.IsWalking)
            return false;

        if (!TryGetCurrentBaseSign(out float baseSign))
            return false;

        if (!PassUserMovementCongruenceGate(baseSign, out _))
            return false;

        float theta = Mathf.Clamp(currentEventThetaDeg, 0f, thetaHardMax);
        candidateSignedThetaDeg = theta * baseSign;
        return true;
    }

    public bool TryGetCurrentCandidateAbsThetaDeg(out float candidateAbsThetaDeg)
    {
        candidateAbsThetaDeg = 0f;

        if (!TryGetCurrentCandidateSignedThetaDeg(out float signedThetaDeg))
            return false;

        candidateAbsThetaDeg = Mathf.Abs(signedThetaDeg);
        return true;
    }

    private void HandleFormalEventStarted(MaskingEventManager.FormalEventTiming timing)
    {
        CancelActiveInjection("EVENT_RESTARTED", false);
        currentEventTiming = timing;
        pendingWindowStart = true;
        active = false;
        appliedAngle = 0f;
        signedTheta = 0f;
        actualInjectionStartTime = float.NaN;
        WalkingSpeedAtInjectionMps = 0f;
        CurrentEvaluationInjectionStarted = false;
        CurrentEvaluationInjectionCompleted = false;
        CurrentEvaluationInjectionOutcome = "WAITING_FOR_INJECTION_WINDOW";
    }

    private void HandleFormalEventEnding()
    {
        if (CurrentEvaluationInjectionCompleted)
            return;

        if (active)
        {
            CancelActiveInjection("INCOMPLETE_AT_EVENT_END", true);
            return;
        }

        if (pendingWindowStart)
        {
            pendingWindowStart = false;
            CurrentEvaluationInjectionOutcome = "SKIPPED_EVENT_ENDED_BEFORE_WINDOW";
            LogInjectionEvent("INJECTION_SKIPPED_EVENT_ENDED_BEFORE_WINDOW", 0f);
        }
    }

    public float ComputeEventYawRate(float dt)
    {
        float now = Time.time;

        if (pendingWindowStart && now >= currentEventTiming.InjectionWindowStartTime)
        {
            pendingWindowStart = false;

            if (now >= currentEventTiming.InjectionWindowEndTime)
            {
                CurrentEvaluationInjectionOutcome = "SKIPPED_MISSED_INJECTION_WINDOW";
                LogInjectionEvent("INJECTION_SKIPPED_MISSED_WINDOW", 0f);
            }
            else
            {
                TryStartInjection(now);
            }
        }

        if (!enableInjection || !active)
            return 0f;

        float windowEnd = currentEventTiming.InjectionWindowEndTime;
        if (now >= windowEnd)
        {
            // Do not apply a delayed remainder outside the authoritative window.
            // This can only occur after an execution interruption; the evaluation
            // is invalidated and retried instead of contaminating the timing.
            CancelActiveInjection("INCOMPLETE_AT_INJECTION_WINDOW_END", true);
            return 0f;
        }

        float interpolationDuration = Mathf.Max(
            windowEnd - actualInjectionStartTime,
            1e-4f
        );
        // Apply the integrated angle for the current frame interval. If this frame
        // crosses the window boundary, it receives the exact remaining angle now,
        // so no later frame can inject outside the window.
        float sampleTime = Mathf.Min(now + Mathf.Max(dt, 0f), windowEnd);
        float u = Mathf.Clamp01(
            (sampleTime - actualInjectionStartTime) / interpolationDuration
        );
        float targetAppliedAngle = signedTheta * SmoothStep01(u);

        float deltaAngle = targetAppliedAngle - appliedAngle;
        appliedAngle = targetAppliedAngle;

        if (Mathf.Abs(appliedAngle - signedTheta) <= 0.0001f)
            CompleteInjection(sampleTime);

        return deltaAngle / Mathf.Max(dt, 1e-4f);
    }

    private void TryStartInjection(float now)
    {
        if (!enableInjection)
        {
            CurrentEvaluationInjectionOutcome = "SKIPPED_DISABLED";
            LogInjectionEvent("INJECTION_SKIPPED_DISABLED", 0f);
            return;
        }

        if (walkingDetector != null && !walkingDetector.IsWalking)
        {
            CurrentEvaluationInjectionOutcome = "SKIPPED_NOT_WALKING";
            LogInjectionEvent("INJECTION_SKIPPED_NOT_WALKING", 0f);
            return;
        }

        if (!TryGetCurrentBaseSign(out float baseSign))
        {
            CurrentEvaluationInjectionOutcome = "SKIPPED_NO_CURRENT_BASE_DIRECTION";
            LogInjectionEvent("INJECTION_SKIPPED_NO_CURRENT_BASE_DIRECTION", 0f);
            return;
        }

        if (!PassUserMovementCongruenceGate(baseSign, out string congruenceReason))
        {
            bool opposite = congruenceReason == "OPPOSITE_USER_TURN";
            CurrentEvaluationInjectionOutcome = opposite
                ? "SKIPPED_OPPOSITE_USER_TURN"
                : "SKIPPED_NO_RELIABLE_USER_TURN";
            LogInjectionEvent(
                opposite
                    ? "INJECTION_SKIPPED_OPPOSITE_USER_TURN"
                    : "INJECTION_SKIPPED_NO_RELIABLE_USER_TURN",
                0f
            );
            return;
        }

        signedTheta = Mathf.Clamp(currentEventThetaDeg, 0f, thetaHardMax) * baseSign;
        active = true;
        actualInjectionStartTime = now;
        appliedAngle = 0f;
        WalkingSpeedAtInjectionMps = walkingDetector != null
            ? Mathf.Max(0f, walkingDetector.SmoothedSpeed)
            : 0f;

        CurrentEvaluationInjectionStarted = true;
        CurrentEvaluationInjectionOutcome = "STARTED";
        OnInjectionStarted?.Invoke(actualInjectionStartTime);
        LogInjectionEvent("INJECTION_START", signedTheta);

        if (Mathf.Abs(signedTheta) <= 0.0001f)
            CompleteInjection(now);
    }

    private void CompleteInjection(float completionTime)
    {
        if (!active && CurrentEvaluationInjectionCompleted)
            return;

        active = false;
        pendingWindowStart = false;
        appliedAngle = signedTheta;
        CurrentEvaluationInjectionCompleted = true;
        CurrentEvaluationInjectionOutcome = "COMPLETED";
        FormalExperimentContext.RecordAppliedTheta(appliedAngle);
        LogInjectionEvent("INJECTION_COMPLETE", signedTheta);
        OnInjectionCompleted?.Invoke(completionTime);
    }

    public void CancelActiveInjection(string reason = "CANCELLED", bool logCancellation = true)
    {
        bool hadActiveState = active || pendingWindowStart;
        float cancelledAppliedAngle = appliedAngle;
        active = false;
        pendingWindowStart = false;

        if (!CurrentEvaluationInjectionCompleted && hadActiveState)
        {
            CurrentEvaluationInjectionOutcome = string.IsNullOrEmpty(reason)
                ? "CANCELLED"
                : reason;
            FormalExperimentContext.RecordAppliedTheta(appliedAngle);

            if (logCancellation)
                LogInjectionEvent("INJECTION_CANCELLED", signedTheta);

            // Any partial event rotation belongs only to this invalid evaluation.
            // WorldRotator owns the inverse transform so it can remove that
            // contribution immediately without touching continuous base steering.
            if (Mathf.Abs(cancelledAppliedAngle) > 0.0001f)
                OnIncompleteInjectionCancelled?.Invoke(cancelledAppliedAngle);

            appliedAngle = 0f;
        }
    }

    private static float SmoothStep01(float x)
    {
        return x * x * (3f - 2f * x);
    }

    /// <summary>
    /// Returns the sign of the current frame's base yaw rate only.
    /// This intentionally does not remember or reuse a previous direction.
    /// </summary>
    private bool TryGetCurrentBaseSign(out float baseSign)
    {
        baseSign = 0f;

        if (Mathf.Abs(cachedBaseYawRate) < minBaseYawRateForInjection)
            return false;

        baseSign = Mathf.Sign(cachedBaseYawRate);
        return Mathf.Abs(baseSign) > 0.5f;
    }

    private void SamplePhysicalMovement()
    {
        if (playArea == null)
            return;

        float now = Time.time;
        Vector2 position = playArea.GetHmdXZ();

        if (
            movementSamples.Count == 0 ||
            (position - movementSamples[movementSamples.Count - 1].position).sqrMagnitude > 1e-8f
        )
        {
            movementSamples.Add(new MovementSample(now, position));
        }

        RemoveOldMovementSamples(now);
    }

    private void RemoveOldMovementSamples(float now)
    {
        float keepSeconds = Mathf.Max(
            Mathf.Max(movementTrendWindowSec * 2f, movementTrendWindowSec + 0.5f),
            1.0f
        );
        float cutoff = now - keepSeconds;

        int removeCount = 0;
        while (
            removeCount < movementSamples.Count - 1 &&
            movementSamples[removeCount].time < cutoff
        )
        {
            removeCount++;
        }

        if (removeCount > 0)
            movementSamples.RemoveRange(0, removeCount);
    }

    private bool PassUserMovementCongruenceGate(float baseSign, out string reason)
    {
        reason = "";

        if (!requireUserMovementCongruence)
        {
            debugMovementCongruenceState = "BYPASSED";
            return true;
        }

        if (Mathf.Abs(baseSign) < 0.5f)
        {
            reason = "NO_BASE_SIGN";
            debugMovementCongruenceState = reason;
            return false;
        }

        if (!TryEstimateUserTurnSign(
                out float userTurnSign,
                out float userTurnAngleDeg,
                out reason
            ))
        {
            debugUserTurnSign = 0f;
            debugUserTurnAngleDeg = userTurnAngleDeg;
            debugMovementCongruenceState = reason;
            return false;
        }

        debugUserTurnSign = userTurnSign;
        debugUserTurnAngleDeg = userTurnAngleDeg;

        // Straight walking: accepted.
        if (Mathf.Abs(userTurnSign) < 0.5f)
        {
            debugMovementCongruenceState = "CONGRUENT_STRAIGHT";
            return true;
        }

        // Same-direction physical turn: accepted.
        if (Mathf.Sign(userTurnSign) == Mathf.Sign(baseSign))
        {
            debugMovementCongruenceState = "CONGRUENT_SAME_TURN";
            return true;
        }

        // Opposite-direction physical turn: excluded.
        reason = "OPPOSITE_USER_TURN";
        debugMovementCongruenceState = reason;
        return false;
    }

    /// <summary>
    /// Estimates the user's recent physical turning trend from three points in the
    /// physical XZ trajectory: an early point, a midpoint, and the latest point.
    /// A signed angle close to zero is classified as straight walking.
    /// </summary>
    private bool TryEstimateUserTurnSign(
        out float userTurnSign,
        out float signedAngleDeg,
        out string reason
    )
    {
        userTurnSign = 0f;
        signedAngleDeg = 0f;
        reason = "";

        if (movementSamples.Count < 3)
        {
            reason = "NO_MOVEMENT_HISTORY";
            return false;
        }

        float now = Time.time;
        float startTimeWanted = now - Mathf.Max(0.1f, movementTrendWindowSec);

        int endIndex = movementSamples.Count - 1;
        int startIndex = 0;

        for (int i = movementSamples.Count - 1; i >= 0; i--)
        {
            if (movementSamples[i].time <= startTimeWanted)
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex >= endIndex - 1)
            startIndex = Mathf.Max(0, endIndex - 2);

        float midTimeWanted =
            (movementSamples[startIndex].time + movementSamples[endIndex].time) * 0.5f;

        int midIndex = startIndex + 1;
        float bestMidTimeError =
            Mathf.Abs(movementSamples[midIndex].time - midTimeWanted);

        for (int i = startIndex + 1; i < endIndex; i++)
        {
            float error = Mathf.Abs(movementSamples[i].time - midTimeWanted);
            if (error < bestMidTimeError)
            {
                bestMidTimeError = error;
                midIndex = i;
            }
        }

        Vector2 a = movementSamples[startIndex].position;
        Vector2 b = movementSamples[midIndex].position;
        Vector2 c = movementSamples[endIndex].position;

        Vector2 firstSegment = b - a;
        Vector2 secondSegment = c - b;

        if (
            firstSegment.magnitude < minMovementSegmentDistance ||
            secondSegment.magnitude < minMovementSegmentDistance
        )
        {
            reason = "INSUFFICIENT_MOVEMENT_FOR_TURN_ESTIMATE";
            return false;
        }

        Vector2 previousDirection = firstSegment.normalized;
        Vector2 currentDirection = secondSegment.normalized;

        float cross =
            previousDirection.x * currentDirection.y -
            previousDirection.y * currentDirection.x;
        float dot = Mathf.Clamp(
            Vector2.Dot(previousDirection, currentDirection),
            -1f,
            1f
        );

        signedAngleDeg = Mathf.Atan2(cross, dot) * Mathf.Rad2Deg;

        if (Mathf.Abs(signedAngleDeg) <= straightAngleToleranceDeg)
        {
            userTurnSign = 0f;
            return true;
        }

        userTurnSign = Mathf.Sign(signedAngleDeg);
        return true;
    }

    public void ResetDirectionCache()
    {
        CancelActiveInjection("RESET", false);
        cachedBaseYawRate = 0f;

        active = false;
        pendingWindowStart = false;
        currentEventTiming = default;
        appliedAngle = 0f;
        signedTheta = 0f;
        actualInjectionStartTime = float.NaN;
        WalkingSpeedAtInjectionMps = 0f;

        movementSamples.Clear();
        debugUserTurnSign = 0f;
        debugUserTurnAngleDeg = 0f;
        debugMovementCongruenceState = "RESET";

        CurrentEvaluationInjectionStarted = false;
        CurrentEvaluationInjectionCompleted = false;
        CurrentEvaluationInjectionOutcome = "RESET";

        if (logDebug)
            Debug.Log("[Injection] Current direction and movement history reset.");
    }

    private void LogInjectionEvent(string mark, float value)
    {
        if (eventLogger == null || maskingEventManager == null)
            return;

        float injectionSign = Mathf.Abs(value) > 0.0001f ? Mathf.Sign(value) : 0f;

        eventLogger.LogInjectionEvent(
            mark,
            maskingEventManager,
            value,
            cachedBaseYawRate,
            injectionSign,
            value,
            CurrentEvaluationInjectionOutcome,
            appliedAngle
        );
    }
}
