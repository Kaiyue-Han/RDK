using UnityEngine;

public class WorldRotator : MonoBehaviour
{
    [Header("References")]
    public Transform virtualWorldRoot;
    public Transform hmd;
    public BaseSteeringController baseController;
    public RotationInjectionController injectionController;

    [Header("Base yaw smoothing")]
    [Tooltip("Exponential smoothing applied only to the continuous base redirection. Event injection already uses its own SmoothStep curve.")]
    public float smoothingTau = 0.2f;

    [Header("During occlusion (optional)")]
    public bool reduceBaseDuringOcclusion = false;
    public float baseScaleDuringOcclusion = 0.2f;
    public bool occlusionActive = false;

    [Header("Runtime Base Freeze")]
    [Tooltip("Runtime state. During a prepared experimental event, the already-smoothed base yaw rate is held constant.")]
    [SerializeField] private bool baseFrozen = false;

    [Tooltip("Runtime value of the frozen, already-smoothed base yaw rate (deg/s).")]
    [SerializeField] private float frozenBaseYawRate = 0f;

    private float smoothedBaseYawRate;

    public float CurrentSmoothedBaseYawRate => smoothedBaseYawRate;
    public bool IsBaseFrozen => baseFrozen;
    public float FrozenBaseYawRate => frozenBaseYawRate;

    private void Update()
    {
        if (virtualWorldRoot == null || hmd == null)
            return;

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        // 1) Continuous base redirection target.
        float omegaBase = baseController != null
            ? baseController.ComputeBaseYawRate()
            : 0f;

        if (reduceBaseDuringOcclusion && occlusionActive)
            omegaBase *= baseScaleDuringOcclusion;

        // 2) Normally smooth the continuous base redirection.
        // During an experimental pre-event/event freeze, keep exactly the
        // already-smoothed value that the participant was experiencing.
        if (baseFrozen)
        {
            smoothedBaseYawRate = frozenBaseYawRate;
        }
        else
        {
            smoothedBaseYawRate = SmoothExp(
                smoothedBaseYawRate,
                omegaBase,
                smoothingTau,
                dt
            );
        }

        // 3) Experimental injection follows the base direction
        // that is actually being applied this frame.
        if (injectionController != null)
            injectionController.SetBaseYawRateForThisFrame(smoothedBaseYawRate);

        // 4) Event injection already contains its own SmoothStep easing.
        // Do not smooth it a second time.
        float omegaEvent = injectionController != null
            ? injectionController.ComputeEventYawRate(dt)
            : 0f;

        // 5) Add the two components directly.
        float omegaTotal = smoothedBaseYawRate + omegaEvent;

        // 6) Rotate the virtual world around the participant.
        float deltaYaw = omegaTotal * dt;
        Vector3 pivot = hmd.position;
        pivot.y = 0f;

        virtualWorldRoot.RotateAround(
            pivot,
            Vector3.up,
            deltaYaw
        );
    }

    /// <summary>
    /// Freeze the base at the exact already-smoothed yaw rate currently being applied.
    /// This is used before an experimental event so the participant experiences the
    /// same background redirection before and during the event.
    /// </summary>
    public void FreezeBase()
    {
        if (baseFrozen)
            return;

        frozenBaseYawRate = smoothedBaseYawRate;
        baseFrozen = true;
    }

    /// <summary>
    /// Release the frozen base. Smoothing resumes from the frozen value toward the
    /// current base-controller target, avoiding a sudden jump after the event.
    /// </summary>
    public void UnfreezeBase()
    {
        if (!baseFrozen)
            return;

        baseFrozen = false;
        frozenBaseYawRate = 0f;
    }

    private static float SmoothExp(float current, float target, float tau, float dt)
    {
        if (tau <= 1e-4f)
            return target;

        float a = 1f - Mathf.Exp(-dt / tau);
        return Mathf.Lerp(current, target, a);
    }

    public void ResetRuntimeState()
    {
        baseFrozen = false;
        frozenBaseYawRate = 0f;
        smoothedBaseYawRate = 0f;
        occlusionActive = false;

        if (baseController != null)
            baseController.ResetRuntimeState();

        if (injectionController != null)
            injectionController.ResetDirectionCache();
    }
}
