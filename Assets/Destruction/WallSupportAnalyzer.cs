using System.Collections.Generic;
using UnityEngine;

/*
 * Analiza la estabilidad de una pared destructible representada como grid.
 * Separa las celdas que siguen conectadas a los bordes de soporte de las
 * regiones que ya quedaron sueltas, para que otros sistemas puedan convertirlas
 * en debris fisico o mostrar informacion de debug.
 */
public sealed class WallSupportAnalyzer
{
    private readonly Queue<CellPos> _queue = new Queue<CellPos>(2048);

    private static readonly int[] FourDirX = { 1, -1, 0, 0 };
    private static readonly int[] FourDirY = { 0, 0, 1, -1 };
    private static readonly int[] EightDirX = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] EightDirY = { 0, 0, 1, -1, 1, -1, 1, -1 };

    /*
     * Ejecuta el analisis completo de soporte sobre el grid actual.
     * Primero marca las celdas conectadas a los bordes validos y despues busca
     * islas solidas no soportadas para registrar sus estadisticas.
     */
    public void Analyze(
        WallMaterialGrid grid,
        SupportEdgeMask supportEdges,
        bool useFourWayConnectivity,
        int largeDetachedChunkMinCells,
        bool[,] supportedMask,
        bool[,] detachedMask,
        List<DetachedRegionInfo> detachedRegions,
        out int supportedCellCount,
        out int detachedCellCount,
        out int detachedRegionCount,
        out int largeDetachedRegionCount)
    {
        ClearBoolGrid(supportedMask);
        ClearBoolGrid(detachedMask);
        detachedRegions.Clear();
        _queue.Clear();

        bool[,] solid = grid.Solid;
        int width = grid.Width;
        int height = grid.Height;

        SeedSupportQueue(solid, width, height, supportEdges, supportedMask);

        while (_queue.Count > 0)
        {
            CellPos c = _queue.Dequeue();
            EnqueueNeighborsIfSupported(c.x, c.y, solid, width, height, supportedMask, useFourWayConnectivity);
        }

        supportedCellCount = 0;
        detachedCellCount = 0;
        detachedRegionCount = 0;
        largeDetachedRegionCount = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!solid[x, y])
                    continue;

                if (supportedMask[x, y])
                {
                    supportedCellCount++;
                    continue;
                }

                if (detachedMask[x, y])
                    continue;

                DetachedRegionInfo info = FloodDetachedRegion(
                    startX: x,
                    startY: y,
                    solid: solid,
                    width: width,
                    height: height,
                    useFourWayConnectivity: useFourWayConnectivity,
                    grid: grid,
                    regionId: detachedRegionCount + 1,
                    detachedMask: detachedMask,
                    supportedMask: supportedMask,
                    largeDetachedChunkMinCells: largeDetachedChunkMinCells);

                detachedRegions.Add(info);
                detachedRegionCount++;
                detachedCellCount += info.cellCount;
                if (info.qualifiesAsLargeChunk)
                    largeDetachedRegionCount++;
            }
        }
    }

    private void SeedSupportQueue(bool[,] solid, int width, int height, SupportEdgeMask supportEdges, bool[,] supportedMask)
    {
        if ((supportEdges & SupportEdgeMask.Bottom) != 0)
        {
            int y = 0;
            for (int x = 0; x < width; x++)
                TryMarkSupportedSeed(x, y, solid, supportedMask);
        }

        if ((supportEdges & SupportEdgeMask.Top) != 0)
        {
            int y = height - 1;
            for (int x = 0; x < width; x++)
                TryMarkSupportedSeed(x, y, solid, supportedMask);
        }

        if ((supportEdges & SupportEdgeMask.Left) != 0)
        {
            int x = 0;
            for (int y = 0; y < height; y++)
                TryMarkSupportedSeed(x, y, solid, supportedMask);
        }

        if ((supportEdges & SupportEdgeMask.Right) != 0)
        {
            int x = width - 1;
            for (int y = 0; y < height; y++)
                TryMarkSupportedSeed(x, y, solid, supportedMask);
        }
    }

    private void TryMarkSupportedSeed(int x, int y, bool[,] solid, bool[,] supportedMask)
    {
        if (!solid[x, y] || supportedMask[x, y])
            return;

        supportedMask[x, y] = true;
        _queue.Enqueue(new CellPos(x, y));
    }

    private void EnqueueNeighborsIfSupported(
        int x,
        int y,
        bool[,] solid,
        int width,
        int height,
        bool[,] visited,
        bool useFourWayConnectivity)
    {
        int[] dx = useFourWayConnectivity ? FourDirX : EightDirX;
        int[] dy = useFourWayConnectivity ? FourDirY : EightDirY;
        int count = useFourWayConnectivity ? 4 : 8;

        for (int i = 0; i < count; i++)
        {
            int nx = x + dx[i];
            int ny = y + dy[i];
            if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                continue;
            if (!solid[nx, ny] || visited[nx, ny])
                continue;

            visited[nx, ny] = true;
            _queue.Enqueue(new CellPos(nx, ny));
        }
    }

    /*
     * Recorre una isla de celdas solidas que no tiene soporte.
     * Devuelve sus limites, centro y tamano para que la pared pueda decidir si
     * esa region se convierte en un fragmento desprendido.
     */
    private DetachedRegionInfo FloodDetachedRegion(
        int startX,
        int startY,
        bool[,] solid,
        int width,
        int height,
        bool useFourWayConnectivity,
        WallMaterialGrid grid,
        int regionId,
        bool[,] detachedMask,
        bool[,] supportedMask,
        int largeDetachedChunkMinCells)
    {
        _queue.Clear();
        detachedMask[startX, startY] = true;
        _queue.Enqueue(new CellPos(startX, startY));

        int count = 0;
        int minX = startX;
        int maxX = startX;
        int minY = startY;
        int maxY = startY;
        Vector2 sum = Vector2.zero;

        int[] dx = useFourWayConnectivity ? FourDirX : EightDirX;
        int[] dy = useFourWayConnectivity ? FourDirY : EightDirY;
        int dirCount = useFourWayConnectivity ? 4 : 8;

        while (_queue.Count > 0)
        {
            CellPos c = _queue.Dequeue();
            count++;
            minX = Mathf.Min(minX, c.x);
            maxX = Mathf.Max(maxX, c.x);
            minY = Mathf.Min(minY, c.y);
            maxY = Mathf.Max(maxY, c.y);
            sum += grid.CellCenterLocal(c.x, c.y);

            for (int i = 0; i < dirCount; i++)
            {
                int nx = c.x + dx[i];
                int ny = c.y + dy[i];
                if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    continue;
                if (!solid[nx, ny] || supportedMask[nx, ny] || detachedMask[nx, ny])
                    continue;

                detachedMask[nx, ny] = true;
                _queue.Enqueue(new CellPos(nx, ny));
            }
        }

        Vector2 center = count > 0 ? sum / count : grid.CellCenterLocal(startX, startY);
        return new DetachedRegionInfo
        {
            id = regionId,
            cellCount = count,
            bounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1),
            localCenter = center,
            qualifiesAsLargeChunk = count >= largeDetachedChunkMinCells
        };
    }

    public static void ClearBoolGrid(bool[,] grid)
    {
        if (grid == null)
            return;

        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            grid[x, y] = false;
    }
}
