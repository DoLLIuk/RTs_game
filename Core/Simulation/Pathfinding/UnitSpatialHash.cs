using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Simulation.Units;

namespace RtsNaGodote.Core.Simulation.Pathfinding;

public sealed class UnitSpatialHash
{
    private readonly float _cellSize;
    private readonly Dictionary<long, List<SimUnit>> _cells = [];
    private readonly Dictionary<int, long> _unitCellKeys = [];

    public UnitSpatialHash(float cellSize)
    {
        _cellSize = cellSize;
    }

    public void Rebuild(IReadOnlyList<SimUnit> units)
    {
        _cells.Clear();
        _unitCellKeys.Clear();
        foreach (var unit in units)
        {
            if (!unit.Alive)
            {
                continue;
            }

            Add(unit, unit.Position);
        }
    }

    public void UpdateUnit(SimUnit unit, Vector2 oldPosition, Vector2 newPosition)
    {
        var oldKey = ToKey(oldPosition);
        var newKey = ToKey(newPosition);
        if (oldKey == newKey)
        {
            _unitCellKeys[unit.Id] = newKey;
            return;
        }

        Remove(unit, oldKey);
        Add(unit, newPosition);
    }

    public void Query(Vector2 point, float radius, List<SimUnit> results)
    {
        results.Clear();
        var minCellX = ToCell(point.X - radius);
        var maxCellX = ToCell(point.X + radius);
        var minCellY = ToCell(point.Y - radius);
        var maxCellY = ToCell(point.Y + radius);
        for (var cellY = minCellY; cellY <= maxCellY; cellY++)
        {
            for (var cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                if (!_cells.TryGetValue(ToKey(cellX, cellY), out var bucket))
                {
                    continue;
                }

                results.AddRange(bucket);
            }
        }
    }

    private void Add(SimUnit unit, Vector2 position)
    {
        var key = ToKey(position);
        if (!_cells.TryGetValue(key, out var bucket))
        {
            bucket = [];
            _cells[key] = bucket;
        }

        bucket.Add(unit);
        _unitCellKeys[unit.Id] = key;
    }

    private void Remove(SimUnit unit, long key)
    {
        if (!_cells.TryGetValue(key, out var bucket))
        {
            return;
        }

        bucket.Remove(unit);
        if (bucket.Count == 0)
        {
            _cells.Remove(key);
        }
    }

    private long ToKey(Vector2 position)
    {
        return ToKey(ToCell(position.X), ToCell(position.Y));
    }

    private long ToKey(int cellX, int cellY)
    {
        return ((long)cellX << 32) ^ (uint)cellY;
    }

    private int ToCell(float coordinate)
    {
        return Mathf.FloorToInt(coordinate / _cellSize);
    }
}
