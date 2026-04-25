using System.Collections.Generic;
using Godot;

namespace RtsNaGodote.Core.Simulation;

internal sealed class HarassMissionState
{
    public bool Active { get; set; }
    public HarassMissionPhase Phase { get; set; } = HarassMissionPhase.Approach;
    public HarassTargetKind CurrentTargetKind { get; set; } = HarassTargetKind.ApproachPoint;
    public Vector2 CurrentTargetPosition { get; set; }
    public int? CurrentTargetEntityId { get; set; }
    public float CurrentTargetScore { get; set; } = float.PositiveInfinity;
    public float StartPower { get; set; }
    public float RaidValue { get; set; }
    public float LossValue { get; set; }
    public int WorkersKilled { get; set; }
    public int OuterBuildingsDestroyed { get; set; }
    public double PhaseEnteredMs { get; set; }
    public double LastPositiveTradeMs { get; set; } = -99999d;
    public double RecoverUntilMs { get; set; }
    public Vector2 RecoverPoint { get; set; }
    public HarassTargetKind? LastTargetKind { get; set; }
    public Vector2? LastTargetPosition { get; set; }
    public bool LastRaidFailed { get; set; }
    public Dictionary<int, int> MemberScores { get; } = [];

    public void Reset()
    {
        Active = false;
        Phase = HarassMissionPhase.Approach;
        CurrentTargetKind = HarassTargetKind.ApproachPoint;
        CurrentTargetPosition = Vector2.Zero;
        CurrentTargetEntityId = null;
        CurrentTargetScore = float.PositiveInfinity;
        StartPower = 0f;
        RaidValue = 0f;
        LossValue = 0f;
        WorkersKilled = 0;
        OuterBuildingsDestroyed = 0;
        PhaseEnteredMs = 0d;
        LastPositiveTradeMs = -99999d;
        RecoverUntilMs = 0d;
        RecoverPoint = Vector2.Zero;
        LastTargetKind = null;
        LastTargetPosition = null;
        LastRaidFailed = false;
        MemberScores.Clear();
    }
}
