using UnityEngine;

public class Coin : MonoBehaviour
{
    public int coinIndex;
    private CoinSequenceManager manager;
    private bool collected = false;

    private CoinPulseVisual pulseVisual;

    private void Awake()
    {
        pulseVisual = GetComponent<CoinPulseVisual>();
    }

    public void Init(CoinSequenceManager mgr, int index)
    {
        PrepareForCollection(mgr, index);
    }

    public void PrepareForCollection(CoinSequenceManager mgr, int index)
    {
        manager = mgr;
        coinIndex = index;
        collected = false;
    }

    public void SetAsCurrentTarget(bool value)
    {
        if (pulseVisual != null)
        {
            pulseVisual.SetActivePulse(value);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (collected) return;
        if (!other.CompareTag("Player")) return;

        if (manager.OnCoinCollected(coinIndex))
        {
            collected = true;
            gameObject.SetActive(false);
        }
    }
}
