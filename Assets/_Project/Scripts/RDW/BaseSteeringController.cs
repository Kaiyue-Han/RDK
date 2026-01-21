using UnityEngine;

public class BaseSteeringController : MonoBehaviour
{
    [Header("Dependencies")]
    public PlayAreaRectProvider playArea;

    [Header("Base RDK (continuous)")]
    public bool enableBase = true;
    public float boundaryBuffer = 1.0f;   // d0：进入缓冲区才明显增大
    public float baseYawRateMax = 8f;     // deg/s（先保守）

    [Header("Locomotion gate (recommended)")]
    [Tooltip("低于该地面速度(m/s)则认为用户未在行走，不输出base旋转。0.1~0.2常用。")]
    public float minWalkingSpeed = 0.12f;

    [Tooltip("速度估计平滑时间常数(s)。越大越稳但响应更慢。")]
    public float speedSmoothingTau = 0.25f;

    [Header("Stability window (recommended)")]
    [Tooltip("turnSign 连续稳定多久(s)才允许输出。0.3~0.8常用。")]
    public float turnSignStableSec = 0.5f;

    [Tooltip("turnSign==0(已朝向中心)时，是否仍允许输出（通常设为false）。")]
    public bool allowWhenTurnSignZero = false;

    [Header("Debug")]
    public bool logDebug = false;

    // --- internal state for speed estimate ---
    Vector2 lastPosXZ;
    bool hasLastPos;
    float speedSmoothed;

    // --- internal state for stable turnSign ---
    float lastTurnSign = 0f;
    float turnSignStableTimer = 0f;

    /// <summary>
    /// 计算当前帧基础 RDK yaw rate（deg/s）。
    /// 由 WorldRotator.Update() 每帧调用。
    /// </summary>
    public float ComputeBaseYawRate()
    {
        if (!enableBase || playArea == null) return 0f;

        // 1) 速度门槛：用户不走就不转（RDW更自然，也利于实验）
        float speed = UpdateAndGetSpeedSmoothed(Time.deltaTime);
        if (speed < minWalkingSpeed)
        {
            if (logDebug) Debug.Log($"[Base] speed={speed:F2} < minWalkingSpeed, output=0");
            // 用户停下时，稳定计时也重置（可选：你也可不重置）
            ResetStability();
            return 0f;
        }

        // 2) 计算 turnSign（指向中心）
        float turnSign = ComputeTurnSignToCenter();

        if (!allowWhenTurnSignZero && Mathf.Abs(turnSign) < 0.5f)
        {
            // 已经基本面向中心：不需要转
            ResetStability();
            return 0f;
        }

        // 3) 稳定窗口：turnSign 在一段时间内保持不变才允许输出
        bool stable = UpdateStability(turnSign, Time.deltaTime);
        if (!stable)
        {
            if (logDebug) Debug.Log($"[Base] turnSign not stable yet (timer={turnSignStableTimer:F2}/{turnSignStableSec:F2}), output=0");
            return 0f;
        }

        // 4) 距离边界决定幅度：越靠边越大
        Vector2 p = playArea.GetHmdXZ();
        float d = playArea.DistanceToBoundary(p);

        // r: 0（远离边界）-> 1（贴边/出界）
        float r = Mathf.Clamp01((boundaryBuffer - d) / Mathf.Max(boundaryBuffer, 1e-4f));
        r = SmoothStep01(r);

        float omega = baseYawRateMax * r * turnSign;

        if (logDebug)
            Debug.Log($"[Base] speed={speed:F2}, d={d:F2}, r={r:F2}, turnSign={turnSign}, omega={omega:F2}");

        return omega;
    }

    /// <summary>返回应向哪边转才能让朝向更接近中心（+1/-1/0）</summary>
    public float ComputeTurnSignToCenter()
    {
        if (playArea == null) return 0f;

        Vector2 p = playArea.GetHmdXZ();
        Vector2 v = playArea.VectorToCenter(p);
        if (v == Vector2.zero) return 0f;

        Vector2 f = playArea.GetHmdForwardXZ();
        if (f == Vector2.zero) return 0f;

        // cross2D(f, v) = f.x*v.y - f.y*v.x
        float cross = f.x * v.y - f.y * v.x;
        if (Mathf.Abs(cross) < 1e-4f) return 0f;

        return Mathf.Sign(cross);
    }

    // -----------------------
    // Speed estimation
    // -----------------------
    float UpdateAndGetSpeedSmoothed(float dt)
    {
        Vector2 p = playArea.GetHmdXZ();

        float instSpeed = 0f;
        if (hasLastPos)
        {
            instSpeed = (p - lastPosXZ).magnitude / Mathf.Max(dt, 1e-4f);
        }
        lastPosXZ = p;
        hasLastPos = true;

        // 指数平滑速度
        speedSmoothed = SmoothExp(speedSmoothed, instSpeed, speedSmoothingTau, dt);
        return speedSmoothed;
    }

    // -----------------------
    // Stability window
    // -----------------------
    bool UpdateStability(float turnSign, float dt)
    {
        // 统一成 -1/0/+1
        float s = (Mathf.Abs(turnSign) < 0.5f) ? 0f : Mathf.Sign(turnSign);

        if (Mathf.Abs(s - lastTurnSign) > 0.1f)
        {
            // 符号变化：重置计时
            lastTurnSign = s;
            turnSignStableTimer = 0f;
            return false;
        }

        // 符号一致：累计稳定时间
        turnSignStableTimer += dt;
        return turnSignStableTimer >= turnSignStableSec;
    }

    void ResetStability()
    {
        lastTurnSign = 0f;
        turnSignStableTimer = 0f;
    }

    // -----------------------
    // Math helpers
    // -----------------------
    static float SmoothStep01(float x) => x * x * (3f - 2f * x);

    static float SmoothExp(float current, float target, float tau, float dt)
    {
        if (tau <= 1e-4f) return target;
        float a = 1f - Mathf.Exp(-dt / tau);
        return Mathf.Lerp(current, target, a);
    }
}
