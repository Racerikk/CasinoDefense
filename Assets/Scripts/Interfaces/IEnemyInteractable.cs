using System.Collections.Generic;
using UnityEngine;

public interface IEnemyInteractable
{
    Transform InteractionPoint { get; }
    bool IsTargetValid { get; }
    bool CanAcceptEnemy { get; }

    bool TryJoinQueue(EnemyAgent enemy);
    void LeaveQueue(EnemyAgent enemy);
    Vector3 GetAssignedPosition(EnemyAgent enemy);
    List<Vector3> GetAllStandPositions();
    bool IsCurrentTurn(EnemyAgent enemy);
    void OnInteract(EnemyAgent enemy);
}