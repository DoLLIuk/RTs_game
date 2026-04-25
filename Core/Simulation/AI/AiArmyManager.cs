using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Units;

namespace RtsNaGodote.Core.Simulation;

internal sealed class AiArmyManager
{
    private readonly AiArmyManagerContext _context;

    public AiArmyManager(AiArmyManagerContext context)
    {
        _context = context;
    }

    public AiSquadMetrics CalculateMetrics(List<SimUnit> squad)
    {
        if (squad.Count == 0)
        {
            return new AiSquadMetrics(Vector2.Zero, 0f, 0f, 0, 0, 0, 0);
        }

        var center = Vector2.Zero;
        var power = 0f;
        var slowest = float.PositiveInfinity;
        var frontline = 0;
        var backline = 0;
        var siege = 0;
        foreach (var unit in squad)
        {
            center += unit.Position;
            power += CalculateUnitPower(unit);
            slowest = float.Min(slowest, unit.Speed);
            if (unit.Kind == UnitKind.Catapult)
            {
                siege++;
            }
            else if (unit.IsRanged())
            {
                backline++;
            }
            else
            {
                frontline++;
            }
        }

        return new AiSquadMetrics(center / squad.Count, power, slowest, frontline, backline, siege, squad.Count);
    }

    public bool ShouldUseHarassSplit(
        bool hasBarracks,
        bool baseConfirmed,
        bool pressure,
        AiSquadMetrics armyMetrics,
        float knownEnemyPower)
    {
        if (_context.Init.AiProfile != AiProfile.Harass || pressure || !hasBarracks || !baseConfirmed)
        {
            return false;
        }

        if (_context.CurrentState is AiState.Push or AiState.Finish or AiState.Regroup)
        {
            return false;
        }

        return !ShouldFinish(armyMetrics, knownEnemyPower) && !ShouldPush(armyMetrics, knownEnemyPower);
    }

    public AiArmyPlan BuildPlan(List<SimUnit> army, bool allowHarassSplit)
    {
        var mainArmy = new List<SimUnit>();
        var harassSquad = new List<SimUnit>();
        var eligibleArmy = army.FindAll(unit => !_context.IsScoutReserved(unit.Id));

        if (!allowHarassSplit || eligibleArmy.Count < 5 || !_context.LastKnownPlayerBase.HasValue)
        {
            mainArmy.AddRange(eligibleArmy);
            return new AiArmyPlan(mainArmy, harassSquad, CalculateMetrics(mainArmy), CalculateMetrics(harassSquad));
        }

        var totalPower = 0f;
        var totalFrontline = 0;
        foreach (var unit in eligibleArmy)
        {
            totalPower += CalculateUnitPower(unit);
            if (!unit.IsRanged() && unit.Kind != UnitKind.Catapult)
            {
                totalFrontline++;
            }
        }

        var desiredSize = GetDesiredHarassSquadSize(eligibleArmy.Count);
        var pickedPower = 0f;
        var pickedFrontline = 0;
        var candidates = BuildHarassCandidates(eligibleArmy);
        foreach (var candidate in candidates)
        {
            if (harassSquad.Count >= desiredSize)
            {
                break;
            }

            var candidatePower = CalculateUnitPower(candidate);
            var isFrontline = !candidate.IsRanged() && candidate.Kind != UnitKind.Catapult;
            var remainingFrontline = totalFrontline - pickedFrontline - (isFrontline ? 1 : 0);
            var remainingPower = totalPower - pickedPower - candidatePower;
            if (remainingFrontline < 2 || remainingPower < totalPower * 0.65f)
            {
                continue;
            }

            harassSquad.Add(candidate);
            pickedPower += candidatePower;
            if (isFrontline)
            {
                pickedFrontline++;
            }
        }

        foreach (var unit in eligibleArmy)
        {
            if (!harassSquad.Contains(unit))
            {
                mainArmy.Add(unit);
            }
        }

        if (harassSquad.Count == 0)
        {
            mainArmy.Clear();
            mainArmy.AddRange(eligibleArmy);
        }

        return new AiArmyPlan(mainArmy, harassSquad, CalculateMetrics(mainArmy), CalculateMetrics(harassSquad));
    }

    public AiState DetermineState(
        bool hasBarracks,
        AiSquadMetrics armyMetrics,
        AiSquadMetrics mainMetrics,
        AiSquadMetrics harassMetrics,
        bool baseConfirmed,
        bool pressure,
        float knownEnemyPower)
    {
        if (pressure)
        {
            return AiState.Defend;
        }

        if (_context.CurrentState == AiState.Harass && (ShouldFinish(armyMetrics, knownEnemyPower) || ShouldPush(armyMetrics, knownEnemyPower)))
        {
            return AiState.Regroup;
        }

        if (_context.CurrentState == AiState.Scout && _context.ShouldContinueScoutMission(baseConfirmed))
        {
            return AiState.Scout;
        }

        if (ShouldFinish(armyMetrics, knownEnemyPower))
        {
            return AiState.Finish;
        }

        if (!baseConfirmed && _context.ElapsedMs >= _context.DifficultyDefinition.ScoutDelayMs)
        {
            return AiState.Scout;
        }

        if (!hasBarracks || _context.ElapsedMs < _context.DifficultyDefinition.ScoutDelayMs)
        {
            return AiState.Open;
        }

        if (_context.CurrentState == AiState.Push || _context.CurrentState == AiState.Finish)
        {
            return ShouldRetreat(armyMetrics, knownEnemyPower) ? AiState.Regroup : _context.CurrentState;
        }

        if (_context.CurrentState == AiState.Harass && harassMetrics.Count > 0 && _context.ElapsedMs - _context.StateEnteredMs < 5200d)
        {
            return AiState.Harass;
        }

        if (_context.CurrentState == AiState.Regroup && _context.ElapsedMs - _context.StateEnteredMs < _context.DifficultyDefinition.RegroupDurationMs)
        {
            return AiState.Regroup;
        }

        if (_context.Init.AiProfile == AiProfile.Harass && CanLaunchHarass(mainMetrics, harassMetrics))
        {
            return AiState.Harass;
        }

        if (ShouldPush(armyMetrics, knownEnemyPower))
        {
            return AiState.Push;
        }

        return armyMetrics.Count >= 3 ? AiState.Regroup : AiState.Boom;
    }

    private bool ShouldPush(AiSquadMetrics mainMetrics, float knownEnemyPower)
    {
        if (mainMetrics.Count == 0 || mainMetrics.FrontlineCount == 0 || mainMetrics.Power < _context.DifficultyDefinition.PushMinPower)
        {
            return false;
        }

        if (knownEnemyPower <= 0.25f)
        {
            return true;
        }

        return mainMetrics.Power >= knownEnemyPower * _context.DifficultyDefinition.AttackAdvantageRatio;
    }

    private bool ShouldRetreat(AiSquadMetrics mainMetrics, float knownEnemyPower)
    {
        if (mainMetrics.Count == 0)
        {
            return false;
        }

        if (mainMetrics.BacklineCount > 0 && mainMetrics.FrontlineCount == 0)
        {
            return true;
        }

        return knownEnemyPower > 0.25f && mainMetrics.Power <= knownEnemyPower * _context.DifficultyDefinition.RetreatRatio;
    }

    private bool ShouldFinish(AiSquadMetrics mainMetrics, float knownEnemyPower)
    {
        var hasKnownTownHall = false;
        var knownBuildingCount = 0;
        foreach (var building in _context.KnownBuildings)
        {
            knownBuildingCount++;
            if (building.Kind == BuildingKind.TownHall && _context.IsFreshEnemyMemory(building.LastSeenMs))
            {
                hasKnownTownHall = true;
            }
        }

        if (!hasKnownTownHall && _context.LastKnownPlayerBase.HasValue && knownBuildingCount <= 2 && mainMetrics.Power >= knownEnemyPower + 4f)
        {
            return true;
        }

        return mainMetrics.Power >= Mathf.Max(_context.DifficultyDefinition.PushMinPower + 2f, knownEnemyPower * 1.75f) && _context.LastKnownPlayerBase.HasValue;
    }

    private bool CanLaunchHarass(AiSquadMetrics mainMetrics, AiSquadMetrics harassMetrics)
    {
        if (_context.Init.AiProfile != AiProfile.Harass || harassMetrics.Count == 0 || !_context.LastKnownPlayerBase.HasValue)
        {
            return false;
        }

        if (_context.ElapsedMs - _context.LastHarassCommandMs < 6500d)
        {
            return false;
        }

        return harassMetrics.Power >= _context.DifficultyDefinition.HarassMinPower && mainMetrics.Count >= 3;
    }

    private static float CalculateUnitPower(SimUnit unit)
    {
        return unit.Score * (unit.Hp / (float)unit.MaxHp);
    }

    private static int GetDesiredHarassSquadSize(int armyCount)
    {
        return armyCount switch
        {
            < 5 => 0,
            <= 7 => 2,
            <= 10 => 3,
            <= 14 => 4,
            _ => 5
        };
    }

    private static List<SimUnit> BuildHarassCandidates(List<SimUnit> army)
    {
        var knights = new List<SimUnit>();
        var archers = new List<SimUnit>();
        var footmen = new List<SimUnit>();
        foreach (var unit in army)
        {
            if (unit.Kind == UnitKind.Catapult)
            {
                continue;
            }

            switch (unit.Kind)
            {
                case UnitKind.Knight:
                    knights.Add(unit);
                    break;
                case UnitKind.Archer:
                    archers.Add(unit);
                    break;
                case UnitKind.Footman:
                    footmen.Add(unit);
                    break;
            }
        }

        var result = new List<SimUnit>(knights.Count + archers.Count + footmen.Count);
        result.AddRange(knights);
        result.AddRange(archers);
        result.AddRange(footmen);
        return result;
    }
}
