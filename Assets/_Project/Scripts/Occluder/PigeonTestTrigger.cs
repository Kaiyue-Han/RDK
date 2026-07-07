using UnityEngine;

public class PigeonTestTrigger : MonoBehaviour
{
    [SerializeField] private PigeonOccluder pigeonOccluder;
    [SerializeField] private float testDuration = 0.4f;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.J))
        {
            pigeonOccluder.Show(40, testDuration);
        }

        if (Input.GetKeyDown(KeyCode.K))
        {
            pigeonOccluder.Show(70, testDuration);
        }

        if (Input.GetKeyDown(KeyCode.L))
        {
            pigeonOccluder.Hide();
        }
    }
}