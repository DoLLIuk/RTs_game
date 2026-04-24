using System;
using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;
using RtsNaGodote.Core.Simulation.World;
using WorldTileMap = RtsNaGodote.Core.Simulation.World.TileMap;

namespace RtsNaGodote.Core.Simulation.Pathfinding;

public static class Pathfinder
{
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

        var goalKeys = new HashSet<int>();
        foreach (var actualGoal in actualGoals)
        {
            goalKeys.Add(Key(actualGoal.X, actualGoal.Y));
        }

        if (allowStartAsGoal && goalKeys.Contains(Key(start.X, start.Y)))
        {
            return [];
        }

        var open = new Dictionary<int, Node>();
        var closed = new bool[GameConstants.MapWidth * GameConstants.MapHeight];
        open[Key(start.X, start.Y)] = new Node(start.X, start.Y, 0f, Heuristic(start, actualGoals), null);

        var iterations = 0;
        while (open.Count > 0)
        {
            if (++iterations > 4000)
            {
                break;
            }

            var current = FindBest(open);
            open.Remove(Key(current.Tx, current.Ty));
            closed[Key(current.Tx, current.Ty)] = true;

            if (goalKeys.Contains(Key(current.Tx, current.Ty)))
            {
                return Reconstruct(current);
            }

            for (var offset = 0; offset < Directions.Length; offset++)
            {
                var direction = Directions[(offset + tieBreakerSeed) % Directions.Length];
                var nx = current.Tx + direction.Dx;
                var ny = current.Ty + direction.Dy;
                if (!map.InBounds(nx, ny) || closed[Key(nx, ny)] || !map.IsWalkable(nx, ny))
                {
                    continue;
                }

                if (direction.Dx != 0 && direction.Dy != 0 &&
                    (!map.IsWalkable(current.Tx + direction.Dx, current.Ty) || !map.IsWalkable(current.Tx, current.Ty + direction.Dy)))
                {
                    continue;
                }

                var g = current.G + direction.Cost + GetTilePenalty(tilePenalty, nx, ny);
                var key = Key(nx, ny);
                if (open.TryGetValue(key, out var existing) && existing.G <= g)
                {
                    continue;
                }

                open[key] = new Node(nx, ny, g, g + Heuristic(new Vector2I(nx, ny), actualGoals), current);
            }
        }

        return [];
    }

    private static Node FindBest(Dictionary<int, Node> open)
    {
        Node? best = null;
        foreach (var candidate in open.Values)
        {
            if (best is null || candidate.F < best.F)
            {
                best = candidate;
            }
        }

        return best ?? throw new InvalidOperationException("Open set was empty.");
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
                    var score = goal.DistanceSquaredTo(next) + penalty + TieBreaker(next, tieBreakerSeed);
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
        var limit = int.Min(6, candidates.Count);
        var result = new List<Vector2I>(limit);
        for (var i = 0; i < limit; i++)
        {
            result.Add(candidates[i].Tile);
        }

        return result;
    }

    private static List<Vector2I> Reconstruct(Node end)
    {
        var result = new List<Vector2I>();
        for (var node = end; node.Parent is not null; node = node.Parent)
        {
            result.Insert(0, new Vector2I(node.Tx, node.Ty));
        }

        return result;
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

    private sealed record Node(int Tx, int Ty, float G, float F, Node? Parent);
}
