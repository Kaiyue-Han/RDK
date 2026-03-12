using UnityEngine;

public class Coin : MonoBehaviour
{
    public int coinIndex;   // 第几个金币
    private CoinSequenceManager manager;

    public void Init(CoinSequenceManager mgr, int index)
    {
        manager = mgr;
        coinIndex = index;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        manager.OnCoinCollected(coinIndex);
        gameObject.SetActive(false);
    }
}
