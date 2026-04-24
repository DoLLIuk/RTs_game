using Godot;

namespace RtsNaGodote.Core.Simulation;

internal enum ScoutMissionPhase
{
    ApproachEdge,
    Peek,
    BreakContact,
    Reposition,
    ReEnter
}

internal enum ScoutIntelTargetKind
{
    BaseEdge,
    WorkerLine,
    OuterBuilding,
    TowerPerimeter,
    ArmyEdge
}

internal readonly record struct ScoutSectorAnchor(int SectorIndex, Vector2 EntryPoint, Vector2 PeekPoint);
internal readonly record struct ScoutFrontierCandidate(int SectorIndex, Vector2I EntryTile, Vector2I PeekTile, Vector2 EntryPoint, Vector2 PeekPoint);
internal readonly record struct ScoutSectorOption(
    int SectorIndex,
    Vector2 EntryPoint,
    Vector2 PeekPoint,
    Vector2 ExitPoint,
    Vector2 FallbackExitPoint,
    ScoutIntelTargetKind IntelKind,
    int RouteExposure,
    int VisibleRunLength,
    float Score);
internal readonly record struct ScoutIntelInfo(ScoutIntelTargetKind Kind, float Score);
internal readonly record struct ScoutRouteExposure(
    int TotalVisibleTiles,
    int LongestVisibleRun,
    int EntryVisibleTiles,
    int PeekVisibleTiles,
    int ExitVisibleTiles,
    int FallbackVisibleTiles);
