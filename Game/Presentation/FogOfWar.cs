using Godot;
using RtsNaGodote.Core.Simulation.World;
using WorldTileMap = RtsNaGodote.Core.Simulation.World.TileMap;

namespace RtsNaGodote.Game.Presentation;

public sealed class FogOfWar
{
    private readonly byte[] _state;

    public FogOfWar(WorldTileMap map)
    {
        Width = map.Width;
        Height = map.Height;
        _state = new byte[Width * Height];
    }

    public int Width { get; }
    public int Height { get; }

    public bool IsVisible(int tx, int ty)
    {
        return InBounds(tx, ty) && _state[Index(tx, ty)] == 2;
    }

    public bool IsExplored(int tx, int ty)
    {
        return InBounds(tx, ty) && _state[Index(tx, ty)] >= 1;
    }

    public byte GetState(int tx, int ty)
    {
        return InBounds(tx, ty) ? _state[Index(tx, ty)] : (byte)0;
    }

    public void DimVisible()
    {
        for (var i = 0; i < _state.Length; i++)
        {
            if (_state[i] == 2)
            {
                _state[i] = 1;
            }
        }
    }

    public void RevealCircle(int cx, int cy, int tiles)
    {
        var minX = int.Max(0, cx - tiles);
        var maxX = int.Min(Width - 1, cx + tiles);
        var minY = int.Max(0, cy - tiles);
        var maxY = int.Min(Height - 1, cy + tiles);
        var radiusSquared = tiles * tiles;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                if (dx * dx + dy * dy <= radiusSquared)
                {
                    _state[Index(x, y)] = 2;
                }
            }
        }
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
