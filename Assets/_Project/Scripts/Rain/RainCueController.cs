using UnityEngine;

public class RainCueController : MonoBehaviour
{
    [Header("References")]
    public ParticleSystem rainFx;
    public RainSnapToHmd rainSnap;

    public void StartRain()
    {
        // 1) 先把上一次的粒子状态清空，避免“雨还停在旧位置”
        if (rainFx != null)
        {
            rainFx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            rainFx.Clear(true);
            // 可选：把模拟时间归零（更稳）
            rainFx.Simulate(0f, true, true, true);
        }

        // 2) 再把雨的发射器 snap 到玩家附近（这次的位置）
        if (rainSnap != null)
        {
            rainSnap.SnapToHmd();
        }

        // 3) 最后重新开始播放
        if (rainFx != null)
        {
            rainFx.Play(true);
        }
    }

    public void StopRain()
    {
        // 停止发射（不清空，让雨自然消失）
        if (rainFx != null)
        {
            rainFx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}