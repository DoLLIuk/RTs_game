using System;
using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Resources;
using RtsNaGodote.Core.Simulation.Units;
using WorldTileMap = RtsNaGodote.Core.Simulation.World.TileMap;

namespace RtsNaGodote.Core.Simulation.Pathfinding;

public sealed class UnitPathService
{
    private const float HeavyRerouteCooldownMs = 650f;
    private const float HeavyRerouteTriggerMs = 1100f;
    private const float SoftUnitTilePenalty = 2.1f;
    private const float SoftUnitNeighborTilePenalty = 0.35f;
    private const float StaticBlockerTilePenalty = 110f;
    private const float StaticBlockerNeighborTilePenalty = 18f;

    private readonly WorldTileMap _map;
    private readonly List<SimUnit> _units;
    private readonly List<SimBuilding> _buildings;
    private readonly List<SimResourceNode> _resources;
    private readonly Func<SimUnit, SimBuilding?> _findNearestHall;
    private readonly Func<SimUnit, ICombatTarget, float> _getAttackRange;
    private readonly LocalMovementService _localMovement;

    public UnitPathService(
        WorldTileMap map,
        List<SimUnit> units,
        List<SimBuilding> buildings,
        List<SimResourceNode> resources,
        Func<SimUnit, SimBuilding?> findNearestHall,
        Func<SimUnit, ICombatTarget, float> getAttackRange,
        LocalMovementService localMovement)
    {
        _map = map;
        _units = units;
        _buildings = buildings;
        _resources = resources;
        _findNearestHall = findNearestHall;
        _getAttackRange = getAttackRange;
        _localMovement = localMovement;
    }

    public bool Repath(SimUnit unit, PathRequest request, double elapsedMs)
    {
        var start = _map.WorldToTile(unit.Position);
        var goal = _map.WorldToTile(request.WorldTarget);
        var goalRadiusTiles = int.Max(0, Mathf.CeilToInt(request.ArrivalRadius / GameConstants.TileSize));
        var allowStartAsGoal = request.ArrivalRadius <= 0f || unit.Position.DistanceTo(request.InteractionAnchor) <= request.ArrivalRadius + 0.5f;
        var previousPath = request.PreserveExistingPathOnFailure ? new List<Vector2>(unit.Path) : null;
        var previousDestination = unit.PathDestination;
        var tilePenalty = BuildDynamicPenaltyMap(unit, goal, goalRadiusTiles, request.StuckReroute);
        var tilePath = Pathfinder.FindPath(
            _map,
            start,
            goal,
            goalRadiusTiles,
            unit.Id % 8,
            tilePenalty,
            allowStartAsGoal);

        PathPlan plan;
        if (tilePath.Count == 0)
        {
            if (!allowStartAsGoal &&
                TryBuildCloseRangeFallbackPath(unit, request.WorldTarget, request.InteractionAnchor, request.ArrivalRadius, out var fallbackPath))
            {
                plan = new PathPlan(true, fallbackPath, request.WorldTarget, true);
            }
            else if (request.PreserveExistingPathOnFailure && previousPath is not null)
            {
                plan = new PathPlan(false, previousPath, previousDestination ?? request.WorldTarget);
            }
            else
            {
                plan = new PathPlan(false, [], request.WorldTarget);
            }
        }
        else
        {
            var worldPath = new List<Vector2>(tilePath.Count);
            foreach (var point in tilePath)
            {
                worldPath.Add(_map.TileToWorldCenter(point.X, point.Y));
            }

            plan = new PathPlan(true, worldPath, worldPath.Count > 0 ? worldPath[^1] : request.WorldTarget);
        }

        unit.SetPath(plan.Points);
        unit.PathDestination = plan.Destination;
        unit.PathRepathMs = 0d;
        if (request.StuckReroute && plan.Succeeded)
        {
            unit.LastHeavyRerouteMs = elapsedMs;
        }

        return plan.Succeeded;
    }

    public bool AdvanceWithRecovery(SimUnit unit, double delta, double elapsedMs)
    {
        if (!unit.Alive || unit.Path.Count == 0)
        {
            unit.StuckAccumMs = 0d;
            unit.PathProgressStallMs = 0d;
            unit.LastPathProgressMetric = float.PositiveInfinity;
            return false;
        }

        UpdatePathProgressState(unit, delta);

        var before = unit.Position;
        var pathCountBefore = unit.Path.Count;
        var expectedStep = unit.Speed * (float)delta;
        var hasPath = _localMovement.AdvanceAlongPathWithSteering(unit, delta);
        var moved = before.DistanceTo(unit.Position);
        var reachedWaypoint = unit.Path.Count < pathCountBefore;
        var movedEnough = moved >= float.Max(GameConstants.StuckMovedEpsilon, expectedStep * 0.18f);

        if (!reachedWaypoint && !movedEnough)
        {
            unit.StuckAccumMs += delta * 1000d;
        }
        else
        {
            unit.StuckAccumMs = 0d;
        }

        var needsRecovery = hasPath && (unit.StuckAccumMs >= GameConstants.StuckRepathDelayMs ||
                                        unit.PathProgressStallMs >= GameConstants.StuckRepathDelayMs);
        if (needsRecovery)
        {
            var allowHeavyReroute = unit.StuckAccumMs >= HeavyRerouteTriggerMs ||
                                    unit.PathProgressStallMs >= HeavyRerouteTriggerMs;
            ResolveLocalStuck(unit, allowHeavyReroute, elapsedMs);
        }

        return hasPath;
    }

    public Dictionary<int, float> BuildDynamicTilePenalty(SimUnit unit, Vector2I goal, int goalRadiusTiles, bool stuckReroute)
    {
        return BuildDynamicPenaltyMap(unit, goal, goalRadiusTiles, stuckReroute);
    }

    public bool TryGetRepathRequest(SimUnit unit, out PathRequest request)
    {
        if (unit.TargetCombat is { Alive: true } combat)
        {
            if (CombatApproachService.TryBuildCombatApproachTarget(unit, combat, out var approachTarget, out var arrivalRadius))
            {
                request = new PathRequest(approachTarget, arrivalRadius, combat.Position);
                return true;
            }

            request = new PathRequest(combat.Position, _getAttackRange(unit, combat), combat.Position);
            return true;
        }

        if (unit.TargetResource is { Alive: true } resource)
        {
            var hall = unit.ReturnBuilding is { Alive: true } returnBuilding ? returnBuilding : _findNearestHall(unit);
            var target = MovementTargetResolver.GetWorkerGatherPathTarget(unit, resource, hall);
            var arrivalRadius = resource.Radius + unit.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
            request = new PathRequest(target, arrivalRadius, resource.Center);
            return true;
        }

        if (unit.ReturnBuilding is { Alive: true } returnHall)
        {
            var target = MovementTargetResolver.GetWorkerReturnPathTarget(unit, returnHall, unit.TargetResource);
            var arrivalRadius = returnHall.Radius + unit.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
            request = new PathRequest(target, arrivalRadius, returnHall.Center);
            return true;
        }

        if (unit.TargetBuilding is { Alive: true } site)
        {
            var arrivalRadius = site.Radius + unit.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
            request = new PathRequest(site.Center, arrivalRadius, site.Center);
            return true;
        }

        if (unit.PathDestination.HasValue && unit.MoveInteractionAnchor.HasValue)
        {
            request = new PathRequest(unit.PathDestination.Value, unit.MoveArrivalRadius, unit.MoveInteractionAnchor.Value);
            return true;
        }

        if (unit.PathDestination.HasValue)
        {
            request = new PathRequest(unit.PathDestination.Value, 0f, unit.PathDestination.Value);
            return true;
        }

        request = default;
        return false;
    }

    private void ResolveLocalStuck(SimUnit unit, bool allowHeavyReroute, double elapsedMs)
    {
        unit.StuckAccumMs = 0d;
        unit.PathProgressStallMs = 0d;
        if (_localMovement.TryLocalAvoidanceStep(unit))
        {
            return;
        }

        if (!TryGetRepathRequest(unit, out var request))
        {
            return;
        }

        var lightRequest = request with { PreserveExistingPathOnFailure = true };
        if (Repath(unit, lightRequest, elapsedMs))
        {
            return;
        }

        if (allowHeavyReroute && elapsedMs - unit.LastHeavyRerouteMs >= HeavyRerouteCooldownMs)
        {
            var heavyRequest = request with { StuckReroute = true, PreserveExistingPathOnFailure = true };
            Repath(unit, heavyRequest, elapsedMs);
        }
    }

    private void UpdatePathProgressState(SimUnit unit, double delta)
    {
        var currentMetric = ComputePathProgressMetric(unit);
        if (float.IsPositiveInfinity(unit.LastPathProgressMetric) ||
            currentMetric + GameConstants.PathProgressImprovementEpsilon < unit.LastPathProgressMetric)
        {
            unit.LastPathProgressMetric = currentMetric;
            unit.PathProgressStallMs = 0d;
            return;
        }

        unit.PathProgressStallMs += delta * 1000d;
    }

    private static float ComputePathProgressMetric(SimUnit unit)
    {
        if (unit.Path.Count == 0)
        {
            return 0f;
        }

        var remaining = unit.Position.DistanceTo(unit.Path[0]);
        for (var index = 1; index < unit.Path.Count; index++)
        {
            remaining += unit.Path[index - 1].DistanceTo(unit.Path[index]);
        }

        return remaining;
    }

    private bool TryBuildCloseRangeFallbackPath(
        SimUnit unit,
        Vector2 worldTarget,
        Vector2 interactionAnchor,
        float arrivalRadius,
        out List<Vector2> path)
    {
        path = [];
        if (arrivalRadius <= 0f)
        {
            return false;
        }

        var distance = unit.Position.DistanceTo(interactionAnchor);
        if (distance > arrivalRadius + GameConstants.TileSize * 1.4f)
        {
            return false;
        }

        var direction = unit.Position - interactionAnchor;
        if (direction.LengthSquared() <= 0.01f)
        {
            direction = unit.Position - worldTarget;
            if (direction.LengthSquared() <= 0.01f)
            {
                direction = Vector2.Right;
            }
        }

        var stopDistance = Mathf.Max(arrivalRadius * 0.9f, unit.Radius + 2f);
        var candidate = interactionAnchor + direction.Normalized() * stopDistance;
        if (!_localMovement.TryMoveToCandidate(unit, candidate, 1.5f))
        {
            return false;
        }

        path.Add(candidate);
        return true;
    }

    private Dictionary<int, float> BuildDynamicPenaltyMap(SimUnit unit, Vector2I goal, int goalRadiusTiles, bool stuckReroute)
    {
        var penalty = new Dictionary<int, float>();
        var goalWorld = _map.TileToWorldCenter(goal.X, goal.Y);
        foreach (var other in _units)
        {
            if (!other.Alive || other == unit || other.Side != unit.Side)
            {
                continue;
            }

            var occupied = _map.WorldToTile(other.Position);
            if (!_map.InBounds(occupied.X, occupied.Y))
            {
                continue;
            }

            var goalSlack = goalRadiusTiles + 1;
            var nearGoal = Mathf.Abs(occupied.X - goal.X) <= goalSlack && Mathf.Abs(occupied.Y - goal.Y) <= goalSlack;
            var sharedCombatTarget = SharesCombatTarget(unit, other);
            if (!nearGoal || !sharedCombatTarget)
            {
                AddTilePenalty(penalty, occupied.X, occupied.Y, sharedCombatTarget ? SoftUnitNeighborTilePenalty : SoftUnitTilePenalty);
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if ((dx == 0 && dy == 0) || !_map.InBounds(occupied.X + dx, occupied.Y + dy))
                        {
                            continue;
                        }

                        AddTilePenalty(penalty, occupied.X + dx, occupied.Y + dy, SoftUnitNeighborTilePenalty);
                    }
                }
            }

            if (!stuckReroute || !ShouldTreatAsTemporaryBlocker(unit, other, goalWorld, goalRadiusTiles))
            {
                continue;
            }

            AddTilePenalty(penalty, occupied.X, occupied.Y, StaticBlockerTilePenalty);
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dy == 0) || !_map.InBounds(occupied.X + dx, occupied.Y + dy))
                    {
                        continue;
                    }

                    AddTilePenalty(penalty, occupied.X + dx, occupied.Y + dy, StaticBlockerNeighborTilePenalty);
                }
            }
        }

        return penalty;
    }

    private bool ShouldTreatAsTemporaryBlocker(SimUnit mover, SimUnit other, Vector2 goalWorld, int goalRadiusTiles)
    {
        if (SharesCombatTarget(mover, other) || !IsLikelyStaticBlocker(other))
        {
            return false;
        }

        var corridorRadius = GameConstants.TileSize * 1.6f;
        var nearGoalRadius = (goalRadiusTiles + 2f) * GameConstants.TileSize;
        var nearGoal = other.Position.DistanceTo(goalWorld) <= nearGoalRadius;
        var inCorridor = DistanceToSegment(other.Position, mover.Position, goalWorld) <= corridorRadius;
        if (!nearGoal && !inCorridor)
        {
            return false;
        }

        if (mover.TargetResource is not null && other.TargetResource == mover.TargetResource)
        {
            return true;
        }

        if (mover.ReturnBuilding is not null && other.ReturnBuilding == mover.ReturnBuilding)
        {
            return true;
        }

        if (mover.TargetBuilding is not null && other.TargetBuilding == mover.TargetBuilding)
        {
            return true;
        }

        return nearGoal || inCorridor;
    }

    private static bool SharesCombatTarget(SimUnit first, SimUnit second)
    {
        return first.TargetCombat is not null &&
               first.TargetCombat == second.TargetCombat &&
               first.CanAttack() &&
               second.CanAttack();
    }

    private static bool IsLikelyStaticBlocker(SimUnit unit)
    {
        if (unit.State is UnitState.Attack or UnitState.Gather or UnitState.Build or UnitState.ReturnCargo)
        {
            return true;
        }

        if (unit.State is UnitState.Move or UnitState.AttackMove)
        {
            return unit.Path.Count == 0 || unit.StuckAccumMs >= 320d;
        }

        return unit.State == UnitState.Idle;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0.01f)
        {
            return point.DistanceTo(start);
        }

        var t = Mathf.Clamp((point - start).Dot(segment) / lengthSquared, 0f, 1f);
        var projection = start + segment * t;
        return point.DistanceTo(projection);
    }

    private static void AddTilePenalty(Dictionary<int, float> penalty, int tx, int ty, float amount)
    {
        var key = ty * GameConstants.MapWidth + tx;
        penalty[key] = penalty.GetValueOrDefault(key) + amount;
    }
}
