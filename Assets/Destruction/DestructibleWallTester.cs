using UnityEngine;

public class DestructibleWallTester : MonoBehaviour
{
    public enum DamageMode
    {
        Circle,
        Box,
        Capsule
    }

    [Header("Scene")]
    [SerializeField] private Camera cam;
    [SerializeField] private DestructibleWall2DRectFramework wall;
    [SerializeField, Min(0.1f)] private float maxDistance = 100f;
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("Stamp")]
    [SerializeField] private DamageMode defaultMode;
    [SerializeField] private bool stampValuesAreWorldUnits = true;
    [SerializeField, Min(0.001f)] private float circleRadius = 0.25f;
    [SerializeField] private Vector2 boxSize = new Vector2(0.5f, 0.5f);
    [SerializeField] private float boxAngleDeg;
    [SerializeField, Min(0.001f)] private float capsuleRadius = 0.18f;
    [SerializeField, Min(0.001f)] private float capsuleLength = 0.9f;
    [SerializeField] private float capsuleAngleDeg;

    private void Reset()
    {
        cam = Camera.main;
        wall = FindFirstObjectByType<DestructibleWall2DRectFramework>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F2))
        {
            WallDemoScenes.OpenPrefab();
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Application.Quit();
            return;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            wall.ClearDamage();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Alpha1))
            defaultMode = DamageMode.Circle;
        else if (Input.GetKeyDown(KeyCode.Alpha2))
            defaultMode = DamageMode.Box;
        else if (Input.GetKeyDown(KeyCode.Alpha3))
            defaultMode = DamageMode.Capsule;

        if (Input.GetMouseButtonDown(0))
            TryApplyDamage(DamageMode.Circle, Input.mousePosition);
        else if (Input.GetMouseButtonDown(1))
            TryApplyDamage(DamageMode.Box, Input.mousePosition);
        else if (Input.GetMouseButtonDown(2))
            TryApplyDamage(DamageMode.Capsule, Input.mousePosition);
        else if (Input.GetKeyDown(KeyCode.Space))
            TryApplyDamage(defaultMode, Input.mousePosition);
    }

    public bool TryApplyDamage(DamageMode mode, Vector2 screenPosition)
    {
        Ray ray = cam.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore))
            return false;
        if (hit.collider.GetComponentInParent<WallDetachedDebris>() != null)
            return false;
        if (hit.collider.GetComponentInParent<DestructibleWall2DRectFramework>() != wall)
            return false;

        Vector2 local = wall.WorldToDamageLocal2D(hit.point);
        switch (mode)
        {
            case DamageMode.Circle:
                if (stampValuesAreWorldUnits)
                    wall.ApplyCircleDamageWorld(hit.point, circleRadius);
                else
                    wall.ApplyCircleDamageLocal(local, circleRadius);
                return true;
            case DamageMode.Box:
                if (stampValuesAreWorldUnits)
                    wall.ApplyBoxDamageWorld(hit.point, boxSize, boxAngleDeg);
                else
                    wall.ApplyBoxDamageLocal(local, boxSize, boxAngleDeg);
                return true;
            case DamageMode.Capsule:
                if (stampValuesAreWorldUnits)
                    wall.ApplyCapsuleDamageWorld(hit.point, capsuleRadius, capsuleLength, capsuleAngleDeg);
                else
                    wall.ApplyCapsuleDamageLocal(local, capsuleRadius, capsuleLength, capsuleAngleDeg);
                return true;
            default:
                return false;
        }
    }
}
