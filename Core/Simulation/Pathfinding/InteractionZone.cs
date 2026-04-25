using Godot;

namespace RtsNaGodote.Core.Simulation.Pathfinding;

public readonly record struct InteractionZone(
    Vector2 ZoneCenter,
    float ArrivalRadius,
    Vector2 InteractionAnchor);
