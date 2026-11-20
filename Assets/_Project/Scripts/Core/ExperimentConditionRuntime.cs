using UnityEngine;

public class ExperimentConditionRuntime : MonoBehaviour
{
    public enum RuntimeOccluderType
    {
        Newspaper,
        Pigeon
    }

    public enum RuntimePlayAreaSize
    {
        Size3x3,
        Size6x6
    }

    [Header("Current Locked Trial Condition")]
    [SerializeField] private bool isLocked = false;
    [SerializeField] private bool isRunning = false;

    [SerializeField] private RuntimePlayAreaSize currentPlayArea = RuntimePlayAreaSize.Size6x6;
    [SerializeField] private RuntimeOccluderType currentOccluderType = RuntimeOccluderType.Newspaper;
    [SerializeField] private int currentOcclusionRatio = 40;

    public bool IsLocked => isLocked;
    public bool IsRunning => isRunning;
    public RuntimePlayAreaSize CurrentPlayArea => currentPlayArea;
    public RuntimeOccluderType CurrentOccluderType => currentOccluderType;
    public int CurrentOcclusionRatio => currentOcclusionRatio;

    public void LockCondition(
        RuntimePlayAreaSize playArea,
        RuntimeOccluderType occluderType,
        int occlusionRatio)
    {
        currentPlayArea = playArea;
        currentOccluderType = occluderType;
        currentOcclusionRatio = occlusionRatio;

        isLocked = true;
        isRunning = true;

        Debug.Log($"[ExperimentConditionRuntime] Locked: Area={currentPlayArea}, Type={currentOccluderType}, Ratio={currentOcclusionRatio}");
    }

    public void StopTrial()
    {
        isRunning = false;
        Debug.Log("[ExperimentConditionRuntime] Trial stopped.");
    }

    public void UnlockAndClear()
    {
        isLocked = false;
        isRunning = false;

        currentPlayArea = RuntimePlayAreaSize.Size6x6;
        currentOccluderType = RuntimeOccluderType.Newspaper;
        currentOcclusionRatio = 40;

        Debug.Log("[ExperimentConditionRuntime] Trial unlocked and cleared.");
    }
}