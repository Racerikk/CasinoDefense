using System.Collections.Generic;
using UnityEngine;

public static class TargetRegistry
{
    private static readonly List<IEnemyInteractable> targets = new();

    public static IReadOnlyList<IEnemyInteractable> AllTargets => targets;

    public static void Register(IEnemyInteractable target)
    {
        if (!targets.Contains(target)) targets.Add(target);
    }

    public static void Unregister(IEnemyInteractable target)
    {
        targets.Remove(target);
    }

    public static IEnemyInteractable GetNearestAvailableTarget(Vector3 position)
    {
        IEnemyInteractable bestTarget = null;
        float shortestDistSqr = float.MaxValue;

        for (int i = targets.Count - 1; i >= 0; i--)
        {
            var target = targets[i];
            if (target == null || !target.IsTargetValid)
            {
                targets.RemoveAt(i);
                continue;
            }

            if (!target.CanAcceptEnemy) continue;

            float distSqr = (target.GetAssignedPosition(null) - position).sqrMagnitude;
            if (distSqr < shortestDistSqr)
            {
                shortestDistSqr = distSqr;
                bestTarget = target;
            }
        }

        return bestTarget;
    }

    public static IEnemyInteractable GetRandomAvailableTarget()
    {
        List<IEnemyInteractable> available = new();

        for (int i = targets.Count - 1; i >= 0; i--)
        {
            var target = targets[i];
            if (target == null || !target.IsTargetValid)
            {
                targets.RemoveAt(i);
                continue;
            }

            if (target.CanAcceptEnemy) available.Add(target);
        }

        if (available.Count == 0) return null;
        return available[Random.Range(0, available.Count)];
    }
}