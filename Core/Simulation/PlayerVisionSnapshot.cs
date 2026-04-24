using System;

namespace RtsNaGodote.Core.Simulation;

public sealed class PlayerVisionSnapshot
{
    private readonly byte[] _visibleMask;

    public PlayerVisionSnapshot(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Vision snapshot dimensions must be positive.");
        }

        Width = width;
        Height = height;
        _visibleMask = new byte[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public bool HasData => _visibleMask.Length == Width * Height;

    public bool IsVisible(int tx, int ty)
    {
        return InBounds(tx, ty) && _visibleMask[Index(tx, ty)] != 0;
    }

    public void SetVisible(int tx, int ty, bool visible)
    {
        if (!InBounds(tx, ty))
        {
            return;
        }

        _visibleMask[Index(tx, ty)] = visible ? (byte)1 : (byte)0;
    }

    private bool InBounds(int tx, int ty)
    {
        return tx >= 0 && ty >= 0 && tx < Width && ty < Height;
    }

    private int Index(int tx, int ty)
    {
        return ty * Width + tx;
    }
}
