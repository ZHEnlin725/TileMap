using System;
using UnityEngine;

[Serializable]
public sealed class GameGridTerrainLayer
{
    // Bit-packed cell storage: one byte holds eight cells, bit value 1 means
    // occupied. Cell index maps to (byte: index >> 3, bit: index & 7).
    [SerializeField] private byte[] cells = Array.Empty<byte>();

    public GameGridTileSet tileSet;
    public bool visible = true;
    public string sortingLayerName = "Default";
    public int sortingOrder;

    public GameGridTileSet TileSet => tileSet;

    public byte[] Cells => cells;

    public bool GetCell(int index)
    {
        return index >= 0 &&
               cells != null &&
               (index >> 3) < cells.Length &&
               (cells[index >> 3] & (1 << (index & 7))) != 0;
    }

    public void SetCell(int index, bool occupied)
    {
        if (cells == null || index < 0 || (index >> 3) >= cells.Length) return;
        if (occupied) cells[index >> 3] |= (byte)(1 << (index & 7));
        else cells[index >> 3] &= (byte)~(1 << (index & 7));
    }

    public void ReplaceCells(byte[] value)
    {
        cells = value ?? Array.Empty<byte>();
    }

    public void FillCells(bool occupied)
    {
        if (cells == null) return;
        var fill = occupied ? (byte)0xFF : (byte)0;
        for (var i = 0; i < cells.Length; i++) cells[i] = fill;
    }
}
