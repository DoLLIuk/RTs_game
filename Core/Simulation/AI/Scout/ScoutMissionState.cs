using Godot;

namespace RtsNaGodote.Core.Simulation;

internal sealed class ScoutMissionState
{
    public bool Active { get; set; }
    public int? ScoutUnitId { get; set; }
    public bool WorkerFallback { get; set; }
    public ScoutMissionPhase Phase { get; set; } = ScoutMissionPhase.ApproachEdge;
    public double PhaseEnteredMs { get; set; }
    public double LastThreatMs { get; set; } = -99999d;
    public double ConfirmedBaseMs { get; set; } = -99999d;
    public double ExposureStartedMs { get; set; } = -99999d;
    public Vector2 BasePosition { get; set; }
    public Vector2I? BaseTile { get; set; }
    public Vector2 RecoverPoint { get; set; }
    public Vector2? LastThreatPosition { get; set; }
    public int CurrentSector { get; set; } = -1;
    public int LastSector { get; set; } = -1;
    public int MandatorySectorSwitchFrom { get; set; } = -1;
    public Vector2 EntryPoint { get; set; }
    public Vector2 PeekPoint { get; set; }
    public Vector2 PlannedExitPoint { get; set; }
    public Vector2 FallbackExitPoint { get; set; }
    public int CurrentRouteExposure { get; set; }
    public int CurrentVisibleRunLength { get; set; }
    public bool PeekCompleted { get; set; }
    public bool RequireSectorSwitch { get; set; }
    public bool HasCommittedReentryPlan { get; set; }
    public ScoutIntelTargetKind LastIntelTargetKind { get; set; } = ScoutIntelTargetKind.BaseEdge;

    public void Reset()
    {
        Active = false;
        ScoutUnitId = null;
        WorkerFallback = false;
        Phase = ScoutMissionPhase.ApproachEdge;
        PhaseEnteredMs = 0d;
        LastThreatMs = -99999d;
        ConfirmedBaseMs = -99999d;
        ExposureStartedMs = -99999d;
        BasePosition = Vector2.Zero;
        BaseTile = null;
        RecoverPoint = Vector2.Zero;
        LastThreatPosition = null;
        CurrentSector = -1;
        LastSector = -1;
        MandatorySectorSwitchFrom = -1;
        EntryPoint = Vector2.Zero;
        PeekPoint = Vector2.Zero;
        PlannedExitPoint = Vector2.Zero;
        FallbackExitPoint = Vector2.Zero;
        CurrentRouteExposure = 0;
        CurrentVisibleRunLength = 0;
        PeekCompleted = false;
        RequireSectorSwitch = false;
        HasCommittedReentryPlan = false;
        LastIntelTargetKind = ScoutIntelTargetKind.BaseEdge;
    }
}
