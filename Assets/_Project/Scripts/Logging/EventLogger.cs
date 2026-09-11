using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public class EventLogger : MonoBehaviour
{
    public readonly struct EvaluationSnapshot
    {
        public readonly string ParticipantId;
        public readonly string SessionId;
        public readonly string RunId;
        public readonly string TrialId;
        public readonly string EvaluationId;
        public readonly string TrialType;
        public readonly bool IsCatch;
        public readonly string CatchType;
        public readonly float RequestedThetaDeg;
        public readonly float EventStartTime;
        public readonly float InjectionStartTime;
        public readonly float ResponseDeadlineTime;
        public readonly float ResponseTime;
        public readonly float ResponseRtSec;
        public readonly bool? ResponseAccepted;
        public readonly float WalkingSpeedAtTriggerMps;
        public readonly float WalkingSpeedAtInjectionMps;
        public readonly string UserTurnCongruency;
        public readonly float ActualAppliedThetaDeg;
        public readonly float AppliedThetaAtResponseDeg;
        public readonly string SceneName;
        public readonly string ConditionKey;
        public readonly string AnchorMode;
        public readonly string MotionMode;
        public readonly string OccluderName;
        public readonly string OcclusionRatio;

        public EvaluationSnapshot(MaskingEventManager maskingEventManager)
        {
            ParticipantId = FormalExperimentContext.ParticipantId;
            SessionId = FormalExperimentContext.SessionId;
            RunId = FormalExperimentContext.RunId;
            TrialId = FormalExperimentContext.TrialId;
            EvaluationId = FormalExperimentContext.EvaluationId;
            TrialType = FormalExperimentContext.TrialType;
            IsCatch = FormalExperimentContext.IsCatch;
            CatchType = FormalExperimentContext.CatchType;
            RequestedThetaDeg = FormalExperimentContext.RequestedThetaDeg;
            EventStartTime = FormalExperimentContext.EventStartTime;
            InjectionStartTime = FormalExperimentContext.InjectionStartTime;
            ResponseDeadlineTime = FormalExperimentContext.ResponseDeadlineTime;
            ResponseTime = FormalExperimentContext.ResponseTime;
            ResponseRtSec = FormalExperimentContext.ResponseRtSec;
            ResponseAccepted = FormalExperimentContext.ResponseAccepted;
            WalkingSpeedAtTriggerMps = FormalExperimentContext.WalkingSpeedAtTriggerMps;
            WalkingSpeedAtInjectionMps = FormalExperimentContext.WalkingSpeedAtInjectionMps;
            UserTurnCongruency = FormalExperimentContext.UserTurnCongruency;
            ActualAppliedThetaDeg = FormalExperimentContext.ActualAppliedThetaDeg;
            AppliedThetaAtResponseDeg = FormalExperimentContext.AppliedThetaAtResponseDeg;
            SceneName = SceneManager.GetActiveScene().name;
            ConditionKey = maskingEventManager != null ? maskingEventManager.CurrentConditionKey : "Unknown";
            AnchorMode = maskingEventManager != null ? maskingEventManager.CurrentAnchorModeName : "";
            MotionMode = maskingEventManager != null ? maskingEventManager.CurrentMotionModeName : "";
            OccluderName = maskingEventManager != null ? maskingEventManager.CurrentVisualName : "";
            OcclusionRatio = maskingEventManager != null
                ? maskingEventManager.CurrentOcclusionRatio.ToString(CultureInfo.InvariantCulture)
                : "";
        }
    }

    private const string Header =
        "utc,mark,participant_id,session_id,run_id,trial_id,evaluation_id,trial_type,is_catch,catch_type,requested_theta_deg,event_start_time_sec,injection_start_time_sec,response_deadline_time_sec,response_time_sec,response_rt_sec,response_accepted,walking_speed_trigger_mps,walking_speed_injection_mps,pre_event_mean_speed_mps,pre_event_min_speed_mps,during_event_mean_speed_mps,during_event_min_speed_mps,post_event_mean_speed_mps,post_event_min_speed_mps,user_turn_congruency,actual_applied_theta_deg,applied_theta_at_response_deg,sceneName,conditionKey,anchorMode,motionMode,occluderName,occlusionRatio,timeSec,success,testThetaDeg,noticed,validTrial,invalidReason,currentStepDeg,staircaseDeltaDeg,nextThetaDeg,isReversal,reversalIndex,reversalCount,estimatedThresholdDeg,usedReversals,allReversals,baseYawRateAtInjection,injectionSign,signedInjectedThetaDeg,injectionOutcome,resetReason,extra";

    private string csvPath;

    private struct LogRow
    {
        public string mark;
        public string actualAppliedThetaDeg;
        public string preEventMeanSpeedMps;
        public string preEventMinSpeedMps;
        public string duringEventMeanSpeedMps;
        public string duringEventMinSpeedMps;
        public string postEventMeanSpeedMps;
        public string postEventMinSpeedMps;
        public string sceneName;
        public string conditionKey;
        public string anchorMode;
        public string motionMode;
        public string occluderName;
        public string occlusionRatio;
        public string success;
        public string testThetaDeg;
        public string noticed;
        public string validTrial;
        public string invalidReason;
        public string currentStepDeg;
        public string staircaseDeltaDeg;
        public string nextThetaDeg;
        public string isReversal;
        public string reversalIndex;
        public string reversalCount;
        public string estimatedThresholdDeg;
        public string usedReversals;
        public string allReversals;
        public string baseYawRateAtInjection;
        public string injectionSign;
        public string signedInjectedThetaDeg;
        public string injectionOutcome;
        public string resetReason;
        public string extra;
        public EvaluationSnapshot? evaluationSnapshot;
    }

    private void Awake()
    {
        csvPath = Path.Combine(Application.persistentDataPath, "events.csv");
        EnsureHeader();
    }

    // Compatibility overload for old call sites that only pass a condition string.
    // The condition string is treated as conditionKey. New code should prefer the MaskingEventManager overload.
    public void Mark(string mark, string conditionKey, int ratio, float value = -1f)
    {
        WriteRow(new LogRow
        {
            mark = mark,
            conditionKey = conditionKey,
            occlusionRatio = ratio.ToString(CultureInfo.InvariantCulture),
            extra = value >= 0f ? $"legacyValue={FormatFloat(value)}" : ""
        });
    }

    public void Mark(string mark, MaskingEventManager maskingEventManager, float value = -1f, string extra = "")
    {
        LogRow row = CreateConditionRow(mark, maskingEventManager);
        row.extra = MergeExtra(extra, value >= 0f ? $"legacyValue={FormatFloat(value)}" : "");
        WriteRow(row);
    }

    public void LogInjectionEvent(
        string mark,
        MaskingEventManager maskingEventManager,
        float value,
        float baseYawRateAtInjection,
        float injectionSign,
        float signedInjectedThetaDeg,
        string injectionOutcome = "",
        float actualAppliedThetaDeg = float.NaN
    )
    {
        LogRow row = CreateConditionRow(mark, maskingEventManager);
        row.baseYawRateAtInjection = FormatFloat(baseYawRateAtInjection);
        row.injectionSign = FormatFloat(injectionSign);
        row.signedInjectedThetaDeg = FormatFloat(signedInjectedThetaDeg);
        row.injectionOutcome = injectionOutcome;
        row.actualAppliedThetaDeg = float.IsNaN(actualAppliedThetaDeg)
            ? ""
            : FormatFloat(actualAppliedThetaDeg);
        row.extra = value != signedInjectedThetaDeg ? $"legacyValue={FormatFloat(value)}" : "";
        WriteRow(row);
    }

    public void LogParticipantResponse(
        MaskingEventManager maskingEventManager,
        bool noticed,
        float responseTime,
        float responseTimeFromInjectionSec
    )
    {
        LogRow row = CreateConditionRow("RESPONSE_ACCEPTED", maskingEventManager);
        row.noticed = BoolString(noticed);
        row.extra =
            $"responseTime={FormatFloat(responseTime)};" +
            $"responseTimeFromInjectionSec={FormatFloat(responseTimeFromInjectionSec)}";
        WriteRow(row);
    }

    public void LogResetEvent(
        string mark,
        MaskingEventManager maskingEventManager,
        string resetReason,
        string extra = ""
    )
    {
        LogRow row = CreateConditionRow(mark, maskingEventManager);
        row.resetReason = resetReason;
        row.extra = extra;
        WriteRow(row);
    }

    public void LogWalkingSpeedSummary(
        MaskingEventManager maskingEventManager,
        EvaluationSnapshot evaluationSnapshot,
        float preMean,
        float preMin,
        float duringMean,
        float duringMin,
        float postMean,
        float postMin,
        float preWindowSec,
        float eventWindowSec,
        float postWindowSec
    )
    {
        LogRow row = CreateConditionRow("WALKING_SPEED_SUMMARY", maskingEventManager);
        row.evaluationSnapshot = evaluationSnapshot;
        row.preEventMeanSpeedMps = FormatFloat(preMean);
        row.preEventMinSpeedMps = FormatFloat(preMin);
        row.duringEventMeanSpeedMps = FormatFloat(duringMean);
        row.duringEventMinSpeedMps = FormatFloat(duringMin);
        row.postEventMeanSpeedMps = FormatFloat(postMean);
        row.postEventMinSpeedMps = FormatFloat(postMin);
        row.extra =
            $"preWindowSec={FormatFloat(preWindowSec)};" +
            $"eventWindowSec={FormatFloat(eventWindowSec)};" +
            $"postWindowSec={FormatFloat(postWindowSec)};" +
            "speedSource=WalkingDetector.SmoothedSpeed";
        WriteRow(row);
    }

    public void LogStaircaseEvaluation(
        string mark,
        MaskingEventManager maskingEventManager,
        float testThetaDeg,
        bool noticed,
        bool? validTrial,
        float currentStepDeg,
        float staircaseDeltaDeg,
        float nextThetaDeg,
        bool isReversal,
        int reversalIndex,
        int reversalCount,
        string invalidReason = "",
        string extra = ""
    )
    {
        LogRow row = CreateConditionRow(mark, maskingEventManager);
        row.testThetaDeg = FormatFloat(testThetaDeg);
        row.noticed = BoolString(noticed);
        row.validTrial = validTrial.HasValue ? BoolString(validTrial.Value) : "";
        row.invalidReason = invalidReason;
        row.currentStepDeg = FormatFloat(currentStepDeg);
        row.staircaseDeltaDeg = FormatFloat(staircaseDeltaDeg);
        row.nextThetaDeg = FormatFloat(nextThetaDeg);
        row.isReversal = BoolString(isReversal);
        row.reversalIndex = reversalIndex > 0 ? reversalIndex.ToString(CultureInfo.InvariantCulture) : "";
        row.reversalCount = reversalCount.ToString(CultureInfo.InvariantCulture);
        row.extra = extra;
        WriteRow(row);
    }


    public void LogCatchEvaluation(
        string mark,
        MaskingEventManager maskingEventManager,
        string trialType,
        float catchThetaDeg,
        bool? noticed,
        bool? validTrial,
        string catchOutcome,
        float heldStaircaseThetaDeg,
        string invalidReason = "",
        string extra = ""
    )
    {
        LogRow row = CreateConditionRow(mark, maskingEventManager);
        row.testThetaDeg = FormatFloat(catchThetaDeg);
        row.noticed = noticed.HasValue ? BoolString(noticed.Value) : "";
        row.validTrial = validTrial.HasValue ? BoolString(validTrial.Value) : "";
        row.invalidReason = invalidReason;

        string catchExtra =
            $"trialType={trialType};" +
            $"catchThetaDeg={FormatFloat(catchThetaDeg)};" +
            $"catchOutcome={catchOutcome};" +
            $"heldStaircaseThetaDeg={FormatFloat(heldStaircaseThetaDeg)}";

        row.extra = MergeExtra(catchExtra, extra);
        WriteRow(row);
    }

    public void LogStaircaseResult(
        string mark,
        MaskingEventManager maskingEventManager,
        float estimatedThresholdDeg,
        string usedReversals,
        string allReversals,
        int validTrialCount,
        int reversalCount,
        bool success,
        string stopReason,
        string extra = ""
    )
    {
        LogRow row = CreateConditionRow(mark, maskingEventManager);
        row.success = BoolString(success);
        row.estimatedThresholdDeg = FormatFloat(estimatedThresholdDeg);
        row.usedReversals = usedReversals;
        row.allReversals = allReversals;
        row.reversalCount = reversalCount.ToString(CultureInfo.InvariantCulture);
        row.extra = MergeExtra(extra, $"validTrialCount={validTrialCount};stopReason={stopReason}");
        WriteRow(row);
    }

    private LogRow CreateConditionRow(string mark, MaskingEventManager maskingEventManager)
    {
        LogRow row = new LogRow { mark = mark };

        if (maskingEventManager == null)
        {
            row.conditionKey = "Unknown";
            row.occlusionRatio = "";
            return row;
        }

        row.conditionKey = maskingEventManager.CurrentConditionKey;
        row.anchorMode = maskingEventManager.CurrentAnchorModeName;
        row.motionMode = maskingEventManager.CurrentMotionModeName;
        row.occluderName = maskingEventManager.CurrentVisualName;
        row.occlusionRatio = maskingEventManager.CurrentOcclusionRatio.ToString(CultureInfo.InvariantCulture);
        return row;
    }

    private void WriteRow(LogRow row)
    {
        EnsureHeader();

        EvaluationSnapshot? snapshot = row.evaluationSnapshot;

        if (snapshot.HasValue)
        {
            row.sceneName = snapshot.Value.SceneName;
            row.conditionKey = snapshot.Value.ConditionKey;
            row.anchorMode = snapshot.Value.AnchorMode;
            row.motionMode = snapshot.Value.MotionMode;
            row.occluderName = snapshot.Value.OccluderName;
            row.occlusionRatio = snapshot.Value.OcclusionRatio;
        }
        else if (string.IsNullOrEmpty(row.sceneName))
            row.sceneName = SceneManager.GetActiveScene().name;

        string utc = DateTime.UtcNow.ToString("o");
        string timeSec = FormatFloat(Time.time);

        string[] values =
        {
            utc,
            row.mark,
            snapshot.HasValue ? snapshot.Value.ParticipantId : FormalExperimentContext.ParticipantId,
            snapshot.HasValue ? snapshot.Value.SessionId : FormalExperimentContext.SessionId,
            snapshot.HasValue ? snapshot.Value.RunId : FormalExperimentContext.RunId,
            snapshot.HasValue ? snapshot.Value.TrialId : FormalExperimentContext.TrialId,
            snapshot.HasValue ? snapshot.Value.EvaluationId : FormalExperimentContext.EvaluationId,
            snapshot.HasValue ? snapshot.Value.TrialType : FormalExperimentContext.TrialType,
            BoolString(snapshot.HasValue ? snapshot.Value.IsCatch : FormalExperimentContext.IsCatch),
            snapshot.HasValue ? snapshot.Value.CatchType : FormalExperimentContext.CatchType,
            OptionalFloat(snapshot.HasValue ? snapshot.Value.RequestedThetaDeg : FormalExperimentContext.RequestedThetaDeg),
            OptionalFloat(snapshot.HasValue ? snapshot.Value.EventStartTime : FormalExperimentContext.EventStartTime),
            OptionalFloat(snapshot.HasValue ? snapshot.Value.InjectionStartTime : FormalExperimentContext.InjectionStartTime),
            OptionalFloat(snapshot.HasValue ? snapshot.Value.ResponseDeadlineTime : FormalExperimentContext.ResponseDeadlineTime),
            OptionalFloat(snapshot.HasValue ? snapshot.Value.ResponseTime : FormalExperimentContext.ResponseTime),
            OptionalFloat(snapshot.HasValue ? snapshot.Value.ResponseRtSec : FormalExperimentContext.ResponseRtSec),
            (snapshot.HasValue ? snapshot.Value.ResponseAccepted : FormalExperimentContext.ResponseAccepted).HasValue
                ? BoolString((snapshot.HasValue ? snapshot.Value.ResponseAccepted : FormalExperimentContext.ResponseAccepted).Value)
                : "",
            OptionalFloat(snapshot.HasValue ? snapshot.Value.WalkingSpeedAtTriggerMps : FormalExperimentContext.WalkingSpeedAtTriggerMps),
            OptionalFloat(snapshot.HasValue ? snapshot.Value.WalkingSpeedAtInjectionMps : FormalExperimentContext.WalkingSpeedAtInjectionMps),
            row.preEventMeanSpeedMps,
            row.preEventMinSpeedMps,
            row.duringEventMeanSpeedMps,
            row.duringEventMinSpeedMps,
            row.postEventMeanSpeedMps,
            row.postEventMinSpeedMps,
            snapshot.HasValue ? snapshot.Value.UserTurnCongruency : FormalExperimentContext.UserTurnCongruency,
            string.IsNullOrEmpty(row.actualAppliedThetaDeg)
                ? OptionalFloat(snapshot.HasValue ? snapshot.Value.ActualAppliedThetaDeg : FormalExperimentContext.ActualAppliedThetaDeg)
                : row.actualAppliedThetaDeg,
            OptionalFloat(snapshot.HasValue ? snapshot.Value.AppliedThetaAtResponseDeg : FormalExperimentContext.AppliedThetaAtResponseDeg),
            row.sceneName,
            row.conditionKey,
            row.anchorMode,
            row.motionMode,
            row.occluderName,
            row.occlusionRatio,
            timeSec,
            row.success,
            row.testThetaDeg,
            row.noticed,
            row.validTrial,
            row.invalidReason,
            row.currentStepDeg,
            row.staircaseDeltaDeg,
            row.nextThetaDeg,
            row.isReversal,
            row.reversalIndex,
            row.reversalCount,
            row.estimatedThresholdDeg,
            row.usedReversals,
            row.allReversals,
            row.baseYawRateAtInjection,
            row.injectionSign,
            row.signedInjectedThetaDeg,
            row.injectionOutcome,
            row.resetReason,
            row.extra
        };

        File.AppendAllText(csvPath, string.Join(",", EscapeValues(values)) + "\n");
    }

    private void EnsureHeader()
    {
        if (string.IsNullOrEmpty(csvPath))
            csvPath = Path.Combine(Application.persistentDataPath, "events.csv");

        string directory = Path.GetDirectoryName(csvPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (!File.Exists(csvPath))
        {
            File.WriteAllText(csvPath, Header + "\n");
            return;
        }

        string firstLine = "";
        using (StreamReader reader = new StreamReader(csvPath))
        {
            firstLine = reader.ReadLine() ?? "";
        }

        if (firstLine == Header)
            return;

        string backupPath = Path.Combine(
            Path.GetDirectoryName(csvPath),
            $"events_legacy_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv"
        );

        try
        {
            File.Move(csvPath, backupPath);
            File.WriteAllText(csvPath, Header + "\n");
            Debug.Log($"[EventLogger] Existing events.csv used an older header. Moved it to {backupPath} and started a new events.csv.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[EventLogger] Could not rotate old events.csv header: {ex.Message}. New rows may not match the current header.");
        }
    }

    private static string[] EscapeValues(string[] values)
    {
        string[] escaped = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
            escaped[i] = Escape(values[i]);
        return escaped;
    }

    private static string Escape(string value)
    {
        if (value == null)
            value = "";

        if (value.Contains(",") || value.Contains("\n") || value.Contains("\r") || value.Contains("\""))
        {
            value = value.Replace("\"", "\"\"");
            return $"\"{value}\"";
        }

        return value;
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static string OptionalFloat(float value)
    {
        return float.IsNaN(value) ? "" : FormatFloat(value);
    }

    private static string BoolString(bool value)
    {
        return value ? "true" : "false";
    }

    private static string MergeExtra(string a, string b)
    {
        if (string.IsNullOrEmpty(a))
            return b ?? "";
        if (string.IsNullOrEmpty(b))
            return a ?? "";
        return $"{a};{b}";
    }
}

/// <summary>
/// Process-persistent provenance and timing for the formal experiment. The launcher
/// configures participant/session scope, each staircase start creates a run, each
/// planned normal/catch item creates a trial, and every actual trigger attempt creates
/// a distinct evaluation. Invalid attempts retain the trial id but receive a new
/// evaluation id when retried.
/// </summary>
public static class FormalExperimentContext
{
    public static bool IsSessionConfigured { get; private set; }
    public static string ParticipantId { get; private set; } = "";
    public static string SessionId { get; private set; } = "";
    public static string RunId { get; private set; } = "";
    public static string TrialId { get; private set; } = "";
    public static string EvaluationId { get; private set; } = "";
    public static string TrialType { get; private set; } = "";
    public static bool IsCatch { get; private set; }
    public static string CatchType { get; private set; } = "";
    public static float RequestedThetaDeg { get; private set; } = float.NaN;
    public static float EventStartTime { get; private set; } = float.NaN;
    public static float InjectionStartTime { get; private set; } = float.NaN;
    public static float ResponseDeadlineTime { get; private set; } = float.NaN;
    public static float ResponseTime { get; private set; } = float.NaN;
    public static float ResponseRtSec { get; private set; } = float.NaN;
    public static bool? ResponseAccepted { get; private set; }
    public static float WalkingSpeedAtTriggerMps { get; private set; } = float.NaN;
    public static float WalkingSpeedAtInjectionMps { get; private set; } = float.NaN;
    public static string UserTurnCongruency { get; private set; } = "";
    public static float ActualAppliedThetaDeg { get; private set; } = float.NaN;
    public static float AppliedThetaAtResponseDeg { get; private set; } = float.NaN;

    private static int runCounter;
    private static int trialCounter;
    private static int evaluationCounter;
    private static bool trialOpen;

    public static bool TryConfigureSession(string participantId, string sessionId, out string error)
    {
        string cleanParticipant = SanitizeIdentifier(participantId);
        if (string.IsNullOrEmpty(cleanParticipant))
        {
            error = "participant_id is required and may contain letters, digits, '-' or '_'.";
            return false;
        }

        string cleanSession = SanitizeIdentifier(sessionId);
        if (string.IsNullOrEmpty(cleanSession))
            cleanSession = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        bool changed = !IsSessionConfigured ||
                       ParticipantId != cleanParticipant ||
                       SessionId != cleanSession;

        ParticipantId = cleanParticipant;
        SessionId = cleanSession;
        IsSessionConfigured = true;
        error = "";

        if (changed)
        {
            runCounter = 0;
            ResetRunScope();
        }

        return true;
    }

    public static bool BeginRun()
    {
        if (!IsSessionConfigured)
            return false;

        runCounter++;
        RunId = $"R{runCounter:000}";
        trialCounter = 0;
        evaluationCounter = 0;
        trialOpen = false;
        ClearEvaluationData(true);
        return true;
    }

    public static bool BeginEvaluation(string trialType, float requestedThetaDeg, float triggerSpeedMps)
    {
        if (string.IsNullOrEmpty(RunId))
            return false;

        if (!trialOpen)
        {
            trialCounter++;
            TrialId = $"{RunId}-T{trialCounter:000}";
            trialOpen = true;
        }

        evaluationCounter++;
        EvaluationId = $"{RunId}-E{evaluationCounter:000}";
        TrialType = trialType ?? "";
        IsCatch = TrialType == "CATCH_ZERO" || TrialType == "CATCH_HIGH";
        CatchType = TrialType == "CATCH_ZERO"
            ? "Zero"
            : TrialType == "CATCH_HIGH" ? "High" : "";
        RequestedThetaDeg = requestedThetaDeg;
        WalkingSpeedAtTriggerMps = Mathf.Max(0f, triggerSpeedMps);
        ClearTimingAndResponseData();
        return true;
    }

    public static void RecordEventStart(float time)
    {
        EventStartTime = time;
    }

    public static void RecordInjectionStart(
        float time,
        float responseDeadline,
        float walkingSpeedMps,
        string congruency
    )
    {
        InjectionStartTime = time;
        ResponseDeadlineTime = responseDeadline;
        WalkingSpeedAtInjectionMps = Mathf.Max(0f, walkingSpeedMps);
        UserTurnCongruency = congruency ?? "";
    }

    public static void RecordResponse(float time, bool accepted)
    {
        ResponseTime = time;
        ResponseAccepted = accepted;
        ResponseRtSec = float.IsNaN(InjectionStartTime)
            ? float.NaN
            : Mathf.Max(0f, time - InjectionStartTime);
    }

    public static void RecordNoResponse()
    {
        ResponseAccepted = false;
        ResponseTime = float.NaN;
        ResponseRtSec = float.NaN;
    }

    public static void RecordAppliedTheta(float appliedThetaDeg)
    {
        ActualAppliedThetaDeg = appliedThetaDeg;
    }

    public static void RecordAppliedThetaAtResponse(float appliedThetaDeg)
    {
        AppliedThetaAtResponseDeg = appliedThetaDeg;
    }

    public static void CompleteTrial()
    {
        trialOpen = false;
    }

    public static void EndRun()
    {
        ResetRunScope();
    }

    private static void ResetRunScope()
    {
        RunId = "";
        trialCounter = 0;
        evaluationCounter = 0;
        trialOpen = false;
        ClearEvaluationData(true);
    }

    private static void ClearEvaluationData(bool clearTrialId)
    {
        if (clearTrialId)
            TrialId = "";

        EvaluationId = "";
        TrialType = "";
        IsCatch = false;
        CatchType = "";
        RequestedThetaDeg = float.NaN;
        WalkingSpeedAtTriggerMps = float.NaN;
        ClearTimingAndResponseData();
    }

    private static void ClearTimingAndResponseData()
    {
        EventStartTime = float.NaN;
        InjectionStartTime = float.NaN;
        ResponseDeadlineTime = float.NaN;
        ResponseTime = float.NaN;
        ResponseRtSec = float.NaN;
        ResponseAccepted = null;
        WalkingSpeedAtInjectionMps = float.NaN;
        UserTurnCongruency = "";
        ActualAppliedThetaDeg = float.NaN;
        AppliedThetaAtResponseDeg = float.NaN;
    }

    private static string SanitizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        char[] source = value.Trim().ToCharArray();
        char[] target = new char[source.Length];
        int count = 0;

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                target[count++] = c;
        }

        return count == 0 ? "" : new string(target, 0, count);
    }
}
