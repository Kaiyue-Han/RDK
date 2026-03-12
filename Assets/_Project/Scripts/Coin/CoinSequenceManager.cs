using UnityEngine;

public class CoinSequenceManager : MonoBehaviour
{
    public Coin[] coins;
    private int currentIndex = 0;

    void Start()
    {
        for (int i = 0; i < coins.Length; i++)
        {
            coins[i].Init(this, i);
            coins[i].gameObject.SetActive(i == 0); // 只开第一个
        }
    }

    public void OnCoinCollected(int index)
    {
        if (index != currentIndex) return;

        currentIndex++;

        if (currentIndex < coins.Length)
        {
            coins[currentIndex].gameObject.SetActive(true);
        }
        else
        {
            Debug.Log("🎉Get All Coins!");
        }
    }
}

