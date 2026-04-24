using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Resources;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Game.Presentation;

public readonly record struct RememberedBuildingState(
    int Id,
    BuildingKind Kind,
    GameSide Side,
    Race Race,
    Vector2 Center,
    Vector2I TilePosition,
    int SizeTiles,
    float Radius,
    bool Alive,
    bool Completed,
    int Hp,
    int MaxHp,
    float ProgressFraction)
{
    public Vector2I CenterTile => new(TilePosition.X + SizeTiles / 2, TilePosition.Y + SizeTiles / 2);

    public static RememberedBuildingState From(SimBuilding building)
    {
        return new RememberedBuildingState(
            building.Id,
            building.Kind,
            building.Side,
            building.Race,
            building.Center,
            building.TilePosition,
            building.SizeTiles,
            building.Radius,
            building.Alive,
            building.Completed,
            building.Hp,
            building.MaxHp,
            building.ProgressFraction());
    }
}

public readonly record struct RememberedResourceState(
    int Id,
    ResourceType Type,
    Vector2 Center,
    Vector2I TilePosition,
    int TileWidth,
    int TileHeight,
    float Radius,
    bool Alive)
{
    public static RememberedResourceState From(SimResourceNode resource)
    {
        return new RememberedResourceState(
            resource.Id,
            resource.Type,
            resource.Center,
            resource.TilePosition,
            resource.TileWidth,
            resource.TileHeight,
            resource.Radius,
            resource.Alive);
    }
}
