using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Button))]
public class TowerShopButton : MonoBehaviour
{
    [Header("Tower Setup")]
    [SerializeField] private GameObject towerPrefab;
    [SerializeField] private int cost = 100;

    [Header("Optional UI Labels")]
    [SerializeField] private TextMeshProUGUI costLabel;
    [SerializeField] private TowerPlacementManager placementManager;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();

        if (placementManager == null)
        {
            placementManager = FindFirstObjectByType<TowerPlacementManager>();
        }

        if (costLabel != null)
        {
            costLabel.text = $"${cost}";
        }

        button.onClick.AddListener(OnButtonClicked);
    }

    private void OnEnable()
    {
        if (CurrencyManager.Instance != null)
        {
            CurrencyManager.Instance.OnBalanceChanged += HandleBalanceChanged;
            HandleBalanceChanged(CurrencyManager.Instance.CurrentBalance);
        }
    }

    private void Start()
    {
        if (CurrencyManager.Instance != null)
        {
            CurrencyManager.Instance.OnBalanceChanged -= HandleBalanceChanged;
            CurrencyManager.Instance.OnBalanceChanged += HandleBalanceChanged;
            HandleBalanceChanged(CurrencyManager.Instance.CurrentBalance);
        }
    }

    private void OnDisable()
    {
        if (CurrencyManager.Instance != null)
        {
            CurrencyManager.Instance.OnBalanceChanged -= HandleBalanceChanged;
        }
    }

    private void HandleBalanceChanged(int currentBalance)
    {
        // Automatically disable the UI button if the player cannot afford this machine
        button.interactable = currentBalance >= cost;
    }

    private void OnButtonClicked()
    {
        if (placementManager != null && towerPrefab != null)
        {
            placementManager.StartPlacement(towerPrefab, cost);
        }
    }
}