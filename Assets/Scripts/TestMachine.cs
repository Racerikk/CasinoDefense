using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshObstacle))]
public class TestMachine : MonoBehaviour, IEnemyInteractable
{
    [Header("Interaction & Queue Setup")]
    [Tooltip("The spot where the active player stands while spinning.")]
    [SerializeField] private Transform interactionPoint;

    [Tooltip("Distance between enemies waiting in line behind the interaction point.")]
    [SerializeField] private float queueSpacing = 1.0f;

    [Tooltip("Optional: Specific transforms for the waiting line. If left empty, positions calculate automatically.")]
    [SerializeField] private Transform[] customQueuePoints;

    [Header("Stats")]
    [SerializeField] private int machineBankroll = 100;
    [SerializeField] private int maxWaitQueue = 2; // Up to 2 waiting + 1 active

    private readonly List<EnemyAgent> waitingQueue = new();
    private EnemyAgent currentOccupant;
    private NavMeshObstacle obstacle;

    public Transform InteractionPoint => interactionPoint;
    public bool IsTargetValid => machineBankroll > 0 && gameObject.activeInHierarchy;
    public bool CanAcceptEnemy => IsTargetValid && (currentOccupant == null || waitingQueue.Count < maxWaitQueue);

    private void Awake()
    {
        obstacle = GetComponent<NavMeshObstacle>();
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
    }

    private void OnEnable() => TargetRegistry.Register(this);
    private void OnDisable()
    {
        TargetRegistry.Unregister(this);
        DismissAllQueue();
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
            // Advance the line: next in queue takes the machine
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
        Transform basePoint = interactionPoint != null ? interactionPoint : transform;

        // Active occupant stands directly at the interaction point
        if (currentOccupant == enemy)
        {
            return basePoint.position;
        }

        int queueIndex = waitingQueue.IndexOf(enemy);
        if (queueIndex >= 0)
        {
            // Use custom markers if assigned
            if (customQueuePoints != null && queueIndex < customQueuePoints.Length && customQueuePoints[queueIndex] != null)
            {
                return customQueuePoints[queueIndex].position;
            }

            // Fallback: Line up backward along the interaction point's forward vector
            return basePoint.position - (basePoint.forward * ((queueIndex + 1) * queueSpacing));
        }

        return basePoint.position;
    }

    public void OnInteract(EnemyAgent enemy)
    {
        if (!IsCurrentTurn(enemy)) return;

        machineBankroll -= 10;
        Debug.Log($"[{name}] Pulled by {enemy.name}! Credits left: {machineBankroll}");

        if (machineBankroll <= 0)
        {
            Debug.Log($"[{name}] Out of cash! Disbanding queue.");
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
        Transform basePoint = interactionPoint != null ? interactionPoint : transform;

        // Front interaction point
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(basePoint.position, 0.3f);

        // Queue spots
        Gizmos.color = Color.yellow;
        for (int i = 0; i < maxWaitQueue; i++)
        {
            Vector3 pos;
            if (customQueuePoints != null && i < customQueuePoints.Length && customQueuePoints[i] != null)
            {
                pos = customQueuePoints[i].position;
            }
            else
            {
                pos = basePoint.position - (basePoint.forward * ((i + 1) * queueSpacing));
            }

            Gizmos.DrawWireSphere(pos, 0.25f);
        }
    }
}