using UnityEngine;

public class NewspaperTestTrigger : MonoBehaviour
{
    [SerializeField] private NewspaperOccluder newspaperOccluder;
    [SerializeField] private float durationSec = 0.8f;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.N))
        {
            newspaperOccluder.Show(40, durationSec);
        }

        if (Input.GetKeyDown(KeyCode.M))
        {
            newspaperOccluder.Show(70, durationSec);
        }
    }
}