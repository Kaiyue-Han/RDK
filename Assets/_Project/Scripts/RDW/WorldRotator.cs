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

    private float smoothedBaseYawRate;

    private void Update()
    {
        if (virtualWorldRoot == null || hmd == null)
            return;

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        // 1) Continuous base redirection.
        float omegaBase = baseController != null
            ? baseController.ComputeBaseYawRate()
            : 0f;

        if (reduceBaseDuringOcclusion && occlusionActive)
            omegaBase *= baseScaleDuringOcclusion;

        // 2) Smooth only the continuous base redirection.
        smoothedBaseYawRate = SmoothExp(
            smoothedBaseYawRate,
            omegaBase,
            smoothingTau,
            dt
        );

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
        // No low total-yaw clamp is applied here, because it could reduce
        // the actual event angle below the staircase target.
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

    private static float SmoothExp(float current, float target, float tau, float dt)
    {
        if (tau <= 1e-4f)
            return target;

        float a = 1f - Mathf.Exp(-dt / tau);
        return Mathf.Lerp(current, target, a);
    }

    public void ResetRuntimeState()
    {
        smoothedBaseYawRate = 0f;
        occlusionActive = false;

        if (baseController != null)
            baseController.ResetRuntimeState();

        if (injectionController != null)
            injectionController.ResetDirectionCache();
    }
}
