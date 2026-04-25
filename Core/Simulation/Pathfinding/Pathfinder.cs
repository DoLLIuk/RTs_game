using System.Buffers;
using System;
using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.World;
using WorldTileMap = RtsNaGodote.Core.Simulation.World.TileMap;

namespace RtsNaGodote.Core.Simulation.Pathfinding;

public static class Pathfinder
{
    private const float PenaltyWeight = 0.45f;
    private static readonly (int Dx, int Dy, float Cost)[] Directions =
    [
        (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
        (1, 1, Mathf.Sqrt(2f)), (1, -1, Mathf.Sqrt(2f)), (-1, 1, Mathf.Sqrt(2f)), (-1, -1, Mathf.Sqrt(2f))
    ];

    public static List<Vector2I> FindPath(
        WorldTileMap map,
        Vector2I start,
        Vector2I goal,
        int goalRadiusTiles = 0,
        int tieBreakerSeed = 0,
        IReadOnlyDictionary<int, float>? tilePenalty = null,
        bool allowStartAsGoal = true)
    {
        if (!map.InBounds(goal.X, goal.Y))
        {
            return [];
        }

        var actualGoals = FindGoalCandidates(map, goal, goalRadiusTiles, tieBreakerSeed, tilePenalty);
        if (actualGoals.Count == 0)
        {
            return [];
        }

        if (!allowStartAsGoal)
        {
            actualGoals.RemoveAll(candidate => candidate == start);
            if (actualGoals.Count == 0)
            {
                return [];
            }
        }

        var goalKeys = new List<int>(actualGoals.Count);
        foreach (var actualGoal in actualGoals)
        {
            goalKeys.Add(Key(actualGoal.X, actualGoal.Y));
        }

        if (allowStartAsGoal && ContainsGoalKey(goalKeys, Key(start.X, start.Y)))
        {
            return [];
        }

        var mapCellCount = GameConstants.MapWidth * GameConstants.MapHeight;
        var startKey = Key(start.X, start.Y);
        var parent = ArrayPool<int>.Shared.Rent(mapCellCount);
        var gScore = ArrayPool<float>.Shared.Rent(mapCellCount);
        var closed = ArrayPool<bool>.Shared.Rent(mapCellCount);
        Array.Fill(parent, -1, 0, mapCellCount);
        Array.Fill(gScore, float.PositiveInfinity, 0, mapCellCount);
        Array.Clear(closed, 0, mapCellCount);

        try
        {
            var open = new PriorityQueue<int, float>();
            gScore[startKey] = 0f;
            open.Enqueue(startKey, Heuristic(start, actualGoals));

            var iterations = 0;
            while (open.Count > 0)
            {
                if (++iterations > 4000)
                {
                    break;
                }

                var currentKey = open.Dequeue();
                if (closed[currentKey])
                {
                    continue;
                }

                closed[currentKey] = true;
                var current = FromKey(currentKey);

                if (ContainsGoalKey(goalKeys, currentKey))
                {
                    return Reconstruct(parent, currentKey);
                }

                for (var offset = 0; offset < Directions.Length; offset++)
                {
                    var direction = Directions[(offset + tieBreakerSeed) % Directions.Length];
                    var nx = current.X + direction.Dx;
                    var ny = current.Y + direction.Dy;
                    var nextKey = Key(nx, ny);
                    if (!map.InBounds(nx, ny) || closed[nextKey] || !map.IsWalkable(nx, ny))
                    {
                        continue;
                    }

                    if (direction.Dx != 0 && direction.Dy != 0 &&
                        (!map.IsWalkable(current.X + direction.Dx, current.Y) || !map.IsWalkable(current.X, current.Y + direction.Dy)))
                    {
                        continue;
                    }

                    var g = gScore[currentKey] + direction.Cost + GetTilePenalty(tilePenalty, nx, ny) * PenaltyWeight;
                    if (g >= gScore[nextKey])
                    {
                        continue;
                    }

                    parent[nextKey] = currentKey;
                    gScore[nextKey] = g;
                    var f = g + Heuristic(new Vector2I(nx, ny), actualGoals) + TieBreaker(new Vector2I(nx, ny), tieBreakerSeed) * 0.001f;
                    open.Enqueue(nextKey, f);
                }
            }

            return [];
        }
        finally
        {
            ArrayPool<int>.Shared.Return(parent, clearArray: false);
            ArrayPool<float>.Shared.Return(gScore, clearArray: false);
            ArrayPool<bool>.Shared.Return(closed, clearArray: true);
        }
    }

    private static List<Vector2I> FindGoalCandidates(
        WorldTileMap map,
        Vector2I goal,
        int goalRadiusTiles,
        int tieBreakerSeed,
        IReadOnlyDictionary<int, float>? tilePenalty)
    {
        var candidates = new List<(Vector2I Tile, float Score)>();
        var radiusLimit = int.Max(0, goalRadiusTiles);
        for (var radius = 0; radius <= radiusLimit; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (radius > 0 && Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    var next = new Vector2I(goal.X + dx, goal.Y + dy);
                    if (!map.IsWalkable(next.X, next.Y))
                    {
                        continue;
                    }

                    var penalty = GetTilePenalty(tilePenalty, next.X, next.Y);
                    var score = goal.DistanceSquaredTo(next) + penalty * PenaltyWeight + TieBreaker(next, tieBreakerSeed);
                    candidates.Add((next, score));
                }
            }
        }

        if (candidates.Count == 0)
        {
            var nearest = FindNearestWalkable(map, goal);
            if (nearest is null)
            {
                return [];
            }

            candidates.Add((nearest.Value, GetTilePenalty(tilePenalty, nearest.Value.X, nearest.Value.Y)));
        }

        candidates.Sort(static (left, right) => left.Score.CompareTo(right.Score));
        var limit = int.Min(10, candidates.Count);
        var result = new List<Vector2I>(limit);
        for (var i = 0; i < limit; i++)
        {
            result.Add(candidates[i].Tile);
        }

        return result;
    }

    private static List<Vector2I> Reconstruct(int[] parent, int endKey)
    {
        var result = new List<Vector2I>();
        for (var current = endKey; parent[current] >= 0; current = parent[current])
        {
            result.Add(FromKey(current));
        }

        result.Reverse();
        return result;
    }

    private static bool ContainsGoalKey(List<int> goalKeys, int key)
    {
        for (var index = 0; index < goalKeys.Count; index++)
        {
            if (goalKeys[index] == key)
            {
                return true;
            }
        }

        return false;
    }

    private static Vector2I? FindNearestWalkable(WorldTileMap map, Vector2I goal)
    {
        if (map.IsWalkable(goal.X, goal.Y))
        {
            return goal;
        }

        for (var radius = 1; radius < 12; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    var next = new Vector2I(goal.X + dx, goal.Y + dy);
                    if (map.IsWalkable(next.X, next.Y))
                    {
                        return next;
                    }
                }
            }
        }

        return null;
    }

    private static float Heuristic(Vector2I start, List<Vector2I> goals)
    {
        var best = float.PositiveInfinity;
        foreach (var goal in goals)
        {
            var candidate = DiagonalDistance(start, goal);
            if (candidate < best)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static float DiagonalDistance(Vector2I a, Vector2I b)
    {
        var dx = Mathf.Abs(a.X - b.X);
        var dy = Mathf.Abs(a.Y - b.Y);
        return (dx + dy) + (Mathf.Sqrt(2f) - 2f) * Mathf.Min(dx, dy);
    }

    private static float GetTilePenalty(IReadOnlyDictionary<int, float>? tilePenalty, int tx, int ty)
    {
        return tilePenalty is not null && tilePenalty.TryGetValue(Key(tx, ty), out var penalty) ? penalty : 0f;
    }

    private static float TieBreaker(Vector2I tile, int tieBreakerSeed)
    {
        var value = tile.X * 73856093 ^ tile.Y * 19349663 ^ (tieBreakerSeed + 1) * 83492791;
        value &= 0x7fffffff;
        return (value % 1000) / 1000f;
    }

    private static int Key(int tx, int ty) => ty * GameConstants.MapWidth + tx;

    private static Vector2I FromKey(int key)
    {
        var ty = key / GameConstants.MapWidth;
        var tx = key - ty * GameConstants.MapWidth;
        return new Vector2I(tx, ty);
    }
}
