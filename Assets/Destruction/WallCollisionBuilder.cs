using System.Collections.Generic;
using UnityEngine;

/*
 * Convierte celdas solidas de una pared destructible en colliders rectangulares.
 * Su objetivo es mantener una colision razonable despues del dano sin crear un
 * collider por celda, usando agrupacion de rectangulos y reutilizacion de cajas.
 */
public static class WallCollisionBuilder
{
    /*
     * Lee el grid completo y agrupa las celdas solidas en tramos horizontales.
     * Puede fusionar tramos verticales para reducir la cantidad final de
     * colliders necesarios en la pared.
     */
    public static List<RectIntRun> CollectRectsFromSolidGrid(WallMaterialGrid grid, bool mergeVerticalRuns)
    {
        List<RectIntRun> rowRuns = new List<RectIntRun>();
        bool[,] solid = grid.Solid;

        for (int y = 0; y < grid.Height; y++)
        {
            int x = 0;
            while (x < grid.Width)
            {
                if (!solid[x, y])
                {
                    x++;
                    continue;
                }

                int startX = x;
                while (x < grid.Width && solid[x, y])
                    x++;

                rowRuns.Add(new RectIntRun(startX, x - 1, y, y));
            }
        }

        return mergeVerticalRuns ? MergeVerticalRuns(rowRuns) : rowRuns;
    }

    /*
     * Agrupa una lista concreta de celdas en rectangulos de colision.
     * Se usa cuando el sistema ya conoce una region especifica, por ejemplo
     * para generar colliders de un fragmento de debris.
     */
    public static List<RectIntRun> CollectRectsFromCells(List<Vector2Int> cells, WallMaterialGrid grid, bool mergeVerticalRuns)
    {
        List<RectIntRun> rowRuns = new List<RectIntRun>();
        if (cells == null || cells.Count == 0)
            return rowRuns;

        bool[,] mask = new bool[grid.Width, grid.Height];
        for (int i = 0; i < cells.Count; i++)
            mask[cells[i].x, cells[i].y] = true;

        for (int y = 0; y < grid.Height; y++)
        {
            int x = 0;
            while (x < grid.Width)
            {
                if (!mask[x, y])
                {
                    x++;
                    continue;
                }

                int startX = x;
                while (x < grid.Width && mask[x, y])
                    x++;

                rowRuns.Add(new RectIntRun(startX, x - 1, y, y));
            }
        }

        return mergeVerticalRuns ? MergeVerticalRuns(rowRuns) : rowRuns;
    }

    /*
     * Agrupa una mascara booleana independiente en rectangulos.
     * Permite reutilizar la misma logica de colision para grids temporales que
     * no pertenecen directamente a WallMaterialGrid.
     */
    public static List<RectIntRun> CollectRectsFromSolidMask(bool[,] solidMask, bool mergeVerticalRuns)
    {
        List<RectIntRun> rowRuns = new List<RectIntRun>();
        if (solidMask == null)
            return rowRuns;

        int width = solidMask.GetLength(0);
        int height = solidMask.GetLength(1);

        for (int y = 0; y < height; y++)
        {
            int x = 0;
            while (x < width)
            {
                if (!solidMask[x, y])
                {
                    x++;
                    continue;
                }

                int startX = x;
                while (x < width && solidMask[x, y])
                    x++;

                rowRuns.Add(new RectIntRun(startX, x - 1, y, y));
            }
        }

        return mergeVerticalRuns ? MergeVerticalRuns(rowRuns) : rowRuns;
    }

    /*
     * Reconstruye los BoxCollider activos usando un pool existente.
     * Cada rectangulo del grid se transforma al espacio local de la pared para
     * que la colision coincida con la malla visible despues del dano.
     */
    public static void RebuildPooledBoxColliders(
        Transform collisionRoot,
        List<BoxCollider> pooledColliders,
        List<BoxCollider> activeColliders,
        List<RectIntRun> rects,
        WallMaterialGrid grid,
        Vector3 localWallCenter,
        Vector3 localWallSize,
        WallFrontAxis frontAxis,
        PhysicsMaterial material,
        int layer)
    {
        DeactivateAll(activeColliders);

        WallAxisUtility.GetBasis(
            frontAxis,
            out int uAxis,
            out int vAxis,
            out int nAxis,
            out Vector3 uDir,
            out Vector3 vDir,
            out _);

        float halfW = WallAxisUtility.GetAxis(localWallSize, uAxis) * 0.5f;
        float halfH = WallAxisUtility.GetAxis(localWallSize, vAxis) * 0.5f;
        float thickness = WallAxisUtility.GetAxis(localWallSize, nAxis);
        float cellW = grid.CellWidth;
        float cellH = grid.CellHeight;

        for (int i = 0; i < rects.Count; i++)
        {
            RectIntRun rect = rects[i];
            BoxCollider bc = GetPooledCollider(collisionRoot, pooledColliders, i);

            float width = (rect.xMax - rect.xMin + 1) * cellW;
            float height = (rect.yMax - rect.yMin + 1) * cellH;
            float centerX = -halfW + (rect.xMin + rect.xMax + 1) * 0.5f * cellW;
            float centerY = -halfH + (rect.yMin + rect.yMax + 1) * 0.5f * cellH;

            Vector3 localOffset = uDir * centerX + vDir * centerY;

            Vector3 boxSize = Vector3.zero;
            WallAxisUtility.SetAxis(ref boxSize, uAxis, width);
            WallAxisUtility.SetAxis(ref boxSize, vAxis, height);
            WallAxisUtility.SetAxis(ref boxSize, nAxis, thickness);

            Transform t = bc.transform;
            t.SetParent(collisionRoot, false);
            t.localPosition = localWallCenter + localOffset;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            t.name = $"Col_{i}";

            bc.size = boxSize;
            bc.sharedMaterial = material;
            bc.enabled = true;
            bc.gameObject.layer = layer;
            bc.gameObject.SetActive(true);

            activeColliders.Add(bc);
        }
    }

    public static void DeactivateAll(List<BoxCollider> activeColliders)
    {
        for (int i = 0; i < activeColliders.Count; i++)
        {
            BoxCollider bc = activeColliders[i];
            if (bc == null)
                continue;

            bc.enabled = false;
            bc.gameObject.SetActive(false);
        }

        activeColliders.Clear();
    }

    public static BoxCollider GetPooledCollider(Transform collisionRoot, List<BoxCollider> pooledColliders, int index)
    {
        if (index < pooledColliders.Count && pooledColliders[index] != null)
            return pooledColliders[index];

        GameObject go = new GameObject($"Col_{index}");
        go.transform.SetParent(collisionRoot, false);
        go.SetActive(false);
        BoxCollider bc = go.AddComponent<BoxCollider>();

        while (pooledColliders.Count <= index)
            pooledColliders.Add(null);

        pooledColliders[index] = bc;
        return bc;
    }

    /*
     * Fusiona tramos horizontales apilados que tienen el mismo ancho.
     * Esta optimizacion reduce el numero de colliders y mantiene la fisica mas
     * barata sin cambiar la forma general del area solida.
     */
    private static List<RectIntRun> MergeVerticalRuns(List<RectIntRun> rowRuns)
    {
        List<RectIntRun> merged = new List<RectIntRun>();
        bool[] used = new bool[rowRuns.Count];

        for (int i = 0; i < rowRuns.Count; i++)
        {
            if (used[i])
                continue;

            RectIntRun baseRun = rowRuns[i];
            int yMin = baseRun.yMin;
            int yMax = baseRun.yMax;
            used[i] = true;

            bool extended = true;
            while (extended)
            {
                extended = false;

                for (int j = 0; j < rowRuns.Count; j++)
                {
                    if (used[j])
                        continue;

                    RectIntRun candidate = rowRuns[j];
                    if (candidate.xMin != baseRun.xMin || candidate.xMax != baseRun.xMax)
                        continue;

                    if (candidate.yMin == yMax + 1)
                    {
                        yMax = candidate.yMax;
                        used[j] = true;
                        extended = true;
                    }
                }
            }

            merged.Add(new RectIntRun(baseRun.xMin, baseRun.xMax, yMin, yMax));
        }

        return merged;
    }
}
