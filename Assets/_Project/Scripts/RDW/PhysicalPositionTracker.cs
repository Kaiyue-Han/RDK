using UnityEngine;

public class PhysicalPositionTracker : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Usually Main Camera / HMD")]
    public Transform hmd;

    [Tooltip("The tracking-space parent of HMD, usually Camera Offset")]
    public Transform cameraOffset;

    [Header("Debug")]
    public bool logDebug = false;

    public Vector2 PhysicalPositionXZ { get; private set; }

    [SerializeField] private Vector2 debugLocalHmdXZ;
    [SerializeField] private Vector2 debugPhysicalPositionXZ;

    private Vector2 lastLocalHmdXZ;
    private bool hasLast;

    private void Start()
    {
        ResetTracker();
    }

    private void Update()
    {
        if (hmd == null || cameraOffset == null) return;

        // HMD relative to Camera Offset
        Vector3 local = cameraOffset.InverseTransformPoint(hmd.position);
        Vector2 currentLocalHmdXZ = new Vector2(local.x, local.z);

        debugLocalHmdXZ = currentLocalHmdXZ;

        if (!hasLast)
        {
            lastLocalHmdXZ = currentLocalHmdXZ;
            hasLast = true;
            debugPhysicalPositionXZ = PhysicalPositionXZ;
            return;
        }

        Vector2 delta = currentLocalHmdXZ - lastLocalHmdXZ;
        lastLocalHmdXZ = currentLocalHmdXZ;

        PhysicalPositionXZ += delta;
        debugPhysicalPositionXZ = PhysicalPositionXZ;

        if (logDebug)
            Debug.Log($"[PhysicalPositionTracker] localDelta={delta}, physical={PhysicalPositionXZ}");
    }

    public void ResetTracker()
    {
        PhysicalPositionXZ = Vector2.zero;
        hasLast = false;
        debugLocalHmdXZ = Vector2.zero;
        debugPhysicalPositionXZ = Vector2.zero;
    }
}