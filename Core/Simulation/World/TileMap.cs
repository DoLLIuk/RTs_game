using Godot;
using RtsNaGodote.Core.Data;

namespace RtsNaGodote.Core.Simulation.World;

public sealed class TileMap
{
    private readonly TileType[] _tiles;
    private readonly bool[] _walkable;

    public TileMap()
    {
        _tiles = new TileType[GameConstants.MapWidth * GameConstants.MapHeight];
        _walkable = new bool[_tiles.Length];
        for (var i = 0; i < _walkable.Length; i++)
        {
            _walkable[i] = true;
        }
    }

    public int Width => GameConstants.MapWidth;
    public int Height => GameConstants.MapHeight;

    public bool InBounds(int tx, int ty) => tx >= 0 && ty >= 0 && tx < Width && ty < Height;

    public TileType Get(int tx, int ty) => _tiles[Index(tx, ty)];

    public void Set(int tx, int ty, TileType tile)
    {
        _tiles[Index(tx, ty)] = tile;
        if (tile is TileType.Water or TileType.Stone)
        {
            _walkable[Index(tx, ty)] = false;
        }
    }

    public bool IsWalkable(int tx, int ty) => InBounds(tx, ty) && _walkable[Index(tx, ty)];

    public void SetWalkable(int tx, int ty, bool value)
    {
        if (!InBounds(tx, ty))
        {
            return;
        }

        _walkable[Index(tx, ty)] = value;
    }

    public Vector2I WorldToTile(Vector2 worldPosition)
    {
        return new(
            Mathf.FloorToInt(worldPosition.X / GameConstants.TileSize),
            Mathf.FloorToInt(worldPosition.Y / GameConstants.TileSize));
    }

    public Vector2 TileToWorldCenter(int tx, int ty)
    {
        return new(
            tx * GameConstants.TileSize + (GameConstants.TileSize / 2.0f),
            ty * GameConstants.TileSize + (GameConstants.TileSize / 2.0f));
    }

    private int Index(int tx, int ty) => ty * Width + tx;
}
