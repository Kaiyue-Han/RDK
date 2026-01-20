using System;
using UnityEngine;

public class MaskingEventManager : MonoBehaviour
{
    [Header("Modules (drag components here)")]
    public RainCueController rain;
    public MonoBehaviour inputBehaviour;   // drag XRPrimaryButtonInput
    public MonoBehaviour occluderBehaviour; // drag UIOccluder
    public EventLogger logger;

    [Header("Timing")]
    public float intervalSec = 60f;
    public float occlusionSec = 0.9f;
    [Range(0f, 1f)] public float injectAt = 0.5f; // 0.5 = mid

    [Header("Condition")]
    public int occlusionRatio = 40; // 40/70 (manager decides; logger/occluder read this too)

    [Header("Debug")]
    public bool allowManualTrigger = true;
    public KeyCode manualTriggerKey = KeyCode.T;

    public event Action OnInjectPoint; // 给RDW订阅

    IButtonInput input;
    IOccluder occluder;

    enum State { Idle, WaitButton, Occlusion }
    State state = State.Idle;

    float nextEventTime;
    float rainStartTime;
    float occlusionStartTime;
    bool injectFired;

    void Awake()
    {
        input = inputBehaviour as IButtonInput;
        occluder = occluderBehaviour as IOccluder;

        if (input == null)
            Debug.LogError("[MaskingEvent] inputBehaviour must implement IButtonInput.");
        if (occluder == null)
            Debug.LogError("[MaskingEvent] occluderBehaviour must implement IOccluder.");
        if (logger == null)
            Debug.LogWarning("[MaskingEvent] logger not assigned (no csv output).");
    }

    void Start()
    {
        occluder?.Hide();
        nextEventTime = Time.time + intervalSec;
    }

    void Update()
    {
        if (allowManualTrigger && Input.GetKeyDown(manualTriggerKey) && state == State.Idle)
            TriggerRainEvent();

        switch (state)
        {
            case State.Idle:
                if (Time.time >= nextEventTime)
                    TriggerRainEvent();
                break;

            case State.WaitButton:
                if (input != null && input.PressedThisFrame())
                    StartOcclusion();
                break;

            case State.Occlusion:
                RunOcclusion();
                break;
        }
    }

    void TriggerRainEvent()
    {
        rainStartTime = Time.time;

        // 更新日志和遮挡器的 ratio（方便你未来在manager里随机40/70）
        if (logger != null) logger.occlusionRatio = occlusionRatio;

        rain?.StartRain();
        logger?.Mark("RAIN_START");

        state = State.WaitButton;
        Debug.Log("[MaskingEvent] Rain started. Waiting for button...");
    }

    void StartOcclusion()
    {
        occlusionStartTime = Time.time;
        injectFired = false;

        if (logger != null) logger.occlusionRatio = occlusionRatio;

        Debug.Log($"[MaskingEvent] StartOcclusion called. ratio={occlusionRatio}");
        Debug.Log($"[MaskingEvent] occluder null? {occluder == null}");

        occluder?.Show(occlusionRatio);

        float rt = occlusionStartTime - rainStartTime;
        logger?.Mark("OCCLUSION_START", rtSec: rt);

        state = State.Occlusion;
        Debug.Log("[MaskingEvent] Button pressed. Occlusion active...");
    }

    void RunOcclusion()
    {
        float elapsed = Time.time - occlusionStartTime;

        if (!injectFired && elapsed >= occlusionSec * injectAt)
        {
            injectFired = true;
            logger?.Mark("INJECT_POINT");
            OnInjectPoint?.Invoke();
        }

        if (elapsed >= occlusionSec)
            EndEvent();
    }

    void EndEvent()
    {
        occluder?.Hide();
        rain?.StopRain();
        logger?.Mark("EVENT_END");

        nextEventTime = Time.time + intervalSec;
        state = State.Idle;

        Debug.Log("[MaskingEvent] Event ended. Rain stopped.");
    }
}
