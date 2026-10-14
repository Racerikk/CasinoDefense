using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class WaveManager : MonoBehaviour
{
    [Serializable]
    public class EnemySpawnEntry
    {
        public string enemyName = "Gambler";
        public GameObject prefab;
        [Tooltip("The wave number when this enemy type starts appearing.")]
        public int minWaveToUnlock = 1;
        [Tooltip("Relative chance to spawn compared to other unlocked enemies.")]
        [Range(1, 100)] public int spawnWeight = 50;
    }

    [Header("Wave Timing")]
    [Tooltip("Delay in seconds before Wave 1 and between subsequent waves.")]
    [SerializeField] private float delayBetweenWaves = 10f;
    [SerializeField] private bool autoStartWaves = true;

    [Header("Enemy Roster")]
    [SerializeField] private List<EnemySpawnEntry> enemyPool = new();

    [Header("Difficulty Scaling")]
    [SerializeField] private int baseEnemyCount = 5;
    [Tooltip("Additional enemies added per wave (e.g., 1.35 = +35% more enemies each wave).")]
    [SerializeField] private float enemyCountMultiplier = 1.3f;
    [SerializeField] private int flatEnemyIncreasePerWave = 2;

    [Space(6)]
    [SerializeField] private float baseSpawnInterval = 2.0f;
    [Tooltip("Multiplies spawn interval each wave (e.g., 0.92 = 8% faster spawns per wave).")]
    [SerializeField] private float spawnIntervalDecay = 0.92f;
    [SerializeField] private float minSpawnInterval = 0.35f;

    [Space(6)]
    [Tooltip("Increases enemy movement speed per wave (e.g., 0.05 = +5% speed per wave).")]
    [SerializeField] private float speedScalePerWave = 0.05f;
    [Tooltip("Adds extra machine spins to enemies every N waves.")]
    [SerializeField] private int wavesPerExtraSpin = 3;

    [Header("Wave Rewards")]
    [SerializeField] private int baseWaveClearBonus = 50;
    [SerializeField] private int bonusIncreasePerWave = 25;

    [Header("UI References (Optional)")]
    [SerializeField] private TextMeshProUGUI waveNumberText;
    [SerializeField] private TextMeshProUGUI waveStatusText;

    public int CurrentWave { get; private set; } = 0;
    public bool IsWaveActive { get; private set; } = false;

    private Coroutine waveLoopCoroutine;
    private float currentCountdown;
    private bool skipCountdownRequested = false;

    private void Start()
    {
        UpdateWaveUI();

        if (autoStartWaves)
        {
            waveLoopCoroutine = StartCoroutine(WaveLifecycleRoutine());
        }
    }

    private IEnumerator WaveLifecycleRoutine()
    {
        while (true)
        {
            // 1. Pre-Wave Delay (Preparation Phase)
            IsWaveActive = false;
            currentCountdown = delayBetweenWaves;
            skipCountdownRequested = false;

            while (currentCountdown > 0f && !skipCountdownRequested)
            {
                if (waveStatusText != null)
                {
                    waveStatusText.text = $"Next Wave in: {Mathf.CeilToInt(currentCountdown)}s";
                }

                currentCountdown -= Time.deltaTime;
                yield return null;
            }

            // 2. Start Wave & Calculate Scaled Difficulty
            CurrentWave++;
            IsWaveActive = true;
            UpdateWaveUI();

            int totalEnemiesToSpawn = CalculateEnemyCount(CurrentWave);
            float currentSpawnInterval = CalculateSpawnInterval(CurrentWave);
            float speedMultiplier = 1f + ((CurrentWave - 1) * speedScalePerWave);
            int extraSpins = (CurrentWave - 1) / Mathf.Max(1, wavesPerExtraSpin);

            // 3. Spawn Enemies Across Entrances
            for (int i = 0; i < totalEnemiesToSpawn; i++)
            {
                if (waveStatusText != null)
                {
                    waveStatusText.text = $"Spawning: {totalEnemiesToSpawn - i} left";
                }

                SpawnScaledEnemy(speedMultiplier, extraSpins);
                yield return new WaitForSeconds(currentSpawnInterval);
            }

            // 4. Wait for All Active Enemies to Finish Gambling & Despawn
            while (EnemyAgent.ActiveEnemies.Count > 0)
            {
                if (waveStatusText != null)
                {
                    waveStatusText.text = $"Gamblers on Floor: {EnemyAgent.ActiveEnemies.Count}";
                }
                yield return new WaitForSeconds(0.25f);
            }

            // 5. Wave Complete Bonus
            AwardWaveClearBonus();
        }
    }

    private void SpawnScaledEnemy(float speedMultiplier, int extraSpins)
    {
        if (CasinoEntrance.AllEntrances.Count == 0)
        {
            Debug.LogWarning("[WaveManager] No active CasinoEntrances found in scene!");
            return;
        }

        GameObject selectedPrefab = GetWeightedEnemyPrefab(CurrentWave);
        if (selectedPrefab == null) return;

        // Pick a random entrance from available entrances
        CasinoEntrance entrance = CasinoEntrance.AllEntrances[UnityEngine.Random.Range(0, CasinoEntrance.AllEntrances.Count)];
        EnemyAgent spawnedAgent = entrance.SpawnEnemy(selectedPrefab);

        if (spawnedAgent != null)
        {
            spawnedAgent.ApplyWaveScaling(speedMultiplier, extraSpins);
        }
    }

    private GameObject GetWeightedEnemyPrefab(int wave)
    {
        List<EnemySpawnEntry> unlocked = new();
        int totalWeight = 0;

        foreach (var entry in enemyPool)
        {
            if (entry.prefab != null && wave >= entry.minWaveToUnlock)
            {
                unlocked.Add(entry);
                totalWeight += entry.spawnWeight;
            }
        }

        if (unlocked.Count == 0) return null;

        int roll = UnityEngine.Random.Range(0, totalWeight);
        int cursor = 0;

        foreach (var entry in unlocked)
        {
            cursor += entry.spawnWeight;
            if (roll < cursor)
            {
                return entry.prefab;
            }
        }

        return unlocked[0].prefab;
    }

    private int CalculateEnemyCount(int wave)
    {
        // Combines exponential and linear growth for smooth early-to-late game scaling
        float scaled = baseEnemyCount * Mathf.Pow(enemyCountMultiplier, wave - 1) + ((wave - 1) * flatEnemyIncreasePerWave);
        return Mathf.RoundToInt(scaled);
    }

    private float CalculateSpawnInterval(int wave)
    {
        float interval = baseSpawnInterval * Mathf.Pow(spawnIntervalDecay, wave - 1);
        return Mathf.Max(minSpawnInterval, interval);
    }

    private void AwardWaveClearBonus()
    {
        if (CurrencyManager.Instance != null)
        {
            int reward = baseWaveClearBonus + ((CurrentWave - 1) * bonusIncreasePerWave);
            CurrencyManager.Instance.AddCurrency(reward);
        }
    }

    private void UpdateWaveUI()
    {
        if (waveNumberText != null)
        {
            waveNumberText.text = CurrentWave > 0 ? $"Wave {CurrentWave}" : "Preparing...";
        }
    }

    // Hook this up to a UI Button if you want players to call the next wave early
    public void SkipCountdown()
    {
        if (!IsWaveActive)
        {
            skipCountdownRequested = true;
        }
    }
}