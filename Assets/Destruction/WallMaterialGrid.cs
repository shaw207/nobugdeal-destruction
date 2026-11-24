using UnityEngine;

/*
 * Representa una pared destructible como una grilla 2D de celdas solidas.
 * Es la estructura de datos central para saber que partes de la pared existen
 * todavia y que partes fueron eliminadas por impactos o explosiones.
 */
public sealed class WallMaterialGrid
{
    private readonly bool[,] _solid;
    private readonly int _width;
    private readonly int _height;
    private readonly float _halfW;
    private readonly float _halfH;
    private readonly float _cellW;
    private readonly float _cellH;

    public int Width => _width;
    public int Height => _height;
    public float CellWidth => _cellW;
    public float CellHeight => _cellH;
    public bool[,] Solid => _solid;

    public WallMaterialGrid(float wallWidth, float wallHeight, int resolutionX)
        : this(
            wallWidth,
            wallHeight,
            resolutionX,
            Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp(resolutionX, 16, 255) * wallHeight / Mathf.Max(0.0001f, wallWidth)),
                16,
                255))
    {
    }

    public WallMaterialGrid(float wallWidth, float wallHeight, int resolutionX, int resolutionY)
    {
        _width = Mathf.Clamp(resolutionX, 16, 255);
        _height = Mathf.Clamp(resolutionY, 16, 255);
        _solid = new bool[_width, _height];
        _halfW = wallWidth * 0.5f;
        _halfH = wallHeight * 0.5f;
        _cellW = wallWidth / _width;
        _cellH = wallHeight / _height;
        FillAll(true);
    }

    public void FillAll(bool solid)
    {
        for (int y = 0; y < _height; y++)
        for (int x = 0; x < _width; x++)
            _solid[x, y] = solid;
    }

    public Vector2 CellCenterLocal(int x, int y)
    {
        return new Vector2(
            -_halfW + (x + 0.5f) * _cellW,
            -_halfH + (y + 0.5f) * _cellH);
    }

    /*
     * Elimina del grid todas las celdas cubiertas por un stamp de dano.
     * Devuelve cuantas celdas nuevas fueron quitadas para actualizar porcentaje
     * de destruccion, coste de contrato y reconstruccion visual.
     */
    public int RemoveByStamp(IDamageStamp2D stamp)
    {
        Rect bounds = stamp.GetBounds();
        int xMin = Mathf.Clamp(WorldToCellX(bounds.xMin), 0, _width - 1);
        int xMax = Mathf.Clamp(WorldToCellX(bounds.xMax), 0, _width - 1);
        int yMin = Mathf.Clamp(WorldToCellY(bounds.yMin), 0, _height - 1);
        int yMax = Mathf.Clamp(WorldToCellY(bounds.yMax), 0, _height - 1);

        int removed = 0;
        for (int y = yMin; y <= yMax; y++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                if (!_solid[x, y])
                    continue;

                if (!stamp.Contains(CellCenterLocal(x, y)))
                    continue;

                _solid[x, y] = false;
                removed++;
            }
        }

        return removed;
    }

    public float CellMinX(int x) => -_halfW + x * _cellW;
    public float CellMinY(int y) => -_halfH + y * _cellH;

    private int WorldToCellX(float x)
    {
        return Mathf.FloorToInt((x + _halfW) / _cellW);
    }

    private int WorldToCellY(float y)
    {
        return Mathf.FloorToInt((y + _halfH) / _cellH);
    }
}
