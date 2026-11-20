using UnityEngine;

public class CoinSequenceManager : MonoBehaviour
{
    private const int VisibleCoinWindowSize = 5;

    public Coin[] coins;
    [Min(1)]
    [SerializeField] private int reverseCoinsRequiredToResume = 1;
    private int currentIndex = 0;
    private int direction = 1;
    private int reverseCoinsCollected;

    public bool IsTurnaroundInProgress { get; private set; }
    public bool BlocksFormalEvents { get; private set; }
    public int CurrentIndex => currentIndex;
    public int Direction => direction;

    void Start()
    {
        ResetSequence();
    }

    public void ResetSequence()
    {
        currentIndex = 0;
        direction = 1;
        reverseCoinsCollected = 0;
        IsTurnaroundInProgress = false;
        BlocksFormalEvents = false;

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

        PrepareCurrentTarget();
        RefreshVisibleCoins();

        Debug.Log($"[CoinSequenceManager] Sequence reset. VisibleWindow={VisibleCoinWindowSize}, TotalCoins={coins.Length}", this);
    }

    public bool OnCoinCollected(int index)
    {
        if (coins == null || index != currentIndex || currentIndex < 0 || currentIndex >= coins.Length)
            return false;

        if (coins[currentIndex] != null)
            coins[currentIndex].SetAsCurrentTarget(false);

        bool reachedEndpoint =
            (direction > 0 && index == coins.Length - 1) ||
            (direction < 0 && index == 0);
        if (reachedEndpoint)
        {
            direction = -direction;
            reverseCoinsCollected = 0;
            IsTurnaroundInProgress = true;
            BlocksFormalEvents = true;
            ResetCoinsForNewLeg(index);
            currentIndex = index + direction;
            PrepareCurrentTarget();
            RefreshVisibleCoins();
            Debug.Log($"[CoinSequenceManager] Turnaround started at endpoint {index}. NewDirection={DirectionName}", this);
            return true;
        }

        if (IsTurnaroundInProgress)
        {
            reverseCoinsCollected++;
            if (reverseCoinsCollected >= reverseCoinsRequiredToResume)
            {
                IsTurnaroundInProgress = false;
                Debug.Log($"[CoinSequenceManager] Turnaround ready after {reverseCoinsCollected} reverse coin(s). Direction={DirectionName}", this);
            }
        }

        currentIndex += direction;
        UpdateFormalEventBlock();
        PrepareCurrentTarget();
        RefreshVisibleCoins();

        return true;
    }

    private string DirectionName => direction > 0 ? "FORWARD" : "BACKWARD";

    // Block new events when only the penultimate coin and endpoint remain.
    // RotationInjectionController only uses this route flag before event onset,
    // so an event already in progress is allowed to finish normally.
    private bool IsNearEndpointApproach =>
        direction > 0 ? currentIndex >= coins.Length - 2 : currentIndex <= 1;

    private void UpdateFormalEventBlock()
    {
        BlocksFormalEvents = IsTurnaroundInProgress || IsNearEndpointApproach;
    }

    private void ResetCoinsForNewLeg(int reachedEndpointIndex)
    {
        for (int i = 0; i < coins.Length; i++)
        {
            if (i == reachedEndpointIndex || coins[i] == null)
                continue;

            coins[i].PrepareForCollection(this, i);
            coins[i].SetAsCurrentTarget(false);
            coins[i].gameObject.SetActive(false);
        }
    }

    private void PrepareCurrentTarget()
    {
        if (coins == null || currentIndex < 0 || currentIndex >= coins.Length || coins[currentIndex] == null)
            return;

        coins[currentIndex].PrepareForCollection(this, currentIndex);
    }

    private void RefreshVisibleCoins()
    {
        if (coins == null)
            return;

        for (int i = 0; i < coins.Length; i++)
        {
            if (coins[i] == null)
                continue;

            bool shouldBeVisible = false;
            for (int offset = 0; offset < VisibleCoinWindowSize; offset++)
            {
                int visibleIndex = currentIndex + direction * offset;
                if (visibleIndex == i)
                {
                    shouldBeVisible = true;
                    break;
                }
            }

            coins[i].gameObject.SetActive(shouldBeVisible);
            coins[i].SetAsCurrentTarget(i == currentIndex);
        }
    }
}
