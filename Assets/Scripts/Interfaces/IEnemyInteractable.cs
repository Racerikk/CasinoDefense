using UnityEngine;

public interface IEnemyInteractable
{
    bool IsTargetValid { get; }
    bool CanAcceptEnemy { get; }
    Transform InteractionPoint { get; }

    bool TryJoinQueue(EnemyAgent enemy);
    void LeaveQueue(EnemyAgent enemy);
    Vector3 GetAssignedPosition(EnemyAgent enemy);
    bool IsCurrentTurn(EnemyAgent enemy);
    void OnInteract(EnemyAgent enemy);
}