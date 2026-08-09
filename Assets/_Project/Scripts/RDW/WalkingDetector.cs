using UnityEngine;
using UnityEngine.InputSystem;

public class WalkingDetector : MonoBehaviour
{
    [Header("Reference")]
    [Tooltip("Usually Main Camera / HMD")]
    public Transform hmd;

    [Header("Controller locomotion exclusion")]
    [Tooltip("Same action used by Continuous Move Provider")]
    public InputActionReference continuousLocomotionInput;

    [Tooltip("Same action used by teleport locomotion")]
    public InputActionReference teleportLocomotionInput;

    [Tooltip("Deadzone to avoid stick drift")]
    public float moveDeadzone = 0.2f;

    [Header("Walking thresholds")]
    [Tooltip("Minimum smoothed horizontal speed (m/s)")]
    public float minWalkingSpeed = 0.15f;

    [Tooltip("Minimum accumulated horizontal displacement in the recent walking window (m)")]
    public float minWalkingDistance = 0.20f;

    [Tooltip("Speed smoothing time constant (s)")]
    public float speedSmoothingTau = 0.25f;

    [Header("Optional hysteresis")]
    [Tooltip("How long to keep the walking state after conditions fail slightly (s)")]
    public float walkingHoldTime = 0.10f;

    [Header("Debug")]
    public bool logDebug = false;

    public bool IsWalking { get; private set; }
    public float SmoothedSpeed => speedSmoothed;
    public float AccumulatedDistance => accumulatedDistance;

    private Vector2 lastPosXZ;
    private bool hasLastPos;

    private float speedSmoothed;
    private float accumulatedDistance;
    private float holdTimer;

    private bool wasControllerLocomotionLastFrame;

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
        if (hmd == null)
        {
            ResetState();
            wasControllerLocomotionLastFrame = false;
            return;
        }

        Vector3 p3 = hmd.position;
        Vector2 posXZ = new Vector2(p3.x, p3.z);

        bool controllerLocomotionActive =
            IsContinuousLocomotionActive() || IsTeleportLocomotionActive();

        // 1) 手柄移动 / teleport 期间：清零，不累计
        if (controllerLocomotionActive)
        {
            ResetState();
            lastPosXZ = posXZ;
            hasLastPos = true;
            wasControllerLocomotionLastFrame = true;

            if (logDebug)
                Debug.Log("[WalkingDetector] Controller locomotion active -> reset and ignore frame");

            return;
        }

        // 2) 手柄刚结束后的第一帧：重新设基准，不累计
        if (wasControllerLocomotionLastFrame)
        {
            ResetState();
            lastPosXZ = posXZ;
            hasLastPos = true;
            wasControllerLocomotionLastFrame = false;

            if (logDebug)
                Debug.Log("[WalkingDetector] Controller locomotion just ended -> rebuild baseline");

            return;
        }

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        if (!hasLastPos)
        {
            lastPosXZ = posXZ;
            hasLastPos = true;
            IsWalking = false;
            return;
        }

        float frameDistance = (posXZ - lastPosXZ).magnitude;
        float instSpeed = frameDistance / dt;
        lastPosXZ = posXZ;

        speedSmoothed = SmoothExp(speedSmoothed, instSpeed, speedSmoothingTau, dt);

        // 衰减式累计位移：表示“最近一段时间内的有效位移”
        accumulatedDistance = Mathf.Max(0f, accumulatedDistance - dt * minWalkingSpeed);
        accumulatedDistance += frameDistance;

        bool passSpeed = speedSmoothed >= minWalkingSpeed;
        bool passDistance = accumulatedDistance >= minWalkingDistance;
        bool walkingConditionsMet = passSpeed && passDistance;

        if (walkingConditionsMet)
        {
            // 速度和近期累计位移都满足条件，立即确认正在真实行走。
            IsWalking = true;

            // 条件持续满足时，不断刷新停止宽限时间。
            holdTimer = Mathf.Max(0f, walkingHoldTime);
        }
        else if (IsWalking)
        {
            // 已经确认正在行走，但条件短暂失效时，先使用宽限时间保持状态。
            holdTimer = Mathf.Max(0f, holdTimer - dt);

            if (holdTimer <= 0f)
                IsWalking = false;
        }
        else
        {
            holdTimer = 0f;
        }

        if (logDebug)
        {
            Debug.Log(
                $"[WalkingDetector] speed={speedSmoothed:F3}, dist={accumulatedDistance:F3}, " +
                $"passSpeed={passSpeed}, passDistance={passDistance}, " +
                $"hold={holdTimer:F2}, walking={IsWalking}");
        }
    }

    public void ResetState()
    {
        hasLastPos = false;
        speedSmoothed = 0f;
        accumulatedDistance = 0f;
        holdTimer = 0f;
        IsWalking = false;
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

    private static float SmoothExp(float current, float target, float tau, float dt)
    {
        if (tau <= 1e-4f)
            return target;

        float a = 1f - Mathf.Exp(-dt / tau);
        return Mathf.Lerp(current, target, a);
    }
}
