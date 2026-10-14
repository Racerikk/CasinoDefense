using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshObstacle))]
public class TestMachine : MonoBehaviour, IEnemyInteractable
{
    [Header("Interaction & Queue Setup")]
    [Tooltip("The spot in front of the machine where the active player stands.")]
    [SerializeField] private Transform interactionPoint;

    [Tooltip("Distance between enemies waiting in line.")]
    [SerializeField] private float queueSpacing = 1.0f;

    [Tooltip("Flips the queue line 180 degrees if your interaction point or model faces the opposite way.")]
    [SerializeField] private bool invertQueueDirection = false;

    [Tooltip("Optional: Specific transforms for the waiting line. If empty, calculates automatically.")]
    [SerializeField] private Transform[] customQueuePoints;

    [Header("Stats")]
    [SerializeField] private int machineBankroll = 100;
    [SerializeField] private int maxWaitQueue = 2;

    private readonly List<EnemyAgent> waitingQueue = new();
    private EnemyAgent currentOccupant;
    private NavMeshObstacle obstacle;
    private bool isPreview = false;

    public Transform InteractionPoint => interactionPoint != null ? interactionPoint : transform;
    public bool IsTargetValid => !isPreview && machineBankroll > 0 && gameObject.activeInHierarchy;
    public bool CanAcceptEnemy => IsTargetValid && (currentOccupant == null || waitingQueue.Count < maxWaitQueue);

    private void Awake()
    {
        obstacle = GetComponent<NavMeshObstacle>();
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
    }

    private void OnEnable()
    {
        if (!isPreview)
        {
            TargetRegistry.Register(this);
        }
    }

    private void OnDisable()
    {
        TargetRegistry.Unregister(this);
        DismissAllQueue();
    }

    public void ConfigureAsPreview()
    {
        isPreview = true;
        TargetRegistry.Unregister(this);

        if (obstacle != null)
        {
            obstacle.enabled = false;
        }
    }

    public List<Vector3> GetAllStandPositions()
    {
        List<Vector3> positions = new(maxWaitQueue + 1);
        Transform basePoint = InteractionPoint;

        // Default: extends backward from the interaction point's forward arrow (-basePoint.forward)
        Vector3 queueDir = invertQueueDirection ? basePoint.forward : -basePoint.forward;
        queueDir.y = 0f;
        queueDir.Normalize();

        // 0: Active player spot
        positions.Add(basePoint.position);

        // 1..N: Waiting queue spots
        for (int i = 0; i < maxWaitQueue; i++)
        {
            if (customQueuePoints != null && i < customQueuePoints.Length && customQueuePoints[i] != null)
            {
                positions.Add(customQueuePoints[i].position);
            }
            else
            {
                positions.Add(basePoint.position + (queueDir * ((i + 1) * queueSpacing)));
            }
        }

        return positions;
    }

    public bool TryJoinQueue(EnemyAgent enemy)
    {
        if (!CanAcceptEnemy || enemy == null) return false;

        if (currentOccupant == null)
        {
            currentOccupant = enemy;
            return true;
        }

        if (waitingQueue.Count < maxWaitQueue && !waitingQueue.Contains(enemy))
        {
            waitingQueue.Add(enemy);
            return true;
        }

        return false;
    }

    public void LeaveQueue(EnemyAgent enemy)
    {
        if (currentOccupant == enemy)
        {
            if (waitingQueue.Count > 0)
            {
                currentOccupant = waitingQueue[0];
                waitingQueue.RemoveAt(0);
            }
            else
            {
                currentOccupant = null;
            }
        }
        else
        {
            waitingQueue.Remove(enemy);
        }
    }

    public bool IsCurrentTurn(EnemyAgent enemy) => currentOccupant == enemy;

    public Vector3 GetAssignedPosition(EnemyAgent enemy)
    {
        List<Vector3> allSpots = GetAllStandPositions();

        if (currentOccupant == enemy || enemy == null)
        {
            return allSpots[0];
        }

        int queueIndex = waitingQueue.IndexOf(enemy);
        if (queueIndex >= 0 && (queueIndex + 1) < allSpots.Count)
        {
            return allSpots[queueIndex + 1];
        }

        return allSpots[0];
    }

    public void OnInteract(EnemyAgent enemy)
    {
        if (!IsCurrentTurn(enemy)) return;

        machineBankroll -= 10;
        if (CurrencyManager.Instance != null)
        {
            CurrencyManager.Instance.AddCurrency(10);
        }

        if (machineBankroll <= 0)
        {
            TargetRegistry.Unregister(this);
            DismissAllQueue();
        }
    }

    private void DismissAllQueue()
    {
        currentOccupant = null;
        waitingQueue.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        List<Vector3> spots = GetAllStandPositions();
        for (int i = 0; i < spots.Count; i++)
        {
            Gizmos.color = i == 0 ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(spots[i], 0.3f);
            if (i > 0)
            {
                Gizmos.DrawLine(spots[i - 1], spots[i]);
            }
        }
    }
}