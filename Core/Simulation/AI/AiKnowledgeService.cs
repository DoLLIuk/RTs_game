using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.Buildings;
using RtsNaGodote.Core.Simulation.Units;
using GameSide = RtsNaGodote.Core.Data.Side;

namespace RtsNaGodote.Core.Simulation;

internal sealed class AiKnowledgeService
{
    private const double EnemyFreshMemoryMs = 28000d;

    private readonly AiKnowledgeContext _context;
    private readonly AiMemory _memory = new();

    public AiKnowledgeService(AiKnowledgeContext context)
    {
        _context = context;
    }

    public Vector2? LastKnownPlayerBase => _memory.LastKnownPlayerBase;
    public Vector2I? LastKnownPlayerBaseTile => _memory.LastKnownPlayerBaseTile;
    public double LastContactMs => _memory.LastContactMs;
    public int KnownBuildingCount => _memory.Buildings.Count;
    public IEnumerable<AiKnownUnit> KnownUnits => _memory.Units.Values;
    public IEnumerable<AiKnownBuilding> KnownBuildings => _memory.Buildings.Values;

    public void Update(List<SimUnit> aiUnits, List<SimBuilding> aiBuildings)
    {
        var visibleUnitIds = new HashSet<int>();
        var visibleBuildingIds = new HashSet<int>();
        var contact = false;

        foreach (var unit in _context.Units)
        {
            if (!unit.Alive || unit.Side != GameSide.Player || !CanAiSeePosition(aiUnits, aiBuildings, unit.Position, unit.Radius))
            {
                continue;
            }

            _memory.Units[unit.Id] = new AiKnownUnit(
                unit.Id,
                unit.Kind,
                unit.Position,
                unit.Score * (unit.Hp / (float)unit.MaxHp),
                _context.ElapsedMs);
            visibleUnitIds.Add(unit.Id);
            contact = true;
        }

        foreach (var building in _context.Buildings)
        {
            if (!building.Alive || building.Side != GameSide.Player || !CanAiSeePosition(aiUnits, aiBuildings, building.Center, building.Radius))
            {
                continue;
            }

            var centerTile = building.CenterTile();
            _memory.Buildings[building.Id] = new AiKnownBuilding(
                building.Id,
                building.Kind,
                building.Center,
                centerTile,
                building.MaxHp,
                _context.ElapsedMs);
            visibleBuildingIds.Add(building.Id);
            contact = true;

            if (building.Kind == BuildingKind.TownHall || !_memory.LastKnownPlayerBase.HasValue)
            {
                _memory.LastKnownPlayerBase = building.Center;
                _memory.LastKnownPlayerBaseTile = centerTile;
            }
        }

        if (contact)
        {
            _memory.LastContactMs = _context.ElapsedMs;
        }

        Cleanup(aiUnits, aiBuildings, visibleUnitIds, visibleBuildingIds);
    }

    public bool IsFreshEnemyMemory(double lastSeenMs)
    {
        return _context.ElapsedMs - lastSeenMs <= EnemyFreshMemoryMs;
    }

    public bool IsFreshEnemyMemory(double lastSeenMs, double maxAgeMs)
    {
        return _context.ElapsedMs - lastSeenMs <= maxAgeMs;
    }

    public float EstimateKnownEnemyPower()
    {
        var power = 0f;
        foreach (var unit in _memory.Units.Values)
        {
            if (IsFreshEnemyMemory(unit.LastSeenMs))
            {
                power += unit.Power;
            }
        }

        foreach (var building in _memory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs))
            {
                continue;
            }

            power += building.Kind == BuildingKind.Tower ? 2.6f : building.Kind == BuildingKind.TownHall ? 2.2f : 1.2f;
        }

        return power;
    }

    public bool KnownEnemyPressureNear(Vector2 point, float radius)
    {
        foreach (var unit in _memory.Units.Values)
        {
            if (IsFreshEnemyMemory(unit.LastSeenMs) && unit.Position.DistanceTo(point) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    public int CountKnownWorkersNear(Vector2 position, float radius)
    {
        var count = 0;
        foreach (var unit in _memory.Units.Values)
        {
            if (IsFreshEnemyMemory(unit.LastSeenMs) && unit.Kind == UnitKind.Worker && unit.Position.DistanceTo(position) <= radius)
            {
                count++;
            }
        }

        return count;
    }

    public int CountKnownCombatUnitsNear(Vector2 position, float radius)
    {
        var count = 0;
        foreach (var unit in _memory.Units.Values)
        {
            if (IsFreshEnemyMemory(unit.LastSeenMs) &&
                unit.Kind != UnitKind.Worker &&
                unit.Position.DistanceTo(position) <= radius)
            {
                count++;
            }
        }

        return count;
    }

    public bool HasKnownTowerNear(Vector2 position, float radius)
    {
        foreach (var building in _memory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs) ||
                building.Kind != BuildingKind.Tower)
            {
                continue;
            }

            if (building.Position.DistanceTo(position) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    public bool HasKnownOuterTargetNear(Vector2 position, float radius)
    {
        foreach (var building in _memory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs) || building.Kind == BuildingKind.TownHall)
            {
                continue;
            }

            if (building.Position.DistanceTo(position) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    public float EstimateKnownThreatAt(Vector2 position, float radius)
    {
        var threat = 0f;
        foreach (var unit in _memory.Units.Values)
        {
            if (IsFreshEnemyMemory(unit.LastSeenMs) && unit.Position.DistanceTo(position) <= radius)
            {
                threat += unit.Power;
            }
        }

        foreach (var building in _memory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs))
            {
                continue;
            }

            if (building.Kind == BuildingKind.Tower && building.Position.DistanceTo(position) <= radius + GameConstants.TileSize * 2f)
            {
                threat += 2.8f;
            }
        }

        return threat;
    }

    public int CountScoutKnownWorkersNear(Vector2 position, float radius, double scoutMemoryMaxAgeMs)
    {
        var count = 0;
        foreach (var unit in _memory.Units.Values)
        {
            if (IsFreshEnemyMemory(unit.LastSeenMs, scoutMemoryMaxAgeMs) &&
                unit.Kind == UnitKind.Worker &&
                unit.Position.DistanceTo(position) <= radius)
            {
                count++;
            }
        }

        return count;
    }

    public int CountScoutKnownCombatUnitsNear(Vector2 position, float radius, double scoutMemoryMaxAgeMs)
    {
        var count = 0;
        foreach (var unit in _memory.Units.Values)
        {
            if (IsFreshEnemyMemory(unit.LastSeenMs, scoutMemoryMaxAgeMs) &&
                unit.Kind != UnitKind.Worker &&
                unit.Position.DistanceTo(position) <= radius)
            {
                count++;
            }
        }

        return count;
    }

    public bool HasScoutKnownTowerNear(Vector2 position, float radius, double scoutMemoryMaxAgeMs)
    {
        foreach (var building in _memory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs, scoutMemoryMaxAgeMs) ||
                building.Kind != BuildingKind.Tower)
            {
                continue;
            }

            if (building.Position.DistanceTo(position) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    public bool HasScoutKnownOuterTargetNear(Vector2 position, float radius, double scoutMemoryMaxAgeMs)
    {
        foreach (var building in _memory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs, scoutMemoryMaxAgeMs) || building.Kind == BuildingKind.TownHall)
            {
                continue;
            }

            if (building.Position.DistanceTo(position) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    public float EstimateScoutKnownThreatAt(Vector2 position, float radius, double scoutMemoryMaxAgeMs)
    {
        var threat = 0f;
        foreach (var unit in _memory.Units.Values)
        {
            if (IsFreshEnemyMemory(unit.LastSeenMs, scoutMemoryMaxAgeMs) && unit.Position.DistanceTo(position) <= radius)
            {
                threat += unit.Power;
            }
        }

        foreach (var building in _memory.Buildings.Values)
        {
            if (!IsFreshEnemyMemory(building.LastSeenMs, scoutMemoryMaxAgeMs))
            {
                continue;
            }

            if (building.Kind == BuildingKind.Tower &&
                building.Position.DistanceTo(position) <= radius + GameConstants.TileSize * 2f)
            {
                threat += 2.8f;
            }
        }

        return threat;
    }

    private void Cleanup(
        List<SimUnit> aiUnits,
        List<SimBuilding> aiBuildings,
        HashSet<int> visibleUnitIds,
        HashSet<int> visibleBuildingIds)
    {
        var staleUnits = new List<int>();
        foreach (var pair in _memory.Units)
        {
            if (visibleUnitIds.Contains(pair.Key))
            {
                continue;
            }

            if (!_context.Units.Exists(unit => unit.Alive && unit.Id == pair.Key) &&
                CanAiSeePosition(aiUnits, aiBuildings, pair.Value.Position, GameConstants.TileSize * 0.4f))
            {
                staleUnits.Add(pair.Key);
            }
        }

        foreach (var id in staleUnits)
        {
            _memory.Units.Remove(id);
        }

        var staleBuildings = new List<int>();
        foreach (var pair in _memory.Buildings)
        {
            if (visibleBuildingIds.Contains(pair.Key))
            {
                continue;
            }

            if (!_context.Buildings.Exists(building => building.Alive && building.Id == pair.Key) &&
                CanAiSeePosition(aiUnits, aiBuildings, pair.Value.Position, GameConstants.TileSize))
            {
                staleBuildings.Add(pair.Key);
            }
        }

        foreach (var id in staleBuildings)
        {
            if (_memory.Buildings.TryGetValue(id, out var removed) && removed.Kind == BuildingKind.TownHall)
            {
                _memory.LastKnownPlayerBase = null;
                _memory.LastKnownPlayerBaseTile = null;
            }

            _memory.Buildings.Remove(id);
        }
    }

    private static bool CanAiSeePosition(List<SimUnit> aiUnits, List<SimBuilding> aiBuildings, Vector2 position, float padding)
    {
        foreach (var unit in aiUnits)
        {
            if (unit.Position.DistanceTo(position) <= unit.Sight * GameConstants.TileSize + padding)
            {
                return true;
            }
        }

        foreach (var building in aiBuildings)
        {
            if (building.Center.DistanceTo(position) <= building.Sight * GameConstants.TileSize + padding)
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class AiMemory
{
    public Dictionary<int, AiKnownUnit> Units { get; } = [];
    public Dictionary<int, AiKnownBuilding> Buildings { get; } = [];
    public Vector2? LastKnownPlayerBase { get; set; }
    public Vector2I? LastKnownPlayerBaseTile { get; set; }
    public double LastContactMs { get; set; } = -99999d;
}

internal sealed record AiKnownUnit(int Id, UnitKind Kind, Vector2 Position, float Power, double LastSeenMs);
internal sealed record AiKnownBuilding(int Id, BuildingKind Kind, Vector2 Position, Vector2I CenterTile, int MaxHp, double LastSeenMs);
