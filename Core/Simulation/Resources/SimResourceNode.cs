using Godot;
using RtsNaGodote.Core.Data;

namespace RtsNaGodote.Core.Simulation.Resources;

public sealed class SimResourceNode
{
    public SimResourceNode(int id, ResourceType type, Vector2I tilePosition)
    {
        var definition = GameDefinitions.Resources[type];
        Id = id;
        Type = type;
        TilePosition = tilePosition;
        TileWidth = definition.TileWidth;
        TileHeight = definition.TileHeight;
        Amount = definition.Amount;
        Radius = definition.Radius;
        Center = type == ResourceType.Gold
            ? new Vector2(
                tilePosition.X * GameConstants.TileSize + GameConstants.TileSize * 1.5f,
                tilePosition.Y * GameConstants.TileSize + GameConstants.TileSize * 1.5f)
            : new Vector2(
                tilePosition.X * GameConstants.TileSize + GameConstants.TileSize / 2.0f,
                tilePosition.Y * GameConstants.TileSize + GameConstants.TileSize / 2.0f);
    }

    public int Id { get; }
    public ResourceType Type { get; }
    public Vector2I TilePosition { get; }
    public int TileWidth { get; }
    public int TileHeight { get; }
    public Vector2 Center { get; }
    public float Radius { get; }
    public bool Alive => Amount > 0;
    public int Amount { get; private set; }

    public int Harvest(int amount)
    {
        var gathered = int.Min(amount, Amount);
        Amount -= gathered;
        return gathered;
    }
}
