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

public sealed partial class GameSimulation
{
	private const float CombatBuildingTargetPenalty = 72f;
	private const float CombatRetargetBuffer = 18f;
	private const float AiAssaultStandoffDistance = GameConstants.TileSize * 3f;
	private const float ScoutQuietWindowMs = 1800f;
	private const float ScoutContinueAfterConfirmMs = 5200f;
	private const float ScoutDangerBuffer = GameConstants.TileSize * 1.75f;
	private const float ScoutSectorThreatRadius = GameConstants.TileSize * 4.5f;
	private const float ScoutPeekCompletionDistance = GameConstants.TileSize * 0.38f;
	private const float ScoutEntryArrivalDistance = GameConstants.TileSize * 0.95f;
	private const float ScoutMinVisibleCommitMs = 260f;
	private const double ScoutIntelFreshMemoryMs = 7000d;
	private const int ScoutMaxVisibleEntryTiles = 1;
	private const int ScoutMinPeekVisibleTiles = 2;
	private const int FrontierScoutPreferredPeekDepthTiles = 2;
	private const int FrontierScoutMaxPeekDepthTiles = 3;

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
	private int _nextMovementCohortId = 1;
	private readonly DifficultyDefinition _difficultyDefinition;
	private PlayerVisionSnapshot? _playerVisionSnapshot;
	private AiState _aiState = AiState.Open;
	private double _aiStateEnteredMs;
	private double _aiLastScoutCommandMs = -99999d;
	private double _aiLastHarassCommandMs = -99999d;
	private double _aiLastMainCommandMs = -99999d;
	private readonly LocalMovementService _localMovementService;
	private readonly UnitPathService _unitPathService;
	private readonly UnitSeparationService _unitSeparationService;

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
		_localMovementService = new LocalMovementService(Map, Units, Buildings, Resources);
		_unitPathService = new UnitPathService(Map, Units, Buildings, Resources, FindNearestHall, GetAttackRange, _localMovementService);
		_unitSeparationService = new UnitSeparationService(Units, _localMovementService);
		_aiStateEnteredMs = 0d;
		_aiKnowledge = new AiKnowledgeService(CreateAiKnowledgeContext());
		_aiArmyManager = new AiArmyManager(CreateAiArmyManagerContext());
		_aiEconomyPlanner = new AiEconomyPlanner(CreateAiEconomyPlannerContext());
		_harassMissionService = new HarassMissionService(CreateHarassMissionContext());
		_scoutSystem = new ScoutSystem(CreateScoutContext());

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

		_unitSeparationService.ApplySeparation(delta);

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
		IssueMove(unit, worldTarget, 0f, null);
	}

	public void IssueMoveGroup(IReadOnlyList<SimUnit> units, IReadOnlyList<Vector2> finalTargets, Vector2 sharedWorldTarget)
	{
		IssueMoveGroup(units, finalTargets, sharedWorldTarget, attackMove: false);
	}

	public void IssueMove(SimUnit unit, Vector2 worldTarget, float arrivalRadius, Vector2? interactionAnchor)
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

		var resolvedTarget = worldTarget;
		var resolvedArrivalRadius = arrivalRadius;
		var resolvedAnchor = interactionAnchor ?? worldTarget;
		if (MovementTargetResolver.TryResolveOccupiedMoveTarget(unit, worldTarget, Units, Buildings, Resources, out var approachTarget, out var occupiedArrivalRadius, out var occupiedAnchor))
		{
			resolvedTarget = approachTarget;
			resolvedArrivalRadius = float.Max(resolvedArrivalRadius, occupiedArrivalRadius);
			resolvedAnchor = occupiedAnchor;
		}

		unit.MoveInteractionAnchor = resolvedAnchor;
		unit.MoveArrivalRadius = resolvedArrivalRadius;
		Repath(unit, resolvedTarget, resolvedArrivalRadius, interactionAnchor: resolvedAnchor);
		unit.SetState(unit.Path.Count > 0 ? UnitState.Move : UnitState.Idle);
	}

	private void IssueMoveGroup(IReadOnlyList<SimUnit> units, IReadOnlyList<Vector2> finalTargets, Vector2 sharedWorldTarget, bool attackMove)
	{
		if (units.Count == 0 || units.Count != finalTargets.Count)
		{
			return;
		}

		var cohortUnits = new List<SimUnit>(units.Count);
		var cohortTargets = new List<Vector2>(units.Count);
		for (var index = 0; index < units.Count; index++)
		{
			var unit = units[index];
			if (!unit.Alive)
			{
				continue;
			}

			var finalTarget = finalTargets[index];
			if (unit.IsWorker())
			{
				if (attackMove && unit.CanAttack())
				{
					IssueAttackMove(unit, finalTarget);
				}
				else
				{
					IssueMove(unit, finalTarget);
				}

				continue;
			}

			cohortUnits.Add(unit);
			cohortTargets.Add(finalTarget);
		}

		if (cohortUnits.Count == 0)
		{
			return;
		}

		if (cohortUnits.Count == 1)
		{
			if (attackMove)
			{
				IssueAttackMove(cohortUnits[0], cohortTargets[0]);
			}
			else
			{
				IssueMove(cohortUnits[0], cohortTargets[0]);
			}

			return;
		}

		var cohortId = _nextMovementCohortId++;
		for (var index = 0; index < cohortUnits.Count; index++)
		{
			IssueGroupedMove(cohortUnits[index], sharedWorldTarget, cohortTargets[index], cohortId, index, cohortUnits.Count, attackMove);
		}
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

	public void IssueAttackMoveGroup(IReadOnlyList<SimUnit> units, IReadOnlyList<Vector2> finalTargets, Vector2 sharedWorldTarget)
	{
		IssueMoveGroup(units, finalTargets, sharedWorldTarget, attackMove: true);
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
		var arrivalRadius = GetStaticApproachArrivalRadius(unit);
		Repath(unit, StaticInteractionService.BuildApproachTarget(unit, site), arrivalRadius, interactionAnchor: site.Center);
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
		var arrivalRadius = GetStaticApproachArrivalRadius(unit);
		Repath(unit, MovementTargetResolver.GetWorkerReturnPathTarget(unit, hall, unit.TargetResource), arrivalRadius, interactionAnchor: hall.Center);
		unit.SetState(UnitState.ReturnCargo);
	}

	public void IssueStop(SimUnit unit)
	{
		unit.ClearOrders();
	}

	private void IssueGroupedMove(
		SimUnit unit,
		Vector2 sharedWorldTarget,
		Vector2 finalWorldTarget,
		int cohortId,
		int cohortIndex,
		int cohortCount,
		bool attackMove)
	{
		unit.ClearOrders();
		unit.AssignMovementCohort(cohortId, cohortIndex, cohortCount, sharedWorldTarget, finalWorldTarget, useTerminalFormation: true);
		if (attackMove)
		{
			unit.AttackMoveTarget = finalWorldTarget;
		}

		var initialTarget = unit.SharedMoveTarget ?? sharedWorldTarget;
		Repath(unit, initialTarget, 0f);
		unit.SetState(unit.Path.Count > 0
			? (attackMove ? UnitState.AttackMove : UnitState.Move)
			: UnitState.Idle);
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
				if (_unitPathService.TryGetRepathRequest(unit, out var resumeRequest))
				{
					_unitPathService.Repath(unit, resumeRequest, _elapsedMs);
				}
				else
				{
					var resume = unit.AttackMoveTarget.Value;
					Repath(unit, resume, 0f);
				}

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
				if (CombatApproachService.TryBuildCombatApproachTarget(unit, target, out var approachTarget, out var approachArrivalRadius))
				{
					Repath(unit, approachTarget, approachArrivalRadius, interactionAnchor: target.Position);
				}
				else
				{
					Repath(unit, target.Position, range, interactionAnchor: target.Position);
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

		if (!StaticInteractionService.IsWithinInteractionRange(unit, unit.Position, node))
		{
			if (unit.Path.Count == 0 || unit.PathRepathMs >= GameConstants.RepathIntervalMs)
			{
				Repath(
					unit,
					MovementTargetResolver.GetWorkerGatherPathTarget(unit, node, unit.ReturnBuilding is { Alive: true } returnBuilding ? returnBuilding : FindNearestHall(unit)),
					GetStaticApproachArrivalRadius(unit),
					interactionAnchor: node.Center);
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

		if (!StaticInteractionService.IsWithinInteractionRange(unit, unit.Position, hall))
		{
			if (unit.Path.Count == 0 || unit.PathRepathMs >= GameConstants.RepathIntervalMs)
			{
				Repath(unit, MovementTargetResolver.GetWorkerReturnPathTarget(unit, hall, unit.TargetResource), GetStaticApproachArrivalRadius(unit), interactionAnchor: hall.Center);
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

		if (!StaticInteractionService.IsWithinInteractionRange(unit, unit.Position, site))
		{
			if (unit.Path.Count == 0 || unit.PathRepathMs >= GameConstants.RepathIntervalMs)
			{
				Repath(unit, StaticInteractionService.BuildApproachTarget(unit, site), GetStaticApproachArrivalRadius(unit), interactionAnchor: site.Center);
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
			_harassMissionService.RegisterTrade(unitSource, target, amount);
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

	private static float GetStaticApproachArrivalRadius(SimUnit unit)
	{
		return Mathf.Max(10f, unit.Radius * 0.75f);
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
		var arrivalRadius = GetStaticApproachArrivalRadius(unit);
		Repath(unit, MovementTargetResolver.GetWorkerReturnPathTarget(unit, hall, unit.TargetResource), arrivalRadius, interactionAnchor: hall.Center);
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
		var arrivalRadius = GetStaticApproachArrivalRadius(worker);
		Repath(worker, MovementTargetResolver.GetWorkerReturnPathTarget(worker, hall, worker.TargetResource), arrivalRadius, interactionAnchor: hall.Center);
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

		if (!StaticInteractionService.IsWithinInteractionRange(worker, worker.Position, hall))
		{
			if (worker.Path.Count == 0 || worker.PathRepathMs >= GameConstants.RepathIntervalMs)
			{
				Repath(worker, MovementTargetResolver.GetWorkerReturnPathTarget(worker, hall, worker.TargetResource), GetStaticApproachArrivalRadius(worker), interactionAnchor: hall.Center);
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
					var buildArrivalRadius = GetStaticApproachArrivalRadius(worker);
					Repath(worker, StaticInteractionService.BuildApproachTarget(worker, site), buildArrivalRadius, interactionAnchor: site.Center);
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
						var returnArrivalRadius = GetStaticApproachArrivalRadius(worker);
						Repath(worker, MovementTargetResolver.GetWorkerReturnPathTarget(worker, hall, worker.TargetResource), returnArrivalRadius, interactionAnchor: hall.Center);
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

	private bool Repath(
		SimUnit unit,
		Vector2 worldTarget,
		float arrivalRadius,
		bool stuckReroute = false,
		bool preserveExistingPathOnFailure = false,
		Vector2? interactionAnchor = null)
	{
		var request = new PathRequest(
			worldTarget,
			arrivalRadius,
			interactionAnchor ?? worldTarget,
			stuckReroute,
			preserveExistingPathOnFailure);
		return _unitPathService.Repath(unit, request, _elapsedMs);
	}

	private bool AdvanceWithRecovery(SimUnit unit, double delta)
	{
		return _unitPathService.AdvanceWithRecovery(unit, delta, _elapsedMs);
	}

	private Dictionary<int, float> BuildDynamicTilePenalty(SimUnit unit, Vector2I goal, int goalRadiusTiles, bool stuckReroute)
	{
		return _unitPathService.BuildDynamicTilePenalty(unit, goal, goalRadiusTiles, stuckReroute);
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

		_aiKnowledge.Update(units, buildings);
		_aiEconomyPlanner.AssignIdleWorkers(workers);
		var hasBarracks = buildings.Exists(building => building.Kind == BuildingKind.Barracks && building.Completed);
		var hasWorkshop = buildings.Exists(building => building.Kind == BuildingKind.Workshop && building.Completed);
		var barracks = buildings.Find(building => building.Kind == BuildingKind.Barracks && building.Completed);
		var workshop = buildings.Find(building => building.Kind == BuildingKind.Workshop && building.Completed);
		var armyMetrics = _aiArmyManager.CalculateMetrics(army);
		var knownEnemyPower = _aiKnowledge.EstimateKnownEnemyPower();
		var pressure = _aiKnowledge.KnownEnemyPressureNear(hall.Center, GameConstants.TileSize * _difficultyDefinition.DefendRadiusTiles);
		var confirmedBase = _aiKnowledge.LastKnownPlayerBase;
		var suspectedBase = confirmedBase ?? Map.TileToWorldCenter(Layout.PlayerBase.X, Layout.PlayerBase.Y);
		var stagePoint = GetAiStagePoint(hall.Center, suspectedBase);
		var allowHarassSplit = _aiArmyManager.ShouldUseHarassSplit(hasBarracks, confirmedBase.HasValue, pressure, armyMetrics, knownEnemyPower);
		var provisionalPlan = _aiArmyManager.BuildPlan(army, allowHarassSplit);
		var nextState = _aiArmyManager.DetermineState(
			hasBarracks,
			armyMetrics,
			provisionalPlan.MainMetrics,
			provisionalPlan.HarassMetrics,
			confirmedBase.HasValue,
			pressure,
			knownEnemyPower);
		if (_aiState == AiState.Scout && nextState != AiState.Scout)
		{
			_scoutSystem.BeginRecallToAssembly(stagePoint);
		}

		if (nextState != _aiState)
		{
			_aiState = nextState;
			_aiStateEnteredMs = _elapsedMs;
		}

		if (_aiState != AiState.Scout || confirmedBase.HasValue)
		{
			_scoutSystem.ClearWorkerScoutReservation();
		}

		if (_scoutSystem.HasRecallingScout)
		{
			_scoutSystem.UpdateRecall(stagePoint);
		}

		var finalPlan = _aiArmyManager.BuildPlan(army, allowHarassSplit);
		var mainArmy = finalPlan.MainArmy;
		var harassSquad = finalPlan.HarassSquad;
		var mainMetrics = finalPlan.MainMetrics;
		var harassMetrics = finalPlan.HarassMetrics;

		if (_aiState == AiState.Harass)
		{
			_harassMissionService.SyncMembers(harassSquad, units);
		}
		else
		{
			_harassMissionService.Reset(preserveHistory: true);
		}

		_aiEconomyPlanner.Maintain(
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
		ExecuteAiState(hall, suspectedBase, army, mainArmy, mainMetrics, harassSquad, harassMetrics, pressure);
		ApplyAiMicro(mainArmy, mainMetrics, harassSquad, harassMetrics, hall, pressure);
	}

	private void ExecuteAiState(
		SimBuilding hall,
		Vector2 suspectedBase,
		List<SimUnit> army,
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
				CommandScout(army, mainArmy, true, suspectedBase, stagePoint);
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
				_harassMissionService.Command(harassSquad, harassMetrics, hall, stagePoint, suspectedBase);
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
			facing = lookTarget - _aiArmyManager.CalculateMetrics(squad).Center;
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
		var destinations = new List<Vector2>(units.Count);
		var movers = new List<SimUnit>(units.Count);
		for (var index = 0; index < units.Count; index++)
		{
			var destination = rowAnchor + side * (start + index) * spacing;
			if (regroupOnly && units[index].Position.DistanceTo(destination) <= 24f)
			{
				continue;
			}

			movers.Add(units[index]);
			destinations.Add(destination);
		}

		if (movers.Count == 0)
		{
			return;
		}

		if (attackMove)
		{
			IssueAttackMoveGroup(movers, destinations, rowAnchor);
		}
		else
		{
			IssueMoveGroup(movers, destinations, rowAnchor);
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

	private float EstimateKnownEnemyPower()
	{
		return _aiKnowledge.EstimateKnownEnemyPower();
	}

	private bool IsFreshEnemyMemory(double lastSeenMs)
	{
		return _aiKnowledge.IsFreshEnemyMemory(lastSeenMs);
	}

	private bool IsFreshEnemyMemory(double lastSeenMs, double maxAgeMs)
	{
		return _aiKnowledge.IsFreshEnemyMemory(lastSeenMs, maxAgeMs);
	}

	private bool KnownEnemyPressureNear(Vector2 point, float radius)
	{
		return _aiKnowledge.KnownEnemyPressureNear(point, radius);
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
		return _aiKnowledge.LastKnownPlayerBase ?? Map.TileToWorldCenter(Layout.PlayerBase.X, Layout.PlayerBase.Y);
	}

	private Vector2 FindPushTargetPosition(Vector2 fallback, Vector2 assaultOrigin)
	{
		var hasOuterTargets = HasFreshKnownBuilding(excludeTownHall: true);
		AiKnownBuilding? bestBuilding = null;
		var bestScore = float.PositiveInfinity;
		foreach (var building in _aiKnowledge.KnownBuildings)
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
		foreach (var unit in _aiKnowledge.KnownUnits)
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
		foreach (var building in _aiKnowledge.KnownBuildings)
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

		return bestBuilding?.Position ?? (_aiKnowledge.LastKnownPlayerBase ?? fallback);
	}

	private bool HasFreshKnownBuilding(bool excludeTownHall)
	{
		foreach (var building in _aiKnowledge.KnownBuildings)
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
			_harassMissionService.ApplyMicro(harassSquad, harassMetrics, hall.Center);
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

}
