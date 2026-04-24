using Godot;
using RtsNaGodote.Core.Data;

namespace RtsNaGodote.Core.Simulation;

public enum BuildingPlacementIssue
{
    None,
    OutOfBounds,
    Blocked,
    InsufficientResources
}

public readonly record struct BuildingPlacementResult(
    bool CanPlace,
    BuildingPlacementIssue Issue,
    Vector2I TilePosition,
    int Size)
{
    public string Message(BuildingKind kind, Race race)
    {
        return Issue switch
        {
            BuildingPlacementIssue.None => $"Placing {GameDefinitions.BuildingLabel(kind, race)}",
            BuildingPlacementIssue.OutOfBounds => "Cannot place building outside the map",
            BuildingPlacementIssue.Blocked => "That area is occupied or blocked",
            BuildingPlacementIssue.InsufficientResources => "Not enough resources",
            _ => "Cannot place building there"
        };
    }
}
