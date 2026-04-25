using System;
using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Economy;
using RtsNaGodote.Core.Simulation.Resources;
using RtsNaGodote.Core.Simulation.Units;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Core.Simulation;

internal sealed class AiEconomyPlannerContext
{
    private readonly Func<SimUnit, ResourceType, SimResourceNode?> _findNearestResource;
    private readonly Action<SimUnit, SimResourceNode> _issueGather;
    private readonly Func<GameSide, Race, BuildingKind, Vector2I, SimBuilding?> _tryStartBuilding;
    private readonly Action<SimUnit, SimBuilding> _issueBuild;
    private readonly Func<BuildingKind, Vector2I, bool> _canPlaceBuilding;
    private readonly Func<SimBuilding, UnitKind, bool> _tryQueueUnit;
    private readonly Func<Vector2> _getAiPrimaryTargetPosition;

    public AiEconomyPlannerContext(
        DifficultyDefinition difficultyDefinition,
        GameInit init,
        EconomySystem economy,
        List<SimUnit> units,
        List<SimBuilding> buildings,
        AiKnowledgeService aiKnowledge,
        Func<SimUnit, ResourceType, SimResourceNode?> findNearestResource,
        Action<SimUnit, SimResourceNode> issueGather,
        Func<GameSide, Race, BuildingKind, Vector2I, SimBuilding?> tryStartBuilding,
        Action<SimUnit, SimBuilding> issueBuild,
        Func<BuildingKind, Vector2I, bool> canPlaceBuilding,
        Func<SimBuilding, UnitKind, bool> tryQueueUnit,
        Func<Vector2> getAiPrimaryTargetPosition)
    {
        DifficultyDefinition = difficultyDefinition;
        Init = init;
        Economy = economy;
        Units = units;
        Buildings = buildings;
        AiKnowledge = aiKnowledge;
        _findNearestResource = findNearestResource;
        _issueGather = issueGather;
        _tryStartBuilding = tryStartBuilding;
        _issueBuild = issueBuild;
        _canPlaceBuilding = canPlaceBuilding;
        _tryQueueUnit = tryQueueUnit;
        _getAiPrimaryTargetPosition = getAiPrimaryTargetPosition;
    }

    public DifficultyDefinition DifficultyDefinition { get; }
    public GameInit Init { get; }
    public EconomySystem Economy { get; }
    public List<SimUnit> Units { get; }
    public List<SimBuilding> Buildings { get; }
    public AiKnowledgeService AiKnowledge { get; }

    public SimResourceNode? FindNearestResource(SimUnit unit, ResourceType type)
    {
        return _findNearestResource(unit, type);
    }

    public void IssueGather(SimUnit unit, SimResourceNode node)
    {
        _issueGather(unit, node);
    }

    public SimBuilding? TryStartBuilding(GameSide side, Race race, BuildingKind kind, Vector2I tilePosition)
    {
        return _tryStartBuilding(side, race, kind, tilePosition);
    }

    public void IssueBuild(SimUnit unit, SimBuilding site)
    {
        _issueBuild(unit, site);
    }

    public bool CanPlaceBuilding(BuildingKind kind, Vector2I tilePosition)
    {
        return _canPlaceBuilding(kind, tilePosition);
    }

    public bool TryQueueUnit(SimBuilding building, UnitKind kind)
    {
        return _tryQueueUnit(building, kind);
    }

    public Vector2 GetAiPrimaryTargetPosition()
    {
        return _getAiPrimaryTargetPosition();
    }
}
