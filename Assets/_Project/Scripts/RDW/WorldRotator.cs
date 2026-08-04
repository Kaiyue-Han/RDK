using UnityEngine;

public class WorldRotator : MonoBehaviour
{
    [Header("References")]
    public Transform virtualWorldRoot;
    public Transform hmd;
    public BaseSteeringController baseController;
    public RotationInjectionController injectionController;

    [Header("Mixing")]
    public float totalYawRateMax = 20f;
    public float smoothingTau = 0.2f;

    [Header("During occlusion (optional, recommended later)")]
    public bool reduceBaseDuringOcclusion = false;
    public float baseScaleDuringOcclusion = 0.2f;
    public bool occlusionActive = false;

    private float smoothedYawRate;

    private void Update()
    {
        if (virtualWorldRoot == null || hmd == null)
            return;

        float dt = Time.deltaTime;

        // 1) Continuous base redirection.
        float omegaBase = baseController != null
            ? baseController.ComputeBaseYawRate()
            : 0f;

        if (reduceBaseDuringOcclusion && occlusionActive)
            omegaBase *= baseScaleDuringOcclusion;

        // 2) The experimental injection must follow the current base direction.
        if (injectionController != null)
            injectionController.SetBaseYawRateForThisFrame(omegaBase);

        // 3) Event-based extra rotation.
        float omegaEvent = injectionController != null
            ? injectionController.ComputeEventYawRate(dt)
            : 0f;

        // 4) Combine, limit, and smooth.
        float omegaTotal = Mathf.Clamp(
            omegaBase + omegaEvent,
            -totalYawRateMax,
            totalYawRateMax
        );

        smoothedYawRate = SmoothExp(
            smoothedYawRate,
            omegaTotal,
            smoothingTau,
            dt
        );

        // 5) Rotate the virtual world around the participant.
        float deltaYaw = smoothedYawRate * dt;
        Vector3 pivot = hmd.position;
        pivot.y = 0f;
        virtualWorldRoot.RotateAround(pivot, Vector3.up, deltaYaw);
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
        smoothedYawRate = 0f;
        occlusionActive = false;

        if (baseController != null)
            baseController.ResetRuntimeState();

        if (injectionController != null)
            injectionController.ResetDirectionCache();
    }
}
