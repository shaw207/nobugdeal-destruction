using UnityEngine;

public sealed class PrefabWallTester : MonoBehaviour
{
    [SerializeField] private Camera cam;
    [SerializeField] private BreakableChunkWall wall;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            WallDemoScenes.OpenMesh();
            return;
        }
        if (Input.GetKeyDown(KeyCode.R))
        {
            WallDemoScenes.RestartPrefab();
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Application.Quit();
            return;
        }
        if (Input.GetMouseButtonDown(0))
            TryHit(Input.mousePosition, false);
        else if (Input.GetMouseButtonDown(1))
            TryHit(Input.mousePosition, true);
    }

    public bool TryHit(Vector2 screenPosition, bool area)
    {
        Ray ray = cam.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, cam.farClipPlane, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;

        BreakableChunk piece = hit.collider.GetComponent<BreakableChunk>();
        if (piece == null || piece.IsBroken || piece.GetComponentInParent<BreakableChunkWall>() != wall)
            return false;

        return wall.ApplyImpact(hit.point, ray.direction, hit.normal, area, piece);
    }
}
