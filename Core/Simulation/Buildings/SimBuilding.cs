using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Core.Simulation.Buildings;

public sealed record ProductionItem(UnitKind Kind, double TotalMs)
{
    public double RemainingMs { get; set; } = TotalMs;
}

public sealed class SimBuilding : ICombatTarget
{
    public SimBuilding(int id, BuildingKind kind, GameSide side, Race race, Vector2I tilePosition, bool completed)
    {
        var definition = GameDefinitions.Buildings[kind];
        Id = id;
        Kind = kind;
        Side = side;
        Race = race;
        TilePosition = tilePosition;
        Completed = completed;
        SizeTiles = definition.Size;
        MaxHp = definition.Hp;
        Hp = completed ? definition.Hp : int.Max(1, (int)(definition.Hp * 0.1f));
        BuildTimeMs = definition.BuildTimeMs;
        BuildProgressMs = completed ? definition.BuildTimeMs : 0d;
        Attack = definition.Attack;
        Range = definition.Range;
        CooldownMs = definition.CooldownMs;
        Sight = definition.Sight;
        Center = new Vector2(
            tilePosition.X * GameConstants.TileSize + (definition.Size * GameConstants.TileSize) / 2.0f,
            tilePosition.Y * GameConstants.TileSize + (definition.Size * GameConstants.TileSize) / 2.0f);
        Radius = (definition.Size * GameConstants.TileSize) / 2.0f;
    }

    public int Id { get; }
    public BuildingKind Kind { get; }
    public GameSide Side { get; }
    public Race Race { get; }
    public Vector2I TilePosition { get; }
    public int SizeTiles { get; }
    public Vector2 Center { get; }
    public Vector2 Position => Center;
    public float Radius { get; }
    public bool Completed { get; private set; }
    public bool Alive { get; private set; } = true;
    public bool IsBuilding => true;
    public int Hp { get; private set; }
    public int MaxHp { get; }
    public int Sight { get; }
    public double BuildProgressMs { get; private set; }
    public int BuildTimeMs { get; }
    public int Attack { get; }
    public float Range { get; }
    public int CooldownMs { get; }
    public double LastAttackMs { get; set; }
    public Vector2? RallyPoint { get; set; }
    public List<ProductionItem> Queue { get; } = [];

    public Vector2I CenterTile()
    {
        return new Vector2I(TilePosition.X + SizeTiles / 2, TilePosition.Y + SizeTiles / 2);
    }

    public bool AddBuildProgress(double deltaMs)
    {
        if (!Alive || Completed)
        {
            return false;
        }

        BuildProgressMs = double.Min(BuildTimeMs, BuildProgressMs + deltaMs);
        var ratio = Mathf.Clamp((float)(BuildProgressMs / BuildTimeMs), 0f, 1f);
        Hp = int.Max(1, Mathf.RoundToInt(MaxHp * (0.1f + 0.9f * ratio)));
        if (BuildProgressMs < BuildTimeMs)
        {
            return false;
        }

        Completed = true;
        Hp = MaxHp;
        return true;
    }

    public UnitKind? TickProduction(double deltaMs)
    {
        if (!Alive || !Completed || Queue.Count == 0)
        {
            return null;
        }

        var item = Queue[0];
        item.RemainingMs -= deltaMs;
        if (item.RemainingMs > 0d)
        {
            return null;
        }

        Queue.RemoveAt(0);
        return item.Kind;
    }

    public void Enqueue(UnitKind kind)
    {
        Queue.Add(new ProductionItem(kind, GameDefinitions.Units[kind].BuildTimeMs));
    }

    public bool CanAttack()
    {
        return Alive && Completed && Attack > 0 && Range > 0f;
    }

    public float ProgressFraction()
    {
        if (!Completed)
        {
            return (float)(BuildProgressMs / BuildTimeMs);
        }

        if (Queue.Count == 0)
        {
            return 0f;
        }

        var current = Queue[0];
        return 1f - Mathf.Clamp((float)(current.RemainingMs / current.TotalMs), 0f, 1f);
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
            Queue.Clear();
            RallyPoint = null;
        }
    }
}
