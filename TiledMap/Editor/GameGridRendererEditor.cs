using System.Collections.Generic;
using System.Numerics;
using UnityEditor;
using UnityEngine;
using UnityEditorInternal;
using Plane = UnityEngine.Plane;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

[CustomEditor(typeof(GameGridRenderer))]
public sealed class GameGridRendererEditor : Editor
{
    private const string ItemContainerName = "__Grid_Items";

    private SerializedProperty cellSizeProperty;
    private SerializedProperty tileTypeProperty;
    private SerializedProperty sortOrderProperty;
    private SerializedProperty isometricTileSizeProperty;
    private SerializedProperty isometricLevelsProperty;
    private SerializedProperty isometricLevelHeightProperty;
    private SerializedProperty tileRecycleDelayProperty;
    private SerializedProperty terrainLayersProperty;
    private ReorderableList terrainLayersList;

    private readonly List<Vector2Int> brushCells = new List<Vector2Int>(64);
    private Vector2Int lastPaintCell;
    private bool painting;
    private bool eraseStroke;
    private bool itemPainting;
    private GameObject itemPreviewObject;
    private GameObject itemPreviewPrefab;
    private Vector2 scenePanelScroll;
    private static Rect scenePanelRect = new Rect(16f, 90f, 300f, 360f);

    private void OnEnable()
    {
        cellSizeProperty = serializedObject.FindProperty("cellSize");
        tileTypeProperty = serializedObject.FindProperty("tileType");
        sortOrderProperty = serializedObject.FindProperty("sortOrder");
        isometricTileSizeProperty = serializedObject.FindProperty("isometricTileSize");
        isometricLevelsProperty = serializedObject.FindProperty("isometricLevels");
        isometricLevelHeightProperty = serializedObject.FindProperty("isometricLevelHeight");
        tileRecycleDelayProperty = serializedObject.FindProperty("tileRecycleDelay");
        terrainLayersProperty = serializedObject.FindProperty("terrainLayers");
        terrainLayersList = new ReorderableList(
            serializedObject,
            terrainLayersProperty,
            true,
            true,
            true,
            true);
        terrainLayersList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Terrain Layers");
        terrainLayersList.drawElementCallback = (rect, index, isActive, isFocused) =>
            DrawTerrainLayerElement(rect, index);
        terrainLayersList.elementHeight = EditorGUIUtility.singleLineHeight * 6f +
            EditorGUIUtility.standardVerticalSpacing * 7f + 8f;
        terrainLayersList.onAddCallback = list => AddTerrainLayerElement();
        terrainLayersList.onRemoveCallback = list =>
        {
            ReorderableList.defaultBehaviours.DoRemoveButton(list);
            GUI.changed = true;
        };
    }

    public override void OnInspectorGUI()
    {
        var renderer = (GameGridRenderer)target;
        DrawEditingPanel(renderer);
    }

    private void OnSceneGUI()
    {
        var renderer = (GameGridRenderer)target;
        if (renderer == null || !renderer.isActiveAndEnabled) return;

        DrawScenePanel();

        if (!renderer.EditorItemMode)
        {
            itemPainting = false;
            HideItemPreview();
        }

        if (renderer.EditorItemMode)
        {
            return;
        }

        var e = Event.current;
        if (scenePanelRect.Contains(e.mousePosition))
        {
            painting = false;
            itemPainting = false;
            HideItemPreview();
            return;
        }

        DrawSceneGrid(renderer);
        var controlId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlId);
        if (e.type == EventType.KeyDown && !e.control && !e.alt) HandleHotkeys(renderer, e);
        if (!TryGetHoverCell(renderer, e.mousePosition, out var cell))
        {
            HideItemPreview();
            return;
        }

        if (renderer.EditorItemMode)
        {
            HandleItemEditing(renderer, cell, e);
            return;
        }

        HideItemPreview();
        DrawBrushPreview(renderer, cell, e.shift);
        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0:
                painting = true;
                eraseStroke = e.shift;
                Undo.RecordObject(renderer, "Paint Grid");
                lastPaintCell = cell;
                PaintAt(renderer, cell, eraseStroke);
                e.Use();
                break;
            case EventType.MouseDrag when e.button == 0 && painting:
                PaintLine(renderer, lastPaintCell, cell, eraseStroke);
                lastPaintCell = cell;
                e.Use();
                break;
            case EventType.MouseUp when e.button == 0 && painting:
                painting = false;
                e.Use();
                break;
            case EventType.MouseLeaveWindow:
                painting = false;
                break;
        }
    }

    private void DrawScenePanel()
    {
        Handles.BeginGUI();
        scenePanelRect = GUILayout.Window(
            GetInstanceID(),
            scenePanelRect,
            DrawScenePanelWindow,
            "Game Grid Tools",
            GUILayout.Width(300f),
            GUILayout.Height(360f));
        Handles.EndGUI();
    }

    private void DrawScenePanelWindow(int windowId)
    {
        var dragHeight = EditorGUIUtility.singleLineHeight + 8f;
        GUILayout.Space(dragHeight);
        GUI.DragWindow(new Rect(0f, 0f, 10000f, dragHeight));

        scenePanelScroll = GUILayout.BeginScrollView(scenePanelScroll);
        DrawEditingPanel((GameGridRenderer)target);
        GUILayout.EndScrollView();
    }

    private void DrawEditingPanel(GameGridRenderer renderer)
    {
        GUI.changed = false;
        serializedObject.Update();

        EditorGUILayout.LabelField("Grid", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(tileTypeProperty, new GUIContent("Tile Type"));
        EditorGUILayout.PropertyField(sortOrderProperty, new GUIContent("Sort Order"));
        DrawGridSizeFields(renderer);
        if (renderer.IsIsometric)
        {
            EditorGUILayout.PropertyField(
                isometricTileSizeProperty,
                new GUIContent("Isometric Tile Size"));
            EditorGUILayout.PropertyField(isometricLevelsProperty, new GUIContent("Z Levels"));
            EditorGUILayout.PropertyField(
                isometricLevelHeightProperty,
                new GUIContent("Level Height"));
        }
        else
        {
            EditorGUILayout.PropertyField(cellSizeProperty);
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Runtime Tiles", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            tileRecycleDelayProperty,
            new GUIContent(
                "Tile Recycle Delay",
                "Seconds before tiles outside the visible range are recycled into the pool."));

        EditorGUILayout.Space(6f);
        terrainLayersList.DoLayoutList();
        if (GUILayout.Button("Add Terrain Layer"))
        {
            GameGridTileSetPickerWindow.Show(renderer, set => AddTerrainLayer(renderer, set));
        }

        var propertiesChanged = GUI.changed;
        var applied = serializedObject.ApplyModifiedProperties();
        if (propertiesChanged || applied)
        {
            renderer.EnsureLayerData(false);
            renderer.Rebuild();
        }

        DrawTools(renderer);
    }

    private static void DrawGridSizeFields(GameGridRenderer renderer)
    {
        // Delayed on purpose: resizing reallocates all layer cells and
        // rebuilds the preview, which should only happen on commit (Enter or
        // focus loss), not on every keystroke.
        EditorGUI.BeginChangeCheck();
        var newWidth = EditorGUILayout.DelayedIntField(new GUIContent("Width"), renderer.Width);
        var newHeight = EditorGUILayout.DelayedIntField(new GUIContent("Height"), renderer.Height);
        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(renderer, "Resize Grid");
        renderer.Resize(newWidth, newHeight);
        EditorUtility.SetDirty(renderer);
        renderer.Rebuild();
        SceneView.RepaintAll();
    }

    [MenuItem("GameObject/2D Object/Game Grid Renderer", false, 30)]
    private static void CreateDualGridRenderer(MenuCommand command)
    {
        var gameObject = new GameObject("Game Grid Renderer");
        GameObjectUtility.SetParentAndAlign(gameObject, command.context as GameObject);
        Undo.RegisterCreatedObjectUndo(gameObject, "Create " + gameObject.name);
        gameObject.AddComponent<GameGridRenderer>();
        Selection.activeObject = gameObject;
    }

    private void AddTerrainLayer(GameGridRenderer renderer, GameGridTileSet tileSet)
    {
        if (tileSet == null) return;
        if (!renderer.IsTileSetCompatible(tileSet))
        {
            Debug.LogWarning(
                $"Grid Renderer: '{tileSet.name}' is {tileSet.TileType}, but this renderer requires {renderer.TileType}.",
                renderer);
            return;
        }

        Undo.RecordObject(renderer, "Add Grid Terrain");
        var index = terrainLayersProperty.arraySize;
        terrainLayersProperty.arraySize++;
        var element = terrainLayersProperty.GetArrayElementAtIndex(index);
        element.FindPropertyRelative("tileSet").objectReferenceValue = tileSet;
        element.FindPropertyRelative("visible").boolValue = true;
        element.FindPropertyRelative("sortingLayerName").stringValue = "Default";
        element.FindPropertyRelative("sortingOrder").intValue = 0;
        renderer.EditorBrushLayerIndex = index;
        EditorUtility.SetDirty(renderer);
        renderer.EnsureLayerData(false);
        serializedObject.ApplyModifiedProperties();
        renderer.Rebuild();
        GUI.changed = true;
    }

    private void AddTerrainLayerElement()
    {
        var renderer = (GameGridRenderer)target;
        Undo.RecordObject(renderer, "Add Grid Terrain");
        var index = terrainLayersProperty.arraySize;
        terrainLayersProperty.arraySize++;
        var element = terrainLayersProperty.GetArrayElementAtIndex(index);
        element.FindPropertyRelative("tileSet").objectReferenceValue = null;
        element.FindPropertyRelative("visible").boolValue = true;
        element.FindPropertyRelative("sortingLayerName").stringValue = "Default";
        element.FindPropertyRelative("sortingOrder").intValue = 0;
        renderer.EditorBrushLayerIndex = index;
        renderer.EnsureLayerData(false);
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(renderer);
        GUI.changed = true;
    }

    private void DrawTerrainLayerElement(Rect position, int index)
    {
        var renderer = (GameGridRenderer)target;
        var element = terrainLayersProperty.GetArrayElementAtIndex(index);
        var tileSetProperty = element.FindPropertyRelative("tileSet");
        var visibleProperty = element.FindPropertyRelative("visible");
        var sortingLayerNameProperty = element.FindPropertyRelative("sortingLayerName");
        var sortingOrderProperty = element.FindPropertyRelative("sortingOrder");

        var line = EditorGUIUtility.singleLineHeight;
        var spacing = EditorGUIUtility.standardVerticalSpacing;
        var tileSetRect = new Rect(
            position.x + 4f,
            position.y + 4f,
            position.width - 8f,
            line);
        DrawTileSetPropertyField(tileSetRect, tileSetProperty, renderer);

        var visibleRect = new Rect(
            tileSetRect.x,
            tileSetRect.yMax + spacing,
            position.width - 8f,
            line);
        EditorGUI.PropertyField(visibleRect, visibleProperty, new GUIContent("Visible"));

        var sortingRect = new Rect(
            tileSetRect.x,
            visibleRect.yMax + spacing,
            position.width - 8f,
            line);
        DrawSortingLayerPopup(sortingRect, sortingLayerNameProperty);

        var orderRect = new Rect(
            tileSetRect.x,
            sortingRect.yMax + spacing,
            position.width - 8f,
            line);
        sortingOrderProperty.intValue = EditorGUI.IntField(orderRect, "SortingOrder", sortingOrderProperty.intValue);

        DrawTerrainLayerStatistics(
            new Rect(
                tileSetRect.x,
                orderRect.yMax + spacing + 2f,
                position.width - 8f,
                (line + spacing) * 2f),
            renderer,
            index);
    }

    private void DrawTerrainLayerStatistics(Rect position, GameGridRenderer renderer, int index)
    {
        var line = EditorGUIUtility.singleLineHeight;
        var spacing = EditorGUIUtility.standardVerticalSpacing;
        var cellsRect = new Rect(position.x, position.y, position.width, line);
        var tilesRect = new Rect(position.x, cellsRect.yMax + spacing, position.width, line);

        var layer = index >= 0 && index < renderer.TerrainLayers.Count
            ? renderer.TerrainLayers[index]
            : null;
        var filled = layer != null ? renderer.CountCells(layer) : 0;
        EditorGUI.LabelField(
            cellsRect,
            $"Cells: {filled}/{renderer.Width * renderer.Height * renderer.LevelCount}",
            EditorStyles.miniLabel);

        var set = layer?.TileSet;
        if (set == null)
        {
            EditorGUI.LabelField(tilesRect, "Tile slots: Tile Set missing", EditorStyles.miniLabel);
            return;
        }

        var spriteCount = set.SpriteCount;
        var missing = 0;
        for (var tileIndex = 0; tileIndex < spriteCount; tileIndex++)
            if (set.GetSpriteAt(tileIndex) == null) missing++;

        EditorGUI.LabelField(tilesRect, $"Tile slots: {spriteCount - missing}/{spriteCount}", EditorStyles.miniLabel);
    }

    private static void DrawTileSetPropertyField(
        Rect position,
        SerializedProperty property,
        GameGridRenderer renderer)
    {
        EditorGUI.BeginProperty(position, new GUIContent("Tile Set"), property);

        var fieldRect = EditorGUI.PrefixLabel(position, new GUIContent("Tile Set"));
        var tileSet = property.objectReferenceValue as GameGridTileSet;
        var icon = tileSet != null ? AssetPreview.GetMiniThumbnail(tileSet) : null;
        var buttonText = tileSet != null ? tileSet.name : "None (GameGridTileSet)";
        if (GUI.Button(fieldRect, new GUIContent(buttonText, icon), EditorStyles.objectField))
        {
            GUI.FocusControl(null);
            GameGridTileSetPickerWindow.Show(renderer, selected =>
            {
                property.serializedObject.Update();
                property.objectReferenceValue = selected;
                property.serializedObject.ApplyModifiedProperties();
                renderer.EnsureLayerData(false);
                renderer.Rebuild();
                EditorUtility.SetDirty(renderer);
                GUI.changed = true;
            });
        }

        if (tileSet != null && !renderer.IsTileSetCompatible(tileSet))
        {
            var warningRect = new Rect(fieldRect.xMax + 2f, position.y + 1f, 14f, position.height);
            GUI.Label(warningRect, EditorGUIUtility.IconContent("console.warnicon"));
        }

        EditorGUI.EndProperty();
    }

    private static void DrawSortingLayerPopup(Rect rect, SerializedProperty property)
    {
        var current = property.stringValue;
        if (string.IsNullOrEmpty(current)) current = "Default";

        var layers = SortingLayer.layers;
        var names = new string[layers.Length + 2];
        var currentIndex = -1;
        for (var i = 0; i < layers.Length; i++)
        {
            names[i] = layers[i].name;
            if (layers[i].name == current) currentIndex = i;
        }

        if (currentIndex < 0)
        {
            names[layers.Length] = current + " (Missing)";
            currentIndex = layers.Length;
        }

        names[layers.Length + 1] = "Add Sorting Layer...";

        const float labelWidth = 88f;
        var labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
        var popupRect = new Rect(
            labelRect.xMax + 4f,
            rect.y,
            Mathf.Max(0f, rect.width - labelWidth - 4f),
            rect.height);
        EditorGUI.LabelField(labelRect, "Sorting Layer");
        var newIndex = EditorGUI.Popup(popupRect, currentIndex, names);
        if (newIndex == layers.Length + 1)
        {
            SettingsService.OpenProjectSettings("Project/Tags and Layers");
            GUIUtility.ExitGUI();
        }

        if (newIndex != currentIndex) property.stringValue = names[newIndex];
    }

    private static void DrawTools(GameGridRenderer renderer)
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Scene Tools", EditorStyles.boldLabel);

        var showGrid = EditorGUILayout.ToggleLeft("Show Grid In Scene", renderer.EditorShowGrid);
        if (showGrid != renderer.EditorShowGrid)
        {
            renderer.EditorShowGrid = showGrid;
            EditorUtility.SetDirty(renderer);
            SceneView.RepaintAll();
        }

        var itemMode = EditorGUILayout.ToggleLeft("Item Mode (Place Props)", renderer.EditorItemMode);
        if (itemMode != renderer.EditorItemMode)
        {
            renderer.EditorItemMode = itemMode;
            EditorUtility.SetDirty(renderer);
            SceneView.RepaintAll();
        }

        var layers = renderer.TerrainLayers;
        if (layers == null || layers.Count == 0)
        {
            EditorGUILayout.HelpBox("Add a terrain layer to paint.", MessageType.Info);
            return;
        }

        var options = new string[layers.Count];
        var currentIndex = Mathf.Clamp(renderer.EditorBrushLayerIndex, 0, layers.Count - 1);
        for (var i = 0; i < layers.Count; i++)
        {
            var set = layers[i]?.TileSet;
            options[i] = set != null ? $"{i}: {set.SafeName}" : $"{i}: Missing";
        }

        var newIndex = EditorGUILayout.Popup("Brush Layer", currentIndex, options);
        if (newIndex != currentIndex)
        {
            renderer.EditorBrushLayerIndex = newIndex;
            EditorUtility.SetDirty(renderer);
            SceneView.RepaintAll();
        }

        if (renderer.IsIsometric)
        {
            var brushZ = EditorGUILayout.IntSlider(
                "Brush Z",
                renderer.EditorBrushZ,
                0,
                Mathf.Max(0, renderer.LevelCount - 1));
            if (brushZ != renderer.EditorBrushZ)
            {
                renderer.EditorBrushZ = brushZ;
                EditorUtility.SetDirty(renderer);
                SceneView.RepaintAll();
            }
        }

        var brushSize = EditorGUILayout.IntSlider("Brush Size", renderer.EditorBrushSize, 1, 32);
        if (brushSize != renderer.EditorBrushSize)
        {
            renderer.EditorBrushSize = brushSize;
            EditorUtility.SetDirty(renderer);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Fill Brush Terrain"))
        {
            Undo.RecordObject(renderer, "Fill Grid");
            renderer.FillLevel(GetBrushLayer(renderer), true, renderer.EditorBrushZ, true);
            EditorUtility.SetDirty(renderer);
        }
        if (GUILayout.Button("Clear Brush Level"))
        {
            Undo.RecordObject(renderer, "Clear Grid");
            renderer.ClearLevel(GetBrushLayer(renderer), renderer.EditorBrushZ, true);
            EditorUtility.SetDirty(renderer);
        }
        EditorGUILayout.EndHorizontal();
        if (GUILayout.Button("Rebuild Preview")) renderer.Rebuild();

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Item Brush", EditorStyles.boldLabel);
        var brushPrefab = GameGridItemBrushState.SelectedPrefab;
        EditorGUILayout.LabelField(
            "Brush Item",
            brushPrefab != null ? brushPrefab.name : "None");
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Open Item Palette")) GameGridItemPaletteWindow.ShowWindow();
        if (GUILayout.Button("Clear All Items")) ClearAllItems(renderer);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(
            "Scene: drag the panel title to move it; left-drag paints, Shift+left-drag erases, [ and ] switch layer.\n" +
            "Item Mode: left-click places the selected item, Shift+left-drag erases items.",
            MessageType.None);
    }

    private static GameGridTerrainLayer GetBrushLayer(GameGridRenderer renderer)
    {
        var layers = renderer.TerrainLayers;
        var index = renderer.EditorBrushLayerIndex;
        if (layers == null || index < 0 || index >= layers.Count) return null;
        return layers[index];
    }

    private static void DrawSceneGrid(GameGridRenderer renderer)
    {
        if (!renderer.EditorShowGrid) return;

        var stepX = Mathf.Max(1, Mathf.CeilToInt(renderer.Width / 80f));
        var stepY = Mathf.Max(1, Mathf.CeilToInt(renderer.Height / 80f));

        if (renderer.IsIsometric)
        {
            Handles.color = new Color(1f, 1f, 1f, 0.16f);
            for (var x = 0; x <= renderer.Width; x += stepX)
                Handles.DrawLine(
                    renderer.CellCornerToWorld(x, 0),
                    renderer.CellCornerToWorld(x, renderer.Height));

            for (var y = 0; y <= renderer.Height; y += stepY)
                Handles.DrawLine(
                    renderer.CellCornerToWorld(0, y),
                    renderer.CellCornerToWorld(renderer.Width, y));

            Handles.color = new Color(0.2f, 1f, 0.5f, 0.5f);
            Handles.DrawLine(renderer.CellCornerToWorld(0, 0), renderer.CellCornerToWorld(renderer.Width, 0));
            Handles.DrawLine(renderer.CellCornerToWorld(renderer.Width, 0), renderer.CellCornerToWorld(renderer.Width, renderer.Height));
            Handles.DrawLine(renderer.CellCornerToWorld(renderer.Width, renderer.Height), renderer.CellCornerToWorld(0, renderer.Height));
            Handles.DrawLine(renderer.CellCornerToWorld(0, renderer.Height), renderer.CellCornerToWorld(0, 0));
            return;
        }

        var oldMatrix = Handles.matrix;
        Handles.matrix = renderer.transform.localToWorldMatrix;
        var cellSize = renderer.CellSize;
        var width = renderer.Width * cellSize;
        var height = renderer.Height * cellSize;

        Handles.color = new Color(1f, 1f, 1f, 0.12f);
        for (var x = 0; x <= renderer.Width; x += stepX)
            Handles.DrawLine(new Vector3(x * cellSize, 0f, 0f), new Vector3(x * cellSize, height, 0f));
        for (var y = 0; y <= renderer.Height; y += stepY)
            Handles.DrawLine(new Vector3(0f, y * cellSize, 0f), new Vector3(width, y * cellSize, 0f));

        Handles.matrix = oldMatrix;
    }

    private static bool TryGetHoverCell(GameGridRenderer renderer, Vector2 mousePosition, out Vector2Int cell)
    {
        var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        var plane = new Plane(renderer.transform.forward, renderer.transform.position);
        if (!plane.Raycast(ray, out var enter))
        {
            cell = default;
            return false;
        }

        var cell3D = renderer.WorldToCell3D(ray.GetPoint(enter), 0);
        cell = new Vector2Int(cell3D.x, cell3D.y);
        return cell.x >= 0 && cell.y >= 0 && cell.x < renderer.Width && cell.y < renderer.Height;
    }

    private void DrawBrushPreview(GameGridRenderer renderer, Vector2Int center, bool erase)
    {
        var size = renderer.EditorBrushSize;
        var offset = (size - 1) / 2;
        var minX = center.x - offset;
        var minY = center.y - offset;
        var maxX = minX + size - 1;
        var maxY = minY + size - 1;
        var p0 = renderer.CellCornerToWorld(minX, minY, renderer.EditorBrushZ);
        var p1 = renderer.CellCornerToWorld(maxX + 1, minY, renderer.EditorBrushZ);
        var p2 = renderer.CellCornerToWorld(maxX + 1, maxY + 1, renderer.EditorBrushZ);
        var p3 = renderer.CellCornerToWorld(minX, maxY + 1, renderer.EditorBrushZ);

        Color face;
        Color outline;
        if (erase)
        {
            face = new Color(1f, 0.2f, 0.2f, 0.14f);
            outline = new Color(1f, 0.2f, 0.2f, 0.9f);
        }
        else
        {
            var set = GetBrushLayer(renderer)?.TileSet;
            var tint = set != null ? set.tint : new Color(0.3f, 0.7f, 1f);
            face = new Color(tint.r, tint.g, tint.b, 0.16f);
            outline = new Color(tint.r, tint.g, tint.b, 0.9f);
        }

        Handles.DrawSolidRectangleWithOutline(new[] { p0, p1, p2, p3 }, face, outline);
        if (renderer.IsIsometric && renderer.EditorBrushZ > 0)
        {
            var b0 = renderer.CellCornerToWorld(minX, minY, 0);
            var b1 = renderer.CellCornerToWorld(maxX + 1, minY, 0);
            var b2 = renderer.CellCornerToWorld(maxX + 1, maxY + 1, 0);
            var b3 = renderer.CellCornerToWorld(minX, maxY + 1, 0);

            var baseColor = outline;
            baseColor.a *= 0.55f;
            Handles.color = baseColor;
            Handles.DrawPolyLine(b0, b1, b2, b3, b0);

            Handles.color = outline;
            Handles.DrawDottedLine(p0, b0, 4f);
            Handles.DrawDottedLine(p1, b1, 4f);
            Handles.DrawDottedLine(p2, b2, 4f);
            Handles.DrawDottedLine(p3, b3, 4f);
        }

        if (size == 1) Handles.Label(p2 + Vector3.up * 0.2f, center.ToString());
    }

    private void PaintAt(GameGridRenderer renderer, Vector2Int center, bool erase)
    {
        brushCells.Clear();
        AppendBrushCells(renderer, center);
        ApplyBrushCells(renderer, erase, renderer.EditorBrushZ);
    }

    private void PaintLine(GameGridRenderer renderer, Vector2Int from, Vector2Int to, bool erase)
    {
        brushCells.Clear();
        var dx = to.x - from.x;
        var dy = to.y - from.y;
        var steps = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (float)steps;
            AppendBrushCells(renderer, Vector2Int.RoundToInt(new Vector2(from.x + dx * t, from.y + dy * t)));
        }
        ApplyBrushCells(renderer, erase, renderer.EditorBrushZ);
    }

    private void AppendBrushCells(GameGridRenderer renderer, Vector2Int center)
    {
        var size = renderer.EditorBrushSize;
        var offset = (size - 1) / 2;
        var minX = center.x - offset;
        var minY = center.y - offset;
        for (var y = minY; y < minY + size; y++)
            for (var x = minX; x < minX + size; x++)
                brushCells.Add(new Vector2Int(x, y));
    }

    private void ApplyBrushCells(GameGridRenderer renderer, bool erase, int brushZ)
    {
        if (brushCells.Count == 0) return;
        var layer = GetBrushLayer(renderer);
        if (layer == null) return;
        if (renderer.SetCells(layer, brushCells, !erase, brushZ, false) <= 0) return;

        renderer.Rebuild();
        EditorUtility.SetDirty(renderer);
        SceneView.RepaintAll();
    }

    private void HandleItemEditing(GameGridRenderer renderer, Vector2Int cell, Event e)
    {
        var prefab = GameGridItemBrushState.SelectedPrefab;
        var z = renderer.EditorBrushZ;
        var worldPosition = renderer.CellToWorld(cell.x, cell.y, z);
        UpdateItemPreview(prefab, worldPosition);
        DrawItemCellOutline(renderer, cell, z, prefab == null);
        if (prefab == null) Handles.Label(worldPosition, "Select an item in the Item Palette");

        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0:
                itemPainting = true;
                lastPaintCell = cell;
                if (e.shift) EraseItemsAt(renderer, cell, z);
                else if (prefab != null) PlaceItemAt(renderer, cell, z, prefab);
                e.Use();
                break;
            case EventType.MouseDrag when e.button == 0 && itemPainting:
                PaintItemLine(renderer, lastPaintCell, cell, e.shift, prefab);
                lastPaintCell = cell;
                e.Use();
                break;
            case EventType.MouseUp when e.button == 0 && itemPainting:
                itemPainting = false;
                e.Use();
                break;
            case EventType.MouseLeaveWindow:
                itemPainting = false;
                break;
        }
    }

    private static void PaintItemLine(
        GameGridRenderer renderer,
        Vector2Int from,
        Vector2Int to,
        bool erase,
        GameObject prefab)
    {
        var z = renderer.EditorBrushZ;
        var dx = to.x - from.x;
        var dy = to.y - from.y;
        var steps = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (float)steps;
            var cell = Vector2Int.RoundToInt(new Vector2(from.x + dx * t, from.y + dy * t));
            if (erase) EraseItemsAt(renderer, cell, z);
            else if (prefab != null) PlaceItemAt(renderer, cell, z, prefab);
        }
    }

    private static void PlaceItemAt(GameGridRenderer renderer, Vector2Int cell, int z, GameObject prefab)
    {
        var container = GetOrCreateItemContainer(renderer);
        if (FindItemAt(container, cell, z, prefab) != null) return;

        var itemObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (itemObject == null) itemObject = new GameObject(prefab.name);
        itemObject.transform.SetParent(container, false);
        Undo.RegisterCreatedObjectUndo(itemObject, "Place Grid Item");
        itemObject.transform.position = renderer.CellToWorld(cell.x, cell.y, z);

        var marker = itemObject.AddComponent<GameGridItemInstance>();
        marker.cell = new Vector3Int(cell.x, cell.y, z);
        marker.prefab = prefab;
    }

    private static void EraseItemsAt(GameGridRenderer renderer, Vector2Int cell, int z)
    {
        var container = FindItemContainer(renderer);
        if (container == null) return;
        EraseItemsAt(container, cell, z);
    }

    private static void EraseItemsAt(Transform container, Vector2Int cell, int z)
    {
        for (var i = container.childCount - 1; i >= 0; i--)
        {
            var child = container.GetChild(i);
            var marker = child.GetComponent<GameGridItemInstance>();
            if (marker == null) continue;
            if (marker.cell.x != cell.x || marker.cell.y != cell.y || marker.cell.z != z) continue;
            Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    private static void ClearAllItems(GameGridRenderer renderer)
    {
        var container = FindItemContainer(renderer);
        if (container == null) return;
        for (var i = container.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(container.GetChild(i).gameObject);
    }

    private static Transform GetOrCreateItemContainer(GameGridRenderer renderer)
    {
        var container = FindItemContainer(renderer);
        if (container != null) return container;

        var itemObject = new GameObject(ItemContainerName);
        itemObject.transform.SetParent(renderer.transform, false);
        Undo.RegisterCreatedObjectUndo(itemObject, "Create Grid Item Container");
        return itemObject.transform;
    }

    private static Transform FindItemContainer(GameGridRenderer renderer)
    {
        return renderer.transform.Find(ItemContainerName);
    }

    private static GameGridItemInstance FindItemAt(Transform container, Vector2Int cell, int z, GameObject prefab)
    {
        for (var i = 0; i < container.childCount; i++)
        {
            var marker = container.GetChild(i).GetComponent<GameGridItemInstance>();
            if (marker == null || marker.prefab != prefab) continue;
            if (marker.cell.x != cell.x || marker.cell.y != cell.y || marker.cell.z != z) continue;
            return marker;
        }

        return null;
    }

    private void UpdateItemPreview(GameObject prefab, Vector3 worldPosition)
    {
        if (prefab == null)
        {
            HideItemPreview();
            return;
        }

        if (itemPreviewObject == null || itemPreviewPrefab != prefab)
        {
            if (itemPreviewObject != null) DestroyImmediate(itemPreviewObject);

            itemPreviewObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (itemPreviewObject == null) return;

            itemPreviewPrefab = prefab;
            itemPreviewObject.hideFlags = HideFlags.DontSave;
        }

        itemPreviewObject.transform.position = worldPosition;
        if (!itemPreviewObject.activeSelf) itemPreviewObject.SetActive(true);
    }

    private void HideItemPreview()
    {
        if (itemPreviewObject != null && itemPreviewObject.activeSelf)
            itemPreviewObject.SetActive(false);
    }

    private static void DrawItemCellOutline(GameGridRenderer renderer, Vector2Int cell, int z, bool warn)
    {
        var p0 = renderer.CellCornerToWorld(cell.x, cell.y, z);
        var p1 = renderer.CellCornerToWorld(cell.x + 1, cell.y, z);
        var p2 = renderer.CellCornerToWorld(cell.x + 1, cell.y + 1, z);
        var p3 = renderer.CellCornerToWorld(cell.x, cell.y + 1, z);
        Handles.color = warn
            ? new Color(1f, 0.9f, 0.2f, 0.9f)
            : new Color(0.24f, 0.7f, 1f, 0.9f);
        Handles.DrawPolyLine(p0, p1, p2, p3, p0);
    }

    private void OnDisable()
    {
        if (itemPreviewObject != null) DestroyImmediate(itemPreviewObject);
        itemPreviewObject = null;
        itemPreviewPrefab = null;
    }

    private static void HandleHotkeys(GameGridRenderer renderer, Event e)
    {
        if (e.keyCode != KeyCode.LeftBracket && e.keyCode != KeyCode.RightBracket) return;
        CycleBrushTerrain(renderer, e.keyCode == KeyCode.LeftBracket ? -1 : 1);
        EditorUtility.SetDirty(renderer);
        e.Use();
        SceneView.RepaintAll();
    }

    private static void CycleBrushTerrain(GameGridRenderer renderer, int direction)
    {
        var layers = renderer.TerrainLayers;
        if (layers == null || layers.Count == 0) return;

        var currentIndex = Mathf.Clamp(renderer.EditorBrushLayerIndex, 0, layers.Count - 1);
        var nextIndex = Mathf.Clamp(currentIndex + direction, 0, layers.Count - 1);
        renderer.EditorBrushLayerIndex = nextIndex;
    }
}
