using System;
using System.IO;
using UnityEngine;

public class EventLogger : MonoBehaviour
{
    string csvPath;

    void Awake()
    {
        csvPath = Path.Combine(Application.persistentDataPath, "events.csv");
        EnsureHeader();
    }

    public void Mark(string mark, string conditionName, int ratio, float value = -1f)
    {
        string utc = DateTime.UtcNow.ToString("o");
        float t = Time.time;
        string line = $"{utc},{mark},{conditionName},{ratio},{value:F3},{t:F3},,,,,,\n";
        File.AppendAllText(csvPath, line);
    }

    public void LogTrialResult(string conditionName, int ratio, float confirmedTheta, bool success)
    {
        string utc = DateTime.UtcNow.ToString("o");
        float t = Time.time;
        string line = $"{utc},TRIAL_RESULT,{conditionName},{ratio},{confirmedTheta:F3},{t:F3},{success},,,,,\n";
        File.AppendAllText(csvPath, line);
    }

    public void LogEvaluation(
        string mark,
        string conditionName,
        int ratio,
        string phase,
        float testTheta,
        float safeTheta,
        bool noticed
    )
    {
        string utc = DateTime.UtcNow.ToString("o");
        float t = Time.time;
        string line = $"{utc},{mark},{conditionName},{ratio},,{t:F3},,{phase},{testTheta:F3},{safeTheta:F3},{noticed},\n";
        File.AppendAllText(csvPath, line);
    }

    public void LogSearchEvent(
        string mark,
        string conditionName,
        int ratio,
        string phase,
        float testTheta,
        float safeTheta,
        string extra = ""
    )
    {
        string utc = DateTime.UtcNow.ToString("o");
        float t = Time.time;
        string line = $"{utc},{mark},{conditionName},{ratio},,{t:F3},,{phase},{testTheta:F3},{safeTheta:F3},,{extra}\n";
        File.AppendAllText(csvPath, line);
    }

    void EnsureHeader()
    {
        if (!File.Exists(csvPath))
        {
            File.WriteAllText(
                csvPath,
                "utc,mark,condition,occlusionRatio,value,timeSec,success,phase,testTheta,safeTheta,noticed,extra\n"
            );
        }
    }
}
