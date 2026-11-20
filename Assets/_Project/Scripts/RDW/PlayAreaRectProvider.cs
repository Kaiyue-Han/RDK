using UnityEngine;

public class PlayAreaRectProvider : MonoBehaviour
{
    [Header("References")]
    public Transform hmd; // Main Camera
    public PhysicalPositionTracker physicalTracker;

    [Header("Rect Play Area (meters)")]
    public float width = 7f;
    public float depth = 7f;
    public bool lockCenterAtStart = true;

    [Header("Debug")]
    public bool drawGizmos = true;

    public Vector2 CenterXZ { get; private set; }
    public float HalfW => width * 0.5f;
    public float HalfD => depth * 0.5f;

    private void Start()
    {
        if (lockCenterAtStart)
            ResetOrigin();
    }

    public void ResetOrigin()
    {
        CenterXZ = GetHmdXZ();
    }

    /// <summary>
    /// Returns the participant's accumulated physical position in tracking-space XZ.
    /// This comes from PhysicalPositionTracker, not from the virtual world position.
    /// </summary>
    public Vector2 GetHmdXZ()
    {
        if (physicalTracker == null)
            return Vector2.zero;

        return physicalTracker.PhysicalPositionXZ;
    }

    /// <summary>
    /// Returns HMD forward projected onto XZ in the same tracking-space coordinate
    /// frame used by PhysicalPositionTracker. This is used only as a fallback when
    /// recent physical displacement is insufficient for a reliable walking direction.
    /// </summary>
    public Vector2 GetHmdForwardXZ()
    {
        if (hmd == null)
            return Vector2.zero;

        Vector3 forward = hmd.forward;

        if (
            physicalTracker != null &&
            physicalTracker.cameraOffset != null
        )
        {
            forward = physicalTracker.cameraOffset.InverseTransformDirection(forward);
        }

        Vector2 projected = new Vector2(forward.x, forward.z);
        float magnitudeSquared = projected.sqrMagnitude;

        return magnitudeSquared < 1e-6f
            ? Vector2.zero
            : projected / Mathf.Sqrt(magnitudeSquared);
    }

    /// <summary>Distance to the nearest rectangle boundary. Positive inside, negative outside.</summary>
    public float DistanceToBoundary(Vector2 pXZ)
    {
        float dx = HalfW - Mathf.Abs(pXZ.x - CenterXZ.x);
        float dz = HalfD - Mathf.Abs(pXZ.y - CenterXZ.y);
        return Mathf.Min(dx, dz);
    }

    public Vector2 VectorToCenter(Vector2 pXZ)
    {
        Vector2 v = CenterXZ - pXZ;
        return v.sqrMagnitude < 1e-6f ? Vector2.zero : v.normalized;
    }

    public Rect AsRect()
    {
        return new Rect(
            CenterXZ.x - HalfW,
            CenterXZ.y - HalfD,
            width,
            depth
        );
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying)
            return;

        Rect r = AsRect();
        Vector3 a = new Vector3(r.xMin, 0f, r.yMin);
        Vector3 b = new Vector3(r.xMax, 0f, r.yMin);
        Vector3 c = new Vector3(r.xMax, 0f, r.yMax);
        Vector3 d = new Vector3(r.xMin, 0f, r.yMax);

        Gizmos.DrawLine(a, b);
        Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d);
        Gizmos.DrawLine(d, a);
    }
}
