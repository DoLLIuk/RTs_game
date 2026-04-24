using System;
using System.Collections.Generic;
using Godot;
using RtsNaGodote.Core.Data;

namespace RtsNaGodote.Core.Simulation.World;

public static class MapGenerator
{
    public static MapLayout Generate(int seed = GameConstants.DefaultSeed)
    {
        var rng = new Mulberry32(seed);
        var map = new TileMap();
        var playerBase = new Vector2I(9, 10);
        var aiBase = Mirror(playerBase);
        var center = new Vector2I(GameConstants.MapWidth / 2, GameConstants.MapHeight / 2);

        for (var y = 0; y < GameConstants.MapHeight; y++)
        {
            for (var x = 0; x < GameConstants.MapWidth; x++)
            {
                map.Set(x, y, rng.NextFloat() < 0.3f ? TileType.Grass2 : TileType.Grass);
            }
        }

        AddDecorativeWater(rng, map);
        ScatterStone(rng, 7, map);
        ClusterForestMirrored(rng, 12, 4, map);

        CarveLane(map, playerBase, center, 4);
        CarveLane(map, center, aiBase, 4);
        CarveLane(map, new Vector2I(10, GameConstants.MapHeight - 13), new Vector2I(GameConstants.MapWidth - 12, 12), 3);
        ClearArea(map, playerBase.X, playerBase.Y, 8);
        ClearArea(map, aiBase.X, aiBase.Y, 8);
        ClearArea(map, center.X, center.Y, 6);

        var goldMines = MirroredPoints(
        [
            new Vector2I(playerBase.X + 7, playerBase.Y + 1),
            new Vector2I(playerBase.X + 1, playerBase.Y + 9),
            new Vector2I(center.X - 4, center.Y - 5)
        ]);
        goldMines.Add(new Vector2I(center.X + 2, center.Y + 3));
        foreach (var mine in goldMines)
        {
            ClearArea(map, mine.X, mine.Y, 4);
        }

        if (!MapValidation.HasWalkablePath(map, playerBase, aiBase))
        {
            CarveLane(map, playerBase, center, 5);
            CarveLane(map, center, aiBase, 5);
        }

        var trees = MaterializeTrees(map, playerBase, aiBase, goldMines);
        return new MapLayout
        {
            Map = map,
            PlayerBase = playerBase,
            AIBase = aiBase,
            GoldMines = goldMines,
            Trees = trees
        };
    }

    private static void AddDecorativeWater(Mulberry32 rng, TileMap map)
    {
        var first = new Pond(18, 45, 3 + rng.NextInt(0, 2));
        var second = new Pond(30, 14, 2);
        var ponds = new[] { first, Mirror(first), second, Mirror(second) };
        foreach (var pond in ponds)
        {
            for (var dy = -pond.Radius; dy <= pond.Radius; dy++)
            {
                for (var dx = -pond.Radius; dx <= pond.Radius; dx++)
                {
                    var x = pond.Tx + dx;
                    var y = pond.Ty + dy;
                    if (!map.InBounds(x, y))
                    {
                        continue;
                    }

                    if (MathF.Sqrt(dx * dx + dy * dy) <= pond.Radius && rng.NextFloat() < 0.8f)
                    {
                        map.Set(x, y, TileType.Water);
                    }
                }
            }
        }
    }

    private static void ClusterForestMirrored(Mulberry32 rng, int count, int radius, TileMap map)
    {
        for (var i = 0; i < count; i++)
        {
            var cx = 5 + rng.NextInt(0, GameConstants.MapWidth / 2 - 10);
            var cy = 5 + rng.NextInt(0, GameConstants.MapHeight - 10);
            PaintForestCluster(rng, map, cx, cy, radius);
            var mirrored = Mirror(new Vector2I(cx, cy));
            PaintForestCluster(rng, map, mirrored.X, mirrored.Y, radius);
        }
    }

    private static void PaintForestCluster(Mulberry32 rng, TileMap map, int cx, int cy, int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var x = cx + dx;
                var y = cy + dy;
                if (!map.InBounds(x, y))
                {
                    continue;
                }

                var distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance <= radius && rng.NextFloat() < 0.9f - distance / (radius + 1))
                {
                    map.Set(x, y, TileType.Forest);
                }
            }
        }
    }

    private static void ScatterStone(Mulberry32 rng, int count, TileMap map)
    {
        for (var i = 0; i < count; i++)
        {
            var cx = 4 + rng.NextInt(0, GameConstants.MapWidth / 2 - 8);
            var cy = 4 + rng.NextInt(0, GameConstants.MapHeight - 8);
            PaintStone(rng, map, cx, cy);
            var mirrored = Mirror(new Vector2I(cx, cy));
            PaintStone(rng, map, mirrored.X, mirrored.Y);
        }
    }

    private static void PaintStone(Mulberry32 rng, TileMap map, int cx, int cy)
    {
        var radius = 1 + rng.NextInt(0, 2);
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var x = cx + dx;
                var y = cy + dy;
                if (!map.InBounds(x, y))
                {
                    continue;
                }

                if (MathF.Sqrt(dx * dx + dy * dy) <= radius && rng.NextFloat() < 0.65f)
                {
                    map.Set(x, y, TileType.Stone);
                }
            }
        }
    }

    private static void CarveLane(TileMap map, Vector2I from, Vector2I to, int radius)
    {
        var steps = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
        for (var i = 0; i <= steps; i++)
        {
            var t = steps == 0 ? 0 : i / (float)steps;
            var x = Mathf.RoundToInt(Mathf.Lerp(from.X, to.X, t));
            var y = Mathf.RoundToInt(Mathf.Lerp(from.Y, to.Y, t));
            ClearArea(map, x, y, radius, TileType.Dirt);
        }
    }

    private static void ClearArea(TileMap map, int cx, int cy, int radius, TileType tile = TileType.Grass)
    {
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var x = cx + dx;
                var y = cy + dy;
                if (!map.InBounds(x, y))
                {
                    continue;
                }

                if (MathF.Sqrt(dx * dx + dy * dy) <= radius)
                {
                    var actual = tile == TileType.Grass && (dx + dy) % 2 != 0 ? TileType.Grass2 : tile;
                    map.Set(x, y, actual);
                    map.SetWalkable(x, y, true);
                }
            }
        }
    }

    private static List<Vector2I> MaterializeTrees(TileMap map, Vector2I playerBase, Vector2I aiBase, List<Vector2I> goldMines)
    {
        var trees = new List<Vector2I>();
        for (var y = 0; y < GameConstants.MapHeight; y++)
        {
            for (var x = 0; x < GameConstants.MapWidth; x++)
            {
                if (map.Get(x, y) != TileType.Forest)
                {
                    continue;
                }

                if (!IsFar(x, y, playerBase, 8) || !IsFar(x, y, aiBase, 8) || goldMines.Exists(mine => !IsFar(x, y, mine, 5)))
                {
                    map.Set(x, y, TileType.Grass);
                    map.SetWalkable(x, y, true);
                    continue;
                }

                trees.Add(new Vector2I(x, y));
                map.SetWalkable(x, y, false);
            }
        }

        return trees;
    }

    private static List<Vector2I> MirroredPoints(IEnumerable<Vector2I> points)
    {
        var result = new List<Vector2I>();
        foreach (var point in points)
        {
            result.Add(point);
            result.Add(Mirror(point));
        }

        return result;
    }

    private static Vector2I Mirror(Vector2I point, int offset = 0)
    {
        return new Vector2I(GameConstants.MapWidth - point.X - 1 + offset, GameConstants.MapHeight - point.Y - 1 + offset);
    }

    private static Pond Mirror(Pond pond)
    {
        var mirrored = Mirror(new Vector2I(pond.Tx, pond.Ty));
        return new Pond(mirrored.X, mirrored.Y, pond.Radius);
    }

    private static bool IsFar(int x, int y, Vector2I point, int radius)
    {
        return Mathf.Sqrt((x - point.X) * (x - point.X) + (y - point.Y) * (y - point.Y)) > radius;
    }

    private readonly record struct Pond(int Tx, int Ty, int Radius);

    private sealed class Mulberry32
    {
        private uint _state;

        public Mulberry32(int seed)
        {
            _state = unchecked((uint)seed);
        }

        public float NextFloat()
        {
            _state += 0x6d2b79f5;
            var t = _state;
            t = (uint)((int)(t ^ (t >> 15)) * (int)(t | 1));
            t ^= t + (uint)((int)(t ^ (t >> 7)) * (int)(t | 61));
            return ((t ^ (t >> 14)) & 0xffffffff) / 4294967296f;
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            return minInclusive + (int)MathF.Floor(NextFloat() * (maxExclusive - minInclusive));
        }
    }
}
