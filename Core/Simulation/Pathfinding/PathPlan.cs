using System.Collections.Generic;
using Godot;

namespace RtsNaGodote.Core.Simulation.Pathfinding;

public readonly record struct PathPlan(
    bool Succeeded,
    List<Vector2> Points,
    Vector2 Destination,
    bool UsedFallback = false);
