using UnityEngine;

/*
 * Mantiene la configuracion global de resolucion para paredes destructibles.
 * Permite que cada pared use valores comunes o un override local, manteniendo
 * limites de calidad para controlar coste de malla, colision y analisis.
 */
public class DestructibleWallGridManager : MonoBehaviour
{
    public static DestructibleWallGridManager Instance { get; private set; }

    [Header("Global Grid Quality")]
    [SerializeField] private DestructibleWallGridSettings settings = DestructibleWallGridSettings.Default;

    private readonly System.Collections.Generic.HashSet<DestructibleWall2DRectFramework> walls =
        new System.Collections.Generic.HashSet<DestructibleWall2DRectFramework>();

    public DestructibleWallGridSettings Settings => settings.Validated();
    public int RegisteredWallCount => walls.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void RegisterWall(DestructibleWall2DRectFramework wall)
    {
        if (wall != null)
            walls.Add(wall);
    }

    public void UnregisterWall(DestructibleWall2DRectFramework wall)
    {
        if (wall != null)
            walls.Remove(wall);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        settings = settings.Validated();
    }
#endif
}

[System.Serializable]
public struct DestructibleWallGridSettings
{
    public float targetWorldCellSize;
    public int minResolutionX;
    public int minResolutionY;
    public int maxResolutionX;
    public int maxResolutionY;
    public int maxTotalCellsPerWall;

    public static DestructibleWallGridSettings Default => new DestructibleWallGridSettings
    {
        targetWorldCellSize = 0.1f,
        minResolutionX = 16,
        minResolutionY = 16,
        maxResolutionX = 192,
        maxResolutionY = 192,
        maxTotalCellsPerWall = 16384
    };

    public DestructibleWallGridSettings Validated()
    {
        DestructibleWallGridSettings result = this;
        result.targetWorldCellSize = Mathf.Max(0.01f, result.targetWorldCellSize);
        result.minResolutionX = Mathf.Clamp(result.minResolutionX, 16, 255);
        result.minResolutionY = Mathf.Clamp(result.minResolutionY, 16, 255);
        result.maxResolutionX = Mathf.Clamp(Mathf.Max(result.maxResolutionX, result.minResolutionX), 16, 255);
        result.maxResolutionY = Mathf.Clamp(Mathf.Max(result.maxResolutionY, result.minResolutionY), 16, 255);
        result.maxTotalCellsPerWall = Mathf.Max(result.minResolutionX * result.minResolutionY, result.maxTotalCellsPerWall);
        return result;
    }
}

public struct DestructibleWallGridResolution
{
    public int x;
    public int y;

    public DestructibleWallGridResolution(int x, int y)
    {
        this.x = x;
        this.y = y;
    }
}

public static class DestructibleWallGridResolver
{
    /*
     * Decide que configuracion de grid debe usar una pared.
     * Prioriza el override local cuando esta activo, despues el manager global y
     * finalmente una configuracion por defecto si no existe ningun manager.
     */
    public static DestructibleWallGridSettings ResolveSettings(
        bool useLocalOverride,
        DestructibleWallGridSettings localOverride)
    {
        if (useLocalOverride)
            return localOverride.Validated();

        DestructibleWallGridManager manager = DestructibleWallGridManager.Instance;
        if (manager == null)
            manager = Object.FindFirstObjectByType<DestructibleWallGridManager>();

        if (manager != null)
            return manager.Settings;

        return DestructibleWallGridSettings.Default;
    }

    /*
     * Calcula la resolucion de celdas a partir del tamano real de la pared.
     * Ajusta el resultado a los limites configurados para mantener una densidad
     * visual estable sin superar el presupuesto de celdas.
     */
    public static DestructibleWallGridResolution ResolveResolution(
        float worldWidth,
        float worldHeight,
        DestructibleWallGridSettings settings)
    {
        settings = settings.Validated();

        int x = Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Max(0.0001f, worldWidth) / settings.targetWorldCellSize),
            settings.minResolutionX,
            settings.maxResolutionX);

        int y = Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Max(0.0001f, worldHeight) / settings.targetWorldCellSize),
            settings.minResolutionY,
            settings.maxResolutionY);

        ClampToCellBudget(ref x, ref y, settings);
        return new DestructibleWallGridResolution(x, y);
    }

    /*
     * Reduce la resolucion si el numero total de celdas supera el presupuesto.
     * Conserva los minimos configurados y baja primero el eje mas grande para
     * evitar grids excesivamente caros.
     */
    private static void ClampToCellBudget(
        ref int x,
        ref int y,
        DestructibleWallGridSettings settings)
    {
        int budget = Mathf.Max(1, settings.maxTotalCellsPerWall);
        if (x * y <= budget)
            return;

        float scale = Mathf.Sqrt(budget / (float)(x * y));
        x = Mathf.Max(settings.minResolutionX, Mathf.FloorToInt(x * scale));
        y = Mathf.Max(settings.minResolutionY, Mathf.FloorToInt(y * scale));

        while (x * y > budget && (x > settings.minResolutionX || y > settings.minResolutionY))
        {
            if (x >= y && x > settings.minResolutionX)
                x--;
            else if (y > settings.minResolutionY)
                y--;
            else
                break;
        }
    }
}
