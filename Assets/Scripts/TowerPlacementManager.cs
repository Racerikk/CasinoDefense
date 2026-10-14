using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using DG.Tweening;

public class TowerPlacementManager : MonoBehaviour
{
    [Header("Placement Settings")]
    [SerializeField] private LayerMask placementLayer; // Ground layer
    [SerializeField] private LayerMask obstacleLayer;  // Placed machines & walls
    [SerializeField] private bool snapToGrid = true;
    [SerializeField] private float gridSize = 1f;

    [Header("Queue Clearance Settings")]
    [Tooltip("Half-width of the queue corridor. Two queues cannot come closer than 2x this value at any point along their lines.")]
    [SerializeField] private float queueCorridorRadius = 0.45f;
    [Tooltip("Maximum horizontal drift allowed when snapping a queue spot to the NavMesh.")]
    [SerializeField] private float maxNavMeshSnapDrift = 0.5f;

    [Header("Rotation Settings")]
    [SerializeField] private float rotationStep = 90f;
    [SerializeField] private float rotationDuration = 0.2f;
    [SerializeField] private Ease rotationEase = Ease.OutBack;

    [Header("Height Adjustment")]
    [SerializeField] private bool autoCalculateHeight = true;
    [SerializeField] private float heightOffset = 0f;

    [Header("Visual Feedback Materials")]
    [SerializeField] private Material validMaterial;
    [SerializeField] private Material invalidMaterial;

    private Camera mainCamera;
    private GameObject currentTowerPrefab;
    private int currentTowerCost;
    private GameObject currentPreview;
    private Renderer[] previewRenderers;

    // Stores world-scale offsets at 0 rotation so prefab localScale never distorts queue length
    private readonly List<Vector3> unrotatedWorldOffsets = new();
    private Vector3 machineExtents;
    private Vector3 localBoundsCenterOffset;

    private bool isPlacing = false;
    private bool isCurrentSpotValid = false;
    private float calculatedOffset = 0f;
    private float currentRotationY = 0f;

    private Tween rotationTween;
    private NavMeshPath reusablePath;

    private void Awake()
    {
        mainCamera = Camera.main;
        reusablePath = new NavMeshPath();
    }

    private void Update()
    {
        if (!isPlacing || Keyboard.current == null || Mouse.current == null) return;

        if (CurrencyManager.Instance != null && !CurrencyManager.Instance.CanAfford(currentTowerCost))
        {
            CancelPlacement();
            return;
        }

        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            RotatePreview();
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            if (currentPreview != null) currentPreview.SetActive(false);
            return;
        }

        UpdatePreview();

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (isCurrentSpotValid)
            {
                PlaceTower();
            }
            else
            {
                Debug.LogWarning("[Placement] Invalid placement: Insufficient space for machine or queue overlap!");
            }
        }
        else if (Mouse.current.rightButton.wasPressedThisFrame || Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CancelPlacement();
        }
    }

    public void StartPlacement(GameObject newTowerPrefab, int cost = 0)
    {
        if (CurrencyManager.Instance != null && !CurrencyManager.Instance.CanAfford(cost))
        {
            Debug.LogWarning("[Placement] Not enough currency to buy this tower!");
            return;
        }

        CancelPlacement();

        currentTowerPrefab = newTowerPrefab;
        currentTowerCost = cost;
        isPlacing = true;
        currentRotationY = 0f;

        currentPreview = Instantiate(currentTowerPrefab, Vector3.zero, Quaternion.identity);

        // Support TestMachine on either the root or a child GameObject
        TestMachine testMachine = currentPreview.GetComponentInChildren<TestMachine>();
        if (testMachine != null)
        {
            testMachine.ConfigureAsPreview();
        }

        CalculateFootprintBounds(currentPreview, out machineExtents, out localBoundsCenterOffset);

        unrotatedWorldOffsets.Clear();
        IEnemyInteractable interactable = currentPreview.GetComponentInChildren<IEnemyInteractable>();
        if (interactable != null)
        {
            TargetRegistry.Unregister(interactable);
            List<Vector3> worldSpots = interactable.GetAllStandPositions();
            foreach (Vector3 spot in worldSpots)
            {
                // Subtract root position directly to preserve exact world scale distances
                Vector3 offset = spot - currentPreview.transform.position;
                offset.y = 0f;
                unrotatedWorldOffsets.Add(offset);
            }
        }
        else
        {
            unrotatedWorldOffsets.Add(Vector3.forward * 1.0f);
        }

        foreach (var col in currentPreview.GetComponentsInChildren<Collider>()) col.enabled = false;
        foreach (var obs in currentPreview.GetComponentsInChildren<NavMeshObstacle>()) obs.enabled = false;

        calculatedOffset = autoCalculateHeight ? CalculateBottomOffset(currentPreview) + heightOffset : heightOffset;
        previewRenderers = currentPreview.GetComponentsInChildren<Renderer>();

        currentPreview.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);
    }

    private void RotatePreview()
    {
        if (currentPreview == null) return;

        currentRotationY = (currentRotationY + rotationStep) % 360f;

        rotationTween?.Kill();
        rotationTween = currentPreview.transform
            .DORotate(new Vector3(0f, currentRotationY, 0f), rotationDuration, RotateMode.Fast)
            .SetEase(rotationEase);
    }

    private void UpdatePreview()
    {
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, placementLayer))
        {
            currentPreview.SetActive(true);
            Vector3 targetPos = CalculatePlacementPosition(hit.point);
            currentPreview.transform.position = targetPos;

            isCurrentSpotValid = ValidatePlacement(targetPos, currentRotationY);
            ApplyFeedbackMaterial(isCurrentSpotValid ? validMaterial : invalidMaterial);
        }
        else
        {
            currentPreview.SetActive(false);
            isCurrentSpotValid = false;
        }
    }

    private bool ValidatePlacement(Vector3 candidatePos, float rotationY)
    {
        Quaternion rot = Quaternion.Euler(0f, rotationY, 0f);
        float floorY = candidatePos.y - calculatedOffset;

        // 1. Check physical overlap of the machine body against placed machines/walls
        Vector3 worldBoxCenter = candidatePos + (rot * localBoundsCenterOffset) + Vector3.up * 0.05f;
        Vector3 checkHalfExtents = new Vector3(
            machineExtents.x * 0.9f,
            Mathf.Max(0.1f, machineExtents.y * 0.85f),
            machineExtents.z * 0.9f
        );

        if (Physics.CheckBox(worldBoxCenter, checkHalfExtents, rot, obstacleLayer, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        // 2. Build the candidate queue polyline (starting at machine center -> spot 0 -> spot 1 -> spot 2)
        // Including machine center ensures another queue cannot cut between the machine and the active player
        List<Vector3> candidatePolyline = new(unrotatedWorldOffsets.Count + 1)
        {
            new Vector3(candidatePos.x, floorY, candidatePos.z)
        };

        List<Vector3> candidateQueueNavPoints = new(unrotatedWorldOffsets.Count);

        for (int i = 0; i < unrotatedWorldOffsets.Count; i++)
        {
            Vector3 rawSpot = candidatePos + (rot * unrotatedWorldOffsets[i]);
            Vector3 floorSpot = new Vector3(rawSpot.x, floorY, rawSpot.z);

            // A: Physical clearance check against walls and other machine colliders
            Vector3 sphereCenter = floorSpot + Vector3.up * (queueCorridorRadius + 0.15f);
            if (Physics.CheckSphere(sphereCenter, queueCorridorRadius, obstacleLayer, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            // B: NavMesh walkability check
            if (!NavMesh.SamplePosition(floorSpot, out NavMeshHit navHit, 2.0f, NavMesh.AllAreas))
            {
                return false;
            }

            if (FlatDistance(floorSpot, navHit.position) > maxNavMeshSnapDrift)
            {
                return false;
            }

            // C: Ensure no wall or carved hole cuts between consecutive queue spots
            if (i > 0)
            {
                if (NavMesh.Raycast(candidateQueueNavPoints[i - 1], navHit.position, out _, NavMesh.AllAreas))
                {
                    return false;
                }
            }

            candidatePolyline.Add(floorSpot);
            candidateQueueNavPoints.Add(navHit.position);
        }

        // 3. Continuous Corridor (Capsule-to-Capsule) Overlap Check against all existing machines
        IReadOnlyList<IEnemyInteractable> existingTargets = TargetRegistry.AllTargets;
        float minAllowedDistance = queueCorridorRadius * 2f;
        float machineBodyRadius = Mathf.Max(machineExtents.x, machineExtents.z) + queueCorridorRadius;

        for (int i = 0; i < existingTargets.Count; i++)
        {
            var existing = existingTargets[i];
            if (existing == null || !existing.IsTargetValid) continue;

            List<Vector3> existingSpots = existing.GetAllStandPositions();
            if (existingSpots.Count == 0) continue;

            // Build existing machine's polyline (existing machine center -> spot 0 -> spot 1 -> ...)
            List<Vector3> existingPolyline = new(existingSpots.Count + 1);
            if (existing is MonoBehaviour mb)
            {
                existingPolyline.Add(mb.transform.position);
            }
            existingPolyline.AddRange(existingSpots);

            // A: Check every segment of the candidate queue against every segment of the existing queue
            for (int c = 0; c < candidatePolyline.Count - 1; c++)
            {
                Vector3 cStart = candidatePolyline[c];
                Vector3 cEnd = candidatePolyline[c + 1];

                for (int e = 0; e < existingPolyline.Count - 1; e++)
                {
                    Vector3 eStart = existingPolyline[e];
                    Vector3 eEnd = existingPolyline[e + 1];

                    float segDist = SegmentToSegmentDistanceXZ(cStart, cEnd, eStart, eEnd);
                    if (segDist < minAllowedDistance)
                    {
                        return false; // Queues cross, touch, or interleave!
                    }
                }
            }

            // B: Ensure existing queue segments do not pass through the candidate machine's physical footprint
            for (int e = 0; e < existingPolyline.Count - 1; e++)
            {
                float distToCandidateBody = PointToSegmentDistanceXZ(candidatePos, existingPolyline[e], existingPolyline[e + 1]);
                if (distToCandidateBody < machineBodyRadius)
                {
                    return false; // Candidate machine body sits on an existing queue line!
                }
            }
        }

        if (CasinoEntrance.AllEntrances.Count == 0) return true;

        // 4. Verify at least one entrance has a valid path to the back of the new machine's queue
        Vector3 backQueueTarget = candidateQueueNavPoints[candidateQueueNavPoints.Count - 1];
        bool canReachNew = false;

        foreach (var entrance in CasinoEntrance.AllEntrances)
        {
            if (entrance == null) continue;

            if (NavMesh.SamplePosition(entrance.SpawnPoint.position, out NavMeshHit startHit, 2.0f, NavMesh.AllAreas))
            {
                if (NavMesh.CalculatePath(startHit.position, backQueueTarget, NavMesh.AllAreas, reusablePath) &&
                    reusablePath.status == NavMeshPathStatus.PathComplete)
                {
                    canReachNew = true;
                    break;
                }
            }
        }

        return canReachNew;
    }

    // Shortest flat (XZ) distance between two line segments (AB and CD)
    private float SegmentToSegmentDistanceXZ(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Vector2 p1 = new Vector2(a.x, a.z);
        Vector2 q1 = new Vector2(b.x, b.z);
        Vector2 p2 = new Vector2(c.x, c.z);
        Vector2 q2 = new Vector2(d.x, d.z);

        if (SegmentsIntersect2D(p1, q1, p2, q2))
        {
            return 0f;
        }

        float d1 = PointToSegmentDistance2D(p1, p2, q2);
        float d2 = PointToSegmentDistance2D(q1, p2, q2);
        float d3 = PointToSegmentDistance2D(p2, p1, q1);
        float d4 = PointToSegmentDistance2D(q2, p1, q1);

        return Mathf.Min(Mathf.Min(d1, d2), Mathf.Min(d3, d4));
    }

    private float PointToSegmentDistanceXZ(Vector3 point, Vector3 segStart, Vector3 segEnd)
    {
        return PointToSegmentDistance2D(
            new Vector2(point.x, point.z),
            new Vector2(segStart.x, segStart.z),
            new Vector2(segEnd.x, segEnd.z)
        );
    }

    private float PointToSegmentDistance2D(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSqr = ab.sqrMagnitude;
        if (lenSqr < 0.00001f) return Vector2.Distance(p, a);

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSqr);
        Vector2 projection = a + t * ab;
        return Vector2.Distance(p, projection);
    }

    private bool SegmentsIntersect2D(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
    {
        float o1 = Orientation2D(p1, q1, p2);
        float o2 = Orientation2D(p1, q1, q2);
        float o3 = Orientation2D(p2, q2, p1);
        float o4 = Orientation2D(p2, q2, q1);

        return (o1 * o2 < 0f) && (o3 * o4 < 0f);
    }

    private float Orientation2D(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }

    private void PlaceTower()
    {
        if (CurrencyManager.Instance != null && !CurrencyManager.Instance.TrySpend(currentTowerCost))
        {
            CancelPlacement();
            return;
        }

        Vector3 finalPos = currentPreview.transform.position;
        Quaternion finalRot = Quaternion.Euler(0f, currentRotationY, 0f);

        GameObject placed = Instantiate(currentTowerPrefab, finalPos, finalRot);
        SetLayerRecursively(placed, LayerMaskToLayer(obstacleLayer));

        CancelPlacement();
    }

    private float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private void CalculateFootprintBounds(GameObject obj, out Vector3 extents, out Vector3 localCenterOffset)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            extents = new Vector3(0.5f, 1f, 0.5f);
            localCenterOffset = new Vector3(0f, 0.5f, 0f);
            return;
        }

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combined.Encapsulate(renderers[i].bounds);
        }

        extents = combined.extents;
        localCenterOffset = combined.center - obj.transform.position;
    }

    private void ApplyFeedbackMaterial(Material mat)
    {
        if (mat == null || previewRenderers == null) return;
        for (int i = 0; i < previewRenderers.Length; i++)
        {
            if (previewRenderers[i] != null) previewRenderers[i].material = mat;
        }
    }

    private Vector3 CalculatePlacementPosition(Vector3 rawHitPoint)
    {
        float y = rawHitPoint.y + calculatedOffset;
        if (!snapToGrid) return new Vector3(rawHitPoint.x, y, rawHitPoint.z);

        float x = Mathf.Round(rawHitPoint.x / gridSize) * gridSize;
        float z = Mathf.Round(rawHitPoint.z / gridSize) * gridSize;
        return new Vector3(x, y, z);
    }

    private float CalculateBottomOffset(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0f;

        float lowestY = float.MaxValue;
        foreach (var r in renderers)
        {
            if (r.bounds.min.y < lowestY) lowestY = r.bounds.min.y;
        }
        return obj.transform.position.y - lowestY;
    }

    private int LayerMaskToLayer(LayerMask mask)
    {
        int layerNumber = 0;
        int layer = mask.value;
        while (layer > 1)
        {
            layer >>= 1;
            layerNumber++;
        }
        return layerNumber;
    }

    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }

    public void CancelPlacement()
    {
        isPlacing = false;
        isCurrentSpotValid = false;
        rotationTween?.Kill();

        if (currentPreview != null)
        {
            Destroy(currentPreview);
            currentPreview = null;
        }
    }

    private void OnDestroy() => rotationTween?.Kill();

    private void OnDrawGizmos()
    {
        if (!isPlacing || currentPreview == null || !currentPreview.activeSelf) return;

        Quaternion rot = Quaternion.Euler(0f, currentRotationY, 0f);
        Vector3 candidatePos = currentPreview.transform.position;
        float floorY = candidatePos.y - calculatedOffset;

        Gizmos.color = isCurrentSpotValid ? Color.green : Color.red;

        Vector3 prevSpot = new Vector3(candidatePos.x, floorY + queueCorridorRadius, candidatePos.z);
        for (int i = 0; i < unrotatedWorldOffsets.Count; i++)
        {
            Vector3 rawSpot = candidatePos + (rot * unrotatedWorldOffsets[i]);
            Vector3 spherePos = new Vector3(rawSpot.x, floorY + queueCorridorRadius, rawSpot.z);
            Gizmos.DrawWireSphere(spherePos, queueCorridorRadius);
            Gizmos.DrawLine(prevSpot, spherePos);
            prevSpot = spherePos;
        }
    }
}