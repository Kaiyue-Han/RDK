using UnityEngine;

public class RainSnapToHmd : MonoBehaviour
{
    public Transform hmd;
    public float height = 20f;

    private Quaternion _baseRot;

    void Awake()
    {
        _baseRot = transform.rotation;
    }

    public void SnapToHmd()
    {
        if (!hmd) return;
        Vector3 p = hmd.position;
        transform.position = new Vector3(p.x, height, p.z);
        transform.rotation = _baseRot;
    }
}
