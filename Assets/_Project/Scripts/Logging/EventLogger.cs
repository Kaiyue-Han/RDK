using System;
using System.Globalization;
using System.IO;
using UnityEngine;

public class EventLogger : MonoBehaviour
{
    private const string Header =
        "utc,mark,conditionKey,anchorMode,motionMode,occluderName,occlusionRatio,timeSec,success,testThetaDeg,noticed,validTrial,invalidReason,currentStepDeg,staircaseDeltaDeg,nextThetaDeg,isReversal,reversalIndex,reversalCount,estimatedThresholdDeg,usedReversals,allReversals,baseYawRateAtInjection,injectionSign,signedInjectedThetaDeg,injectionOutcome,resetReason,extra";

    private string csvPath;

    private struct LogRow
    {
        public string mark;
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

    // Compatibility overload. The old confirmed theta is written to estimatedThresholdDeg.
    // After the staircase refactor, STAIRCASE_RESULT should become the main result mark.
    public void LogTrialResult(string conditionKey, int ratio, float confirmedTheta, bool success)
    {
        WriteRow(new LogRow
        {
            mark = "STAIRCASE_RESULT",
            conditionKey = conditionKey,
            occlusionRatio = ratio.ToString(CultureInfo.InvariantCulture),
            success = BoolString(success),
            estimatedThresholdDeg = FormatFloat(confirmedTheta)
        });
    }

    public void LogTrialResult(MaskingEventManager maskingEventManager, float confirmedTheta, bool success)
    {
        LogRow row = CreateConditionRow("STAIRCASE_RESULT", maskingEventManager);
        row.success = BoolString(success);
        row.estimatedThresholdDeg = FormatFloat(confirmedTheta);
        WriteRow(row);
    }

    // Compatibility overload for the old coarse/fine/confirm search.
    // phase and safeTheta are intentionally not columns anymore.
    public void LogEvaluation(
        string mark,
        string conditionKey,
        int ratio,
        string phase,
        float testTheta,
        float safeTheta,
        bool noticed
    )
    {
        WriteRow(new LogRow
        {
            mark = mark,
            conditionKey = conditionKey,
            occlusionRatio = ratio.ToString(CultureInfo.InvariantCulture),
            testThetaDeg = FormatFloat(testTheta),
            noticed = BoolString(noticed),
            extra = BuildLegacySearchExtra(phase, safeTheta)
        });
    }

    public void LogEvaluation(
        string mark,
        MaskingEventManager maskingEventManager,
        string phase,
        float testTheta,
        float safeTheta,
        bool noticed,
        bool? validTrial = null,
        string invalidReason = "",
        string extra = ""
    )
    {
        LogRow row = CreateConditionRow(mark, maskingEventManager);
        row.testThetaDeg = FormatFloat(testTheta);
        row.noticed = BoolString(noticed);
        row.validTrial = validTrial.HasValue ? BoolString(validTrial.Value) : "";
        row.invalidReason = invalidReason;
        row.extra = MergeExtra(extra, BuildLegacySearchExtra(phase, safeTheta));
        WriteRow(row);
    }

    // Compatibility overload for the old coarse/fine/confirm search.
    // phase and safeTheta are kept only in extra until the staircase refactor removes old search events.
    public void LogSearchEvent(
        string mark,
        string conditionKey,
        int ratio,
        string phase,
        float testTheta,
        float safeTheta,
        string extra = ""
    )
    {
        WriteRow(new LogRow
        {
            mark = mark,
            conditionKey = conditionKey,
            occlusionRatio = ratio.ToString(CultureInfo.InvariantCulture),
            testThetaDeg = FormatFloat(testTheta),
            extra = MergeExtra(extra, BuildLegacySearchExtra(phase, safeTheta))
        });
    }

    public void LogSearchEvent(
        string mark,
        MaskingEventManager maskingEventManager,
        string phase,
        float testTheta,
        float safeTheta,
        string extra = ""
    )
    {
        LogRow row = CreateConditionRow(mark, maskingEventManager);
        row.testThetaDeg = FormatFloat(testTheta);
        row.extra = MergeExtra(extra, BuildLegacySearchExtra(phase, safeTheta));
        WriteRow(row);
    }

    public void LogInvalidEvaluation(
        string mark,
        MaskingEventManager maskingEventManager,
        string phase,
        float testTheta,
        float safeTheta,
        bool noticed,
        string invalidReason,
        string injectionOutcome = "",
        string extra = ""
    )
    {
        LogRow row = CreateConditionRow(mark, maskingEventManager);
        row.testThetaDeg = FormatFloat(testTheta);
        row.noticed = BoolString(noticed);
        row.validTrial = BoolString(false);
        row.invalidReason = invalidReason;
        row.injectionOutcome = injectionOutcome;
        row.extra = MergeExtra(extra, BuildLegacySearchExtra(phase, safeTheta));
        WriteRow(row);
    }

    public void LogInjectionEvent(
        string mark,
        MaskingEventManager maskingEventManager,
        float value,
        float baseYawRateAtInjection,
        float injectionSign,
        float signedInjectedThetaDeg,
        string injectionOutcome = ""
    )
    {
        LogRow row = CreateConditionRow(mark, maskingEventManager);
        row.baseYawRateAtInjection = FormatFloat(baseYawRateAtInjection);
        row.injectionSign = FormatFloat(injectionSign);
        row.signedInjectedThetaDeg = FormatFloat(signedInjectedThetaDeg);
        row.injectionOutcome = injectionOutcome;
        row.extra = value != signedInjectedThetaDeg ? $"legacyValue={FormatFloat(value)}" : "";
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

        string utc = DateTime.UtcNow.ToString("o");
        string timeSec = FormatFloat(Time.time);

        string[] values =
        {
            utc,
            row.mark,
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

    private static string BoolString(bool value)
    {
        return value ? "true" : "false";
    }

    private static string BuildLegacySearchExtra(string phase, float safeTheta)
    {
        string extra = "";

        if (!string.IsNullOrEmpty(phase))
            extra = MergeExtra(extra, $"legacyPhase={phase}");

        if (!float.IsNaN(safeTheta))
            extra = MergeExtra(extra, $"legacySafeThetaDeg={FormatFloat(safeTheta)}");

        return extra;
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
