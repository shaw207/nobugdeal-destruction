using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Crea los fragmentos fisicos que salen de una pared destructible.
 * Recibe datos de una region desprendida, genera su mesh y colliders, copia
 * materiales, configura rigidbody y aplica un impulso inicial para vender el
 * efecto de desprendimiento.
 */
public static class WallDebrisSpawner
{
    /*
     * Construye un objeto de debris completo a partir de una region separada.
     * Decide si usar geometria exacta o una caja de respaldo, configura fisica
     * y registra el fragmento activo para que la pared pueda controlarlo.
     */
    public static bool SpawnDebrisFromRegion(
        DebrisBuildData data,
        DebrisRegionInfo info,
        Transform wallTransform,
        Transform debrisRoot,
        MeshRenderer sourceRenderer,
        WallMaterialGrid grid,
        Vector3 localWallCenter,
        Vector3 localWallSize,
        WallFrontAxis frontAxis,
        bool spawnExactRegionVisualDebris,
        bool useBoundingBoxFallbackForTinyDebris,
        int exactDebrisMinCells,
        bool useMultiBoxCollidersForExactDebris,
        bool mergeVerticalRuns,
        PhysicsMaterial collisionMaterial,
        PhysicsMaterial debrisPhysicsMaterial,
        float debrisLifetime,
        float debrisSpawnImpulse,
        float debrisUpwardImpulse,
        float debrisMassPerCell,
        int layer,
        List<WallDetachedDebris> activeDebris)
    {
        bool exactVisual = spawnExactRegionVisualDebris &&
                           (!useBoundingBoxFallbackForTinyDebris || data.cellCount >= exactDebrisMinCells);

        GameObject go = new GameObject($"WallDebris_{data.sourceRegionId}");
        go.layer = layer;

        Transform t = go.transform;

        WallAxisUtility.GetBasis(
            frontAxis,
            out int uAxis,
            out int vAxis,
            out int nAxis,
            out Vector3 uDir,
            out Vector3 vDir,
            out Vector3 nDir);

        Vector3 localPos = info.hasResolvedLocal3D
            ? info.localCenter3D
            : localWallCenter + uDir * info.localCenter.x + vDir * info.localCenter.y;

        Vector3 worldPos = wallTransform.TransformPoint(localPos);
        Quaternion worldRot = wallTransform.rotation;

        bool parentedToDebrisRoot = CanUseDebrisParent(debrisRoot);
        if (parentedToDebrisRoot)
        {
            t.SetParent(debrisRoot, false);
            t.localPosition = debrisRoot.InverseTransformPoint(worldPos);
            t.localRotation = Quaternion.Inverse(debrisRoot.rotation) * worldRot;
            t.localScale = Vector3.one;
        }
        else
        {
            t.SetPositionAndRotation(worldPos, worldRot);
            t.localScale = wallTransform.lossyScale;
        }

        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        if (sourceRenderer != null)
            mr.sharedMaterials = sourceRenderer.sharedMaterials;

        float thickness = ResolveWallThickness(localWallSize, frontAxis);

        Mesh mesh = exactVisual
            ? WallMeshBuilder.BuildExactDebrisMesh(
                data,
                grid,
                frontAxis,
                thickness,
                localWallCenter,
                wallTransform,
                worldPos,
                worldRot)
            : WallMeshBuilder.BuildBoundsDebrisMesh(
                info.localSize,
                frontAxis,
                localWallCenter,
                wallTransform,
                worldPos,
                worldRot);

        mesh.indexFormat = IndexFormat.UInt32;
        mf.sharedMesh = mesh;

        if (exactVisual && useMultiBoxCollidersForExactDebris)
            AddExactDebrisBoxColliders(go, data, grid, localWallSize, frontAxis, mergeVerticalRuns, collisionMaterial, debrisPhysicsMaterial);
        else
        {
            Vector3 sizeForCollider = info.hasResolvedLocal3D
                ? info.localSize3D
                : WallAxisUtility.ComposeVector(uAxis, info.localSize.x, vAxis, info.localSize.y, nAxis, info.localSize.z);

            AddBoundsDebrisBoxCollider(go, sizeForCollider, frontAxis, collisionMaterial, debrisPhysicsMaterial, true);
        }

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.mass = Mathf.Max(0.05f, info.cellCount * debrisMassPerCell);

        Vector3 pushDir = wallTransform.TransformDirection(nDir).normalized;
        Vector3 impulse = pushDir * debrisSpawnImpulse + wallTransform.up * debrisUpwardImpulse;
        rb.AddForce(impulse, ForceMode.Impulse);

        WallDetachedDebris debris = go.AddComponent<WallDetachedDebris>();
        debris.Initialize(debrisLifetime, mesh);
        activeDebris.Add(debris);

        return true;
    }

    private static bool CanUseDebrisParent(Transform debrisRoot)
    {
        if (debrisRoot == null)
            return false;

        Vector3 s = debrisRoot.lossyScale;
        return Mathf.Abs(s.x - 1f) <= 0.01f &&
               Mathf.Abs(s.y - 1f) <= 0.01f &&
               Mathf.Abs(s.z - 1f) <= 0.01f;
    }

    public static void AddBoundsDebrisBoxCollider(
        GameObject go,
        Vector3 localSize,
        WallFrontAxis frontAxis,
        PhysicsMaterial collisionMaterial,
        PhysicsMaterial debrisPhysicsMaterial,
        bool sizeIsResolvedLocal3D = false)
    {
        BoxCollider bc = go.AddComponent<BoxCollider>();

        if (sizeIsResolvedLocal3D)
        {
            bc.size = localSize;
        }
        else
        {
            WallAxisUtility.GetBasis(
                frontAxis,
                out int uAxis,
                out int vAxis,
                out int nAxis,
                out _,
                out _,
                out _);

            bc.size = WallAxisUtility.ComposeVector(uAxis, localSize.x, vAxis, localSize.y, nAxis, localSize.z);
        }

        bc.sharedMaterial = debrisPhysicsMaterial != null ? debrisPhysicsMaterial : collisionMaterial;
    }

    /*
     * Genera varios colliders de caja para un debris con forma exacta.
     * Agrupa celdas solidas en rectangulos para aproximar la colision sin crear
     * un collider por cada celda individual.
     */
    public static void AddExactDebrisBoxColliders(
        GameObject go,
        DebrisBuildData data,
        WallMaterialGrid grid,
        Vector3 localWallSize,
        WallFrontAxis frontAxis,
        bool mergeVerticalRuns,
        PhysicsMaterial collisionMaterial,
        PhysicsMaterial debrisPhysicsMaterial)
    {
        bool[,] regionSolid = BuildRegionSolidGrid(data);
        if (regionSolid == null)
        {
            AddBoundsDebrisBoxCollider(go, data.localSize, frontAxis, collisionMaterial, debrisPhysicsMaterial);
            return;
        }

        List<RectIntRun> rects = WallCollisionBuilder.CollectRectsFromSolidMask(regionSolid, mergeVerticalRuns);
        if (rects.Count == 0)
        {
            AddBoundsDebrisBoxCollider(go, data.localSize, frontAxis, collisionMaterial, debrisPhysicsMaterial);
            return;
        }

        WallAxisUtility.GetBasis(
            frontAxis,
            out int uAxis,
            out int vAxis,
            out int nAxis,
            out Vector3 uDir,
            out Vector3 vDir,
            out _);

        float thickness = WallAxisUtility.GetAxis(localWallSize, nAxis);
        float cellW = grid.CellWidth;
        float cellH = grid.CellHeight;
        Vector2 regionOrigin = new Vector2(grid.CellMinX(data.cellBounds.xMin), grid.CellMinY(data.cellBounds.yMin))
            - data.localCenter;

        for (int i = 0; i < rects.Count; i++)
        {
            RectIntRun rect = rects[i];
            BoxCollider bc = go.AddComponent<BoxCollider>();

            float width = (rect.xMax - rect.xMin + 1) * cellW;
            float height = (rect.yMax - rect.yMin + 1) * cellH;

            float centerX = (rect.xMin + rect.xMax + 1) * 0.5f * cellW;
            float centerY = (rect.yMin + rect.yMax + 1) * 0.5f * cellH;

            bc.center = uDir * (regionOrigin.x + centerX) + vDir * (regionOrigin.y + centerY);

            Vector3 size = Vector3.zero;
            WallAxisUtility.SetAxis(ref size, uAxis, width);
            WallAxisUtility.SetAxis(ref size, vAxis, height);
            WallAxisUtility.SetAxis(ref size, nAxis, thickness);
            bc.size = size;

            bc.sharedMaterial = debrisPhysicsMaterial != null ? debrisPhysicsMaterial : collisionMaterial;
        }
    }

    private static bool[,] BuildRegionSolidGrid(DebrisBuildData data)
    {
        if (data == null || data.cells == null || data.cells.Count == 0)
            return null;

        int width = data.cellBounds.width;
        int height = data.cellBounds.height;
        if (width <= 0 || height <= 0)
            return null;

        bool[,] solid = new bool[width, height];

        for (int i = 0; i < data.cells.Count; i++)
        {
            Vector2Int cell = data.cells[i];
            int x = cell.x - data.cellBounds.xMin;
            int y = cell.y - data.cellBounds.yMin;

            if (x < 0 || x >= width || y < 0 || y >= height)
                continue;

            solid[x, y] = true;
        }

        return solid;
    }

    private static float ResolveWallThickness(Vector3 localWallSize, WallFrontAxis frontAxis)
    {
        WallAxisUtility.GetBasis(frontAxis, out _, out _, out int nAxis, out _, out _, out _);
        return Mathf.Max(0.01f, WallAxisUtility.GetAxis(localWallSize, nAxis));
    }
}
