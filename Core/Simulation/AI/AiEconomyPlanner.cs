using System;
using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Economy;
using RtsNaGodote.Core.Simulation.Units;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Core.Simulation;

internal sealed class AiEconomyPlanner
{
    private readonly AiEconomyPlannerContext _context;

    public AiEconomyPlanner(AiEconomyPlannerContext context)
    {
        _context = context;
    }

    public void AssignIdleWorkers(List<SimUnit> workers)
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
            var resource = _context.FindNearestResource(worker, type) ??
                           _context.FindNearestResource(worker, type == ResourceType.Gold ? ResourceType.Lumber : ResourceType.Gold);
            if (resource is null)
            {
                continue;
            }

            _context.IssueGather(worker, resource);
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

    public void Maintain(
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

        if (workers.Count < _context.DifficultyDefinition.TargetWorkers && hall.Queue.Count < 2)
        {
            _context.TryQueueUnit(hall, UnitKind.Worker);
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

        var facing = (_context.GetAiPrimaryTargetPosition() - hall.Center).Normalized();
        if (facing.LengthSquared() <= 0.01f)
        {
            facing = Vector2.Left;
        }

        if (barracks is not null && barracks.Queue.Count < 2)
        {
            var pick = PickBarracksUnit(hasWorkshop);
            if (pick.HasValue)
            {
                _context.TryQueueUnit(barracks, pick.Value);
            }

            barracks.RallyPoint = hall.Center + facing * 94f;
        }

        if (workshop is not null && workshop.Queue.Count < 1 && ShouldBuildSiege(mainMetrics))
        {
            _context.TryQueueUnit(workshop, UnitKind.Catapult);
            workshop.RallyPoint = hall.Center + facing * 124f + new Vector2(-facing.Y, facing.X) * 28f;
        }
    }

    private bool TryBuildAi(BuildingKind kind, SimBuilding hall, List<SimUnit> workers, int preferredRadius)
    {
        if (workers.Count == 0)
        {
            return false;
        }

        var spot = FindBuildSpot(kind, hall, preferredRadius);
        var building = spot.HasValue
            ? _context.TryStartBuilding(GameSide.AI, _context.Init.AIRace, kind, spot.Value)
            : null;
        if (building is null)
        {
            return false;
        }

        var worker = workers.Find(candidate => candidate.State is UnitState.Gather or UnitState.Idle) ?? workers[0];
        _context.IssueBuild(worker, building);
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
                    if (_context.CanPlaceBuilding(kind, new Vector2I(tx, ty)))
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
        return _context.Buildings.Exists(building => building.Alive && building.Side == GameSide.AI && building.Kind == kind && !building.Completed);
    }

    private static bool NearbyTower(List<SimBuilding> buildings, SimBuilding hall)
    {
        return buildings.Exists(building => building.Kind == BuildingKind.Tower && building.Center.DistanceTo(hall.Center) < GameConstants.TileSize * 10f);
    }

    private bool ShouldBuildSiege(AiSquadMetrics mainMetrics)
    {
        if (_context.AiKnowledge.KnownBuildingCount == 0)
        {
            return mainMetrics.Power >= _context.DifficultyDefinition.PushMinPower + 2f;
        }

        foreach (var building in _context.AiKnowledge.KnownBuildings)
        {
            if (building.Kind is BuildingKind.Tower or BuildingKind.Barracks or BuildingKind.Workshop)
            {
                return true;
            }
        }

        return mainMetrics.SiegeCount == 0 && mainMetrics.Power >= _context.DifficultyDefinition.PushMinPower + 1f;
    }

    private UnitKind? PickBarracksUnit(bool hasWorkshop)
    {
        var economy = _context.Economy.Get(GameSide.AI);
        var archers = 0;
        var footmen = 0;
        var knights = 0;

        foreach (var unit in _context.Units)
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

        if (hasWorkshop &&
            knights < 3 &&
            economy.Gold >= GameDefinitions.Units[UnitKind.Knight].CostGold &&
            economy.Lumber >= GameDefinitions.Units[UnitKind.Knight].CostLumber)
        {
            return UnitKind.Knight;
        }

        if (_context.Init.AiProfile == AiProfile.Harass &&
            hasWorkshop &&
            knights < 2 &&
            economy.Gold >= GameDefinitions.Units[UnitKind.Knight].CostGold &&
            economy.Lumber >= GameDefinitions.Units[UnitKind.Knight].CostLumber)
        {
            return UnitKind.Knight;
        }

        if (archers < footmen &&
            economy.Gold >= GameDefinitions.Units[UnitKind.Archer].CostGold &&
            economy.Lumber >= GameDefinitions.Units[UnitKind.Archer].CostLumber)
        {
            return UnitKind.Archer;
        }

        if (economy.Gold >= GameDefinitions.Units[UnitKind.Footman].CostGold)
        {
            return UnitKind.Footman;
        }

        return null;
    }
}
