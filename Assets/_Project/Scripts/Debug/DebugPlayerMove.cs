using UnityEngine;

/// <summary>
/// Editor-only keyboard movement helper.
/// It intentionally does NOT read the legacy Horizontal/Vertical axes, because
/// those axes can also receive XR controller stick input when Active Input
/// Handling is set to Both. Formal Quest locomotion is handled by XRI's
/// Continuous Move Provider instead.
/// </summary>
public class DebugPlayerMove : MonoBehaviour
{
    public float speed = 2f;

    private void Update()
    {
#if UNITY_EDITOR
        float h = 0f;
        float v = 0f;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            h -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            h += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            v -= 1f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            v += 1f;

        Vector3 move = new Vector3(h, 0f, v);
        if (move.sqrMagnitude > 1f)
            move.Normalize();

        transform.Translate(move * speed * Time.deltaTime, Space.Self);
#endif
    }
}
