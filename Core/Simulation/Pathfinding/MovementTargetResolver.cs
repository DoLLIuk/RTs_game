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
    private const float MoveTargetStandOffPadding = 12f;

    public static bool TryResolveOccupiedMoveTarget(
        SimUnit unit,
        Vector2 worldTarget,
        IReadOnlyList<SimUnit> units,
        IReadOnlyList<SimBuilding> buildings,
        IReadOnlyList<SimResourceNode> resources,
        out Vector2 resolvedTarget,
        out float arrivalRadius,
        out Vector2 interactionAnchor)
    {
        foreach (var other in units)
        {
            if (!other.Alive || other == unit)
            {
                continue;
            }

            if (TryResolveOccupiedMoveTarget(unit, worldTarget, other.Position, other.Radius, out resolvedTarget, out arrivalRadius, out interactionAnchor))
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

            if (TryResolveOccupiedMoveTarget(unit, worldTarget, building.Center, building.Radius, out resolvedTarget, out arrivalRadius, out interactionAnchor))
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

            if (TryResolveOccupiedMoveTarget(unit, worldTarget, resource.Center, resource.Radius, out resolvedTarget, out arrivalRadius, out interactionAnchor))
            {
                return true;
            }
        }

        resolvedTarget = worldTarget;
        arrivalRadius = 0f;
        interactionAnchor = worldTarget;
        return false;
    }

    public static Vector2 GetWorkerGatherPathTarget(SimUnit unit, SimResourceNode node, SimBuilding? hall)
    {
        if (!unit.IsWorker() || hall is null)
        {
            return node.Center;
        }

        return TryBuildWorkerFlowTarget(unit.Id, hall.Center, hall.Radius, node.Center, node.Radius, approachingHall: false, out var target)
            ? target
            : node.Center;
    }

    public static Vector2 GetWorkerReturnPathTarget(SimUnit unit, SimBuilding hall, SimResourceNode? node)
    {
        if (!unit.IsWorker() || node is not { Alive: true })
        {
            return hall.Center;
        }

        return TryBuildWorkerFlowTarget(unit.Id, hall.Center, hall.Radius, node.Center, node.Radius, approachingHall: true, out var target)
            ? target
            : hall.Center;
    }

    private static bool TryResolveOccupiedMoveTarget(
        SimUnit unit,
        Vector2 worldTarget,
        Vector2 occupiedCenter,
        float occupiedRadius,
        out Vector2 resolvedTarget,
        out float arrivalRadius,
        out Vector2 interactionAnchor)
    {
        var detectionRadius = occupiedRadius + MoveTargetClickSlack;
        if (worldTarget.DistanceTo(occupiedCenter) > detectionRadius)
        {
            resolvedTarget = worldTarget;
            arrivalRadius = 0f;
            interactionAnchor = worldTarget;
            return false;
        }

        interactionAnchor = occupiedCenter;
        arrivalRadius = occupiedRadius + unit.Radius + MoveTargetStandOffPadding;
        resolvedTarget = BuildOccupiedMoveApproachPoint(unit, worldTarget, occupiedCenter, arrivalRadius);
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
        var lane = CombatApproachService.CenteredSlotIndex(Mathf.PosMod(unit.Id, 5));
        var rank = Mathf.PosMod(unit.Id / 5, 2);
        var laneSpacing = float.Max(unit.Radius * 2f + 4f, GameConstants.GroupSpacing * 0.55f);
        var rankSpacing = float.Max(unit.Radius * 2f + 6f, GameConstants.GroupSpacing * 0.42f);
        return anchor + outward * (arrivalRadius + rank * rankSpacing) + lateral * (lane * laneSpacing);
    }

    private static bool TryBuildWorkerFlowTarget(
        int unitId,
        Vector2 hallCenter,
        float hallRadius,
        Vector2 nodeCenter,
        float nodeRadius,
        bool approachingHall,
        out Vector2 target)
    {
        var route = nodeCenter - hallCenter;
        if (route.LengthSquared() <= 4f)
        {
            target = Vector2.Zero;
            return false;
        }

        var routeDirection = route.Normalized();
        var perpendicular = new Vector2(-routeDirection.Y, routeDirection.X);
        var laneIndex = unitId % 5 - 2;
        var laneOffset = perpendicular * (laneIndex * (GameConstants.WorkerFlowLaneOffset * 0.55f));

        if (approachingHall)
        {
            var depth = hallRadius + GameConstants.TileSize * 0.35f + Mathf.Abs(laneIndex) * 1.5f;
            target = hallCenter + routeDirection * depth - laneOffset;
            return true;
        }

        var depthToNode = nodeRadius + GameConstants.TileSize * 0.28f + Mathf.Abs(laneIndex) * 1.2f;
        target = nodeCenter - routeDirection * depthToNode + laneOffset;
        return true;
    }
}
