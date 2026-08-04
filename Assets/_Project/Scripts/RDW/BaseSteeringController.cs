using System.Collections.Generic;
using UnityEngine;

public class BaseSteeringController : MonoBehaviour
{
    [Header("Dependencies")]
    public PlayAreaRectProvider playArea;

    [Header("External gates")]
    public WalkingDetector walkingDetector;

    [Header("Base RDW (continuous)")]
    public bool enableBase = true;
    public float boundaryBuffer = 1.0f;
    public float baseYawRateMax = 8f;

    [Header("Walking direction estimation")]
    [Tooltip("Prefer the participant's recent physical XZ displacement over HMD forward when computing the base steering direction.")]
    public bool preferPhysicalMovementDirection = true;

    [Tooltip("Recent time window used to estimate the participant's physical forward direction (seconds).")]
    public float movementDirectionWindowSec = 0.35f;

    [Tooltip("Minimum displacement within the direction window before the physical movement direction is considered reliable (meters).")]
    public float minMovementDirectionDistance = 0.06f;

    [Tooltip("Light smoothing applied to the physical movement direction. Set to 0 to disable smoothing (seconds).")]
    public float movementDirectionSmoothingTau = 0.15f;

    [Tooltip("When physical movement direction is not reliable, use HMD forward as a fallback.")]
    public bool fallbackToHmdForward = true;

    [Header("Stability window (recommended)")]
    [Tooltip("How long turnSign must remain unchanged before base redirection is output (seconds).")]
    public float turnSignStableSec = 0.5f;

    [Tooltip("Whether output is allowed when turnSign is zero (normally false).")]
    public bool allowWhenTurnSignZero = false;

    [Header("Debug")]
    public bool logDebug = false;

    [SerializeField] private string debugForwardSource = "NO_DATA";
    [SerializeField] private Vector2 debugRawMovementDirection = Vector2.zero;
    [SerializeField] private Vector2 debugUsedForwardDirection = Vector2.zero;
    [SerializeField] private float debugMovementWindowDistance = 0f;
    [SerializeField] private bool debugMovementDirectionReliable = false;

    private float lastTurnSign = 0f;
    private float turnSignStableTimer = 0f;

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

    private readonly List<MovementSample> movementSamples = new List<MovementSample>(128);
    private Vector2 smoothedMovementDirection = Vector2.zero;
    private bool hasSmoothedMovementDirection = false;
    private bool currentMovementDirectionReliable = false;
    private int lastSampleFrame = -1;

    public string DebugForwardSource => debugForwardSource;
    public Vector2 DebugUsedForwardDirection => debugUsedForwardDirection;
    public bool DebugMovementDirectionReliable => debugMovementDirectionReliable;

    private void Update()
    {
        SamplePhysicalMovementOncePerFrame();
    }

    public float ComputeBaseYawRate()
    {
        SamplePhysicalMovementOncePerFrame();

        if (!enableBase || playArea == null)
            return 0f;

        if (walkingDetector != null && !walkingDetector.IsWalking)
        {
            if (logDebug)
                Debug.Log("[Base] walkingDetector=false, output=0");

            ResetStability();
            return 0f;
        }

        float turnSign = ComputeTurnSignToCenter();

        if (!allowWhenTurnSignZero && Mathf.Abs(turnSign) < 0.5f)
        {
            ResetStability();
            return 0f;
        }

        bool stable = UpdateStability(turnSign, Time.deltaTime);
        if (!stable)
        {
            if (logDebug)
            {
                Debug.Log(
                    $"[Base] turnSign not stable yet " +
                    $"(timer={turnSignStableTimer:F2}/{turnSignStableSec:F2}), output=0"
                );
            }

            return 0f;
        }

        Vector2 p = playArea.GetHmdXZ();
        float d = playArea.DistanceToBoundary(p);

        float r = Mathf.Clamp01(
            (boundaryBuffer - d) / Mathf.Max(boundaryBuffer, 1e-4f)
        );
        r = SmoothStep01(r);

        float omega = baseYawRateMax * r * turnSign;

        if (logDebug)
        {
            Debug.Log(
                $"[Base] d={d:F2}, r={r:F2}, turnSign={turnSign:F0}, " +
                $"omega={omega:F2}, forwardSource={debugForwardSource}, " +
                $"forward={debugUsedForwardDirection}, " +
                $"movementWindowDistance={debugMovementWindowDistance:F3}"
            );
        }

        return omega;
    }

    /// <summary>
    /// Computes whether the play-area center is to the left or right of the
    /// participant's estimated forward walking direction.
    ///
    /// Preferred source: recent physical XZ displacement.
    /// Fallback source: HMD forward, only when movement direction is unreliable.
    /// </summary>
    public float ComputeTurnSignToCenter()
    {
        SamplePhysicalMovementOncePerFrame();

        if (playArea == null)
            return 0f;

        Vector2 p = playArea.GetHmdXZ();
        Vector2 toCenter = playArea.VectorToCenter(p);
        if (toCenter == Vector2.zero)
            return 0f;

        if (!TryGetForwardDirection(out Vector2 forward, out string source))
        {
            debugForwardSource = "NO_RELIABLE_FORWARD";
            debugUsedForwardDirection = Vector2.zero;
            return 0f;
        }

        debugForwardSource = source;
        debugUsedForwardDirection = forward;

        float cross = forward.x * toCenter.y - forward.y * toCenter.x;
        if (Mathf.Abs(cross) < 1e-4f)
            return 0f;

        return Mathf.Sign(cross);
    }

    private bool TryGetForwardDirection(out Vector2 forward, out string source)
    {
        forward = Vector2.zero;
        source = "NONE";

        if (
            preferPhysicalMovementDirection &&
            currentMovementDirectionReliable &&
            hasSmoothedMovementDirection &&
            smoothedMovementDirection.sqrMagnitude > 1e-6f
        )
        {
            forward = smoothedMovementDirection.normalized;
            source = "PHYSICAL_MOVEMENT";
            return true;
        }

        if (fallbackToHmdForward && playArea != null)
        {
            Vector2 hmdForward = playArea.GetHmdForwardXZ();
            if (hmdForward.sqrMagnitude > 1e-6f)
            {
                forward = hmdForward.normalized;
                source = preferPhysicalMovementDirection
                    ? "HMD_FALLBACK"
                    : "HMD_ONLY";
                return true;
            }
        }

        return false;
    }

    private void SamplePhysicalMovementOncePerFrame()
    {
        if (lastSampleFrame == Time.frameCount)
            return;

        lastSampleFrame = Time.frameCount;

        if (playArea == null)
        {
            currentMovementDirectionReliable = false;
            debugMovementDirectionReliable = false;
            return;
        }

        float now = Time.time;
        Vector2 position = playArea.GetHmdXZ();
        movementSamples.Add(new MovementSample(now, position));

        RemoveOldMovementSamples(now);
        UpdateMovementDirectionEstimate(now);
    }

    private void RemoveOldMovementSamples(float now)
    {
        float keepSeconds = Mathf.Max(movementDirectionWindowSec * 2f, 1.0f);
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

    private void UpdateMovementDirectionEstimate(float now)
    {
        currentMovementDirectionReliable = false;
        debugMovementDirectionReliable = false;
        debugRawMovementDirection = Vector2.zero;
        debugMovementWindowDistance = 0f;

        if (movementSamples.Count < 2)
            return;

        int endIndex = movementSamples.Count - 1;
        float targetStartTime = now - Mathf.Max(0.1f, movementDirectionWindowSec);
        int startIndex = 0;

        // Choose the sample nearest to the beginning of the requested time window.
        // If the complete window is not available yet, use the oldest retained sample.
        for (int i = endIndex - 1; i >= 0; i--)
        {
            if (movementSamples[i].time <= targetStartTime)
            {
                startIndex = i;
                break;
            }
        }

        float actualTimeSpan =
            movementSamples[endIndex].time - movementSamples[startIndex].time;

        // Avoid estimating a direction from only one or two very close frames.
        float minimumTimeSpan = Mathf.Min(
            Mathf.Max(movementDirectionWindowSec * 0.4f, 0.10f),
            Mathf.Max(movementDirectionWindowSec, 0.10f)
        );

        if (actualTimeSpan < minimumTimeSpan)
            return;

        Vector2 displacement =
            movementSamples[endIndex].position - movementSamples[startIndex].position;

        debugMovementWindowDistance = displacement.magnitude;

        if (displacement.magnitude < Mathf.Max(minMovementDirectionDistance, 0.001f))
            return;

        Vector2 rawDirection = displacement.normalized;
        debugRawMovementDirection = rawDirection;

        if (!hasSmoothedMovementDirection || movementDirectionSmoothingTau <= 1e-4f)
        {
            smoothedMovementDirection = rawDirection;
            hasSmoothedMovementDirection = true;
        }
        else
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float a = 1f - Mathf.Exp(-dt / movementDirectionSmoothingTau);
            Vector2 blended = Vector2.Lerp(
                smoothedMovementDirection,
                rawDirection,
                a
            );

            smoothedMovementDirection = blended.sqrMagnitude > 1e-6f
                ? blended.normalized
                : rawDirection;
        }

        currentMovementDirectionReliable = true;
        debugMovementDirectionReliable = true;
    }

    private bool UpdateStability(float turnSign, float dt)
    {
        float s = Mathf.Abs(turnSign) < 0.5f ? 0f : Mathf.Sign(turnSign);

        if (Mathf.Abs(s - lastTurnSign) > 0.1f)
        {
            lastTurnSign = s;
            turnSignStableTimer = 0f;
            return false;
        }

        turnSignStableTimer += dt;
        return turnSignStableTimer >= turnSignStableSec;
    }

    private void ResetStability()
    {
        lastTurnSign = 0f;
        turnSignStableTimer = 0f;
    }

    public void ResetRuntimeState()
    {
        ResetStability();

        movementSamples.Clear();
        smoothedMovementDirection = Vector2.zero;
        hasSmoothedMovementDirection = false;
        currentMovementDirectionReliable = false;
        lastSampleFrame = -1;

        debugForwardSource = "RESET";
        debugRawMovementDirection = Vector2.zero;
        debugUsedForwardDirection = Vector2.zero;
        debugMovementWindowDistance = 0f;
        debugMovementDirectionReliable = false;

        if (logDebug)
            Debug.Log("[Base] Walking-direction estimator reset.");
    }

    private static float SmoothStep01(float x)
    {
        return x * x * (3f - 2f * x);
    }
}
