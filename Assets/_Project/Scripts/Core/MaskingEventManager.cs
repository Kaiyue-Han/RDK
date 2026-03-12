using System;
using UnityEngine;

public class MaskingEventManager : MonoBehaviour
{
    [Header("Modules (drag components here)")]
    public RainCueController rain;
    public MonoBehaviour inputBehaviour;     // drag XRPrimaryButtonInput
    public MonoBehaviour occluderBehaviour;  // drag UIOccluder / UmbrellaOccluder
    public EventLogger logger;

    [Header("Timing")]
    public float intervalSec = 60f;
    public float occlusionSec = 0.9f;
    [Range(0f, 1f)] public float injectAt = 0.5f; // 0.5 = mid

    [Header("Condition")]
    public int occlusionRatio = 40; // 40/70

    [Header("Debug")]
    public bool allowManualTrigger = true;
    public KeyCode manualTriggerKey = KeyCode.T;

    public event Action OnInjectPoint; // 给RDW订阅

    private IButtonInput input;
    private IOccluder occluder;

    private enum State { Idle, WaitButton, Occlusion }
    private State state = State.Idle;

    private float nextEventTime;
    private float rainStartTime;
    private float occlusionStartTime;
    private bool injectFired;

    void Awake()
    {
        RebindModules();

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

    // =========================
    // ✅ Added for UI control
    // =========================

    /// <summary>
    /// Called by UI: set occlusion ratio (40/70) and sync to logger.
    /// </summary>
    public void SetOcclusionRatio(int ratio)
    {
        occlusionRatio = ratio;
        if (logger != null) logger.occlusionRatio = occlusionRatio;
    }

    /// <summary>
    /// Called by UI: swap occluder behaviour at runtime and rebind IOccluder.
    /// </summary>
    public void SetOccluderBehaviour(MonoBehaviour newOccluder)
    {
        AbortAndResetToIdle(); 
        occluderBehaviour = newOccluder;
        RebindOccluderOnly();
        occluder?.Hide(); // keep clean state
    }

    /// <summary>
    /// Called by UI: abort current event and reset state machine to Idle.
    /// </summary>
    public void AbortAndResetToIdle()
    {
        occluder?.Hide();
        rain?.StopRain();
        logger?.Mark("ABORT_RESET");

        state = State.Idle;
        nextEventTime = Time.time + intervalSec;

        Debug.Log("[MaskingEvent] AbortAndResetToIdle.");
    }

    /// <summary>
    /// Optional: if UI needs to swap input behaviour too.
    /// </summary>
    public void SetInputBehaviour(MonoBehaviour newInput)
    {
        inputBehaviour = newInput;
        RebindInputOnly();
    }

    // =========================
    // Binding helpers
    // =========================

    private void RebindModules()
    {
        RebindInputOnly();
        RebindOccluderOnly();
    }

    private void RebindInputOnly()
    {
        input = inputBehaviour as IButtonInput;
        if (input == null)
            Debug.LogError("[MaskingEvent] inputBehaviour must implement IButtonInput.");
    }

    private void RebindOccluderOnly()
    {
        occluder = occluderBehaviour as IOccluder;
        if (occluder == null)
            Debug.LogError("[MaskingEvent] occluderBehaviour must implement IOccluder.");
    }

    // =========================
    // Original logic
    // =========================

    void TriggerRainEvent()
    {
        rainStartTime = Time.time;

        // sync ratio to logger
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

        occluder?.Show(occlusionRatio, occlusionSec);

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
