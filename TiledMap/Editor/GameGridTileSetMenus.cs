using UnityEditor;
using UnityEngine;

internal static class GameGridTileSetMenus
{
    private const string RectangleMenuRoot = "Assets/Create/Game Grid Tile Set/Rectangle";
    private const string IsometricMenuRoot = "Assets/Create/Game Grid Tile Set/Isometric";

    [MenuItem(RectangleMenuRoot + "/Dual Grid 16", false, 7)]
    private static void CreateRectangleDualGrid16()
    
    {
        CreateRectangleTileSet<DualGrid16TileSet>("New Isometric Dual Grid 16 Tile Set.asset");
    }

    [MenuItem(RectangleMenuRoot + "/Blob 47", false, 8)]
    private static void CreateRectangleBlob47()
    {
        CreateRectangleTileSet<Blob47TileSet>("New Isometric Blob 47 Tile Set.asset");
    }

    [MenuItem(RectangleMenuRoot + "/Custom Mask Rules", false, 9)]
    private static void CreateRectangleCustomMaskRules()
    {
        CreateRectangleTileSet<CustomMaskRulesTileSet>("New Isometric Custom Mask Rules Tile Set.asset");
    }

    private static void CreateRectangleTileSet<T>(string defaultName) where T : GameGridTileSet
    {
        var tileSet = ScriptableObject.CreateInstance<T>();
        tileSet.SetGridTileType(GameGridTileType.Rectangle);
        ProjectWindowUtil.CreateAsset(tileSet, defaultName);
    }
    
    [MenuItem(IsometricMenuRoot + "/Dual Grid 16", false, 10)]
    private static void CreateIsometricDualGrid16()
    {
        CreateIsometricTileSet<DualGrid16TileSet>("New Isometric Dual Grid 16 Tile Set.asset");
    }

    [MenuItem(IsometricMenuRoot + "/Blob 47", false, 11)]
    private static void CreateIsometricBlob47()
    {
        CreateIsometricTileSet<Blob47TileSet>("New Isometric Blob 47 Tile Set.asset");
    }

    [MenuItem(IsometricMenuRoot + "/Custom Mask Rules", false, 12)]
    private static void CreateIsometricCustomMaskRules()
    {
        CreateIsometricTileSet<CustomMaskRulesTileSet>("New Isometric Custom Mask Rules Tile Set.asset");
    }

    private static void CreateIsometricTileSet<T>(string defaultName) where T : GameGridTileSet
    {
        var tileSet = ScriptableObject.CreateInstance<T>();
        tileSet.SetGridTileType(GameGridTileType.Isometric);
        ProjectWindowUtil.CreateAsset(tileSet, defaultName);
    }
}
