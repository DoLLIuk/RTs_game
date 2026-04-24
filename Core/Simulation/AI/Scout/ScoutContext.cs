using System;
using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Units;
using RtsNaGodote.Core.Simulation.World;
using WorldTileMap = RtsNaGodote.Core.Simulation.World.TileMap;

namespace RtsNaGodote.Core.Simulation;

internal delegate bool ScoutTryFindWalkableRaidPointDelegate(Vector2I centerTile, int minRadius, int maxRadius, Vector2 reference, out Vector2 point);
internal delegate Dictionary<int, float> ScoutBuildDynamicTilePenaltyDelegate(SimUnit unit, Vector2I goal, int goalRadiusTiles, bool stuckReroute);

internal sealed class ScoutContext
{
    private readonly Func<double> _getElapsedMs;
    private readonly Func<PlayerVisionSnapshot?> _getPlayerVisionSnapshot;
    private readonly Func<Vector2?> _getLastKnownPlayerBase;
    private readonly Func<Vector2I?> _getLastKnownPlayerBaseTile;
    private readonly Func<Vector2, float, int> _countKnownWorkersNear;
    private readonly Func<Vector2, float, int> _countKnownCombatUnitsNear;
    private readonly Func<Vector2, float, bool> _hasKnownTowerNear;
    private readonly Func<Vector2, float, bool> _hasKnownOuterTargetNear;
    private readonly Func<Vector2, float, float> _estimateKnownThreatAt;
    private readonly ScoutTryFindWalkableRaidPointDelegate _tryFindWalkableRaidPoint;
    private readonly ScoutBuildDynamicTilePenaltyDelegate _buildDynamicTilePenalty;

    public ScoutContext(
        Difficulty difficulty,
        WorldTileMap map,
        List<SimUnit> units,
        List<SimBuilding> buildings,
        DifficultyDefinition difficultyDefinition,
        Func<double> getElapsedMs,
        Func<PlayerVisionSnapshot?> getPlayerVisionSnapshot,
        Func<Vector2?> getLastKnownPlayerBase,
        Func<Vector2I?> getLastKnownPlayerBaseTile,
        Func<Vector2, float, int> countKnownWorkersNear,
        Func<Vector2, float, int> countKnownCombatUnitsNear,
        Func<Vector2, float, bool> hasKnownTowerNear,
        Func<Vector2, float, bool> hasKnownOuterTargetNear,
        Func<Vector2, float, float> estimateKnownThreatAt,
        ScoutTryFindWalkableRaidPointDelegate tryFindWalkableRaidPoint,
        ScoutBuildDynamicTilePenaltyDelegate buildDynamicTilePenalty)
    {
        Difficulty = difficulty;
        Map = map;
        Units = units;
        Buildings = buildings;
        DifficultyDefinition = difficultyDefinition;
        _getElapsedMs = getElapsedMs;
        _getPlayerVisionSnapshot = getPlayerVisionSnapshot;
        _getLastKnownPlayerBase = getLastKnownPlayerBase;
        _getLastKnownPlayerBaseTile = getLastKnownPlayerBaseTile;
        _countKnownWorkersNear = countKnownWorkersNear;
        _countKnownCombatUnitsNear = countKnownCombatUnitsNear;
        _hasKnownTowerNear = hasKnownTowerNear;
        _hasKnownOuterTargetNear = hasKnownOuterTargetNear;
        _estimateKnownThreatAt = estimateKnownThreatAt;
        _tryFindWalkableRaidPoint = tryFindWalkableRaidPoint;
        _buildDynamicTilePenalty = buildDynamicTilePenalty;
    }

    public Difficulty Difficulty { get; }
    public WorldTileMap Map { get; }
    public List<SimUnit> Units { get; }
    public List<SimBuilding> Buildings { get; }
    public DifficultyDefinition DifficultyDefinition { get; }
    public double ElapsedMs => _getElapsedMs();
    public PlayerVisionSnapshot? PlayerVisionSnapshot => _getPlayerVisionSnapshot();
    public Vector2? LastKnownPlayerBase => _getLastKnownPlayerBase();
    public Vector2I? LastKnownPlayerBaseTile => _getLastKnownPlayerBaseTile();

    public int CountKnownWorkersNear(Vector2 position, float radius)
    {
        return _countKnownWorkersNear(position, radius);
    }

    public int CountKnownCombatUnitsNear(Vector2 position, float radius)
    {
        return _countKnownCombatUnitsNear(position, radius);
    }

    public bool HasKnownTowerNear(Vector2 position, float radius)
    {
        return _hasKnownTowerNear(position, radius);
    }

    public bool HasKnownOuterTargetNear(Vector2 position, float radius)
    {
        return _hasKnownOuterTargetNear(position, radius);
    }

    public float EstimateKnownThreatAt(Vector2 position, float radius)
    {
        return _estimateKnownThreatAt(position, radius);
    }

    public bool TryFindWalkableRaidPoint(Vector2I centerTile, int minRadius, int maxRadius, Vector2 reference, out Vector2 point)
    {
        return _tryFindWalkableRaidPoint(centerTile, minRadius, maxRadius, reference, out point);
    }

    public Dictionary<int, float> BuildDynamicTilePenalty(SimUnit unit, Vector2I goal, int goalRadiusTiles, bool stuckReroute)
    {
        return _buildDynamicTilePenalty(unit, goal, goalRadiusTiles, stuckReroute);
    }
}
