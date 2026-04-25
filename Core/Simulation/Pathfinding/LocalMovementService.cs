using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Resources;
using RtsNaGodote.Core.Simulation.Units;
using WorldTileMap = RtsNaGodote.Core.Simulation.World.TileMap;

namespace RtsNaGodote.Core.Simulation.Pathfinding;

public sealed class LocalMovementService
{
    private readonly WorldTileMap _map;
    private readonly List<SimUnit> _units;
    private readonly List<SimBuilding> _buildings;
    private readonly List<SimResourceNode> _resources;
    private const float CompactSideStepFactor = 0.45f;
    private const float CompactForwardBiasFactor = 0.2f;

    public LocalMovementService(WorldTileMap map, List<SimUnit> units, List<SimBuilding> buildings, List<SimResourceNode> resources)
    {
        _map = map;
        _units = units;
        _buildings = buildings;
        _resources = resources;
    }

    public bool AdvanceAlongPathWithSteering(SimUnit unit, double delta)
    {
        if (!unit.Alive || unit.Path.Count == 0)
        {
            return false;
        }

        var step = unit.Speed * (float)delta;
        var next = unit.Path[0];
        var toNext = next - unit.Position;
        var distance = toNext.Length();
        if (distance <= step)
        {
            if (TryMoveToCandidate(unit, next, 1.5f))
            {
                unit.Position = next;
                unit.Path.RemoveAt(0);
                return unit.Path.Count > 0;
            }

            return TrySteeredAdvance(unit, distance <= 0.01f ? Vector2.Right : toNext / distance, step);
        }

        var direction = toNext / distance;
        var direct = unit.Position + direction * step;
        if (TryMoveToCandidate(unit, direct, 1.5f))
        {
            unit.Position = direct;
            return true;
        }

        return TrySteeredAdvance(unit, direction, step);
    }

    public bool TryMoveToCandidate(SimUnit unit, Vector2 candidate, float padding, SimUnit? ignoredUnit = null)
    {
        if (!IsCandidateWalkable(candidate, unit.Radius + padding))
        {
            return false;
        }

        foreach (var other in _units)
        {
            if (!other.Alive || other == unit || other == ignoredUnit)
            {
                continue;
            }

            if (AllowsCohortPassThrough(unit, other))
            {
                continue;
            }

            var minimum = unit.Radius + other.Radius + padding;
            if (candidate.DistanceTo(other.Position) < minimum)
            {
                return false;
            }
        }

        return true;
    }

    public bool TryLocalAvoidanceStep(SimUnit unit)
    {
        if (unit.Path.Count == 0)
        {
            return false;
        }

        var direction = unit.Path[0] - unit.Position;
        if (direction.LengthSquared() <= 0.01f)
        {
            return false;
        }

        direction = direction.Normalized();
        var blocker = FindMovementBlocker(unit, unit.Position + direction * GameConstants.LocalAvoidanceStep, 1.5f);
        if (ShouldPreferCohortFollow(unit, blocker, direction, out var blockerDirection))
        {
            if (TryFollowCohortLeader(unit, blocker!, blockerDirection, GameConstants.LocalAvoidanceStep))
            {
                return true;
            }

            if (IsMovingForward(blocker!))
            {
                return false;
            }
        }

        var preferredSide = GetPreferredSteerSide(unit, direction, blocker);
        var perpendicular = new Vector2(-direction.Y, direction.X) * preferredSide;
        var sideStep = GameConstants.LocalAvoidanceStep * 0.7f;
        var offsets = new[]
        {
            perpendicular * sideStep,
            -perpendicular * sideStep
        };

        foreach (var offset in offsets)
        {
            if (TryMoveIntoFreeSpace(unit, offset))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryMoveIntoFreeSpace(SimUnit unit, Vector2 offset)
    {
        var candidate = unit.Position + offset;
        if (!TryMoveToCandidate(unit, candidate, 2f))
        {
            return false;
        }

        unit.Position = candidate;
        return true;
    }

    private bool TrySteeredAdvance(SimUnit unit, Vector2 direction, float step)
    {
        if (direction.LengthSquared() <= 0.001f || step <= 0.01f)
        {
            return false;
        }

        var blocker = FindMovementBlocker(unit, unit.Position + direction * step, 1.5f);
        if (blocker is null)
        {
            return false;
        }

        if (ShouldPreferCohortFollow(unit, blocker, direction, out var blockerDirection))
        {
            if (TryFollowCohortLeader(unit, blocker, blockerDirection, step))
            {
                return true;
            }

            if (IsMovingForward(blocker))
            {
                return false;
            }
        }

        var preferredSide = GetPreferredSteerSide(unit, direction, blocker);
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var sideStep = Mathf.Clamp(step * CompactSideStepFactor, 2f, GameConstants.LocalAvoidanceStep * 0.8f);
        var forwardBias = direction * Mathf.Min(step * CompactForwardBiasFactor, sideStep * 0.6f);
        var offsets = new[]
        {
            forwardBias + perpendicular * preferredSide * sideStep,
            forwardBias - perpendicular * preferredSide * sideStep
        };

        foreach (var offset in offsets)
        {
            var candidate = unit.Position + offset;
            if (!TryMoveToCandidate(unit, candidate, 1.5f))
            {
                continue;
            }

            unit.Position = candidate;
            return true;
        }

        return false;
    }

    public bool HasDirectStaticPath(Vector2 start, Vector2 end, float clearance)
    {
        var delta = end - start;
        var distance = delta.Length();
        if (distance <= 0.01f)
        {
            return IsCandidateWalkable(end, clearance);
        }

        var steps = Mathf.Max(2, Mathf.CeilToInt(distance / 10f));
        for (var index = 1; index <= steps; index++)
        {
            var sample = start + delta * (index / (float)steps);
            if (!IsCandidateWalkable(sample, clearance))
            {
                return false;
            }
        }

        return true;
    }

    private SimUnit? FindMovementBlocker(SimUnit unit, Vector2 candidate, float padding)
    {
        SimUnit? best = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var other in _units)
        {
            if (!other.Alive || other == unit)
            {
                continue;
            }

            if (AllowsCohortPassThrough(unit, other))
            {
                continue;
            }

            var minimum = unit.Radius + other.Radius + padding;
            var distance = candidate.DistanceTo(other.Position);
            if (distance >= minimum || distance >= bestDistance)
            {
                continue;
            }

            best = other;
            bestDistance = distance;
        }

        return best;
    }

    private bool TryFollowCohortLeader(SimUnit unit, SimUnit blocker, Vector2 blockerDirection, float step)
    {
        var followSpacing = (unit.Radius + blocker.Radius + 1.5f) * 0.6f;
        var trailingPoint = blocker.Position - blockerDirection * followSpacing;
        var candidate = unit.Position.MoveToward(trailingPoint, step);
        if (candidate.DistanceTo(unit.Position) <= 0.05f)
        {
            return false;
        }

        if (!TryMoveToCandidate(unit, candidate, 1f))
        {
            return false;
        }

        unit.Position = candidate;
        return true;
    }

    private static bool ShouldPreferCohortFollow(SimUnit unit, SimUnit? blocker, Vector2 direction, out Vector2 blockerDirection)
    {
        blockerDirection = Vector2.Zero;
        if (blocker is null || !SharesActiveMarchCohort(unit, blocker) || unit.IsInTerminalFormation || blocker.IsInTerminalFormation)
        {
            return false;
        }

        blockerDirection = GetTravelDirection(blocker);
        if (blockerDirection == Vector2.Zero)
        {
            blockerDirection = direction;
        }

        var sameDirection = direction.Dot(blockerDirection);
        var blockerAhead = direction.Dot(blocker.Position - unit.Position) > unit.Radius * 0.2f;
        return sameDirection >= 0.65f && blockerAhead;
    }

    private static bool AllowsCohortPassThrough(SimUnit unit, SimUnit other)
    {
        return SharesActiveMarchCohort(unit, other);
    }

    private static float GetPreferredSteerSide(SimUnit unit, Vector2 direction, SimUnit? blocker)
    {
        if (blocker is null)
        {
            return unit.Id % 2 == 0 ? 1f : -1f;
        }

        var perpendicular = new Vector2(-direction.Y, direction.X);
        var lateralDot = perpendicular.Dot(blocker.Position - unit.Position);
        if (Mathf.Abs(lateralDot) <= 0.5f)
        {
            return unit.Id % 2 == 0 ? 1f : -1f;
        }

        return lateralDot > 0f ? -1f : 1f;
    }

    private bool IsCandidateWalkable(Vector2 candidate, float clearance)
    {
        var tile = _map.WorldToTile(candidate);
        if (!_map.IsWalkable(tile.X, tile.Y))
        {
            return false;
        }

        return !OverlapsStaticObstacle(candidate, clearance);
    }

    private bool OverlapsStaticObstacle(Vector2 candidate, float clearance)
    {
        foreach (var building in _buildings)
        {
            if (!building.Alive)
            {
                continue;
            }

            if (OverlapsFootprint(
                    candidate,
                    clearance,
                    building.TilePosition,
                    building.SizeTiles,
                    building.SizeTiles))
            {
                return true;
            }
        }

        foreach (var resource in _resources)
        {
            if (!resource.Alive)
            {
                continue;
            }

            if (OverlapsFootprint(
                    candidate,
                    clearance,
                    resource.TilePosition,
                    resource.TileWidth,
                    resource.TileHeight))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SharesActiveMarchCohort(SimUnit first, SimUnit second)
    {
        return first.HasMovementCohort &&
               second.HasMovementCohort &&
               first.MovementCohortId == second.MovementCohortId &&
               first.MovementCohortId != 0 &&
               first.State is UnitState.Move or UnitState.AttackMove &&
               second.State is UnitState.Move or UnitState.AttackMove &&
               !first.IsInTerminalFormation &&
               !second.IsInTerminalFormation;
    }

    private static bool IsMovingForward(SimUnit unit)
    {
        return unit.Path.Count > 0 &&
               unit.State is UnitState.Move or UnitState.AttackMove &&
               unit.StuckAccumMs < 180d &&
               unit.PathProgressStallMs < 180d;
    }

    private static Vector2 GetTravelDirection(SimUnit unit)
    {
        for (var index = 0; index < unit.Path.Count; index++)
        {
            var direction = unit.Path[index] - unit.Position;
            if (direction.LengthSquared() > 1f)
            {
                return direction.Normalized();
            }
        }

        return Vector2.Zero;
    }

    private static bool OverlapsFootprint(
        Vector2 point,
        float clearance,
        Vector2I tilePosition,
        int widthTiles,
        int heightTiles)
    {
        var minX = tilePosition.X * GameConstants.TileSize;
        var minY = tilePosition.Y * GameConstants.TileSize;
        var maxX = minX + widthTiles * GameConstants.TileSize;
        var maxY = minY + heightTiles * GameConstants.TileSize;
        var closestX = Mathf.Clamp(point.X, minX, maxX);
        var closestY = Mathf.Clamp(point.Y, minY, maxY);
        var dx = point.X - closestX;
        var dy = point.Y - closestY;
        return dx * dx + dy * dy < clearance * clearance;
    }
}
