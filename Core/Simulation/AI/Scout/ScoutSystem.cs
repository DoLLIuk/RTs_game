using System;
using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Pathfinding;
using RtsNaGodote.Core.Simulation.Units;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Core.Simulation;

internal sealed class ScoutSystem
{
    private const float ScoutContinueAfterConfirmMs = 5200f;
    private const float ScoutDangerBuffer = GameConstants.TileSize * 1.75f;
    private const float ScoutSectorThreatRadius = GameConstants.TileSize * 4.5f;
    private const float ScoutPeekCompletionDistance = GameConstants.TileSize * 0.38f;
    private const float ScoutEntryArrivalDistance = GameConstants.TileSize * 0.95f;
    private const float ScoutMinVisibleCommitMs = 260f;
    private const float ScoutRecallAssemblyArrivalDistance = 24f;
    private const int ScoutMaxVisibleEntryTiles = 1;
    private const int ScoutMinPeekVisibleTiles = 2;
    private const int FrontierScoutPreferredPeekDepthTiles = 2;
    private const int FrontierScoutMaxPeekDepthTiles = 3;
    private static readonly bool ScoutDebugLogging = false;

    private readonly ScoutContext _context;
    private readonly ScoutMissionState _mission = new();
    private int? _workerScoutId;
    private int? _recallingScoutId;
    private Vector2 _assemblyPoint;

    public ScoutSystem(ScoutContext context)
    {
        _context = context;
    }

    public bool HasRecallingScout => _recallingScoutId.HasValue;

    public void ClearWorkerScoutReservation()
    {
        _workerScoutId = null;
    }

    public void ResetMission()
    {
        ReleaseScoutLock();
        _mission.Reset();
        _workerScoutId = null;
    }

    public void BeginRecallToAssembly(Vector2 assemblyPoint)
    {
        _assemblyPoint = assemblyPoint;

        if (_mission.ScoutUnitId is not int scoutId)
        {
            return;
        }

        if (_recallingScoutId == scoutId)
        {
            return;
        }

        var scout = _context.Units.Find(unit => unit.Alive && unit.Side == GameSide.AI && unit.Id == scoutId);
        if (scout is null)
        {
            ClearRecallState();
            ClearMissionOwnership();
            return;
        }

        _recallingScoutId = scoutId;
        scout.IsNonCombatScout = true;
        scout.TargetCombat = null;
        ClearMissionOwnership();
    }

    public void UpdateRecall(Vector2 assemblyPoint)
    {
        _assemblyPoint = assemblyPoint;
        if (_recallingScoutId is not int scoutId)
        {
            return;
        }

        var scout = _context.Units.Find(unit => unit.Alive && unit.Side == GameSide.AI && unit.Id == scoutId);
        if (scout is null)
        {
            ClearRecallState();
            return;
        }

        scout.IsNonCombatScout = true;
        scout.TargetCombat = null;
        if (scout.Position.DistanceTo(_assemblyPoint) <= ScoutRecallAssemblyArrivalDistance)
        {
            scout.IsNonCombatScout = false;
            ClearRecallState();
            return;
        }

        _context.CommandMove(scout, _assemblyPoint);
    }

    public bool IsScoutReserved(int unitId)
    {
        return _mission.ScoutUnitId == unitId || _recallingScoutId == unitId;
    }

    public bool ShouldContinueMission(bool baseConfirmed)
    {
        if (!_mission.Active)
        {
            return false;
        }

        if (!baseConfirmed)
        {
            return true;
        }

        if (_context.ElapsedMs - _mission.ConfirmedBaseMs <= ScoutContinueAfterConfirmMs)
        {
            return true;
        }

        if (_mission.ScoutUnitId is not int scoutId)
        {
            return false;
        }

        var scout = _context.Units.Find(unit => unit.Alive && unit.Side == GameSide.AI && unit.Id == scoutId);
        if (scout is null)
        {
            return false;
        }

        if (_mission.Phase is ScoutMissionPhase.BreakContact or ScoutMissionPhase.Reposition)
        {
            return true;
        }

        if (_context.ElapsedMs - _mission.LastThreatMs < _context.DifficultyDefinition.ScoutReentryDelayMs)
        {
            return true;
        }

        var safeDistance = scout.Sight * GameConstants.TileSize;
        if (scout.Position.DistanceTo(_mission.BasePosition) <= safeDistance)
        {
            return true;
        }

        return EstimateScoutSectorThreat(scout.Position) > _context.DifficultyDefinition.ScoutThreatTolerance * 0.45f;
    }

    public SimUnit? SelectScoutUnit(List<SimUnit> mainArmy, bool workersFallback, Vector2 suspectedBase, Vector2 fallback)
    {
        if (HasRecallingScout)
        {
            return null;
        }

        SimUnit? scout = null;
        if (_mission.ScoutUnitId.HasValue)
        {
            scout = _context.Units.Find(unit => unit.Alive && unit.Side == GameSide.AI && unit.Id == _mission.ScoutUnitId.Value);
            if (scout is not null)
            {
                return scout;
            }

            ResetMission();
        }

        var bestScore = float.NegativeInfinity;
        foreach (var unit in mainArmy)
        {
            var score = EvaluateScoutCandidate(unit);
            if (score > bestScore)
            {
                bestScore = score;
                scout = unit;
            }
        }

        if (scout is not null)
        {
            _workerScoutId = null;
            return scout;
        }

        if (!workersFallback)
        {
            return null;
        }

        if (_workerScoutId.HasValue)
        {
            scout = _context.Units.Find(unit => unit.Alive && unit.Side == GameSide.AI && unit.Id == _workerScoutId.Value);
            if (scout is not null &&
                scout.IsWorker() &&
                scout.State is UnitState.Idle or UnitState.Gather &&
                CanWorkerScoutSafely(scout, suspectedBase, fallback))
            {
                return scout;
            }

            _workerScoutId = null;
        }

        foreach (var worker in _context.Units)
        {
            if (!worker.Alive || worker.Side != GameSide.AI || !worker.IsWorker())
            {
                continue;
            }

            if (worker.State is not (UnitState.Idle or UnitState.Gather) ||
                !CanWorkerScoutSafely(worker, suspectedBase, fallback))
            {
                continue;
            }

            _workerScoutId = worker.Id;
            return worker;
        }

        return null;
    }

    public void EnsureMission(SimUnit scout, Vector2 suspectedBase, Vector2 fallback)
    {
        if (_mission.Active && _mission.ScoutUnitId == scout.Id)
        {
            scout.IsNonCombatScout = true;
            scout.TargetCombat = null;
            return;
        }

        ReleaseScoutLock();
        _mission.Reset();
        _mission.Active = true;
        _mission.ScoutUnitId = scout.Id;
        _mission.WorkerFallback = scout.IsWorker();
        _mission.BaseTile = _context.LastKnownPlayerBaseTile ?? _context.Map.WorldToTile(suspectedBase);
        _mission.BasePosition = _context.LastKnownPlayerBase ?? suspectedBase;
        _mission.RecoverPoint = fallback;
        _mission.Phase = ScoutMissionPhase.ApproachEdge;
        _mission.PhaseEnteredMs = _context.ElapsedMs;
        _mission.LastThreatMs = -99999d;
        _mission.ConfirmedBaseMs = _context.LastKnownPlayerBase.HasValue ? _context.ElapsedMs : -99999d;
        _mission.ExposureStartedMs = -99999d;
        _mission.CurrentSector = -1;
        _mission.LastSector = -1;
        _mission.MandatorySectorSwitchFrom = -1;
        _mission.HasCommittedReentryPlan = false;
        scout.IsNonCombatScout = true;
        scout.TargetCombat = null;
        TryPlanScoutSector(scout, _mission.BasePosition, _mission.BaseTile!.Value, fallback, allowRepeat: _context.Difficulty == Difficulty.Easy, out _);
    }

    public Vector2 UpdateMission(SimUnit scout, Vector2 suspectedBase, Vector2 fallback)
    {
        scout.IsNonCombatScout = true;
        scout.TargetCombat = null;

        var basePosition = _context.LastKnownPlayerBase ?? suspectedBase;
        var baseTile = _context.LastKnownPlayerBaseTile ?? _context.Map.WorldToTile(basePosition);
        _mission.BasePosition = basePosition;
        _mission.BaseTile = baseTile;
        _mission.RecoverPoint = fallback;
        if (_context.LastKnownPlayerBase.HasValue && _mission.ConfirmedBaseMs < 0d)
        {
            _mission.ConfirmedBaseMs = _context.ElapsedMs;
        }

        var threat = FindScoutThreat(scout);
        if (threat is not null)
        {
            _mission.LastThreatMs = _context.ElapsedMs;
            _mission.LastThreatPosition = threat.Position;
        }

        if (_mission.Phase is ScoutMissionPhase.Peek or ScoutMissionPhase.ReEnter &&
            _mission.ExposureStartedMs < 0d &&
            IsScoutCurrentlyVisibleToPlayer(scout))
        {
            _mission.ExposureStartedMs = _context.ElapsedMs;
        }

        if (_mission.Phase is ScoutMissionPhase.Peek or ScoutMissionPhase.ReEnter &&
            !IsScoutPeekPointCurrentlyVisible())
        {
            if (!TryRefreshDynamicScoutPeekPoint(scout, basePosition))
            {
                _mission.Phase = ScoutMissionPhase.Reposition;
                _mission.PhaseEnteredMs = _context.ElapsedMs;
                _mission.ExposureStartedMs = -99999d;
                _mission.HasCommittedReentryPlan = false;
                TraceScoutRetarget("stale-peek-reposition", scout, _mission.PeekPoint);
                return _mission.FallbackExitPoint == Vector2.Zero ? fallback : _mission.FallbackExitPoint;
            }
        }

        if (!HasActiveScoutPlan() &&
            !TryPlanScoutSector(scout, basePosition, baseTile, fallback, allowRepeat: true, out _))
        {
            return fallback;
        }

        if (_mission.Phase == ScoutMissionPhase.ApproachEdge &&
            TryStartBreakContactForImmediateThreat(scout, threat, fallback, "approach-threat", out var emergencyRetreat))
        {
            return emergencyRetreat;
        }

        switch (_mission.Phase)
        {
            case ScoutMissionPhase.ApproachEdge:
                if (ScoutReachedPoint(scout, _mission.EntryPoint, ScoutEntryArrivalDistance))
                {
                    StartScoutPeek(ScoutMissionPhase.Peek);
                    return _mission.PeekPoint;
                }

                return _mission.EntryPoint;

            case ScoutMissionPhase.Peek:
            case ScoutMissionPhase.ReEnter:
                var reachedPeekPoint = ScoutReachedPoint(scout, _mission.PeekPoint, ScoutPeekCompletionDistance);
                var reachedVisiblePeekPoint = reachedPeekPoint && IsScoutCurrentlyVisibleToPlayer(scout);
                if (ShouldBreakScoutContact(scout, threat) || reachedVisiblePeekPoint)
                {
                    StartScoutBreakContact();
                    return GetScoutBreakContactTarget(scout, fallback);
                }

                return _mission.PeekPoint;

            case ScoutMissionPhase.BreakContact:
                if (ScoutReachedPoint(scout, _mission.PlannedExitPoint, ScoutEntryArrivalDistance) ||
                    ScoutReachedPoint(scout, _mission.FallbackExitPoint, GameConstants.TileSize * 1.5f) ||
                    !IsPlayerVisibleTile(_context.Map.WorldToTile(scout.Position).X, _context.Map.WorldToTile(scout.Position).Y))
                {
                    _mission.Phase = ScoutMissionPhase.Reposition;
                    _mission.PhaseEnteredMs = _context.ElapsedMs;
                    return _mission.FallbackExitPoint == Vector2.Zero ? fallback : _mission.FallbackExitPoint;
                }

                return GetScoutBreakContactTarget(scout, fallback);

            case ScoutMissionPhase.Reposition:
                if (CanScoutReEnter(scout, threat))
                {
                    if (!_mission.HasCommittedReentryPlan &&
                        !TryPlanScoutSector(
                            scout,
                            basePosition,
                            baseTile,
                            fallback,
                            allowRepeat: _context.Difficulty == Difficulty.Easy,
                            out _))
                    {
                        return _mission.FallbackExitPoint == Vector2.Zero ? fallback : _mission.FallbackExitPoint;
                    }

                    _mission.HasCommittedReentryPlan = true;
                    if (ScoutReachedPoint(scout, _mission.EntryPoint, ScoutEntryArrivalDistance))
                    {
                        StartScoutPeek(ScoutMissionPhase.ReEnter);
                        return _mission.PeekPoint;
                    }

                    return _mission.EntryPoint;
                }

                if (TryStartBreakContactForImmediateThreat(scout, threat, fallback, "reposition-threat", out var retreatTarget))
                {
                    return retreatTarget;
                }

                return _mission.FallbackExitPoint == Vector2.Zero ? fallback : _mission.FallbackExitPoint;

            default:
                if (TryPlanScoutSector(scout, basePosition, baseTile, fallback, allowRepeat: true, out _))
                {
                    _mission.Phase = ScoutMissionPhase.ApproachEdge;
                    _mission.PhaseEnteredMs = _context.ElapsedMs;
                    return _mission.EntryPoint;
                }

                return fallback;
        }
    }

    public void TraceMissionTick(SimUnit scout, Vector2 target)
    {
        if (!ScoutDebugLogging || !_mission.Active || _mission.ScoutUnitId != scout.Id)
        {
            return;
        }

        var scoutTile = _context.Map.WorldToTile(scout.Position);
        var entryTile = _context.Map.WorldToTile(_mission.EntryPoint);
        var peekTile = _context.Map.WorldToTile(_mission.PeekPoint);
        var targetTile = _context.Map.WorldToTile(target);
        var visibleCommitMs = _mission.ExposureStartedMs >= 0d
            ? _context.ElapsedMs - _mission.ExposureStartedMs
            : 0d;
        var threat = FindScoutThreat(scout);
        var pathDestination = scout.PathDestination.HasValue
            ? scout.PathDestination.Value.ToString()
            : "none";
        GD.Print(
            $"[ScoutTick] t={_context.ElapsedMs:0} phase={_mission.Phase} pos={scout.Position} tile={scoutTile} " +
            $"visible={IsPlayerVisibleTile(scoutTile.X, scoutTile.Y)} target={target} targetTile={targetTile} pathDest={pathDestination} " +
            $"entry={_mission.EntryPoint} entryTile={entryTile} entryDist={scout.Position.DistanceTo(_mission.EntryPoint):0.0} " +
            $"peek={_mission.PeekPoint} peekTile={peekTile} peekVisible={IsPlayerVisibleTile(peekTile.X, peekTile.Y)} " +
            $"peekDist={scout.Position.DistanceTo(_mission.PeekPoint):0.0} breakExit={_mission.PlannedExitPoint} fallbackExit={_mission.FallbackExitPoint} " +
            $"sector={_mission.CurrentSector} lastSector={_mission.LastSector} forcedSwitchFrom={_mission.MandatorySectorSwitchFrom} " +
            $"requireSwitch={_mission.RequireSectorSwitch} reentryPlanCommitted={_mission.HasCommittedReentryPlan} " +
            $"exposureMs={visibleCommitMs:0} routeVisible={_mission.CurrentRouteExposure}/{_mission.CurrentVisibleRunLength} " +
            $"localThreat={EstimateScoutSectorThreat(scout.Position):0.00} threat={(threat is null ? "none" : threat.Position.ToString())}");
    }

    private float EvaluateScoutCandidate(SimUnit unit)
    {
        if (!unit.Alive || unit.Kind == UnitKind.Catapult)
        {
            return float.NegativeInfinity;
        }

        var durability = unit.MaxHp <= 0 ? 0f : unit.Hp / (float)unit.MaxHp;
        var kindBonus = unit.Kind switch
        {
            UnitKind.Knight => 90f,
            UnitKind.Archer => 56f,
            UnitKind.Footman => 34f,
            UnitKind.Worker => -140f,
            _ => 0f
        };
        var rangedSafety = unit.IsRanged() ? 12f : 0f;
        var combatPenalty = unit.State == UnitState.Attack ? 10f : 0f;
        return unit.Speed * 2.4f +
               unit.Sight * 20f +
               unit.Hp * 0.08f +
               durability * 55f +
               rangedSafety +
               kindBonus -
               combatPenalty;
    }

    private bool CanWorkerScoutSafely(SimUnit worker, Vector2 suspectedBase, Vector2 fallback)
    {
        if (!worker.Alive || !worker.IsWorker())
        {
            return false;
        }

        var basePosition = _context.LastKnownPlayerBase ?? suspectedBase;
        var baseTile = _context.LastKnownPlayerBaseTile ?? _context.Map.WorldToTile(basePosition);
        var snapshot = CreateMissionSnapshot();
        var canScout = TryPlanScoutSector(worker, basePosition, baseTile, fallback, allowRepeat: true, out _);
        RestoreMissionSnapshot(snapshot);
        return canScout;
    }

    private void ReleaseScoutLock()
    {
        ClearMissionOwnership();
    }

    private void ClearMissionOwnership()
    {
        _mission.Reset();
    }

    private void ClearRecallState()
    {
        _recallingScoutId = null;
        _assemblyPoint = Vector2.Zero;
    }

    private bool HasActiveScoutPlan()
    {
        return _mission.EntryPoint != Vector2.Zero &&
               _mission.PeekPoint != Vector2.Zero &&
               _mission.PlannedExitPoint != Vector2.Zero &&
               _mission.FallbackExitPoint != Vector2.Zero;
    }

    private ScoutMissionState CreateMissionSnapshot()
    {
        return new ScoutMissionState
        {
            Active = _mission.Active,
            ScoutUnitId = _mission.ScoutUnitId,
            WorkerFallback = _mission.WorkerFallback,
            Phase = _mission.Phase,
            PhaseEnteredMs = _mission.PhaseEnteredMs,
            LastThreatMs = _mission.LastThreatMs,
            ConfirmedBaseMs = _mission.ConfirmedBaseMs,
            ExposureStartedMs = _mission.ExposureStartedMs,
            BasePosition = _mission.BasePosition,
            BaseTile = _mission.BaseTile,
            RecoverPoint = _mission.RecoverPoint,
            LastThreatPosition = _mission.LastThreatPosition,
            CurrentSector = _mission.CurrentSector,
            LastSector = _mission.LastSector,
            MandatorySectorSwitchFrom = _mission.MandatorySectorSwitchFrom,
            EntryPoint = _mission.EntryPoint,
            PeekPoint = _mission.PeekPoint,
            PlannedExitPoint = _mission.PlannedExitPoint,
            FallbackExitPoint = _mission.FallbackExitPoint,
            CurrentRouteExposure = _mission.CurrentRouteExposure,
            CurrentVisibleRunLength = _mission.CurrentVisibleRunLength,
            PeekCompleted = _mission.PeekCompleted,
            RequireSectorSwitch = _mission.RequireSectorSwitch,
            HasCommittedReentryPlan = _mission.HasCommittedReentryPlan,
            LastIntelTargetKind = _mission.LastIntelTargetKind
        };
    }

    private void RestoreMissionSnapshot(ScoutMissionState snapshot)
    {
        _mission.Active = snapshot.Active;
        _mission.ScoutUnitId = snapshot.ScoutUnitId;
        _mission.WorkerFallback = snapshot.WorkerFallback;
        _mission.Phase = snapshot.Phase;
        _mission.PhaseEnteredMs = snapshot.PhaseEnteredMs;
        _mission.LastThreatMs = snapshot.LastThreatMs;
        _mission.ConfirmedBaseMs = snapshot.ConfirmedBaseMs;
        _mission.ExposureStartedMs = snapshot.ExposureStartedMs;
        _mission.BasePosition = snapshot.BasePosition;
        _mission.BaseTile = snapshot.BaseTile;
        _mission.RecoverPoint = snapshot.RecoverPoint;
        _mission.LastThreatPosition = snapshot.LastThreatPosition;
        _mission.CurrentSector = snapshot.CurrentSector;
        _mission.LastSector = snapshot.LastSector;
        _mission.MandatorySectorSwitchFrom = snapshot.MandatorySectorSwitchFrom;
        _mission.EntryPoint = snapshot.EntryPoint;
        _mission.PeekPoint = snapshot.PeekPoint;
        _mission.PlannedExitPoint = snapshot.PlannedExitPoint;
        _mission.FallbackExitPoint = snapshot.FallbackExitPoint;
        _mission.CurrentRouteExposure = snapshot.CurrentRouteExposure;
        _mission.CurrentVisibleRunLength = snapshot.CurrentVisibleRunLength;
        _mission.PeekCompleted = snapshot.PeekCompleted;
        _mission.RequireSectorSwitch = snapshot.RequireSectorSwitch;
        _mission.HasCommittedReentryPlan = snapshot.HasCommittedReentryPlan;
        _mission.LastIntelTargetKind = snapshot.LastIntelTargetKind;
    }

    private bool TryPlanScoutSector(
        SimUnit scout,
        Vector2 basePosition,
        Vector2I baseTile,
        Vector2 fallback,
        bool allowRepeat,
        out ScoutSectorOption plan)
    {
        if (!ShouldUseFrontierScoutCheat())
        {
            return TryPlanLegacyScoutSector(scout, basePosition, baseTile, fallback, allowRepeat, out plan);
        }

        return TryPlanFrontierScoutSector(scout, basePosition, baseTile, fallback, allowRepeat, out plan);
    }

    private bool TryPlanLegacyScoutSector(
        SimUnit scout,
        Vector2 basePosition,
        Vector2I baseTile,
        Vector2 fallback,
        bool allowRepeat,
        out ScoutSectorOption plan)
    {
        var outerRadiusTiles = int.Max(5, Mathf.RoundToInt((scout.Sight * GameConstants.TileSize - GameConstants.TileSize * 1.4f) / GameConstants.TileSize));
        var peekRadiusTiles = int.Max(3, outerRadiusTiles - 2);
        var sectorDirections = new[]
        {
            new Vector2(0f, 1f),
            new Vector2(0.75f, 0.75f),
            new Vector2(1f, 0f),
            new Vector2(0.75f, -0.75f),
            new Vector2(0f, -1f),
            new Vector2(-0.75f, -0.75f),
            new Vector2(-1f, 0f),
            new Vector2(-0.75f, 0.75f)
        };
        var candidates = new List<ScoutSectorAnchor>();
        for (var sectorIndex = 0; sectorIndex < sectorDirections.Length; sectorIndex++)
        {
            var direction = sectorDirections[sectorIndex].Normalized();
            var entryTile = baseTile + new Vector2I(
                Mathf.RoundToInt(direction.X * outerRadiusTiles),
                Mathf.RoundToInt(direction.Y * outerRadiusTiles));
            var peekTile = baseTile + new Vector2I(
                Mathf.RoundToInt(direction.X * peekRadiusTiles),
                Mathf.RoundToInt(direction.Y * peekRadiusTiles));
            if (!_context.TryFindWalkableRaidPoint(entryTile, 0, 2, scout.Position, out var entryPoint) ||
                !_context.TryFindWalkableRaidPoint(peekTile, 0, 2, entryPoint, out var peekPoint))
            {
                continue;
            }

            if (entryPoint.DistanceTo(peekPoint) < GameConstants.TileSize * 1.2f)
            {
                continue;
            }

            candidates.Add(new ScoutSectorAnchor(sectorIndex, entryPoint, peekPoint));
        }

        var bestScore = float.PositiveInfinity;
        var bestPlan = default(ScoutSectorOption);
        foreach (var candidate in candidates)
        {
            var repeatPenalty = 0f;
            if (!allowRepeat)
            {
                if (_mission.CurrentSector == candidate.SectorIndex)
                {
                    repeatPenalty += _context.DifficultyDefinition.ScoutSectorRepeatPenalty * 1.2f;
                }

                if (_mission.LastSector == candidate.SectorIndex)
                {
                    repeatPenalty += _context.DifficultyDefinition.ScoutSectorRepeatPenalty;
                }
            }

            var threat = EstimateScoutSectorThreat(candidate.PeekPoint);
            if (_mission.WorkerFallback &&
                threat > _context.DifficultyDefinition.ScoutThreatTolerance + 0.35f)
            {
                continue;
            }

            var intel = EvaluateScoutIntel(candidate.PeekPoint);
            var exitPoint = fallback;
            var fallbackExitPoint = fallback;
            var exitScore = float.PositiveInfinity;
            var fallbackExitScore = float.PositiveInfinity;
            foreach (var exitCandidate in candidates)
            {
                if (exitCandidate.SectorIndex == candidate.SectorIndex)
                {
                    continue;
                }

                var score = EstimateScoutSectorThreat(exitCandidate.EntryPoint) * 22f +
                            exitCandidate.EntryPoint.DistanceTo(candidate.PeekPoint) * 0.16f;
                if (score < exitScore)
                {
                    fallbackExitScore = exitScore;
                    fallbackExitPoint = exitPoint;
                    exitScore = score;
                    exitPoint = exitCandidate.EntryPoint;
                }
                else if (score < fallbackExitScore)
                {
                    fallbackExitScore = score;
                    fallbackExitPoint = exitCandidate.EntryPoint;
                }
            }

            var scoreValue = scout.Position.DistanceTo(candidate.EntryPoint) * 0.22f +
                             candidate.EntryPoint.DistanceTo(candidate.PeekPoint) * 0.12f +
                             Mathf.Max(0f, threat - _context.DifficultyDefinition.ScoutThreatTolerance) * 28f +
                             repeatPenalty +
                             intel.Score;
            if (scoreValue < bestScore)
            {
                bestScore = scoreValue;
                bestPlan = new ScoutSectorOption(
                    candidate.SectorIndex,
                    candidate.EntryPoint,
                    candidate.PeekPoint,
                    exitPoint,
                    fallbackExitPoint,
                    intel.Kind,
                    0,
                    0,
                    scoreValue);
            }
        }

        plan = bestPlan;
        if (bestScore == float.PositiveInfinity)
        {
            return false;
        }

        if (_mission.CurrentSector != plan.SectorIndex)
        {
            _mission.LastSector = _mission.CurrentSector;
        }

        _mission.CurrentSector = plan.SectorIndex;
        _mission.EntryPoint = plan.EntryPoint;
        _mission.PeekPoint = plan.PeekPoint;
        _mission.PlannedExitPoint = plan.ExitPoint;
        _mission.FallbackExitPoint = plan.FallbackExitPoint;
        _mission.ExposureStartedMs = -99999d;
        _mission.LastIntelTargetKind = plan.IntelKind;
        _mission.CurrentRouteExposure = plan.RouteExposure;
        _mission.CurrentVisibleRunLength = plan.VisibleRunLength;
        _mission.PeekCompleted = false;
        TraceScoutPlan("legacy", plan);
        return true;
    }

    private bool ShouldUseFrontierScoutCheat()
    {
        return _context.Difficulty != Difficulty.Easy &&
               _context.PlayerVisionSnapshot is { HasData: true };
    }

    private bool TryPlanFrontierScoutSector(
        SimUnit scout,
        Vector2 basePosition,
        Vector2I baseTile,
        Vector2 fallback,
        bool allowRepeat,
        out ScoutSectorOption plan)
    {
        plan = default;
        var frontierCandidates = CollectFrontierCandidatesAroundBase(scout, basePosition, baseTile);
        if (frontierCandidates.Count == 0)
        {
            return false;
        }

        var routeCandidates = new List<ScoutSectorOption>();
        foreach (var frontier in frontierCandidates)
        {
            if (BuildScoutRouteCandidate(scout, frontier, fallback, out var routeCandidate))
            {
                routeCandidates.Add(routeCandidate);
            }
        }

        if (routeCandidates.Count == 0)
        {
            return false;
        }

        if (_mission.RequireSectorSwitch)
        {
            var excludedSector = _mission.MandatorySectorSwitchFrom >= 0
                ? _mission.MandatorySectorSwitchFrom
                : _mission.CurrentSector;
            var switchedCandidates = SelectNextScoutSectorExcludingLast(routeCandidates, excludedSector);
            if (switchedCandidates.Count == 0)
            {
                return false;
            }

            routeCandidates = switchedCandidates;
        }
        else if (!allowRepeat && _mission.LastSector >= 0)
        {
            var saferCandidates = SelectNextScoutSectorExcludingLast(routeCandidates, _mission.LastSector);
            if (saferCandidates.Count > 0)
            {
                routeCandidates = saferCandidates;
            }
        }

        var bestScore = float.PositiveInfinity;
        var bestPlan = default(ScoutSectorOption);
        foreach (var candidate in routeCandidates)
        {
            var repeatPenalty = 0f;
            if (!allowRepeat)
            {
                if (_mission.CurrentSector == candidate.SectorIndex)
                {
                    repeatPenalty += _context.DifficultyDefinition.ScoutSectorRepeatPenalty * 1.2f;
                }

                if (_mission.LastSector == candidate.SectorIndex)
                {
                    repeatPenalty += _context.DifficultyDefinition.ScoutSectorRepeatPenalty;
                }
            }

            var score = candidate.Score + repeatPenalty;
            if (score < bestScore)
            {
                bestScore = score;
                bestPlan = candidate;
            }
        }

        if (bestScore == float.PositiveInfinity)
        {
            return false;
        }

        if (_mission.CurrentSector != bestPlan.SectorIndex)
        {
            _mission.LastSector = _mission.CurrentSector;
        }

        _mission.CurrentSector = bestPlan.SectorIndex;
        _mission.EntryPoint = bestPlan.EntryPoint;
        _mission.PeekPoint = bestPlan.PeekPoint;
        _mission.PlannedExitPoint = bestPlan.ExitPoint;
        _mission.FallbackExitPoint = bestPlan.FallbackExitPoint;
        _mission.ExposureStartedMs = -99999d;
        _mission.LastIntelTargetKind = bestPlan.IntelKind;
        _mission.CurrentRouteExposure = bestPlan.RouteExposure;
        _mission.CurrentVisibleRunLength = bestPlan.VisibleRunLength;
        plan = bestPlan;
        TraceScoutPlan("frontier", bestPlan);
        return true;
    }

    private List<ScoutSectorOption> SelectNextScoutSectorExcludingLast(List<ScoutSectorOption> candidates, int excludedSector)
    {
        if (excludedSector < 0)
        {
            return candidates;
        }

        return candidates.FindAll(candidate => candidate.SectorIndex != excludedSector);
    }

    private List<ScoutFrontierCandidate> CollectFrontierCandidatesAroundBase(SimUnit scout, Vector2 basePosition, Vector2I baseTile)
    {
        var result = new List<ScoutFrontierCandidate>();
        if (!ShouldUseFrontierScoutCheat())
        {
            return result;
        }

        var searchRadiusTiles = int.Max(scout.Sight + 5, 12);
        var minX = int.Max(0, baseTile.X - searchRadiusTiles);
        var maxX = int.Min(_context.Map.Width - 1, baseTile.X + searchRadiusTiles);
        var minY = int.Max(0, baseTile.Y - searchRadiusTiles);
        var maxY = int.Min(_context.Map.Height - 1, baseTile.Y + searchRadiusTiles);

        for (var ty = minY; ty <= maxY; ty++)
        {
            for (var tx = minX; tx <= maxX; tx++)
            {
                if (!IsFrontierTile(tx, ty))
                {
                    continue;
                }

                var entryPoint = _context.Map.TileToWorldCenter(tx, ty);
                var distanceToBase = entryPoint.DistanceTo(basePosition);
                if (distanceToBase > GameConstants.TileSize * searchRadiusTiles ||
                    distanceToBase < GameConstants.TileSize * 3.2f)
                {
                    continue;
                }

                if (!TryGetFrontierPeekTile(new Vector2I(tx, ty), basePosition, out var peekTile))
                {
                    continue;
                }

                var peekPoint = _context.Map.TileToWorldCenter(peekTile.X, peekTile.Y);
                var sectorIndex = GetScoutSectorIndex(basePosition, entryPoint);
                result.Add(new ScoutFrontierCandidate(sectorIndex, new Vector2I(tx, ty), peekTile, entryPoint, peekPoint));
            }
        }

        return result;
    }

    private bool TryGetFrontierPeekTile(Vector2I frontierTile, Vector2 basePosition, out Vector2I peekTile)
    {
        peekTile = frontierTile;
        var bestScore = float.PositiveInfinity;
        var found = false;
        for (var depth = 1; depth <= FrontierScoutMaxPeekDepthTiles; depth++)
        {
            for (var dy = -depth; dy <= depth; dy++)
            {
                for (var dx = -depth; dx <= depth; dx++)
                {
                    if ((dx == 0 && dy == 0) ||
                        int.Max(Math.Abs(dx), Math.Abs(dy)) != depth)
                    {
                        continue;
                    }

                    var tx = frontierTile.X + dx;
                    var ty = frontierTile.Y + dy;
                    if (!_context.Map.InBounds(tx, ty) ||
                        !_context.Map.IsWalkable(tx, ty) ||
                        !IsPlayerVisibleTile(tx, ty))
                    {
                        continue;
                    }

                    var world = _context.Map.TileToWorldCenter(tx, ty);
                    var depthPenalty = Math.Abs(depth - FrontierScoutPreferredPeekDepthTiles) * 14f;
                    var score = world.DistanceTo(basePosition) * 0.05f +
                                EstimateScoutSectorThreat(world) * 12f +
                                EvaluateScoutIntel(world).Score +
                                depthPenalty;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        peekTile = new Vector2I(tx, ty);
                        found = true;
                    }
                }
            }
        }

        return found;
    }

    private bool BuildScoutRouteCandidate(
        SimUnit scout,
        ScoutFrontierCandidate frontier,
        Vector2 fallback,
        out ScoutSectorOption candidate)
    {
        candidate = default;
        if (!TryBuildScoutPathTiles(scout, scout.Position, frontier.EntryPoint, 0f, out var routeToEntry) ||
            !TryBuildScoutPathTiles(scout, frontier.EntryPoint, frontier.PeekPoint, GameConstants.TileSize * 0.2f, out var routeToPeek) ||
            !TryBuildScoutPathTiles(scout, frontier.PeekPoint, frontier.EntryPoint, 0f, out var routeToExit) ||
            !TryFindScoutExitRoute(scout, frontier.EntryPoint, fallback, out var fallbackExitPoint, out var routeToFallback))
        {
            return false;
        }

        var exposure = EvaluateScoutRouteExposure(routeToEntry, routeToPeek, routeToExit, routeToFallback);
        if (!IsScoutRouteExposureSafe(scout, exposure))
        {
            return false;
        }

        var threat = EstimateScoutSectorThreat(frontier.PeekPoint);
        if (_mission.WorkerFallback &&
            threat > _context.DifficultyDefinition.ScoutThreatTolerance + 0.35f)
        {
            return false;
        }

        var intel = EvaluateScoutIntel(frontier.PeekPoint);
        var score = scout.Position.DistanceTo(frontier.EntryPoint) * 0.18f +
                    frontier.EntryPoint.DistanceTo(frontier.PeekPoint) * 0.1f +
                    Mathf.Max(0f, threat - _context.DifficultyDefinition.ScoutThreatTolerance) * 28f +
                    exposure.TotalVisibleTiles * 26f +
                    exposure.LongestVisibleRun * 32f +
                    intel.Score;
        candidate = new ScoutSectorOption(
            frontier.SectorIndex,
            frontier.EntryPoint,
            frontier.PeekPoint,
            frontier.EntryPoint,
            fallbackExitPoint,
            intel.Kind,
            exposure.TotalVisibleTiles,
            exposure.LongestVisibleRun,
            score);
        return true;
    }

    private bool TryFindScoutExitRoute(
        SimUnit scout,
        Vector2 exitPoint,
        Vector2 fallback,
        out Vector2 fallbackExitPoint,
        out List<Vector2I> routeToFallback)
    {
        fallbackExitPoint = Vector2.Zero;
        routeToFallback = [];
        if (!TryBuildScoutPathTiles(scout, exitPoint, fallback, 0f, out routeToFallback))
        {
            return false;
        }

        fallbackExitPoint = fallback;
        return true;
    }

    private bool TryBuildScoutPathTiles(
        SimUnit scout,
        Vector2 startWorld,
        Vector2 targetWorld,
        float arrivalRadius,
        out List<Vector2I> tilePath)
    {
        tilePath = [];
        var start = _context.Map.WorldToTile(startWorld);
        var goal = _context.Map.WorldToTile(targetWorld);
        if (!_context.Map.InBounds(start.X, start.Y) || !_context.Map.InBounds(goal.X, goal.Y))
        {
            return false;
        }

        var goalRadiusTiles = int.Max(0, Mathf.CeilToInt(arrivalRadius / GameConstants.TileSize));
        var tilePenalty = _context.BuildDynamicTilePenalty(scout, goal, goalRadiusTiles, stuckReroute: false);
        tilePath = Pathfinder.FindPath(_context.Map, start, goal, goalRadiusTiles, scout.Id % 8, tilePenalty);
        return tilePath.Count > 0;
    }

    private ScoutRouteExposure EvaluateScoutRouteExposure(params List<Vector2I>[] routeSegments)
    {
        var totalVisibleTiles = 0;
        var longestVisibleRun = 0;
        var currentRun = 0;
        var entryVisibleTiles = 0;
        var peekVisibleTiles = 0;
        var exitVisibleTiles = 0;
        var fallbackVisibleTiles = 0;

        for (var segmentIndex = 0; segmentIndex < routeSegments.Length; segmentIndex++)
        {
            foreach (var tile in routeSegments[segmentIndex])
            {
                if (IsPlayerVisibleTile(tile.X, tile.Y))
                {
                    totalVisibleTiles++;
                    currentRun++;
                    longestVisibleRun = int.Max(longestVisibleRun, currentRun);
                    switch (segmentIndex)
                    {
                        case 0:
                            entryVisibleTiles++;
                            break;
                        case 1:
                            peekVisibleTiles++;
                            break;
                        case 2:
                            exitVisibleTiles++;
                            break;
                        case 3:
                            fallbackVisibleTiles++;
                            break;
                    }
                }
                else
                {
                    currentRun = 0;
                }
            }
        }

        return new ScoutRouteExposure(totalVisibleTiles, longestVisibleRun, entryVisibleTiles, peekVisibleTiles, exitVisibleTiles, fallbackVisibleTiles);
    }

    private bool IsScoutRouteExposureSafe(SimUnit scout, ScoutRouteExposure exposure)
    {
        var maxVisibleTiles = int.Max(3, Mathf.CeilToInt(scout.Speed * (_context.DifficultyDefinition.ScoutMaxExposureMs / 1000f) / GameConstants.TileSize) + 2);
        var maxVisibleRunTiles = int.Max(2, maxVisibleTiles - 2);
        if (exposure.EntryVisibleTiles > ScoutMaxVisibleEntryTiles)
        {
            return false;
        }

        if (exposure.TotalVisibleTiles > maxVisibleTiles)
        {
            return false;
        }

        if (exposure.LongestVisibleRun > maxVisibleRunTiles)
        {
            return false;
        }

        if (exposure.PeekVisibleTiles < ScoutMinPeekVisibleTiles || exposure.ExitVisibleTiles > maxVisibleRunTiles)
        {
            return false;
        }

        return exposure.FallbackVisibleTiles <= maxVisibleTiles;
    }

    private bool IsScoutCurrentlyVisibleToPlayer(SimUnit scout)
    {
        var tile = _context.Map.WorldToTile(scout.Position);
        return IsPlayerVisibleTile(tile.X, tile.Y);
    }

    private bool IsScoutPeekPointCurrentlyVisible()
    {
        if (_mission.PeekPoint == Vector2.Zero)
        {
            return false;
        }

        var peekTile = _context.Map.WorldToTile(_mission.PeekPoint);
        return IsPlayerVisibleTile(peekTile.X, peekTile.Y);
    }

    private bool IsImmediateScoutThreat(SimUnit scout, ICombatTarget threat)
    {
        if (threat is SimUnit enemy)
        {
            var distance = scout.Position.DistanceTo(enemy.Position);
            var threatRange = enemy.Range + scout.Radius + enemy.Radius + ScoutDangerBuffer * 0.35f;
            return enemy.TargetCombat == scout || distance <= threatRange;
        }

        if (threat is SimBuilding building)
        {
            var distance = scout.Position.DistanceTo(building.Center);
            var threatRange = building.Range + scout.Radius + ScoutDangerBuffer * 0.2f;
            return distance <= threatRange;
        }

        return false;
    }

    private bool IsPlayerVisibleTile(int tx, int ty)
    {
        return _context.PlayerVisionSnapshot is not null && _context.PlayerVisionSnapshot.IsVisible(tx, ty);
    }

    private bool TryRefreshDynamicScoutPeekPoint(SimUnit scout, Vector2 basePosition)
    {
        if (!ShouldUseFrontierScoutCheat() ||
            _mission.PeekPoint == Vector2.Zero ||
            _mission.EntryPoint == Vector2.Zero)
        {
            return false;
        }

        var entryTile = _context.Map.WorldToTile(_mission.EntryPoint);
        var previousPeekPoint = _mission.PeekPoint;
        var bestPeekPoint = Vector2.Zero;
        var bestScore = float.PositiveInfinity;
        var maxDepth = FrontierScoutMaxPeekDepthTiles + 2;

        for (var depth = 1; depth <= maxDepth; depth++)
        {
            for (var dy = -depth; dy <= depth; dy++)
            {
                for (var dx = -depth; dx <= depth; dx++)
                {
                    if ((dx == 0 && dy == 0) ||
                        int.Max(Math.Abs(dx), Math.Abs(dy)) != depth)
                    {
                        continue;
                    }

                    var tx = entryTile.X + dx;
                    var ty = entryTile.Y + dy;
                    if (!_context.Map.InBounds(tx, ty) ||
                        !_context.Map.IsWalkable(tx, ty) ||
                        !IsPlayerVisibleTile(tx, ty))
                    {
                        continue;
                    }

                    var candidate = _context.Map.TileToWorldCenter(tx, ty);
                    if (GetScoutSectorIndex(basePosition, candidate) != _mission.CurrentSector)
                    {
                        continue;
                    }

                    if (!TryBuildScoutPathTiles(scout, scout.Position, candidate, GameConstants.TileSize * 0.2f, out _))
                    {
                        continue;
                    }

                    var threat = EstimateScoutSectorThreat(candidate);
                    if (threat > _context.DifficultyDefinition.ScoutThreatTolerance + (_mission.WorkerFallback ? 0.35f : 0.95f))
                    {
                        continue;
                    }

                    var depthPenalty = Math.Abs(depth - FrontierScoutPreferredPeekDepthTiles) * 14f;
                    var score = scout.Position.DistanceTo(candidate) * 0.12f +
                                previousPeekPoint.DistanceTo(candidate) * 0.16f +
                                threat * 14f +
                                EvaluateScoutIntel(candidate).Score +
                                depthPenalty;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestPeekPoint = candidate;
                    }
                }
            }
        }

        if (bestPeekPoint == Vector2.Zero)
        {
            return false;
        }

        _mission.PeekPoint = bestPeekPoint;
        TraceScoutRetarget("dynamic-peek-refresh", scout, previousPeekPoint);
        return true;
    }

    private bool IsFrontierTile(int tx, int ty)
    {
        if (!_context.Map.InBounds(tx, ty) || !_context.Map.IsWalkable(tx, ty) || IsPlayerVisibleTile(tx, ty))
        {
            return false;
        }

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                var nx = tx + dx;
                var ny = ty + dy;
                if (_context.Map.InBounds(nx, ny) && IsPlayerVisibleTile(nx, ny))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int GetScoutSectorIndex(Vector2 origin, Vector2 point)
    {
        var direction = point - origin;
        if (direction.LengthSquared() <= 0.01f)
        {
            return 0;
        }

        var angle = Mathf.PosMod(Mathf.Atan2(direction.Y, direction.X) + Mathf.Pi / 8f, Mathf.Tau);
        return Mathf.Clamp((int)(angle / (Mathf.Tau / 8f)), 0, 7);
    }

    private ScoutIntelInfo EvaluateScoutIntel(Vector2 position)
    {
        var workers = _context.CountKnownWorkersNear(position, GameConstants.TileSize * 3.1f);
        if (workers > 0)
        {
            return new ScoutIntelInfo(ScoutIntelTargetKind.WorkerLine, -165f - workers * 16f);
        }

        if (_context.HasKnownTowerNear(position, GameConstants.TileSize * 3.2f))
        {
            return new ScoutIntelInfo(ScoutIntelTargetKind.TowerPerimeter, -108f);
        }

        if (_context.HasKnownOuterTargetNear(position, GameConstants.TileSize * 3.3f))
        {
            return new ScoutIntelInfo(ScoutIntelTargetKind.OuterBuilding, -88f);
        }

        if (_context.CountKnownCombatUnitsNear(position, GameConstants.TileSize * 3.6f) > 0)
        {
            return new ScoutIntelInfo(ScoutIntelTargetKind.ArmyEdge, -72f);
        }

        return new ScoutIntelInfo(ScoutIntelTargetKind.BaseEdge, -32f);
    }

    private float EstimateScoutSectorThreat(Vector2 position)
    {
        return _context.EstimateKnownThreatAt(position, ScoutSectorThreatRadius);
    }

    private void StartScoutPeek(ScoutMissionPhase phase)
    {
        _mission.Phase = phase;
        _mission.PhaseEnteredMs = _context.ElapsedMs;
        _mission.ExposureStartedMs = -99999d;
        _mission.PeekCompleted = false;
        _mission.RequireSectorSwitch = false;
        _mission.MandatorySectorSwitchFrom = -1;
        _mission.HasCommittedReentryPlan = false;
    }

    private void StartScoutBreakContact()
    {
        _mission.MandatorySectorSwitchFrom = _mission.CurrentSector;
        _mission.Phase = ScoutMissionPhase.BreakContact;
        _mission.PhaseEnteredMs = _context.ElapsedMs;
        _mission.PeekCompleted = true;
        _mission.RequireSectorSwitch = true;
        _mission.HasCommittedReentryPlan = false;
    }

    private bool TryStartBreakContactForImmediateThreat(SimUnit scout, ICombatTarget? threat, Vector2 fallback, string reason, out Vector2 retreatTarget)
    {
        retreatTarget = Vector2.Zero;
        if (threat is null || !IsImmediateScoutThreat(scout, threat))
        {
            return false;
        }

        TraceScoutBreak(reason, scout, threat);
        StartScoutBreakContact();
        retreatTarget = GetScoutBreakContactTarget(scout, fallback);
        return true;
    }

    private bool ShouldBreakScoutContact(SimUnit scout, ICombatTarget? threat)
    {
        var inPlayerVision = IsScoutCurrentlyVisibleToPlayer(scout);
        var visibleCommitMs = _mission.ExposureStartedMs >= 0d
            ? _context.ElapsedMs - _mission.ExposureStartedMs
            : 0d;
        if (threat is not null)
        {
            if (!inPlayerVision || visibleCommitMs < ScoutMinVisibleCommitMs)
            {
                var shouldBreakImmediately = IsImmediateScoutThreat(scout, threat);
                if (shouldBreakImmediately)
                {
                    TraceScoutBreak("immediate-threat", scout, threat);
                }

                return shouldBreakImmediately;
            }

            TraceScoutBreak("visible-threat", scout, threat);
            return true;
        }

        if (_mission.ExposureStartedMs >= 0d &&
            _context.ElapsedMs - _mission.ExposureStartedMs >= _context.DifficultyDefinition.ScoutMaxExposureMs)
        {
            TraceScoutBreak("max-exposure", scout, null);
            return true;
        }

        if (!inPlayerVision || visibleCommitMs < ScoutMinVisibleCommitMs)
        {
            return false;
        }

        var localThreat = EstimateScoutSectorThreat(scout.Position);
        var shouldBreakForThreat = localThreat > _context.DifficultyDefinition.ScoutThreatTolerance + (_mission.WorkerFallback ? 0.2f : 0.75f);
        if (shouldBreakForThreat)
        {
            TraceScoutBreak("local-threat-threshold", scout, null);
        }

        return shouldBreakForThreat;
    }

    private bool CanScoutReEnter(SimUnit scout, ICombatTarget? threat)
    {
        if (threat is not null)
        {
            return false;
        }

        if (_context.ElapsedMs - Math.Max(_mission.LastThreatMs, _mission.PhaseEnteredMs) < _context.DifficultyDefinition.ScoutReentryDelayMs)
        {
            return false;
        }

        if (EstimateScoutSectorThreat(scout.Position) > _context.DifficultyDefinition.ScoutThreatTolerance * 0.7f + 0.3f)
        {
            return false;
        }

        return scout.Position.DistanceTo(_mission.PlannedExitPoint) <= GameConstants.TileSize * 1.75f ||
               scout.Position.DistanceTo(_mission.FallbackExitPoint) <= GameConstants.TileSize * 1.75f ||
               scout.Position.DistanceTo(_mission.BasePosition) >= scout.Sight * GameConstants.TileSize;
    }

    private Vector2 GetScoutBreakContactTarget(SimUnit scout, Vector2 fallback)
    {
        var best = fallback;
        var bestScore = float.PositiveInfinity;
        var options = new[] { _mission.PlannedExitPoint, _mission.FallbackExitPoint, fallback };
        foreach (var point in options)
        {
            if (point == Vector2.Zero)
            {
                continue;
            }

            var score = scout.Position.DistanceTo(point) * 0.2f +
                        EstimateScoutSectorThreat(point) * 24f;
            if (_mission.LastThreatPosition.HasValue)
            {
                score -= point.DistanceTo(_mission.LastThreatPosition.Value) * 0.48f;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = point;
            }
        }

        return best;
    }

    private static bool ScoutReachedPoint(SimUnit scout, Vector2 point, float distance)
    {
        return point != Vector2.Zero && scout.Position.DistanceTo(point) <= distance;
    }

    private void TraceScoutPlan(string plannerName, ScoutSectorOption plan)
    {
        if (!ScoutDebugLogging)
        {
            return;
        }

        var peekTile = _context.Map.WorldToTile(plan.PeekPoint);
        GD.Print(
            $"[ScoutPlan] t={_context.ElapsedMs:0} planner={plannerName} phase={_mission.Phase} sector={plan.SectorIndex} " +
            $"entry={plan.EntryPoint} peek={plan.PeekPoint} peekTile={peekTile} peekVisible={IsPlayerVisibleTile(peekTile.X, peekTile.Y)} " +
            $"exit={plan.ExitPoint} fallbackExit={plan.FallbackExitPoint} exposure={plan.RouteExposure}/{plan.VisibleRunLength} score={plan.Score:0.0} " +
            $"switchFrom={_mission.MandatorySectorSwitchFrom} current={_mission.CurrentSector} last={_mission.LastSector}");
    }

    private void TraceScoutBreak(string reason, SimUnit scout, ICombatTarget? threat)
    {
        if (!ScoutDebugLogging)
        {
            return;
        }

        var scoutTile = _context.Map.WorldToTile(scout.Position);
        var threatPosition = threat is null ? "none" : threat.Position.ToString();
        GD.Print(
            $"[ScoutBreak] t={_context.ElapsedMs:0} reason={reason} phase={_mission.Phase} pos={scout.Position} tile={scoutTile} " +
            $"visible={IsPlayerVisibleTile(scoutTile.X, scoutTile.Y)} peekDist={scout.Position.DistanceTo(_mission.PeekPoint):0.0} " +
            $"sector={_mission.CurrentSector} forcedSwitchFrom={_mission.MandatorySectorSwitchFrom} threat={threatPosition}");
    }

    private void TraceScoutRetarget(string reason, SimUnit scout, Vector2 previousPeekPoint)
    {
        if (!ScoutDebugLogging)
        {
            return;
        }

        var previousTile = previousPeekPoint == Vector2.Zero ? new Vector2I(-1, -1) : _context.Map.WorldToTile(previousPeekPoint);
        var currentTile = _mission.PeekPoint == Vector2.Zero ? new Vector2I(-1, -1) : _context.Map.WorldToTile(_mission.PeekPoint);
        GD.Print(
            $"[ScoutRetarget] t={_context.ElapsedMs:0} reason={reason} phase={_mission.Phase} pos={scout.Position} " +
            $"oldPeek={previousPeekPoint} oldPeekTile={previousTile} oldVisible={(previousPeekPoint != Vector2.Zero && IsPlayerVisibleTile(previousTile.X, previousTile.Y))} " +
            $"newPeek={_mission.PeekPoint} newPeekTile={currentTile} newVisible={(_mission.PeekPoint != Vector2.Zero && IsPlayerVisibleTile(currentTile.X, currentTile.Y))} " +
            $"sector={_mission.CurrentSector}");
    }

    private ICombatTarget? FindScoutThreat(SimUnit scout)
    {
        ICombatTarget? best = null;
        var bestScore = float.PositiveInfinity;
        foreach (var enemy in _context.Units)
        {
            if (!enemy.Alive || enemy.Side == scout.Side || !enemy.CanAttack())
            {
                continue;
            }

            var distance = scout.Position.DistanceTo(enemy.Position);
            if (distance > scout.Sight * GameConstants.TileSize + GameConstants.TileSize)
            {
                continue;
            }

            var threatRange = enemy.Range + scout.Radius + enemy.Radius + ScoutDangerBuffer;
            var canThreaten = distance <= threatRange || enemy.TargetCombat == scout || enemy.Speed >= scout.Speed * 0.92f;
            if (!canThreaten)
            {
                continue;
            }

            var score = distance - enemy.Speed * 0.35f;
            if (enemy.TargetCombat == scout)
            {
                score -= 40f;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = enemy;
            }
        }

        foreach (var building in _context.Buildings)
        {
            if (!building.Alive || building.Side == scout.Side || !building.CanAttack())
            {
                continue;
            }

            var distance = scout.Position.DistanceTo(building.Center);
            var threatRange = building.Range + scout.Radius + ScoutDangerBuffer;
            if (distance > scout.Sight * GameConstants.TileSize + GameConstants.TileSize ||
                distance > threatRange + GameConstants.TileSize * 0.75f)
            {
                continue;
            }

            var score = distance - 24f;
            if (score < bestScore)
            {
                bestScore = score;
                best = building;
            }
        }

        return best;
    }

}
