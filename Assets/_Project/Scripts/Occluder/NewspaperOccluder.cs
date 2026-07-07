using System.Collections;
using UnityEngine;

public class NewspaperOccluder : MonoBehaviour, IOccluder
{
    [Header("Assign newspaper visuals")]
    [SerializeField] private GameObject newspaper40;
    [SerializeField] private GameObject newspaper70;

    [Header("Enter Animation")]
    [SerializeField] private float enterDuration = 0.15f;
    [SerializeField] private Vector3 enterOffset = new Vector3(0f, 0.15f, 0.25f);

    private Coroutine playCo;
    private Vector3 restingLocalPos;

    public bool IsPlaying { get; private set; }

    private void Awake()
    {
        restingLocalPos = transform.localPosition;

        if (newspaper40) newspaper40.SetActive(false);
        if (newspaper70) newspaper70.SetActive(false);

        IsPlaying = false;
    }

    public void Show(int ratio, float durationSec)
    {
        if (playCo != null) StopCoroutine(playCo);
        playCo = StartCoroutine(PlayOcclusion(ratio, durationSec));
    }

    private IEnumerator PlayOcclusion(int ratio, float durationSec)
    {
        IsPlaying = true;

        bool use70 = (ratio == 70);

        if (newspaper40) newspaper40.SetActive(!use70);
        if (newspaper70) newspaper70.SetActive(use70);

        Vector3 finalPos = restingLocalPos;
        Vector3 startPos = restingLocalPos + enterOffset;

        transform.localPosition = startPos;

        float t = 0f;
        while (t < enterDuration)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Clamp01(t / enterDuration);
            float smooth = alpha * alpha * (3f - 2f * alpha);

            transform.localPosition = Vector3.Lerp(startPos, finalPos, smooth);
            yield return null;
        }

        transform.localPosition = finalPos;

        if (durationSec > 0f)
        {
            yield return new WaitForSeconds(durationSec);
        }

        Hide();
    }

    public void Hide()
    {
        if (playCo != null)
        {
            StopCoroutine(playCo);
            playCo = null;
        }

        if (newspaper40) newspaper40.SetActive(false);
        if (newspaper70) newspaper70.SetActive(false);

        transform.localPosition = restingLocalPos;
        IsPlaying = false;
    }
}