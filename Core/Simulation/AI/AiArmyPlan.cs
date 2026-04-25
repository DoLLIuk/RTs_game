using System.Collections.Generic;
using RtsNaGodote.Core.Simulation.Units;

namespace RtsNaGodote.Core.Simulation;

internal sealed record AiArmyPlan(
    List<SimUnit> MainArmy,
    List<SimUnit> HarassSquad,
    AiSquadMetrics MainMetrics,
    AiSquadMetrics HarassMetrics);
