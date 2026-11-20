using UnityEngine;

public class UIAnchorFollowHmd : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform hmd;

    [Header("Layout")]
    [SerializeField] private float distance = 1.2f;
    [SerializeField] private float heightOffset = -0.08f;

    private void LateUpdate()
    {
        if (!hmd) return;

        // 只保留水平朝向（yaw）
        Vector3 flatForward = hmd.forward;
        flatForward.y = 0f;

        if (flatForward.sqrMagnitude < 0.0001f)
            return;

        flatForward.Normalize();

        // 目标位置：跟随 HMD 的水平位置，但高度固定为“当前头高 + offset”
        Vector3 targetPos = hmd.position;
        targetPos += flatForward * distance;
        targetPos.y = hmd.position.y + heightOffset;

        transform.position = targetPos;
        transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up);
    }
}