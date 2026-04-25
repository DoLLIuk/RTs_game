using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Simulation.Units;

namespace RtsNaGodote.Core.Simulation;

public sealed partial class GameSimulation
{
    private readonly ScoutSystem _scoutSystem;

    private ScoutContext CreateScoutContext()
    {
        return new ScoutContext(
            Difficulty,
            Map,
            Units,
            Buildings,
            _difficultyDefinition,
            () => _elapsedMs,
            () => _playerVisionSnapshot,
            () => _aiKnowledge.LastKnownPlayerBase,
            () => _aiKnowledge.LastKnownPlayerBaseTile,
            (position, radius) => _aiKnowledge.CountScoutKnownWorkersNear(position, radius, ScoutIntelFreshMemoryMs),
            (position, radius) => _aiKnowledge.CountScoutKnownCombatUnitsNear(position, radius, ScoutIntelFreshMemoryMs),
            (position, radius) => _aiKnowledge.HasScoutKnownTowerNear(position, radius, ScoutIntelFreshMemoryMs),
            (position, radius) => _aiKnowledge.HasScoutKnownOuterTargetNear(position, radius, ScoutIntelFreshMemoryMs),
            (position, radius) => _aiKnowledge.EstimateScoutKnownThreatAt(position, radius, ScoutIntelFreshMemoryMs),
            TryFindWalkableRaidPoint,
            BuildDynamicTilePenalty,
            CommandUnitMove);
    }

    private bool ShouldContinueScoutMission(bool baseConfirmed)
    {
        return _scoutSystem.ShouldContinueMission(baseConfirmed);
    }

    private void CommandScout(List<SimUnit> army, List<SimUnit> mainArmy, bool workersFallback, Vector2 suspectedBase, Vector2 stagePoint)
    {
        var scout = _scoutSystem.SelectScoutUnit(army, workersFallback, suspectedBase, stagePoint);
        if (scout is null)
        {
            _scoutSystem.ResetMission();
            foreach (var unit in mainArmy)
            {
                CommandUnitMove(unit, stagePoint);
            }

            return;
        }

        _scoutSystem.EnsureMission(scout, suspectedBase, stagePoint);
        var scoutTarget = _scoutSystem.UpdateMission(scout, suspectedBase, stagePoint);
        _scoutSystem.TraceMissionTick(scout, scoutTarget);
        if (_elapsedMs - _aiLastScoutCommandMs > 420d ||
            scout.State != UnitState.Move ||
            !scout.PathDestination.HasValue ||
            scout.PathDestination.Value.DistanceTo(scoutTarget) > 18f)
        {
            CommandUnitMove(scout, scoutTarget);
            _aiLastScoutCommandMs = _elapsedMs;
        }

        foreach (var unit in mainArmy)
        {
            if (unit != scout)
            {
                CommandUnitMove(unit, stagePoint);
            }
        }
    }
}
