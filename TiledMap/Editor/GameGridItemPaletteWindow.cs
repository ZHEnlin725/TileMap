using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal sealed class GameGridItemPaletteWindow : EditorWindow
{
    private const string DefaultFolderPath = "Assets/Art/PIXEL/Prefab/Objects";
    private const float CellSize = 72f;
    private const float CellGap = 6f;
    private const float RowHeight = CellSize + CellGap;
    private const double PreviewRepaintInterval = 0.1;

    private DefaultAsset folder;
    private string search = string.Empty;
    private Vector2 scroll;
    private readonly List<string> prefabPaths = new List<string>();
    private readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();
    private bool previewsPending;
    private double nextPreviewRepaint;

    [MenuItem("Tools/Game Grid Item Palette")]
    public static void ShowWindow()
    {
        var window = GetWindow<GameGridItemPaletteWindow>("Item Palette");
        window.minSize = new Vector2(320f, 360f);
        window.Refresh();
    }

    private void OnGUI()
    {
        // Asset previews are generated in the background; repaint at a low
        // fixed rate while the visible previews are still loading instead of
        // every frame (a full-frame repaint re-lays out the whole window).
        if (previewsPending)
        {
            if (EditorApplication.timeSinceStartup >= nextPreviewRepaint)
            {
                nextPreviewRepaint = EditorApplication.timeSinceStartup + PreviewRepaintInterval;
                Repaint();
            }

            previewsPending = false;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            folder = (DefaultAsset)EditorGUILayout.ObjectField(folder, typeof(DefaultAsset), false);
            if (GUILayout.Button("Refresh", GUILayout.Width(60f))) Refresh();
        }

        search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);

        DrawSelectionSummary();

        if (prefabPaths.Count == 0)
        {
            EditorGUILayout.HelpBox(
                $"No prefabs found under {CurrentFolderPath()}. Assign a folder or press Refresh.",
                MessageType.Info);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawPrefabGrid();
        EditorGUILayout.EndScrollView();
    }

    private void DrawSelectionSummary()
    {
        var prefab = GameGridItemBrushState.SelectedPrefab;
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            var previewRect = GUILayoutUtility.GetRect(28f, 28f, GUILayout.Width(28f));
            var preview = prefab != null ? AssetPreview.GetAssetPreview(prefab) : null;
            if (preview == null && prefab != null) preview = AssetPreview.GetMiniThumbnail(prefab);
            if (preview != null) EditorGUI.DrawPreviewTexture(previewRect, preview);

            EditorGUILayout.LabelField(
                prefab != null ? prefab.name : "No item selected",
                EditorStyles.miniLabel);
        }
    }

    private void DrawPrefabGrid()
    {
        var columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 24f) / RowHeight));
        var rows = Mathf.CeilToInt(prefabPaths.Count / (float)columns);
        var totalHeight = Mathf.Max(1f, rows * RowHeight);
        var gridRect = GUILayoutUtility.GetRect(1f, totalHeight, GUILayout.ExpandWidth(true));

        // Only draw the rows inside the scroll viewport. A GUILayout grid
        // would lay out every button on every GUI pass even offscreen.
        var viewportHeight = position.height;
        var firstRow = Mathf.Clamp(Mathf.FloorToInt(scroll.y / RowHeight), 0, Mathf.Max(0, rows - 1));
        var lastRow = Mathf.Min(rows - 1, Mathf.CeilToInt((scroll.y + viewportHeight) / RowHeight));

        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var col = 0; col < columns; col++)
            {
                var index = row * columns + col;
                if (index >= prefabPaths.Count) break;

                var cellRect = new Rect(
                    gridRect.x + CellGap + col * RowHeight,
                    gridRect.y + row * RowHeight,
                    CellSize,
                    CellSize);
                DrawPrefabCell(prefabPaths[index], cellRect);
            }
        }
    }

    private void DrawPrefabCell(string path, Rect cellRect)
    {
        var prefab = GetOrLoadPrefab(path);
        if (prefab == null)
        {
            GUI.Box(cellRect, string.Empty);
            return;
        }

        var preview = AssetPreview.GetAssetPreview(prefab);
        if (preview == null) previewsPending = true;

        var content = new GUIContent(preview, prefab.name);
        if (GUI.Button(cellRect, content))
        {
            GameGridItemBrushState.SelectedPrefab = prefab;
            SceneView.RepaintAll();
            Repaint();
        }

        if (prefab == GameGridItemBrushState.SelectedPrefab) DrawSelectionFrame(cellRect);
    }

    private GameObject GetOrLoadPrefab(string path)
    {
        if (prefabCache.TryGetValue(path, out var cached)) return cached;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        prefabCache[path] = prefab;
        return prefab;
    }

    private static void DrawSelectionFrame(Rect rect)
    {
        var color = new Color(0.24f, 0.7f, 1f, 0.9f);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), color);
    }

    private void Refresh()
    {
        prefabPaths.Clear();
        prefabCache.Clear();

        var folderPath = CurrentFolderPath();
        var useFolder = !string.IsNullOrEmpty(folderPath) && AssetDatabase.IsValidFolder(folderPath);
        var guids = useFolder
            ? AssetDatabase.FindAssets("t:Prefab", new[] { folderPath })
            : AssetDatabase.FindAssets("t:Prefab");

        var seenPaths = new HashSet<string>();
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (seenPaths.Add(path) && MatchesSearch(path))
                prefabPaths.Add(path);
        }

        prefabPaths.Sort((a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
        scroll = Vector2.zero;
        Repaint();
    }

    private string CurrentFolderPath()
    {
        return folder != null ? AssetDatabase.GetAssetPath(folder) : DefaultFolderPath;
    }

    private bool MatchesSearch(string path)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        return path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
