using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAgent : MonoBehaviour
{
    public static readonly System.Collections.Generic.HashSet<EnemyAgent> ActiveEnemies = new();
    private enum EnemyState
    {
        SeekingMachine,
        WaitingOrPlaying,
        ReturningToExit
    }

    [Header("Targeting Mode")]
    [SerializeField] private TargetStrategy strategy = TargetStrategy.Nearest;
    [SerializeField] private float repathRate = 0.5f;

    [Header("Gambling Session Goals")]
    [SerializeField] private int minPlays = 2;
    [SerializeField] private int maxPlays = 4;
    [SerializeField] private float interactInterval = 1.2f;

    [Header("Interaction Effects")]
    [Tooltip("Prefab to instantiate on each pull (e.g., coin burst, confetti, sparks).")]
    [SerializeField] private GameObject interactionParticlePrefab;

    [Tooltip("Optional: An attached ParticleSystem on this enemy to trigger instead of or in addition to a prefab.")]
    [SerializeField] private ParticleSystem attachedParticleSystem;

    [Tooltip("Custom spawn origin for the VFX. If unassigned, spawns at the machine's interaction point.")]
    [SerializeField] private Transform particleSpawnPoint;

    [Header("Despawn & Arrival")]
    [Tooltip("Maximum horizontal distance to the exit point to trigger despawn.")]
    [SerializeField] private float exitArrivalThreshold = 1.2f;

    private NavMeshAgent agent;
    private IEnemyInteractable currentTarget;
    private CasinoEntrance originEntrance;
    private Vector3 fallbackExitPoint;
    private Vector3 resolvedExitDestination;

    private EnemyState state = EnemyState.SeekingMachine;
    private int targetPlays;
    private int completedPlays;
    private float nextInteractTime;
    private float nextRepathTime;

    public enum TargetStrategy { Nearest, Random }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        fallbackExitPoint = transform.position;
    }

    private void Start()
    {
        if (originEntrance == null)
        {
            InitializeGoals();
            AcquireTarget();
        }
    }
    private void OnEnable()
    {
        ActiveEnemies.Add(this);
    }

    private void OnDisable()
    {
        ActiveEnemies.Remove(this);
        LeaveCurrentMachine();
    }

    public void InitializeEntrance(CasinoEntrance entrance)
    {
        originEntrance = entrance;
        fallbackExitPoint = entrance.SpawnPoint.position;
        InitializeGoals();
        AcquireTarget();
    }

    private void InitializeGoals()
    {
        targetPlays = Random.Range(minPlays, maxPlays + 1);
        completedPlays = 0;
    }

    private void Update()
    {
        switch (state)
        {
            case EnemyState.SeekingMachine:
            case EnemyState.WaitingOrPlaying:
                HandleMachineLifecycle();
                break;

            case EnemyState.ReturningToExit:
                HandleExitLifecycle();
                break;
        }
    }

    private void HandleMachineLifecycle()
    {
        if (currentTarget == null || !currentTarget.IsTargetValid)
        {
            LeaveCurrentMachine();

            if (completedPlays > 0)
            {
                HeadToExit();
            }
            else
            {
                AcquireTarget();
            }
            return;
        }

        Vector3 targetSpot = currentTarget.GetAssignedPosition(this);

        if (Time.time >= nextRepathTime)
        {
            if (Vector3.Distance(agent.destination, targetSpot) > 0.1f)
            {
                agent.SetDestination(targetSpot);
            }
            nextRepathTime = Time.time + repathRate;
        }

        float flatDist = FlatDistance(transform.position, targetSpot);
        bool hasArrived = !agent.pathPending && (flatDist <= agent.stoppingDistance + 0.4f);

        if (hasArrived)
        {
            Vector3 lookDir = targetSpot - transform.position;
            lookDir.y = 0;
            if (lookDir != Vector3.zero)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 10f);
            }

            if (currentTarget.IsCurrentTurn(this))
            {
                state = EnemyState.WaitingOrPlaying;

                if (Time.time >= nextInteractTime)
                {
                    currentTarget.OnInteract(this);
                    completedPlays++;

                    SpawnInteractionParticles(); // Triggers VFX here

                    nextInteractTime = Time.time + interactInterval;

                    if (completedPlays >= targetPlays)
                    {
                        HeadToExit();
                    }
                }
            }
        }
    }

    private void SpawnInteractionParticles()
    {
        // 1. Play attached particle system if one exists
        if (attachedParticleSystem != null)
        {
            attachedParticleSystem.Play();
        }

        // 2. Instantiate external particle prefab if assigned
        if (interactionParticlePrefab != null)
        {
            Vector3 spawnPosition;

            if (particleSpawnPoint != null)
            {
                spawnPosition = particleSpawnPoint.position;
            }
            else if (currentTarget != null)
            {
                // Default to slightly raised above the slot machine's interaction spot
                spawnPosition = currentTarget.GetAssignedPosition(this) + Vector3.up * 1.0f;
            }
            else
            {
                spawnPosition = transform.position + Vector3.up * 1.0f;
            }

            GameObject effectInstance = Instantiate(interactionParticlePrefab, spawnPosition, interactionParticlePrefab.transform.rotation);

            // Safety cleanup in case the prefab doesn't have Stop Action set to Destroy
            if (effectInstance.TryGetComponent<ParticleSystem>(out var ps))
            {
                float totalDuration = ps.main.duration + ps.main.startLifetime.constantMax;
                Destroy(effectInstance, totalDuration);
            }
            else
            {
                Destroy(effectInstance, 2.0f);
            }
        }
    }
    public void ApplyWaveScaling(float speedMultiplier, int extraPlays)
    {
        if (agent != null)
        {
            agent.speed *= speedMultiplier;
        }

        targetPlays += extraPlays;
    }
    private void HandleExitLifecycle()
    {
        float flatDistToExit = FlatDistance(transform.position, resolvedExitDestination);

        bool navMeshArrived = !agent.pathPending &&
                              agent.remainingDistance <= agent.stoppingDistance + exitArrivalThreshold &&
                              (!agent.hasPath || agent.velocity.sqrMagnitude < 0.05f);

        if (flatDistToExit <= exitArrivalThreshold || navMeshArrived)
        {
            Destroy(gameObject);
        }
    }

    private void HeadToExit()
    {
        LeaveCurrentMachine();
        state = EnemyState.ReturningToExit;

        Vector3 rawExit = originEntrance != null ? originEntrance.SpawnPoint.position : fallbackExitPoint;

        if (NavMesh.SamplePosition(rawExit, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
        {
            resolvedExitDestination = hit.position;
        }
        else
        {
            resolvedExitDestination = rawExit;
        }

        agent.isStopped = false;
        agent.ResetPath();
        agent.SetDestination(resolvedExitDestination);
    }

    public void AcquireTarget()
    {
        IEnemyInteractable selected = strategy switch
        {
            TargetStrategy.Random => TargetRegistry.GetRandomAvailableTarget(),
            _ => TargetRegistry.GetNearestAvailableTarget(transform.position)
        };

        if (selected != null && selected.TryJoinQueue(this))
        {
            currentTarget = selected;
            state = EnemyState.SeekingMachine;
            agent.isStopped = false;
            agent.SetDestination(currentTarget.GetAssignedPosition(this));
        }
        else
        {
            currentTarget = null;
            agent.isStopped = true;

            if (completedPlays > 0)
            {
                HeadToExit();
            }
        }
    }

    private void LeaveCurrentMachine()
    {
        if (currentTarget != null)
        {
            currentTarget.LeaveQueue(this);
            currentTarget = null;
        }
    }

    private float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
    private void OnDestroy() => LeaveCurrentMachine();
}