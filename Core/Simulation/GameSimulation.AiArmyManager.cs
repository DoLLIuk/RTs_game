namespace RtsNaGodote.Core.Simulation;

public sealed partial class GameSimulation
{
    private readonly AiArmyManager _aiArmyManager;

    private AiArmyManagerContext CreateAiArmyManagerContext()
    {
        return new AiArmyManagerContext(
            _difficultyDefinition,
            Init,
            () => _elapsedMs,
            () => _aiKnowledge.LastKnownPlayerBase,
            () => _aiKnowledge.KnownBuildings,
            () => _aiState,
            () => _aiStateEnteredMs,
            () => _aiLastHarassCommandMs,
            unitId => _scoutSystem.IsScoutReserved(unitId),
            ShouldContinueScoutMission,
            IsFreshEnemyMemory);
    }
}
