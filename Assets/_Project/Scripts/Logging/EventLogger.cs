using System;
using System.IO;
using UnityEngine;

public class EventLogger : MonoBehaviour
{
    [Header("Optional tags")]
    public string condition = "UI"; // "UI" / "Umbrella"
    public int occlusionRatio = 40; // 40 / 70

    string csvPath;

    void Awake()
    {
        csvPath = Path.Combine(Application.persistentDataPath, "events.csv");
        EnsureHeader();
    }

    public void Mark(string mark, float rtSec = -1f)
    {
        string utc = DateTime.UtcNow.ToString("o");
        float t = Time.time;
        string line = $"{utc},{mark},{condition},{occlusionRatio},{rtSec:F3},{t:F3}\n";
        File.AppendAllText(csvPath, line);
    }

    void EnsureHeader()
    {
        if (!File.Exists(csvPath))
        {
            File.WriteAllText(csvPath, "utc,mark,condition,occlusionRatio,rtSec,timeSec\n");
        }
    }
}

