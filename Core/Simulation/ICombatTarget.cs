using Godot;
using RtsNaGodote.Core.Data;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Core.Simulation;

public interface ICombatTarget
{
    int Id { get; }
    GameSide Side { get; }
    Vector2 Position { get; }
    float Radius { get; }
    bool Alive { get; }
    bool IsBuilding { get; }
    int Hp { get; }
    int MaxHp { get; }

    void TakeDamage(int amount);
}
