using System;
using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Resources;
using RtsNaGodote.Core.Simulation.Units;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Core.Simulation;

internal sealed class HarassMissionService
{
    private const float HarassRecoverDurationMs = 3200f;
    private const float HarassNoTradeWindowMs = 2500f;
    private const float HarassRaidActivationDistance = GameConstants.TileSize * 2.5f;
    private const float HarassThreatRadius = GameConstants.TileSize * 5.5f;
    private const float HarassRepeatPenaltyDistance = GameConstants.TileSize * 3.5f;

    private readonly HarassMissionContext _context;
    private readonly HarassMissionState _mission = new();

    public HarassMissionService(HarassMissionContext context)
    {
        _context = context;
    }

    public void Reset(bool preserveHistory)
    {
        var lastKind = _mission.LastTargetKind;
        var lastPosition = _mission.LastTargetPosition;
        var lastFailed = _mission.LastRaidFailed;
        _mission.Reset();
        if (preserveHistory)
        {
            _mission.LastTargetKind = lastKind;
            _mission.LastTargetPosition = lastPosition;
            _mission.LastRaidFailed = lastFailed;
        }
    }

    public void SyncMembers(List<SimUnit> harassSquad, List<SimUnit> aiUnits)
    {
        if (!_mission.Active)
        {
            return;
        }

        var aliveIds = new HashSet<int>(harassSquad.ConvertAll(unit => unit.Id));
        var removed = new List<int>();
        foreach (var pair in _mission.MemberScores)
        {
            if (!aliveIds.Contains(pair.Key))
            {
                _mission.LossValue += pair.Value;
                removed.Add(pair.Key);
            }
        }

        foreach (var id in removed)
        {
            _mission.MemberScores.Remove(id);
        }

        foreach (var unit in aiUnits)
        {
            if (aliveIds.Contains(unit.Id))
            {
                _mission.MemberScores[unit.Id] = unit.Score;
            }
        }
    }

    public void RegisterTrade(SimUnit source, ICombatTarget target, int amount)
    {
        if (!_mission.Active || !_mission.MemberScores.ContainsKey(source.Id) || target.Side != GameSide.Player)
        {
            return;
        }

        switch (target)
        {
            case SimUnit victim:
                _mission.RaidValue += (amount / (float)victim.MaxHp) * victim.Score;
                _mission.LastPositiveTradeMs = _context.ElapsedMs;
                break;

            case SimBuilding building:
                _mission.LastPositiveTradeMs = _context.ElapsedMs;
                var progressFactor = building.Kind == BuildingKind.Tower ? 3.5f : 2f;
                _mission.RaidValue += (amount / (float)building.MaxHp) * progressFactor;
                break;
        }

        if (target.Alive)
        {
            return;
        }

        switch (target)
        {
            case SimUnit { Kind: UnitKind.Worker }:
                _mission.WorkersKilled++;
                _mission.RaidValue += 1f;
                _mission.LastPositiveTradeMs = _context.ElapsedMs;
                break;

            case SimBuilding building when building.Kind == BuildingKind.Tower:
                _mission.OuterBuildingsDestroyed++;
                _mission.RaidValue += 4f;
                _mission.LastPositiveTradeMs = _context.ElapsedMs;
                break;

            case SimBuilding building when building.Kind != BuildingKind.TownHall:
                _mission.OuterBuildingsDestroyed++;
                _mission.RaidValue += 3f;
                _mission.LastPositiveTradeMs = _context.ElapsedMs;
                break;
        }
    }

    public void Command(List<SimUnit> squad, AiSquadMetrics metrics, SimBuilding hall, Vector2 stagePoint, Vector2 suspectedBase)
    {
        if (squad.Count == 0)
        {
            Reset(preserveHistory: true);
            return;
        }

        if (!_mission.Active)
        {
            var opening = SelectObjective(squad, hall, suspectedBase);
            StartMission(opening, metrics.Power, stagePoint);
        }

        if (_mission.Phase == HarassMissionPhase.Recover && _context.ElapsedMs >= _mission.RecoverUntilMs)
        {
            var nextObjective = SelectObjective(squad, hall, suspectedBase);
            SetMissionTarget(nextObjective, HarassMissionPhase.Approach);
        }

        if (_mission.Phase is HarassMissionPhase.Approach or HarassMissionPhase.Raid)
        {
            RefreshMissionTarget(squad, hall, suspectedBase);
        }

        var squadCenter = metrics.Center == Vector2.Zero ? squad[0].Position : metrics.Center;
        var objectivePosition = _mission.CurrentTargetPosition;
        if (_mission.Phase == HarassMissionPhase.Approach &&
            (squadCenter.DistanceTo(objectivePosition) <= HarassRaidActivationDistance || HasVisibleOpportunity(squad)))
        {
            SetPhase(HarassMissionPhase.Raid);
        }

        if (_mission.Phase == HarassMissionPhase.Raid)
        {
            if (ShouldDisengage(squad, metrics, hall, suspectedBase))
            {
                BeginDisengage(stagePoint);
            }
            else if (IsCurrentTargetExhausted(squad, objectivePosition))
            {
                var nextObjective = SelectObjective(squad, hall, suspectedBase);
                if (nextObjective.Kind == HarassTargetKind.ApproachPoint &&
                    nextObjective.Position.DistanceTo(objectivePosition) <= GameConstants.TileSize * 2f)
                {
                    BeginDisengage(stagePoint);
                }
                else
                {
                    SetMissionTarget(nextObjective, HarassMissionPhase.Approach);
                }
            }
        }

        if (_mission.Phase == HarassMissionPhase.Disengage &&
            squadCenter.DistanceTo(_mission.RecoverPoint) <= GameConstants.TileSize * 2.2f)
        {
            SetPhase(HarassMissionPhase.Recover);
            _mission.RecoverUntilMs = _context.ElapsedMs + HarassRecoverDurationMs;
        }

        switch (_mission.Phase)
        {
            case HarassMissionPhase.Approach:
                CommandFormation(squad, _mission.CurrentTargetPosition, suspectedBase, 18f, 34f);
                break;

            case HarassMissionPhase.Raid:
                CommandFormation(squad, _mission.CurrentTargetPosition, suspectedBase, 8f, 24f);
                break;

            case HarassMissionPhase.Disengage:
            case HarassMissionPhase.Recover:
                CommandRetreat(squad, _mission.RecoverPoint, hall.Center);
                break;
        }
    }

    public void ApplyMicro(List<SimUnit> squad, AiSquadMetrics metrics, Vector2 fallback)
    {
        if (squad.Count == 0)
        {
            return;
        }

        foreach (var unit in squad)
        {
            ICombatTarget? target = _mission.Phase is HarassMissionPhase.Disengage or HarassMissionPhase.Recover
                ? FindRetreatThreat(unit)
                : FindPreferredEnemy(unit);
            if (target is not null && (unit.State != UnitState.Attack || unit.TargetCombat != target))
            {
                _context.IssueAttack(unit, target);
            }

            if (target is null)
            {
                continue;
            }

            var enemyDistance = unit.Position.DistanceTo(target.Position);
            var frontlineNearby = CountAiFrontlineNear(unit.Position, squad, 64f);
            if (unit.Kind == UnitKind.Archer && enemyDistance < 74f && frontlineNearby == 0)
            {
                var retreatAnchor = _mission.Phase is HarassMissionPhase.Disengage or HarassMissionPhase.Recover
                    ? _mission.RecoverPoint
                    : (metrics.Center == Vector2.Zero ? fallback : metrics.Center);
                var retreat = retreatAnchor - (target.Position - retreatAnchor).Normalized() * 42f;
                _context.CommandUnitMove(unit, retreat);
            }
        }
    }

    private void StartMission(HarassObjective objective, float startPower, Vector2 recoverPoint)
    {
        _mission.Reset();
        _mission.Active = true;
        _mission.StartPower = float.Max(startPower, 0.01f);
        _mission.RecoverPoint = recoverPoint;
        _mission.LastPositiveTradeMs = _context.ElapsedMs;
        SetMissionTarget(objective, HarassMissionPhase.Approach);
    }

    private void SetMissionTarget(HarassObjective objective, HarassMissionPhase phase)
    {
        _mission.CurrentTargetKind = objective.Kind;
        _mission.CurrentTargetPosition = objective.Position;
        _mission.CurrentTargetEntityId = objective.EntityId;
        _mission.CurrentTargetScore = objective.Score;
        _mission.LastTargetKind = objective.Kind;
        _mission.LastTargetPosition = objective.Position;
        SetPhase(phase);
    }

    private void SetPhase(HarassMissionPhase phase)
    {
        _mission.Phase = phase;
        _mission.PhaseEnteredMs = _context.ElapsedMs;
    }

    private void BeginDisengage(Vector2 recoverPoint)
    {
        _mission.LastRaidFailed = !IsSuccessfulRaid();
        _mission.RecoverPoint = recoverPoint;
        SetPhase(HarassMissionPhase.Disengage);
    }

    private bool IsSuccessfulRaid()
    {
        return _mission.WorkersKilled >= 2 ||
               _mission.OuterBuildingsDestroyed > 0 ||
               _mission.RaidValue >= 3f;
    }

    private void RefreshMissionTarget(List<SimUnit> squad, SimBuilding hall, Vector2 suspectedBase)
    {
        var nextObjective = SelectObjective(squad, hall, suspectedBase);
        var currentStillUseful = IsCurrentObjectiveRelevant(squad);
        if (!currentStillUseful || nextObjective.Score + 28f < _mission.CurrentTargetScore)
        {
            SetMissionTarget(nextObjective, _mission.Phase == HarassMissionPhase.Raid ? HarassMissionPhase.Raid : HarassMissionPhase.Approach);
        }
    }

    private bool IsCurrentObjectiveRelevant(List<SimUnit> squad)
    {
        if (!_mission.Active)
        {
            return false;
        }

        switch (_mission.CurrentTargetKind)
        {
            case HarassTargetKind.WorkerLine:
                if (_mission.CurrentTargetEntityId.HasValue &&
                    TryGetPlayerUnit(_mission.CurrentTargetEntityId.Value, out var worker) &&
                    worker.Kind == UnitKind.Worker)
                {
                    return true;
                }

                return _context.AiKnowledge.CountKnownWorkersNear(_mission.CurrentTargetPosition, GameConstants.TileSize * 3f) > 0;

            case HarassTargetKind.OuterBuilding:
            case HarassTargetKind.FallbackBuilding:
                return _mission.CurrentTargetEntityId.HasValue &&
                       TryGetPlayerBuilding(_mission.CurrentTargetEntityId.Value, out _);

            case HarassTargetKind.GoldMine:
            case HarassTargetKind.ApproachPoint:
                return true;

            default:
                return HasVisibleOpportunity(squad);
        }
    }

    private bool ShouldDisengage(List<SimUnit> squad, AiSquadMetrics metrics, SimBuilding hall, Vector2 suspectedBase)
    {
        if (metrics.Count == 0)
        {
            return true;
        }

        var localEnemyPower = EstimateVisibleEnemyPowerAround(
            squad,
            metrics.Center == Vector2.Zero ? _mission.CurrentTargetPosition : metrics.Center,
            HarassThreatRadius);
        var currentPower = metrics.Power;
        var losing = currentPower < _mission.StartPower * 0.7f ||
                     localEnemyPower >= currentPower * 1.15f ||
                     (_mission.LossValue > 0f && _context.ElapsedMs - _mission.LastPositiveTradeMs > HarassNoTradeWindowMs);
        if (!losing)
        {
            return false;
        }

        if (IsSuccessfulRaid())
        {
            return true;
        }

        return currentPower < _mission.StartPower * 0.55f ||
               localEnemyPower >= currentPower * 1.35f ||
               metrics.Center.DistanceTo(hall.Center) < hall.Center.DistanceTo(suspectedBase) * 0.45f;
    }

    private bool HasVisibleOpportunity(List<SimUnit> squad)
    {
        foreach (var unit in _context.Units)
        {
            if (!unit.Alive || unit.Side != GameSide.Player)
            {
                continue;
            }

            if (!IsVisibleToSquad(squad, unit.Position, unit.Radius))
            {
                continue;
            }

            if (unit.Kind == UnitKind.Worker || unit.CanAttack())
            {
                return true;
            }
        }

        foreach (var building in _context.Buildings)
        {
            if (!building.Alive || building.Side != GameSide.Player || building.Kind == BuildingKind.TownHall)
            {
                continue;
            }

            if (IsVisibleToSquad(squad, building.Center, building.Radius))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsCurrentTargetExhausted(List<SimUnit> squad, Vector2 objectivePosition)
    {
        return !HasVisibleOpportunity(squad) &&
               _context.AiKnowledge.CountKnownWorkersNear(objectivePosition, GameConstants.TileSize * 3f) == 0 &&
               !_context.AiKnowledge.HasKnownOuterTargetNear(objectivePosition, GameConstants.TileSize * 4f);
    }

    private HarassObjective SelectObjective(List<SimUnit> squad, SimBuilding hall, Vector2 suspectedBase)
    {
        var squadCenter = _context.CalculateMetrics(squad).Center;
        if (squadCenter == Vector2.Zero)
        {
            squadCenter = squad[0].Position;
        }

        var basePosition = _context.AiKnowledge.LastKnownPlayerBase ?? suspectedBase;
        var baseTile = _context.AiKnowledge.LastKnownPlayerBaseTile ?? _context.Map.WorldToTile(basePosition);
        HarassObjective? best = null;

        void Consider(HarassTargetKind kind, Vector2 position, int? entityId, float score)
        {
            if (best is null || score < best.Value.Score)
            {
                best = new HarassObjective(kind, position, entityId, score);
            }
        }

        foreach (var unit in _context.Units)
        {
            if (!unit.Alive || unit.Side != GameSide.Player || unit.Kind != UnitKind.Worker || !IsVisibleToSquad(squad, unit.Position, unit.Radius))
            {
                continue;
            }

            var cluster = CountVisibleWorkersNear(unit.Position, GameConstants.TileSize * 2.4f);
            var score = -220f - cluster * 32f + squadCenter.DistanceTo(unit.Position) * 0.42f + _context.AiKnowledge.EstimateKnownThreatAt(unit.Position, HarassThreatRadius) * 18f;
            Consider(HarassTargetKind.WorkerLine, unit.Position, unit.Id, ApplyRepeatPenalty(HarassTargetKind.WorkerLine, unit.Position, score));
        }

        foreach (var remembered in _context.AiKnowledge.KnownUnits)
        {
            if (!_context.AiKnowledge.IsFreshEnemyMemory(remembered.LastSeenMs) || remembered.Kind != UnitKind.Worker)
            {
                continue;
            }

            var mineDistance = DistanceToNearestPlayerMine(remembered.Position);
            if (mineDistance > GameConstants.TileSize * 7f)
            {
                continue;
            }

            var workerSupport = _context.AiKnowledge.CountKnownWorkersNear(remembered.Position, GameConstants.TileSize * 2.8f);
            var score = -150f - workerSupport * 24f + squadCenter.DistanceTo(remembered.Position) * 0.5f + _context.AiKnowledge.EstimateKnownThreatAt(remembered.Position, HarassThreatRadius) * 20f;
            Consider(HarassTargetKind.WorkerLine, remembered.Position, remembered.Id, ApplyRepeatPenalty(HarassTargetKind.WorkerLine, remembered.Position, score));
        }

        foreach (var resource in _context.Resources)
        {
            if (!resource.Alive || basePosition.DistanceTo(resource.Center) > GameConstants.TileSize * 10f)
            {
                continue;
            }

            if (!TryFindWalkableRaidPoint(_context.Map.WorldToTile(resource.Center), 2, 5, squadCenter, out var raidPoint))
            {
                continue;
            }

            var workerPressure = _context.AiKnowledge.CountKnownWorkersNear(resource.Center, GameConstants.TileSize * 3.2f);
            var score = (resource.Type == ResourceType.Gold ? -92f : -46f) - workerPressure * 18f + squadCenter.DistanceTo(raidPoint) * 0.48f + _context.AiKnowledge.EstimateKnownThreatAt(raidPoint, HarassThreatRadius) * 20f;
            Consider(HarassTargetKind.GoldMine, raidPoint, resource.Id, ApplyRepeatPenalty(HarassTargetKind.GoldMine, raidPoint, score));
        }

        foreach (var building in _context.AiKnowledge.KnownBuildings)
        {
            if (!_context.AiKnowledge.IsFreshEnemyMemory(building.LastSeenMs) || building.Kind == BuildingKind.TownHall)
            {
                continue;
            }

            if (!TryFindWalkableRaidPoint(building.CenterTile, 3, 6, squadCenter, out var raidPoint))
            {
                continue;
            }

            var score = squadCenter.DistanceTo(raidPoint) * 0.46f + _context.AiKnowledge.EstimateKnownThreatAt(raidPoint, HarassThreatRadius) * 21f;
            score -= building.Kind switch
            {
                BuildingKind.Tower => 88f,
                BuildingKind.Workshop => 54f,
                BuildingKind.Barracks => 48f,
                BuildingKind.Farm => 28f,
                _ => 18f
            };
            Consider(HarassTargetKind.OuterBuilding, raidPoint, building.Id, ApplyRepeatPenalty(HarassTargetKind.OuterBuilding, raidPoint, score));
        }

        foreach (var point in GenerateApproachPoints(baseTile, squadCenter))
        {
            var score = 54f + squadCenter.DistanceTo(point) * 0.38f + _context.AiKnowledge.EstimateKnownThreatAt(point, HarassThreatRadius) * 16f + point.DistanceTo(basePosition) * 0.12f;
            Consider(HarassTargetKind.ApproachPoint, point, null, ApplyRepeatPenalty(HarassTargetKind.ApproachPoint, point, score));
        }

        foreach (var building in _context.AiKnowledge.KnownBuildings)
        {
            if (!_context.AiKnowledge.IsFreshEnemyMemory(building.LastSeenMs) || building.Kind != BuildingKind.TownHall)
            {
                continue;
            }

            if (!TryFindWalkableRaidPoint(building.CenterTile, 3, 7, squadCenter, out var raidPoint))
            {
                continue;
            }

            var score = 220f + squadCenter.DistanceTo(raidPoint) * 0.42f + _context.AiKnowledge.EstimateKnownThreatAt(raidPoint, HarassThreatRadius) * 24f;
            Consider(HarassTargetKind.FallbackBuilding, raidPoint, building.Id, ApplyRepeatPenalty(HarassTargetKind.FallbackBuilding, raidPoint, score));
        }

        if (best.HasValue)
        {
            return best.Value;
        }

        return new HarassObjective(HarassTargetKind.ApproachPoint, _context.FindAssaultApproachPoint(basePosition, hall.Center), null, 999f);
    }

    private float ApplyRepeatPenalty(HarassTargetKind kind, Vector2 position, float score)
    {
        if (_mission.LastTargetPosition.HasValue &&
            _mission.LastRaidFailed &&
            _mission.LastTargetPosition.Value.DistanceTo(position) <= HarassRepeatPenaltyDistance)
        {
            score += 140f;
        }

        if (_mission.LastTargetKind.HasValue &&
            _mission.LastRaidFailed &&
            _mission.LastTargetKind.Value == kind)
        {
            score += 36f;
        }

        return score;
    }

    private int CountVisibleWorkersNear(Vector2 position, float radius)
    {
        var count = 0;
        foreach (var unit in _context.Units)
        {
            if (unit.Alive && unit.Side == GameSide.Player && unit.Kind == UnitKind.Worker && unit.Position.DistanceTo(position) <= radius)
            {
                count++;
            }
        }

        return count;
    }

    private float DistanceToNearestPlayerMine(Vector2 position)
    {
        var best = float.PositiveInfinity;
        foreach (var resource in _context.Resources)
        {
            if (!resource.Alive || resource.Type != ResourceType.Gold)
            {
                continue;
            }

            var distance = resource.Center.DistanceTo(position);
            if (distance < best)
            {
                best = distance;
            }
        }

        return best;
    }

    private float EstimateVisibleEnemyPowerAround(List<SimUnit> squad, Vector2 position, float radius)
    {
        var power = 0f;
        foreach (var unit in _context.Units)
        {
            if (!unit.Alive || unit.Side != GameSide.Player || !IsVisibleToSquad(squad, unit.Position, unit.Radius))
            {
                continue;
            }

            if (unit.Position.DistanceTo(position) <= radius)
            {
                power += unit.Score * (unit.Hp / (float)unit.MaxHp);
            }
        }

        foreach (var building in _context.Buildings)
        {
            if (!building.Alive || building.Side != GameSide.Player || !IsVisibleToSquad(squad, building.Center, building.Radius))
            {
                continue;
            }

            if (building.Kind == BuildingKind.Tower && building.Center.DistanceTo(position) <= radius + GameConstants.TileSize * 2f)
            {
                power += 2.8f;
            }
        }

        return power;
    }

    private bool IsVisibleToSquad(List<SimUnit> squad, Vector2 position, float padding)
    {
        foreach (var unit in squad)
        {
            if (!unit.Alive)
            {
                continue;
            }

            if (unit.Position.DistanceTo(position) <= unit.Sight * GameConstants.TileSize + padding)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryFindWalkableRaidPoint(Vector2I centerTile, int minRadius, int maxRadius, Vector2 reference, out Vector2 point)
    {
        point = Vector2.Zero;
        var bestScore = float.PositiveInfinity;
        var found = false;
        for (var radius = minRadius; radius <= maxRadius; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    var tx = centerTile.X + dx;
                    var ty = centerTile.Y + dy;
                    if (!_context.Map.IsWalkable(tx, ty))
                    {
                        continue;
                    }

                    var world = _context.Map.TileToWorldCenter(tx, ty);
                    var score = world.DistanceTo(reference) + _context.AiKnowledge.EstimateKnownThreatAt(world, GameConstants.TileSize * 4f) * 18f;
                    if (_mission.LastTargetPosition.HasValue &&
                        _mission.LastRaidFailed &&
                        _mission.LastTargetPosition.Value.DistanceTo(world) <= HarassRepeatPenaltyDistance)
                    {
                        score += 120f;
                    }

                    if (score < bestScore)
                    {
                        bestScore = score;
                        point = world;
                        found = true;
                    }
                }
            }
        }

        return found;
    }

    private List<Vector2> GenerateApproachPoints(Vector2I baseTile, Vector2 reference)
    {
        var result = new List<Vector2>();
        var offsets = new[]
        {
            new Vector2I(0, 6),
            new Vector2I(6, 0),
            new Vector2I(4, 4),
            new Vector2I(-4, 4),
            new Vector2I(0, -6),
            new Vector2I(6, -3),
            new Vector2I(-6, 3)
        };

        foreach (var offset in offsets)
        {
            var candidateTile = baseTile + offset;
            if (TryFindWalkableRaidPoint(candidateTile, 0, 2, reference, out var point))
            {
                result.Add(point);
            }
        }

        if (result.Count == 0 && TryFindWalkableRaidPoint(baseTile, 5, 8, reference, out var fallback))
        {
            result.Add(fallback);
        }

        return result;
    }

    private bool TryGetPlayerUnit(int id, out SimUnit unit)
    {
        unit = null!;
        foreach (var candidate in _context.Units)
        {
            if (candidate.Alive && candidate.Side == GameSide.Player && candidate.Id == id)
            {
                unit = candidate;
                return true;
            }
        }

        return false;
    }

    private bool TryGetPlayerBuilding(int id, out SimBuilding building)
    {
        building = null!;
        foreach (var candidate in _context.Buildings)
        {
            if (candidate.Alive && candidate.Side == GameSide.Player && candidate.Id == id)
            {
                building = candidate;
                return true;
            }
        }

        return false;
    }

    private void CommandFormation(List<SimUnit> squad, Vector2 anchor, Vector2 lookTarget, float frontlineOffset, float backlineOffset)
    {
        var facing = lookTarget - anchor;
        if (facing.LengthSquared() <= 0.01f)
        {
            facing = Vector2.Right;
        }

        facing = facing.Normalized();
        var side = new Vector2(-facing.Y, facing.X);
        var frontline = new List<SimUnit>();
        var backline = new List<SimUnit>();
        foreach (var unit in squad)
        {
            if (unit.IsRanged())
            {
                backline.Add(unit);
            }
            else
            {
                frontline.Add(unit);
            }
        }

        CommandFormationRow(frontline, anchor + facing * frontlineOffset, side);
        CommandFormationRow(backline, anchor - facing * backlineOffset, side);
    }

    private void CommandRetreat(List<SimUnit> squad, Vector2 recoverPoint, Vector2 lookTarget)
    {
        CommandFormation(squad, recoverPoint, lookTarget, 10f, 20f);
    }

    private void CommandFormationRow(List<SimUnit> units, Vector2 rowAnchor, Vector2 side)
    {
        if (units.Count == 0)
        {
            return;
        }

        var spacing = GameConstants.GroupSpacing;
        var start = -(units.Count - 1) * 0.5f;
        for (var index = 0; index < units.Count; index++)
        {
            var destination = rowAnchor + side * (start + index) * spacing;
            _context.CommandUnitMove(units[index], destination);
        }
    }

    private ICombatTarget? FindPreferredEnemy(SimUnit unit)
    {
        var sensorRange = unit.Sight * GameConstants.TileSize;
        var visibleWorkers = false;
        var visibleCombat = false;
        var visibleOuterBuildings = false;
        foreach (var other in _context.Units)
        {
            if (!other.Alive || other.Side == unit.Side)
            {
                continue;
            }

            var distance = unit.Position.DistanceTo(other.Position);
            if (distance > sensorRange)
            {
                continue;
            }

            if (other.Kind == UnitKind.Worker)
            {
                visibleWorkers = true;
            }
            else if (other.CanAttack())
            {
                visibleCombat = true;
            }
        }

        foreach (var building in _context.Buildings)
        {
            if (!building.Alive || building.Side == unit.Side)
            {
                continue;
            }

            var distance = unit.Position.DistanceTo(building.Center);
            if (distance > sensorRange + building.Radius)
            {
                continue;
            }

            if (building.Kind != BuildingKind.TownHall)
            {
                visibleOuterBuildings = true;
            }
        }

        SimUnit? bestUnit = null;
        var bestUnitScore = float.PositiveInfinity;
        foreach (var other in _context.Units)
        {
            if (!other.Alive || other.Side == unit.Side)
            {
                continue;
            }

            var distance = unit.Position.DistanceTo(other.Position);
            if (distance > sensorRange)
            {
                continue;
            }

            var score = distance;
            if (other.Kind == UnitKind.Worker)
            {
                score -= 165f;
            }
            else if (other.CanAttack())
            {
                score -= distance <= unit.Range + other.Radius + unit.Radius + 26f ? 120f : 72f;
            }

            if (score < bestUnitScore)
            {
                bestUnitScore = score;
                bestUnit = other;
            }
        }

        if (bestUnit is not null)
        {
            return bestUnit;
        }

        SimBuilding? bestBuilding = null;
        var bestBuildingScore = float.PositiveInfinity;
        foreach (var building in _context.Buildings)
        {
            if (!building.Alive || building.Side == unit.Side)
            {
                continue;
            }

            var distance = unit.Position.DistanceTo(building.Center);
            if (distance > sensorRange + building.Radius)
            {
                continue;
            }

            var score = distance;
            if (building.Kind == BuildingKind.TownHall && (visibleWorkers || visibleCombat || visibleOuterBuildings))
            {
                score += 240f;
            }
            else
            {
                score -= building.Kind switch
                {
                    BuildingKind.Tower => 96f,
                    BuildingKind.Workshop => 62f,
                    BuildingKind.Barracks => 56f,
                    BuildingKind.Farm => 34f,
                    BuildingKind.TownHall => -12f,
                    _ => 18f
                };
            }

            if (score < bestBuildingScore)
            {
                bestBuildingScore = score;
                bestBuilding = building;
            }
        }

        return bestBuilding;
    }

    private ICombatTarget? FindRetreatThreat(SimUnit unit)
    {
        SimUnit? bestThreat = null;
        var bestScore = float.PositiveInfinity;
        var sensorRange = unit.Sight * GameConstants.TileSize;
        foreach (var other in _context.Units)
        {
            if (!other.Alive || other.Side == unit.Side || !other.CanAttack())
            {
                continue;
            }

            var distance = unit.Position.DistanceTo(other.Position);
            if (distance > sensorRange)
            {
                continue;
            }

            var score = distance;
            if (other.TargetCombat == unit)
            {
                score -= 48f;
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestThreat = other;
            }
        }

        return bestThreat;
    }

    private int CountAiFrontlineNear(Vector2 position, List<SimUnit> squad, float radius)
    {
        var count = 0;
        foreach (var ally in squad)
        {
            if (!ally.Alive || ally.IsRanged() || ally.Kind == UnitKind.Catapult)
            {
                continue;
            }

            if (ally.Position.DistanceTo(position) <= radius)
            {
                count++;
            }
        }

        return count;
    }
}
