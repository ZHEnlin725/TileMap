using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Blob layout with 47 canonical sprites for the eight-neighbour masks
/// (N, NE, E, SE, S, SW, W, NW).
/// </summary>
public sealed class Blob47TileSet : GameGridTileSet
{
    public const int TileCount = 47;

    /// <summary>Maps a source-slice index to the runtime tile-slot index it serves.</summary>
    public static readonly int[] Blob47SliceToTileSlot =
    {
        10, 31, 26, 5, 7, 28, 23, 18, 20, 40,
        12, 46, 42, 6, 9, 44, 41, 37, 39, 30,
        4, 36, 34, 1, 11, 45, 33, 27, 32, 24,
        29, 2, 15, 13, 0, 3, 35, 17, 14, 16,
        22, 38, 8, 43, 25, 19, 21
    };
    
    private static readonly int[] Blob47TileSlotToSlice =
    {
        34, 23, 31, 35, 20, 3, 13, 4, 42, 14, 0, 24, 10,
        33, 38, 32, 39, 37, 7, 45, 8, 46, 40, 6, 29, 44,
        2, 27, 5, 30, 19, 1, 28, 26, 22, 36, 21, 17, 41,
        18, 9, 16, 12, 43, 15, 25, 11,
    };

    public Sprite[] blobTiles = new Sprite[TileCount];
    [SerializeField] private Texture2D sourceTileset;

    public Texture2D SourceTileset => sourceTileset;

    public override GameGridTileSetMode Mode => GameGridTileSetMode.Blob47;
    public override int SpriteCount => TileCount;
    public override bool UsesCornerLayout => false;

    public override Sprite GetTileForMask(int mask)
    {
        if (blobTiles == null || mask < 0 || mask >= Blob47IndexForMask.Length) return null;

        var tileSlot = Blob47IndexForMask[mask];
        if (tileSlot < 0 || tileSlot >= Blob47TileSlotToSlice.Length) return null;

        var sliceIndex = Blob47TileSlotToSlice[tileSlot];
        return sliceIndex >= 0 && sliceIndex < blobTiles.Length ? blobTiles[sliceIndex] : null;
    }

    public override Sprite GetSpriteAt(int index)
    {
        if (blobTiles == null || index < 0 || index >= blobTiles.Length) return null;
        return blobTiles[index];
    }

    public override void EnsureSlots()
    {
        base.EnsureSlots();

        if (blobTiles == null)
            blobTiles = new Sprite[TileCount];
        else if (blobTiles.Length != TileCount)
        {
            var resized = new Sprite[TileCount];
            Array.Copy(blobTiles, resized, Mathf.Min(blobTiles.Length, TileCount));
            blobTiles = resized;
        }
    }

    public static int GetBlob47Mask(int index)
    {
        if (Blob47Masks == null || index < 0 || index >= Blob47Masks.Length) return -1;
        return Blob47Masks[index];
    }

    public static int GetTileSlotForSlice(int sliceIndex)
    {
        if (sliceIndex < 0 || sliceIndex >= Blob47SliceToTileSlot.Length) return -1;
        return Blob47SliceToTileSlot[sliceIndex];
    }

    private static readonly int[] Blob47Masks;
    private static readonly int[] Blob47IndexForMask = new int[256];
    
    static Blob47TileSet()
    {
        var masks = new List<int>(TileCount);
        var seen = new bool[256];
        var indexForMask = new int[256];

        for (var raw = 0; raw < 256; raw++)
        {
            var canonical = CanonicalizeBlobMask(raw);
            if (seen[canonical]) continue;
            seen[canonical] = true;
            indexForMask[canonical] = masks.Count;
            masks.Add(canonical);
        }

        Blob47Masks = masks.ToArray();
        for (var raw = 0; raw < 256; raw++)
            Blob47IndexForMask[raw] = indexForMask[CanonicalizeBlobMask(raw)];
    }

    private static int CanonicalizeBlobMask(int mask)
    {
        const int n = 1;
        const int e = 4;
        const int s = 16;
        const int w = 64;
        const int ne = 2;
        const int se = 8;
        const int sw = 32;
        const int nw = 128;

        var hasN = (mask & n) != 0;
        var hasE = (mask & e) != 0;
        var hasS = (mask & s) != 0;
        var hasW = (mask & w) != 0;

        var canonicalNe = hasN && hasE ? mask & ne : 0;
        var canonicalSe = hasE && hasS ? mask & se : 0;
        var canonicalSw = hasS && hasW ? mask & sw : 0;
        var canonicalNw = hasW && hasN ? mask & nw : 0;

        return (mask & (n | e | s | w)) | canonicalNe | canonicalSe | canonicalSw | canonicalNw;
    }
}