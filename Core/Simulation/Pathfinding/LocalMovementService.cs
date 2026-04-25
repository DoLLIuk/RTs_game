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
        var tile = _map.WorldToTile(candidate);
        if (!_map.IsWalkable(tile.X, tile.Y))
        {
            return false;
        }

        foreach (var other in _units)
        {
            if (!other.Alive || other == unit || other == ignoredUnit)
            {
                continue;
            }

            var minimum = unit.Radius + other.Radius + padding;
            if (candidate.DistanceTo(other.Position) < minimum)
            {
                return false;
            }
        }

        return !OverlapsStaticObstacle(unit, candidate, padding);
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

    private bool OverlapsStaticObstacle(SimUnit unit, Vector2 candidate, float padding)
    {
        foreach (var building in _buildings)
        {
            if (!building.Alive)
            {
                continue;
            }

            var minimum = unit.Radius + building.Radius + padding;
            if (candidate.DistanceTo(building.Center) < minimum)
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

            var minimum = unit.Radius + resource.Radius + padding;
            if (candidate.DistanceTo(resource.Center) < minimum)
            {
                return true;
            }
        }

        return false;
    }
}
