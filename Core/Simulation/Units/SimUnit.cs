using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Resources;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Core.Simulation.Units;

public enum UnitState
{
    Idle,
    Move,
    AttackMove,
    Attack,
    Gather,
    ReturnCargo,
    Build,
    Dead
}

public enum WorkerPassiveOrderType
{
    None,
    Move,
    Gather,
    Build,
    ReturnCargo
}

public enum WorkerDefenseMode
{
    None,
    EvadeToHall,
    BaseDefenseCombat
}

public sealed class SimUnit : ICombatTarget
{
    public SimUnit(int id, UnitKind kind, GameSide side, Race race, Vector2 startPosition)
    {
        var definition = GameDefinitions.Units[kind];
        Id = id;
        Kind = kind;
        Side = side;
        Race = race;
        Position = startPosition;
        Radius = definition.Size / 2f;
        Speed = definition.Speed;
        Sight = definition.Sight;
        Attack = definition.Attack;
        Range = definition.Range;
        CooldownMs = definition.CooldownMs;
        Food = definition.Food;
        BuildTimeMs = definition.BuildTimeMs;
        Producer = definition.Producer;
        Requires = definition.Requires;
        SplashRadius = definition.SplashRadius;
        BonusVsBuilding = definition.BonusVsBuilding;
        Score = definition.Score;
        MaxHp = definition.Hp;
        Hp = definition.Hp;
    }

    public int Id { get; }
    public UnitKind Kind { get; }
    public GameSide Side { get; }
    public Race Race { get; }
    public Vector2 Position { get; set; }
    public float Radius { get; }
    public float Speed { get; }
    public int Sight { get; }
    public int Attack { get; }
    public float Range { get; }
    public int CooldownMs { get; }
    public int Food { get; }
    public int BuildTimeMs { get; }
    public int Score { get; }
    public BuildingKind Producer { get; }
    public BuildingKind? Requires { get; }
    public float SplashRadius { get; }
    public int BonusVsBuilding { get; }
    public int MaxHp { get; }
    public int Hp { get; private set; }
    public bool Alive { get; private set; } = true;
    public bool IsBuilding => false;
    public UnitState State { get; private set; } = UnitState.Idle;
    public List<Vector2> Path { get; } = [];
    public Vector2? PathDestination { get; set; }
    public Vector2? MoveInteractionAnchor { get; set; }
    public float MoveArrivalRadius { get; set; }
    public Vector2? AttackMoveTarget { get; set; }
    public double PathRepathMs { get; set; }
    public double StuckAccumMs { get; set; }
    public double PathProgressStallMs { get; set; }
    public double LastHeavyRerouteMs { get; set; } = -99999d;
    public float LastPathProgressMetric { get; set; } = float.PositiveInfinity;
    public double LastAttackMs { get; set; }
    public ICombatTarget? TargetCombat { get; set; }
    public SimResourceNode? TargetResource { get; set; }
    public SimBuilding? TargetBuilding { get; set; }
    public SimBuilding? ReturnBuilding { get; set; }
    public ResourceType? DesiredResourceType { get; set; }
    public ResourceType? CargoType { get; set; }
    public int CargoAmount { get; set; }
    public double GatherAccumMs { get; set; }
    public WorkerPassiveOrderType WorkerPassiveOrderType { get; private set; } = WorkerPassiveOrderType.None;
    public WorkerDefenseMode WorkerDefenseMode { get; set; } = WorkerDefenseMode.None;
    public Vector2? WorkerSavedMoveTarget { get; private set; }
    public SimResourceNode? WorkerSavedResource { get; private set; }
    public SimBuilding? WorkerSavedBuildTarget { get; private set; }
    public SimBuilding? WorkerSavedReturnHall { get; private set; }
    public ResourceType? WorkerSavedDesiredResourceType { get; private set; }
    public SimBuilding? WorkerAnchorHall { get; set; }
    public float WorkerSafeCombatRadius { get; set; }
    public float WorkerCombatLeashRadius { get; set; }
    public double WorkerThreatQuietMs { get; set; }
    public bool IsNonCombatScout { get; set; }

    public void SetPath(IEnumerable<Vector2> points)
    {
        Path.Clear();
        Path.AddRange(points);
        StuckAccumMs = 0d;
        PathProgressStallMs = 0d;
        LastPathProgressMetric = float.PositiveInfinity;
    }

    public void SetState(UnitState state)
    {
        if (!Alive)
        {
            State = UnitState.Dead;
            return;
        }

        State = state;
        if (state == UnitState.Idle)
        {
            Path.Clear();
            PathDestination = null;
            MoveInteractionAnchor = null;
            MoveArrivalRadius = 0f;
            AttackMoveTarget = null;
            StuckAccumMs = 0d;
            PathProgressStallMs = 0d;
            LastHeavyRerouteMs = -99999d;
            LastPathProgressMetric = float.PositiveInfinity;
        }
    }

    public void ClearOrders(bool clearWorkerPassiveOrder = true)
    {
        Path.Clear();
        State = Alive ? UnitState.Idle : UnitState.Dead;
        PathDestination = null;
        MoveInteractionAnchor = null;
        MoveArrivalRadius = 0f;
        AttackMoveTarget = null;
        PathRepathMs = 0d;
        StuckAccumMs = 0d;
        PathProgressStallMs = 0d;
        LastHeavyRerouteMs = -99999d;
        LastPathProgressMetric = float.PositiveInfinity;
        TargetCombat = null;
        TargetResource = null;
        TargetBuilding = null;
        ReturnBuilding = null;
        DesiredResourceType = null;
        GatherAccumMs = 0;
        WorkerDefenseMode = WorkerDefenseMode.None;
        WorkerAnchorHall = null;
        WorkerSafeCombatRadius = 0f;
        WorkerCombatLeashRadius = 0f;
        WorkerThreatQuietMs = 0d;
        if (clearWorkerPassiveOrder)
        {
            ClearWorkerPassiveOrder();
        }
    }

    public bool AdvanceAlongPath(double delta)
    {
        if (!Alive || Path.Count == 0)
        {
            return false;
        }

        var step = Speed * (float)delta;
        var next = Path[0];
        var toNext = next - Position;
        var distance = toNext.Length();
        if (distance <= step)
        {
            Position = next;
            Path.RemoveAt(0);
            return Path.Count > 0;
        }

        Position += toNext.Normalized() * step;
        return true;
    }

    public void TakeDamage(int amount)
    {
        if (!Alive || amount <= 0)
        {
            return;
        }

        Hp = int.Max(0, Hp - amount);
        if (Hp == 0)
        {
            Alive = false;
            ClearOrders();
            State = UnitState.Dead;
        }
    }

    public bool CanAttack()
    {
        return Alive && Attack > 0;
    }

    public bool IsWorker()
    {
        return Kind == UnitKind.Worker;
    }

    public bool IsRanged()
    {
        return Kind is UnitKind.Archer or UnitKind.Catapult;
    }

    public bool IsSiege()
    {
        return Kind == UnitKind.Catapult;
    }

    public void SaveWorkerMoveOrder(Vector2 target)
    {
        WorkerPassiveOrderType = WorkerPassiveOrderType.Move;
        WorkerSavedMoveTarget = target;
        WorkerSavedBuildTarget = null;
        WorkerSavedReturnHall = null;
        WorkerSavedResource = null;
        WorkerSavedDesiredResourceType = null;
    }

    public void SaveWorkerGatherOrder(SimResourceNode resource, ResourceType desiredType)
    {
        WorkerPassiveOrderType = WorkerPassiveOrderType.Gather;
        WorkerSavedMoveTarget = null;
        WorkerSavedBuildTarget = null;
        WorkerSavedReturnHall = null;
        WorkerSavedResource = resource;
        WorkerSavedDesiredResourceType = desiredType;
    }

    public void SaveWorkerBuildOrder(SimBuilding building)
    {
        WorkerPassiveOrderType = WorkerPassiveOrderType.Build;
        WorkerSavedMoveTarget = null;
        WorkerSavedBuildTarget = building;
        WorkerSavedReturnHall = null;
        WorkerSavedResource = null;
        WorkerSavedDesiredResourceType = null;
    }

    public void SaveWorkerReturnOrder(SimBuilding hall)
    {
        WorkerPassiveOrderType = WorkerPassiveOrderType.ReturnCargo;
        WorkerSavedMoveTarget = null;
        WorkerSavedBuildTarget = null;
        WorkerSavedReturnHall = hall;
    }

    public void ClearWorkerPassiveOrder()
    {
        WorkerPassiveOrderType = WorkerPassiveOrderType.None;
        WorkerSavedMoveTarget = null;
        WorkerSavedResource = null;
        WorkerSavedBuildTarget = null;
        WorkerSavedReturnHall = null;
        WorkerSavedDesiredResourceType = null;
    }
}
