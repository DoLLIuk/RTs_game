using System;
using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Units;

namespace RtsNaGodote.Core.Simulation.Pathfinding;

public sealed class UnitSeparationService
{
    private readonly List<SimUnit> _units;
    private readonly LocalMovementService _localMovement;

    public UnitSeparationService(List<SimUnit> units, LocalMovementService localMovement)
    {
        _units = units;
        _localMovement = localMovement;
    }

    public void ApplySeparation(double delta)
    {
        var strength = Math.Min(1d, delta / 0.01667d) * 0.55d;
        for (var i = 0; i < _units.Count; i++)
        {
            var first = _units[i];
            if (!first.Alive)
            {
                continue;
            }

            for (var j = i + 1; j < _units.Count; j++)
            {
                var second = _units[j];
                if (!second.Alive || second.Side != first.Side)
                {
                    continue;
                }

                var minimum = first.Radius + second.Radius + 1.25f;
                var deltaVector = second.Position - first.Position;
                var distance = deltaVector.Length();
                if (distance <= 0.01f || distance >= minimum)
                {
                    continue;
                }

                if (TryResolveHeadOnDeadlock(first, second, deltaVector, minimum))
                {
                    continue;
                }

                var overlap = minimum - distance;
                if (overlap <= 0.2f)
                {
                    continue;
                }

                var push = Mathf.Clamp((float)(overlap * 0.35f * strength), 0.2f, 1.4f);
                var normal = deltaVector / distance;
                TryNudge(first, -normal * push, second);
                TryNudge(second, normal * push, first);
            }
        }
    }

    private bool TryResolveHeadOnDeadlock(SimUnit first, SimUnit second, Vector2 deltaVector, float minimum)
    {
        var firstDirection = GetPathTravelDirection(first);
        var secondDirection = GetPathTravelDirection(second);
        if (firstDirection == Vector2.Zero || secondDirection == Vector2.Zero)
        {
            return false;
        }

        if (firstDirection.Dot(secondDirection) > -0.45f)
        {
            return false;
        }

        var axis = deltaVector.Normalized();
        var firstFacing = firstDirection.Dot(axis);
        var secondFacing = secondDirection.Dot(-axis);
        if (firstFacing < 0.35f || secondFacing < 0.35f)
        {
            return false;
        }

        var overlap = Mathf.Max(0f, minimum - deltaVector.Length());
        if (overlap < GameConstants.DeadlockResolveMinOverlap || !IsDeadlockResolutionReady(first, second))
        {
            return false;
        }

        var leader = CompareMovementPriority(first, second) <= 0 ? first : second;
        var yielder = leader == first ? second : first;
        var leaderDirection = leader == first ? firstDirection : secondDirection;
        var sideSign = yielder.Id % 2 == 0 ? 1f : -1f;
        var lateral = new Vector2(-leaderDirection.Y, leaderDirection.X) * sideSign;
        var yieldStep = Mathf.Clamp(
            overlap * 0.32f + GameConstants.LocalAvoidanceStep * 0.12f,
            GameConstants.DeadlockYieldMinStep * 0.8f,
            GameConstants.DeadlockYieldMaxStep * 0.75f);
        var offsets = new[]
        {
            lateral * yieldStep,
            -lateral * yieldStep,
            -leaderDirection * (yieldStep * 0.45f)
        };

        foreach (var offset in offsets)
        {
            if (!_localMovement.TryMoveIntoFreeSpace(yielder, offset))
            {
                continue;
            }

            yielder.StuckAccumMs = 0d;
            yielder.PathProgressStallMs = 0d;
            yielder.LastPathProgressMetric = float.PositiveInfinity;
            return true;
        }

        return false;
    }

    private static bool IsDeadlockResolutionReady(SimUnit first, SimUnit second)
    {
        var triggerMs = GameConstants.DeadlockResolveTriggerMs;
        var firstStalled = first.PathProgressStallMs >= triggerMs || first.StuckAccumMs >= triggerMs;
        var secondStalled = second.PathProgressStallMs >= triggerMs || second.StuckAccumMs >= triggerMs;
        return firstStalled && secondStalled;
    }

    private static Vector2 GetPathTravelDirection(SimUnit unit)
    {
        if (unit.Path.Count == 0)
        {
            return Vector2.Zero;
        }

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

    private static int CompareMovementPriority(SimUnit first, SimUnit second)
    {
        var firstPriority = GetMovementPriority(first);
        var secondPriority = GetMovementPriority(second);
        if (firstPriority != secondPriority)
        {
            return secondPriority.CompareTo(firstPriority);
        }

        return first.Id.CompareTo(second.Id);
    }

    private static int GetMovementPriority(SimUnit unit)
    {
        return unit.State switch
        {
            UnitState.Attack => 5,
            UnitState.ReturnCargo => 4,
            UnitState.Gather => 4,
            UnitState.Build => 4,
            UnitState.Move => 3,
            UnitState.AttackMove => 3,
            _ => 1
        };
    }

    private void TryNudge(SimUnit unit, Vector2 offset, SimUnit? ignoredUnit = null)
    {
        var next = unit.Position + offset;
        if (!_localMovement.TryMoveToCandidate(unit, next, 1f, ignoredUnit))
        {
            return;
        }

        unit.Position = next;
    }
}
