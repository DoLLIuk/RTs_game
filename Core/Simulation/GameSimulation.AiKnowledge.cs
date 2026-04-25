namespace RtsNaGodote.Core.Simulation;

public sealed partial class GameSimulation
{
    private readonly AiKnowledgeService _aiKnowledge;

    private AiKnowledgeContext CreateAiKnowledgeContext()
    {
        return new AiKnowledgeContext(
            Units,
            Buildings,
            () => _elapsedMs);
    }
}
