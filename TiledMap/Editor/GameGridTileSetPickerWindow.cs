using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal sealed class GameGridTileSetPickerWindow : EditorWindow
{
    private static readonly string[] SearchTypes =
    {
        "DualGrid16TileSet",
        "Blob47TileSet",
        "CustomMaskRulesTileSet"
    };

    private GameGridRenderer renderer;
    private Action<GameGridTileSet> onSelected;
    private readonly List<GameGridTileSet> candidates = new List<GameGridTileSet>();
    private Vector2 scrollPosition;
    private string search = string.Empty;

    public static void Show(GameGridRenderer renderer, Action<GameGridTileSet> onSelected)
    {
        if (renderer == null) return;

        var window = CreateInstance<GameGridTileSetPickerWindow>();
        window.renderer = renderer;
        window.onSelected = onSelected;
        window.minSize = new Vector2(320f, 320f);
        window.titleContent = new GUIContent($"Select {renderer.TileType} Tile Set");
        window.Refresh();

        var screenPosition = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
        window.ShowAsDropDown(
            new Rect(screenPosition.x, screenPosition.y, 1f, 1f),
            new Vector2(360f, 360f));
    }

    private void OnGUI()
    {
        if (renderer == null)
        {
            Close();
            return;
        }

        EditorGUILayout.HelpBox(
            $"Showing {renderer.TileType} Tile Sets only.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("Refresh", GUILayout.Width(70f))) Refresh();
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        var visibleCount = 0;

        foreach (var candidate in candidates)
        {
            if (!Matches(candidate, search)) continue;

            visibleCount++;
            var path = AssetDatabase.GetAssetPath(candidate);
            var label = $"{candidate.SafeName}  [{candidate.Mode} / {candidate.TileType}]";
            var icon = AssetPreview.GetMiniThumbnail(candidate);
            if (GUILayout.Button(new GUIContent(label, icon, path), GUILayout.Height(28f)))
            {
                onSelected?.Invoke(candidate);
                Close();
                GUIUtility.ExitGUI();
            }

            GUILayout.Label(path, EditorStyles.miniLabel);
        }

        if (visibleCount == 0)
            EditorGUILayout.HelpBox("No matching Tile Sets.", MessageType.Warning);

        EditorGUILayout.EndScrollView();
    }

    private void Refresh()
    {
        candidates.Clear();
        var seen = new HashSet<string>();

        foreach (var searchType in SearchTypes)
        {
            var guids = AssetDatabase.FindAssets($"t:{searchType}");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!seen.Add(path)) continue;

                var tileSet = AssetDatabase.LoadAssetAtPath<GameGridTileSet>(path);
                if (tileSet != null && renderer.IsTileSetCompatible(tileSet))
                    candidates.Add(tileSet);
            }
        }

        candidates.Sort((a, b) => string.Compare(
            AssetDatabase.GetAssetPath(a),
            AssetDatabase.GetAssetPath(b),
            StringComparison.OrdinalIgnoreCase));
    }

    private static bool Matches(GameGridTileSet tileSet, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText)) return true;

        return tileSet.SafeName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
               tileSet.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
               AssetDatabase.GetAssetPath(tileSet)
                   .IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
