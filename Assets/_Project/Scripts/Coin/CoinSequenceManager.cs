using UnityEngine;

public class CoinSequenceManager : MonoBehaviour
{
    private const int VisibleCoinWindowSize = 5;

    public Coin[] coins;
    private int currentIndex = 0;

    void Start()
    {
        ResetSequence();
    }

    public void ResetSequence()
    {
        currentIndex = 0;

        if (coins == null || coins.Length == 0)
        {
            Debug.LogWarning("[CoinSequenceManager] ResetSequence called but no coins are assigned.", this);
            return;
        }

        for (int i = 0; i < coins.Length; i++)
        {
            if (coins[i] == null)
                continue;

            coins[i].Init(this, i);
            coins[i].SetAsCurrentTarget(false);
            coins[i].gameObject.SetActive(false);
        }

        RefreshVisibleCoins();

        Debug.Log($"[CoinSequenceManager] Sequence reset. VisibleWindow={VisibleCoinWindowSize}, TotalCoins={coins.Length}", this);
    }

    public bool OnCoinCollected(int index)
    {
        if (coins == null || index != currentIndex || currentIndex < 0 || currentIndex >= coins.Length)
            return false;

        if (coins[currentIndex] != null)
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
        if (coins == null)
            return;

        int visibleEndExclusive = Mathf.Min(currentIndex + VisibleCoinWindowSize, coins.Length);

        for (int i = 0; i < coins.Length; i++)
        {
            if (coins[i] == null)
                continue;

            bool shouldBeVisible = i >= currentIndex && i < visibleEndExclusive;
            coins[i].gameObject.SetActive(shouldBeVisible);
            coins[i].SetAsCurrentTarget(i == currentIndex);
        }
    }
}
