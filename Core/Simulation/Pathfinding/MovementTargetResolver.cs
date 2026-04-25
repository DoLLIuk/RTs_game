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
            return StaticInteractionService.BuildApproachTarget(unit, node);
        }

        return TryBuildWorkerFlowTarget(
                unit,
                hall.Center,
                hall.SizeTiles,
                hall.SizeTiles,
                node.Center,
                node.TileWidth,
                node.TileHeight,
                approachingHall: false,
                out var target)
            ? target
            : StaticInteractionService.BuildApproachTarget(unit, node);
    }

    public static Vector2 GetWorkerReturnPathTarget(SimUnit unit, SimBuilding hall, SimResourceNode? node)
    {
        if (!unit.IsWorker() || node is not { Alive: true })
        {
            return StaticInteractionService.BuildApproachTarget(unit, hall);
        }

        return TryBuildWorkerFlowTarget(
                unit,
                hall.Center,
                hall.SizeTiles,
                hall.SizeTiles,
                node.Center,
                node.TileWidth,
                node.TileHeight,
                approachingHall: true,
                out var target)
            ? target
            : StaticInteractionService.BuildApproachTarget(unit, hall);
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
        var lane = CombatApproachService.CenteredSlotIndex(Mathf.PosMod(unit.Id, 3));
        var rank = Mathf.PosMod(unit.Id / 3, 2);
        var laneSpacing = float.Max(unit.Radius * 1.25f + 2f, GameConstants.GroupSpacing * 0.28f);
        var rankSpacing = float.Max(unit.Radius * 0.95f + 2f, GameConstants.GroupSpacing * 0.18f);
        return anchor + outward * (arrivalRadius + rank * rankSpacing) + lateral * (lane * laneSpacing);
    }

    private static bool TryBuildWorkerFlowTarget(
        SimUnit unit,
        Vector2 hallCenter,
        int hallWidthTiles,
        int hallHeightTiles,
        Vector2 nodeCenter,
        int nodeWidthTiles,
        int nodeHeightTiles,
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
        var laneIndex = unit.Id % 5 - 2;
        var laneOffset = perpendicular * (laneIndex * (GameConstants.WorkerFlowLaneOffset * 0.55f));
        var interactionClearance = StaticInteractionService.GetInteractionClearance(unit);

        if (approachingHall)
        {
            var depth = StaticInteractionService.GetDirectionalSupportDistance(routeDirection, hallWidthTiles, hallHeightTiles) +
                        interactionClearance +
                        Mathf.Abs(laneIndex) * 1.5f;
            target = hallCenter + routeDirection * depth - laneOffset;
            return true;
        }

        var depthToNode = StaticInteractionService.GetDirectionalSupportDistance(routeDirection, nodeWidthTiles, nodeHeightTiles) +
                          interactionClearance +
                          Mathf.Abs(laneIndex) * 1.2f;
        target = nodeCenter - routeDirection * depthToNode + laneOffset;
        return true;
    }
}
