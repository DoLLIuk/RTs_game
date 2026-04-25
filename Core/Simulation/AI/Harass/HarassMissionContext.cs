using System;
using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Resources;
using RtsNaGodote.Core.Simulation.Units;
using WorldTileMap = RtsNaGodote.Core.Simulation.World.TileMap;

namespace RtsNaGodote.Core.Simulation;

internal sealed class HarassMissionContext
{
    private readonly Func<double> _getElapsedMs;
    private readonly Func<Vector2, Vector2, Vector2> _findAssaultApproachPoint;
    private readonly Func<List<SimUnit>, AiSquadMetrics> _calculateMetrics;
    private readonly Action<SimUnit, Vector2> _commandUnitMove;
    private readonly Action<IReadOnlyList<SimUnit>, IReadOnlyList<Vector2>, Vector2> _commandUnitMoveGroup;
    private readonly Action<SimUnit, ICombatTarget> _issueAttack;

    public HarassMissionContext(
        WorldTileMap map,
        List<SimUnit> units,
        List<SimBuilding> buildings,
        List<SimResourceNode> resources,
        AiKnowledgeService aiKnowledge,
        Func<double> getElapsedMs,
        Func<Vector2, Vector2, Vector2> findAssaultApproachPoint,
        Func<List<SimUnit>, AiSquadMetrics> calculateMetrics,
        Action<SimUnit, Vector2> commandUnitMove,
        Action<IReadOnlyList<SimUnit>, IReadOnlyList<Vector2>, Vector2> commandUnitMoveGroup,
        Action<SimUnit, ICombatTarget> issueAttack)
    {
        Map = map;
        Units = units;
        Buildings = buildings;
        Resources = resources;
        AiKnowledge = aiKnowledge;
        _getElapsedMs = getElapsedMs;
        _findAssaultApproachPoint = findAssaultApproachPoint;
        _calculateMetrics = calculateMetrics;
        _commandUnitMove = commandUnitMove;
        _commandUnitMoveGroup = commandUnitMoveGroup;
        _issueAttack = issueAttack;
    }

    public WorldTileMap Map { get; }
    public List<SimUnit> Units { get; }
    public List<SimBuilding> Buildings { get; }
    public List<SimResourceNode> Resources { get; }
    public AiKnowledgeService AiKnowledge { get; }
    public double ElapsedMs => _getElapsedMs();

    public Vector2 FindAssaultApproachPoint(Vector2 fallback, Vector2 assaultOrigin)
    {
        return _findAssaultApproachPoint(fallback, assaultOrigin);
    }

    public AiSquadMetrics CalculateMetrics(List<SimUnit> squad)
    {
        return _calculateMetrics(squad);
    }

    public void CommandUnitMove(SimUnit unit, Vector2 destination)
    {
        _commandUnitMove(unit, destination);
    }

    public void CommandUnitMoveGroup(IReadOnlyList<SimUnit> units, IReadOnlyList<Vector2> destinations, Vector2 sharedTarget)
    {
        _commandUnitMoveGroup(units, destinations, sharedTarget);
    }

    public void IssueAttack(SimUnit unit, ICombatTarget target)
    {
        _issueAttack(unit, target);
    }
}
