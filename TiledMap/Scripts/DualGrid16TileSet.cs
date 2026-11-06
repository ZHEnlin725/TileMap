using System;
using System.Text;
using UnityEngine;

/// <summary>
/// Dual-grid layout with 16 sprites mapped to the four data cells around a corner.
/// Bit 0 = bottom-left, bit 1 = bottom-right, bit 2 = top-left, bit 3 = top-right.
/// </summary>
public sealed class DualGrid16TileSet : GameGridTileSet
{
    public const int TileCount = 16;

    /// <summary>Maps a source-slice index to the runtime tile slot it serves.</summary>
    public static readonly int[] SliceToTileSlot =
    {
        1, 10, 7, 3,
        6, 11, 15, 13,
        8, 12, 14, 5,
        0, 2, 9, 4
    };

    private static readonly int[] SliceIndexForMask = new int[TileCount]
    {
        12, 0, 13, 3, 
        15, 11, 4, 2,
        8, 14, 1, 5, 
        9, 7, 10, 6
    };

    public Sprite[] tiles = new Sprite[TileCount];
    [SerializeField] private Texture2D sourceTileset;

    public Texture2D SourceTileset => sourceTileset;

    public override GameGridTileSetMode Mode => GameGridTileSetMode.DualGrid16;
    public override int SpriteCount => TileCount;
    public override bool UsesCornerLayout => true;

    public static int GetTileSlotForSlice(int sliceIndex)
    {
        if (sliceIndex < 0 || sliceIndex >= SliceToTileSlot.Length) return -1;
        return SliceToTileSlot[sliceIndex];
    }

    public Sprite GetTile(int mask)
    {
        if (tiles == null || mask < 0 || mask >= SliceIndexForMask.Length) return null;
        var sliceIndex = SliceIndexForMask[mask];
        return sliceIndex >= 0 && sliceIndex < tiles.Length ? tiles[sliceIndex] : null;
    }

    public override Sprite GetTileForMask(int mask)
    {
        return GetTile(mask);
    }

    public override Sprite GetSpriteAt(int index)
    {
        return GetTile(index);
    }

    public override void EnsureSlots()
    {
        base.EnsureSlots();

        if (tiles == null)
            tiles = new Sprite[TileCount];
        else if (tiles.Length != TileCount)
        {
            var resized = new Sprite[TileCount];
            Array.Copy(tiles, resized, Mathf.Min(tiles.Length, TileCount));
            tiles = resized;
        }
    }
}
