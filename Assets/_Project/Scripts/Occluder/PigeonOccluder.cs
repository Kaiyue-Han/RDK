using System.Collections;
using UnityEngine;

public class PigeonOccluder : MonoBehaviour, IOccluder
{
    [Header("Assign pigeon visuals")]
    [SerializeField] private GameObject pigeon40;
    [SerializeField] private GameObject pigeon70;

    [Header("Flight Path on THIS root")]
    [SerializeField] private Vector3 startLocalPos = new Vector3(0.50f, 0.10f, 0.35f);
    [SerializeField] private Vector3 endLocalPos   = new Vector3(-0.50f, 0.02f, 0.18f);

    [Header("Root rotation during flight")]
    [SerializeField] private Vector3 flightLocalEulerAngles = new Vector3(0f, -25f, 0f);

    private Coroutine playCo;

    public bool IsPlaying { get; private set; }

    private void Awake()
    {
        if (pigeon40) pigeon40.SetActive(false);
        if (pigeon70) pigeon70.SetActive(false);

        IsPlaying = false;
    }

    public void Show(int ratio, float durationSec)
    {
        if (playCo != null)
        {
            StopCoroutine(playCo);
            playCo = null;
        }

        playCo = StartCoroutine(PlayOcclusion(ratio, durationSec));
    }

    private IEnumerator PlayOcclusion(int ratio, float durationSec)
    {
        IsPlaying = true;

        bool use70 = (ratio == 70);

        GameObject activeObj   = use70 ? pigeon70 : pigeon40;
        GameObject inactiveObj = use70 ? pigeon40 : pigeon70;

        if (inactiveObj) inactiveObj.SetActive(false);
        if (activeObj) activeObj.SetActive(true);

        transform.localPosition = startLocalPos;
        transform.localEulerAngles = flightLocalEulerAngles;

        float dur = Mathf.Max(0.01f, durationSec);
        float t = 0f;

        while (t < dur)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Clamp01(t / dur);
            float smooth = alpha * alpha * (3f - 2f * alpha);

            transform.localPosition = Vector3.Lerp(startLocalPos, endLocalPos, smooth);
            yield return null;
        }

        transform.localPosition = endLocalPos;

        Hide();
    }

    public void Hide()
    {
        if (playCo != null)
        {
            StopCoroutine(playCo);
            playCo = null;
        }

        if (pigeon40) pigeon40.SetActive(false);
        if (pigeon70) pigeon70.SetActive(false);

        IsPlaying = false;
    }
}