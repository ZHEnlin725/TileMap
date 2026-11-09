using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum GameGridTileType
{
    Rectangle,
    Isometric
}

public enum GameGridSortOrder
{
    BottomLeft = 0,
    BottomRight = 1,
    TopLeft = 2,
    TopRight = 3
}

/// <summary>
/// Renders a cell-based terrain grid on the grid: one tile is placed on each
/// cell corner and is selected from the four surrounding data cells. Out-of-range
/// cells are treated as empty.
/// Edit mode builds merged preview meshes for painting; at runtime tiles are
/// individual pooled renderers driven by <see cref="GameGridRenderer.ShowRange"/>.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Rendering/Game Grid Renderer")]
public sealed class GameGridRenderer : MonoBehaviour
{
    private const string GeneratedPrefix = "__Grid_";
    private const int LargeMeshVertexCount = 65000;
    private const int MaxPooledTileObjects = 2048;
    private const float Epsilon = 0.0001f;
    private static readonly int MainTexProperty = Shader.PropertyToID("_MainTex");
    private static Shader defaultSpriteShader;

    [SerializeField, Min(1)] private int width = 16;

    [SerializeField, Min(1)] private int height = 16;
    [SerializeField, Min(Epsilon)] private float cellSize = .125f;
    [SerializeField] private bool renderInEditMode = true;
    [SerializeField] private GameGridTileType tileType = GameGridTileType.Rectangle;
    [SerializeField] private GameGridSortOrder sortOrder = GameGridSortOrder.BottomLeft;
    [SerializeField] private Vector2 isometricTileSize = new Vector2(1f, 0.5f);
    [SerializeField, Min(1)] private int isometricLevels = 1;
    [SerializeField, Min(Epsilon)] private float isometricLevelHeight = 0.5f;

    [Header("Runtime Tiles")]
    [SerializeField, Min(0)] private float tileRecycleDelay = 5f;

    [Header("Terrain")] [SerializeField]
    private List<GameGridTerrainLayer> terrainLayers = new List<GameGridTerrainLayer>();

    [SerializeField, HideInInspector] private int cellsWidth;
    [SerializeField, HideInInspector] private int cellsHeight;

#if UNITY_EDITOR
    [SerializeField, HideInInspector] private bool editorItemMode;
    [SerializeField, HideInInspector] private int editorBrushLayerIndex = -1;
    [SerializeField, HideInInspector] private int editorBrushZ;
    [SerializeField, HideInInspector] private int editorBrushSize = 1;
    [SerializeField, HideInInspector] private bool editorShowGrid = true;
#endif

    public event Action Changed;

    private readonly List<GameObject> generatedObjects = new List<GameObject>();
    private readonly List<Mesh> generatedMeshes = new List<Mesh>();
    private readonly List<Material> generatedMaterials = new List<Material>();
    private readonly List<RuntimeTile> activeTiles = new List<RuntimeTile>();
    private readonly Dictionary<(int layerIndex, int z, int x, int y), RuntimeTile> activeTileMap =
        new Dictionary<(int layerIndex, int z, int x, int y), RuntimeTile>();
    private readonly List<GameObject> staticTilePool = new List<GameObject>();
    private readonly List<GameObject> animatedTilePool = new List<GameObject>();
    private readonly List<RuntimeTile> runtimeTilePool = new List<RuntimeTile>();
    private readonly Dictionary<Material, Dictionary<Texture2D, Material>> tileMaterialCache =
        new Dictionary<Material, Dictionary<Texture2D, Material>>();
    private readonly List<Vector2Int> changedCellScratch = new List<Vector2Int>();
    private int rangeMinX;
    private int rangeMinY;
    private int rangeMaxX = -1;
    private int rangeMaxY = -1;
    private bool hasRange;
    private bool warnedShowRangeInEditMode;
    private bool rebuildPending;
    private bool hasPreviousShowRange;
    private int prevShowMinX;
    private int prevShowMaxX;
    private int prevShowMinY;
    private int prevShowMaxY;
    private int pendingRecycleCount;

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;
    public GameGridTileType TileType => tileType;
    public bool IsIsometric => tileType == GameGridTileType.Isometric;
    public Vector2 IsometricTileSize => isometricTileSize;
    public int LevelCount => Mathf.Max(1, isometricLevels);
    public float LevelHeight => Mathf.Max(Epsilon, isometricLevelHeight);
    public bool UsesZAsY => IsIsometric && LevelCount > 1;

    public IReadOnlyList<GameGridTerrainLayer> TerrainLayers => terrainLayers;

    public GameGridSortOrder SortOrder
    {
        get => sortOrder;
        set
        {
            if (sortOrder == value) return;
            sortOrder = value;
            Rebuild();
        }
    }

    /// <summary>Seconds a tile stays alive after leaving the visible range.</summary>
    public float TileRecycleDelay
    {
        get => tileRecycleDelay;
        set => tileRecycleDelay = Mathf.Max(0f, value);
    }

    public bool HasVisibleRange => hasRange;

    public int VisibleTileCount => activeTiles.Count;

    public bool IsTileSetCompatible(GameGridTileSet set)
    {
        return set != null &&
               (set.TileType == GameGridTileType.Isometric) == IsIsometric;
    }

#if UNITY_EDITOR
    public bool EditorItemMode
    {
        get => editorItemMode;
        set => editorItemMode = value;
    }

    public int EditorBrushLayerIndex
    {
        get => editorBrushLayerIndex;
        set => editorBrushLayerIndex = value;
    }

    public int EditorBrushZ
    {
        get => Mathf.Clamp(editorBrushZ, 0, LevelCount - 1);
        set => editorBrushZ = Mathf.Clamp(value, 0, LevelCount - 1);
    }

    public int EditorBrushSize
    {
        get => editorBrushSize;
        set => editorBrushSize = Mathf.Max(1, value);
    }

    public bool EditorShowGrid
    {
        get => editorShowGrid;
        set => editorShowGrid = value;
    }
#endif

    /// <summary>
    /// Shows every tile whose grid position falls inside the cell rectangle
    /// [minX..maxX] x [minY..maxY]. Corner-layout (dual grid) tiles extend one
    /// corner beyond the rectangle so the covered cells stay seamless. Tiles
    /// outside the range are recycled into the pool after
    /// <see cref="TileRecycleDelay"/> seconds.
    /// </summary>
    public void ShowRange(int minX, int minY, int maxX, int maxY)
    {
        if (this == null) return;
        if (!Application.isPlaying)
        {
#if UNITY_EDITOR
            if (!warnedShowRangeInEditMode)
            {
                warnedShowRangeInEditMode = true;
                Debug.LogWarning("GameGridRenderer.ShowRange only works at runtime; edit mode renders the paint preview.", this);
            }
#endif
            return;
        }

        var changed = !hasRange ||
                      minX != rangeMinX || minY != rangeMinY ||
                      maxX != rangeMaxX || maxY != rangeMaxY;
        rangeMinX = minX;
        rangeMinY = minY;
        rangeMaxX = maxX;
        rangeMaxY = maxY;
        hasRange = true;
        if (changed) RefreshRangeTiles();
    }

    public void ShowRange(RectInt range)
    {
        ShowRange(range.xMin, range.yMin, range.xMax - 1, range.yMax - 1);
    }

    /// <summary>Recycles every visible tile immediately and clears the range.</summary>
    public void HideRangeTiles()
    {
        if (this == null) return;
        hasRange = false;
        hasPreviousShowRange = false;
        if (Application.isPlaying) RecycleAllTiles();
    }

    public bool GetCell(GameGridTerrainLayer layer, int x, int y)
    {
        return GetCell(layer, x, y, 0);
    }

    public bool GetCell(GameGridTerrainLayer layer, int x, int y, int z)
    {
        EnsureData();
        if (layer == null || !InBounds(x, y, z)) return false;
        return layer.GetCell(GetCellIndex(x, y, z));
    }

    public int CountCells(GameGridTerrainLayer layer)
    {
        EnsureData();
        if (layer == null) return 0;

        var filled = 0;
        var count = width * height * LevelCount;
        for (var index = 0; index < count; index++)
            if (layer.GetCell(index))
                filled++;
        return filled;
    }

    public bool SetCell(GameGridTerrainLayer layer, int x, int y, bool occupied, bool rebuild = true)
    {
        return SetCell(layer, x, y, 0, occupied, rebuild);
    }

    public bool SetCell(GameGridTerrainLayer layer, int x, int y, int z, bool occupied, bool rebuild = true)
    {
        EnsureData();
        if (layer == null || !InBounds(x, y, z)) return false;

        var index = GetCellIndex(x, y, z);
        if (layer.GetCell(index) == occupied) return false;

        layer.SetCell(index, occupied);
        if (Application.isPlaying)
        {
            // Runtime tiles refresh incrementally around the changed cell;
            // full range rescans are reserved for bulk rebuilds.
            RefreshTilesForCellChange(layer, x, y, z);
        }
        else if (rebuild)
        {
            Rebuild();
        }

        Changed?.Invoke();
        return true;
    }

    public int SetCells(GameGridTerrainLayer layer, IEnumerable<Vector2Int> positions, bool occupied,
        bool rebuild = true)
    {
        return SetCells(layer, positions, occupied, 0, rebuild);
    }

    public int SetCells(GameGridTerrainLayer layer, IEnumerable<Vector2Int> positions, bool occupied,
        int z, bool rebuild = true)
    {
        if (layer == null || positions == null) return 0;
        EnsureData();

        var changed = 0;
        changedCellScratch.Clear();
        foreach (var position in positions)
        {
            if (!InBounds(position.x, position.y, z)) continue;
            var index = GetCellIndex(position.x, position.y, z);
            if (layer.GetCell(index) == occupied) continue;
            layer.SetCell(index, occupied);
            changed++;
            if (Application.isPlaying) changedCellScratch.Add(position);
        }

        if (changed == 0) return 0;
        if (Application.isPlaying)
        {
            for (var i = 0; i < changedCellScratch.Count; i++)
                RefreshTilesForCellChange(layer, changedCellScratch[i].x, changedCellScratch[i].y, z);
        }
        else if (rebuild)
        {
            Rebuild();
        }

        Changed?.Invoke();
        return changed;
    }

    public void Fill(GameGridTerrainLayer layer, bool occupied, bool rebuild = true)
    {
        if (layer == null) return;
        EnsureData();
        var changed = false;
        var levelCount = LevelCount;
        for (var z = 0; z < levelCount; z++)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = GetCellIndex(x, y, z);
                    if (layer.GetCell(index) == occupied) continue;
                    layer.SetCell(index, occupied);
                    changed = true;
                }
            }
        }

        if (!changed) return;
        if (rebuild) Rebuild();
        Changed?.Invoke();
    }

    public void FillLevel(GameGridTerrainLayer layer, bool occupied, int z, bool rebuild = true)
    {
        if (layer == null || !IsLevelInBounds(z)) return;
        EnsureData();

        var changed = false;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = GetCellIndex(x, y, z);
                if (layer.GetCell(index) == occupied) continue;
                layer.SetCell(index, occupied);
                changed = true;
            }
        }

        if (!changed) return;
        if (rebuild) Rebuild();
        Changed?.Invoke();
    }

    public void ClearLevel(GameGridTerrainLayer layer, int z, bool rebuild = true)
    {
        FillLevel(layer, false, z, rebuild);
    }

    public void Clear(GameGridTerrainLayer layer, bool rebuild = true)
    {
        Fill(layer, false, rebuild);
    }

    public void ClearAll(bool rebuild = true)
    {
        EnsureData();
        if (terrainLayers == null) return;

        var changed = false;
        for (var i = 0; i < terrainLayers.Count; i++)
        {
            var layer = terrainLayers[i];
            if (layer == null) continue;

            var hadContent = false;
            var count = width * height * LevelCount;
            for (var index = 0; index < count; index++)
            {
                if (!layer.GetCell(index)) continue;
                hadContent = true;
                break;
            }

            if (!hadContent) continue;
            layer.FillCells(false);
            changed = true;
        }

        if (!changed) return;
        if (rebuild) Rebuild();
        Changed?.Invoke();
    }

    public void Resize(int newWidth, int newHeight, bool preserveContent = true)
    {
        newWidth = Mathf.Max(1, newWidth);
        newHeight = Mathf.Max(1, newHeight);
        if (newWidth == width && newHeight == height) return;

        width = newWidth;
        height = newHeight;
        EnsureData(preserveContent);
        rebuildPending = true;
        Changed?.Invoke();
    }

    public Vector2Int WorldToCell(Vector3 world)
    {
        var cell = WorldToCell3D(world, 0);
        return new Vector2Int(cell.x, cell.y);
    }

    public Vector3Int WorldToCell3D(Vector3 world, int z)
    {
        var local = transform.InverseTransformPoint(world);
        if (IsIsometric)
        {
            local.y -= z * LevelHeight;
            var tileSize = GetTileSize();
            var gridX = local.x / (tileSize.x * 0.5f);
            var gridY = local.y / (tileSize.y * 0.5f);
            return new Vector3Int(
                Mathf.FloorToInt((gridX + gridY) * 0.5f),
                Mathf.FloorToInt((gridY - gridX) * 0.5f),
                z);
        }

        return new Vector3Int(
            Mathf.FloorToInt(local.x / cellSize),
            Mathf.FloorToInt(local.y / cellSize),
            z);
    }

    public Vector3 CellToWorld(int x, int y)
    {
        return CellToWorld(x, y, 0);
    }

    public Vector3 CellToWorld(int x, int y, int z)
    {
        var local = IsIsometric
            ? IsometricFromGrid(x + 0.5f, y + 0.5f, z)
            : new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);
        return transform.TransformPoint(new Vector3(local.x, local.y, 0f));
    }

    public Vector3 CellCornerToWorld(int x, int y)
    {
        return CellCornerToWorld(x, y, 0);
    }

    public Vector3 CellCornerToWorld(int x, int y, int z)
    {
        var local = IsIsometric
            ? IsometricFromGrid(x, y, z)
            : new Vector2(x * cellSize, y * cellSize);
        return transform.TransformPoint(new Vector3(local.x, local.y, 0f));
    }

    public bool ContainsTileSet(GameGridTileSet tileSet)
    {
        if (tileSet == null || terrainLayers == null) return false;
        for (var i = 0; i < terrainLayers.Count; i++)
        {
            var layer = terrainLayers[i];
            if (layer?.TileSet == tileSet) return true;
        }

        return false;
    }

    public bool AddTileSet(GameGridTileSet set)
    {
        if (!IsTileSetCompatible(set)) return false;
        if (terrainLayers == null) terrainLayers = new List<GameGridTerrainLayer>();
        for (var i = 0; i < terrainLayers.Count; i++)
            if (terrainLayers[i]?.TileSet == set)
                return false;

        terrainLayers.Add(new GameGridTerrainLayer { tileSet = set });
        Rebuild();
        return true;
    }

    public bool RemoveTileSet(GameGridTileSet set)
    {
        if (terrainLayers == null || set == null) return false;

        var removed = false;
        for (var i = terrainLayers.Count - 1; i >= 0; i--)
        {
            if (terrainLayers[i]?.TileSet != set) continue;
            terrainLayers.RemoveAt(i);
            removed = true;
        }

        if (!removed) return false;
        EnsureLayerData(false);
        Rebuild();
        return true;
    }

    public void Rebuild()
    {
        if (this == null) return;
        rebuildPending = false;
        EnsureData();

        if (Application.isPlaying)
        {
            // Runtime tiles are driven by ShowRange; a rebuild just re-shows
            // the current range against fresh data.
            RefreshVisibleTiles();
            return;
        }

        ClearGenerated();
        if (terrainLayers == null || terrainLayers.Count == 0) return;

        for (var i = 0; i < terrainLayers.Count; i++)
        {
            var layer = terrainLayers[i];
            var set = layer?.TileSet;
            if (set == null || !layer.visible) continue;
            BuildTerrain(layer);
        }
    }

    private void OnEnable()
    {
        EnsureData();
        if (Application.isPlaying)
        {
            if (hasRange) RefreshVisibleTiles();
        }
        else
        {
            rebuildPending = true;
        }
    }

    private void OnDisable()
    {
        ClearRuntimeTiles();
        ClearGenerated();
        rebuildPending = false;
    }

    private void Update()
    {
        if (Application.isPlaying)
        {
            ProcessTileRecycling();
            return;
        }

#if UNITY_EDITOR
        if (!rebuildPending || !isActiveAndEnabled) return;
        Rebuild();
#endif
    }

    private void OnValidate()
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
        cellSize = Mathf.Max(Epsilon, cellSize);
        isometricTileSize = new Vector2(
            Mathf.Max(Epsilon, isometricTileSize.x),
            Mathf.Max(Epsilon, isometricTileSize.y));
        isometricLevels = tileType == GameGridTileType.Isometric ? Mathf.Max(1, isometricLevels) : 1;
        isometricLevelHeight = Mathf.Max(Epsilon, isometricLevelHeight);
        tileRecycleDelay = Mathf.Max(0f, tileRecycleDelay);
        if (terrainLayers != null)
        {
            for (var i = 0; i < terrainLayers.Count; i++)
                terrainLayers[i]?.TileSet?.EnsureSlots();
        }
#if UNITY_EDITOR
        if (editorBrushSize < 1) editorBrushSize = 1;
        editorBrushZ = Mathf.Clamp(editorBrushZ, 0, isometricLevels - 1);
#endif

        EnsureData();
        EnsureLayerData(false);
        if (Application.isPlaying) Rebuild();
        else rebuildPending = true;
    }

    private void OnDrawGizmosSelected()
    {
        if (this == null || width <= 0 || height <= 0) return;

        var oldMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        if (IsIsometric)
        {
            DrawIsometricGridGizmos();
            Gizmos.matrix = oldMatrix;
            return;
        }

        var size = new Vector3(width * cellSize, height * cellSize, 0f);

        Gizmos.color = new Color(1f, 1f, 1f, 0.8f);
        Gizmos.DrawWireCube(new Vector3(size.x * 0.5f, size.y * 0.5f, 0f), size);
        Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.35f);
        Gizmos.DrawWireCube(new Vector3(size.x * 0.5f, size.y * 0.5f, 0f),
            new Vector3(size.x + cellSize, size.y + cellSize, 0f));
        Gizmos.matrix = oldMatrix;
    }

    private void DrawIsometricGridGizmos()
    {
        var stepX = Mathf.Max(1, Mathf.CeilToInt(width / 80f));
        var stepY = Mathf.Max(1, Mathf.CeilToInt(height / 80f));
        Gizmos.color = new Color(1f, 1f, 1f, 0.35f);

        for (var x = 0; x <= width; x += stepX)
            Gizmos.DrawLine(
                IsometricFromGrid(x, 0f),
                IsometricFromGrid(x, height));

        for (var y = 0; y <= height; y += stepY)
            Gizmos.DrawLine(
                IsometricFromGrid(0f, y),
                IsometricFromGrid(width, y));

        if (!HasCornerLayoutLayer()) return;

        // Corner layout tiles are centered on the cell corners, so the
        // rendered tiles reach half a cell beyond the data area, matching
        // the rectangle mode outline.
        const float xMin = -0.5f;
        const float yMin = -0.5f;
        var xMax = width + 0.5f;
        var yMax = height + 0.5f;

        Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.65f);
        Gizmos.DrawLine(IsometricFromGrid(xMin, yMin), IsometricFromGrid(xMax, yMin));
        Gizmos.DrawLine(IsometricFromGrid(xMax, yMin), IsometricFromGrid(xMax, yMax));
        Gizmos.DrawLine(IsometricFromGrid(xMax, yMax), IsometricFromGrid(xMin, yMax));
        Gizmos.DrawLine(IsometricFromGrid(xMin, yMax), IsometricFromGrid(xMin, yMin));
    }

    private bool HasCornerLayoutLayer()
    {
        if (terrainLayers == null) return false;
        for (var i = 0; i < terrainLayers.Count; i++)
        {
            var set = terrainLayers[i] != null ? terrainLayers[i].TileSet : null;
            if (set != null && set.UsesCornerLayout) return true;
        }

        return false;
    }

    private bool InBounds(int x, int y)
    {
        return InBounds(x, y, 0);
    }

    private bool InBounds(int x, int y, int z)
    {
        return x >= 0 && y >= 0 && z >= 0 && x < width && y < height && z < LevelCount;
    }

    private bool IsLevelInBounds(int z)
    {
        return z >= 0 && z < LevelCount;
    }

    private int GetCellIndex(int x, int y, int z)
    {
        return z * width * height + y * width + x;
    }

    private int GetLayerIndex(GameGridTerrainLayer layer)
    {
        if (terrainLayers == null || layer == null) return -1;
        for (var i = 0; i < terrainLayers.Count; i++)
            if (ReferenceEquals(terrainLayers[i], layer)) return i;
        return -1;
    }

    private bool HasCell(GameGridTerrainLayer layer, int x, int y)
    {
        return HasCell(layer, x, y, 0);
    }

    private bool HasCell(GameGridTerrainLayer layer, int x, int y, int z)
    {
        return layer != null && InBounds(x, y, z) && layer.GetCell(GetCellIndex(x, y, z));
    }

    private void EnsureData(bool preserveContent = true)
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
        var count = width * height * LevelCount;
        var byteCount = (count + 7) >> 3;
        if (cellsWidth == width && cellsHeight == height && LayersAreSized(byteCount)) return;

        var oldWidth = Mathf.Max(0, cellsWidth);
        var oldHeight = Mathf.Max(0, cellsHeight);

        if (terrainLayers != null)
        {
            for (var i = 0; i < terrainLayers.Count; i++)
            {
                var layer = terrainLayers[i];
                if (layer == null) continue;
                ResizeLayerCells(layer, byteCount, oldWidth, oldHeight, preserveContent);
            }
        }

        cellsWidth = width;
        cellsHeight = height;
    }

    private bool LayersAreSized(int count)
    {
        if (terrainLayers == null) return true;
        for (var i = 0; i < terrainLayers.Count; i++)
        {
            var layer = terrainLayers[i];
            if (layer == null) continue;
            var cells = layer.Cells;
            if (cells == null || cells.Length != count) return false;
        }

        return true;
    }

    private void ResizeLayerCells(
        GameGridTerrainLayer layer,
        int byteCount,
        int oldWidth,
        int oldHeight,
        bool preserveContent)
    {
        var resized = new byte[byteCount];
        var oldCells = layer.Cells;
        var planeCount = width * height;
        var oldPlaneCount = oldWidth * oldHeight;
        var oldLevels = oldPlaneCount > 0 && oldCells is { Length: > 0 }
            ? (oldCells.Length << 3) / oldPlaneCount
            : 0;
        var newLevels = planeCount > 0 ? (byteCount << 3) / planeCount : 0;

        if (preserveContent && oldWidth > 0 && oldHeight > 0 && oldCells is { Length: > 0 })
        {
            var copyWidth = Mathf.Min(oldWidth, width);
            var copyHeight = Mathf.Min(oldHeight, height);
            for (var z = 0; z < Mathf.Min(oldLevels, newLevels); z++)
            {
                for (var y = 0; y < copyHeight; y++)
                {
                    // Rows are bit-packed with the global index
                    // z * plane + y * width, so row starts are generally not
                    // byte aligned and must be copied bit by bit.
                    var srcBitBase = z * oldPlaneCount + y * oldWidth;
                    var dstBitBase = z * planeCount + y * width;
                    for (var x = 0; x < copyWidth; x++)
                    {
                        var srcBit = srcBitBase + x;
                        if ((oldCells[srcBit >> 3] & (1 << (srcBit & 7))) == 0) continue;
                        var dstBit = dstBitBase + x;
                        resized[dstBit >> 3] |= (byte)(1 << (dstBit & 7));
                    }
                }
            }
        }

        layer.ReplaceCells(resized);
    }

    public void EnsureLayerData(bool rebuild = true)
    {
        EnsureData();
#if UNITY_EDITOR
        ValidateEditorBrush();
#endif
        if (rebuild) Rebuild();
        else rebuildPending = true;
    }

#if UNITY_EDITOR
    private void ValidateEditorBrush()
    {
        if (terrainLayers == null || terrainLayers.Count == 0)
        {
            editorBrushLayerIndex = -1;
            return;
        }

        if (editorBrushLayerIndex >= 0 && editorBrushLayerIndex < terrainLayers.Count) return;
        editorBrushLayerIndex = 0;

        editorBrushZ = Mathf.Clamp(editorBrushZ, 0, LevelCount - 1);
    }
#endif

    private void BuildTerrain(GameGridTerrainLayer layer)
    {
        var set = layer?.TileSet;
        if (set == null) return;

        var levelCount = IsIsometric ? LevelCount : 1;
        for (var z = 0; z < levelCount; z++)
        {
            if (!HasAnyCell(layer, z)) continue;

            var buffers = new Dictionary<Texture2D, MeshBuffers>();
            if (set.UsesCornerLayout) BuildDualGridTerrain(layer, z, buffers);
            else BuildBlobTerrain(layer, z, buffers);

            if (buffers.Count > 0) CreateTerrainObject(layer, buffers, z);
        }
    }

    private Vector2 GetTileSize()
    {
        return IsIsometric
            ? new Vector2(
                Mathf.Max(Epsilon, isometricTileSize.x),
                Mathf.Max(Epsilon, isometricTileSize.y))
            : Vector2.one * cellSize;
    }

    private Vector2 IsometricFromGrid(float x, float y, int z = 0)
    {
        var tileSize = GetTileSize();
        return new Vector2(
            (x - y) * tileSize.x * 0.5f,
            (x + y) * tileSize.y * 0.5f + z * LevelHeight);
    }

    private bool HasAnyCell(GameGridTerrainLayer layer, int z)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (HasCell(layer, x, y, z)) return true;
            }
        }

        return false;
    }

    private void BuildDualGridTerrain(
        GameGridTerrainLayer layer,
        int z,
        Dictionary<Texture2D, MeshBuffers> buffers)
    {
        var set = layer.TileSet;
        var tileSize = GetTileSize();
        var sortedDraws = IsIsometric ? new List<IsometricTileDraw>() : null;
        for (var y = 0; y <= height; y++)
        {
            for (var x = 0; x <= width; x++)
            {
                var mask = 0;
                if (HasCell(layer, x - 1, y - 1, z)) mask |= 1;
                if (HasCell(layer, x, y - 1, z)) mask |= 2;
                if (HasCell(layer, x - 1, y, z)) mask |= 4;
                if (HasCell(layer, x, y, z)) mask |= 8;
                if (mask == 0) continue;

                var selection = set.Resolve(mask, new Vector2Int(x, y));
                var sprite = selection.sprite;
                if (sprite == null || sprite.texture == null) continue;
                var anchor = IsIsometric
                    ? IsometricFromGrid(x, y, z)
                    : new Vector2(x * cellSize, y * cellSize);

                if (selection.IsAnimated)
                {
                    CreateAnimatedTile(layer, z, selection, anchor, tileSize);
                    continue;
                }

                if (sortedDraws != null)
                {
                    sortedDraws.Add(new IsometricTileDraw
                    {
                        sprite = sprite,
                        anchor = anchor,
                        depth = GetSortDepth(x, y),
                        diagonal = x - y
                    });
                    continue;
                }

                var key = sprite.texture;
                if (!buffers.TryGetValue(key, out var buffer))
                {
                    buffer = new MeshBuffers();
                    buffers.Add(key, buffer);
                }

                AppendSprite(
                    buffer,
                    sprite,
                    anchor,
                    tileSize,
                    set.tint);
            }
        }

        if (sortedDraws != null)
            AppendSortedIsometricTileDraws(sortedDraws, buffers, tileSize, set.tint);
    }

    private void BuildBlobTerrain(
        GameGridTerrainLayer layer,
        int z,
        Dictionary<Texture2D, MeshBuffers> buffers)
    {
        var set = layer.TileSet;
        var tileSize = GetTileSize();
        var sortedDraws = IsIsometric ? new List<IsometricTileDraw>() : null;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!HasCell(layer, x, y, z)) continue;

                var mask = GetBlobMask(x, y, z, layer);
                var selection = set.Resolve(mask, new Vector2Int(x, y));
                var sprite = selection.sprite;
                if (sprite == null || sprite.texture == null) continue;
                var anchor = IsIsometric
                    ? IsometricFromGrid(x + 0.5f, y + 0.5f, z)
                    : new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);

                if (selection.IsAnimated)
                {
                    CreateAnimatedTile(layer, z, selection, anchor, tileSize);
                    continue;
                }

                if (sortedDraws != null)
                {
                    sortedDraws.Add(new IsometricTileDraw
                    {
                        sprite = sprite,
                        anchor = anchor,
                        depth = GetSortDepth(x, y),
                        diagonal = x - y
                    });
                    continue;
                }

                var key = sprite.texture;
                if (!buffers.TryGetValue(key, out var buffer))
                {
                    buffer = new MeshBuffers();
                    buffers.Add(key, buffer);
                }

                AppendFallbackQuad(
                    buffer,
                    sprite,
                    anchor,
                    tileSize,
                    set.tint);
            }
        }

        if (sortedDraws != null)
            AppendSortedIsometricTileDraws(sortedDraws, buffers, tileSize, set.tint);
    }

    private void AppendSortedIsometricTileDraws(
        List<IsometricTileDraw> draws,
        Dictionary<Texture2D, MeshBuffers> buffers,
        Vector2 tileSize,
        Color tint)
    {
        draws.Sort((a, b) =>
        {
            var comparison = a.depth.CompareTo(b.depth);
            if (comparison != 0) return comparison;

            comparison = a.diagonal.CompareTo(b.diagonal);
            if (comparison != 0) return comparison;

            return a.anchor.y.CompareTo(b.anchor.y);
        });

        foreach (var draw in draws)
        {
            var key = draw.sprite.texture;
            if (!buffers.TryGetValue(key, out var buffer))
            {
                buffer = new MeshBuffers();
                buffers.Add(key, buffer);
            }

            AppendSprite(
                buffer,
                draw.sprite,
                draw.anchor,
                tileSize,
                tint);
        }
    }

    private int GetSortDepth(int x, int y)
    {
        switch (sortOrder)
        {
            case GameGridSortOrder.BottomRight: return (width - x) + y;
            case GameGridSortOrder.TopLeft: return x + (height - y);
            case GameGridSortOrder.TopRight: return (width - x) + (height - y);
            default: return x + y;
        }
    }

    private int GetBlobMask(int x, int y, int z, GameGridTerrainLayer layer)
    {
        const int n = 1;
        const int ne = 2;
        const int e = 4;
        const int se = 8;
        const int s = 16;
        const int sw = 32;
        const int w = 64;
        const int nw = 128;

        var hasN = HasCell(layer, x, y + 1, z);
        var hasE = HasCell(layer, x + 1, y, z);
        var hasS = HasCell(layer, x, y - 1, z);
        var hasW = HasCell(layer, x - 1, y, z);

        var mask = 0;
        if (hasN) mask |= n;
        if (hasE) mask |= e;
        if (hasS) mask |= s;
        if (hasW) mask |= w;

        if (hasN && hasE && HasCell(layer, x + 1, y + 1, z)) mask |= ne;
        if (hasE && hasS && HasCell(layer, x + 1, y - 1, z)) mask |= se;
        if (hasS && hasW && HasCell(layer, x - 1, y - 1, z)) mask |= sw;
        if (hasW && hasN && HasCell(layer, x - 1, y + 1, z)) mask |= nw;
        return mask;
    }

    private static void AppendSprite(
        MeshBuffers buffer,
        Sprite sprite,
        Vector2 anchor,
        Vector2 cellSize,
        Color tint)
    {
        var vertices = sprite.vertices;
        var uvs = sprite.uv;
        var triangles = sprite.triangles;
        if (vertices == null || vertices.Length == 0 || uvs == null || uvs.Length != vertices.Length)
        {
            AppendFallbackQuad(buffer, sprite, anchor, cellSize, tint);
            return;
        }

        var rect = sprite.rect;
        var localSize = sprite.bounds.size;
        var scaleX = cellSize.x / Mathf.Max(Epsilon, localSize.x);
        var scaleY = cellSize.y / Mathf.Max(Epsilon, localSize.y);
        var pivot = new Vector2(
            sprite.pivot.x / Mathf.Max(1f, rect.width),
            sprite.pivot.y / Mathf.Max(1f, rect.height));
        var pivotOffset = new Vector2(
            (pivot.x - 0.5f) * cellSize.x,
            (pivot.y - 0.5f) * cellSize.y);
        var baseIndex = buffer.vertices.Count;
        for (var i = 0; i < vertices.Length; i++)
        {
            buffer.vertices.Add(new Vector3(
                anchor.x + pivotOffset.x + vertices[i].x * scaleX,
                anchor.y + pivotOffset.y + vertices[i].y * scaleY,
                0f));
            buffer.uvs.Add(uvs[i]);
            buffer.colors.Add(tint);
        }

        if (triangles == null) return;
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            buffer.triangles.Add(baseIndex + triangles[i]);
            buffer.triangles.Add(baseIndex + triangles[i + 1]);
            buffer.triangles.Add(baseIndex + triangles[i + 2]);
        }
    }

    private static void AppendFallbackQuad(
        MeshBuffers buffer,
        Sprite sprite,
        Vector2 anchor,
        Vector2 cellSize,
        Color tint)
    {
        var texture = sprite.texture;
        var rect = sprite.textureRect;

        if (texture == null || rect.width <= 0f || rect.height <= 0f) return;

        var center = anchor;
        var half = new Vector2(cellSize.x * 0.5f, cellSize.y * 0.5f);
        var texel = new Vector2(1f / texture.width, 1f / texture.height);

        var baseIndex = buffer.vertices.Count;
        buffer.vertices.Add(new Vector3(center.x - half.x, center.y - half.y, 0f));
        buffer.vertices.Add(new Vector3(center.x - half.x, center.y + half.y, 0f));
        buffer.vertices.Add(new Vector3(center.x + half.x, center.y + half.y, 0f));
        buffer.vertices.Add(new Vector3(center.x + half.x, center.y - half.y, 0f));
        buffer.uvs.Add(new Vector2(rect.xMin * texel.x, rect.yMin * texel.y));
        buffer.uvs.Add(new Vector2(rect.xMin * texel.x, rect.yMax * texel.y));
        buffer.uvs.Add(new Vector2(rect.xMax * texel.x, rect.yMax * texel.y));
        buffer.uvs.Add(new Vector2(rect.xMax * texel.x, rect.yMin * texel.y));
        for (var i = 0; i < 4; i++)
        {
            buffer.colors.Add(tint);
        }
        buffer.triangles.Add(baseIndex);
        buffer.triangles.Add(baseIndex + 1);
        buffer.triangles.Add(baseIndex + 2);
        buffer.triangles.Add(baseIndex + 2);
        buffer.triangles.Add(baseIndex + 3);
        buffer.triangles.Add(baseIndex);
    }

    private void CreateAnimatedTile(
        GameGridTerrainLayer layer,
        int z,
        GameGridTileSelection selection,
        Vector2 anchor,
        Vector2 cellSize)
    {
        var sprite = selection.animation.GetFrame(0);
        if (sprite == null) return;

        var tileObject = new GameObject($"__Grid_Animated_{layer.TileSet.SafeName}_{z}_{Mathf.RoundToInt(anchor.x * 1000f)}_{Mathf.RoundToInt(anchor.y * 1000f)}")
        {
            hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy,
            layer = gameObject.layer
        };
        tileObject.transform.SetParent(transform, false);

        var rect = sprite.rect;
        var pivot = new Vector2(
            sprite.pivot.x / Mathf.Max(1f, rect.width),
            sprite.pivot.y / Mathf.Max(1f, rect.height));
        var pivotOffset = new Vector2(
            (pivot.x - 0.5f) * cellSize.x,
            (pivot.y - 0.5f) * cellSize.y);
        tileObject.transform.localPosition = anchor + pivotOffset;

        var bounds = sprite.bounds.size;
        tileObject.transform.localScale = new Vector3(
            cellSize.x / Mathf.Max(Epsilon, bounds.x),
            cellSize.y / Mathf.Max(Epsilon, bounds.y),
            1f);

        var spriteRenderer = tileObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.color = layer.TileSet.tint;
        spriteRenderer.sortingLayerID = ResolveSortingLayerId(layer.sortingLayerName);
        spriteRenderer.sortingOrder = layer.sortingOrder + z;
        spriteRenderer.shadowCastingMode = ShadowCastingMode.Off;
        spriteRenderer.receiveShadows = false;

        var material = layer.TileSet.materialTemplate;
        if (material != null)
        {
            spriteRenderer.sharedMaterial = material;
        }

        tileObject.AddComponent<GameGridAnimatedTile>().Setup(selection.animation);
        generatedObjects.Add(tileObject);
    }

    private void CreateTerrainObject(
        GameGridTerrainLayer layer,
        Dictionary<Texture2D, MeshBuffers> buffers,
        int z)
    {
        // Edit-mode paint preview keeps merged meshes; runtime rendering uses
        // the per-tile pool driven by ShowRange instead.
        var set = layer.TileSet;
        var objectName = GeneratedPrefix + set.SafeName + (z > 0 ? $"_Z{z}" : string.Empty);
        var meshObject = new GameObject(objectName)
        {
            layer = gameObject.layer
        };
        meshObject.transform.SetParent(transform, false);
        meshObject.transform.localPosition = Vector3.zero;
        meshObject.transform.localRotation = Quaternion.identity;
        meshObject.transform.localScale = Vector3.one;
        generatedObjects.Add(meshObject);

        var allVertices = new List<Vector3>();
        var allUvs = new List<Vector2>();
        var allColors = new List<Color32>();
        var subTriangles = new List<int>[buffers.Count];
        var materials = new Material[buffers.Count];
        var ownsMaterials = new bool[buffers.Count];

        var subMesh = 0;
        foreach (var pair in buffers)
        {
            var buffer = pair.Value;
            var vertexOffset = allVertices.Count;
            allVertices.AddRange(buffer.vertices);
            allUvs.AddRange(buffer.uvs);
            allColors.AddRange(buffer.colors);

            var triangles = new List<int>(buffer.triangles.Count);
            for (var i = 0; i < buffer.triangles.Count; i++)
                triangles.Add(vertexOffset + buffer.triangles[i]);
            subTriangles[subMesh] = triangles;
            materials[subMesh] = CreateMaterial(pair.Key, set.materialTemplate, out ownsMaterials[subMesh]);
            subMesh++;
        }
        var mesh = new Mesh
        {
            name = objectName + " Mesh",
            hideFlags = HideFlags.DontSave
        };
        if (allVertices.Count > LargeMeshVertexCount) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(allVertices);
        mesh.SetUVs(0, allUvs);
        mesh.SetColors(allColors);
        mesh.subMeshCount = buffers.Count;
        for (var i = 0; i < subTriangles.Length; i++)
            mesh.SetTriangles(subTriangles[i], i, false);
        mesh.RecalculateBounds();
        generatedMeshes.Add(mesh);
        for (var i = 0; i < materials.Length; i++)
            if (ownsMaterials[i]) generatedMaterials.Add(materials[i]);

        var meshFilter = meshObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;
        var meshRenderer = meshObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = materials;
        meshRenderer.sortingLayerID = ResolveSortingLayerId(layer.sortingLayerName);
        meshRenderer.sortingOrder = layer.sortingOrder + z;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private static Material CreateMaterial(Texture2D texture, Material template, out bool created)
    {
        if (template != null && MaterialUsesTexture(template, texture))
        {
            created = false;
            return template;
        }

        created = true;
        var material = template != null ? new Material(template) : new Material(GetDefaultSpriteShader());
        material.hideFlags = HideFlags.DontSave;
        material.name = texture != null ? texture.name + " Grid" : "Grid";
        if (texture != null)
        {
            material.mainTexture = texture;
            if (material.HasProperty(MainTexProperty)) material.SetTexture(MainTexProperty, texture);
        }

        return material;
    }

    private static bool MaterialUsesTexture(Material material, Texture2D texture)
    {
        if (material == null || texture == null) return false;
        if (material.mainTexture == texture) return true;
        return material.HasProperty(MainTexProperty) && material.GetTexture(MainTexProperty) == texture;
    }

    private static Shader GetDefaultSpriteShader()
    {
        if (defaultSpriteShader == null) defaultSpriteShader = Shader.Find("Sprites/Default");
        if (defaultSpriteShader == null)
            defaultSpriteShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
        if (defaultSpriteShader == null)
            defaultSpriteShader = Shader.Find("Unlit/Texture");
        return defaultSpriteShader;
    }

    private static int ResolveSortingLayerId(string sortingLayerName)
    {
        if (string.IsNullOrEmpty(sortingLayerName)) return 0;
        var layers = SortingLayer.layers;
        for (var i = 0; i < layers.Length; i++)
            if (layers[i].name == sortingLayerName)
                return layers[i].id;
        return 0;
    }

    private void ClearGenerated()
    {
        for (var i = 0; i < generatedObjects.Count; i++)
        {
            var generatedObject = generatedObjects[i];
            if (generatedObject == null) continue;
            if (Application.isPlaying) Destroy(generatedObject);
            else DestroyImmediate(generatedObject);
        }

        generatedObjects.Clear();

        for (var i = 0; i < generatedMeshes.Count; i++)
        {
            var mesh = generatedMeshes[i];
            if (mesh == null) continue;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }

        generatedMeshes.Clear();

        for (var i = 0; i < generatedMaterials.Count; i++)
        {
            var material = generatedMaterials[i];
            if (material == null) continue;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }

        generatedMaterials.Clear();

        // Earlier builds serialized generated objects into the scene while the
        // tracked list was runtime-only, so sweep stale "__Grid_" children left
        // behind by domain reloads or scene reloads.
        for (var i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child == null || !child.name.StartsWith(GeneratedPrefix, StringComparison.Ordinal)) continue;
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }
    }

    private void RefreshVisibleTiles()
    {
        RecycleAllTiles();
        hasPreviousShowRange = false;
        if (!hasRange) return;
        RefreshRangeTiles();
    }

    private void RefreshRangeTiles()
    {
        if (terrainLayers == null) return;

        var minX = Mathf.Max(0, rangeMinX);
        var maxX = Mathf.Min(width - 1, rangeMaxX);
        var minY = Mathf.Max(0, rangeMinY);
        var maxY = Mathf.Min(height - 1, rangeMaxY);
        var empty = minX > maxX || minY > maxY;
        var levelCount = IsIsometric ? LevelCount : 1;

        for (var i = 0; i < activeTiles.Count; i++)
        {
            var tile = activeTiles[i];
            var layer = tile.layerIndex >= 0 && tile.layerIndex < terrainLayers.Count
                ? terrainLayers[tile.layerIndex]
                : null;
            var inRange = !empty &&
                          layer != null && layer.visible && layer.TileSet != null &&
                          tile.z < levelCount &&
                          tile.x >= minX && tile.x <= maxX &&
                          tile.y >= minY && tile.y <= maxY;
            if (inRange)
            {
                if (tile.outOfRangeTime >= 0f)
                {
                    tile.outOfRangeTime = -1f;
                    pendingRecycleCount--;
                }
            }
            else if (tile.outOfRangeTime < 0f)
            {
                tile.outOfRangeTime = Time.time;
                pendingRecycleCount++;
            }
        }

        if (empty)
        {
            hasPreviousShowRange = false;
            return;
        }

        // Corner tiles one step beyond the rectangle keep the covered cells
        // seamless, matching the map edge behavior.
        var extendsCorners = HasCornerLayoutLayer();
        var showMaxX = extendsCorners ? Mathf.Min(width, maxX + 1) : maxX;
        var showMaxY = extendsCorners ? Mathf.Min(height, maxY + 1) : maxY;

        if (hasPreviousShowRange && activeTiles.Count > 0)
        {
            // Only newly exposed rows and columns need a scan; tiles already in
            // the map keep their configuration and exits were marked above.
            if (minX < prevShowMinX)
                ShowRangeRegion(minX, Mathf.Min(showMaxX, prevShowMinX - 1), minY, showMaxY, levelCount);
            if (showMaxX > prevShowMaxX)
                ShowRangeRegion(Mathf.Max(minX, prevShowMaxX + 1), showMaxX, minY, showMaxY, levelCount);
            if (minY < prevShowMinY)
                ShowRangeRegion(minX, showMaxX, minY, Mathf.Min(showMaxY, prevShowMinY - 1), levelCount);
            if (showMaxY > prevShowMaxY)
                ShowRangeRegion(minX, showMaxX, Mathf.Max(minY, prevShowMaxY + 1), showMaxY, levelCount);
        }
        else
        {
            ShowRangeRegion(minX, showMaxX, minY, showMaxY, levelCount);
        }

        prevShowMinX = minX;
        prevShowMaxX = showMaxX;
        prevShowMinY = minY;
        prevShowMaxY = showMaxY;
        hasPreviousShowRange = true;
    }

    private void ShowRangeRegion(int xMin, int xMax, int yMin, int yMax, int levelCount)
    {
        if (xMin > xMax || yMin > yMax) return;

        for (var i = 0; i < terrainLayers.Count; i++)
        {
            var layer = terrainLayers[i];
            var set = layer?.TileSet;
            if (set == null || !layer.visible) continue;

            for (var z = 0; z < levelCount; z++)
            {
                for (var y = yMin; y <= yMax; y++)
                {
                    for (var x = xMin; x <= xMax; x++)
                    {
                        if (set.UsesCornerLayout || HasCell(layer, x, y, z))
                        {
                            ShowTileAt(layer, i, z, x, y);
                        }
                    }
                }
            }
        }
    }

    private void ShowTileAt(GameGridTerrainLayer layer, int layerIndex, int z, int x, int y)
    {
        var key = (layerIndex, z, x, y);
        if (activeTileMap.ContainsKey(key)) return;

        var selection = ResolveTileSelection(layer, z, x, y, out var anchor);
        var sprite = selection.sprite;
        if (sprite == null || sprite.texture == null) return;

        var animated = selection.IsAnimated;
        var pool = animated ? animatedTilePool : staticTilePool;
        GameObject tileObject;
        if (pool.Count > 0)
        {
            tileObject = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
            tileObject.SetActive(true);
        }
        else
        {
            tileObject = new GameObject(animated ? "__Grid_AnimatedTile" : "__Grid_Tile")
            {
                hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy,
                layer = gameObject.layer
            };
            tileObject.transform.SetParent(transform, false);
            if (animated) tileObject.AddComponent<GameGridAnimatedTile>();
            tileObject.AddComponent<SpriteRenderer>();
        }

        RuntimeTile tile;
        if (runtimeTilePool.Count > 0)
        {
            tile = runtimeTilePool[runtimeTilePool.Count - 1];
            runtimeTilePool.RemoveAt(runtimeTilePool.Count - 1);
        }
        else
        {
            tile = new RuntimeTile();
        }

        tile.gameObject = tileObject;
        tile.spriteRenderer = tileObject.GetComponent<SpriteRenderer>();
        tile.animated = animated ? tileObject.GetComponent<GameGridAnimatedTile>() : null;
        tile.layerIndex = layerIndex;
        tile.z = z;
        tile.x = x;
        tile.y = y;
        tile.outOfRangeTime = -1f;
        ConfigureTile(tile, layer, z, x, y, selection, anchor);
        activeTiles.Add(tile);
        activeTileMap.Add(key, tile);
    }

    private GameGridTileSelection ResolveTileSelection(GameGridTerrainLayer layer, int z, int x, int y, out Vector2 anchor)
    {
        var set = layer.TileSet;
        var mask = 0;
        if (set.UsesCornerLayout)
        {
            if (HasCell(layer, x - 1, y - 1, z)) mask |= 1;
            if (HasCell(layer, x, y - 1, z)) mask |= 2;
            if (HasCell(layer, x - 1, y, z)) mask |= 4;
            if (HasCell(layer, x, y, z)) mask |= 8;
            anchor = IsIsometric
                ? IsometricFromGrid(x, y, z)
                : new Vector2(x * cellSize, y * cellSize);
        }
        else
        {
            if (HasCell(layer, x, y, z)) mask = GetBlobMask(x, y, z, layer);
            anchor = IsIsometric
                ? IsometricFromGrid(x + 0.5f, y + 0.5f, z)
                : new Vector2((x + 0.5f) * cellSize, (y + 0.5f) * cellSize);
        }

        if (mask == 0) return default;
        return set.Resolve(mask, new Vector2Int(x, y));
    }

    private void ConfigureTile(
        RuntimeTile tile,
        GameGridTerrainLayer layer,
        int z,
        int x,
        int y,
        GameGridTileSelection selection,
        Vector2 anchor)
    {
        var sprite = selection.sprite;
        var set = layer.TileSet;
        var tileSize = GetTileSize();

        var rect = sprite.rect;
        var pivot = new Vector2(
            sprite.pivot.x / Mathf.Max(1f, rect.width),
            sprite.pivot.y / Mathf.Max(1f, rect.height));
        var pivotOffset = new Vector2(
            (pivot.x - 0.5f) * tileSize.x,
            (pivot.y - 0.5f) * tileSize.y);

        var bounds = sprite.bounds.size;
        var tileTransform = tile.gameObject.transform;
        tileTransform.localPosition = anchor + pivotOffset;
        tileTransform.localScale = new Vector3(
            tileSize.x / Mathf.Max(Epsilon, bounds.x),
            tileSize.y / Mathf.Max(Epsilon, bounds.y),
            1f);

        var spriteRenderer = tile.spriteRenderer;
        spriteRenderer.sprite = sprite;
        spriteRenderer.color = set.tint;
        spriteRenderer.sortingLayerID = ResolveSortingLayerId(layer.sortingLayerName);
        spriteRenderer.sortingOrder = GetTileSortingOrder(layer, z, x, y);
        spriteRenderer.shadowCastingMode = ShadowCastingMode.Off;
        spriteRenderer.receiveShadows = false;

        if (tile.animated != null)
        {
            if (set.materialTemplate != null) spriteRenderer.sharedMaterial = set.materialTemplate;
            tile.animated.Setup(selection.animation);
        }
        else
        {
            spriteRenderer.sharedMaterial = ResolveTileMaterial(set, sprite.texture);
        }
    }

    private int GetTileSortingOrder(GameGridTerrainLayer layer, int z, int x, int y)
    {
        if (!IsIsometric) return layer.sortingOrder + z;
        var span = width + height + 2;
        return (layer.sortingOrder + z) * span + GetSortDepth(x, y);
    }

    private Material ResolveTileMaterial(GameGridTileSet set, Texture2D texture)
    {
        var template = set.materialTemplate;
        if (template != null && MaterialUsesTexture(template, texture)) return template;

        if (!tileMaterialCache.TryGetValue(template, out var byTexture))
        {
            byTexture = new Dictionary<Texture2D, Material>();
            tileMaterialCache.Add(template, byTexture);
        }

        if (byTexture.TryGetValue(texture, out var cached)) return cached;

        var material = CreateMaterial(texture, template, out _);
        byTexture.Add(texture, material);
        return material;
    }

    private void RefreshTilesForCellChange(GameGridTerrainLayer layer, int x, int y, int z)
    {
        var layerIndex = GetLayerIndex(layer);
        if (layerIndex < 0 || !IsLevelInBounds(z)) return;
        var set = layer.TileSet;
        if (set == null) return;

        if (set.UsesCornerLayout)
        {
            var minX = Mathf.Max(0, x);
            var maxX = Mathf.Min(width, x + 1);
            var minY = Mathf.Max(0, y);
            var maxY = Mathf.Min(height, y + 1);
            for (var cy = minY; cy <= maxY; cy++)
            {
                for (var cx = minX; cx <= maxX; cx++)
                    RefreshTileAt(layer, layerIndex, z, cx, cy);
            }

            return;
        }

        for (var cy = Mathf.Max(0, y - 1); cy <= Mathf.Min(height - 1, y + 1); cy++)
        {
            for (var cx = Mathf.Max(0, x - 1); cx <= Mathf.Min(width - 1, x + 1); cx++)
                RefreshTileAt(layer, layerIndex, z, cx, cy);
        }
    }

    private void RefreshTileAt(GameGridTerrainLayer layer, int layerIndex, int z, int x, int y)
    {
        var key = (layerIndex, z, x, y);
        if (!activeTileMap.TryGetValue(key, out var tile))
        {
            if (hasRange &&
                x >= rangeMinX && x <= rangeMaxX &&
                y >= rangeMinY && y <= rangeMaxY)
            {
                ShowTileAt(layer, layerIndex, z, x, y);
            }

            return;
        }

        var selection = ResolveTileSelection(layer, z, x, y, out var anchor);
        if (selection.sprite == null || selection.sprite.texture == null)
        {
            var tileIndex = activeTiles.IndexOf(tile);
            if (tileIndex >= 0) RecycleTile(tileIndex);
            return;
        }

        ConfigureTile(tile, layer, z, x, y, selection, anchor);
        if (tile.outOfRangeTime >= 0f)
        {
            tile.outOfRangeTime = -1f;
            pendingRecycleCount--;
        }
    }

    private void ProcessTileRecycling()
    {
        if (pendingRecycleCount == 0) return;
        var now = Time.time;
        for (var i = activeTiles.Count - 1; i >= 0; i--)
        {
            if (activeTiles[i].outOfRangeTime < 0f) continue;
            if (now - activeTiles[i].outOfRangeTime < tileRecycleDelay) continue;
            RecycleTile(i);
        }
    }

    private void RecycleAllTiles()
    {
        for (var i = activeTiles.Count - 1; i >= 0; i--)
            RecycleTile(i);
    }

    private void RecycleTile(int index)
    {
        var tile = activeTiles[index];
        activeTileMap.Remove((tile.layerIndex, tile.z, tile.x, tile.y));
        // Swap-and-pop keeps recycling O(1); iteration order does not matter.
        var last = activeTiles.Count - 1;
        activeTiles[index] = activeTiles[last];
        activeTiles.RemoveAt(last);

        tile.gameObject.SetActive(false);
        var pool = tile.animated != null ? animatedTilePool : staticTilePool;
        if (pool.Count < MaxPooledTileObjects) pool.Add(tile.gameObject);
        else DestroyGridObject(tile.gameObject);
        tile.gameObject = null;
        tile.spriteRenderer = null;
        tile.animated = null;
        runtimeTilePool.Add(tile);
    }

    private void ClearRuntimeTiles()
    {
        for (var i = activeTiles.Count - 1; i >= 0; i--)
        {
            var tile = activeTiles[i];
            activeTileMap.Remove((tile.layerIndex, tile.z, tile.x, tile.y));
            DestroyGridObject(tile.gameObject);
        }

        activeTiles.Clear();
        activeTileMap.Clear();
        runtimeTilePool.Clear();
        pendingRecycleCount = 0;
        hasPreviousShowRange = false;
        DestroyPoolList(staticTilePool);
        DestroyPoolList(animatedTilePool);
        ClearTileMaterialCache();
    }

    private void DestroyPoolList(List<GameObject> pool)
    {
        for (var i = 0; i < pool.Count; i++)
            DestroyGridObject(pool[i]);
        pool.Clear();
    }

    private void ClearTileMaterialCache()
    {
        foreach (var pair in tileMaterialCache)
        {
            foreach (var materialPair in pair.Value)
                DestroyGridObject(materialPair.Value);
            pair.Value.Clear();
        }

        tileMaterialCache.Clear();
    }

    private static void DestroyGridObject(UnityEngine.Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    private sealed class MeshBuffers
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<Color32> colors = new List<Color32>();
        public readonly List<int> triangles = new List<int>();
    }

    private sealed class IsometricTileDraw
    {
        public Sprite sprite;
        public Vector2 anchor;
        public int depth;
        public int diagonal;
    }

    private sealed class RuntimeTile
    {
        public GameObject gameObject;
        public SpriteRenderer spriteRenderer;
        public GameGridAnimatedTile animated;
        public int layerIndex;
        public int z;
        public int x;
        public int y;
        public float outOfRangeTime;
    }
}
