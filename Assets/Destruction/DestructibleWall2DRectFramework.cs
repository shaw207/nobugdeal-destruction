using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Controlador principal de una pared destructible basada en una grilla 2D.
 * Resuelve la forma de la pared desde collider, mesh o datos manuales, aplica
 * sellos de dano sobre celdas, reconstruye mesh y colision, analiza soporte y
 * genera debris cuando grandes regiones quedan separadas.
 */
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DestructibleWall2DRectFramework : DestructibleBase
{
    [Header("Wall Shape Source")]
    [SerializeField] private WallShapeSource sourceMode = WallShapeSource.MeshCollider;
    [SerializeField] private bool lockResolvedShapeAtRuntime = true;
    [SerializeField] private Vector3 manualCenter = Vector3.zero;
    [SerializeField] private Vector3 manualSize = new Vector3(4f, 3f, 0.2f);
    [SerializeField] private BoxCollider sourceBox;
    [SerializeField] private MeshCollider sourceMeshCollider;
    [SerializeField] private MeshFilter sourceMeshFilter;

    [Header("Orientation")]
    [SerializeField] private WallFrontAxis frontAxis = WallFrontAxis.Z;

    [Header("Grid / State")]
    [SerializeField] private bool overrideGlobalGridSettings;
    [SerializeField] private DestructibleWallGridSettings localGridSettings = DestructibleWallGridSettings.Default;
    [SerializeField] private int maxDebugStoredStampHistory = 255;
    [SerializeField] private bool buildDestructionOnlyWhenDamaged = true;

    [Header("Collision")]
    [SerializeField] private bool generateCollisionBoxes = true;
    [SerializeField] private bool mergeVerticalRuns = true;
    [SerializeField] private Transform collisionRoot;
    [SerializeField] private PhysicsMaterial collisionMaterial;
    [SerializeField] private bool disableSourceCollidersOnAwake = true;

    [Header("Support Analysis")]
    [SerializeField] private bool analyzeSupportAfterDamage = true;
    [SerializeField] private SupportEdgeMask supportEdges = SupportEdgeMask.Bottom;
    [SerializeField] private bool useFourWayConnectivity = true;
    [SerializeField] private int largeDetachedChunkMinCells = 12;
    [SerializeField] private bool drawSupportGizmos = true;

    [Header("Debris")]
    [SerializeField] private bool autoDetachLargeChunksAfterAnalysis = true;
    [SerializeField] private bool spawnPhysicsDebris = true;
    [SerializeField] private int maxActiveDebrisObjects = 12;
    [SerializeField] private float debrisLifetime = 6f;
    [SerializeField] private float debrisSpawnImpulse = 1.75f;
    [SerializeField] private float debrisUpwardImpulse = 0.75f;
    [SerializeField] private float debrisMassPerCell = 0.08f;
    [SerializeField] private PhysicsMaterial debrisPhysicsMaterial;
    [SerializeField] private bool spawnExactRegionVisualDebris = true;
    [SerializeField] private bool useMultiBoxCollidersForExactDebris = true;
    [SerializeField] private bool useBoundingBoxFallbackForTinyDebris = true;
    [SerializeField] private int exactDebrisMinCells = 6;
    [SerializeField] private Transform debrisRoot;

    [Header("Debug")]
    [SerializeField] private bool applyDebugCircleOnStart;
    [SerializeField] private Vector2 debugCircleCenter;
    [SerializeField] private float debugCircleRadius = 0.25f;
    [SerializeField] private bool drawGridGizmos;

    [Header("Read Only")]
    [SerializeField] private Vector3 _localWallCenter;
    [SerializeField] private Vector3 _localWallSize;
    [SerializeField] private int _resolvedGridResolutionX;
    [SerializeField] private int _resolvedGridResolutionY;
    [SerializeField] private int _lastRemovedCells;
    [SerializeField] private int _lastRectCount;
    [SerializeField] private int _lastCollisionBoxCount;
    [SerializeField] private int _activeColliderObjects;
    [SerializeField] private int _supportedCellCount;
    [SerializeField] private int _detachedCellCount;
    [SerializeField] private int _detachedRegionCount;
    [SerializeField] private int _largeDetachedRegionCount;
    [SerializeField] private int _spawnedDebrisCount;
    [SerializeField] private int _activeDebrisCount;
    [SerializeField] private float _destroyedPercent;
    [SerializeField] private List<DamageStampData> _debugStampHistory = new List<DamageStampData>();
    [SerializeField] private List<DetachedRegionInfo> _detachedRegions = new List<DetachedRegionInfo>();
    [SerializeField] private List<DebrisRegionInfo> _spawnedDebrisRegions = new List<DebrisRegionInfo>();

    private readonly List<BoxCollider> _pooledColliders = new List<BoxCollider>();
    private readonly List<BoxCollider> _activeColliders = new List<BoxCollider>();
    private readonly List<WallDetachedDebris> _activeDebris = new List<WallDetachedDebris>();
    private readonly List<Collider> _sourceColliders = new List<Collider>(4);
    private readonly List<DamageStampData> _appliedStampHistory = new List<DamageStampData>(128);
    private static readonly Vector3[] BoundsCornerSigns =
    {
        new Vector3(-1f, -1f, -1f),
        new Vector3(1f, -1f, -1f),
        new Vector3(-1f, 1f, -1f),
        new Vector3(1f, 1f, -1f),
        new Vector3(-1f, -1f, 1f),
        new Vector3(1f, -1f, 1f),
        new Vector3(-1f, 1f, 1f),
        new Vector3(1f, 1f, 1f)
    };

    private MeshFilter _meshFilter;
    private Mesh _mesh;
    private Mesh _initialSharedMesh;
    private WallMaterialGrid _grid;
    private bool[,] _supportedMask;
    private bool[,] _detachedMask;
    private readonly WallSupportAnalyzer _supportAnalyzer = new WallSupportAnalyzer();
    private bool _hasInitialMeshRepresentation;
    private bool _destructibleRepresentationActive;
    private bool _pendingSourceColliderDisable;
    private float _gridWallWidth;
    private float _gridWallHeight;
    private WallFrontAxis _gridFrontAxis;
    private bool _visualUvAnchorInitialized;
    private Vector2 _visualUvWallPlaneOffset;
    private WallFrontAxis _visualUvFrontAxis;
    private DestructibleWallGridSettings _lastResolvedGridSettings;

    private float HalfW => GetWallPlaneWidth() * 0.5f;
    private float HalfH => GetWallPlaneHeight() * 0.5f;

    public int DebugStoredStampCount => _debugStampHistory.Count;
    public int DetachedRegionCount => _detachedRegionCount;
    public int DetachedCellCount => _detachedCellCount;
    public IReadOnlyList<DetachedRegionInfo> DetachedRegions => _detachedRegions;
    public IReadOnlyList<DebrisRegionInfo> SpawnedDebrisRegions => _spawnedDebrisRegions;
    public float DestroyedPercent => _destroyedPercent;

    /*
     * Inicializa referencias, forma resuelta y raices internas de la pared.
     * Decide si empieza usando la malla original o si debe construir de inmediato
     * la representacion destructible basada en grid.
     */
    private void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        ResolveMissingReferences();
        RefreshSourceColliderCache();
        ResolveWallShapeOrFallback();
        EnsureCollisionRoot();
        EnsureDebrisRoot();

        _initialSharedMesh = _meshFilter != null ? _meshFilter.sharedMesh : null;
        _hasInitialMeshRepresentation = _initialSharedMesh != null;
        _destroyedPercent = 0f;

        if (applyDebugCircleOnStart)
        {
            SwitchToDestructibleRepresentation(ensureGrid: true);
            ApplyCircleDamageLocal(debugCircleCenter, debugCircleRadius);
            return;
        }

        if (buildDestructionOnlyWhenDamaged && _hasInitialMeshRepresentation)
        {
            SwitchToInitialRepresentation(clearGrid: true);
            return;
        }

        SwitchToDestructibleRepresentation(ensureGrid: true);
        RebuildNow();
    }

    private void Start()
    {
        if (DestructibleWallGridManager.Instance != null)
            DestructibleWallGridManager.Instance.RegisterWall(this);
    }

    private void OnDestroy()
    {
        if (DestructibleWallGridManager.Instance != null)
            DestructibleWallGridManager.Instance.UnregisterWall(this);

        ClearAllDebrisImmediate();

        if (_mesh != null)
        {
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
            _mesh = null;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        localGridSettings = localGridSettings.Validated();
        maxDebugStoredStampHistory = Mathf.Max(0, maxDebugStoredStampHistory);
        debugCircleRadius = Mathf.Max(0.001f, debugCircleRadius);
        manualSize.x = Mathf.Max(0.01f, manualSize.x);
        manualSize.y = Mathf.Max(0.01f, manualSize.y);
        manualSize.z = Mathf.Max(0.01f, manualSize.z);
        largeDetachedChunkMinCells = Mathf.Max(1, largeDetachedChunkMinCells);
        exactDebrisMinCells = Mathf.Max(1, exactDebrisMinCells);
        maxActiveDebrisObjects = Mathf.Max(0, maxActiveDebrisObjects);
        debrisLifetime = Mathf.Max(0f, debrisLifetime);
        debrisMassPerCell = Mathf.Max(0.001f, debrisMassPerCell);
        _destroyedPercent = Mathf.Clamp(_destroyedPercent, 0f, 100f);

        if (!Application.isPlaying)
        {
            ResolveMissingReferences();
            ResolveWallShapeOrFallback();
            TryFindExistingRootsOnly();
        }
    }
#endif

    /*
     * Convierte puntos y tamanos del mundo al plano local de dano.
     * Estas ayudas permiten que los controles externos no tengan que conocer la
     * orientacion interna de la pared.
     */
    public Vector2 WorldToDamageLocal2D(Vector3 worldPoint)
    {
        return WorldToWallLocal2D(worldPoint);
    }

    public float WorldToDamageLocalLength(float worldLength)
    {
        return WorldLengthToLocalPlane(worldLength);
    }

    public Vector2 WorldToDamageLocalSize(Vector2 worldSize)
    {
        return WorldSizeToLocalPlane(worldSize);
    }

    public Vector3 DamagePlaneToLocal3D(Vector2 damageLocalCenter, float normalOffset = 0f)
    {
        return DamagePlaneToLocalVector3(damageLocalCenter, normalOffset);
    }

    public Vector3 DamagePlaneSizeToLocal3D(Vector2 damageLocalSize, float thickness)
    {
        return DamagePlaneSizeToVector3(damageLocalSize.x, damageLocalSize.y, thickness);
    }

    /*
     * Entradas publicas para aplicar dano desde coordenadas de mundo.
     * Convierten posicion, tamano y escala al plano local de la pared antes de
     * crear el stamp que modifica la grilla destructible.
     */
    public void ApplyCircleDamageWorld(Vector3 worldCenter, float worldRadius)
    {
        ApplyStamp(BuildWorldCircleStamp(worldCenter, worldRadius));
    }

    public void ApplyBoxDamageWorld(Vector3 worldCenter, Vector2 worldSize, float rotationDeg = 0f)
    {
        Vector2 localCenter = WorldToWallLocal2D(worldCenter);
        Vector2 localSize = WorldSizeToLocalPlane(worldSize);
        ApplyBoxDamageLocal(localCenter, localSize, rotationDeg);
    }

    public void ApplyCapsuleDamageWorld(Vector3 worldCenter, float worldRadius, float worldLength, float rotationDeg = 0f)
    {
        Vector2 localCenter = WorldToWallLocal2D(worldCenter);
        float localRadius = WorldLengthToLocalPlane(worldRadius);
        float localLength = WorldLengthToLocalPlane(worldLength);
        ApplyCapsuleDamageLocal(localCenter, localRadius, localLength, rotationDeg);
    }

    public void ApplyCircleDamageLocal(Vector2 localCenter, float radius)
    {
        ApplyStamp(DamageStampData.Circle(localCenter, radius));
    }

    public void ApplyBoxDamageLocal(Vector2 localCenter, Vector2 size, float rotationDeg = 0f)
    {
        ApplyStamp(DamageStampData.Box(localCenter, size, rotationDeg));
    }

    public void ApplyCapsuleDamageLocal(Vector2 localCenter, float radius, float length, float rotationDeg = 0f)
    {
        ApplyStamp(DamageStampData.Capsule(localCenter, radius, length, rotationDeg));
    }

    [ContextMenu("Analyze Support Now")]
    /*
     * Fuerza el analisis de soporte estructural desde el inspector o codigo.
     * Sirve para actualizar regiones soportadas y desprendidas sin esperar a un
     * nuevo impacto de arma.
     */
    public void AnalyzeSupportNow()
    {
        if (!_destructibleRepresentationActive)
            return;

        if (_grid == null)
            InitializeGrid(forceReset: true);

        RebuildSupportAnalysis();
        AnalysisDirty = false;
    }

    [ContextMenu("Clear Damage")]
    /*
     * Limpia todo el dano acumulado de la pared.
     * Reinicia historial, debris, porcentaje destruido y vuelve a la
     * representacion inicial cuando la configuracion lo permite.
     */
    public void ClearDamage()
    {
        _debugStampHistory.Clear();
        _appliedStampHistory.Clear();
        ClearAllDebrisImmediate();
        _lastRemovedCells = 0;
        _destroyedPercent = 0f;

        if (buildDestructionOnlyWhenDamaged && _hasInitialMeshRepresentation)
        {
            SwitchToInitialRepresentation(clearGrid: true);
            return;
        }

        InitializeGrid(forceReset: true);
        SwitchToDestructibleRepresentation(ensureGrid: false);
        MarkAllDirty(analyzeSupportAfterDamage);
        RebuildNow();
    }

    [ContextMenu("Clear Debris")]
    public void ClearDebris()
    {
        ClearAllDebrisImmediate();
    }

    [ContextMenu("Check Damage Percent And Switch")]
    /*
     * Revisa el porcentaje destruido y sincroniza la representacion visual.
     * Evita mantener la malla destructible activa cuando no queda dano real y
     * fuerza reconstruccion cuando la pared ya tiene material eliminado.
     */
    public void CheckDamagePercentAndSwitch()
    {
        UpdateDestroyedPercentFromGrid();

        if (_destroyedPercent <= 0.0001f && buildDestructionOnlyWhenDamaged && _hasInitialMeshRepresentation)
        {
            SwitchToInitialRepresentation(clearGrid: true);
            return;
        }

        if (_destroyedPercent > 0.0001f)
        {
            SwitchToDestructibleRepresentation(ensureGrid: true);
            MarkVisualDirty();
            MarkCollisionDirty();
            if (analyzeSupportAfterDamage)
                MarkAnalysisDirty();
            RebuildNow();
        }
    }

    [ContextMenu("Rebuild Now")]
    /*
     * Ejecuta la reconstruccion pendiente de la pared destructible.
     * Coordina analisis de soporte, generacion de debris, mesh visual y cajas de
     * colision segun los dirty flags activos.
     */
    public override void RebuildNow()
    {
        if (!Application.isPlaying || !lockResolvedShapeAtRuntime)
            ResolveWallShapeOrFallback();

        if (!_destructibleRepresentationActive)
        {
            ClearDirtyFlags();
            RegisterRebuildTime();
            return;
        }

        EnsureGridMatchesResolvedWallShape(replayStampHistory: true);

        if (AnalysisDirty || (analyzeSupportAfterDamage && _supportedMask == null))
        {
            RebuildSupportAnalysis();

            if (autoDetachLargeChunksAfterAnalysis)
                ProcessLargeDetachedRegions();
        }

        CleanupDeadDebrisRefs();

        if (VisualDirty)
            RebuildVisualMesh();

        if (CollisionDirty)
            RebuildCollisionBoxes();

        ClearDirtyFlags();
        RegisterRebuildTime();
    }

    /*
     * Aplica un stamp de dano al grid de la pared.
     * Es el punto central que cambia celdas solidas, registra coste de contrato,
     * guarda historial para replay y marca los subsistemas que deben reconstruirse.
     */
    private void ApplyStamp(DamageStampData data)
    {
        if (!Application.isPlaying || !lockResolvedShapeAtRuntime)
            ResolveWallShapeOrFallback();

        bool switchedRepresentation = false;
        if (!_destructibleRepresentationActive)
        {
            SwitchToDestructibleRepresentation(ensureGrid: true);
            switchedRepresentation = true;
        }
        EnsureGridMatchesResolvedWallShape(replayStampHistory: true);
        if (_grid == null)
            return;

        DamageStampData originalData = data;

        NormalizeStampForGrid(ref data);

        IDamageStamp2D stamp = DamageStampFactory.Build(data);
        int removed = _grid.RemoveByStamp(stamp);
        _lastRemovedCells = removed;
        UpdateDestroyedPercentFromGrid();

        if (maxDebugStoredStampHistory > 0)
        {
            if (_debugStampHistory.Count >= maxDebugStoredStampHistory)
                _debugStampHistory.RemoveAt(0);

            _debugStampHistory.Add(originalData);
        }

        if (removed <= 0)
        {
            if (switchedRepresentation &&
                _destroyedPercent <= 0.0001f &&
                buildDestructionOnlyWhenDamaged &&
                _hasInitialMeshRepresentation)
            {
                SwitchToInitialRepresentation(clearGrid: true);
            }
            return;
        }

        _appliedStampHistory.Add(originalData);

        MarkVisualDirty();
        MarkCollisionDirty();

        if (analyzeSupportAfterDamage)
            MarkAnalysisDirty();

        if (switchedRepresentation)
        {
            RebuildNow();
            return;
        }

        if (rebuildImmediatelyOnDamage && CanRebuildNow())
            RebuildNow();
    }

    /*
     * Crea o reinicia la grilla destructible con la resolucion adecuada.
     * Usa el tamano real de la pared y la configuracion global/local para definir
     * cuantas celdas representan el material destruible.
     */
    private void InitializeGrid(bool forceReset)
    {
        if (!forceReset && _grid != null)
            return;

        float wallWidth = GetWallPlaneWidth();
        float wallHeight = GetWallPlaneHeight();
        GetWallPlaneWorldScales(out float scaleU, out float scaleV);
        DestructibleWallGridSettings gridSettings = DestructibleWallGridResolver.ResolveSettings(
            overrideGlobalGridSettings,
            localGridSettings);
        DestructibleWallGridResolution resolution = DestructibleWallGridResolver.ResolveResolution(
            wallWidth * scaleU,
            wallHeight * scaleV,
            gridSettings);

        _grid = new WallMaterialGrid(wallWidth, wallHeight, resolution.x, resolution.y);
        _gridWallWidth = wallWidth;
        _gridWallHeight = wallHeight;
        _gridFrontAxis = frontAxis;
        _lastResolvedGridSettings = gridSettings;
        _resolvedGridResolutionX = _grid.Width;
        _resolvedGridResolutionY = _grid.Height;
        _supportedMask = new bool[_grid.Width, _grid.Height];
        _detachedMask = new bool[_grid.Width, _grid.Height];
        _detachedRegions.Clear();
        _spawnedDebrisRegions.Clear();
        _spawnedDebrisCount = 0;
        _supportedCellCount = _grid.Width * _grid.Height;
        _detachedCellCount = 0;
        _detachedRegionCount = 0;
        _largeDetachedRegionCount = 0;
        _destroyedPercent = 0f;
        _visualUvAnchorInitialized = false;

        MarkAllDirty(analyzeSupportAfterDamage);
    }

    private void EnsureGeneratedMeshInstance()
    {
        if (_mesh != null)
            return;

        _mesh = new Mesh
        {
            name = "DestructibleWall2DRectFrameworkMesh",
            indexFormat = IndexFormat.UInt32
        };
    }

    private void ReleaseGridState()
    {
        _grid = null;
        _gridWallWidth = 0f;
        _gridWallHeight = 0f;
        _gridFrontAxis = frontAxis;
        _resolvedGridResolutionX = 0;
        _resolvedGridResolutionY = 0;
        _visualUvAnchorInitialized = false;
        _supportedMask = null;
        _detachedMask = null;
        _detachedRegions.Clear();
        _spawnedDebrisRegions.Clear();
        _spawnedDebrisCount = 0;
        _supportedCellCount = 0;
        _detachedCellCount = 0;
        _detachedRegionCount = 0;
        _largeDetachedRegionCount = 0;
    }

    /*
     * Mantiene la grilla sincronizada con la forma actual de la pared.
     * Si cambia tamano, eje o configuracion, reconstruye el grid y reaplica el
     * historial de dano para no perder el estado destructivo.
     */
    private void EnsureGridMatchesResolvedWallShape(bool replayStampHistory)
    {
        if (_grid == null)
        {
            InitializeGrid(forceReset: true);
            return;
        }

        float wallWidth = GetWallPlaneWidth();
        float wallHeight = GetWallPlaneHeight();
        bool axisChanged = _gridFrontAxis != frontAxis;
        bool gridSettingsChanged = HasGridSettingsChanged();
        if (!axisChanged &&
            !gridSettingsChanged &&
            !HasMeaningfulGridSizeChange(wallWidth, _gridWallWidth) &&
            !HasMeaningfulGridSizeChange(wallHeight, _gridWallHeight))
            return;

        List<DamageStampData> replay = null;
        if (replayStampHistory && _appliedStampHistory.Count > 0)
            replay = new List<DamageStampData>(_appliedStampHistory);

        ClearAllDebrisImmediate();
        InitializeGrid(forceReset: true);

        if (replay != null)
        {
            for (int i = 0; i < replay.Count; i++)
            {
                DamageStampData normalized = replay[i];
                NormalizeStampForGrid(ref normalized);
                _grid.RemoveByStamp(DamageStampFactory.Build(normalized));
            }
        }

        UpdateDestroyedPercentFromGrid();
        MarkVisualDirty();
        MarkCollisionDirty();
        if (analyzeSupportAfterDamage)
            MarkAnalysisDirty();
    }

    private static bool HasMeaningfulGridSizeChange(float current, float previous)
    {
        float tolerance = Mathf.Max(0.001f, Mathf.Max(Mathf.Abs(current), Mathf.Abs(previous)) * 0.001f);
        return Mathf.Abs(current - previous) > tolerance;
    }

    private bool HasGridSettingsChanged()
    {
        DestructibleWallGridSettings current = DestructibleWallGridResolver.ResolveSettings(
            overrideGlobalGridSettings,
            localGridSettings);

        return Mathf.Abs(current.targetWorldCellSize - _lastResolvedGridSettings.targetWorldCellSize) > 0.0001f ||
               current.minResolutionX != _lastResolvedGridSettings.minResolutionX ||
               current.minResolutionY != _lastResolvedGridSettings.minResolutionY ||
               current.maxResolutionX != _lastResolvedGridSettings.maxResolutionX ||
               current.maxResolutionY != _lastResolvedGridSettings.maxResolutionY ||
               current.maxTotalCellsPerWall != _lastResolvedGridSettings.maxTotalCellsPerWall;
    }

    private void RefreshSourceColliderCache()
    {
        _sourceColliders.Clear();
        TryAddSourceCollider(sourceBox);
        TryAddSourceCollider(sourceMeshCollider);

        if (_sourceColliders.Count > 0)
            return;

        Collider[] localColliders = GetComponents<Collider>();
        for (int i = 0; i < localColliders.Length; i++)
            TryAddSourceCollider(localColliders[i]);
    }

    private void TryAddSourceCollider(Collider collider)
    {
        if (collider == null || _sourceColliders.Contains(collider))
            return;

        _sourceColliders.Add(collider);
    }

    private void SetSourceCollidersEnabled(bool enabled)
    {
        RefreshSourceColliderCache();
        for (int i = 0; i < _sourceColliders.Count; i++)
        {
            Collider c = _sourceColliders[i];
            if (c != null)
                c.enabled = enabled;
        }
    }

    /*
     * Cambia temporalmente a la representacion original de la pared.
     * Se usa cuando no hay dano y conviene conservar la malla/colliders fuente
     * en lugar de mantener activos mesh y colliders generados.
     */
    private void SwitchToInitialRepresentation(bool clearGrid)
    {
        if (_meshFilter != null && _hasInitialMeshRepresentation)
            _meshFilter.sharedMesh = _initialSharedMesh;

        SetSourceCollidersEnabled(true);
        _pendingSourceColliderDisable = false;

        WallCollisionBuilder.DeactivateAll(_activeColliders);
        _lastRectCount = 0;
        _lastCollisionBoxCount = 0;
        _activeColliderObjects = 0;

        if (clearGrid)
        {
            ReleaseGridState();
            _appliedStampHistory.Clear();
        }

        _destructibleRepresentationActive = false;
        ClearDirtyFlags();

        if (Application.isPlaying)
            Physics.SyncTransforms();
    }

    /*
     * Activa la representacion generada por el sistema destructible.
     * Prepara la malla dinamica, controla colliders fuente y asegura que exista
     * una grilla lista para recibir dano.
     */
    private void SwitchToDestructibleRepresentation(bool ensureGrid)
    {
        EnsureGeneratedMeshInstance();

        if (_meshFilter != null)
            _meshFilter.sharedMesh = _mesh;

        SetSourceCollidersEnabled(true);
        _pendingSourceColliderDisable = disableSourceCollidersOnAwake && generateCollisionBoxes;

        if (ensureGrid && _grid == null)
            InitializeGrid(forceReset: true);

        _destructibleRepresentationActive = true;

        if (Application.isPlaying)
            Physics.SyncTransforms();
    }

    private void UpdateDestroyedPercentFromGrid()
    {
        if (_grid == null)
        {
            _destroyedPercent = 0f;
            return;
        }

        bool[,] solid = _grid.Solid;
        int width = _grid.Width;
        int height = _grid.Height;
        int total = width * height;
        int solidCount = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (solid[x, y])
                    solidCount++;
            }
        }

        float destroyedRatio = 1f - (solidCount / Mathf.Max(1f, total));
        _destroyedPercent = Mathf.Clamp01(destroyedRatio) * 100f;
    }

    private void NormalizeStampForGrid(ref DamageStampData data)
    {
        if (_grid == null)
            return;

        float minCell = 0.5f * Mathf.Min(_grid.CellWidth, _grid.CellHeight);

        switch (data.shape)
        {
            case StampShape.Circle:
                data.radius = Mathf.Max(data.radius, minCell);
                break;

            case StampShape.Box:
                data.size.x = Mathf.Max(data.size.x, _grid.CellWidth);
                data.size.y = Mathf.Max(data.size.y, _grid.CellHeight);
                break;

            case StampShape.Capsule:
                data.capsuleRadius = Mathf.Max(data.capsuleRadius, minCell);
                break;

            case StampShape.Ellipse:
                data.ellipseRadiusX = Mathf.Max(data.ellipseRadiusX, _grid.CellWidth * 0.5f);
                data.ellipseRadiusY = Mathf.Max(data.ellipseRadiusY, _grid.CellHeight * 0.5f);
                break;
        }
    }

    private void ResolveMissingReferences()
    {
        if (sourceBox == null)
            sourceBox = GetComponent<BoxCollider>();

        if (sourceMeshCollider == null)
            sourceMeshCollider = GetComponent<MeshCollider>();

        if (sourceMeshFilter == null)
            sourceMeshFilter = GetComponent<MeshFilter>();

        RefreshSourceColliderCache();
    }

    private int GetPreferredCollisionLayer()
    {
        if (sourceBox != null)
            return sourceBox.gameObject.layer;
        if (sourceMeshCollider != null)
            return sourceMeshCollider.gameObject.layer;
        return gameObject.layer;
    }

    private void ResolveWallShapeOrFallback()
    {
        if (!TryResolveWallShape(out _localWallCenter, out _localWallSize))
        {
            _localWallCenter = manualCenter;
            _localWallSize = manualSize;
        }

        _localWallSize.x = Mathf.Max(0.01f, _localWallSize.x);
        _localWallSize.y = Mathf.Max(0.01f, _localWallSize.y);
        _localWallSize.z = Mathf.Max(0.01f, _localWallSize.z);
    }

    private void GetWallBasis(
        out int uAxis,
        out int vAxis,
        out int nAxis,
        out Vector3 uDir,
        out Vector3 vDir,
        out Vector3 nDir)
    {
        WallAxisUtility.GetBasis(frontAxis, out uAxis, out vAxis, out nAxis, out uDir, out vDir, out nDir);
    }

    private float GetWallPlaneWidth()
    {
        GetWallBasis(out int uAxis, out _, out _, out _, out _, out _);
        return Mathf.Max(0.01f, WallAxisUtility.GetAxis(_localWallSize, uAxis));
    }

    private float GetWallPlaneHeight()
    {
        GetWallBasis(out _, out int vAxis, out _, out _, out _, out _);
        return Mathf.Max(0.01f, WallAxisUtility.GetAxis(_localWallSize, vAxis));
    }

    private float GetWallThickness()
    {
        GetWallBasis(out _, out _, out int nAxis, out _, out _, out _);
        return Mathf.Max(0.01f, WallAxisUtility.GetAxis(_localWallSize, nAxis));
    }

    private Vector2 LocalToWallPlane2D(Vector3 localPoint)
    {
        GetWallBasis(out _, out _, out _, out Vector3 uDir, out Vector3 vDir, out _);
        Vector3 centered = localPoint - _localWallCenter;
        return new Vector2(Vector3.Dot(centered, uDir), Vector3.Dot(centered, vDir));
    }

    private Vector3 DamagePlaneToLocalVector3(Vector2 damageLocalCenter, float normalOffset = 0f)
    {
        GetWallBasis(out int uAxis, out int vAxis, out int nAxis, out _, out _, out _);
        Vector3 offset = WallAxisUtility.ComposeVector(
            uAxis,
            damageLocalCenter.x,
            vAxis,
            damageLocalCenter.y,
            nAxis,
            normalOffset);
        return _localWallCenter + offset;
    }

    private Vector3 DamagePlaneSizeToVector3(float width, float height, float thickness)
    {
        GetWallBasis(out int uAxis, out int vAxis, out int nAxis, out _, out _, out _);
        return WallAxisUtility.ComposeVector(uAxis, width, vAxis, height, nAxis, thickness);
    }

    private float GetWorldScaleAlongLocalDirection(Vector3 localDirection)
    {
        Vector3 worldVector = transform.TransformVector(localDirection);
        return Mathf.Max(0.0001f, worldVector.magnitude);
    }

    private void GetWallPlaneWorldScales(out float scaleU, out float scaleV)
    {
        GetWallBasis(out _, out _, out _, out Vector3 uDir, out Vector3 vDir, out _);
        scaleU = GetWorldScaleAlongLocalDirection(uDir);
        scaleV = GetWorldScaleAlongLocalDirection(vDir);
    }

    private float WorldLengthToLocalPlane(float worldLength)
    {
        GetWallPlaneWorldScales(out float scaleU, out float scaleV);
        float planeScale = Mathf.Sqrt(scaleU * scaleV);
        return worldLength / Mathf.Max(0.0001f, planeScale);
    }

    private Vector2 WorldSizeToLocalPlane(Vector2 worldSize)
    {
        GetWallPlaneWorldScales(out float scaleU, out float scaleV);
        return new Vector2(
            worldSize.x / scaleU,
            worldSize.y / scaleV);
    }

    private DamageStampData BuildWorldCircleStamp(Vector3 worldCenter, float worldRadius)
    {
        Vector2 localCenter = WorldToWallLocal2D(worldCenter);
        GetWallPlaneWorldScales(out float scaleU, out float scaleV);

        float radiusU = worldRadius / Mathf.Max(0.0001f, scaleU);
        float radiusV = worldRadius / Mathf.Max(0.0001f, scaleV);
        if (Mathf.Abs(radiusU - radiusV) <= Mathf.Max(0.0001f, Mathf.Max(radiusU, radiusV) * 0.01f))
            return DamageStampData.Circle(localCenter, 0.5f * (radiusU + radiusV));

        return DamageStampData.Ellipse(localCenter, radiusU, radiusV, 0f);
    }

    private bool TryResolveWallShape(out Vector3 center, out Vector3 size)
    {
        center = Vector3.zero;
        size = Vector3.one;

        switch (sourceMode)
        {
            case WallShapeSource.Manual:
                center = manualCenter;
                size = manualSize;
                return true;

            case WallShapeSource.BoxCollider:
                if (sourceBox == null)
                    return false;
                return TryResolveBoundsToWallLocal(sourceBox, new Bounds(sourceBox.center, sourceBox.size), out center, out size);

            case WallShapeSource.MeshCollider:
                if (sourceMeshCollider == null || sourceMeshCollider.sharedMesh == null)
                    return false;
                return TryResolveBoundsToWallLocal(sourceMeshCollider.transform, sourceMeshCollider.sharedMesh.bounds, out center, out size);

            case WallShapeSource.MeshFilter:
                if (sourceMeshFilter == null || sourceMeshFilter.sharedMesh == null)
                    return false;
                return TryResolveBoundsToWallLocal(sourceMeshFilter.transform, sourceMeshFilter.sharedMesh.bounds, out center, out size);

            default:
                return false;
        }
    }

    private bool TryResolveBoundsToWallLocal(BoxCollider box, Bounds localBounds, out Vector3 center, out Vector3 size)
    {
        if (box == null)
        {
            center = Vector3.zero;
            size = Vector3.one;
            return false;
        }

        return TryResolveBoundsToWallLocal(box.transform, localBounds, out center, out size);
    }

    private bool TryResolveBoundsToWallLocal(Transform sourceTransform, Bounds localBounds, out Vector3 center, out Vector3 size)
    {
        center = Vector3.zero;
        size = Vector3.one;
        if (sourceTransform == null)
            return false;

        Vector3 extents = localBounds.extents;
        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < BoundsCornerSigns.Length; i++)
        {
            Vector3 sourceLocalCorner = localBounds.center + Vector3.Scale(extents, BoundsCornerSigns[i]);
            Vector3 worldCorner = sourceTransform.TransformPoint(sourceLocalCorner);
            Vector3 wallLocalCorner = transform.InverseTransformPoint(worldCorner);
            min = Vector3.Min(min, wallLocalCorner);
            max = Vector3.Max(max, wallLocalCorner);
        }

        center = (min + max) * 0.5f;
        size = max - min;
        return true;
    }

    private void TryFindExistingRootsOnly()
    {
        if (collisionRoot == null)
        {
            Transform existingCollision = transform.Find("CollisionRoot");
            if (existingCollision != null)
                collisionRoot = existingCollision;
        }

        if (debrisRoot == null)
        {
            Transform existingDebris = transform.Find("DebrisRoot");
            if (existingDebris != null)
                debrisRoot = existingDebris;
        }
    }

    private void EnsureCollisionRoot()
    {
        if (collisionRoot != null)
            return;

        Transform existing = transform.Find("CollisionRoot");
        if (existing != null)
        {
            collisionRoot = existing;
            return;
        }

        GameObject go = new GameObject("CollisionRoot");
        go.transform.SetParent(transform, false);
        collisionRoot = go.transform;
    }

    private void EnsureDebrisRoot()
    {
        if (debrisRoot != null)
            return;

        Transform existing = transform.Find("DebrisRoot");
        if (existing != null)
        {
            debrisRoot = existing;
            return;
        }

        GameObject go = new GameObject("DebrisRoot");
        go.transform.SetParent(transform, false);
        debrisRoot = go.transform;
    }

    /*
     * Calcula que celdas siguen conectadas a los soportes y que regiones quedaron
     * separadas. Los resultados alimentan debug, estadisticas y generacion de
     * debris para trozos grandes.
     */
    private void RebuildSupportAnalysis()
    {
        if (_grid == null)
            return;

        EnsureAnalysisBuffers();

        _supportAnalyzer.Analyze(
            grid: _grid,
            supportEdges: supportEdges,
            useFourWayConnectivity: useFourWayConnectivity,
            largeDetachedChunkMinCells: largeDetachedChunkMinCells,
            supportedMask: _supportedMask,
            detachedMask: _detachedMask,
            detachedRegions: _detachedRegions,
            supportedCellCount: out _supportedCellCount,
            detachedCellCount: out _detachedCellCount,
            detachedRegionCount: out _detachedRegionCount,
            largeDetachedRegionCount: out _largeDetachedRegionCount);

        for (int i = 0; i < _detachedRegions.Count; i++)
        {
            DetachedRegionInfo info = _detachedRegions[i];
            info.localCenter3D = DamagePlaneToLocalVector3(info.localCenter);
            info.frontAxis = frontAxis;
            _detachedRegions[i] = info;
        }
    }

    private void EnsureAnalysisBuffers()
    {
        if (_grid == null)
            return;

        if (_supportedMask == null || _supportedMask.GetLength(0) != _grid.Width || _supportedMask.GetLength(1) != _grid.Height)
            _supportedMask = new bool[_grid.Width, _grid.Height];

        if (_detachedMask == null || _detachedMask.GetLength(0) != _grid.Width || _detachedMask.GetLength(1) != _grid.Height)
            _detachedMask = new bool[_grid.Width, _grid.Height];
    }

    /*
     * Convierte regiones desprendidas grandes en debris fisico.
     * Extrae sus celdas del grid, intenta crear fragmentos con fisica y actualiza
     * la pared para reflejar que esas piezas ya no forman parte del muro.
     */
    private void ProcessLargeDetachedRegions()
    {
        if (_grid == null || _detachedRegions.Count == 0)
            return;

        CleanupDeadDebrisRefs();
        bool removedAny = false;
        _spawnedDebrisRegions.Clear();

        for (int i = 0; i < _detachedRegions.Count; i++)
        {
            DetachedRegionInfo region = _detachedRegions[i];
            if (!region.qualifiesAsLargeChunk)
                continue;

            if (spawnPhysicsDebris && !TryEnsureDebrisCapacityForNextSpawn())
                continue;

            DebrisBuildData buildData = ExtractDetachedRegionBuildData(region);
            if (buildData == null || buildData.cellCount <= 0)
                continue;

            DebrisRegionInfo info = ToDebrisRegionInfo(buildData);

            bool spawnedPhysics = false;
            if (spawnPhysicsDebris)
            {
                spawnedPhysics = WallDebrisSpawner.SpawnDebrisFromRegion(
                    data: buildData,
                    info: info,
                    wallTransform: transform,
                    debrisRoot: debrisRoot,
                    sourceRenderer: GetComponent<MeshRenderer>(),
                    grid: _grid,
                    localWallCenter: _localWallCenter,
                    localWallSize: _localWallSize,
                    frontAxis: frontAxis,
                    spawnExactRegionVisualDebris: spawnExactRegionVisualDebris,
                    useBoundingBoxFallbackForTinyDebris: useBoundingBoxFallbackForTinyDebris,
                    exactDebrisMinCells: exactDebrisMinCells,
                    useMultiBoxCollidersForExactDebris: useMultiBoxCollidersForExactDebris,
                    mergeVerticalRuns: mergeVerticalRuns,
                    collisionMaterial: collisionMaterial,
                    debrisPhysicsMaterial: debrisPhysicsMaterial,
                    debrisLifetime: debrisLifetime,
                    debrisSpawnImpulse: debrisSpawnImpulse,
                    debrisUpwardImpulse: debrisUpwardImpulse,
                    debrisMassPerCell: debrisMassPerCell,
                    layer: gameObject.layer,
                    activeDebris: _activeDebris);

                if (!spawnedPhysics)
                {
                    RestoreDetachedRegionCells(buildData);
                    continue;
                }
            }

            removedAny = true;

            info.spawnedAsPhysicsDebris = spawnedPhysics;
            _spawnedDebrisRegions.Add(info);
        }

        _spawnedDebrisCount = _spawnedDebrisRegions.Count;
        CleanupDeadDebrisRefs();

        if (removedAny)
        {
            UpdateDestroyedPercentFromGrid();
            RebuildSupportAnalysis();
            MarkVisualDirty();
            MarkCollisionDirty();
        }
    }

    private bool TryEnsureDebrisCapacityForNextSpawn()
    {
        CleanupDeadDebrisRefs();
        if (maxActiveDebrisObjects <= 0)
            return true;

        while (_activeDebris.Count >= maxActiveDebrisObjects)
        {
            WallDetachedDebris oldest = _activeDebris[0];
            _activeDebris.RemoveAt(0);
            if (oldest != null)
                DestroyImmediateOrRuntime(oldest.gameObject);
        }

        CleanupDeadDebrisRefs();
        return _activeDebris.Count < maxActiveDebrisObjects;
    }

    /*
     * Extrae datos de construccion desde una region desprendida.
     * Quita sus celdas del grid principal y calcula bounds, centro y tamano para
     * que WallDebrisSpawner pueda crear el fragmento correspondiente.
     */
    private DebrisBuildData ExtractDetachedRegionBuildData(DetachedRegionInfo region)
    {
        if (_grid == null || _detachedMask == null)
            return null;

        DebrisBuildData data = new DebrisBuildData
        {
            sourceRegionId = region.id
        };

        bool[,] solid = _grid.Solid;
        int minX = int.MaxValue;
        int maxX = int.MinValue;
        int minY = int.MaxValue;
        int maxY = int.MinValue;
        Vector2 sum = Vector2.zero;

        for (int y = region.bounds.yMin; y < region.bounds.yMax; y++)
        {
            for (int x = region.bounds.xMin; x < region.bounds.xMax; x++)
            {
                if (!_detachedMask[x, y] || !solid[x, y])
                    continue;

                solid[x, y] = false;
                data.cellCount++;
                data.cells.Add(new Vector2Int(x, y));

                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
                sum += _grid.CellCenterLocal(x, y);
            }
        }

        if (data.cellCount <= 0)
            return data;

        data.localCenter = sum / data.cellCount;
        data.cellBounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);

        float width = data.cellBounds.width * _grid.CellWidth;
        float height = data.cellBounds.height * _grid.CellHeight;
        data.localSize = new Vector3(width, height, GetWallThickness());

        return data;
    }

    private void RestoreDetachedRegionCells(DebrisBuildData data)
    {
        if (data == null || data.cells == null || _grid == null)
            return;

        bool[,] solid = _grid.Solid;
        for (int i = 0; i < data.cells.Count; i++)
        {
            Vector2Int cell = data.cells[i];
            if (cell.x < 0 || cell.x >= _grid.Width || cell.y < 0 || cell.y >= _grid.Height)
                continue;

            solid[cell.x, cell.y] = true;
        }
    }

    private DebrisRegionInfo ToDebrisRegionInfo(DebrisBuildData data)
    {
        bool exactVisual = spawnExactRegionVisualDebris &&
                           (!useBoundingBoxFallbackForTinyDebris || data.cellCount >= exactDebrisMinCells);

        Vector3 center3D = DamagePlaneToLocalVector3(data.localCenter);
        Vector3 size3D = DamagePlaneSizeToVector3(data.localSize.x, data.localSize.y, data.localSize.z);

        return new DebrisRegionInfo
        {
            sourceRegionId = data.sourceRegionId,
            cellCount = data.cellCount,
            localCenter = data.localCenter,
            localSize = data.localSize,
            localCenter3D = center3D,
            localSize3D = size3D,
            frontAxis = frontAxis,
            hasResolvedLocal3D = true,
            spawnedAsPhysicsDebris = false,
            usedExactRegionVisual = exactVisual
        };
    }

    private void ClearAllDebrisImmediate()
    {
        CleanupDeadDebrisRefs();

        for (int i = 0; i < _activeDebris.Count; i++)
        {
            if (_activeDebris[i] != null)
                DestroyImmediateOrRuntime(_activeDebris[i].gameObject);
        }

        _activeDebris.Clear();
        _activeDebrisCount = 0;
        _spawnedDebrisRegions.Clear();
        _spawnedDebrisCount = 0;
    }

    private void CleanupDeadDebrisRefs()
    {
        for (int i = _activeDebris.Count - 1; i >= 0; i--)
        {
            if (_activeDebris[i] == null)
                _activeDebris.RemoveAt(i);
        }

        _activeDebrisCount = _activeDebris.Count;
    }

    private void DestroyImmediateOrRuntime(GameObject go)
    {
        if (go == null)
            return;

        if (Application.isPlaying)
            Destroy(go);
        else
            DestroyImmediate(go);
    }

    /*
     * Reconstruye la malla visible de la pared desde el grid actual.
     * Copia los datos generados a la instancia persistente para mantener estable
     * la referencia del MeshFilter durante la partida.
     */
    private void RebuildVisualMesh()
    {
        if (_mesh == null)
            return;

        EnsureVisualUvAnchor();
        Mesh rebuilt = WallMeshBuilder.BuildWallMesh(
            _grid,
            _localWallCenter,
            _localWallSize,
            frontAxis,
            _visualUvWallPlaneOffset);

        _mesh.Clear();
        _mesh.vertices = rebuilt.vertices;
        _mesh.triangles = rebuilt.triangles;
        _mesh.normals = rebuilt.normals;
        _mesh.uv = rebuilt.uv;
        _mesh.bounds = rebuilt.bounds;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            DestroyImmediate(rebuilt);
        else
            Destroy(rebuilt);
#else
        Destroy(rebuilt);
#endif
    }

    private void EnsureVisualUvAnchor()
    {
        if (_visualUvAnchorInitialized && _visualUvFrontAxis == frontAxis)
            return;

        _visualUvWallPlaneOffset = WallMeshBuilder.ResolveWallPlaneTopLeftOffsetForWall(_localWallCenter, _localWallSize, frontAxis);
        _visualUvFrontAxis = frontAxis;
        _visualUvAnchorInitialized = true;
    }

    /*
     * Reconstruye las cajas de colision que representan las celdas solidas.
     * Usa WallCollisionBuilder y un pool de BoxCollider para reducir creacion de
     * objetos al aplicar dano repetidamente.
     */
    private void RebuildCollisionBoxes()
    {
        EnsureCollisionRoot();
        WallCollisionBuilder.DeactivateAll(_activeColliders);

        if (!generateCollisionBoxes || _grid == null)
        {
            SetSourceCollidersEnabled(true);
            _pendingSourceColliderDisable = false;

            _lastRectCount = 0;
            _lastCollisionBoxCount = 0;
            _activeColliderObjects = 0;

            if (Application.isPlaying)
                Physics.SyncTransforms();
            return;
        }

        List<RectIntRun> rects = WallCollisionBuilder.CollectRectsFromSolidGrid(_grid, mergeVerticalRuns);
        _lastRectCount = rects.Count;
        _lastCollisionBoxCount = rects.Count;

        WallCollisionBuilder.RebuildPooledBoxColliders(
            collisionRoot: collisionRoot,
            pooledColliders: _pooledColliders,
            activeColliders: _activeColliders,
            rects: rects,
            grid: _grid,
            localWallCenter: _localWallCenter,
            localWallSize: _localWallSize,
            frontAxis: frontAxis,
            material: collisionMaterial,
            layer: GetPreferredCollisionLayer());

        _activeColliderObjects = _activeColliders.Count;

        if (_pendingSourceColliderDisable && disableSourceCollidersOnAwake)
        {
            SetSourceCollidersEnabled(false);
            _pendingSourceColliderDisable = false;
        }

        if (Application.isPlaying)
            Physics.SyncTransforms();
    }

    private Vector2 WorldToWallLocal2D(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        return LocalToWallPlane2D(local);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(_localWallCenter, _localWallSize);

        if (_grid == null)
            return;

        bool[,] solid = _grid.Solid;
        float stepX = _grid.CellWidth;
        float stepY = _grid.CellHeight;
        float halfW = HalfW;
        float halfH = HalfH;
        float thickness = GetWallThickness();

        GetWallBasis(
            out int uAxis,
            out int vAxis,
            out int nAxis,
            out Vector3 uDir,
            out Vector3 vDir,
            out _);

        Vector3 gridCellSize = WallAxisUtility.ComposeVector(uAxis, stepX, vAxis, stepY, nAxis, thickness * 0.05f);
        Vector3 detachedCellSize = WallAxisUtility.ComposeVector(uAxis, stepX, vAxis, stepY, nAxis, thickness * 0.1f);
        Vector3 supportedCellSize = WallAxisUtility.ComposeVector(uAxis, stepX, vAxis, stepY, nAxis, thickness * 0.08f);

        if (drawGridGizmos)
        {
            for (int y = 0; y < _grid.Height; y++)
            {
                for (int x = 0; x < _grid.Width; x++)
                {
                    if (!solid[x, y])
                        continue;

                    Gizmos.color = new Color(0f, 1f, 0f, 0.08f);
                    float u = -halfW + (x + 0.5f) * stepX;
                    float v = -halfH + (y + 0.5f) * stepY;
                    Vector3 pos = _localWallCenter + uDir * u + vDir * v;
                    Gizmos.DrawCube(pos, gridCellSize);
                }
            }
        }

        if (!drawSupportGizmos || _supportedMask == null || _detachedMask == null)
            return;

        for (int y = 0; y < _grid.Height; y++)
        {
            for (int x = 0; x < _grid.Width; x++)
            {
                if (!solid[x, y])
                    continue;

                float u = -halfW + (x + 0.5f) * stepX;
                float v = -halfH + (y + 0.5f) * stepY;
                Vector3 pos = _localWallCenter + uDir * u + vDir * v;

                if (_detachedMask[x, y])
                {
                    Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.28f);
                    Gizmos.DrawCube(pos, detachedCellSize);
                }
                else if (_supportedMask[x, y])
                {
                    Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.18f);
                    Gizmos.DrawCube(pos, supportedCellSize);
                }
            }
        }
    }
}
