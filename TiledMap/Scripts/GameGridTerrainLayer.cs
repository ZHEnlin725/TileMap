using System;
using UnityEngine;

[Serializable]
public sealed class GameGridTerrainLayer
{
    public GameGridTileSet tileSet;
    public bool visible = true;
    public string sortingLayerName = "Default";
    public int sortingOrder;

    [SerializeField] private bool[] cells = Array.Empty<bool>();
    
    public GameGridTileSet TileSet => tileSet;

    public bool[] Cells => cells;
    
    // private int compressedBitOffset;
    // private byte[] compressedCells = Array.Empty<byte>();
    // public byte[] CompressedCells => compressedCells;
    
    // public int CellCount
    // {
    //     get
    //     {
    //         if (compressedBitOffset > 0)
    //         {
    //             return (compressedCells.Length - 1) * 8 + compressedBitOffset;
    //         }
    //
    //         return compressedCells.Length * 8;
    //     }
    // }

    public bool GetCell(int index)
    {
        return cells != null && index >= 0 && index < cells.Length && cells[index];
        // var group = index / 8;
        // var offset = index % 8;
        // return compressedCells != null && group >= 0 && group < compressedCells.Length &&
               // (compressedCells[group] & (1 << offset)) > 0;
    }

    public void SetCell(int index, bool occupied)
    {
        if (cells == null || index < 0 || index >= cells.Length) return;
        cells[index] = occupied;
        // var group = index / 8;
        // if (compressedCells == null || group < 0 || group >= compressedCells.Length) return;
        // var offset = index % 8;
        // compressedCells[group] = (byte)(compressedCells[group] | (byte)(1 << offset));
    }

    public void ReplaceCells(bool[] value)
    {
        cells = value ?? Array.Empty<bool>();
    }
    
    // public void ReplaceCompressedCells(byte[] value, int offset)
    // {
    //     compressedBitOffset = offset;
    //     compressedCells = value ?? Array.Empty<byte>();
    // }

    public void FillCells(bool occupied)
    {
        if (cells == null) return;
        for (var i = 0; i < cells.Length; i++) cells[i] = occupied;
    }
}