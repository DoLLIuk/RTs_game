using System;
using Godot;
using RtsNaGodote.Core.Data;

namespace RtsNaGodote.Core.Simulation;

public sealed partial class GameSimulation
{
    private readonly HarassMissionService _harassMissionService;

    private HarassMissionContext CreateHarassMissionContext()
    {
        return new HarassMissionContext(
            Map,
            Units,
            Buildings,
            Resources,
            _aiKnowledge,
            () => _elapsedMs,
            FindAssaultApproachPoint,
            _aiArmyManager.CalculateMetrics,
            CommandUnitMove,
            IssueMoveGroup,
            IssueAttack);
    }

    private bool TryFindWalkableRaidPoint(Vector2I centerTile, int minRadius, int maxRadius, Vector2 reference, out Vector2 point)
    {
        point = Vector2.Zero;
        var bestScore = float.PositiveInfinity;
        var found = false;
        for (var radius = minRadius; radius <= maxRadius; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    var tx = centerTile.X + dx;
                    var ty = centerTile.Y + dy;
                    if (!Map.IsWalkable(tx, ty))
                    {
                        continue;
                    }

                    var world = Map.TileToWorldCenter(tx, ty);
                    var score = world.DistanceTo(reference) + _aiKnowledge.EstimateKnownThreatAt(world, GameConstants.TileSize * 4f) * 18f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        point = world;
                        found = true;
                    }
                }
            }
        }

        return found;
    }
}
