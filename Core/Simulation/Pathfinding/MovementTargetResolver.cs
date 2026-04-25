using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Resources;
using RtsNaGodote.Core.Simulation.Units;

namespace RtsNaGodote.Core.Simulation.Pathfinding;

public static class MovementTargetResolver
{
    private const float MoveTargetClickSlack = 10f;
    private const float MoveTargetStandOffPadding = 6f;

    public static bool TryResolveOccupiedMoveZone(
        SimUnit unit,
        Vector2 worldTarget,
        IReadOnlyList<SimUnit> units,
        IReadOnlyList<SimBuilding> buildings,
        IReadOnlyList<SimResourceNode> resources,
        out InteractionZone zone)
    {
        foreach (var other in units)
        {
            if (!other.Alive || other == unit)
            {
                continue;
            }

            if (TryResolveOccupiedMoveZone(unit, worldTarget, other.Position, other.Radius, out zone))
            {
                return true;
            }
        }

        foreach (var building in buildings)
        {
            if (!building.Alive)
            {
                continue;
            }

            if (TryResolveOccupiedMoveZone(unit, worldTarget, building.Center, building.Radius, out zone))
            {
                return true;
            }
        }

        foreach (var resource in resources)
        {
            if (!resource.Alive)
            {
                continue;
            }

            if (TryResolveOccupiedMoveZone(unit, worldTarget, resource.Center, resource.Radius, out zone))
            {
                return true;
            }
        }

        zone = default;
        return false;
    }

    public static InteractionZone GetWorkerGatherZone(SimUnit unit, SimResourceNode node, SimBuilding? hall)
    {
        return StaticInteractionService.GetInteractionZone(unit, node);
    }

    public static InteractionZone GetWorkerReturnZone(SimUnit unit, SimBuilding hall, SimResourceNode? node)
    {
        return StaticInteractionService.GetInteractionZone(unit, hall);
    }

    private static bool TryResolveOccupiedMoveZone(
        SimUnit unit,
        Vector2 worldTarget,
        Vector2 occupiedCenter,
        float occupiedRadius,
        out InteractionZone zone)
    {
        var detectionRadius = occupiedRadius + MoveTargetClickSlack;
        if (worldTarget.DistanceTo(occupiedCenter) > detectionRadius)
        {
            zone = default;
            return false;
        }

        var arrivalRadius = Mathf.Max(unit.Radius * 0.8f + 4f, GameConstants.TileSize * 0.42f);
        var zoneCenter = BuildOccupiedMoveApproachPoint(unit, worldTarget, occupiedCenter, occupiedRadius + unit.Radius + MoveTargetStandOffPadding);
        zone = new InteractionZone(zoneCenter, arrivalRadius, occupiedCenter);
        return true;
    }

    private static Vector2 BuildOccupiedMoveApproachPoint(SimUnit unit, Vector2 rawTarget, Vector2 anchor, float arrivalRadius)
    {
        var outward = rawTarget - anchor;
        if (outward.LengthSquared() <= 9f)
        {
            outward = unit.Position - anchor;
        }

        if (outward.LengthSquared() <= 9f)
        {
            var angle = Mathf.Tau * (Mathf.PosMod(unit.Id, 8) / 8f);
            outward = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        outward = outward.Normalized();
        var lateral = new Vector2(-outward.Y, outward.X);
        return anchor + outward * arrivalRadius + lateral * (CombatApproachService.CenteredSlotIndex(Mathf.PosMod(unit.Id, 3)) * (unit.Radius * 0.35f + 2f));
    }
}
