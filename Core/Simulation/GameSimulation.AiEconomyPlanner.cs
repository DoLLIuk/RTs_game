using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Core.Simulation;

public sealed partial class GameSimulation
{
    private readonly AiEconomyPlanner _aiEconomyPlanner;

    private AiEconomyPlannerContext CreateAiEconomyPlannerContext()
    {
        return new AiEconomyPlannerContext(
            _difficultyDefinition,
            Init,
            Economy,
            Units,
            Buildings,
            _aiKnowledge,
            FindNearestResource,
            IssueGather,
            TryStartBuildingForAi,
            IssueBuild,
            CanPlaceBuildingForAi,
            TryQueueUnit,
            GetAiPrimaryTargetPosition);
    }

    private SimBuilding? TryStartBuildingForAi(GameSide side, Race race, BuildingKind kind, Vector2I tilePosition)
    {
        return TryStartBuilding(side, race, kind, tilePosition, out var site) ? site : null;
    }

    private bool CanPlaceBuildingForAi(BuildingKind kind, Vector2I tilePosition)
    {
        return EvaluateBuildingPlacement(GameSide.AI, kind, tilePosition).CanPlace;
    }
}
