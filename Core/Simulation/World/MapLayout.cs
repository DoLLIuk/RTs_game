using System.Collections.Generic;
using Godot;

namespace RtsNaGodote.Core.Simulation.World;

public sealed class MapLayout
{
    public required TileMap Map { get; init; }
    public required Vector2I PlayerBase { get; init; }
    public required Vector2I AIBase { get; init; }
    public required IReadOnlyList<Vector2I> GoldMines { get; init; }
    public required IReadOnlyList<Vector2I> Trees { get; init; }
}
