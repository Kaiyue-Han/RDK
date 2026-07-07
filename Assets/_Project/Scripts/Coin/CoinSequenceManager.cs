using UnityEngine;

public class CoinSequenceManager : MonoBehaviour
{
    private const int VisibleCoinWindowSize = 5;

    public Coin[] coins;
    private int currentIndex = 0;

    void Start()
    {
        currentIndex = 0;

        for (int i = 0; i < coins.Length; i++)
        {
            coins[i].Init(this, i);
            coins[i].SetAsCurrentTarget(false);
            coins[i].gameObject.SetActive(false);
        }

        RefreshVisibleCoins();
    }

    public bool OnCoinCollected(int index)
    {
        if (index != currentIndex) return false;

        coins[currentIndex].SetAsCurrentTarget(false);
        currentIndex++;

        if (currentIndex < coins.Length)
        {
            RefreshVisibleCoins();
        }
        else
        {
            Debug.Log("🎉 Get All Coins!");
        }

        return true;
    }

    private void RefreshVisibleCoins()
    {
        int visibleEndExclusive = Mathf.Min(currentIndex + VisibleCoinWindowSize, coins.Length);

        for (int i = 0; i < coins.Length; i++)
        {
            bool shouldBeVisible = i >= currentIndex && i < visibleEndExclusive;
            coins[i].gameObject.SetActive(shouldBeVisible);
            coins[i].SetAsCurrentTarget(i == currentIndex);
        }
    }
}
