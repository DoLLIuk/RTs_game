using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Simulation.World;
using RtsNaGodote.Game.Presentation;
using WorldTileMap = RtsNaGodote.Core.Simulation.World.TileMap;

namespace RtsNaGodote.Game.UI;

public readonly record struct MinimapMarker(
    Vector2 Position,
    Color Color,
    float Radius,
    bool IsBuilding);

public readonly record struct MinimapPing(
    Vector2 Position,
    Color Color,
    float NormalizedLife);

public sealed class MinimapState
{
    public required WorldTileMap Map { get; init; }
    public required FogOfWar Fog { get; init; }
    public required IReadOnlyList<MinimapMarker> Markers { get; init; }
    public required IReadOnlyList<MinimapPing> Pings { get; init; }
    public required Rect2 CameraWorldRect { get; init; }
}
