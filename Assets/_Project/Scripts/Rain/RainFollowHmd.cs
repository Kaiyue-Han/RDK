using UnityEngine;

public class RainSnapToHmd : MonoBehaviour
{
    public Transform hmd;          // Main Camera
    public Transform worldRoot;    // WorldRoot（建议在 Inspector 里拖上）

    private float _fixedLocalY;
    private Quaternion _baseLocalRot;

    void Awake()
    {
        // Cache initial local height and local rotation
        _fixedLocalY = transform.localPosition.y;
        _baseLocalRot = transform.localRotation;
    }

    public void SnapToHmd()
    {
        if (!hmd) return;

        Transform wr = worldRoot ? worldRoot : transform.parent;
        if (!wr) return;

        // Convert HMD world position into WorldRoot local space
        Vector3 hmdLocal = wr.InverseTransformPoint(hmd.position);

        // Follow XZ, keep initial local height
        transform.localPosition = new Vector3(
            hmdLocal.x,
            _fixedLocalY,
            hmdLocal.z
        );

        // Keep initial local rotation (do not cancel RDW world rotation)
        transform.localRotation = _baseLocalRot;
    }
}
