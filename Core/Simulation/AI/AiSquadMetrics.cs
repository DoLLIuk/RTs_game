using Godot;

namespace RtsNaGodote.Core.Simulation;

internal readonly record struct AiSquadMetrics(
    Vector2 Center,
    float Power,
    float SlowestSpeed,
    int FrontlineCount,
    int BacklineCount,
    int SiegeCount,
    int Count);
