using System;
using System.Collections.Generic;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Units;

namespace RtsNaGodote.Core.Simulation;

internal sealed class AiKnowledgeContext
{
    private readonly Func<double> _getElapsedMs;

    public AiKnowledgeContext(
        List<SimUnit> units,
        List<SimBuilding> buildings,
        Func<double> getElapsedMs)
    {
        Units = units;
        Buildings = buildings;
        _getElapsedMs = getElapsedMs;
    }

    public List<SimUnit> Units { get; }
    public List<SimBuilding> Buildings { get; }
    public double ElapsedMs => _getElapsedMs();
}
