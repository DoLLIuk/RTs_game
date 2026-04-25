using Godot;

namespace RtsNaGodote.Core.Simulation.Pathfinding;

public readonly record struct PathRequest(
    Vector2 WorldTarget,
    float ArrivalRadius,
    Vector2 InteractionAnchor,
    bool StuckReroute = false,
    bool PreserveExistingPathOnFailure = false);
