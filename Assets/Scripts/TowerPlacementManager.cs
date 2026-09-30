using UnityEngine;
using UnityEngine.InputSystem;

public class TowerPlacementManager : MonoBehaviour
{
    [Header("Placement Settings")]
    [SerializeField] private GameObject towerPrefab;
    [SerializeField] private LayerMask placementLayer;
    [SerializeField] private bool snapToGrid = true;
    [SerializeField] private float gridSize = 1f;

    [Header("Rotation Settings")]
    [Tooltip("Degrees to rotate each time 'R' is pressed (e.g., 90 for orthogonal, 45 for diagonal).")]
    [SerializeField] private float rotationStep = 90f;

    [Header("Height Adjustment")]
    [Tooltip("Automatically calculates the vertical offset using the tower's mesh bounds so it sits flush on top of the surface.")]
    [SerializeField] private bool autoCalculateHeight = true;
    [Tooltip("Manual vertical offset added to hit.point.y if autoCalculateHeight is disabled or needs fine-tuning.")]
    [SerializeField] private float heightOffset = 0f;

    [Header("Preview Settings")]
    [SerializeField] private Material previewMaterial;

    private Camera mainCamera;
    private GameObject currentPreview;
    private bool isPlacing = false;
    private float calculatedOffset = 0f;
    private float currentRotationY = 0f;

    private void Awake()
    {
        mainCamera = Camera.main;
    }

    private void Update()
    {
        if (Keyboard.current == null || Mouse.current == null) return;

        // Toggle placement mode with 'B'
        if (Keyboard.current.bKey.wasPressedThisFrame && !isPlacing && towerPrefab != null)
        {
            StartPlacement(towerPrefab);
        }

        if (!isPlacing) return;

        // Rotate clockwise on R press
        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            RotatePreview();
        }

        UpdatePreviewPosition();

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            PlaceTower();
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
        currentRotationY = 0f; // Reset rotation for newly selected tower

        currentPreview = Instantiate(towerPrefab, Vector3.zero, Quaternion.identity);

        if (autoCalculateHeight)
        {
            calculatedOffset = CalculateBottomOffset(currentPreview) + heightOffset;
        }
        else
        {
            calculatedOffset = heightOffset;
        }

        // Apply initial rotation
        currentPreview.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);

        // Disable colliders so preview does not block raycasting
        Collider[] colliders = currentPreview.GetComponentsInChildren<Collider>();
        foreach (var col in colliders)
        {
            col.enabled = false;
        }

        // Apply preview material if provided
        if (previewMaterial != null)
        {
            Renderer[] renderers = currentPreview.GetComponentsInChildren<Renderer>();
            foreach (var rend in renderers)
            {
                rend.material = previewMaterial;
            }
        }
    }

    private void RotatePreview()
    {
        currentRotationY = (currentRotationY + rotationStep) % 360f;

        if (currentPreview != null)
        {
            currentPreview.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);
        }
    }

    private void UpdatePreviewPosition()
    {
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, placementLayer))
        {
            currentPreview.SetActive(true);
            currentPreview.transform.position = CalculatePlacementPosition(hit.point);
        }
        else
        {
            currentPreview.SetActive(false);
        }
    }

    private void PlaceTower()
    {
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, placementLayer))
        {
            Vector3 finalPos = CalculatePlacementPosition(hit.point);
            Quaternion finalRot = Quaternion.Euler(0f, currentRotationY, 0f);

            // Spawns the tower with the matching rotation
            Instantiate(towerPrefab, finalPos, finalRot);

            CancelPlacement();
        }
    }

    private Vector3 CalculatePlacementPosition(Vector3 rawHitPoint)
    {
        float y = rawHitPoint.y + calculatedOffset;

        if (!snapToGrid)
        {
            return new Vector3(rawHitPoint.x, y, rawHitPoint.z);
        }

        float x = Mathf.Round(rawHitPoint.x / gridSize) * gridSize;
        float z = Mathf.Round(rawHitPoint.z / gridSize) * gridSize;

        return new Vector3(x, y, z);
    }

    private float CalculateBottomOffset(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0f;

        float lowestPointY = float.MaxValue;
        foreach (Renderer rend in renderers)
        {
            if (rend.bounds.min.y < lowestPointY)
            {
                lowestPointY = rend.bounds.min.y;
            }
        }

        return obj.transform.position.y - lowestPointY;
    }

    public void CancelPlacement()
    {
        isPlacing = false;
        if (currentPreview != null)
        {
            Destroy(currentPreview);
            currentPreview = null;
        }
    }
}