using System;
using UnityEngine;
using TMPro;

public class CurrencyManager : MonoBehaviour
{
    public static CurrencyManager Instance { get; private set; }

    [Header("Starting Bankroll")]
    [SerializeField] private int startingCurrency = 500;

    [Header("UI Reference")]
    [SerializeField] private TextMeshProUGUI balanceText;
    [SerializeField] private string currencyPrefix = "$";

    public int CurrentBalance { get; private set; }

    public event Action<int> OnBalanceChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        CurrentBalance = startingCurrency;
    }

    private void Start()
    {
        UpdateUI();
        OnBalanceChanged?.Invoke(CurrentBalance);
    }

    public bool CanAfford(int cost)
    {
        return CurrentBalance >= cost;
    }

    public bool TrySpend(int cost)
    {
        if (!CanAfford(cost)) return false;

        CurrentBalance -= cost;
        UpdateUI();
        OnBalanceChanged?.Invoke(CurrentBalance);
        return true;
    }

    public void AddCurrency(int amount)
    {
        if (amount <= 0) return;

        CurrentBalance += amount;
        UpdateUI();
        OnBalanceChanged?.Invoke(CurrentBalance);
    }

    private void UpdateUI()
    {
        if (balanceText != null)
        {
            balanceText.text = $"{currencyPrefix}{CurrentBalance:N0}";
        }
    }
}