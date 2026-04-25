using System;
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
    private const float SharedInteractionCompressionFactor = 0.48f;
    private const float CohortCompressionFactor = 0.52f;
    private const float CohortEmergencyCompressionFactor = 0.34f;
    private const float AllyPassThroughFloorFactor = 0.22f;
    private const float HeadOnAvoidanceDotThreshold = -0.4f;
    private const float HeadOnAvoidanceSideFactor = 1.35f;
    private const float HeadOnAvoidanceForwardFactor = 0.18f;
    private const float DirectApproachSideStepFactor = 0.24f;
    private const float DirectApproachMinSideStep = 1.25f;
    private const float DirectApproachMaxSideStepFactor = 0.45f;
    private const float LaneChangeForwardFactor = 0.36f;
    private const float LaneChangeSideFactor = 0.9f;
    private readonly WorldTileMap _map;
    private readonly List<SimUnit> _units;
    private readonly List<SimBuilding> _buildings;
    private readonly List<SimResourceNode> _resources;
    private readonly UnitSpatialHash _unitSpatialHash;
    private readonly List<SimUnit> _nearbyUnits = [];
    private readonly List<SimUnit> _passThroughUnits = [];
    private readonly float _maxUnitRadius;
    private const float CompactSideStepFactor = 0.3f;
    private const float CompactForwardBiasFactor = 0.16f;

    public LocalMovementService(WorldTileMap map, List<SimUnit> units, List<SimBuilding> buildings, List<SimResourceNode> resources)
    {
        _map = map;
        _units = units;
        _buildings = buildings;
        _resources = resources;
        _unitSpatialHash = new UnitSpatialHash(GameConstants.TileSize * 2f);
        _maxUnitRadius = 66f;
    }

    public void RebuildUnitIndex()
    {
        _unitSpatialHash.Rebuild(_units);
    }

    public void QueryNearbyUnits(Vector2 point, float radius, List<SimUnit> results)
    {
        _unitSpatialHash.Query(point, radius, results);
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
                CommitMove(unit, next);
                unit.Path.RemoveAt(0);
                return unit.Path.Count > 0;
            }

            return TrySteeredAdvance(unit, distance <= 0.01f ? Vector2.Right : toNext / distance, step);
        }

        var direction = toNext / distance;
        var direct = unit.Position + direction * step;
        if (TryMoveToCandidate(unit, direct, 1.5f))
        {
            CommitMove(unit, direct);
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

        var queryRadius = unit.Radius + padding + _maxUnitRadius;
        _unitSpatialHash.Query(candidate, queryRadius, _nearbyUnits);
        _passThroughUnits.Clear();
        foreach (var other in _nearbyUnits)
        {
            if (!other.Alive || other == unit || other == ignoredUnit)
            {
                continue;
            }

            if (AllowsTemporaryAllyPassThrough(unit, other, candidate))
            {
                _passThroughUnits.Add(other);
                continue;
            }

            var minimum = unit.Radius + other.Radius + padding;
            minimum *= GetDynamicClearanceScale(unit, other, candidate);

            if (candidate.DistanceTo(other.Position) < minimum)
            {
                return false;
            }
        }

        if (_passThroughUnits.Count > 0)
        {
            ActivateAllyPassThrough(unit);
            foreach (var other in _passThroughUnits)
            {
                ActivateAllyPassThrough(other);
            }

            unit.LastRecoveryKind = MovementRecoveryKind.AllyPassThrough;
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
        var step = GameConstants.LocalAvoidanceStep;
        var directCandidate = unit.Position + direction * step;
        var blockedByStatic = !IsCandidateStaticallyWalkable(directCandidate, unit.Radius + 1.5f);
        var blocker = FindMovementBlocker(unit, directCandidate, 1.5f);
        if (ShouldPreferCohortFollow(unit, blocker, direction, out var blockerDirection))
        {
            if (TryLaneChangeAroundBlocker(unit, blocker!, direction, step))
            {
                return true;
            }

            if (TryFollowCohortLeader(unit, blocker!, blockerDirection, GameConstants.LocalAvoidanceStep))
            {
                unit.LastRecoveryKind = MovementRecoveryKind.CohortFollow;
                return true;
            }
        }

        if (TryHeadOnAvoidance(unit, blocker, direction, step))
        {
            unit.LastRecoveryKind = MovementRecoveryKind.HeadOnAvoidance;
            return true;
        }

        if (TryAdvanceWithCandidateFan(unit, direction, step, 2f, blocker, blockedByStatic, out var recoveryKind))
        {
            unit.LastRecoveryKind = recoveryKind;
            return true;
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

        CommitMove(unit, candidate);
        return true;
    }

    public bool TryAdvanceToward(SimUnit unit, Vector2 targetPoint, double delta, float padding)
    {
        var toTarget = targetPoint - unit.Position;
        var distance = toTarget.Length();
        if (distance <= 0.05f)
        {
            return false;
        }

        var step = unit.Speed * (float)delta;
        var direction = toTarget / distance;
        var direct = unit.Position + direction * Mathf.Min(step, distance);
        if (TryMoveToCandidate(unit, direct, padding))
        {
            CommitMove(unit, direct);
            return true;
        }

        var probeStep = Mathf.Min(step, distance);
        var blocker = FindMovementBlocker(unit, unit.Position + direction * probeStep, padding);
        var blockedByStatic = !IsCandidateStaticallyWalkable(direct, unit.Radius + padding);
        if (TryHeadOnAvoidance(unit, blocker, direction, probeStep))
        {
            unit.LastRecoveryKind = MovementRecoveryKind.HeadOnAvoidance;
            return true;
        }

        if (TryAdvanceWithCandidateFan(unit, direction, probeStep, padding, blocker, blockedByStatic, out var recoveryKind))
        {
            unit.LastRecoveryKind = recoveryKind;
            return true;
        }

        return false;
    }

    private bool TrySteeredAdvance(SimUnit unit, Vector2 direction, float step)
    {
        if (direction.LengthSquared() <= 0.001f || step <= 0.01f)
        {
            return false;
        }

        var directCandidate = unit.Position + direction * step;
        var blockedByStatic = !IsCandidateStaticallyWalkable(directCandidate, unit.Radius + 1.5f);
        var blocker = FindMovementBlocker(unit, directCandidate, 1.5f);
        if (blocker is null && !blockedByStatic)
        {
            return false;
        }

        if (ShouldPreferCohortFollow(unit, blocker, direction, out var blockerDirection))
        {
            if (TryLaneChangeAroundBlocker(unit, blocker!, direction, step))
            {
                return true;
            }

            if (TryFollowCohortLeader(unit, blocker!, blockerDirection, step))
            {
                unit.LastRecoveryKind = MovementRecoveryKind.CohortFollow;
                return true;
            }
        }

        if (TryHeadOnAvoidance(unit, blocker, direction, step))
        {
            unit.LastRecoveryKind = MovementRecoveryKind.HeadOnAvoidance;
            return true;
        }

        if (blocker is not null && IsMovingForward(blocker) && !blockedByStatic)
        {
            return false;
        }

        if (TryAdvanceWithCandidateFan(unit, direction, step, 1.5f, blocker, blockedByStatic, out var recoveryKind))
        {
            unit.LastRecoveryKind = recoveryKind;
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
            return IsCandidateStaticallyWalkable(end, clearance);
        }

        var steps = Mathf.Max(2, Mathf.CeilToInt(distance / 10f));
        for (var index = 1; index <= steps; index++)
        {
            var sample = start + delta * (index / (float)steps);
            if (!IsCandidateStaticallyWalkable(sample, clearance))
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
        var queryRadius = unit.Radius + padding + _maxUnitRadius;
        _unitSpatialHash.Query(candidate, queryRadius, _nearbyUnits);
        foreach (var other in _nearbyUnits)
        {
            if (!other.Alive || other == unit)
            {
                continue;
            }

            if (AllowsTemporaryAllyPassThrough(unit, other, candidate))
            {
                continue;
            }

            var minimum = unit.Radius + other.Radius + padding;
            minimum *= GetDynamicClearanceScale(unit, other, candidate);

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

        CommitMove(unit, candidate);
        return true;
    }

    private bool TryHeadOnAvoidance(SimUnit unit, SimUnit? blocker, Vector2 direction, float step)
    {
        if (blocker is null || !IsHeadOnConflict(unit, blocker, direction, out var blockerDirection))
        {
            return false;
        }

        var preferredSide = GetHeadOnAvoidanceSide(unit, blocker);
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var convoyFactor = 1f + Mathf.Min(0.75f, CountAlignedFollowers(unit, -direction) * 0.22f);
        var sideStep = Mathf.Max(
            (unit.Radius + blocker.Radius + 3f) * HeadOnAvoidanceSideFactor * convoyFactor,
            step * 1.15f);
        var forwardBias = direction * Mathf.Min(step * HeadOnAvoidanceForwardFactor, sideStep * 0.22f);
        var blockerForwardBias = blockerDirection * Mathf.Min(step * 0.08f, sideStep * 0.12f);
        var offsets = new[]
        {
            forwardBias + perpendicular * preferredSide * sideStep,
            forwardBias - perpendicular * preferredSide * sideStep,
            blockerForwardBias + perpendicular * preferredSide * (sideStep * 0.82f),
            blockerForwardBias - perpendicular * preferredSide * (sideStep * 0.82f)
        };

        foreach (var offset in offsets)
        {
            var candidate = unit.Position + offset;
            if (!TryMoveToCandidate(unit, candidate, 1.5f))
            {
                continue;
            }

            CommitMove(unit, candidate);
            return true;
        }

        return false;
    }

    private bool TryLaneChangeAroundBlocker(SimUnit unit, SimUnit blocker, Vector2 direction, float step)
    {
        var preferredSide = GetPreferredSteerSide(unit, direction, blocker);
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var sideStep = Mathf.Clamp(step * LaneChangeSideFactor, 2f, GameConstants.LocalAvoidanceStep * 1.15f);
        var forwardStep = direction * Mathf.Min(step * LaneChangeForwardFactor, sideStep * 0.75f);
        var offsets = new[]
        {
            forwardStep + perpendicular * preferredSide * sideStep,
            forwardStep - perpendicular * preferredSide * sideStep
        };

        foreach (var offset in offsets)
        {
            var candidate = unit.Position + offset;
            if (!TryMoveToCandidate(unit, candidate, 1.5f))
            {
                continue;
            }

            CommitMove(unit, candidate);
            unit.LastRecoveryKind = MovementRecoveryKind.CohortLaneChange;
            return true;
        }

        return false;
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

    private static bool IsHeadOnConflict(SimUnit unit, SimUnit blocker, Vector2 direction, out Vector2 blockerDirection)
    {
        blockerDirection = GetTravelDirection(blocker);
        if (blockerDirection == Vector2.Zero)
        {
            return false;
        }

        var blockerAhead = direction.Dot(blocker.Position - unit.Position) > unit.Radius * 0.2f;
        if (!blockerAhead)
        {
            return false;
        }

        return direction.Dot(blockerDirection) <= HeadOnAvoidanceDotThreshold;
    }

    private int CountAlignedFollowers(SimUnit unit, Vector2 backwardDirection)
    {
        var count = 0;
        var radius = unit.Radius + GameConstants.GroupSpacing * 1.8f;
        _unitSpatialHash.Query(unit.Position, radius, _nearbyUnits);
        foreach (var other in _nearbyUnits)
        {
            if (!other.Alive || other == unit || other.Side != unit.Side)
            {
                continue;
            }

            var offset = other.Position - unit.Position;
            if (offset.LengthSquared() <= 1f)
            {
                continue;
            }

            var otherDirection = GetTravelDirection(other);
            if (otherDirection == Vector2.Zero || backwardDirection.Dot(offset.Normalized()) < 0.45f)
            {
                continue;
            }

            if (otherDirection.Dot(-backwardDirection) >= 0.55f)
            {
                count++;
            }
        }

        return count;
    }

    private static float GetHeadOnAvoidanceSide(SimUnit unit, SimUnit blocker)
    {
        return unit.Id < blocker.Id ? 1f : -1f;
    }

    private static bool AllowsSharedInteractionCompression(SimUnit unit, SimUnit other, Vector2 candidate)
    {
        if (!TryGetSharedInteractionAnchor(unit, other, out var anchor, out var threshold))
        {
            return false;
        }

        return candidate.DistanceTo(anchor) <= threshold || unit.Position.DistanceTo(anchor) <= threshold;
    }

    public void CommitMove(SimUnit unit, Vector2 newPosition)
    {
        var oldPosition = unit.Position;
        unit.Position = newPosition;
        _unitSpatialHash.UpdateUnit(unit, oldPosition, newPosition);
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

        return IsCandidateStaticallyWalkable(candidate, clearance);
    }

    private bool IsCandidateStaticallyWalkable(Vector2 candidate, float clearance)
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

    private static bool TryGetSharedInteractionAnchor(SimUnit first, SimUnit second, out Vector2 anchor, out float threshold)
    {
        anchor = Vector2.Zero;
        threshold = 0f;

        if (first.TargetBuilding is { Alive: true } firstBuilding &&
            second.TargetBuilding == firstBuilding &&
            first.State == UnitState.Build &&
            second.State == UnitState.Build)
        {
            anchor = firstBuilding.Center;
            threshold = firstBuilding.Radius + GameConstants.TileSize * 1.45f;
            return true;
        }

        if (first.TargetResource is { Alive: true } firstResource &&
            second.TargetResource == firstResource &&
            first.State == UnitState.Gather &&
            second.State == UnitState.Gather)
        {
            anchor = firstResource.Center;
            threshold = firstResource.Radius + GameConstants.TileSize * 1.35f;
            return true;
        }

        if (first.ReturnBuilding is { Alive: true } firstHall &&
            second.ReturnBuilding == firstHall &&
            first.State == UnitState.ReturnCargo &&
            second.State == UnitState.ReturnCargo)
        {
            anchor = firstHall.Center;
            threshold = firstHall.Radius + GameConstants.TileSize * 1.45f;
            return true;
        }

        return false;
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

    private float GetDynamicClearanceScale(SimUnit unit, SimUnit other, Vector2 candidate)
    {
        var scale = 1f;
        if (SharesActiveMarchCohort(unit, other))
        {
            var stalled = unit.StuckAccumMs >= 120d ||
                          other.StuckAccumMs >= 120d ||
                          unit.PathProgressStallMs >= 120d ||
                          other.PathProgressStallMs >= 120d;
            scale = stalled ? CohortEmergencyCompressionFactor : CohortCompressionFactor;
        }

        if (AllowsSharedInteractionCompression(unit, other, candidate))
        {
            scale = Math.Min(scale, SharedInteractionCompressionFactor);
        }

        return scale;
    }

    private static bool AllowsTemporaryAllyPassThrough(SimUnit unit, SimUnit other, Vector2 candidate)
    {
        if (!CanUseAllyPassThrough(unit, other))
        {
            return false;
        }

        var minimum = (unit.Radius + other.Radius) * AllyPassThroughFloorFactor;
        return candidate.DistanceTo(other.Position) >= minimum;
    }

    private static bool CanUseAllyPassThrough(SimUnit unit, SimUnit other)
    {
        if (unit.Side != other.Side ||
            !unit.Alive ||
            !other.Alive)
        {
            return false;
        }

        return unit.StuckAccumMs >= GameConstants.AllyPassThroughDelayMs ||
               unit.PathProgressStallMs >= GameConstants.AllyPassThroughDelayMs ||
               other.StuckAccumMs >= GameConstants.AllyPassThroughDelayMs ||
               other.PathProgressStallMs >= GameConstants.AllyPassThroughDelayMs;
    }

    private static void ActivateAllyPassThrough(SimUnit unit)
    {
        unit.IsUsingAllyPassThrough = true;
        unit.AllyPassThroughTimerMs = Mathf.Max((float)unit.AllyPassThroughTimerMs, GameConstants.AllyPassThroughHoldMs);
    }

    private bool TryAdvanceWithCandidateFan(
        SimUnit unit,
        Vector2 direction,
        float step,
        float padding,
        SimUnit? blocker,
        bool blockedByStatic,
        out MovementRecoveryKind recoveryKind)
    {
        recoveryKind = MovementRecoveryKind.None;
        var preferredSide = GetPreferredSteerSide(unit, direction, blocker);
        var waypoint = unit.Path.Count > 0 ? unit.Path[0] : unit.Position + direction * step;
        var bestScore = float.PositiveInfinity;
        var bestCandidate = Vector2.Zero;
        var bestRecovery = blockedByStatic ? MovementRecoveryKind.StaticSlide : MovementRecoveryKind.LocalAvoidance;
        var found = false;

        TryFanCandidate(0f, 0.55f, blockedByStatic ? MovementRecoveryKind.StaticSlide : MovementRecoveryKind.LocalAvoidance);
        TryFanCandidate(0.18f * preferredSide, 0.92f, blockedByStatic ? MovementRecoveryKind.StaticSlide : MovementRecoveryKind.LocalAvoidance);
        TryFanCandidate(-0.18f * preferredSide, 0.92f, blockedByStatic ? MovementRecoveryKind.StaticSlide : MovementRecoveryKind.LocalAvoidance);
        TryFanCandidate(0.42f * preferredSide, 0.86f, blockedByStatic ? MovementRecoveryKind.StaticSlide : MovementRecoveryKind.LocalAvoidance);
        TryFanCandidate(-0.42f * preferredSide, 0.86f, blockedByStatic ? MovementRecoveryKind.StaticSlide : MovementRecoveryKind.LocalAvoidance);
        TryFanCandidate(0.72f * preferredSide, 0.78f, MovementRecoveryKind.StaticSlide);
        TryFanCandidate(-0.72f * preferredSide, 0.78f, MovementRecoveryKind.StaticSlide);

        if (blockedByStatic)
        {
            var perpendicular = new Vector2(-direction.Y, direction.X);
            TryOffsetCandidate(direction * (step * 0.18f) + perpendicular * preferredSide * Mathf.Max(1.5f, step * 0.8f), MovementRecoveryKind.StaticSlide);
            TryOffsetCandidate(direction * (step * 0.18f) - perpendicular * preferredSide * Mathf.Max(1.5f, step * 0.8f), MovementRecoveryKind.StaticSlide);
        }

        if (!found)
        {
            return false;
        }

        CommitMove(unit, bestCandidate);
        recoveryKind = bestRecovery;
        return true;

        void TryFanCandidate(float angle, float distanceScale, MovementRecoveryKind candidateRecovery)
        {
            var candidateDirection = direction.Rotated(angle);
            var candidate = unit.Position + candidateDirection * (step * distanceScale);
            TryCandidate(candidate, candidateRecovery, Mathf.Abs(angle) * 0.65f);
        }

        void TryOffsetCandidate(Vector2 offset, MovementRecoveryKind candidateRecovery)
        {
            TryCandidate(unit.Position + offset, candidateRecovery, 0.55f);
        }

        void TryCandidate(Vector2 candidate, MovementRecoveryKind candidateRecovery, float turnPenalty)
        {
            if (!TryMoveToCandidate(unit, candidate, padding))
            {
                return;
            }

            var forwardProgress = unit.Position.DistanceTo(waypoint) - candidate.DistanceTo(waypoint);
            var blockerPenalty = blocker is null ? 0f : Mathf.Max(0f, blocker.Position.DistanceTo(candidate) - (unit.Radius + blocker.Radius));
            var score = candidate.DistanceTo(waypoint) - forwardProgress * 0.35f + turnPenalty + blockerPenalty * 0.01f;
            if (score >= bestScore)
            {
                return;
            }

            bestScore = score;
            bestCandidate = candidate;
            bestRecovery = candidateRecovery;
            found = true;
        }
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
