using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using DG.Tweening;

public class TowerPlacementManager : MonoBehaviour
{
    [Header("Placement Settings")]
    [SerializeField] private GameObject towerPrefab;
    [SerializeField] private LayerMask placementLayer; // Ground layer
    [Tooltip("Layer assigned to placed machines, walls, or obstacles to prevent overlap.")]
    [SerializeField] private LayerMask obstacleLayer;
    [SerializeField] private bool snapToGrid = true;
    [SerializeField] private float gridSize = 1f;

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
    private GameObject currentPreview;
    private Renderer[] previewRenderers;
    private Vector3 localInteractionOffset;
    private Vector3 machineExtents; // Half-size of the machine's footprint

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
        if (Keyboard.current == null || Mouse.current == null) return;

        if (Keyboard.current.bKey.wasPressedThisFrame && !isPlacing && towerPrefab != null)
        {
            StartPlacement(towerPrefab);
        }

        if (!isPlacing) return;

        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            RotatePreview();
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
                Debug.LogWarning("[Placement] Invalid placement: Spot blocked or isolates machines.");
            }
        }
        else if (Mouse.current.rightButton.wasPressedThisFrame || Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CancelPlacement();
        }
    }

    public void StartPlacement(GameObject newTowerPrefab)
    {
        CancelPlacement();

        towerPrefab = newTowerPrefab;
        isPlacing = true;
        currentRotationY = 0f;

        currentPreview = Instantiate(towerPrefab, Vector3.zero, Quaternion.identity);

        // Calculate machine footprint extents for overlap checks
        machineExtents = CalculateFootprintExtents(currentPreview);

        if (currentPreview.TryGetComponent<TestMachine>(out var machine) && machine.InteractionPoint != null)
        {
            localInteractionOffset = currentPreview.transform.InverseTransformPoint(machine.InteractionPoint.position);
        }
        else
        {
            localInteractionOffset = Vector3.forward * 0.8f;
        }

        // Disable colliders and obstacles on preview
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

            // Full check: Overlap + Reachability + Global Network Integrity
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

        // 1. Check physical overlap against existing machines and obstacles
        Vector3 checkCenter = candidatePos + Vector3.up * (machineExtents.y * 0.5f);
        Vector3 checkHalfExtents = machineExtents * 0.95f; // Slight tolerance buffer

        if (Physics.CheckBox(checkCenter, checkHalfExtents, rot, obstacleLayer, QueryTriggerInteraction.Ignore))
        {
            return false; // Intersects an already placed machine or barrier
        }

        // 2. Validate the interaction point lands on valid, walkable NavMesh
        Vector3 candidateInteractionPos = candidatePos + (rot * localInteractionOffset);
        if (!NavMesh.SamplePosition(candidateInteractionPos, out NavMeshHit targetHit, 0.6f, NavMesh.AllAreas))
        {
            return false; // Interaction spot is off the mesh or inside a carved hole
        }

        // Ensure interaction point doesn't physically clip into an existing machine
        if (Physics.CheckSphere(candidateInteractionPos + Vector3.up * 0.5f, 0.3f, obstacleLayer, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (CasinoEntrance.AllEntrances.Count == 0) return true;

        // Bounding box for checking if paths cut through the unplaced machine
        Bounds candidateBounds = new Bounds(candidatePos + Vector3.up * machineExtents.y, machineExtents * 2f);

        // 3. Ensure at least one entrance can reach the new machine without passing through its own box
        bool canReachNew = false;
        foreach (var entrance in CasinoEntrance.AllEntrances)
        {
            if (entrance == null) continue;

            if (NavMesh.SamplePosition(entrance.SpawnPoint.position, out NavMeshHit startHit, 2.0f, NavMesh.AllAreas))
            {
                if (NavMesh.CalculatePath(startHit.position, targetHit.position, NavMesh.AllAreas, reusablePath))
                {
                    if (reusablePath.status == NavMeshPathStatus.PathComplete && !PathIntersectsBounds(reusablePath, candidateBounds))
                    {
                        canReachNew = true;
                        break;
                    }
                }
            }
        }

        if (!canReachNew) return false;

        // 4. Ensure ALL existing machines remain reachable (no maze walling)
        IReadOnlyList<IEnemyInteractable> existingTargets = TargetRegistry.AllTargets;
        for (int i = 0; i < existingTargets.Count; i++)
        {
            var existing = existingTargets[i];
            if (existing == null || !existing.IsTargetValid) continue;

            Vector3 existingSpot = existing.InteractionPoint.position;
            bool targetStillReachable = false;

            foreach (var entrance in CasinoEntrance.AllEntrances)
            {
                if (entrance == null) continue;

                if (NavMesh.SamplePosition(entrance.SpawnPoint.position, out NavMeshHit startHit, 2.0f, NavMesh.AllAreas))
                {
                    if (NavMesh.CalculatePath(startHit.position, existingSpot, NavMesh.AllAreas, reusablePath))
                    {
                        // Path must be complete AND not rely on walking through this candidate machine
                        if (reusablePath.status == NavMeshPathStatus.PathComplete && !PathIntersectsBounds(reusablePath, candidateBounds))
                        {
                            targetStillReachable = true;
                            break;
                        }
                    }
                }
            }

            if (!targetStillReachable)
            {
                return false; // Placing here blocks access to an already placed machine
            }
        }

        return true;
    }

    private bool PathIntersectsBounds(NavMeshPath path, Bounds bounds)
    {
        Vector3[] corners = path.corners;
        for (int i = 0; i < corners.Length - 1; i++)
        {
            Vector3 start = corners[i];
            Vector3 end = corners[i + 1];
            Vector3 dir = end - start;
            float dist = dir.magnitude;

            if (dist > 0.001f)
            {
                Ray ray = new Ray(start, dir.normalized);
                if (bounds.IntersectRay(ray, out float hitDist) && hitDist <= dist)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private void PlaceTower()
    {
        Vector3 finalPos = currentPreview.transform.position;
        Quaternion finalRot = Quaternion.Euler(0f, currentRotationY, 0f);

        GameObject placed = Instantiate(towerPrefab, finalPos, finalRot);

        // Ensure placed machine colliders are on the obstacleLayer so subsequent placements detect it
        SetLayerRecursively(placed, LayerMaskToLayer(obstacleLayer));

        CancelPlacement();
    }

    private Vector3 CalculateFootprintExtents(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Vector3(0.5f, 1f, 0.5f);

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combined.Encapsulate(renderers[i].bounds);
        }
        return combined.extents;
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
}