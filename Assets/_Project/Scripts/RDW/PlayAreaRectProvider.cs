using UnityEngine;

public class PlayAreaRectProvider : MonoBehaviour
{
    [Header("References")]
    public Transform hmd; // Main Camera

    [Header("Rect Play Area (meters)")]
    public float width = 3f;
    public float depth = 3f;
    public bool lockCenterAtStart = true;

    [Header("Debug")]
    public bool drawGizmos = true;

    public Vector2 CenterXZ { get; private set; }
    public float HalfW => width * 0.5f;
    public float HalfD => depth * 0.5f;

    void Start()
    {
        if (lockCenterAtStart)
        {
            CenterXZ = GetHmdXZ();
        }
    }

    public Vector2 GetHmdXZ()
    {
        if (hmd == null) return Vector2.zero;
        Vector3 p = hmd.position;
        return new Vector2(p.x, p.z);
    }

    public Vector2 GetHmdForwardXZ()
    {
        if (hmd == null) return Vector2.zero;
        Vector3 f3 = hmd.forward;
        Vector2 f = new Vector2(f3.x, f3.z);
        float m2 = f.sqrMagnitude;
        return m2 < 1e-6f ? Vector2.zero : f / Mathf.Sqrt(m2);
    }

    /// <summary>到最近边界的距离（在矩形内为正；出界为负）</summary>
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
        return new Rect(CenterXZ.x - HalfW, CenterXZ.y - HalfD, width, depth);
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        if (!Application.isPlaying) return;

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
