using UnityEngine;

public class CoinRotate : MonoBehaviour
{
    [SerializeField]
    private float rotateSpeed = 90f;

    void Update()
    {
        transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
    }
}
