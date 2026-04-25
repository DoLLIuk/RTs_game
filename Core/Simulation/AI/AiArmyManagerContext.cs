using System;
using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;

namespace RtsNaGodote.Core.Simulation;

internal sealed class AiArmyManagerContext
{
    private readonly Func<double> _getElapsedMs;
    private readonly Func<Vector2?> _getLastKnownPlayerBase;
    private readonly Func<IEnumerable<AiKnownBuilding>> _getKnownBuildings;
    private readonly Func<AiState> _getCurrentState;
    private readonly Func<double> _getStateEnteredMs;
    private readonly Func<double> _getLastHarassCommandMs;
    private readonly Func<int, bool> _isScoutReserved;
    private readonly Func<bool, bool> _shouldContinueScoutMission;
    private readonly Func<double, bool> _isFreshEnemyMemory;

    public AiArmyManagerContext(
        DifficultyDefinition difficultyDefinition,
        GameInit init,
        Func<double> getElapsedMs,
        Func<Vector2?> getLastKnownPlayerBase,
        Func<IEnumerable<AiKnownBuilding>> getKnownBuildings,
        Func<AiState> getCurrentState,
        Func<double> getStateEnteredMs,
        Func<double> getLastHarassCommandMs,
        Func<int, bool> isScoutReserved,
        Func<bool, bool> shouldContinueScoutMission,
        Func<double, bool> isFreshEnemyMemory)
    {
        DifficultyDefinition = difficultyDefinition;
        Init = init;
        _getElapsedMs = getElapsedMs;
        _getLastKnownPlayerBase = getLastKnownPlayerBase;
        _getKnownBuildings = getKnownBuildings;
        _getCurrentState = getCurrentState;
        _getStateEnteredMs = getStateEnteredMs;
        _getLastHarassCommandMs = getLastHarassCommandMs;
        _isScoutReserved = isScoutReserved;
        _shouldContinueScoutMission = shouldContinueScoutMission;
        _isFreshEnemyMemory = isFreshEnemyMemory;
    }

    public DifficultyDefinition DifficultyDefinition { get; }
    public GameInit Init { get; }
    public double ElapsedMs => _getElapsedMs();
    public Vector2? LastKnownPlayerBase => _getLastKnownPlayerBase();
    public IEnumerable<AiKnownBuilding> KnownBuildings => _getKnownBuildings();
    public AiState CurrentState => _getCurrentState();
    public double StateEnteredMs => _getStateEnteredMs();
    public double LastHarassCommandMs => _getLastHarassCommandMs();

    public bool IsScoutReserved(int unitId)
    {
        return _isScoutReserved(unitId);
    }

    public bool ShouldContinueScoutMission(bool baseConfirmed)
    {
        return _shouldContinueScoutMission(baseConfirmed);
    }

    public bool IsFreshEnemyMemory(double lastSeenMs)
    {
        return _isFreshEnemyMemory(lastSeenMs);
    }
}
