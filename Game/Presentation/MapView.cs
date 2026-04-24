using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.World;
using WorldTileMap = RtsNaGodote.Core.Simulation.World.TileMap;

namespace RtsNaGodote.Game.Presentation;

public partial class MapView : Node2D
{
    private WorldTileMap? _map;

    public void SetMap(WorldTileMap map)
    {
        _map = map;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_map is null)
        {
            return;
        }

        for (var y = 0; y < _map.Height; y++)
        {
            for (var x = 0; x < _map.Width; x++)
            {
                var tile = _map.Get(x, y);
                var rect = new Rect2(
                    x * GameConstants.TileSize,
                    y * GameConstants.TileSize,
                    GameConstants.TileSize,
                    GameConstants.TileSize);
                DrawRect(rect, GetTileColor(tile));
            }
        }
    }

    private static Color GetTileColor(TileType tile)
    {
        return tile switch
        {
            TileType.Grass => GameColors.Grass,
            TileType.Grass2 => GameColors.Grass2,
            TileType.Forest => GameColors.Forest,
            TileType.Stone => GameColors.Stone,
            TileType.Water => GameColors.Water,
            TileType.Dirt => GameColors.Dirt,
            _ => Colors.Magenta
        };
    }
}
