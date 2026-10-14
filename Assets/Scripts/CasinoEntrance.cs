using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CasinoEntrance : MonoBehaviour
{
    public static readonly List<CasinoEntrance> AllEntrances = new();

    [Header("Default Spawner Setup")]
    [SerializeField] private GameObject defaultEnemyPrefab;
    [SerializeField] private Transform spawnPoint;

    public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;

    private void OnEnable() => AllEntrances.Add(this);
    private void OnDisable() => AllEntrances.Remove(this);

    // Used by the Editor test button
    public EnemyAgent SpawnEnemy()
    {
        return SpawnEnemy(defaultEnemyPrefab);
    }

    // Used by WaveManager to spawn specific enemy types
    public EnemyAgent SpawnEnemy(GameObject prefabToSpawn)
    {
        if (prefabToSpawn == null)
        {
            Debug.LogWarning($"[{name}] No enemy prefab assigned!", this);
            return null;
        }

        GameObject spawned = Instantiate(prefabToSpawn, SpawnPoint.position, SpawnPoint.rotation);

        if (spawned.TryGetComponent<EnemyAgent>(out var agent))
        {
            agent.InitializeEntrance(this);
            return agent;
        }

        return null;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Vector3 pos = SpawnPoint.position;
        Gizmos.DrawWireCube(pos + Vector3.up * 1f, new Vector3(1f, 2f, 1f));
        Gizmos.DrawLine(pos, pos + SpawnPoint.forward * 1.5f);
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(CasinoEntrance))]
public class CasinoEntranceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CasinoEntrance entrance = (CasinoEntrance)target;
        EditorGUILayout.Space(12);
        GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);

        if (GUILayout.Button("Spawn Test Enemy", GUILayout.Height(32)))
        {
            if (Application.isPlaying) entrance.SpawnEnemy();
            else Debug.LogWarning("Must be in Play Mode to spawn NavMesh agents.");
        }

        GUI.backgroundColor = Color.white;
    }
}
#endif