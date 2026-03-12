using UnityEngine;

public class BaseSteeringController : MonoBehaviour
{
    [Header("Dependencies")]
    public PlayAreaRectProvider playArea;

    [Header("External gates")]
    public WalkingDetector walkingDetector;

    [Header("Base RDK (continuous)")]
    public bool enableBase = true;
    public float boundaryBuffer = 1.0f;
    public float baseYawRateMax = 8f;

    [Header("Stability window (recommended)")]
    [Tooltip("turnSign 连续稳定多久(s)才允许输出。0.3~0.8常用。")]
    public float turnSignStableSec = 0.5f;

    [Tooltip("turnSign==0(已朝向中心)时，是否仍允许输出（通常设为false）。")]
    public bool allowWhenTurnSignZero = false;

    [Header("Debug")]
    public bool logDebug = false;

    float lastTurnSign = 0f;
    float turnSignStableTimer = 0f;

    public float ComputeBaseYawRate()
    {
        if (!enableBase || playArea == null) return 0f;

        if (walkingDetector != null && !walkingDetector.IsWalking)
        {
            if (logDebug) Debug.Log("[Base] walkingDetector=false, output=0");
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
            if (logDebug) Debug.Log($"[Base] turnSign not stable yet (timer={turnSignStableTimer:F2}/{turnSignStableSec:F2}), output=0");
            return 0f;
        }

        Vector2 p = playArea.GetHmdXZ();
        float d = playArea.DistanceToBoundary(p);

        float r = Mathf.Clamp01((boundaryBuffer - d) / Mathf.Max(boundaryBuffer, 1e-4f));
        r = SmoothStep01(r);

        float omega = baseYawRateMax * r * turnSign;

        if (logDebug)
            Debug.Log($"[Base] d={d:F2}, r={r:F2}, turnSign={turnSign}, omega={omega:F2}");

        return omega;
    }

    public float ComputeTurnSignToCenter()
    {
        if (playArea == null) return 0f;

        Vector2 p = playArea.GetHmdXZ();
        Vector2 v = playArea.VectorToCenter(p);
        if (v == Vector2.zero) return 0f;

        Vector2 f = playArea.GetHmdForwardXZ();
        if (f == Vector2.zero) return 0f;

        float cross = f.x * v.y - f.y * v.x;
        if (Mathf.Abs(cross) < 1e-4f) return 0f;

        return Mathf.Sign(cross);
    }

    bool UpdateStability(float turnSign, float dt)
    {
        float s = (Mathf.Abs(turnSign) < 0.5f) ? 0f : Mathf.Sign(turnSign);

        if (Mathf.Abs(s - lastTurnSign) > 0.1f)
        {
            lastTurnSign = s;
            turnSignStableTimer = 0f;
            return false;
        }

        turnSignStableTimer += dt;
        return turnSignStableTimer >= turnSignStableSec;
    }

    void ResetStability()
    {
        lastTurnSign = 0f;
        turnSignStableTimer = 0f;
    }

    static float SmoothStep01(float x) => x * x * (3f - 2f * x);
}