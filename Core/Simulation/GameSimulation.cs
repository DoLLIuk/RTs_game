using System;
using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Economy;
using RtsNaGodote.Core.Simulation.Pathfinding;
using RtsNaGodote.Core.Simulation.Resources;
using RtsNaGodote.Core.Simulation.Units;
using RtsNaGodote.Core.Simulation.World;
using GameSide = RtsNaGodote.Core.Data.Side;
using WorldTileMap = RtsNaGodote.Core.Simulation.World.TileMap;

namespace RtsNaGodote.Core.Simulation;

public sealed class GameSimulation
{
    private const float CombatBuildingTargetPenalty = 72f;
    private const float CombatRetargetBuffer = 18f;
    private const float AiAssaultStandoffDistance = GameConstants.TileSize * 3f;
    private const float HeavyRerouteCooldownMs = 650f;
    private const float HeavyRerouteTriggerMs = 1100f;
    private const float CompactSideStepFactor = 0.45f;
    private const float CompactForwardBiasFactor = 0.2f;
    private const float SoftUnitTilePenalty = 2.1f;
    private const float SoftUnitNeighborTilePenalty = 0.35f;
    private const float StaticBlockerTilePenalty = 110f;
    private const float StaticBlockerNeighborTilePenalty = 18f;
    private const float HarassRecoverDurationMs = 3200f;
    private const float HarassNoTradeWindowMs = 2500f;
    private const float HarassRaidActivationDistance = GameConstants.TileSize * 2.5f;
    private const float HarassThreatRadius = GameConstants.TileSize * 5.5f;
    private const float HarassRepeatPenaltyDistance = GameConstants.TileSize * 3.5f;
    private const float ScoutQuietWindowMs = 1800f;
    private const float ScoutContinueAfterConfirmMs = 5200f;
    private const float ScoutDangerBuffer = GameConstants.TileSize * 1.75f;
    private const float ScoutSectorThreatRadius = GameConstants.TileSize * 4.5f;
    private const float ScoutPeekCompletionDistance = GameConstants.TileSize * 0.38f;
    private const float ScoutEntryArrivalDistance = GameConstants.TileSize * 0.95f;
    private const float ScoutMinVisibleCommitMs = 260f;
    private const int ScoutMaxVisibleEntryTiles = 1;
    private const int ScoutMinPeekVisibleTiles = 2;
    private const int FrontierScoutPreferredPeekDepthTiles = 2;
    private const int FrontierScoutMaxPeekDepthTiles = 3;

    private readonly record struct CombatApproachSlot(Vector2 Target, float ArrivalRadius);

    public event Action<Vector2, Vector2, GameSide, bool, bool>? ProjectileLaunched;
    public event Action<Vector2, bool, int>? HitOccurred;
    public event Action<Vector2, bool, GameSide>? EntityDestroyed;
    public event Action<Vector2, GameSide>? BuildingCompleted;
    public event Action<Vector2, GameSide>? UnitProduced;
    public event Action<Vector2, ResourceType, int, GameSide>? ResourceGathered;
    public event Action<Vector2, ResourceType, int, GameSide>? ResourceDeposited;
    public event Action<Vector2>? UnderAttack;
    public event Action<GameSide>? GameOverResolved;

    private int _nextEntityId = 1;
    private double _elapsedMs;
    private double _aiTickAccumMs;
    private readonly DifficultyDefinition _difficultyDefinition;
    private readonly AiMemory _aiMemory = new();
    private readonly HarassMissionState _aiHarassMission = new();
    private readonly ScoutMissionState _aiScoutMission = new();
    private PlayerVisionSnapshot? _playerVisionSnapshot;
    private AiState _aiState = AiState.Open;
    private double _aiStateEnteredMs;
    private int? _aiWorkerScoutId;
    private double _aiLastScoutCommandMs = -99999d;
    private double _aiLastHarassCommandMs = -99999d;
    private double _aiLastMainCommandMs = -99999d;

    public GameSimulation(GameInit init)
    {
        Init = init;
        Seed = init.Seed;
        PlayerRace = init.PlayerRace;
        AIRace = init.AIRace;
        Difficulty = init.Difficulty;
        _difficultyDefinition = GameSettings.GetDifficulty(init.Difficulty);
        Layout = MapGenerator.Generate(init.Seed);
        Map = Layout.Map;
        Units = [];
        Buildings = [];
        Resources = [];
        Economy = new EconomySystem();
        _aiStateEnteredMs = 0d;

        SpawnInitialState();
    }

    public int Seed { get; }
    public GameInit Init { get; }
    public Race PlayerRace { get; }
    public Race AIRace { get; }
    public Difficulty Difficulty { get; }
    public WorldTileMap Map { get; }
    public MapLayout Layout { get; }
    public List<SimUnit> Units { get; }
    public List<SimBuilding> Buildings { get; }
    public List<SimResourceNode> Resources { get; }
    public EconomySystem Economy { get; }
    public GameSide? Winner { get; private set; }
    public bool GameOver => Winner.HasValue;

    public PlayerState GetPlayer(GameSide side)
    {
        return Economy.Get(side);
    }

    public void UpdatePlayerVisionSnapshot(PlayerVisionSnapshot snapshot)
    {
        _playerVisionSnapshot = snapshot;
    }

    public void Update(double delta)
    {
        if (GameOver)
        {
            return;
        }

        var deltaMs = delta * 1000d;
        _elapsedMs += deltaMs;
        _aiTickAccumMs += deltaMs;

        foreach (var unit in Units)
        {
            if (!unit.Alive)
            {
                continue;
            }

            unit.PathRepathMs += deltaMs;
            if (unit.IsWorker() && unit.WorkerDefenseMode == WorkerDefenseMode.EvadeToHall)
            {
                TickWorkerEvade(unit, delta, deltaMs);
                continue;
            }

            if (unit.IsWorker() && unit.WorkerDefenseMode == WorkerDefenseMode.BaseDefenseCombat)
            {
                TickWorkerBaseDefenseCombat(unit, delta);
                continue;
            }

            switch (unit.State)
            {
                case UnitState.Idle:
                    TickIdle(unit);
                    break;
                case UnitState.Move:
                    TickMove(unit, delta);
                    break;
                case UnitState.AttackMove:
                    TickAttackMove(unit, delta);
                    break;
                case UnitState.Attack:
                    TickAttack(unit, delta);
                    break;
                case UnitState.Gather:
                    TickGather(unit, delta, deltaMs);
                    break;
                case UnitState.ReturnCargo:
                    TickReturnCargo(unit, delta);
                    break;
                case UnitState.Build:
                    TickBuild(unit, delta, deltaMs);
                    break;
            }
        }

        ApplySeparation(delta);

        foreach (var building in Buildings)
        {
            if (!building.Alive)
            {
                continue;
            }

            TickBuildingAttack(building);
            var produced = building.TickProduction(deltaMs);
            if (produced.HasValue)
            {
                FinishProduction(building, produced.Value);
            }
        }

        if (_aiTickAccumMs >= _difficultyDefinition.AiDelayMs)
        {
            _aiTickAccumMs = 0d;
            RunAi();
        }

        PruneDead();
        CheckVictory();
    }

    public void IssueMove(SimUnit unit, Vector2 worldTarget)
    {
        if (!unit.Alive)
        {
            return;
        }

        unit.ClearOrders();
        if (unit.IsWorker())
        {
            unit.SaveWorkerMoveOrder(worldTarget);
        }
        Repath(unit, worldTarget, 0f);
        unit.SetState(unit.Path.Count > 0 ? UnitState.Move : UnitState.Idle);
    }

    public void IssueAttackMove(SimUnit unit, Vector2 worldTarget)
    {
        if (!unit.Alive || !unit.CanAttack())
        {
            return;
        }

        if (unit.IsNonCombatScout)
        {
            IssueMove(unit, worldTarget);
            return;
        }

        unit.ClearOrders();
        unit.AttackMoveTarget = worldTarget;
        Repath(unit, worldTarget, 0f);
        unit.SetState(unit.Path.Count > 0 ? UnitState.AttackMove : UnitState.Idle);
    }

    public void IssueAttack(SimUnit unit, ICombatTarget target)
    {
        if (!unit.Alive || !target.Alive || unit.Side == target.Side || !unit.CanAttack())
        {
            return;
        }

        if (unit.IsNonCombatScout)
        {
            return;
        }

        unit.ClearOrders();
        unit.TargetCombat = target;
        unit.SetState(UnitState.Attack);
    }

    public void IssueGather(SimUnit unit, SimResourceNode node)
    {
        if (!unit.Alive || !unit.IsWorker() || !node.Alive)
        {
            return;
        }

        unit.ClearOrders();
        unit.TargetResource = node;
        unit.DesiredResourceType = node.Type;
        unit.SaveWorkerGatherOrder(node, node.Type);
        unit.SetState(UnitState.Gather);
    }

    public void IssueBuild(SimUnit unit, SimBuilding site)
    {
        if (!unit.Alive || !unit.IsWorker() || !site.Alive || site.Completed)
        {
            return;
        }

        unit.ClearOrders();
        unit.TargetBuilding = site;
        unit.SaveWorkerBuildOrder(site);
        var reach = site.Radius + unit.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
        Repath(unit, site.Center, reach);
        unit.SetState(UnitState.Build);
    }

    public void IssueReturnCargo(SimUnit unit, SimBuilding hall)
    {
        if (!unit.Alive || !unit.IsWorker() || unit.CargoType is null || unit.CargoAmount <= 0)
        {
            return;
        }

        if (unit.IsWorker())
        {
            unit.SaveWorkerReturnOrder(hall);
        }
        unit.ReturnBuilding = hall;
        unit.Path.Clear();
        var reach = hall.Radius + unit.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
        Repath(unit, GetWorkerReturnPathTarget(unit, hall), reach);
        unit.SetState(UnitState.ReturnCargo);
    }

    public void IssueStop(SimUnit unit)
    {
        unit.ClearOrders();
    }

    public BuildingPlacementResult EvaluateBuildingPlacement(GameSide side, BuildingKind kind, Vector2I tilePosition)
    {
        var definition = GameDefinitions.Buildings[kind];
        var economy = Economy.Get(side);
        if (economy.Gold < definition.CostGold || economy.Lumber < definition.CostLumber)
        {
            return new BuildingPlacementResult(false, BuildingPlacementIssue.InsufficientResources, tilePosition, definition.Size);
        }

        for (var dy = 0; dy < definition.Size; dy++)
        {
            for (var dx = 0; dx < definition.Size; dx++)
            {
                var tx = tilePosition.X + dx;
                var ty = tilePosition.Y + dy;
                if (!Map.InBounds(tx, ty))
                {
                    return new BuildingPlacementResult(false, BuildingPlacementIssue.OutOfBounds, tilePosition, definition.Size);
                }

                if (!Map.IsWalkable(tx, ty))
                {
                    return new BuildingPlacementResult(false, BuildingPlacementIssue.Blocked, tilePosition, definition.Size);
                }
            }
        }

        return new BuildingPlacementResult(true, BuildingPlacementIssue.None, tilePosition, definition.Size);
    }

    public bool CanPlaceBuilding(BuildingKind kind, Vector2I tilePosition)
    {
        return EvaluateBuildingPlacement(GameSide.Player, kind, tilePosition).CanPlace;
    }

    public bool TryStartBuilding(GameSide side, Race race, BuildingKind kind, Vector2I tilePosition, out SimBuilding? site)
    {
        site = null;
        var definition = GameDefinitions.Buildings[kind];
        var placement = EvaluateBuildingPlacement(side, kind, tilePosition);
        if (!placement.CanPlace || !Economy.Spend(side, definition.CostGold, definition.CostLumber))
        {
            return false;
        }

        site = SpawnBuilding(tilePosition, kind, side, race, false);
        return true;
    }

    public bool TryQueueUnit(SimBuilding building, UnitKind kind)
    {
        if (!building.Alive || !building.Completed)
        {
            return false;
        }

        var unitDefinition = GameDefinitions.Units[kind];
        if (building.Kind != unitDefinition.Producer)
        {
            return false;
        }

        if (unitDefinition.Requires.HasValue &&
            !Buildings.Exists(other => other.Alive && other.Completed && other.Side == building.Side && other.Kind == unitDefinition.Requires.Value))
        {
            return false;
        }

        if (building.Queue.Count >= 5 || !Economy.CanAfford(building.Side, unitDefinition.CostGold, unitDefinition.CostLumber))
        {
            return false;
        }

        if (!Economy.HasFoodRoom(building.Side, unitDefinition.Food))
        {
            return false;
        }

        if (!Economy.Spend(building.Side, unitDefinition.CostGold, unitDefinition.CostLumber))
        {
            return false;
        }

        building.Enqueue(kind);
        return true;
    }

    public bool TryCancelLastQueuedUnit(SimBuilding building, out UnitKind? canceledKind)
    {
        canceledKind = null;
        if (!building.Alive || !building.Completed || building.Queue.Count <= 1)
        {
            return false;
        }

        var last = building.Queue[^1];
        building.Queue.RemoveAt(building.Queue.Count - 1);
        var definition = GameDefinitions.Units[last.Kind];
        Refund(building.Side, definition.CostGold, definition.CostLumber);
        canceledKind = last.Kind;
        return true;
    }

    public void SetRallyPoint(SimBuilding building, Vector2 worldPosition)
    {
        if (!building.Alive || !building.Completed)
        {
            return;
        }

        building.RallyPoint = worldPosition;
    }

    public SimBuilding? FindNearestHall(SimUnit unit)
    {
        SimBuilding? best = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var building in Buildings)
        {
            if (!building.Alive || !building.Completed || building.Side != unit.Side || building.Kind != BuildingKind.TownHall)
            {
                continue;
            }

            var distance = building.Center.DistanceTo(unit.Position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = building;
            }
        }

        return best;
    }

    public SimResourceNode? FindNearestResource(SimUnit unit, ResourceType type)
    {
        SimResourceNode? best = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var resource in Resources)
        {
            if (!resource.Alive || resource.Type != type)
            {
                continue;
            }

            var distance = resource.Center.DistanceTo(unit.Position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = resource;
            }
        }

        return best;
    }

    private void SpawnInitialState()
    {
        Economy.Register(GameSide.Player, PlayerRace, GameConstants.StartingGold, GameConstants.StartingLumber, GameConstants.StartingFoodCap);
        Economy.Register(GameSide.AI, AIRace, GameConstants.StartingGold, GameConstants.StartingLumber, GameConstants.StartingFoodCap);

        var playerHall = SpawnBuilding(new Vector2I(Layout.PlayerBase.X - 1, Layout.PlayerBase.Y - 1), BuildingKind.TownHall, GameSide.Player, PlayerRace, true);
        var aiHall = SpawnBuilding(new Vector2I(Layout.AIBase.X - 1, Layout.AIBase.Y - 1), BuildingKind.TownHall, GameSide.AI, AIRace, true);

        var playerSpawnY = playerHall.Center.Y + 76f;
        var aiSpawnY = aiHall.Center.Y + 76f;
        for (var i = 0; i < 4; i++)
        {
            SpawnUnit(new Vector2(playerHall.Center.X - 66f + i * 28f, playerSpawnY), UnitKind.Worker, GameSide.Player, PlayerRace);
            SpawnUnit(new Vector2(aiHall.Center.X - 66f + i * 28f, aiSpawnY), UnitKind.Worker, GameSide.AI, AIRace);
        }

        foreach (var goldMineCenter in Layout.GoldMines)
        {
            SpawnResource(new Vector2I(goldMineCenter.X - 1, goldMineCenter.Y - 1), ResourceType.Gold);
        }

        foreach (var treeTile in Layout.Trees)
        {
            SpawnResource(treeTile, ResourceType.Lumber);
        }
    }

    private SimUnit SpawnUnit(Vector2 position, UnitKind kind, GameSide side, Race race)
    {
        var unit = new SimUnit(_nextEntityId++, kind, side, race, position);
        Units.Add(unit);
        Economy.AddFood(side, unit.Food);
        UnitProduced?.Invoke(position, side);
        return unit;
    }

    private SimBuilding SpawnBuilding(Vector2I tilePosition, BuildingKind kind, GameSide side, Race race, bool completed)
    {
        var building = new SimBuilding(_nextEntityId++, kind, side, race, tilePosition, completed);
        Buildings.Add(building);

        for (var dy = 0; dy < building.SizeTiles; dy++)
        {
            for (var dx = 0; dx < building.SizeTiles; dx++)
            {
                Map.SetWalkable(tilePosition.X + dx, tilePosition.Y + dy, false);
            }
        }

        if (completed)
        {
            Economy.AddCap(side, GameDefinitions.Buildings[kind].FoodCapBonus);
        }

        return building;
    }

    private SimResourceNode SpawnResource(Vector2I tilePosition, ResourceType type)
    {
        var resource = new SimResourceNode(_nextEntityId++, type, tilePosition);
        Resources.Add(resource);

        for (var dy = 0; dy < resource.TileHeight; dy++)
        {
            for (var dx = 0; dx < resource.TileWidth; dx++)
            {
                Map.SetWalkable(tilePosition.X + dx, tilePosition.Y + dy, false);
            }
        }

        return resource;
    }

    private void TickIdle(SimUnit unit)
    {
        if (!unit.CanAttack() || unit.IsNonCombatScout)
        {
            return;
        }

        var target = AcquireTarget(unit, unit.Sight * GameConstants.TileSize);
        if (target is not null)
        {
            SwitchCombatTarget(unit, target);
        }
    }

    private void TickMove(SimUnit unit, double delta)
    {
        if (!AdvanceWithRecovery(unit, delta))
        {
            if (unit.IsWorker() && unit.WorkerPassiveOrderType == WorkerPassiveOrderType.Move)
            {
                unit.ClearWorkerPassiveOrder();
            }
            unit.SetState(UnitState.Idle);
        }
    }

    private void TickAttackMove(SimUnit unit, double delta)
    {
        var target = AcquireTarget(unit, unit.Sight * GameConstants.TileSize);
        if (target is not null)
        {
            SwitchCombatTarget(unit, target);
            return;
        }

        if (!AdvanceWithRecovery(unit, delta))
        {
            unit.AttackMoveTarget = null;
            unit.SetState(UnitState.Idle);
        }
    }

    private void TickAttack(SimUnit unit, double delta)
    {
        var target = unit.TargetCombat;
        if (target is null || !target.Alive || target.Side == unit.Side)
        {
            unit.TargetCombat = null;
            if (unit.AttackMoveTarget.HasValue)
            {
                var resume = unit.AttackMoveTarget.Value;
                Repath(unit, resume, 0f);
                unit.SetState(unit.Path.Count > 0 ? UnitState.AttackMove : UnitState.Idle);
                return;
            }

            unit.SetState(UnitState.Idle);
            return;
        }

        if (unit.Side == GameSide.AI &&
            target.IsBuilding &&
            TryFindImmediateUnitRetarget(unit, out var unitThreat))
        {
            SwitchCombatTarget(unit, unitThreat);
            target = unitThreat;
        }

        var distance = unit.Position.DistanceTo(target.Position);
        var range = GetAttackRange(unit, target);
        if (distance > range)
        {
            if (unit.Path.Count == 0 || unit.PathRepathMs >= GameConstants.RepathIntervalMs)
            {
                if (TryBuildCombatApproachTarget(unit, target, out var approachTarget, out var approachArrivalRadius))
                {
                    Repath(unit, approachTarget, approachArrivalRadius);
                }
                else
                {
                    Repath(unit, target.Position, range);
                }
            }

            AdvanceWithRecovery(unit, delta);
            return;
        }

        if (_elapsedMs - unit.LastAttackMs < unit.CooldownMs)
        {
            return;
        }

        unit.LastAttackMs = _elapsedMs;
        unit.Path.Clear();
        var damage = unit.Attack + (target.IsBuilding ? unit.BonusVsBuilding : 0);
        if (unit.IsRanged())
        {
            ProjectileLaunched?.Invoke(unit.Position, target.Position, unit.Side, unit.IsSiege(), false);
        }
        DealDamage(target, damage, unit, unit.SplashRadius);

        if (target is SimUnit victim &&
            victim.Alive &&
            victim.CanAttack() &&
            !victim.IsNonCombatScout &&
            victim.TargetCombat is null)
        {
            if (victim.IsWorker() && TryTriggerWorkerDefenseOrEvade(victim, unit))
            {
                return;
            }

            if (victim.State == UnitState.Idle)
            {
                victim.TargetCombat = unit;
                victim.SetState(UnitState.Attack);
            }
        }
    }

    private void TickGather(SimUnit unit, double delta, double deltaMs)
    {
        var node = unit.TargetResource;
        if (node is null || !node.Alive)
        {
            if (unit.CargoAmount > 0)
            {
                ReturnToNearestHall(unit);
                return;
            }

            var desiredType = unit.DesiredResourceType ?? ResourceType.Gold;
            var newNode = FindNearestResource(unit, desiredType) ?? FindNearestResource(unit, desiredType == ResourceType.Gold ? ResourceType.Lumber : ResourceType.Gold);
            if (newNode is not null)
            {
                unit.TargetResource = newNode;
            }
            else
            {
                unit.SetState(UnitState.Idle);
            }

            return;
        }

        var reach = node.Radius + unit.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
        var distance = unit.Position.DistanceTo(node.Center);
        if (distance > reach)
        {
            if (unit.Path.Count == 0 || unit.PathRepathMs >= GameConstants.RepathIntervalMs)
            {
                Repath(unit, GetWorkerGatherPathTarget(unit, node), reach);
            }

            AdvanceWithRecovery(unit, delta);
            return;
        }

        unit.Path.Clear();
        unit.GatherAccumMs += deltaMs;
        if (unit.GatherAccumMs < GameConstants.GatherTimeMs)
        {
            return;
        }

        unit.GatherAccumMs = 0d;
        var gathered = node.Harvest(GameConstants.WorkerCarry);
        if (node.Type == ResourceType.Lumber && !node.Alive)
        {
            Map.SetWalkable(node.TilePosition.X, node.TilePosition.Y, true);
        }

        if (gathered <= 0)
        {
            return;
        }

        ResourceGathered?.Invoke(unit.Position, node.Type, gathered, unit.Side);
        unit.CargoType = node.Type;
        unit.CargoAmount = gathered;
        ReturnToNearestHall(unit);
    }

    private void TickReturnCargo(SimUnit unit, double delta)
    {
        if (unit.CargoType is null || unit.CargoAmount <= 0)
        {
            unit.CargoType = null;
            unit.CargoAmount = 0;
            unit.SetState(UnitState.Idle);
            return;
        }

        var hall = unit.ReturnBuilding is { Alive: true } returnBuilding ? returnBuilding : FindNearestHall(unit);
        if (hall is null)
        {
            unit.SetState(UnitState.Idle);
            return;
        }

        var reach = hall.Radius + unit.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
        var distance = unit.Position.DistanceTo(hall.Center);
        if (distance > reach)
        {
            if (unit.Path.Count == 0 || unit.PathRepathMs >= GameConstants.RepathIntervalMs)
            {
                Repath(unit, GetWorkerReturnPathTarget(unit, hall), reach);
            }

            AdvanceWithRecovery(unit, delta);
            return;
        }

        unit.Path.Clear();
        var depositAmount = unit.CargoAmount;
        Economy.Deposit(unit.Side, unit.CargoType.Value, depositAmount);
        ResourceDeposited?.Invoke(hall.Center, unit.CargoType.Value, depositAmount, unit.Side);
        unit.CargoAmount = 0;
        unit.CargoType = null;
        unit.ReturnBuilding = null;

        var previousNode = unit.TargetResource;
        if (previousNode is not null && previousNode.Alive)
        {
            if (unit.IsWorker())
            {
                unit.SaveWorkerGatherOrder(previousNode, unit.DesiredResourceType ?? previousNode.Type);
            }
            unit.SetState(UnitState.Gather);
            return;
        }

        var desiredType = unit.DesiredResourceType ?? ResourceType.Gold;
        var newNode = FindNearestResource(unit, desiredType) ?? FindNearestResource(unit, desiredType == ResourceType.Gold ? ResourceType.Lumber : ResourceType.Gold);
        if (newNode is not null)
        {
            unit.TargetResource = newNode;
            if (unit.IsWorker())
            {
                unit.SaveWorkerGatherOrder(newNode, desiredType);
            }
            unit.SetState(UnitState.Gather);
            return;
        }

        unit.SetState(UnitState.Idle);
    }

    private void TickBuild(SimUnit unit, double delta, double deltaMs)
    {
        var site = unit.TargetBuilding;
        if (site is null || !site.Alive || site.Completed)
        {
            unit.TargetBuilding = null;
            unit.SetState(UnitState.Idle);
            return;
        }

        var reach = site.Radius + unit.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
        var distance = unit.Position.DistanceTo(site.Center);
        if (distance > reach)
        {
            if (unit.Path.Count == 0 || unit.PathRepathMs >= GameConstants.RepathIntervalMs)
            {
                Repath(unit, site.Center, reach);
            }

            AdvanceWithRecovery(unit, delta);
            return;
        }

        unit.Path.Clear();
        if (!site.AddBuildProgress(deltaMs))
        {
            return;
        }

        Economy.AddCap(site.Side, GameDefinitions.Buildings[site.Kind].FoodCapBonus);
        BuildingCompleted?.Invoke(site.Center, site.Side);
        unit.TargetBuilding = null;
        if (unit.IsWorker())
        {
            unit.ClearWorkerPassiveOrder();
        }
        unit.SetState(UnitState.Idle);
    }

    private void TickBuildingAttack(SimBuilding building)
    {
        if (!building.CanAttack())
        {
            return;
        }

        var target = AcquireBuildingTarget(building);
        if (target is null || _elapsedMs - building.LastAttackMs < building.CooldownMs)
        {
            return;
        }

        building.LastAttackMs = _elapsedMs;
        ProjectileLaunched?.Invoke(building.Center + Vector2.Up * building.Radius * 0.45f, target.Position, building.Side, false, true);
        DealDamage(target, building.Attack, building, 0f);
    }

    private void DealDamage(ICombatTarget target, int amount, ICombatTarget source, float splashRadius)
    {
        if (!target.Alive || amount <= 0)
        {
            return;
        }

        if (splashRadius > 0f)
        {
            foreach (var other in FindSplashTargets(target, source.Side, splashRadius))
            {
                var distance = target.Position.DistanceTo(other.Position);
                var falloff = other == target ? 1f : Mathf.Clamp(1f - distance / (splashRadius * 1.25f), 0.35f, 0.65f);
                var bonus = other == target && other.IsBuilding && source is SimUnit { Kind: UnitKind.Catapult } catapult ? catapult.BonusVsBuilding : 0;
                var actual = Mathf.RoundToInt((amount + bonus) * falloff);
                ApplyDamage(other, actual, source);
            }

            return;
        }

        ApplyDamage(target, amount, source);
    }

    private void ApplyDamage(ICombatTarget target, int amount, ICombatTarget source)
    {
        if (!target.Alive || amount <= 0)
        {
            return;
        }

        target.TakeDamage(amount);
        if (source is SimUnit unitSource && unitSource.Side == GameSide.AI)
        {
            RegisterHarassTrade(unitSource, target, amount);
        }
        HitOccurred?.Invoke(target.Position, target.IsBuilding, amount);
        if (target.Side == GameSide.Player && source.Side == GameSide.AI)
        {
            UnderAttack?.Invoke(source.Position);
        }

        if (!target.Alive)
        {
            EntityDestroyed?.Invoke(target.Position, target.IsBuilding, target.Side);
        }
    }

    private List<ICombatTarget> FindSplashTargets(ICombatTarget primary, GameSide sourceSide, float radius)
    {
        var targets = new List<ICombatTarget> { primary };

        foreach (var unit in Units)
        {
            if (!unit.Alive || unit == primary || unit.Side == sourceSide)
            {
                continue;
            }

            if (primary.Position.DistanceTo(unit.Position) <= radius)
            {
                targets.Add(unit);
            }
        }

        foreach (var building in Buildings)
        {
            if (!building.Alive || building == primary || building.Side == sourceSide)
            {
                continue;
            }

            if (primary.Position.DistanceTo(building.Position) <= radius + building.Radius * 0.4f)
            {
                targets.Add(building);
            }
        }

        return targets;
    }

    private ICombatTarget? AcquireTarget(SimUnit unit, float range)
    {
        ICombatTarget? best = null;
        var bestScore = float.PositiveInfinity;

        foreach (var other in Units)
        {
            if (!other.Alive || other.Side == unit.Side)
            {
                continue;
            }

            var distance = unit.Position.DistanceTo(other.Position);
            if (distance > range)
            {
                continue;
            }

            var score = ScoreUnitTarget(unit, other, distance);
            if (score < bestScore)
            {
                best = other;
                bestScore = score;
            }
        }

        foreach (var building in Buildings)
        {
            if (!building.Alive || building.Side == unit.Side)
            {
                continue;
            }

            var distance = unit.Position.DistanceTo(building.Position);
            if (distance > range)
            {
                continue;
            }

            var score = ScoreBuildingTarget(unit, building, distance);
            if (score < bestScore)
            {
                best = building;
                bestScore = score;
            }
        }

        return best;
    }

    private void SwitchCombatTarget(SimUnit unit, ICombatTarget target)
    {
        unit.TargetCombat = target;
        unit.Path.Clear();
        unit.PathRepathMs = GameConstants.RepathIntervalMs;
        unit.SetState(UnitState.Attack);
    }

    private static float GetAttackRange(SimUnit unit, ICombatTarget target)
    {
        return unit.Range + target.Radius + unit.Radius + (target.IsBuilding ? GameConstants.TileSize * 0.65f : 0f);
    }

    private static float ScoreUnitTarget(SimUnit unit, SimUnit target, float distance)
    {
        var score = distance;
        if (target.CanAttack())
        {
            score -= unit.IsRanged() ? 72f : 52f;
        }
        else if (target.IsWorker())
        {
            score -= 28f;
        }

        if (unit.TargetCombat == target)
        {
            score -= 10f;
        }

        return score;
    }

    private static float ScoreBuildingTarget(SimUnit unit, SimBuilding building, float distance)
    {
        var score = distance + CombatBuildingTargetPenalty;
        score -= building.Kind switch
        {
            BuildingKind.Tower => 30f,
            BuildingKind.Workshop => 20f,
            BuildingKind.Barracks => 16f,
            BuildingKind.TownHall => 6f,
            _ => 10f
        };

        if (unit.IsSiege())
        {
            score -= 28f;
        }

        if (unit.TargetCombat == building)
        {
            score -= 8f;
        }

        return score;
    }

    private bool TryFindImmediateUnitRetarget(SimUnit unit, out SimUnit bestTarget)
    {
        bestTarget = null!;
        var bestScore = float.PositiveInfinity;
        var found = false;

        foreach (var other in Units)
        {
            if (!other.Alive || other.Side == unit.Side)
            {
                continue;
            }

            var distance = unit.Position.DistanceTo(other.Position);
            var ownEngageRange = GetAttackRange(unit, other) + CombatRetargetBuffer;
            var enemyThreatRange = other.CanAttack()
                ? GetAttackRange(other, unit) + CombatRetargetBuffer
                : unit.Radius + other.Radius + GameConstants.TileSize * 0.6f;
            if (distance > ownEngageRange && distance > enemyThreatRange)
            {
                continue;
            }

            var score = ScoreUnitTarget(unit, other, distance);
            if (other.TargetCombat == unit)
            {
                score -= 42f;
            }

            if (distance <= GetAttackRange(unit, other))
            {
                score -= 18f;
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = other;
                found = true;
            }
        }

        return found;
    }

    private ICombatTarget? AcquireBuildingTarget(SimBuilding building)
    {
        ICombatTarget? best = null;
        var bestDistance = float.PositiveInfinity;

        foreach (var unit in Units)
        {
            if (!unit.Alive || unit.Side == building.Side)
            {
                continue;
            }

            var distance = building.Position.DistanceTo(unit.Position);
            if (distance <= building.Range && distance < bestDistance)
            {
                best = unit;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void ReturnToNearestHall(SimUnit unit)
    {
        var hall = FindNearestHall(unit);
        if (hall is null)
        {
            unit.SetState(UnitState.Idle);
            return;
        }

        unit.ReturnBuilding = hall;
        unit.Path.Clear();
        var reach = hall.Radius + unit.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
        Repath(unit, GetWorkerReturnPathTarget(unit, hall), reach);
        unit.SetState(UnitState.ReturnCargo);
    }

    private bool TryTriggerWorkerDefenseOrEvade(SimUnit worker, SimUnit attacker)
    {
        if (!worker.IsWorker())
        {
            return false;
        }

        if (worker.WorkerPassiveOrderType == WorkerPassiveOrderType.Move)
        {
            return true;
        }

        if (worker.WorkerDefenseMode == WorkerDefenseMode.BaseDefenseCombat &&
            ShouldWorkerBreakCombatLeash(worker, attacker.Position))
        {
            StartWorkerEvade(worker, worker.WorkerAnchorHall ?? FindNearestHall(worker));
            return true;
        }

        if (worker.WorkerPassiveOrderType is not (WorkerPassiveOrderType.Gather or WorkerPassiveOrderType.Build or WorkerPassiveOrderType.ReturnCargo))
        {
            return false;
        }

        var hall = FindNearestHall(worker);
        if (hall is null)
        {
            return true;
        }

        var safeRadius = hall.Radius * GameConstants.WorkerSafeCombatHallRadiusMultiplier;
        if (worker.Position.DistanceTo(hall.Center) <= safeRadius)
        {
            StartWorkerBaseDefenseCombat(worker, hall, attacker);
            return true;
        }

        StartWorkerEvade(worker, hall);
        return true;
    }

    private void StartWorkerEvade(SimUnit worker, SimBuilding? hall)
    {
        if (!worker.IsWorker())
        {
            return;
        }

        worker.ClearOrders(false);
        worker.WorkerDefenseMode = WorkerDefenseMode.EvadeToHall;
        worker.WorkerThreatQuietMs = 0d;
        if (hall is null)
        {
            worker.WorkerAnchorHall = null;
            worker.SetState(UnitState.Idle);
            return;
        }

        worker.WorkerAnchorHall = hall;
        worker.WorkerSafeCombatRadius = hall.Radius * GameConstants.WorkerSafeCombatHallRadiusMultiplier;
        worker.WorkerCombatLeashRadius = hall.Radius * GameConstants.WorkerCombatLeashHallRadiusMultiplier;
        worker.ReturnBuilding = hall;
        var reach = hall.Radius + worker.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
        Repath(worker, GetWorkerReturnPathTarget(worker, hall), reach);
        worker.SetState(worker.Path.Count > 0 ? UnitState.Move : UnitState.Idle);
    }

    private void StartWorkerBaseDefenseCombat(SimUnit worker, SimBuilding hall, ICombatTarget attacker)
    {
        worker.ClearOrders(false);
        worker.WorkerDefenseMode = WorkerDefenseMode.BaseDefenseCombat;
        worker.WorkerAnchorHall = hall;
        worker.WorkerSafeCombatRadius = hall.Radius * GameConstants.WorkerSafeCombatHallRadiusMultiplier;
        worker.WorkerCombatLeashRadius = hall.Radius * GameConstants.WorkerCombatLeashHallRadiusMultiplier;
        worker.TargetCombat = attacker;
        worker.SetState(UnitState.Attack);
    }

    private void TickWorkerEvade(SimUnit worker, double delta, double deltaMs)
    {
        var hall = worker.WorkerAnchorHall is { Alive: true } anchor ? anchor : FindNearestHall(worker);
        if (hall is null)
        {
            worker.WorkerDefenseMode = WorkerDefenseMode.None;
            worker.SetState(UnitState.Idle);
            return;
        }

        worker.WorkerAnchorHall = hall;
        worker.WorkerSafeCombatRadius = hall.Radius * GameConstants.WorkerSafeCombatHallRadiusMultiplier;
        worker.WorkerCombatLeashRadius = hall.Radius * GameConstants.WorkerCombatLeashHallRadiusMultiplier;

        var reach = hall.Radius + worker.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
        var distance = worker.Position.DistanceTo(hall.Center);
        if (distance > reach)
        {
            if (worker.Path.Count == 0 || worker.PathRepathMs >= GameConstants.RepathIntervalMs)
            {
                Repath(worker, GetWorkerReturnPathTarget(worker, hall), reach);
            }

            AdvanceWithRecovery(worker, delta);
        }
        else
        {
            worker.Path.Clear();
        }

        if (IsThreatNearHall(worker, hall))
        {
            worker.WorkerThreatQuietMs = 0d;
            return;
        }

        worker.WorkerThreatQuietMs += deltaMs;
        if (worker.WorkerThreatQuietMs >= GameConstants.WorkerThreatQuietWindowMs)
        {
            ResumeWorkerPassiveOrder(worker);
        }
    }

    private void TickWorkerBaseDefenseCombat(SimUnit worker, double delta)
    {
        var hall = worker.WorkerAnchorHall;
        if (hall is null || !hall.Alive)
        {
            StartWorkerEvade(worker, FindNearestHall(worker));
            return;
        }

        var target = worker.TargetCombat;
        if (target is null || !target.Alive || target.Side == worker.Side || ShouldWorkerBreakCombatLeash(worker, target.Position))
        {
            StartWorkerEvade(worker, hall);
            return;
        }

        TickAttack(worker, delta);
        if (worker.State == UnitState.Idle || ShouldWorkerBreakCombatLeash(worker, worker.Position))
        {
            StartWorkerEvade(worker, hall);
        }
    }

    private bool IsThreatNearHall(SimUnit worker, SimBuilding hall)
    {
        var threatRadius = float.Max(
            hall.Radius + GameConstants.TileSize * GameConstants.WorkerThreatCheckRadiusTiles,
            worker.WorkerSafeCombatRadius + GameConstants.TileSize * 0.75f);
        foreach (var enemy in Units)
        {
            if (!enemy.Alive || enemy.Side == worker.Side)
            {
                continue;
            }

            if (enemy.Position.DistanceTo(hall.Center) <= threatRadius || enemy.Position.DistanceTo(worker.Position) <= GameConstants.TileSize * 3.5f)
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldWorkerBreakCombatLeash(SimUnit worker, Vector2 targetPosition)
    {
        var hall = worker.WorkerAnchorHall;
        if (hall is null || !hall.Alive)
        {
            return true;
        }

        return worker.Position.DistanceTo(hall.Center) > worker.WorkerCombatLeashRadius ||
               targetPosition.DistanceTo(hall.Center) > worker.WorkerCombatLeashRadius;
    }

    private void ResumeWorkerPassiveOrder(SimUnit worker)
    {
        worker.WorkerDefenseMode = WorkerDefenseMode.None;
        worker.WorkerThreatQuietMs = 0d;
        worker.TargetCombat = null;

        switch (worker.WorkerPassiveOrderType)
        {
            case WorkerPassiveOrderType.Move:
                if (worker.WorkerSavedMoveTarget.HasValue)
                {
                    worker.ClearOrders(false);
                    Repath(worker, worker.WorkerSavedMoveTarget.Value, 0f);
                    worker.SetState(worker.Path.Count > 0 ? UnitState.Move : UnitState.Idle);
                    return;
                }

                break;

            case WorkerPassiveOrderType.Gather:
                worker.ClearOrders(false);
                var resource = worker.WorkerSavedResource;
                if (resource is not null && resource.Alive)
                {
                    worker.TargetResource = resource;
                    worker.DesiredResourceType = worker.WorkerSavedDesiredResourceType ?? resource.Type;
                    worker.SetState(UnitState.Gather);
                    return;
                }

                var desiredType = worker.WorkerSavedDesiredResourceType ?? ResourceType.Gold;
                var fallbackResource = FindNearestResource(worker, desiredType) ??
                    FindNearestResource(worker, desiredType == ResourceType.Gold ? ResourceType.Lumber : ResourceType.Gold);
                if (fallbackResource is not null)
                {
                    worker.TargetResource = fallbackResource;
                    worker.DesiredResourceType = desiredType;
                    worker.SaveWorkerGatherOrder(fallbackResource, desiredType);
                    worker.SetState(UnitState.Gather);
                    return;
                }

                break;

            case WorkerPassiveOrderType.Build:
                var site = worker.WorkerSavedBuildTarget;
                if (site is not null && site.Alive && !site.Completed)
                {
                    worker.ClearOrders(false);
                    worker.TargetBuilding = site;
                    var buildReach = site.Radius + worker.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
                    Repath(worker, site.Center, buildReach);
                    worker.SetState(UnitState.Build);
                    return;
                }

                break;

            case WorkerPassiveOrderType.ReturnCargo:
                if (worker.CargoType is not null && worker.CargoAmount > 0)
                {
                    var hall = worker.WorkerSavedReturnHall is { Alive: true } savedHall ? savedHall : FindNearestHall(worker);
                    if (hall is not null)
                    {
                        worker.ClearOrders(false);
                        worker.ReturnBuilding = hall;
                        var returnReach = hall.Radius + worker.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
                        Repath(worker, GetWorkerReturnPathTarget(worker, hall), returnReach);
                        worker.SetState(UnitState.ReturnCargo);
                        return;
                    }
                }

                if (worker.WorkerSavedResource is not null || worker.WorkerSavedDesiredResourceType.HasValue)
                {
                    if (worker.WorkerSavedResource is { Alive: true } savedResource)
                    {
                        worker.SaveWorkerGatherOrder(savedResource, worker.WorkerSavedDesiredResourceType ?? savedResource.Type);
                        ResumeWorkerPassiveOrder(worker);
                        return;
                    }

                    var gatherType = worker.WorkerSavedDesiredResourceType ?? ResourceType.Gold;
                    var gatherFallback = FindNearestResource(worker, gatherType) ??
                        FindNearestResource(worker, gatherType == ResourceType.Gold ? ResourceType.Lumber : ResourceType.Gold);
                    if (gatherFallback is not null)
                    {
                        worker.SaveWorkerGatherOrder(gatherFallback, gatherType);
                        ResumeWorkerPassiveOrder(worker);
                        return;
                    }
                }

                break;
        }

        worker.ClearOrders();
    }

    private bool Repath(SimUnit unit, Vector2 worldTarget, float arrivalRadius, bool stuckReroute = false, bool preserveExistingPathOnFailure = false)
    {
        var start = Map.WorldToTile(unit.Position);
        var goal = Map.WorldToTile(worldTarget);
        var goalRadiusTiles = int.Max(0, Mathf.CeilToInt(arrivalRadius / GameConstants.TileSize));
        var allowStartAsGoal = arrivalRadius <= 0f || unit.Position.DistanceTo(worldTarget) <= arrivalRadius + 0.5f;
        var previousPath = preserveExistingPathOnFailure ? new List<Vector2>(unit.Path) : null;
        var previousDestination = unit.PathDestination;
        var tilePenalty = BuildDynamicTilePenalty(unit, goal, goalRadiusTiles, stuckReroute);
        var tilePath = Pathfinder.FindPath(
            Map,
            start,
            goal,
            goalRadiusTiles,
            unit.Id % 8,
            tilePenalty,
            allowStartAsGoal);
        if (tilePath.Count == 0)
        {
            if (!allowStartAsGoal &&
                TryBuildCloseRangeFallbackPath(unit, worldTarget, arrivalRadius, out var fallbackPath))
            {
                unit.SetPath(fallbackPath);
                unit.PathDestination = worldTarget;
                unit.PathRepathMs = 0d;
                return true;
            }

            if (preserveExistingPathOnFailure && previousPath is not null)
            {
                unit.SetPath(previousPath);
                unit.PathDestination = previousDestination;
                return false;
            }

            unit.SetPath(Array.Empty<Vector2>());
            unit.PathDestination = worldTarget;
            unit.PathRepathMs = 0d;
            return false;
        }

        var worldPath = new List<Vector2>(tilePath.Count);
        foreach (var point in tilePath)
        {
            worldPath.Add(Map.TileToWorldCenter(point.X, point.Y));
        }

        unit.SetPath(worldPath);
        unit.PathDestination = worldPath.Count > 0 ? worldPath[^1] : worldTarget;
        unit.PathRepathMs = 0d;
        if (stuckReroute)
        {
            unit.LastHeavyRerouteMs = _elapsedMs;
        }

        return true;
    }

    private bool TryBuildCloseRangeFallbackPath(SimUnit unit, Vector2 worldTarget, float arrivalRadius, out List<Vector2> path)
    {
        path = [];
        if (arrivalRadius <= 0f)
        {
            return false;
        }

        var distance = unit.Position.DistanceTo(worldTarget);
        if (distance > arrivalRadius + GameConstants.TileSize * 1.4f)
        {
            return false;
        }

        var direction = unit.Position - worldTarget;
        if (direction.LengthSquared() <= 0.01f)
        {
            direction = Vector2.Right;
        }

        var stopDistance = Mathf.Max(arrivalRadius * 0.9f, unit.Radius + 2f);
        var candidate = worldTarget + direction.Normalized() * stopDistance;
        if (!TryMoveToCandidate(unit, candidate, 1.5f))
        {
            return false;
        }

        path.Add(candidate);
        return true;
    }

    private Dictionary<int, float> BuildDynamicTilePenalty(SimUnit unit, Vector2I goal, int goalRadiusTiles, bool stuckReroute)
    {
        var penalty = new Dictionary<int, float>();
        var goalWorld = Map.TileToWorldCenter(goal.X, goal.Y);
        foreach (var other in Units)
        {
            if (!other.Alive || other == unit)
            {
                continue;
            }

            if (other.Side != unit.Side)
            {
                continue;
            }

            var occupied = Map.WorldToTile(other.Position);
            if (!Map.InBounds(occupied.X, occupied.Y))
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
                        if ((dx == 0 && dy == 0) || !Map.InBounds(occupied.X + dx, occupied.Y + dy))
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
                    if ((dx == 0 && dy == 0) || !Map.InBounds(occupied.X + dx, occupied.Y + dy))
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
        if (SharesCombatTarget(mover, other))
        {
            return false;
        }

        if (!IsLikelyStaticBlocker(other))
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

    private bool AdvanceAlongPathWithSteering(SimUnit unit, double delta)
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
        foreach (var other in Units)
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

    private bool TryBuildCombatApproachTarget(SimUnit unit, ICombatTarget target, out Vector2 approachTarget, out float arrivalRadius)
    {
        if (unit.IsRanged() || unit.IsSiege())
        {
            var rangedSlot = BuildRangedCombatApproachSlot(unit, target);
            approachTarget = rangedSlot.Target;
            arrivalRadius = rangedSlot.ArrivalRadius;
            return true;
        }

        var meleeSlot = BuildMeleeCombatApproachSlot(unit, target);
        approachTarget = meleeSlot.Target;
        arrivalRadius = meleeSlot.ArrivalRadius;
        return true;
    }

    private CombatApproachSlot BuildMeleeCombatApproachSlot(SimUnit unit, ICombatTarget target)
    {
        var forward = GetApproachDirection(unit, target);
        var lateral = new Vector2(-forward.Y, forward.X);
        var contactDistance = target.Radius + unit.Radius + Mathf.Max(unit.Range * 0.3f, 4f);
        var contactCenter = target.Position + forward * contactDistance;
        var contactSlots = target.IsBuilding
            ? Mathf.Clamp(Mathf.RoundToInt(target.Radius / 12f) + 2, 3, 6)
            : Mathf.Clamp(Mathf.RoundToInt(target.Radius / 10f) + 1, 2, 4);
        var rows = target.IsBuilding ? 3 : 2;
        var assignment = Mathf.PosMod(unit.Id, contactSlots * rows);
        var lane = CenteredSlotIndex(assignment % contactSlots);
        var rank = assignment / contactSlots;
        var laneSpacing = unit.Radius * 2f + 4f;
        var followSpacing = unit.Radius * 2.1f + 6f;
        var offset = lateral * (lane * laneSpacing) + forward * (rank * followSpacing);
        var targetPoint = contactCenter + offset;
        var arrival = Mathf.Max(unit.Radius * 0.6f, 7f);
        return new CombatApproachSlot(targetPoint, arrival);
    }

    private CombatApproachSlot BuildRangedCombatApproachSlot(SimUnit unit, ICombatTarget target)
    {
        var forward = GetApproachDirection(unit, target);
        var baseAngle = Mathf.Atan2(forward.Y, forward.X);
        var desiredRadius = target.Radius + unit.Radius + Mathf.Max(unit.Range * 0.52f, GameConstants.TileSize * 0.35f);
        var slotCount = target.IsBuilding ? 4 : 3;
        var spread = target.IsBuilding ? 0.5f : 0.32f;
        var ordinal = Mathf.PosMod(unit.Id, slotCount);
        var t = slotCount == 1 ? 0.5f : ordinal / (float)(slotCount - 1);
        var angle = baseAngle - spread * 0.5f + spread * t;
        var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * desiredRadius;
        var targetPoint = target.Position + offset;
        var arrival = Mathf.Max(unit.Radius * 0.7f, 8f);
        return new CombatApproachSlot(targetPoint, arrival);
    }

    private static Vector2 GetApproachDirection(SimUnit unit, ICombatTarget target)
    {
        var direction = unit.Position - target.Position;
        if (direction.LengthSquared() <= 1f)
        {
            var fallbackAngle = Mathf.Tau * (Mathf.PosMod(unit.Id, 8) / 8f);
            return new Vector2(Mathf.Cos(fallbackAngle), Mathf.Sin(fallbackAngle));
        }

        return direction.Normalized();
    }

    private static int CenteredSlotIndex(int ordinal)
    {
        if (ordinal == 0)
        {
            return 0;
        }

        var step = (ordinal + 1) / 2;
        return ordinal % 2 == 1 ? -step : step;
    }

    private bool TryMoveToCandidate(SimUnit unit, Vector2 candidate, float padding, SimUnit? ignoredUnit = null)
    {
        var tile = Map.WorldToTile(candidate);
        if (!Map.IsWalkable(tile.X, tile.Y))
        {
            return false;
        }

        foreach (var other in Units)
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

        return true;
    }

    private bool AdvanceWithRecovery(SimUnit unit, double delta)
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
        var hasPath = AdvanceAlongPathWithSteering(unit, delta);
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
            ResolveLocalStuck(unit, allowHeavyReroute);
        }

        return hasPath;
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

    private void ResolveLocalStuck(SimUnit unit, bool allowHeavyReroute)
    {
        unit.StuckAccumMs = 0d;
        unit.PathProgressStallMs = 0d;
        if (TryLocalAvoidanceStep(unit))
        {
            return;
        }

        if (TryGetRepathTarget(unit, out var repathTarget, out var arrivalRadius))
        {
            if (Repath(unit, repathTarget, arrivalRadius, preserveExistingPathOnFailure: true))
            {
                return;
            }

            if (allowHeavyReroute && _elapsedMs - unit.LastHeavyRerouteMs >= HeavyRerouteCooldownMs)
            {
                Repath(unit, repathTarget, arrivalRadius, stuckReroute: true, preserveExistingPathOnFailure: true);
            }
        }
    }

    private bool TryGetRepathTarget(SimUnit unit, out Vector2 target, out float arrivalRadius)
    {
        if (unit.TargetCombat is { Alive: true } combat)
        {
            if (TryBuildCombatApproachTarget(unit, combat, out target, out arrivalRadius))
            {
                return true;
            }

            target = combat.Position;
            arrivalRadius = GetAttackRange(unit, combat);
            return true;
        }

        if (unit.TargetResource is { Alive: true } resource)
        {
            target = GetWorkerGatherPathTarget(unit, resource);
            arrivalRadius = resource.Radius + unit.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
            return true;
        }

        if (unit.ReturnBuilding is { Alive: true } hall)
        {
            target = GetWorkerReturnPathTarget(unit, hall);
            arrivalRadius = hall.Radius + unit.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
            return true;
        }

        if (unit.TargetBuilding is { Alive: true } site)
        {
            target = site.Center;
            arrivalRadius = site.Radius + unit.Radius + GameConstants.TileSize * GameConstants.GatherReachPaddingTiles;
            return true;
        }

        if (unit.PathDestination.HasValue)
        {
            target = unit.PathDestination.Value;
            arrivalRadius = 0f;
            return true;
        }

        target = Vector2.Zero;
        arrivalRadius = 0f;
        return false;
    }

    private Vector2 GetWorkerGatherPathTarget(SimUnit unit, SimResourceNode node)
    {
        if (!unit.IsWorker())
        {
            return node.Center;
        }

        var hall = unit.ReturnBuilding is { Alive: true } returnBuilding ? returnBuilding : FindNearestHall(unit);
        if (hall is null)
        {
            return node.Center;
        }

        return TryBuildWorkerFlowTarget(unit.Id, hall.Center, hall.Radius, node.Center, node.Radius, approachingHall: false, out var target)
            ? target
            : node.Center;
    }

    private Vector2 GetWorkerReturnPathTarget(SimUnit unit, SimBuilding hall)
    {
        if (!unit.IsWorker())
        {
            return hall.Center;
        }

        if (unit.TargetResource is not { Alive: true } node)
        {
            return hall.Center;
        }

        return TryBuildWorkerFlowTarget(unit.Id, hall.Center, hall.Radius, node.Center, node.Radius, approachingHall: true, out var target)
            ? target
            : hall.Center;
    }

    private static bool TryBuildWorkerFlowTarget(
        int unitId,
        Vector2 hallCenter,
        float hallRadius,
        Vector2 nodeCenter,
        float nodeRadius,
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
        var laneIndex = unitId % 5 - 2;
        var laneOffset = perpendicular * (laneIndex * (GameConstants.WorkerFlowLaneOffset * 0.55f));

        if (approachingHall)
        {
            var depth = Mathf.Max(GameConstants.TileSize * 0.35f, hallRadius * 0.62f) + Mathf.Abs(laneIndex) * 1.5f;
            target = hallCenter + routeDirection * depth - laneOffset;
            return true;
        }

        var depthToNode = Mathf.Max(GameConstants.TileSize * 0.35f, nodeRadius * 0.58f) + Mathf.Abs(laneIndex) * 1.2f;
        target = nodeCenter - routeDirection * depthToNode + laneOffset;
        return true;
    }

    private bool TryLocalAvoidanceStep(SimUnit unit)
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

    private bool TryMoveIntoFreeSpace(SimUnit unit, Vector2 offset)
    {
        var candidate = unit.Position + offset;
        if (!TryMoveToCandidate(unit, candidate, 2f))
        {
            return false;
        }

        unit.Position = candidate;
        return true;
    }

    private static void AddTilePenalty(Dictionary<int, float> penalty, int tx, int ty, float amount)
    {
        var key = ty * GameConstants.MapWidth + tx;
        penalty[key] = penalty.GetValueOrDefault(key) + amount;
    }

    private void ApplySeparation(double delta)
    {
        var strength = Math.Min(1d, delta / 0.01667d) * 0.55d;
        for (var i = 0; i < Units.Count; i++)
        {
            var first = Units[i];
            if (!first.Alive)
            {
                continue;
            }

            for (var j = i + 1; j < Units.Count; j++)
            {
                var second = Units[j];
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
            if (!TryMoveIntoFreeSpace(yielder, offset))
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
        if (!TryMoveToCandidate(unit, next, 1f, ignoredUnit))
        {
            return;
        }

        unit.Position = next;
    }

    private void FinishProduction(SimBuilding building, UnitKind kind)
    {
        var definition = GameDefinitions.Units[kind];
        if (!Economy.HasFoodRoom(building.Side, definition.Food))
        {
            Refund(building.Side, definition.CostGold, definition.CostLumber);
            return;
        }

        var spawnPosition = FindSpawnPoint(building);
        if (!spawnPosition.HasValue)
        {
            Refund(building.Side, definition.CostGold, definition.CostLumber);
            return;
        }

        var unit = SpawnUnit(spawnPosition.Value, kind, building.Side, building.Race);
        if (building.RallyPoint.HasValue)
        {
            IssueMove(unit, building.RallyPoint.Value);
        }
    }

    private void Refund(GameSide side, int gold, int lumber)
    {
        if (gold > 0)
        {
            Economy.Deposit(side, ResourceType.Gold, gold);
        }

        if (lumber > 0)
        {
            Economy.Deposit(side, ResourceType.Lumber, lumber);
        }
    }

    private Vector2? FindSpawnPoint(SimBuilding building)
    {
        var start = building.CenterTile();
        for (var radius = building.SizeTiles; radius <= building.SizeTiles + 8; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    var tx = start.X + dx;
                    var ty = start.Y + dy;
                    if (!Map.IsWalkable(tx, ty))
                    {
                        continue;
                    }

                    var world = Map.TileToWorldCenter(tx, ty);
                    if (Units.Exists(unit => unit.Alive && unit.Position.DistanceTo(world) < unit.Radius + 16f))
                    {
                        continue;
                    }

                    return world;
                }
            }
        }

        return null;
    }

    private void PruneDead()
    {
        Units.RemoveAll(unit =>
        {
            if (unit.Alive)
            {
                return false;
            }

            Economy.RemoveFood(unit.Side, unit.Food);
            return true;
        });

        Buildings.RemoveAll(building =>
        {
            if (building.Alive)
            {
                return false;
            }

            var definition = GameDefinitions.Buildings[building.Kind];
            if (building.Completed && definition.FoodCapBonus > 0)
            {
                Economy.RemoveCap(building.Side, definition.FoodCapBonus);
            }

            for (var dy = 0; dy < building.SizeTiles; dy++)
            {
                for (var dx = 0; dx < building.SizeTiles; dx++)
                {
                    Map.SetWalkable(building.TilePosition.X + dx, building.TilePosition.Y + dy, true);
                }
            }

            return true;
        });
    }

    private void CheckVictory()
    {
        var playerHasBase = Buildings.Exists(building => building.Alive && building.Side == GameSide.Player);
        var aiHasBase = Buildings.Exists(building => building.Alive && building.Side == GameSide.AI);
        if (!aiHasBase && playerHasBase)
        {
            Winner = GameSide.Player;
            GameOverResolved?.Invoke(Winner.Value);
        }
        else if (!playerHasBase && aiHasBase)
        {
            Winner = GameSide.AI;
            GameOverResolved?.Invoke(Winner.Value);
        }
    }

    private void RunAi()
    {
        var buildings = Buildings.FindAll(building => building.Alive && building.Side == GameSide.AI);
        var units = Units.FindAll(unit => unit.Alive && unit.Side == GameSide.AI);
        var workers = units.FindAll(unit => unit.Kind == UnitKind.Worker);
        var army = units.FindAll(unit => unit.Kind != UnitKind.Worker);
        var economy = Economy.Get(GameSide.AI);
        var hall = buildings.Find(building => building.Kind == BuildingKind.TownHall && building.Completed);
        if (hall is null)
        {
            return;
        }

        UpdateAiMemory(units, buildings);
        AssignIdleWorkers(workers);
        var hasBarracks = buildings.Exists(building => building.Kind == BuildingKind.Barracks && building.Completed);
        var hasWorkshop = buildings.Exists(building => building.Kind == BuildingKind.Workshop && building.Completed);
        var barracks = buildings.Find(building => building.Kind == BuildingKind.Barracks && building.Completed);
        var workshop = buildings.Find(building => building.Kind == BuildingKind.Workshop && building.Completed);
        var armyMetrics = CalculateSquadMetrics(army);
        var knownEnemyPower = EstimateKnownEnemyPower();
        var pressure = KnownEnemyPressureNear(hall.Center, GameConstants.TileSize * _difficultyDefinition.DefendRadiusTiles);
        var confirmedBase = _aiMemory.LastKnownPlayerBase;
        var suspectedBase = confirmedBase ?? Map.TileToWorldCenter(Layout.PlayerBase.X, Layout.PlayerBase.Y);
        var allowHarassSplit = ShouldUseHarassSplit(hasBarracks, confirmedBase.HasValue, pressure, armyMetrics, knownEnemyPower);
        BuildAiSquads(army, allowHarassSplit, out var mainArmy, out var harassSquad);
        var mainMetrics = CalculateSquadMetrics(mainArmy);
        var harassMetrics = CalculateSquadMetrics(harassSquad);
        var nextState = DetermineAiState(
            hall,
            hasBarracks,
            armyMetrics,
            mainMetrics,
            harassMetrics,
            confirmedBase.HasValue,
            pressure,
            knownEnemyPower);
        if (nextState != _aiState)
        {
            _aiState = nextState;
            _aiStateEnteredMs = _elapsedMs;
        }

        if (_aiState != AiState.Scout || confirmedBase.HasValue)
        {
            _aiWorkerScoutId = null;
        }

        if (_aiState != AiState.Scout)
        {
            ResetScoutMission();
        }

        if (_aiState == AiState.Harass)
        {
            SyncHarassMissionMembers(harassSquad, units);
        }
        else
        {
            ResetHarassMission(preserveHistory: true);
        }

        MaintainAiEconomy(
            hall,
            workers,
            buildings,
            economy,
            hasBarracks,
            hasWorkshop,
            pressure,
            mainMetrics,
            barracks,
            workshop);
        ExecuteAiState(hall, suspectedBase, mainArmy, mainMetrics, harassSquad, harassMetrics, pressure);
        ApplyAiMicro(mainArmy, mainMetrics, harassSquad, harassMetrics, hall, pressure);
    }

    private void AssignIdleWorkers(List<SimUnit> workers)
    {
        var goldWorkers = 0;
        var lumberWorkers = 0;
        foreach (var worker in workers)
        {
            if (worker.TargetResource?.Type == ResourceType.Gold)
            {
                goldWorkers++;
            }
            else if (worker.TargetResource?.Type == ResourceType.Lumber)
            {
                lumberWorkers++;
            }
        }

        foreach (var worker in workers)
        {
            if (worker.State != UnitState.Idle && !(worker.State == UnitState.Gather && worker.TargetResource is null))
            {
                continue;
            }

            var type = goldWorkers < lumberWorkers + 2 ? ResourceType.Gold : ResourceType.Lumber;
            var resource = FindNearestResource(worker, type) ?? FindNearestResource(worker, type == ResourceType.Gold ? ResourceType.Lumber : ResourceType.Gold);
            if (resource is null)
            {
                continue;
            }

            IssueGather(worker, resource);
            if (resource.Type == ResourceType.Gold)
            {
                goldWorkers++;
            }
            else
            {
                lumberWorkers++;
            }
        }
    }

    private bool TryBuildAi(BuildingKind kind, SimBuilding hall, List<SimUnit> workers, int preferredRadius)
    {
        if (workers.Count == 0)
        {
            return false;
        }

        var spot = FindBuildSpot(kind, hall, preferredRadius);
        if (!spot.HasValue || !TryStartBuilding(GameSide.AI, AIRace, kind, spot.Value, out var building) || building is null)
        {
            return false;
        }

        var worker = workers.Find(candidate => candidate.State is UnitState.Gather or UnitState.Idle) ?? workers[0];
        IssueBuild(worker, building);
        return true;
    }

    private Vector2I? FindBuildSpot(BuildingKind kind, SimBuilding hall, int preferredRadius)
    {
        var center = hall.CenterTile();
        for (var radius = preferredRadius; radius < 15; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    var tx = center.X + dx;
                    var ty = center.Y + dy;
                    if (CanPlaceBuilding(kind, new Vector2I(tx, ty)))
                    {
                        return new Vector2I(tx, ty);
                    }
                }
            }
        }

        return null;
    }

    private bool IsBuildingUnderConstruction(BuildingKind kind)
    {
        return Buildings.Exists(building => building.Alive && building.Side == GameSide.AI && building.Kind == kind && !building.Completed);
    }

    private bool NearbyTower(List<SimBuilding> buildings, SimBuilding hall)
    {
        return buildings.Exists(building => building.Kind == BuildingKind.Tower && building.Center.DistanceTo(hall.Center) < GameConstants.TileSize * 10f);
    }

    private void UpdateAiMemory(List<SimUnit> aiUnits, List<SimBuilding> aiBuildings)
    {
        var visibleUnitIds = new HashSet<int>();
        var visibleBuildingIds = new HashSet<int>();
        var contact = false;

        foreach (var unit in Units)
        {
            if (!unit.Alive || unit.Side != GameSide.Player || !CanAiSeePosition(aiUnits, aiBuildings, unit.Position, unit.Radius))
            {
                continue;
            }

            _aiMemory.Units[unit.Id] = new AiKnownUnit(
                unit.Id,
                unit.Kind,
                unit.Position,
                unit.Score * (unit.Hp / (float)unit.MaxHp),
                _elapsedMs);
            visibleUnitIds.Add(unit.Id);
            contact = true;
        }

        foreach (var building in Buildings)
        {
            if (!building.Alive || building.Side != GameSide.Player || !CanAiSeePosition(aiUnits, aiBuildings, building.Center, building.Radius))
            {
                continue;
            }

            var centerTile = building.CenterTile();
            _aiMemory.Buildings[building.Id] = new AiKnownBuilding(
                building.Id,
                building.Kind,
                building.Center,
                centerTile,
                building.MaxHp,
                _elapsedMs);
            visibleBuildingIds.Add(building.Id);
            contact = true;

            if (building.Kind == BuildingKind.TownHall || !_aiMemory.LastKnownPlayerBase.HasValue)
            {
                _aiMemory.LastKnownPlayerBase = building.Center;
                _aiMemory.LastKnownPlayerBaseTile = centerTile;
            }
        }

        if (contact)
        {
            _aiMemory.LastContactMs = _elapsedMs;
        }

        CleanupAiMemory(aiUnits, aiBuildings, visibleUnitIds, visibleBuildingIds);
    }

    private void CleanupAiMemory(
        List<SimUnit> aiUnits,
        List<SimBuilding> aiBuildings,
        HashSet<int> visibleUnitIds,
        HashSet<int> visibleBuildingIds)
    {
        var staleUnits = new List<int>();
        foreach (var pair in _aiMemory.Units)
        {
            if (visibleUnitIds.Contains(pair.Key))
            {
                continue;
            }

            if (!Units.Exists(unit => unit.Alive && unit.Id == pair.Key) &&
                CanAiSeePosition(aiUnits, aiBuildings, pair.Value.Position, GameConstants.TileSize * 0.4f))
            {
                staleUnits.Add(pair.Key);
            }
        }

        foreach (var id in staleUnits)
        {
            _aiMemory.Units.Remove(id);
        }

        var staleBuildings = new List<int>();
        foreach (var pair in _aiMemory.Buildings)
        {
            if (visibleBuildingIds.Contains(pair.Key))
            {
                continue;
            }

            if (!Buildings.Exists(building => building.Alive && building.Id == pair.Key) &&
                CanAiSeePosition(aiUnits, aiBuildings, pair.Value.Position, GameConstants.TileSize))
            {
                staleBuildings.Add(pair.Key);
            }
        }

        foreach (var id in staleBuildings)
        {
            if (_aiMemory.Buildings.TryGetValue(id, out var removed) && removed.Kind == BuildingKind.TownHall)
            {
                _aiMemory.LastKnownPlayerBase = null;
                _aiMemory.LastKnownPlayerBaseTile = null;
            }

            _aiMemory.Buildings.Remove(id);
        }
    }

    private bool CanAiSeePosition(List<SimUnit> aiUnits, List<SimBuilding> aiBuildings, Vector2 position, float padding)
    {
        foreach (var unit in aiUnits)
        {
            if (unit.Position.DistanceTo(position) <= unit.Sight * GameConstants.TileSize + padding)
            {
                return true;
            }
        }

        foreach (var building in aiBuildings)
        {
            if (building.Center.DistanceTo(position) <= building.Sight * GameConstants.TileSize + padding)
            {
                return true;
            }
        }

        return false;
    }

    private AiState DetermineAiState(
        SimBuilding hall,
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

        if (_aiState == AiState.Harass && (ShouldFinish(armyMetrics, knownEnemyPower) || ShouldPush(armyMetrics, knownEnemyPower)))
        {
            return AiState.Regroup;
        }

        if (_aiState == AiState.Scout && ShouldContinueScoutMission(baseConfirmed))
        {
            return AiState.Scout;
        }

        if (ShouldFinish(armyMetrics, knownEnemyPower))
        {
            return AiState.Finish;
        }

        if (!baseConfirmed && _elapsedMs >= _difficultyDefinition.ScoutDelayMs)
        {
            return AiState.Scout;
        }

        if (!hasBarracks || _elapsedMs < _difficultyDefinition.ScoutDelayMs)
        {
            return AiState.Open;
        }

        if (_aiState == AiState.Push || _aiState == AiState.Finish)
        {
            return ShouldRetreat(armyMetrics, knownEnemyPower) ? AiState.Regroup : _aiState;
        }

        if (_aiState == AiState.Harass && harassMetrics.Count > 0 && _elapsedMs - _aiStateEnteredMs < 5200d)
        {
            return AiState.Harass;
        }

        if (_aiState == AiState.Regroup && _elapsedMs - _aiStateEnteredMs < _difficultyDefinition.RegroupDurationMs)
        {
            return AiState.Regroup;
        }

        if (Init.AiProfile == AiProfile.Harass && CanLaunchHarass(mainMetrics, harassMetrics))
        {
            return AiState.Harass;
        }

        if (ShouldPush(armyMetrics, knownEnemyPower))
        {
            return AiState.Push;
        }

        return armyMetrics.Count >= 3 ? AiState.Regroup : AiState.Boom;
    }

    private bool ShouldContinueScoutMission(bool baseConfirmed)
    {
        if (!_aiScoutMission.Active)
        {
            return false;
        }

        if (!baseConfirmed)
        {
            return true;
        }

        if (_elapsedMs - _aiScoutMission.ConfirmedBaseMs <= ScoutContinueAfterConfirmMs)
        {
            return true;
        }

        if (_aiScoutMission.ScoutUnitId is not int scoutId)
        {
            return false;
        }

        var scout = Units.Find(unit => unit.Alive && unit.Side == GameSide.AI && unit.Id == scoutId);
        if (scout is null)
        {
            return false;
        }

        if (_aiScoutMission.Phase is ScoutMissionPhase.BreakContact or ScoutMissionPhase.Reposition)
        {
            return true;
        }

        if (_elapsedMs - _aiScoutMission.LastThreatMs < _difficultyDefinition.ScoutReentryDelayMs)
        {
            return true;
        }

        var safeDistance = scout.Sight * GameConstants.TileSize;
        if (scout.Position.DistanceTo(_aiScoutMission.BasePosition) <= safeDistance)
        {
            return true;
        }

        return EstimateScoutSectorThreat(scout.Position) > _difficultyDefinition.ScoutThreatTolerance * 0.45f;
    }

    private bool ShouldUseHarassSplit(
        bool hasBarracks,
        bool baseConfirmed,
        bool pressure,
        AiSquadMetrics armyMetrics,
        float knownEnemyPower)
    {
        if (Init.AiProfile != AiProfile.Harass || pressure || !hasBarracks || !baseConfirmed)
        {
            return false;
        }

        if (_aiState is AiState.Push or AiState.Finish or AiState.Regroup)
        {
            return false;
        }

        return !ShouldFinish(armyMetrics, knownEnemyPower) && !ShouldPush(armyMetrics, knownEnemyPower);
    }

    private bool ShouldPush(AiSquadMetrics mainMetrics, float knownEnemyPower)
    {
        if (mainMetrics.Count == 0 || mainMetrics.FrontlineCount == 0 || mainMetrics.Power < _difficultyDefinition.PushMinPower)
        {
            return false;
        }

        if (knownEnemyPower <= 0.25f)
        {
            return true;
        }

        return mainMetrics.Power >= knownEnemyPower * _difficultyDefinition.AttackAdvantageRatio;
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

        return knownEnemyPower > 0.25f && mainMetrics.Power <= knownEnemyPower * _difficultyDefinition.RetreatRatio;
    }

    private bool ShouldFinish(AiSquadMetrics mainMetrics, float knownEnemyPower)
    {
        var hasKnownTownHall = false;
        foreach (var building in _aiMemory.Buildings.Values)
        {
            if (building.Kind == BuildingKind.TownHall && IsFreshEnemyMemory(building.LastSeenMs))
            {
                hasKnownTownHall = true;
                break;
            }
        }

        if (!hasKnownTownHall && _aiMemory.LastKnownPlayerBase.HasValue && _aiMemory.Buildings.Count <= 2 && mainMetrics.Power >= knownEnemyPower + 4f)
        {
            return true;
        }

        return mainMetrics.Power >= Mathf.Max(_difficultyDefinition.PushMinPower + 2f, knownEnemyPower * 1.75f) && _aiMemory.LastKnownPlayerBase.HasValue;
    }

    private bool CanLaunchHarass(AiSquadMetrics mainMetrics, AiSquadMetrics harassMetrics)
    {
        if (Init.AiProfile != AiProfile.Harass || harassMetrics.Count == 0 || !_aiMemory.LastKnownPlayerBase.HasValue)
        {
            return false;
        }

        if (_elapsedMs - _aiLastHarassCommandMs < 6500d)
        {
            return false;
        }

        return harassMetrics.Power >= _difficultyDefinition.HarassMinPower && mainMetrics.Count >= 3;
    }

    private void MaintainAiEconomy(
        SimBuilding hall,
        List<SimUnit> workers,
        List<SimBuilding> buildings,
        PlayerState economy,
        bool hasBarracks,
        bool hasWorkshop,
        bool pressure,
        AiSquadMetrics mainMetrics,
        SimBuilding? barracks,
        SimBuilding? workshop)
    {
        if (economy.Food + 3 >= economy.FoodCap && !IsBuildingUnderConstruction(BuildingKind.Farm))
        {
            TryBuildAi(BuildingKind.Farm, hall, workers, 4);
        }

        if (workers.Count < _difficultyDefinition.TargetWorkers && hall.Queue.Count < 2)
        {
            TryQueueUnit(hall, UnitKind.Worker);
        }

        if (!hasBarracks && workers.Count >= 4 && !IsBuildingUnderConstruction(BuildingKind.Barracks))
        {
            TryBuildAi(BuildingKind.Barracks, hall, workers, 5);
        }

        if (hasBarracks && !hasWorkshop && workers.Count >= 7 && mainMetrics.Power >= 4f && !IsBuildingUnderConstruction(BuildingKind.Workshop))
        {
            TryBuildAi(BuildingKind.Workshop, hall, workers, 6);
        }

        if (pressure && !NearbyTower(buildings, hall) && !IsBuildingUnderConstruction(BuildingKind.Tower))
        {
            TryBuildAi(BuildingKind.Tower, hall, workers, 5);
        }

        var facing = (GetAiPrimaryTargetPosition() - hall.Center).Normalized();
        if (facing.LengthSquared() <= 0.01f)
        {
            facing = Vector2.Left;
        }

        if (barracks is not null && barracks.Queue.Count < 2)
        {
            var pick = PickBarracksUnit(hasWorkshop);
            if (pick.HasValue)
            {
                TryQueueUnit(barracks, pick.Value);
            }

            barracks.RallyPoint = hall.Center + facing * 94f;
        }

        if (workshop is not null && workshop.Queue.Count < 1 && ShouldBuildSiege(mainMetrics))
        {
            TryQueueUnit(workshop, UnitKind.Catapult);
            workshop.RallyPoint = hall.Center + facing * 124f + new Vector2(-facing.Y, facing.X) * 28f;
        }
    }

    private bool ShouldBuildSiege(AiSquadMetrics mainMetrics)
    {
        if (_aiMemory.Buildings.Count == 0)
        {
            return mainMetrics.Power >= _difficultyDefinition.PushMinPower + 2f;
        }

        foreach (var building in _aiMemory.Buildings.Values)
        {
            if (building.Kind is BuildingKind.Tower or BuildingKind.Barracks or BuildingKind.Workshop)
            {
                return true;
            }
        }

        return mainMetrics.SiegeCount == 0 && mainMetrics.Power >= _difficultyDefinition.PushMinPower + 1f;
    }

    private UnitKind? PickBarracksUnit(bool hasWorkshop)
    {
        var economy = Economy.Get(GameSide.AI);
        var archers = 0;
        var footmen = 0;
        var knights = 0;

        foreach (var unit in Units)
        {
            if (!unit.Alive || unit.Side != GameSide.AI || unit.Kind == UnitKind.Worker)
            {
                continue;
            }

            switch (unit.Kind)
            {
                case UnitKind.Archer:
                    archers++;
                    break;
                case UnitKind.Footman:
                    footmen++;
                    break;
                case UnitKind.Knight:
                    knights++;
                    break;
            }
        }

        if (hasWorkshop && knights < 3 && economy.Gold >= GameDefinitions.Units[UnitKind.Knight].CostGold && economy.Lumber >= GameDefinitions.Units[UnitKind.Knight].CostLumber)
        {
            return UnitKind.Knight;
        }

        if (Init.AiProfile == AiProfile.Harass &&
            hasWorkshop &&
            knights < 2 &&
            economy.Gold >= GameDefinitions.Units[UnitKind.Knight].CostGold &&
            economy.Lumber >= GameDefinitions.Units[UnitKind.Knight].CostLumber)
        {
            return UnitKind.Knight;
        }

        if (archers < footmen && economy.Gold >= GameDefinitions.Units[UnitKind.Archer].CostGold && economy.Lumber >= GameDefinitions.Units[UnitKind.Archer].CostLumber)
        {
            return UnitKind.Archer;
        }

        if (economy.Gold >= GameDefinitions.Units[UnitKind.Footman].CostGold)
        {
            return UnitKind.Footman;
        }

        return null;
    }

    private void ExecuteAiState(
        SimBuilding hall,
        Vector2 suspectedBase,
        List<SimUnit> mainArmy,
        AiSquadMetrics mainMetrics,
        List<SimUnit> harassSquad,
        AiSquadMetrics harassMetrics,
        bool pressure)
    {
        var stagePoint = GetAiStagePoint(hall.Center, suspectedBase);
        var defendPoint = hall.Center + new Vector2(52f, 36f);

        switch (_aiState)
        {
            case AiState.Open:
            case AiState.Boom:
                CommandSquad(mainArmy, stagePoint, suspectedBase, false, false, true);
                if (harassSquad.Count > 0)
                {
                    CommandSquad(harassSquad, hall.Center + new Vector2(18f, 82f), suspectedBase, false, false, true);
                }
                break;

            case AiState.Scout:
                CommandScout(mainArmy, true, suspectedBase, stagePoint);
                if (harassSquad.Count > 0)
                {
                    CommandSquad(harassSquad, stagePoint, suspectedBase, false, false, true);
                }
                break;

            case AiState.Defend:
                CommandSquad(mainArmy, defendPoint, suspectedBase, true, false, true);
                if (harassSquad.Count > 0)
                {
                    CommandSquad(harassSquad, defendPoint + new Vector2(24f, 20f), suspectedBase, true, false, true);
                }
                break;

            case AiState.Regroup:
                CommandSquad(mainArmy, stagePoint, suspectedBase, false, false, true);
                if (harassSquad.Count > 0)
                {
                    CommandSquad(harassSquad, stagePoint + new Vector2(18f, 58f), suspectedBase, false, false, true);
                }
                break;

            case AiState.Push:
                CommandSquad(mainArmy, FindPushTargetPosition(suspectedBase, hall.Center), suspectedBase, true, true, false);
                if (harassSquad.Count > 0)
                {
                    CommandSquad(harassSquad, stagePoint + new Vector2(0f, 52f), suspectedBase, false, false, true);
                }
                _aiLastMainCommandMs = _elapsedMs;
                break;

            case AiState.Harass:
                CommandSquad(mainArmy, stagePoint, suspectedBase, false, false, true);
                CommandHarassSquad(harassSquad, harassMetrics, hall, stagePoint, suspectedBase);
                _aiLastHarassCommandMs = _elapsedMs;
                break;

            case AiState.Finish:
                CommandSquad(mainArmy, FindFinishTargetPosition(suspectedBase, hall.Center), suspectedBase, true, true, false);
                if (harassSquad.Count > 0)
                {
                    CommandSquad(harassSquad, FindFinishTargetPosition(suspectedBase, hall.Center), suspectedBase, true, true, false);
                }
                _aiLastMainCommandMs = _elapsedMs;
                break;
        }
    }

    private void CommandScout(List<SimUnit> mainArmy, bool workersFallback, Vector2 suspectedBase, Vector2 stagePoint)
    {
        var scout = SelectScoutUnit(mainArmy, workersFallback, suspectedBase, stagePoint);
        if (scout is null)
        {
            ResetScoutMission();
            foreach (var unit in mainArmy)
            {
                CommandUnitMove(unit, stagePoint);
            }

            return;
        }

        EnsureScoutMission(scout, suspectedBase, stagePoint);
        var scoutTarget = UpdateScoutMission(scout, suspectedBase, stagePoint);
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

    private SimUnit? SelectScoutUnit(List<SimUnit> mainArmy, bool workersFallback, Vector2 suspectedBase, Vector2 fallback)
    {
        SimUnit? scout = null;
        if (_aiScoutMission.ScoutUnitId.HasValue)
        {
            scout = Units.Find(unit => unit.Alive && unit.Side == GameSide.AI && unit.Id == _aiScoutMission.ScoutUnitId.Value);
            if (scout is not null)
            {
                return scout;
            }

            ResetScoutMission();
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
            _aiWorkerScoutId = null;
            return scout;
        }

        if (!workersFallback)
        {
            return null;
        }

        if (_aiWorkerScoutId.HasValue)
        {
            scout = Units.Find(unit => unit.Alive && unit.Side == GameSide.AI && unit.Id == _aiWorkerScoutId.Value);
            if (scout is not null &&
                scout.IsWorker() &&
                scout.State is UnitState.Idle or UnitState.Gather &&
                CanWorkerScoutSafely(scout, suspectedBase, fallback))
            {
                return scout;
            }

            _aiWorkerScoutId = null;
        }

        foreach (var worker in Units)
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

            _aiWorkerScoutId = worker.Id;
            return worker;
        }

        return null;
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

        var basePosition = _aiMemory.LastKnownPlayerBase ?? suspectedBase;
        var baseTile = _aiMemory.LastKnownPlayerBaseTile ?? Map.WorldToTile(basePosition);
        var savedCurrentSector = _aiScoutMission.CurrentSector;
        var savedLastSector = _aiScoutMission.LastSector;
        var savedEntry = _aiScoutMission.EntryPoint;
        var savedPeek = _aiScoutMission.PeekPoint;
        var savedExit = _aiScoutMission.PlannedExitPoint;
        var savedFallbackExit = _aiScoutMission.FallbackExitPoint;
        var savedExposure = _aiScoutMission.ExposureStartedMs;
        var savedIntelKind = _aiScoutMission.LastIntelTargetKind;
        var savedRouteExposure = _aiScoutMission.CurrentRouteExposure;
        var savedVisibleRun = _aiScoutMission.CurrentVisibleRunLength;
        var savedPeekCompleted = _aiScoutMission.PeekCompleted;
        var savedRequireSectorSwitch = _aiScoutMission.RequireSectorSwitch;
        var canScout = TryPlanScoutSector(worker, basePosition, baseTile, fallback, allowRepeat: true, out _);
        _aiScoutMission.CurrentSector = savedCurrentSector;
        _aiScoutMission.LastSector = savedLastSector;
        _aiScoutMission.EntryPoint = savedEntry;
        _aiScoutMission.PeekPoint = savedPeek;
        _aiScoutMission.PlannedExitPoint = savedExit;
        _aiScoutMission.FallbackExitPoint = savedFallbackExit;
        _aiScoutMission.ExposureStartedMs = savedExposure;
        _aiScoutMission.LastIntelTargetKind = savedIntelKind;
        _aiScoutMission.CurrentRouteExposure = savedRouteExposure;
        _aiScoutMission.CurrentVisibleRunLength = savedVisibleRun;
        _aiScoutMission.PeekCompleted = savedPeekCompleted;
        _aiScoutMission.RequireSectorSwitch = savedRequireSectorSwitch;
        return canScout;
    }

    private void EnsureScoutMission(SimUnit scout, Vector2 suspectedBase, Vector2 fallback)
    {
        if (_aiScoutMission.Active && _aiScoutMission.ScoutUnitId == scout.Id)
        {
            scout.IsNonCombatScout = true;
            scout.TargetCombat = null;
            return;
        }

        ReleaseScoutLock();
        _aiScoutMission.Reset();
        _aiScoutMission.Active = true;
        _aiScoutMission.ScoutUnitId = scout.Id;
        _aiScoutMission.WorkerFallback = scout.IsWorker();
        _aiScoutMission.BaseTile = _aiMemory.LastKnownPlayerBaseTile ?? Map.WorldToTile(suspectedBase);
        _aiScoutMission.BasePosition = _aiMemory.LastKnownPlayerBase ?? suspectedBase;
        _aiScoutMission.RecoverPoint = fallback;
        _aiScoutMission.Phase = ScoutMissionPhase.ApproachEdge;
        _aiScoutMission.PhaseEnteredMs = _elapsedMs;
        _aiScoutMission.LastThreatMs = -99999d;
        _aiScoutMission.ConfirmedBaseMs = _aiMemory.LastKnownPlayerBase.HasValue ? _elapsedMs : -99999d;
        _aiScoutMission.ExposureStartedMs = -99999d;
        _aiScoutMission.CurrentSector = -1;
        _aiScoutMission.LastSector = -1;
        scout.IsNonCombatScout = true;
        scout.TargetCombat = null;
        TryPlanScoutSector(scout, _aiScoutMission.BasePosition, _aiScoutMission.BaseTile!.Value, fallback, allowRepeat: Init.Difficulty == Difficulty.Easy, out _);
    }

    private Vector2 UpdateScoutMission(SimUnit scout, Vector2 suspectedBase, Vector2 fallback)
    {
        scout.IsNonCombatScout = true;
        scout.TargetCombat = null;

        var basePosition = _aiMemory.LastKnownPlayerBase ?? suspectedBase;
        var baseTile = _aiMemory.LastKnownPlayerBaseTile ?? Map.WorldToTile(basePosition);
        _aiScoutMission.BasePosition = basePosition;
        _aiScoutMission.BaseTile = baseTile;
        _aiScoutMission.RecoverPoint = fallback;
        if (_aiMemory.LastKnownPlayerBase.HasValue && _aiScoutMission.ConfirmedBaseMs < 0d)
        {
            _aiScoutMission.ConfirmedBaseMs = _elapsedMs;
        }

        var threat = FindScoutThreat(scout);
        if (threat is not null)
        {
            _aiScoutMission.LastThreatMs = _elapsedMs;
            _aiScoutMission.LastThreatPosition = threat.Position;
        }

        if (_aiScoutMission.Phase is ScoutMissionPhase.Peek or ScoutMissionPhase.ReEnter &&
            _aiScoutMission.ExposureStartedMs < 0d &&
            IsScoutCurrentlyVisibleToPlayer(scout))
        {
            _aiScoutMission.ExposureStartedMs = _elapsedMs;
        }

        if (!HasActiveScoutPlan() &&
            !TryPlanScoutSector(scout, basePosition, baseTile, fallback, allowRepeat: true, out _))
        {
            return fallback;
        }

        switch (_aiScoutMission.Phase)
        {
            case ScoutMissionPhase.ApproachEdge:
                if (ScoutReachedPoint(scout, _aiScoutMission.EntryPoint, ScoutEntryArrivalDistance))
                {
                    StartScoutPeek(ScoutMissionPhase.Peek);
                    return _aiScoutMission.PeekPoint;
                }

                return _aiScoutMission.EntryPoint;

            case ScoutMissionPhase.Peek:
            case ScoutMissionPhase.ReEnter:
                var reachedPeekPoint = ScoutReachedPoint(scout, _aiScoutMission.PeekPoint, ScoutPeekCompletionDistance);
                var hasVisibleCommit = _aiScoutMission.ExposureStartedMs >= 0d || IsScoutCurrentlyVisibleToPlayer(scout);
                if (ShouldBreakScoutContact(scout, threat) ||
                    (reachedPeekPoint && hasVisibleCommit))
                {
                    StartScoutBreakContact();
                    return GetScoutBreakContactTarget(scout, fallback);
                }

                return _aiScoutMission.PeekPoint;

            case ScoutMissionPhase.BreakContact:
                if (ScoutReachedPoint(scout, _aiScoutMission.PlannedExitPoint, ScoutEntryArrivalDistance) ||
                    ScoutReachedPoint(scout, _aiScoutMission.FallbackExitPoint, GameConstants.TileSize * 1.5f) ||
                    !IsPlayerVisibleTile(Map.WorldToTile(scout.Position).X, Map.WorldToTile(scout.Position).Y))
                {
                    _aiScoutMission.Phase = ScoutMissionPhase.Reposition;
                    _aiScoutMission.PhaseEnteredMs = _elapsedMs;
                    return _aiScoutMission.FallbackExitPoint == Vector2.Zero ? fallback : _aiScoutMission.FallbackExitPoint;
                }

                return GetScoutBreakContactTarget(scout, fallback);

            case ScoutMissionPhase.Reposition:
                if (CanScoutReEnter(scout, threat) &&
                    TryPlanScoutSector(
                        scout,
                        basePosition,
                        baseTile,
                        fallback,
                        allowRepeat: Init.Difficulty == Difficulty.Easy,
                        out _))
                {
                    if (ScoutReachedPoint(scout, _aiScoutMission.EntryPoint, ScoutEntryArrivalDistance))
                    {
                        StartScoutPeek(ScoutMissionPhase.ReEnter);
                        return _aiScoutMission.PeekPoint;
                    }

                    return _aiScoutMission.EntryPoint;
                }

                if (threat is not null && scout.Position.DistanceTo(threat.Position) <= GameConstants.TileSize * 3.4f)
                {
                    StartScoutBreakContact();
                    return GetScoutBreakContactTarget(scout, fallback);
                }

                return _aiScoutMission.FallbackExitPoint == Vector2.Zero ? fallback : _aiScoutMission.FallbackExitPoint;

            default:
                if (TryPlanScoutSector(scout, basePosition, baseTile, fallback, allowRepeat: true, out _))
                {
                    _aiScoutMission.Phase = ScoutMissionPhase.ApproachEdge;
                    _aiScoutMission.PhaseEnteredMs = _elapsedMs;
                    return _aiScoutMission.EntryPoint;
                }

                return fallback;
        }
    }

    private void ResetScoutMission()
    {
        ReleaseScoutLock();
        _aiScoutMission.Reset();
        _aiWorkerScoutId = null;
    }

    private void ReleaseScoutLock()
    {
        if (_aiScoutMission.ScoutUnitId.HasValue)
        {
            var scout = Units.Find(unit => unit.Alive && unit.Side == GameSide.AI && unit.Id == _aiScoutMission.ScoutUnitId.Value);
            if (scout is not null)
            {
                scout.IsNonCombatScout = false;
            }
        }
    }

    private bool HasActiveScoutPlan()
    {
        return _aiScoutMission.EntryPoint != Vector2.Zero &&
               _aiScoutMission.PeekPoint != Vector2.Zero &&
               _aiScoutMission.PlannedExitPoint != Vector2.Zero &&
               _aiScoutMission.FallbackExitPoint != Vector2.Zero;
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
            if (!TryFindWalkableRaidPoint(entryTile, 0, 2, scout.Position, out var entryPoint) ||
                !TryFindWalkableRaidPoint(peekTile, 0, 2, entryPoint, out var peekPoint))
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
                if (_aiScoutMission.CurrentSector == candidate.SectorIndex)
                {
                    repeatPenalty += _difficultyDefinition.ScoutSectorRepeatPenalty * 1.2f;
                }

                if (_aiScoutMission.LastSector == candidate.SectorIndex)
                {
                    repeatPenalty += _difficultyDefinition.ScoutSectorRepeatPenalty;
                }
            }

            var threat = EstimateScoutSectorThreat(candidate.PeekPoint);
            if (_aiScoutMission.WorkerFallback &&
                threat > _difficultyDefinition.ScoutThreatTolerance + 0.35f)
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
                             Mathf.Max(0f, threat - _difficultyDefinition.ScoutThreatTolerance) * 28f +
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

        if (_aiScoutMission.CurrentSector != plan.SectorIndex)
        {
            _aiScoutMission.LastSector = _aiScoutMission.CurrentSector;
        }

        _aiScoutMission.CurrentSector = plan.SectorIndex;
        _aiScoutMission.EntryPoint = plan.EntryPoint;
        _aiScoutMission.PeekPoint = plan.PeekPoint;
        _aiScoutMission.PlannedExitPoint = plan.ExitPoint;
        _aiScoutMission.FallbackExitPoint = plan.FallbackExitPoint;
        _aiScoutMission.ExposureStartedMs = -99999d;
        _aiScoutMission.LastIntelTargetKind = plan.IntelKind;
        _aiScoutMission.CurrentRouteExposure = plan.RouteExposure;
        _aiScoutMission.CurrentVisibleRunLength = plan.VisibleRunLength;
        _aiScoutMission.PeekCompleted = false;
        return true;
    }

    private bool ShouldUseFrontierScoutCheat()
    {
        return Difficulty != Difficulty.Easy &&
               _playerVisionSnapshot is { HasData: true };
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

        if (_aiScoutMission.RequireSectorSwitch)
        {
            var switchedCandidates = SelectNextScoutSectorExcludingLast(routeCandidates, _aiScoutMission.CurrentSector);
            if (switchedCandidates.Count == 0)
            {
                return false;
            }

            routeCandidates = switchedCandidates;
        }
        else if (!allowRepeat && _aiScoutMission.LastSector >= 0)
        {
            var saferCandidates = SelectNextScoutSectorExcludingLast(routeCandidates, _aiScoutMission.LastSector);
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
                if (_aiScoutMission.CurrentSector == candidate.SectorIndex)
                {
                    repeatPenalty += _difficultyDefinition.ScoutSectorRepeatPenalty * 1.2f;
                }

                if (_aiScoutMission.LastSector == candidate.SectorIndex)
                {
                    repeatPenalty += _difficultyDefinition.ScoutSectorRepeatPenalty;
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

        if (_aiScoutMission.CurrentSector != bestPlan.SectorIndex)
        {
            _aiScoutMission.LastSector = _aiScoutMission.CurrentSector;
        }

        _aiScoutMission.CurrentSector = bestPlan.SectorIndex;
        _aiScoutMission.EntryPoint = bestPlan.EntryPoint;
        _aiScoutMission.PeekPoint = bestPlan.PeekPoint;
        _aiScoutMission.PlannedExitPoint = bestPlan.ExitPoint;
        _aiScoutMission.FallbackExitPoint = bestPlan.FallbackExitPoint;
        _aiScoutMission.ExposureStartedMs = -99999d;
        _aiScoutMission.LastIntelTargetKind = bestPlan.IntelKind;
        _aiScoutMission.CurrentRouteExposure = bestPlan.RouteExposure;
        _aiScoutMission.CurrentVisibleRunLength = bestPlan.VisibleRunLength;
        plan = bestPlan;
        return true;
    }

    private List<ScoutSectorOption> SelectNextScoutSectorExcludingLast(List<ScoutSectorOption> candidates, int excludedSector)
    {
        if (excludedSector < 0)
        {
            return candidates;
        }

        var filtered = candidates.FindAll(candidate => candidate.SectorIndex != excludedSector);
        return filtered;
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
        var maxX = int.Min(Map.Width - 1, baseTile.X + searchRadiusTiles);
        var minY = int.Max(0, baseTile.Y - searchRadiusTiles);
        var maxY = int.Min(Map.Height - 1, baseTile.Y + searchRadiusTiles);

        for (var ty = minY; ty <= maxY; ty++)
        {
            for (var tx = minX; tx <= maxX; tx++)
            {
                if (!IsFrontierTile(tx, ty))
                {
                    continue;
                }

                var entryPoint = Map.TileToWorldCenter(tx, ty);
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

                var peekPoint = Map.TileToWorldCenter(peekTile.X, peekTile.Y);
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
                    if (!Map.InBounds(tx, ty) ||
                        !Map.IsWalkable(tx, ty) ||
                        !IsPlayerVisibleTile(tx, ty))
                    {
                        continue;
                    }

                    var world = Map.TileToWorldCenter(tx, ty);
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
        if (_aiScoutMission.WorkerFallback &&
            threat > _difficultyDefinition.ScoutThreatTolerance + 0.35f)
        {
            return false;
        }

        var intel = EvaluateScoutIntel(frontier.PeekPoint);
        var score = scout.Position.DistanceTo(frontier.EntryPoint) * 0.18f +
                    frontier.EntryPoint.DistanceTo(frontier.PeekPoint) * 0.1f +
                    Mathf.Max(0f, threat - _difficultyDefinition.ScoutThreatTolerance) * 28f +
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
        var start = Map.WorldToTile(startWorld);
        var goal = Map.WorldToTile(targetWorld);
        if (!Map.InBounds(start.X, start.Y) || !Map.InBounds(goal.X, goal.Y))
        {
            return false;
        }

        var goalRadiusTiles = int.Max(0, Mathf.CeilToInt(arrivalRadius / GameConstants.TileSize));
        var tilePenalty = BuildDynamicTilePenalty(scout, goal, goalRadiusTiles, stuckReroute: false);
        tilePath = Pathfinder.FindPath(Map, start, goal, goalRadiusTiles, scout.Id % 8, tilePenalty);
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
        var maxVisibleTiles = int.Max(3, Mathf.CeilToInt(scout.Speed * (_difficultyDefinition.ScoutMaxExposureMs / 1000f) / GameConstants.TileSize) + 2);
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
        var tile = Map.WorldToTile(scout.Position);
        return IsPlayerVisibleTile(tile.X, tile.Y);
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
        return _playerVisionSnapshot is not null && _playerVisionSnapshot.IsVisible(tx, ty);
    }

    private bool IsFrontierTile(int tx, int ty)
    {
        if (!Map.InBounds(tx, ty) || !Map.IsWalkable(tx, ty) || IsPlayerVisibleTile(tx, ty))
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
                if (Map.InBounds(nx, ny) && IsPlayerVisibleTile(nx, ny))
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
        var workers = CountKnownWorkersNear(position, GameConstants.TileSize * 3.1f);
        if (workers > 0)
        {
            return new ScoutIntelInfo(ScoutIntelTargetKind.WorkerLine, -165f - workers * 16f);
        }

        if (HasKnownTowerNear(position, GameConstants.TileSize * 3.2f))
        {
            return new ScoutIntelInfo(ScoutIntelTargetKind.TowerPerimeter, -108f);
        }

        if (HasKnownOuterTargetNear(position, GameConstants.TileSize * 3.3f))
        {
            return new ScoutIntelInfo(ScoutIntelTargetKind.OuterBuilding, -88f);
        }

        if (CountKnownCombatUnitsNear(position, GameConstants.TileSize * 3.6f) > 0)
        {
            return new ScoutIntelInfo(ScoutIntelTargetKind.ArmyEdge, -72f);
        }

        return new ScoutIntelInfo(ScoutIntelTargetKind.BaseEdge, -32f);
    }

    private float EstimateScoutSectorThreat(Vector2 position)
    {
        return EstimateKnownThreatAt(position, ScoutSectorThreatRadius);
    }

    private void StartScoutPeek(ScoutMissionPhase phase)
    {
        _aiScoutMission.Phase = phase;
        _aiScoutMission.PhaseEnteredMs = _elapsedMs;
        _aiScoutMission.ExposureStartedMs = -99999d;
        _aiScoutMission.PeekCompleted = false;
        _aiScoutMission.RequireSectorSwitch = false;
    }

    private void StartScoutBreakContact()
    {
        _aiScoutMission.Phase = ScoutMissionPhase.BreakContact;
        _aiScoutMission.PhaseEnteredMs = _elapsedMs;
        _aiScoutMission.PeekCompleted = true;
        _aiScoutMission.RequireSectorSwitch = true;
    }

    private bool ShouldBreakScoutContact(SimUnit scout, ICombatTarget? threat)
    {
        var inPlayerVision = IsScoutCurrentlyVisibleToPlayer(scout);
        var visibleCommitMs = _aiScoutMission.ExposureStartedMs >= 0d
            ? _elapsedMs - _aiScoutMission.ExposureStartedMs
            : 0d;
        if (threat is not null)
        {
            if (!inPlayerVision || visibleCommitMs < ScoutMinVisibleCommitMs)
            {
                return IsImmediateScoutThreat(scout, threat);
            }

            return true;
        }

        if (_aiScoutMission.ExposureStartedMs >= 0d &&
            _elapsedMs - _aiScoutMission.ExposureStartedMs >= _difficultyDefinition.ScoutMaxExposureMs)
        {
            return true;
        }

        if (!inPlayerVision || visibleCommitMs < ScoutMinVisibleCommitMs)
        {
            return false;
        }

        var localThreat = EstimateScoutSectorThreat(scout.Position);
        return localThreat > _difficultyDefinition.ScoutThreatTolerance + (_aiScoutMission.WorkerFallback ? 0.2f : 0.75f);
    }

    private bool CanScoutReEnter(SimUnit scout, ICombatTarget? threat)
    {
        if (threat is not null)
        {
            return false;
        }

        if (_elapsedMs - Math.Max(_aiScoutMission.LastThreatMs, _aiScoutMission.PhaseEnteredMs) < _difficultyDefinition.ScoutReentryDelayMs)
        {
            return false;
        }

        if (EstimateScoutSectorThreat(scout.Position) > _difficultyDefinition.ScoutThreatTolerance * 0.7f + 0.3f)
        {
            return false;
        }

        return scout.Position.DistanceTo(_aiScoutMission.PlannedExitPoint) <= GameConstants.TileSize * 1.75f ||
               scout.Position.DistanceTo(_aiScoutMission.FallbackExitPoint) <= GameConstants.TileSize * 1.75f ||
               scout.Position.DistanceTo(_aiScoutMission.BasePosition) >= scout.Sight * GameConstants.TileSize;
    }

    private Vector2 GetScoutBreakContactTarget(SimUnit scout, Vector2 fallback)
    {
        var best = fallback;
        var bestScore = float.PositiveInfinity;
        var options = new[] { _aiScoutMission.PlannedExitPoint, _aiScoutMission.FallbackExitPoint, fallback };
        foreach (var point in options)
        {
            if (point == Vector2.Zero)
            {
                continue;
            }

            var score = scout.Position.DistanceTo(point) * 0.2f +
                        EstimateScoutSectorThreat(point) * 24f;
            if (_aiScoutMission.LastThreatPosition.HasValue)
            {
                score -= point.DistanceTo(_aiScoutMission.LastThreatPosition.Value) * 0.48f;
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

    private ICombatTarget? FindScoutThreat(SimUnit scout)
    {
        ICombatTarget? best = null;
        var bestScore = float.PositiveInfinity;
        foreach (var enemy in Units)
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

        foreach (var building in Buildings)
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

    private void CommandSquad(
        List<SimUnit> squad,
        Vector2 anchor,
        Vector2 lookTarget,
        bool attackMove,
        bool aggressive,
        bool regroupOnly)
    {
        if (squad.Count == 0)
        {
            return;
        }

        var facing = lookTarget - anchor;
        if (facing.LengthSquared() <= 0.01f)
        {
            facing = lookTarget - CalculateSquadMetrics(squad).Center;
        }

        if (facing.LengthSquared() <= 0.01f)
        {
            facing = Vector2.Left;
        }

        facing = facing.Normalized();
        var side = new Vector2(-facing.Y, facing.X);
        var frontline = new List<SimUnit>();
        var backline = new List<SimUnit>();
        var siege = new List<SimUnit>();
        foreach (var unit in squad)
        {
            if (unit.Kind == UnitKind.Catapult)
            {
                siege.Add(unit);
            }
            else if (unit.IsRanged())
            {
                backline.Add(unit);
            }
            else
            {
                frontline.Add(unit);
            }
        }

        CommandFormationRow(frontline, anchor + facing * 14f, side, attackMove, regroupOnly);
        CommandFormationRow(backline, anchor - facing * 28f, side, attackMove && aggressive, regroupOnly);
        CommandFormationRow(siege, anchor - facing * 64f, side, false, regroupOnly);
    }

    private void CommandFormationRow(List<SimUnit> units, Vector2 rowAnchor, Vector2 side, bool attackMove, bool regroupOnly)
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
            if (regroupOnly && units[index].Position.DistanceTo(destination) <= 24f)
            {
                continue;
            }

            if (attackMove)
            {
                CommandUnitAttackMove(units[index], destination);
            }
            else
            {
                CommandUnitMove(units[index], destination);
            }
        }
    }

    private void CommandUnitMove(SimUnit unit, Vector2 destination)
    {
        if (!unit.Alive)
        {
            return;
        }

        if (unit.PathDestination.HasValue && unit.PathDestination.Value.DistanceTo(destination) <= 18f && unit.State is UnitState.Move or UnitState.AttackMove)
        {
            return;
        }

        IssueMove(unit, destination);
    }

    private void CommandUnitAttackMove(SimUnit unit, Vector2 destination)
    {
        if (!unit.Alive || !unit.CanAttack())
        {
            return;
        }

        if (unit.PathDestination.HasValue && unit.PathDestination.Value.DistanceTo(destination) <= 18f && unit.State == UnitState.AttackMove)
        {
            return;
        }

        IssueAttackMove(unit, destination);
    }

    private void BuildAiSquads(List<SimUnit> army, bool allowHarassSplit, out List<SimUnit> mainArmy, out List<SimUnit> harassSquad)
    {
        mainArmy = new List<SimUnit>();
        harassSquad = new List<SimUnit>();

        if (!allowHarassSplit || army.Count < 5 || !_aiMemory.LastKnownPlayerBase.HasValue)
        {
            mainArmy.AddRange(army);
            return;
        }

        var totalPower = 0f;
        var totalFrontline = 0;
        foreach (var unit in army)
        {
            totalPower += CalculateUnitPower(unit);
            if (!unit.IsRanged() && unit.Kind != UnitKind.Catapult)
            {
                totalFrontline++;
            }
        }

        var desiredSize = GetDesiredHarassSquadSize(army.Count);
        var pickedPower = 0f;
        var pickedFrontline = 0;
        var candidates = BuildHarassCandidates(army);
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

        foreach (var unit in army)
        {
            if (!harassSquad.Contains(unit))
            {
                mainArmy.Add(unit);
            }
        }

        if (harassSquad.Count == 0)
        {
            mainArmy.Clear();
            mainArmy.AddRange(army);
        }
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

    private AiSquadMetrics CalculateSquadMetrics(List<SimUnit> squad)
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
            power += unit.Score * (unit.Hp / (float)unit.MaxHp);
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

    private float EstimateKnownEnemyPower()
    {
        var power = 0f;
        foreach (var unit in _aiMemory.Units.Values)
        {
            if (IsFreshEnemyMemory(unit.LastSeenMs))
            {
                power += unit.Power;
            }
        }

        foreach (var building in _aiMemory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs))
            {
                continue;
            }

            power += building.Kind == BuildingKind.Tower ? 2.6f : building.Kind == BuildingKind.TownHall ? 2.2f : 1.2f;
        }

        return power;
    }

    private bool IsFreshEnemyMemory(double lastSeenMs)
    {
        return _elapsedMs - lastSeenMs <= 28000d;
    }

    private bool KnownEnemyPressureNear(Vector2 point, float radius)
    {
        foreach (var unit in _aiMemory.Units.Values)
        {
            if (IsFreshEnemyMemory(unit.LastSeenMs) && unit.Position.DistanceTo(point) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    private Vector2 GetAiStagePoint(Vector2 hallCenter, Vector2 target)
    {
        var direction = target - hallCenter;
        if (direction.LengthSquared() <= 0.01f)
        {
            direction = Vector2.Left;
        }

        direction = direction.Normalized();
        return hallCenter + direction * 120f + new Vector2(-direction.Y, direction.X) * 36f;
    }

    private Vector2 GetAiPrimaryTargetPosition()
    {
        return _aiMemory.LastKnownPlayerBase ?? Map.TileToWorldCenter(Layout.PlayerBase.X, Layout.PlayerBase.Y);
    }

    private Vector2 FindPushTargetPosition(Vector2 fallback, Vector2 assaultOrigin)
    {
        var hasOuterTargets = HasFreshKnownBuilding(excludeTownHall: true);
        AiKnownBuilding? bestBuilding = null;
        var bestScore = float.PositiveInfinity;
        foreach (var building in _aiMemory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs))
            {
                continue;
            }

            if (hasOuterTargets && building.Kind == BuildingKind.TownHall)
            {
                continue;
            }

            var score = ScoreAssaultBuilding(building, fallback, assaultOrigin, allowTownHallFocus: false);

            if (score < bestScore)
            {
                bestScore = score;
                bestBuilding = building;
            }
        }

        return bestBuilding?.Position ?? FindAssaultApproachPoint(fallback, assaultOrigin);
    }

    private Vector2 FindHarassTargetPosition(Vector2 fallback, Vector2 assaultOrigin)
    {
        AiKnownUnit? worker = null;
        var bestWorkerScore = float.PositiveInfinity;
        foreach (var unit in _aiMemory.Units.Values)
        {
            if (!IsFreshEnemyMemory(unit.LastSeenMs))
            {
                continue;
            }

            var score = unit.Position.DistanceTo(fallback);
            if (unit.Kind == UnitKind.Worker)
            {
                score -= 80f;
            }

            if (score < bestWorkerScore)
            {
                worker = unit;
                bestWorkerScore = score;
            }
        }

        return worker?.Position ?? FindPushTargetPosition(fallback, assaultOrigin);
    }

    private Vector2 FindFinishTargetPosition(Vector2 fallback, Vector2 assaultOrigin)
    {
        AiKnownBuilding? bestBuilding = null;
        var bestScore = float.PositiveInfinity;
        foreach (var building in _aiMemory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs))
            {
                continue;
            }

            var score = ScoreAssaultBuilding(building, fallback, assaultOrigin, allowTownHallFocus: true);
            if (score < bestScore)
            {
                bestScore = score;
                bestBuilding = building;
            }
        }

        return bestBuilding?.Position ?? (_aiMemory.LastKnownPlayerBase ?? fallback);
    }

    private bool HasFreshKnownBuilding(bool excludeTownHall)
    {
        foreach (var building in _aiMemory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs))
            {
                continue;
            }

            if (excludeTownHall && building.Kind == BuildingKind.TownHall)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private Vector2 FindAssaultApproachPoint(Vector2 fallback, Vector2 assaultOrigin)
    {
        var direction = fallback - assaultOrigin;
        if (direction.LengthSquared() <= 0.01f)
        {
            return fallback;
        }

        return fallback - direction.Normalized() * AiAssaultStandoffDistance;
    }

    private static float ScoreAssaultBuilding(
        AiKnownBuilding building,
        Vector2 fallback,
        Vector2 assaultOrigin,
        bool allowTownHallFocus)
    {
        var score = building.Position.DistanceTo(fallback) * 0.65f + building.Position.DistanceTo(assaultOrigin) * 0.35f;
        score -= building.Kind switch
        {
            BuildingKind.Tower => 95f,
            BuildingKind.Workshop => 62f,
            BuildingKind.Barracks => 56f,
            BuildingKind.TownHall => allowTownHallFocus ? 34f : -48f,
            _ => 14f
        };

        return score;
    }

    private void ResetHarassMission(bool preserveHistory)
    {
        var lastKind = _aiHarassMission.LastTargetKind;
        var lastPosition = _aiHarassMission.LastTargetPosition;
        var lastFailed = _aiHarassMission.LastRaidFailed;
        _aiHarassMission.Reset();
        if (preserveHistory)
        {
            _aiHarassMission.LastTargetKind = lastKind;
            _aiHarassMission.LastTargetPosition = lastPosition;
            _aiHarassMission.LastRaidFailed = lastFailed;
        }
    }

    private void SyncHarassMissionMembers(List<SimUnit> harassSquad, List<SimUnit> aiUnits)
    {
        if (!_aiHarassMission.Active)
        {
            return;
        }

        var lostIds = new List<int>();
        foreach (var pair in _aiHarassMission.MemberScores)
        {
            if (!aiUnits.Exists(unit => unit.Id == pair.Key))
            {
                _aiHarassMission.LossValue += pair.Value;
                lostIds.Add(pair.Key);
            }
        }

        foreach (var id in lostIds)
        {
            _aiHarassMission.MemberScores.Remove(id);
        }

        foreach (var unit in harassSquad)
        {
            _aiHarassMission.MemberScores[unit.Id] = unit.Score;
        }
    }

    private void RegisterHarassTrade(SimUnit source, ICombatTarget target, int amount)
    {
        if (!_aiHarassMission.Active || !_aiHarassMission.MemberScores.ContainsKey(source.Id) || target.Side != GameSide.Player)
        {
            return;
        }

        if (target is SimUnit { Kind: UnitKind.Worker })
        {
            _aiHarassMission.LastPositiveTradeMs = _elapsedMs;
        }
        else if (target.IsBuilding && target is SimBuilding building && building.Kind != BuildingKind.TownHall)
        {
            _aiHarassMission.LastPositiveTradeMs = _elapsedMs;
            var progressFactor = building.Kind == BuildingKind.Tower ? 1f : 0.75f;
            _aiHarassMission.RaidValue += (amount / (float)building.MaxHp) * progressFactor;
        }

        if (target.Alive)
        {
            return;
        }

        switch (target)
        {
            case SimUnit { Kind: UnitKind.Worker }:
                _aiHarassMission.WorkersKilled++;
                _aiHarassMission.RaidValue += 1f;
                _aiHarassMission.LastPositiveTradeMs = _elapsedMs;
                break;

            case SimBuilding building when building.Kind == BuildingKind.Tower:
                _aiHarassMission.OuterBuildingsDestroyed++;
                _aiHarassMission.RaidValue += 4f;
                _aiHarassMission.LastPositiveTradeMs = _elapsedMs;
                break;

            case SimBuilding building when building.Kind != BuildingKind.TownHall:
                _aiHarassMission.OuterBuildingsDestroyed++;
                _aiHarassMission.RaidValue += 3f;
                _aiHarassMission.LastPositiveTradeMs = _elapsedMs;
                break;
        }
    }

    private void CommandHarassSquad(
        List<SimUnit> squad,
        AiSquadMetrics metrics,
        SimBuilding hall,
        Vector2 stagePoint,
        Vector2 suspectedBase)
    {
        if (squad.Count == 0)
        {
            ResetHarassMission(preserveHistory: true);
            return;
        }

        SyncHarassMissionMembers(squad, Units.FindAll(unit => unit.Alive && unit.Side == GameSide.AI));
        if (!_aiHarassMission.Active)
        {
            var opening = SelectHarassObjective(squad, hall, suspectedBase);
            StartHarassMission(opening, metrics.Power, stagePoint);
        }

        if (_aiHarassMission.Phase == HarassMissionPhase.Recover && _elapsedMs >= _aiHarassMission.RecoverUntilMs)
        {
            var nextObjective = SelectHarassObjective(squad, hall, suspectedBase);
            SetHarassMissionTarget(nextObjective, HarassMissionPhase.Approach);
        }

        if (_aiHarassMission.Phase is HarassMissionPhase.Approach or HarassMissionPhase.Raid)
        {
            RefreshHarassMissionTarget(squad, hall, suspectedBase);
        }

        var squadCenter = metrics.Center == Vector2.Zero ? squad[0].Position : metrics.Center;
        var objectivePosition = _aiHarassMission.CurrentTargetPosition;
        if (_aiHarassMission.Phase == HarassMissionPhase.Approach &&
            (squadCenter.DistanceTo(objectivePosition) <= HarassRaidActivationDistance || HasVisibleHarassOpportunity(squad)))
        {
            SetHarassPhase(HarassMissionPhase.Raid);
        }

        if (_aiHarassMission.Phase == HarassMissionPhase.Raid)
        {
            if (ShouldDisengageHarass(squad, metrics, hall, suspectedBase))
            {
                BeginHarassDisengage(stagePoint);
            }
            else if (IsCurrentHarassTargetExhausted(squad, objectivePosition))
            {
                var nextObjective = SelectHarassObjective(squad, hall, suspectedBase);
                if (nextObjective.Kind == HarassTargetKind.ApproachPoint &&
                    nextObjective.Position.DistanceTo(objectivePosition) <= GameConstants.TileSize * 2f)
                {
                    BeginHarassDisengage(stagePoint);
                }
                else
                {
                    SetHarassMissionTarget(nextObjective, HarassMissionPhase.Approach);
                }
            }
        }

        if (_aiHarassMission.Phase == HarassMissionPhase.Disengage &&
            squadCenter.DistanceTo(_aiHarassMission.RecoverPoint) <= GameConstants.TileSize * 2.2f)
        {
            SetHarassPhase(HarassMissionPhase.Recover);
            _aiHarassMission.RecoverUntilMs = _elapsedMs + HarassRecoverDurationMs;
        }

        switch (_aiHarassMission.Phase)
        {
            case HarassMissionPhase.Approach:
                CommandHarassFormation(squad, _aiHarassMission.CurrentTargetPosition, suspectedBase, 18f, 34f);
                break;

            case HarassMissionPhase.Raid:
                CommandHarassFormation(squad, _aiHarassMission.CurrentTargetPosition, suspectedBase, 8f, 24f);
                break;

            case HarassMissionPhase.Disengage:
            case HarassMissionPhase.Recover:
                CommandHarassRetreat(squad, _aiHarassMission.RecoverPoint, hall.Center);
                break;
        }
    }

    private void StartHarassMission(HarassObjective objective, float startPower, Vector2 recoverPoint)
    {
        _aiHarassMission.Reset();
        _aiHarassMission.Active = true;
        _aiHarassMission.StartPower = float.Max(startPower, 0.01f);
        _aiHarassMission.RecoverPoint = recoverPoint;
        _aiHarassMission.LastPositiveTradeMs = _elapsedMs;
        SetHarassMissionTarget(objective, HarassMissionPhase.Approach);
    }

    private void SetHarassMissionTarget(HarassObjective objective, HarassMissionPhase phase)
    {
        _aiHarassMission.CurrentTargetKind = objective.Kind;
        _aiHarassMission.CurrentTargetPosition = objective.Position;
        _aiHarassMission.CurrentTargetEntityId = objective.EntityId;
        _aiHarassMission.CurrentTargetScore = objective.Score;
        _aiHarassMission.LastTargetKind = objective.Kind;
        _aiHarassMission.LastTargetPosition = objective.Position;
        SetHarassPhase(phase);
    }

    private void SetHarassPhase(HarassMissionPhase phase)
    {
        _aiHarassMission.Phase = phase;
        _aiHarassMission.PhaseEnteredMs = _elapsedMs;
    }

    private void BeginHarassDisengage(Vector2 recoverPoint)
    {
        _aiHarassMission.LastRaidFailed = !IsSuccessfulHarassRaid();
        _aiHarassMission.RecoverPoint = recoverPoint;
        SetHarassPhase(HarassMissionPhase.Disengage);
    }

    private bool IsSuccessfulHarassRaid()
    {
        return _aiHarassMission.WorkersKilled >= 2 ||
               _aiHarassMission.OuterBuildingsDestroyed > 0 ||
               _aiHarassMission.RaidValue >= 3f;
    }

    private void RefreshHarassMissionTarget(List<SimUnit> squad, SimBuilding hall, Vector2 suspectedBase)
    {
        var nextObjective = SelectHarassObjective(squad, hall, suspectedBase);
        var currentStillUseful = IsCurrentHarassObjectiveRelevant(squad);
        if (!currentStillUseful || nextObjective.Score + 28f < _aiHarassMission.CurrentTargetScore)
        {
            SetHarassMissionTarget(nextObjective, _aiHarassMission.Phase == HarassMissionPhase.Raid ? HarassMissionPhase.Raid : HarassMissionPhase.Approach);
        }
    }

    private bool IsCurrentHarassObjectiveRelevant(List<SimUnit> squad)
    {
        if (!_aiHarassMission.Active)
        {
            return false;
        }

        switch (_aiHarassMission.CurrentTargetKind)
        {
            case HarassTargetKind.WorkerLine:
                if (_aiHarassMission.CurrentTargetEntityId.HasValue &&
                    TryGetPlayerUnit(_aiHarassMission.CurrentTargetEntityId.Value, out var worker) &&
                    worker.Kind == UnitKind.Worker)
                {
                    return true;
                }

                return CountKnownWorkersNear(_aiHarassMission.CurrentTargetPosition, GameConstants.TileSize * 3f) > 0;

            case HarassTargetKind.OuterBuilding:
            case HarassTargetKind.FallbackBuilding:
                return _aiHarassMission.CurrentTargetEntityId.HasValue &&
                       TryGetPlayerBuilding(_aiHarassMission.CurrentTargetEntityId.Value, out _);

            case HarassTargetKind.GoldMine:
            case HarassTargetKind.ApproachPoint:
                return true;

            default:
                return HasVisibleHarassOpportunity(squad);
        }
    }

    private bool ShouldDisengageHarass(List<SimUnit> squad, AiSquadMetrics metrics, SimBuilding hall, Vector2 suspectedBase)
    {
        if (metrics.Count == 0)
        {
            return true;
        }

        var localEnemyPower = EstimateVisibleEnemyPowerAround(squad, metrics.Center == Vector2.Zero ? _aiHarassMission.CurrentTargetPosition : metrics.Center, HarassThreatRadius);
        var currentPower = metrics.Power;
        var losing = currentPower < _aiHarassMission.StartPower * 0.7f ||
                     localEnemyPower >= currentPower * 1.15f ||
                     (_aiHarassMission.LossValue > 0f && _elapsedMs - _aiHarassMission.LastPositiveTradeMs > HarassNoTradeWindowMs);
        if (!losing)
        {
            return false;
        }

        if (IsSuccessfulHarassRaid())
        {
            return true;
        }

        return currentPower < _aiHarassMission.StartPower * 0.55f ||
               localEnemyPower >= currentPower * 1.35f ||
               metrics.Center.DistanceTo(hall.Center) < hall.Center.DistanceTo(suspectedBase) * 0.45f;
    }

    private bool HasVisibleHarassOpportunity(List<SimUnit> squad)
    {
        foreach (var unit in Units)
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

        foreach (var building in Buildings)
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

    private bool IsCurrentHarassTargetExhausted(List<SimUnit> squad, Vector2 objectivePosition)
    {
        return !HasVisibleHarassOpportunity(squad) &&
               CountKnownWorkersNear(objectivePosition, GameConstants.TileSize * 3f) == 0 &&
               !HasKnownOuterTargetNear(objectivePosition, GameConstants.TileSize * 4f);
    }

    private HarassObjective SelectHarassObjective(List<SimUnit> squad, SimBuilding hall, Vector2 suspectedBase)
    {
        var squadCenter = CalculateSquadMetrics(squad).Center;
        if (squadCenter == Vector2.Zero)
        {
            squadCenter = squad[0].Position;
        }

        var basePosition = _aiMemory.LastKnownPlayerBase ?? suspectedBase;
        var baseTile = _aiMemory.LastKnownPlayerBaseTile ?? Map.WorldToTile(basePosition);
        HarassObjective? best = null;

        void Consider(HarassTargetKind kind, Vector2 position, int? entityId, float score)
        {
            if (best is null || score < best.Value.Score)
            {
                best = new HarassObjective(kind, position, entityId, score);
            }
        }

        foreach (var unit in Units)
        {
            if (!unit.Alive || unit.Side != GameSide.Player || unit.Kind != UnitKind.Worker || !IsVisibleToSquad(squad, unit.Position, unit.Radius))
            {
                continue;
            }

            var cluster = CountVisibleWorkersNear(unit.Position, GameConstants.TileSize * 2.4f);
            var score = -220f - cluster * 32f + squadCenter.DistanceTo(unit.Position) * 0.42f + EstimateKnownThreatAt(unit.Position, HarassThreatRadius) * 18f;
            Consider(HarassTargetKind.WorkerLine, unit.Position, unit.Id, ApplyHarassRepeatPenalty(kind: HarassTargetKind.WorkerLine, position: unit.Position, score));
        }

        foreach (var remembered in _aiMemory.Units.Values)
        {
            if (!IsFreshEnemyMemory(remembered.LastSeenMs) || remembered.Kind != UnitKind.Worker)
            {
                continue;
            }

            var mineDistance = DistanceToNearestPlayerMine(remembered.Position);
            if (mineDistance > GameConstants.TileSize * 7f)
            {
                continue;
            }

            var workerSupport = CountKnownWorkersNear(remembered.Position, GameConstants.TileSize * 2.8f);
            var score = -150f - workerSupport * 24f + squadCenter.DistanceTo(remembered.Position) * 0.5f + EstimateKnownThreatAt(remembered.Position, HarassThreatRadius) * 20f;
            Consider(HarassTargetKind.WorkerLine, remembered.Position, remembered.Id, ApplyHarassRepeatPenalty(HarassTargetKind.WorkerLine, remembered.Position, score));
        }

        foreach (var resource in Resources)
        {
            if (!resource.Alive || basePosition.DistanceTo(resource.Center) > GameConstants.TileSize * 10f)
            {
                continue;
            }

            if (!TryFindWalkableRaidPoint(Map.WorldToTile(resource.Center), 2, 5, squadCenter, out var raidPoint))
            {
                continue;
            }

            var workerPressure = CountKnownWorkersNear(resource.Center, GameConstants.TileSize * 3.2f);
            var score = (resource.Type == ResourceType.Gold ? -92f : -46f) - workerPressure * 18f + squadCenter.DistanceTo(raidPoint) * 0.48f + EstimateKnownThreatAt(raidPoint, HarassThreatRadius) * 20f;
            Consider(HarassTargetKind.GoldMine, raidPoint, resource.Id, ApplyHarassRepeatPenalty(HarassTargetKind.GoldMine, raidPoint, score));
        }

        foreach (var building in _aiMemory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs) || building.Kind == BuildingKind.TownHall)
            {
                continue;
            }

            if (!TryFindWalkableRaidPoint(building.CenterTile, 3, 6, squadCenter, out var raidPoint))
            {
                continue;
            }

            var score = squadCenter.DistanceTo(raidPoint) * 0.46f + EstimateKnownThreatAt(raidPoint, HarassThreatRadius) * 21f;
            score -= building.Kind switch
            {
                BuildingKind.Tower => 88f,
                BuildingKind.Workshop => 54f,
                BuildingKind.Barracks => 48f,
                BuildingKind.Farm => 28f,
                _ => 18f
            };
            Consider(HarassTargetKind.OuterBuilding, raidPoint, building.Id, ApplyHarassRepeatPenalty(HarassTargetKind.OuterBuilding, raidPoint, score));
        }

        foreach (var point in GenerateHarassApproachPoints(baseTile, squadCenter))
        {
            var score = 54f + squadCenter.DistanceTo(point) * 0.38f + EstimateKnownThreatAt(point, HarassThreatRadius) * 16f + point.DistanceTo(basePosition) * 0.12f;
            Consider(HarassTargetKind.ApproachPoint, point, null, ApplyHarassRepeatPenalty(HarassTargetKind.ApproachPoint, point, score));
        }

        foreach (var building in _aiMemory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs) || building.Kind != BuildingKind.TownHall)
            {
                continue;
            }

            if (!TryFindWalkableRaidPoint(building.CenterTile, 3, 7, squadCenter, out var raidPoint))
            {
                continue;
            }

            var score = 220f + squadCenter.DistanceTo(raidPoint) * 0.42f + EstimateKnownThreatAt(raidPoint, HarassThreatRadius) * 24f;
            Consider(HarassTargetKind.FallbackBuilding, raidPoint, building.Id, ApplyHarassRepeatPenalty(HarassTargetKind.FallbackBuilding, raidPoint, score));
        }

        if (best.HasValue)
        {
            return best.Value;
        }

        return new HarassObjective(HarassTargetKind.ApproachPoint, FindAssaultApproachPoint(basePosition, hall.Center), null, 999f);
    }

    private float ApplyHarassRepeatPenalty(HarassTargetKind kind, Vector2 position, float score)
    {
        if (_aiHarassMission.LastTargetPosition.HasValue &&
            _aiHarassMission.LastRaidFailed &&
            _aiHarassMission.LastTargetPosition.Value.DistanceTo(position) <= HarassRepeatPenaltyDistance)
        {
            score += 140f;
        }

        if (_aiHarassMission.LastTargetKind.HasValue &&
            _aiHarassMission.LastRaidFailed &&
            _aiHarassMission.LastTargetKind.Value == kind)
        {
            score += 36f;
        }

        return score;
    }

    private int CountVisibleWorkersNear(Vector2 position, float radius)
    {
        var count = 0;
        foreach (var unit in Units)
        {
            if (unit.Alive && unit.Side == GameSide.Player && unit.Kind == UnitKind.Worker && unit.Position.DistanceTo(position) <= radius)
            {
                count++;
            }
        }

        return count;
    }

    private int CountKnownWorkersNear(Vector2 position, float radius)
    {
        var count = 0;
        foreach (var unit in _aiMemory.Units.Values)
        {
            if (IsFreshEnemyMemory(unit.LastSeenMs) && unit.Kind == UnitKind.Worker && unit.Position.DistanceTo(position) <= radius)
            {
                count++;
            }
        }

        return count;
    }

    private int CountKnownCombatUnitsNear(Vector2 position, float radius)
    {
        var count = 0;
        foreach (var unit in _aiMemory.Units.Values)
        {
            if (IsFreshEnemyMemory(unit.LastSeenMs) &&
                unit.Kind != UnitKind.Worker &&
                unit.Position.DistanceTo(position) <= radius)
            {
                count++;
            }
        }

        return count;
    }

    private bool HasKnownTowerNear(Vector2 position, float radius)
    {
        foreach (var building in _aiMemory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs) ||
                building.Kind != BuildingKind.Tower)
            {
                continue;
            }

            if (building.Position.DistanceTo(position) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasKnownOuterTargetNear(Vector2 position, float radius)
    {
        foreach (var building in _aiMemory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs) || building.Kind == BuildingKind.TownHall)
            {
                continue;
            }

            if (building.Position.DistanceTo(position) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    private float DistanceToNearestPlayerMine(Vector2 position)
    {
        var best = float.PositiveInfinity;
        foreach (var resource in Resources)
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

    private float EstimateKnownThreatAt(Vector2 position, float radius)
    {
        var threat = 0f;
        foreach (var unit in _aiMemory.Units.Values)
        {
            if (IsFreshEnemyMemory(unit.LastSeenMs) && unit.Position.DistanceTo(position) <= radius)
            {
                threat += unit.Power;
            }
        }

        foreach (var building in _aiMemory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs))
            {
                continue;
            }

            if (building.Kind == BuildingKind.Tower && building.Position.DistanceTo(position) <= radius + GameConstants.TileSize * 2f)
            {
                threat += 2.8f;
            }
        }

        return threat;
    }

    private float EstimateVisibleEnemyPowerAround(List<SimUnit> squad, Vector2 position, float radius)
    {
        var power = 0f;
        foreach (var unit in Units)
        {
            if (!unit.Alive || unit.Side != GameSide.Player || !IsVisibleToSquad(squad, unit.Position, unit.Radius))
            {
                continue;
            }

            if (unit.Position.DistanceTo(position) <= radius)
            {
                power += CalculateUnitPower(unit);
            }
        }

        foreach (var building in Buildings)
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
                    if (!Map.IsWalkable(tx, ty))
                    {
                        continue;
                    }

                    var world = Map.TileToWorldCenter(tx, ty);
                    var score = world.DistanceTo(reference) + EstimateKnownThreatAt(world, GameConstants.TileSize * 4f) * 18f;
                    if (_aiHarassMission.LastTargetPosition.HasValue &&
                        _aiHarassMission.LastRaidFailed &&
                        _aiHarassMission.LastTargetPosition.Value.DistanceTo(world) <= HarassRepeatPenaltyDistance)
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

    private List<Vector2> GenerateHarassApproachPoints(Vector2I baseTile, Vector2 reference)
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
        foreach (var candidate in Units)
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
        foreach (var candidate in Buildings)
        {
            if (candidate.Alive && candidate.Side == GameSide.Player && candidate.Id == id)
            {
                building = candidate;
                return true;
            }
        }

        return false;
    }

    private void CommandHarassFormation(List<SimUnit> squad, Vector2 anchor, Vector2 lookTarget, float frontlineOffset, float backlineOffset)
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

        CommandFormationRow(frontline, anchor + facing * frontlineOffset, side, false, false);
        CommandFormationRow(backline, anchor - facing * backlineOffset, side, false, false);
    }

    private void CommandHarassRetreat(List<SimUnit> squad, Vector2 recoverPoint, Vector2 lookTarget)
    {
        CommandHarassFormation(squad, recoverPoint, lookTarget, 10f, 20f);
    }

    private void ApplyAiMicro(
        List<SimUnit> mainArmy,
        AiSquadMetrics mainMetrics,
        List<SimUnit> harassSquad,
        AiSquadMetrics harassMetrics,
        SimBuilding hall,
        bool pressure)
    {
        ApplyAiMicroToSquad(mainArmy, mainMetrics, false, hall.Center);
        if (_aiState == AiState.Harass)
        {
            ApplyHarassMicro(harassSquad, harassMetrics, hall.Center);
        }
        else
        {
            ApplyAiMicroToSquad(harassSquad, harassMetrics, false, hall.Center);
        }
        if (pressure && mainArmy.Count == 0)
        {
            foreach (var worker in Units)
            {
                if (!worker.Alive || worker.Side != GameSide.AI || !worker.IsWorker())
                {
                    continue;
                }

                if (worker.Position.DistanceTo(hall.Center) <= GameConstants.TileSize * 8f)
                {
                    CommandUnitMove(worker, hall.Center + new Vector2(28f, 0f));
                }
            }
        }
    }

    private void ApplyHarassMicro(List<SimUnit> squad, AiSquadMetrics metrics, Vector2 fallback)
    {
        if (squad.Count == 0)
        {
            return;
        }

        foreach (var unit in squad)
        {
            ICombatTarget? target = _aiHarassMission.Phase is HarassMissionPhase.Disengage or HarassMissionPhase.Recover
                ? FindHarassRetreatThreat(unit)
                : FindPreferredHarassEnemy(unit);
            if (target is not null && (unit.State != UnitState.Attack || unit.TargetCombat != target))
            {
                IssueAttack(unit, target);
            }

            if (target is null)
            {
                continue;
            }

            var enemyDistance = unit.Position.DistanceTo(target.Position);
            var frontlineNearby = CountAiFrontlineNear(unit.Position, squad, 64f);
            if (unit.Kind == UnitKind.Archer && enemyDistance < 74f && frontlineNearby == 0)
            {
                var retreatAnchor = _aiHarassMission.Phase is HarassMissionPhase.Disengage or HarassMissionPhase.Recover
                    ? _aiHarassMission.RecoverPoint
                    : (metrics.Center == Vector2.Zero ? fallback : metrics.Center);
                var retreat = retreatAnchor - (target.Position - retreatAnchor).Normalized() * 42f;
                CommandUnitMove(unit, retreat);
            }
        }
    }

    private void ApplyAiMicroToSquad(List<SimUnit> squad, AiSquadMetrics metrics, bool preferWorkers, Vector2 fallback)
    {
        foreach (var unit in squad)
        {
            if (unit.IsNonCombatScout)
            {
                continue;
            }

            var target = FindPreferredVisibleEnemy(unit, preferWorkers);
            if (target is not null && (unit.State != UnitState.Attack || unit.TargetCombat != target))
            {
                IssueAttack(unit, target);
            }

            if (target is null)
            {
                continue;
            }

            var enemyDistance = unit.Position.DistanceTo(target.Position);
            var frontlineNearby = CountAiFrontlineNear(unit.Position, squad, 64f);
            if (unit.Kind == UnitKind.Catapult && enemyDistance < 140f && frontlineNearby == 0)
            {
                CommandUnitMove(unit, metrics.Center == Vector2.Zero ? fallback : metrics.Center);
                continue;
            }

            if (unit.Kind == UnitKind.Archer && enemyDistance < 72f && frontlineNearby == 0)
            {
                var retreat = metrics.Center == Vector2.Zero
                    ? fallback
                    : metrics.Center - (target.Position - metrics.Center).Normalized() * 38f;
                CommandUnitMove(unit, retreat);
            }
        }
    }

    private ICombatTarget? FindPreferredHarassEnemy(SimUnit unit)
    {
        var sensorRange = unit.Sight * GameConstants.TileSize;
        var visibleWorkers = false;
        var visibleCombat = false;
        var visibleOuterBuildings = false;
        foreach (var other in Units)
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

        foreach (var building in Buildings)
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
        foreach (var other in Units)
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
        foreach (var building in Buildings)
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

    private ICombatTarget? FindHarassRetreatThreat(SimUnit unit)
    {
        SimUnit? bestThreat = null;
        var bestScore = float.PositiveInfinity;
        var sensorRange = unit.Sight * GameConstants.TileSize;
        foreach (var other in Units)
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

    private ICombatTarget? FindPreferredVisibleEnemy(SimUnit unit, bool preferWorkers)
    {
        SimUnit? bestUnit = null;
        var bestUnitScore = float.PositiveInfinity;
        var sensorRange = unit.Sight * GameConstants.TileSize;

        foreach (var other in Units)
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
            if (preferWorkers && other.Kind == UnitKind.Worker)
            {
                score -= 85f;
            }
            else if (other.CanAttack())
            {
                score -= unit.IsRanged() ? 60f : 35f;
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

        ICombatTarget? bestBuilding = null;
        var bestBuildingScore = float.PositiveInfinity;
        foreach (var building in Buildings)
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
            score -= building.Kind switch
            {
                BuildingKind.Tower => 72f,
                BuildingKind.Workshop => 52f,
                BuildingKind.Barracks => 46f,
                BuildingKind.TownHall => preferWorkers ? 18f : 32f,
                _ => 12f
            };
            if (unit.IsSiege())
            {
                score -= 30f;
            }

            if (score < bestBuildingScore)
            {
                bestBuildingScore = score;
                bestBuilding = building;
            }
        }

        return bestBuilding;
    }

    private enum AiState
    {
        Open,
        Scout,
        Boom,
        Defend,
        Regroup,
        Push,
        Harass,
        Finish
    }

    private enum HarassMissionPhase
    {
        Approach,
        Raid,
        Disengage,
        Recover
    }

    private enum HarassTargetKind
    {
        WorkerLine,
        GoldMine,
        OuterBuilding,
        ApproachPoint,
        FallbackBuilding
    }

    private enum ScoutMissionPhase
    {
        ApproachEdge,
        Peek,
        BreakContact,
        Reposition,
        ReEnter
    }

    private enum ScoutIntelTargetKind
    {
        BaseEdge,
        WorkerLine,
        OuterBuilding,
        TowerPerimeter,
        ArmyEdge
    }

    private sealed class AiMemory
    {
        public Dictionary<int, AiKnownUnit> Units { get; } = [];
        public Dictionary<int, AiKnownBuilding> Buildings { get; } = [];
        public Vector2? LastKnownPlayerBase { get; set; }
        public Vector2I? LastKnownPlayerBaseTile { get; set; }
        public double LastContactMs { get; set; } = -99999d;
    }

    private sealed class HarassMissionState
    {
        public bool Active { get; set; }
        public HarassMissionPhase Phase { get; set; } = HarassMissionPhase.Approach;
        public HarassTargetKind CurrentTargetKind { get; set; } = HarassTargetKind.ApproachPoint;
        public Vector2 CurrentTargetPosition { get; set; }
        public int? CurrentTargetEntityId { get; set; }
        public float CurrentTargetScore { get; set; } = float.PositiveInfinity;
        public float StartPower { get; set; }
        public float RaidValue { get; set; }
        public float LossValue { get; set; }
        public int WorkersKilled { get; set; }
        public int OuterBuildingsDestroyed { get; set; }
        public double PhaseEnteredMs { get; set; }
        public double LastPositiveTradeMs { get; set; } = -99999d;
        public double RecoverUntilMs { get; set; }
        public Vector2 RecoverPoint { get; set; }
        public HarassTargetKind? LastTargetKind { get; set; }
        public Vector2? LastTargetPosition { get; set; }
        public bool LastRaidFailed { get; set; }
        public Dictionary<int, int> MemberScores { get; } = [];

        public void Reset()
        {
            Active = false;
            Phase = HarassMissionPhase.Approach;
            CurrentTargetKind = HarassTargetKind.ApproachPoint;
            CurrentTargetPosition = Vector2.Zero;
            CurrentTargetEntityId = null;
            CurrentTargetScore = float.PositiveInfinity;
            StartPower = 0f;
            RaidValue = 0f;
            LossValue = 0f;
            WorkersKilled = 0;
            OuterBuildingsDestroyed = 0;
            PhaseEnteredMs = 0d;
            LastPositiveTradeMs = -99999d;
            RecoverUntilMs = 0d;
            RecoverPoint = Vector2.Zero;
            LastTargetKind = null;
            LastTargetPosition = null;
            LastRaidFailed = false;
            MemberScores.Clear();
        }
    }

    private sealed class ScoutMissionState
    {
        public bool Active { get; set; }
        public int? ScoutUnitId { get; set; }
        public bool WorkerFallback { get; set; }
        public ScoutMissionPhase Phase { get; set; } = ScoutMissionPhase.ApproachEdge;
        public double PhaseEnteredMs { get; set; }
        public double LastThreatMs { get; set; } = -99999d;
        public double ConfirmedBaseMs { get; set; } = -99999d;
        public double ExposureStartedMs { get; set; } = -99999d;
        public Vector2 BasePosition { get; set; }
        public Vector2I? BaseTile { get; set; }
        public Vector2 RecoverPoint { get; set; }
        public Vector2? LastThreatPosition { get; set; }
        public int CurrentSector { get; set; } = -1;
        public int LastSector { get; set; } = -1;
        public Vector2 EntryPoint { get; set; }
        public Vector2 PeekPoint { get; set; }
        public Vector2 PlannedExitPoint { get; set; }
        public Vector2 FallbackExitPoint { get; set; }
        public int CurrentRouteExposure { get; set; }
        public int CurrentVisibleRunLength { get; set; }
        public bool PeekCompleted { get; set; }
        public bool RequireSectorSwitch { get; set; }
        public ScoutIntelTargetKind LastIntelTargetKind { get; set; } = ScoutIntelTargetKind.BaseEdge;

        public void Reset()
        {
            Active = false;
            ScoutUnitId = null;
            WorkerFallback = false;
            Phase = ScoutMissionPhase.ApproachEdge;
            PhaseEnteredMs = 0d;
            LastThreatMs = -99999d;
            ConfirmedBaseMs = -99999d;
            ExposureStartedMs = -99999d;
            BasePosition = Vector2.Zero;
            BaseTile = null;
            RecoverPoint = Vector2.Zero;
            LastThreatPosition = null;
            CurrentSector = -1;
            LastSector = -1;
            EntryPoint = Vector2.Zero;
            PeekPoint = Vector2.Zero;
            PlannedExitPoint = Vector2.Zero;
            FallbackExitPoint = Vector2.Zero;
            CurrentRouteExposure = 0;
            CurrentVisibleRunLength = 0;
            PeekCompleted = false;
            RequireSectorSwitch = false;
            LastIntelTargetKind = ScoutIntelTargetKind.BaseEdge;
        }
    }

    private sealed record AiKnownUnit(int Id, UnitKind Kind, Vector2 Position, float Power, double LastSeenMs);
    private sealed record AiKnownBuilding(int Id, BuildingKind Kind, Vector2 Position, Vector2I CenterTile, int MaxHp, double LastSeenMs);
    private readonly record struct HarassObjective(HarassTargetKind Kind, Vector2 Position, int? EntityId, float Score);
    private readonly record struct ScoutSectorAnchor(int SectorIndex, Vector2 EntryPoint, Vector2 PeekPoint);
    private readonly record struct ScoutFrontierCandidate(int SectorIndex, Vector2I EntryTile, Vector2I PeekTile, Vector2 EntryPoint, Vector2 PeekPoint);
    private readonly record struct ScoutSectorOption(
        int SectorIndex,
        Vector2 EntryPoint,
        Vector2 PeekPoint,
        Vector2 ExitPoint,
        Vector2 FallbackExitPoint,
        ScoutIntelTargetKind IntelKind,
        int RouteExposure,
        int VisibleRunLength,
        float Score);
    private readonly record struct ScoutIntelInfo(ScoutIntelTargetKind Kind, float Score);
    private readonly record struct ScoutRouteExposure(
        int TotalVisibleTiles,
        int LongestVisibleRun,
        int EntryVisibleTiles,
        int PeekVisibleTiles,
        int ExitVisibleTiles,
        int FallbackVisibleTiles);
    private readonly record struct AiSquadMetrics(Vector2 Center, float Power, float SlowestSpeed, int FrontlineCount, int BacklineCount, int SiegeCount, int Count);
}
