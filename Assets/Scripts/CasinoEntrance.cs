using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CasinoEntrance : MonoBehaviour
{
    [Header("Spawner Setup")]
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private Transform spawnPoint;

    public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;

    public void SpawnEnemy()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning($"[{name}] No enemy prefab assigned!", this);
            return;
        }

        Vector3 spawnPos = SpawnPoint.position;
        Quaternion spawnRot = SpawnPoint.rotation;

        GameObject spawned = Instantiate(enemyPrefab, spawnPos, spawnRot);

        if (spawned.TryGetComponent<EnemyAgent>(out var agent))
        {
            agent.InitializeEntrance(this);
        }
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

        if (GUILayout.Button("Spawn Enemy", GUILayout.Height(32)))
        {
            if (Application.isPlaying)
            {
                entrance.SpawnEnemy();
            }
            else
            {
                Debug.LogWarning("You must be in Play Mode to spawn NavMesh agents.");
            }
        }

        GUI.backgroundColor = Color.white;
    }
}
#endif