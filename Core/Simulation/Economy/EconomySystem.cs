using System.Collections.Generic;
using RtsNaGodote.Core.Data;

namespace RtsNaGodote.Core.Simulation.Economy;

public sealed class EconomySystem
{
    private readonly Dictionary<Side, PlayerState> _players = [];

    public void Register(Side side, Race race, int gold, int lumber, int foodCap)
    {
        _players[side] = new PlayerState
        {
            Side = side,
            Race = race,
            Gold = gold,
            Lumber = lumber,
            FoodCap = foodCap,
            Food = 0
        };
    }

    public PlayerState Get(Side side)
    {
        return _players[side];
    }

    public bool CanAfford(Side side, int gold, int lumber)
    {
        var player = _players[side];
        return player.Gold >= gold && player.Lumber >= lumber;
    }

    public bool Spend(Side side, int gold, int lumber)
    {
        var player = _players[side];
        if (player.Gold < gold || player.Lumber < lumber)
        {
            return false;
        }

        player.Gold -= gold;
        player.Lumber -= lumber;
        return true;
    }

    public void Deposit(Side side, ResourceType type, int amount)
    {
        var player = _players[side];
        if (type == ResourceType.Gold)
        {
            player.Gold += amount;
        }
        else
        {
            player.Lumber += amount;
        }
    }

    public void AddCap(Side side, int amount)
    {
        _players[side].FoodCap = int.Clamp(_players[side].FoodCap + amount, 0, 100);
    }

    public void RemoveCap(Side side, int amount)
    {
        _players[side].FoodCap = int.Max(0, _players[side].FoodCap - amount);
    }

    public void AddFood(Side side, int amount)
    {
        _players[side].Food += amount;
    }

    public void RemoveFood(Side side, int amount)
    {
        _players[side].Food = int.Max(0, _players[side].Food - amount);
    }

    public bool HasFoodRoom(Side side, int amount)
    {
        var player = _players[side];
        return player.Food + amount <= player.FoodCap;
    }
}
